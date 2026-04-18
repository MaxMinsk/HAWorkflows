using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Workflow.Engine.Runtime;

namespace Workflow.Api.Runs;

/// <summary>
/// Что: in-memory реализация run service для MVP.
/// Зачем: быстро получить run API и timeline без отдельной БД.
/// Как: хранит mutable state в памяти, запускает runtime в background task и отдает snapshots.
/// </summary>
public sealed class InMemoryWorkflowRunService : IWorkflowRunService
{
    private readonly ConcurrentDictionary<string, WorkflowRunState> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _externalSignalRunIndex = new(StringComparer.Ordinal);
    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly WorkflowRunMetrics _metrics;
    private readonly ILogger<InMemoryWorkflowRunService> _logger;
    private readonly TimeSpan _externalSignalSuppressionWindow;

    public InMemoryWorkflowRunService(
        IWorkflowRuntime workflowRuntime,
        WorkflowRunMetrics metrics,
        ILogger<InMemoryWorkflowRunService> logger,
        IOptions<WorkflowRunServiceOptions> options)
    {
        _workflowRuntime = workflowRuntime;
        _metrics = metrics;
        _logger = logger;
        var configuredSeconds = options.Value.ExternalSignalSuppressionWindowSeconds;
        _externalSignalSuppressionWindow = TimeSpan.FromSeconds(Math.Max(1, configuredSeconds));
    }

    public Task<WorkflowRunSnapshot> StartRunAsync(
        StartWorkflowRunCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Definition);

        if (command.TriggerType == WorkflowRunTriggerType.ExternalSignal &&
            TryBuildExternalSignalDedupKey(command, out var dedupKey))
        {
            return Task.FromResult(StartExternalSignalRun(command, dedupKey));
        }

        var runId = Guid.NewGuid().ToString("N");
        var state = CreateRunState(runId, command, DateTimeOffset.UtcNow);
        if (!_runs.TryAdd(runId, state))
        {
            throw new InvalidOperationException($"Run '{runId}' already exists.");
        }

        _metrics.OnRunStarted(command.TriggerType);
        _logger.LogInformation(
            "Workflow run {RunId} accepted; workflow {WorkflowId}, trigger {TriggerType}.",
            runId,
            command.WorkflowId,
            command.TriggerType);
        StartBackgroundRun(runId, state, command);
        return Task.FromResult(ToSnapshot(state));
    }

    private WorkflowRunSnapshot StartExternalSignalRun(
        StartWorkflowRunCommand command,
        string dedupKey)
    {
        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (_externalSignalRunIndex.TryGetValue(dedupKey, out var indexedRunId))
            {
                if (TryGetRecentRun(indexedRunId, now, out var existingState))
                {
                    _metrics.OnRunDeduplicated(command.TriggerType);
                    _logger.LogInformation(
                        "External signal deduplicated for workflow {WorkflowId}; idempotency key {IdempotencyKey}, run {RunId}.",
                        command.WorkflowId,
                        command.IdempotencyKey,
                        indexedRunId);
                    return ToSnapshot(existingState, wasDeduplicated: true);
                }

                _externalSignalRunIndex.TryRemove(dedupKey, out _);
                continue;
            }

            var runId = Guid.NewGuid().ToString("N");
            if (!_externalSignalRunIndex.TryAdd(dedupKey, runId))
            {
                continue;
            }

            WorkflowRunState state;
            try
            {
                state = CreateRunState(runId, command, now);
            }
            catch
            {
                _externalSignalRunIndex.TryRemove(dedupKey, out _);
                throw;
            }

            if (!_runs.TryAdd(runId, state))
            {
                _externalSignalRunIndex.TryRemove(dedupKey, out _);
                continue;
            }

            _metrics.OnRunStarted(command.TriggerType);
            _logger.LogInformation(
                "External signal accepted as new run {RunId}; workflow {WorkflowId}, source {TriggerSource}.",
                runId,
                command.WorkflowId,
                command.TriggerSource);
            StartBackgroundRun(runId, state, command);
            return ToSnapshot(state);
        }
    }

    private void StartBackgroundRun(
        string runId,
        WorkflowRunState state,
        StartWorkflowRunCommand command)
    {
        _ = Task.Run(
            () => ExecuteRunAsync(runId, state, command),
            CancellationToken.None);
    }

    private WorkflowRunState CreateRunState(
        string runId,
        StartWorkflowRunCommand command,
        DateTimeOffset createdAtUtc)
    {
        return new WorkflowRunState(
            runId: runId,
            workflowId: command.WorkflowId,
            workflowName: command.Definition.Name,
            triggerType: command.TriggerType,
            triggerSource: command.TriggerSource,
            triggerPayloadJson: command.TriggerPayloadJson,
            idempotencyKey: command.IdempotencyKey,
            createdAtUtc: createdAtUtc,
            initialNodeResults: command.Definition.Nodes.Select(node => new WorkflowNodeRunResult
            {
                NodeId = node.Id,
                NodeType = node.Type,
                NodeName = node.Name,
                Status = WorkflowNodeRunStatus.Pending
            }).ToArray());
    }

    private bool TryGetRecentRun(
        string runId,
        DateTimeOffset now,
        out WorkflowRunState state)
    {
        state = null!;
        if (!_runs.TryGetValue(runId, out var existingState))
        {
            return false;
        }

        if (now - existingState.CreatedAtUtc > _externalSignalSuppressionWindow)
        {
            return false;
        }

        state = existingState;
        return true;
    }

    private static bool TryBuildExternalSignalDedupKey(
        StartWorkflowRunCommand command,
        out string dedupKey)
    {
        dedupKey = string.Empty;
        if (string.IsNullOrWhiteSpace(command.WorkflowId) ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return false;
        }

        var workflowId = command.WorkflowId.Trim();
        var idempotencyKey = command.IdempotencyKey.Trim();
        dedupKey = $"{workflowId}::{idempotencyKey}";
        return true;
    }

    public WorkflowRunSnapshot? GetRun(string runId)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return null;
        }

        return ToSnapshot(state);
    }

    public IReadOnlyList<WorkflowNodeRunResult>? GetRunNodes(string runId)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            return state.NodeResults
                .Select(CloneNodeResult)
                .ToArray();
        }
    }

    private async Task ExecuteRunAsync(
        string runId,
        WorkflowRunState state,
        StartWorkflowRunCommand command)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RunId"] = runId,
            ["WorkflowId"] = state.WorkflowId,
            ["TriggerType"] = state.TriggerType.ToString()
        });

        try
        {
            lock (state.SyncRoot)
            {
                state.Status = WorkflowRunStatus.Running;
                state.StartedAtUtc = DateTimeOffset.UtcNow;
            }

            _logger.LogInformation("Workflow run execution started.");

            var runtimeResult = await _workflowRuntime.ExecuteAsync(
                command.Definition,
                new WorkflowRunRequest
                {
                    RunId = runId,
                    InputJson = command.InputJson
                },
                async (nodeUpdate, _) =>
                {
                    lock (state.SyncRoot)
                    {
                        ApplyNodeUpdate(state, nodeUpdate);
                    }

                    _metrics.OnNodeStatusChanged(nodeUpdate.Status);
                    _logger.LogDebug(
                        "Node status update: node {NodeId}, status {NodeStatus}.",
                        nodeUpdate.NodeId,
                        nodeUpdate.Status);
                    await Task.CompletedTask;
                },
                CancellationToken.None);

            lock (state.SyncRoot)
            {
                state.Status = runtimeResult.Status;
                state.CompletedAtUtc = runtimeResult.CompletedAtUtc;
                state.Error = runtimeResult.Error;
                state.OutputJson = runtimeResult.OutputJson;

                state.Logs.Clear();
                foreach (var logItem in runtimeResult.Logs)
                {
                    state.Logs.Add(new WorkflowExecutionLogItem
                    {
                        TimestampUtc = logItem.TimestampUtc,
                        NodeId = logItem.NodeId,
                        Message = logItem.Message
                    });
                }

                foreach (var result in runtimeResult.NodeResults)
                {
                    ApplyNodeUpdate(state, result);
                }
            }

            _logger.LogInformation(
                "Workflow run completed with status {Status}.",
                state.Status);
            ReportRunCompletedMetrics(state);
        }
        catch (Exception exception)
        {
            lock (state.SyncRoot)
            {
                state.Status = WorkflowRunStatus.Failed;
                state.CompletedAtUtc = DateTimeOffset.UtcNow;
                state.Error = exception.Message;
            }

            _logger.LogError(
                exception,
                "Workflow run failed with unhandled exception.");
            ReportRunCompletedMetrics(state);
        }
    }

    private void ReportRunCompletedMetrics(WorkflowRunState state)
    {
        var startedAtUtc = state.StartedAtUtc ?? state.CreatedAtUtc;
        var completedAtUtc = state.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        var duration = completedAtUtc - startedAtUtc;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        _metrics.OnRunCompleted(state.Status, duration, state.TriggerType);
    }

    private static void ApplyNodeUpdate(
        WorkflowRunState state,
        WorkflowNodeRunResult nodeUpdate)
    {
        if (!state.NodeIndexById.TryGetValue(nodeUpdate.NodeId, out var index))
        {
            state.NodeIndexById[nodeUpdate.NodeId] = state.NodeResults.Count;
            state.NodeResults.Add(CloneNodeResult(nodeUpdate));
            return;
        }

        var target = state.NodeResults[index];
        target.Status = nodeUpdate.Status;
        target.StartedAtUtc = nodeUpdate.StartedAtUtc;
        target.CompletedAtUtc = nodeUpdate.CompletedAtUtc;
        target.Error = nodeUpdate.Error;
        target.OutputJson = nodeUpdate.OutputJson;
        target.RoutingStage = nodeUpdate.RoutingStage;
        target.SelectedTier = nodeUpdate.SelectedTier;
        target.SelectedModel = nodeUpdate.SelectedModel;
        target.ThinkingMode = nodeUpdate.ThinkingMode;
        target.RouteReason = nodeUpdate.RouteReason;
        target.RoutingConfidence = nodeUpdate.RoutingConfidence;
        target.RoutingRetryCount = nodeUpdate.RoutingRetryCount;
        target.RoutingBudgetRemaining = nodeUpdate.RoutingBudgetRemaining;
    }

    private static WorkflowRunSnapshot ToSnapshot(
        WorkflowRunState state,
        bool wasDeduplicated = false)
    {
        lock (state.SyncRoot)
        {
            return new WorkflowRunSnapshot
            {
                RunId = state.RunId,
                WorkflowId = state.WorkflowId,
                WorkflowName = state.WorkflowName,
                TriggerType = state.TriggerType,
                TriggerSource = state.TriggerSource,
                TriggerPayloadJson = state.TriggerPayloadJson,
                IdempotencyKey = state.IdempotencyKey,
                WasDeduplicated = wasDeduplicated,
                Status = state.Status,
                CreatedAtUtc = state.CreatedAtUtc,
                StartedAtUtc = state.StartedAtUtc,
                CompletedAtUtc = state.CompletedAtUtc,
                Error = state.Error,
                OutputJson = state.OutputJson,
                NodeResults = state.NodeResults.Select(CloneNodeResult).ToArray(),
                Logs = state.Logs
                    .Select(logItem => new WorkflowExecutionLogItem
                    {
                        TimestampUtc = logItem.TimestampUtc,
                        NodeId = logItem.NodeId,
                        Message = logItem.Message
                    })
                    .ToArray()
            };
        }
    }

    private static WorkflowNodeRunResult CloneNodeResult(WorkflowNodeRunResult source)
    {
        return new WorkflowNodeRunResult
        {
            NodeId = source.NodeId,
            NodeType = source.NodeType,
            NodeName = source.NodeName,
            Status = source.Status,
            StartedAtUtc = source.StartedAtUtc,
            CompletedAtUtc = source.CompletedAtUtc,
            Error = source.Error,
            OutputJson = source.OutputJson,
            RoutingStage = source.RoutingStage,
            SelectedTier = source.SelectedTier,
            SelectedModel = source.SelectedModel,
            ThinkingMode = source.ThinkingMode,
            RouteReason = source.RouteReason,
            RoutingConfidence = source.RoutingConfidence,
            RoutingRetryCount = source.RoutingRetryCount,
            RoutingBudgetRemaining = source.RoutingBudgetRemaining
        };
    }

    private sealed class WorkflowRunState
    {
        public WorkflowRunState(
            string runId,
            string? workflowId,
            string workflowName,
            WorkflowRunTriggerType triggerType,
            string? triggerSource,
            string? triggerPayloadJson,
            string? idempotencyKey,
            DateTimeOffset createdAtUtc,
            IReadOnlyList<WorkflowNodeRunResult> initialNodeResults)
        {
            RunId = runId;
            WorkflowId = workflowId;
            WorkflowName = workflowName;
            TriggerType = triggerType;
            TriggerSource = triggerSource;
            TriggerPayloadJson = triggerPayloadJson;
            IdempotencyKey = idempotencyKey;
            CreatedAtUtc = createdAtUtc;
            Status = WorkflowRunStatus.Pending;
            NodeResults = initialNodeResults
                .Select(CloneNodeResult)
                .ToList();
            NodeIndexById = NodeResults
                .Select((result, index) => new { result.NodeId, index })
                .ToDictionary(item => item.NodeId, item => item.index, StringComparer.Ordinal);
        }

        public string RunId { get; }

        public string? WorkflowId { get; }

        public string WorkflowName { get; }

        public WorkflowRunTriggerType TriggerType { get; }

        public string? TriggerSource { get; }

        public string? TriggerPayloadJson { get; }

        public string? IdempotencyKey { get; }

        public WorkflowRunStatus Status { get; set; }

        public DateTimeOffset CreatedAtUtc { get; }

        public DateTimeOffset? StartedAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public string? Error { get; set; }

        public string? OutputJson { get; set; }

        public List<WorkflowNodeRunResult> NodeResults { get; }

        public Dictionary<string, int> NodeIndexById { get; }

        public List<WorkflowExecutionLogItem> Logs { get; } = new();

        public object SyncRoot { get; } = new();
    }
}
