using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Workflow.Engine.Runtime;

namespace Workflow.Api.Runs;

/// <summary>
/// Что: in-memory metrics pack для запусков workflow.
/// Зачем: сравнивать workflow-профили по времени, outcome, routing decisions и стоимости/токенам, если adapter их отдает.
/// Как: считает counters/histograms, хранит bounded recent run samples и агрегирует node metrics по stage/model/node type.
/// </summary>
public sealed class WorkflowRunMetrics
{
    private const int MaxRecentRuns = 50;

    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> InputTokenFields = CreateFieldSet("inputTokens", "promptTokens", "input_tokens", "prompt_tokens");
    private static readonly HashSet<string> OutputTokenFields = CreateFieldSet("outputTokens", "completionTokens", "output_tokens", "completion_tokens");
    private static readonly HashSet<string> TotalTokenFields = CreateFieldSet("totalTokens", "total_tokens");
    private static readonly HashSet<string> CostFields = CreateFieldSet("costUsd", "estimatedCostUsd", "cost_usd", "estimated_cost_usd");

    private readonly Meter _meter = new("Workflow.Api.Runs", "0.1.0");
    private readonly Counter<long> _runsStartedCounter;
    private readonly Counter<long> _runsCompletedCounter;
    private readonly Counter<long> _runsSucceededCounter;
    private readonly Counter<long> _runsFailedCounter;
    private readonly Counter<long> _runsDeduplicatedCounter;
    private readonly Counter<long> _nodeStatusUpdatesCounter;
    private readonly Counter<long> _nodesCompletedCounter;
    private readonly Counter<long> _tokensObservedCounter;
    private readonly Counter<double> _costObservedCounter;
    private readonly Histogram<double> _runDurationMsHistogram;
    private readonly Histogram<double> _nodeDurationMsHistogram;

    private readonly object _syncRoot = new();
    private readonly Queue<WorkflowRunMetricsRunSample> _recentRuns = new();
    private readonly Dictionary<string, WorkflowRunMetricsAggregateBucket> _nodeTypeBuckets = new(KeyComparer);
    private readonly Dictionary<string, WorkflowRunMetricsAggregateBucket> _stageBuckets = new(KeyComparer);
    private readonly Dictionary<string, WorkflowRunMetricsAggregateBucket> _modelRouteBuckets = new(KeyComparer);
    private readonly Dictionary<string, WorkflowRunMetricsAggregateBucket> _routeReasonBuckets = new(KeyComparer);

    private long _totalRunsStarted;
    private long _activeRuns;
    private long _totalRunsCompleted;
    private long _totalRunsSucceeded;
    private long _totalRunsFailed;
    private long _totalRunsDeduplicated;
    private long _totalNodeStatusUpdates;
    private long _totalCompletedRunDurationMs;
    private long _totalCompletedNodes;
    private long _totalSucceededNodes;
    private long _totalFailedNodes;
    private long _totalSkippedNodes;
    private long _totalAgentNodes;
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private long _totalTokens;
    private double _totalCostUsd;

    public WorkflowRunMetrics()
    {
        _runsStartedCounter = _meter.CreateCounter<long>("workflow_runs_started_total");
        _runsCompletedCounter = _meter.CreateCounter<long>("workflow_runs_completed_total");
        _runsSucceededCounter = _meter.CreateCounter<long>("workflow_runs_succeeded_total");
        _runsFailedCounter = _meter.CreateCounter<long>("workflow_runs_failed_total");
        _runsDeduplicatedCounter = _meter.CreateCounter<long>("workflow_runs_deduplicated_total");
        _nodeStatusUpdatesCounter = _meter.CreateCounter<long>("workflow_node_status_updates_total");
        _nodesCompletedCounter = _meter.CreateCounter<long>("workflow_nodes_completed_total");
        _tokensObservedCounter = _meter.CreateCounter<long>("workflow_tokens_observed_total");
        _costObservedCounter = _meter.CreateCounter<double>("workflow_cost_observed_usd_total");
        _runDurationMsHistogram = _meter.CreateHistogram<double>("workflow_run_duration_ms");
        _nodeDurationMsHistogram = _meter.CreateHistogram<double>("workflow_node_duration_ms");
    }

    public void OnRunStarted(WorkflowRunTriggerType triggerType)
    {
        Interlocked.Increment(ref _totalRunsStarted);
        Interlocked.Increment(ref _activeRuns);
        _runsStartedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));
    }

    public void OnRunDeduplicated(WorkflowRunTriggerType triggerType)
    {
        Interlocked.Increment(ref _totalRunsDeduplicated);
        _runsDeduplicatedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));
    }

    public void OnNodeStatusChanged(WorkflowNodeRunStatus status)
    {
        Interlocked.Increment(ref _totalNodeStatusUpdates);
        _nodeStatusUpdatesCounter.Add(1, new KeyValuePair<string, object?>("node_status", status.ToString()));
    }

    public void OnRunCompleted(WorkflowRunMetricsRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var durationMs = CalculateDurationMs(record.StartedAtUtc ?? record.CreatedAtUtc, record.CompletedAtUtc);
        var nodeSamples = record.NodeResults
            .Select(CreateNodeSample)
            .ToArray();
        var runUsage = SumUsage(nodeSamples);
        var runSample = CreateRunSample(record, durationMs, nodeSamples, runUsage);

        Interlocked.Decrement(ref _activeRuns);
        Interlocked.Increment(ref _totalRunsCompleted);
        Interlocked.Add(ref _totalCompletedRunDurationMs, durationMs);
        Interlocked.Add(ref _totalCompletedNodes, nodeSamples.LongLength);
        Interlocked.Add(ref _totalSucceededNodes, nodeSamples.LongCount(node => node.Status == WorkflowNodeRunStatus.Succeeded));
        Interlocked.Add(ref _totalFailedNodes, nodeSamples.LongCount(node => node.Status == WorkflowNodeRunStatus.Failed));
        Interlocked.Add(ref _totalSkippedNodes, nodeSamples.LongCount(node => node.Status == WorkflowNodeRunStatus.Skipped));
        Interlocked.Add(ref _totalAgentNodes, nodeSamples.LongCount(IsAgentNode));
        Interlocked.Add(ref _totalInputTokens, runUsage.InputTokens);
        Interlocked.Add(ref _totalOutputTokens, runUsage.OutputTokens);
        Interlocked.Add(ref _totalTokens, runUsage.TotalTokens);

        _runsCompletedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", record.TriggerType.ToString()));
        _runDurationMsHistogram.Record(
            durationMs,
            new KeyValuePair<string, object?>("status", record.Status.ToString()),
            new KeyValuePair<string, object?>("trigger_type", record.TriggerType.ToString()));

        if (record.Status == WorkflowRunStatus.Succeeded)
        {
            Interlocked.Increment(ref _totalRunsSucceeded);
            _runsSucceededCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", record.TriggerType.ToString()));
        }
        else if (record.Status == WorkflowRunStatus.Failed)
        {
            Interlocked.Increment(ref _totalRunsFailed);
            _runsFailedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", record.TriggerType.ToString()));
        }

        foreach (var node in nodeSamples)
        {
            _nodesCompletedCounter.Add(
                1,
                new KeyValuePair<string, object?>("node_type", node.NodeType),
                new KeyValuePair<string, object?>("status", node.Status.ToString()));
            _nodeDurationMsHistogram.Record(
                node.DurationMs,
                new KeyValuePair<string, object?>("node_type", node.NodeType),
                new KeyValuePair<string, object?>("stage", node.RoutingStage ?? node.NodeType),
                new KeyValuePair<string, object?>("selected_tier", node.SelectedTier ?? "n/a"),
                new KeyValuePair<string, object?>("selected_model", node.SelectedModel ?? "n/a"));
        }

        if (runUsage.TotalTokens > 0)
        {
            _tokensObservedCounter.Add(runUsage.TotalTokens);
        }

        if (runUsage.CostUsd > 0)
        {
            _costObservedCounter.Add(runUsage.CostUsd);
        }

        lock (_syncRoot)
        {
            _totalCostUsd += runUsage.CostUsd;
            AddRecentRun(runSample);
            foreach (var node in nodeSamples)
            {
                AddToBucket(_nodeTypeBuckets, node.NodeType, node);
                AddToBucket(_stageBuckets, node.RoutingStage ?? node.NodeType, node);
                AddToBucket(_routeReasonBuckets, node.RouteReason ?? "n/a", node);
                AddToBucket(_modelRouteBuckets, CreateModelRouteBucketKey(node), node);
            }
        }
    }

    public WorkflowRunMetricsSnapshot GetSnapshot()
    {
        var completedRuns = Interlocked.Read(ref _totalRunsCompleted);
        var completedDurationMs = Interlocked.Read(ref _totalCompletedRunDurationMs);
        var averageDurationMs = completedRuns == 0
            ? 0d
            : (double)completedDurationMs / completedRuns;

        lock (_syncRoot)
        {
            return new WorkflowRunMetricsSnapshot(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                TotalRunsStarted: Interlocked.Read(ref _totalRunsStarted),
                ActiveRuns: Interlocked.Read(ref _activeRuns),
                TotalRunsCompleted: completedRuns,
                TotalRunsSucceeded: Interlocked.Read(ref _totalRunsSucceeded),
                TotalRunsFailed: Interlocked.Read(ref _totalRunsFailed),
                TotalRunsDeduplicated: Interlocked.Read(ref _totalRunsDeduplicated),
                TotalNodeStatusUpdates: Interlocked.Read(ref _totalNodeStatusUpdates),
                AverageCompletedRunDurationMs: averageDurationMs,
                TotalCompletedNodes: Interlocked.Read(ref _totalCompletedNodes),
                TotalSucceededNodes: Interlocked.Read(ref _totalSucceededNodes),
                TotalFailedNodes: Interlocked.Read(ref _totalFailedNodes),
                TotalSkippedNodes: Interlocked.Read(ref _totalSkippedNodes),
                TotalAgentNodes: Interlocked.Read(ref _totalAgentNodes),
                TotalInputTokens: Interlocked.Read(ref _totalInputTokens),
                TotalOutputTokens: Interlocked.Read(ref _totalOutputTokens),
                TotalTokens: Interlocked.Read(ref _totalTokens),
                TotalCostUsd: _totalCostUsd,
                RecentRuns: _recentRuns.Reverse().ToArray(),
                NodeTypeMetrics: CreateAggregateSnapshots(_nodeTypeBuckets),
                StageMetrics: CreateAggregateSnapshots(_stageBuckets),
                ModelRouteMetrics: CreateAggregateSnapshots(_modelRouteBuckets),
                RouteReasonMetrics: CreateAggregateSnapshots(_routeReasonBuckets));
        }
    }

    private static WorkflowRunMetricsRunSample CreateRunSample(
        WorkflowRunMetricsRunRecord record,
        long durationMs,
        IReadOnlyList<WorkflowRunMetricsNodeSample> nodeSamples,
        WorkflowRunUsageMetrics usage)
    {
        return new WorkflowRunMetricsRunSample(
            RunId: record.RunId,
            WorkflowId: record.WorkflowId,
            WorkflowVersion: record.WorkflowVersion,
            WorkflowName: record.WorkflowName,
            TriggerType: record.TriggerType,
            Status: record.Status,
            CreatedAtUtc: record.CreatedAtUtc,
            StartedAtUtc: record.StartedAtUtc,
            CompletedAtUtc: record.CompletedAtUtc,
            DurationMs: durationMs,
            NodeCount: nodeSamples.Count,
            SucceededNodeCount: nodeSamples.Count(node => node.Status == WorkflowNodeRunStatus.Succeeded),
            FailedNodeCount: nodeSamples.Count(node => node.Status == WorkflowNodeRunStatus.Failed),
            SkippedNodeCount: nodeSamples.Count(node => node.Status == WorkflowNodeRunStatus.Skipped),
            AgentNodeCount: nodeSamples.Count(IsAgentNode),
            Error: record.Error,
            InputTokens: usage.InputTokens,
            OutputTokens: usage.OutputTokens,
            TotalTokens: usage.TotalTokens,
            CostUsd: usage.CostUsd,
            Nodes: nodeSamples);
    }

    private static WorkflowRunMetricsNodeSample CreateNodeSample(WorkflowNodeRunResult node)
    {
        var usage = ExtractUsage(node.NodeType, node.OutputJson);
        return new WorkflowRunMetricsNodeSample(
            NodeId: node.NodeId,
            NodeType: node.NodeType,
            NodeName: node.NodeName,
            Status: node.Status,
            StartedAtUtc: node.StartedAtUtc,
            CompletedAtUtc: node.CompletedAtUtc,
            DurationMs: CalculateDurationMs(node.StartedAtUtc, node.CompletedAtUtc),
            RoutingStage: node.RoutingStage,
            SelectedTier: node.SelectedTier,
            SelectedModel: node.SelectedModel,
            ThinkingMode: node.ThinkingMode,
            RouteReason: node.RouteReason,
            RoutingConfidence: node.RoutingConfidence,
            RoutingRetryCount: node.RoutingRetryCount,
            RoutingBudgetRemaining: node.RoutingBudgetRemaining,
            InputTokens: usage.InputTokens,
            OutputTokens: usage.OutputTokens,
            TotalTokens: usage.TotalTokens,
            CostUsd: usage.CostUsd);
    }

    private static WorkflowRunUsageMetrics ExtractUsage(string nodeType, string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson))
        {
            return WorkflowRunUsageMetrics.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(outputJson);
            var usageRoots = ResolveUsageRoots(nodeType, document.RootElement);
            var inputTokens = usageRoots.Sum(root => SumLongFields(root, InputTokenFields));
            var outputTokens = usageRoots.Sum(root => SumLongFields(root, OutputTokenFields));
            var totalTokens = usageRoots.Sum(root => SumLongFields(root, TotalTokenFields));
            if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
            {
                totalTokens = inputTokens + outputTokens;
            }

            return new WorkflowRunUsageMetrics(
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                TotalTokens: totalTokens,
                CostUsd: usageRoots.Sum(root => SumDoubleFields(root, CostFields)));
        }
        catch (JsonException)
        {
            return WorkflowRunUsageMetrics.Empty;
        }
    }

    private static IReadOnlyList<JsonElement> ResolveUsageRoots(string nodeType, JsonElement root)
    {
        var roots = new List<JsonElement>();

        // Agent adapters should put provider usage under agent_result.*.
        // We intentionally avoid scanning the whole node output: workflow payload is inherited downstream,
        // so a recursive full-output scan would double-count usage copied from previous nodes.
        if (string.Equals(nodeType, "agent_task", StringComparison.OrdinalIgnoreCase) &&
            root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("agent_result", out var agentResult) &&
            agentResult.ValueKind == JsonValueKind.Object)
        {
            AddObjectProperty(agentResult, "metadata", roots);
            AddObjectProperty(agentResult, "usage", roots);
        }

        // Reserved generic containers for future node executors that produce their own usage/cost diagnostics.
        AddObjectProperty(root, "_node_metrics", roots);
        AddObjectProperty(root, "node_metrics", roots);
        AddObjectProperty(root, "nodeUsage", roots);

        return roots;
    }

    private static void AddObjectProperty(JsonElement source, string propertyName, List<JsonElement> roots)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            roots.Add(value);
        }
    }

    private static long SumLongFields(JsonElement element, HashSet<string> fieldNames)
    {
        var total = 0L;
        Visit(element, property =>
        {
            if (fieldNames.Contains(property.Name) && TryReadLong(property.Value, out var value))
            {
                total += value;
            }
        });

        return total;
    }

    private static double SumDoubleFields(JsonElement element, HashSet<string> fieldNames)
    {
        var total = 0d;
        Visit(element, property =>
        {
            if (fieldNames.Contains(property.Name) && TryReadDouble(property.Value, out var value))
            {
                total += value;
            }
        });

        return total;
    }

    private static void Visit(JsonElement element, Action<JsonProperty> visitProperty)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    visitProperty(property);
                    Visit(property.Value, visitProperty);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, visitProperty);
                }

                break;
        }
    }

    private static bool TryReadLong(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private void AddRecentRun(WorkflowRunMetricsRunSample run)
    {
        _recentRuns.Enqueue(run);
        while (_recentRuns.Count > MaxRecentRuns)
        {
            _recentRuns.Dequeue();
        }
    }

    private static void AddToBucket(
        Dictionary<string, WorkflowRunMetricsAggregateBucket> buckets,
        string key,
        WorkflowRunMetricsNodeSample node)
    {
        if (!buckets.TryGetValue(key, out var bucket))
        {
            bucket = new WorkflowRunMetricsAggregateBucket(key);
            buckets[key] = bucket;
        }

        bucket.Add(node);
    }

    private static IReadOnlyList<WorkflowRunMetricsAggregateSnapshot> CreateAggregateSnapshots(
        IReadOnlyDictionary<string, WorkflowRunMetricsAggregateBucket> buckets)
    {
        return buckets.Values
            .Select(bucket => bucket.ToSnapshot())
            .OrderByDescending(snapshot => snapshot.CompletedNodes)
            .ThenBy(snapshot => snapshot.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorkflowRunUsageMetrics SumUsage(IEnumerable<WorkflowRunMetricsNodeSample> nodes)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        long totalTokens = 0;
        double costUsd = 0;
        foreach (var node in nodes)
        {
            inputTokens += node.InputTokens;
            outputTokens += node.OutputTokens;
            totalTokens += node.TotalTokens;
            costUsd += node.CostUsd;
        }

        return new WorkflowRunUsageMetrics(inputTokens, outputTokens, totalTokens, costUsd);
    }

    private static long CalculateDurationMs(DateTimeOffset? startedAtUtc, DateTimeOffset? completedAtUtc)
    {
        if (!startedAtUtc.HasValue || !completedAtUtc.HasValue)
        {
            return 0;
        }

        var duration = completedAtUtc.Value - startedAtUtc.Value;
        return Math.Max(0L, (long)Math.Round(duration.TotalMilliseconds));
    }

    private static string CreateModelRouteBucketKey(WorkflowRunMetricsNodeSample node)
    {
        return $"{node.SelectedTier ?? "n/a"}::{node.SelectedModel ?? "n/a"}::{node.RouteReason ?? "n/a"}";
    }

    private static bool IsAgentNode(WorkflowRunMetricsNodeSample node)
    {
        return !string.Equals(node.SelectedTier, "no_llm", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.SelectedModel, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> CreateFieldSet(params string[] fields)
    {
        return fields.ToHashSet(KeyComparer);
    }

    private sealed class WorkflowRunMetricsAggregateBucket(string key)
    {
        private long _completedNodes;
        private long _succeededNodes;
        private long _failedNodes;
        private long _skippedNodes;
        private long _totalDurationMs;
        private long _inputTokens;
        private long _outputTokens;
        private long _totalTokens;
        private double _costUsd;

        public void Add(WorkflowRunMetricsNodeSample node)
        {
            _completedNodes++;
            _totalDurationMs += node.DurationMs;
            _inputTokens += node.InputTokens;
            _outputTokens += node.OutputTokens;
            _totalTokens += node.TotalTokens;
            _costUsd += node.CostUsd;

            if (node.Status == WorkflowNodeRunStatus.Succeeded)
            {
                _succeededNodes++;
            }
            else if (node.Status == WorkflowNodeRunStatus.Failed)
            {
                _failedNodes++;
            }
            else if (node.Status == WorkflowNodeRunStatus.Skipped)
            {
                _skippedNodes++;
            }
        }

        public WorkflowRunMetricsAggregateSnapshot ToSnapshot()
        {
            return new WorkflowRunMetricsAggregateSnapshot(
                Key: key,
                CompletedNodes: _completedNodes,
                SucceededNodes: _succeededNodes,
                FailedNodes: _failedNodes,
                SkippedNodes: _skippedNodes,
                TotalDurationMs: _totalDurationMs,
                AverageDurationMs: _completedNodes == 0 ? 0d : (double)_totalDurationMs / _completedNodes,
                InputTokens: _inputTokens,
                OutputTokens: _outputTokens,
                TotalTokens: _totalTokens,
                CostUsd: _costUsd);
        }
    }
}

public sealed record WorkflowRunMetricsRunRecord(
    string RunId,
    string? WorkflowId,
    int? WorkflowVersion,
    string WorkflowName,
    WorkflowRunTriggerType TriggerType,
    WorkflowRunStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Error,
    IReadOnlyList<WorkflowNodeRunResult> NodeResults);

public sealed record WorkflowRunMetricsSnapshot(
    DateTimeOffset CapturedAtUtc,
    long TotalRunsStarted,
    long ActiveRuns,
    long TotalRunsCompleted,
    long TotalRunsSucceeded,
    long TotalRunsFailed,
    long TotalRunsDeduplicated,
    long TotalNodeStatusUpdates,
    double AverageCompletedRunDurationMs,
    long TotalCompletedNodes,
    long TotalSucceededNodes,
    long TotalFailedNodes,
    long TotalSkippedNodes,
    long TotalAgentNodes,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    double TotalCostUsd,
    IReadOnlyList<WorkflowRunMetricsRunSample> RecentRuns,
    IReadOnlyList<WorkflowRunMetricsAggregateSnapshot> NodeTypeMetrics,
    IReadOnlyList<WorkflowRunMetricsAggregateSnapshot> StageMetrics,
    IReadOnlyList<WorkflowRunMetricsAggregateSnapshot> ModelRouteMetrics,
    IReadOnlyList<WorkflowRunMetricsAggregateSnapshot> RouteReasonMetrics);

public sealed record WorkflowRunMetricsRunSample(
    string RunId,
    string? WorkflowId,
    int? WorkflowVersion,
    string WorkflowName,
    WorkflowRunTriggerType TriggerType,
    WorkflowRunStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long DurationMs,
    int NodeCount,
    int SucceededNodeCount,
    int FailedNodeCount,
    int SkippedNodeCount,
    int AgentNodeCount,
    string? Error,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double CostUsd,
    IReadOnlyList<WorkflowRunMetricsNodeSample> Nodes);

public sealed record WorkflowRunMetricsNodeSample(
    string NodeId,
    string NodeType,
    string NodeName,
    WorkflowNodeRunStatus Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long DurationMs,
    string? RoutingStage,
    string? SelectedTier,
    string? SelectedModel,
    string? ThinkingMode,
    string? RouteReason,
    double? RoutingConfidence,
    int? RoutingRetryCount,
    double? RoutingBudgetRemaining,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double CostUsd);

public sealed record WorkflowRunMetricsAggregateSnapshot(
    string Key,
    long CompletedNodes,
    long SucceededNodes,
    long FailedNodes,
    long SkippedNodes,
    long TotalDurationMs,
    double AverageDurationMs,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double CostUsd);

public sealed record WorkflowRunUsageMetrics(
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double CostUsd)
{
    public static WorkflowRunUsageMetrics Empty { get; } = new(0, 0, 0, 0);
}
