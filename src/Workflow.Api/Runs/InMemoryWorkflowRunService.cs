using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Workflow.Engine.Definitions;
using Workflow.Engine.Runtime;

namespace Workflow.Api.Runs;

/// <summary>
/// Что: in-memory реализация run service для MVP.
/// Зачем: быстро получить run API и timeline без отдельной БД.
/// Как: хранит mutable state в памяти, запускает runtime в background task и отдает snapshots.
/// </summary>
public sealed class InMemoryWorkflowRunService : IWorkflowRunService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, WorkflowRunState> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _externalSignalRunIndex = new(StringComparer.Ordinal);
    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IWorkflowRunCheckpointStore _checkpointStore;
    private readonly WorkflowRunMetrics _metrics;
    private readonly ILogger<InMemoryWorkflowRunService> _logger;
    private readonly TimeSpan _externalSignalSuppressionWindow;

    public InMemoryWorkflowRunService(
        IWorkflowRuntime workflowRuntime,
        IWorkflowRunCheckpointStore checkpointStore,
        WorkflowRunMetrics metrics,
        ILogger<InMemoryWorkflowRunService> logger,
        IOptions<WorkflowRunServiceOptions> options)
    {
        _workflowRuntime = workflowRuntime;
        _checkpointStore = checkpointStore;
        _metrics = metrics;
        _logger = logger;
        var configuredSeconds = options.Value.ExternalSignalSuppressionWindowSeconds;
        _externalSignalSuppressionWindow = TimeSpan.FromSeconds(Math.Max(1, configuredSeconds));
        LoadCheckpointedRuns();
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
            "Workflow run {RunId} accepted; workflow {WorkflowId}, version {WorkflowVersion}, trigger {TriggerType}.",
            runId,
            command.WorkflowId,
            command.WorkflowVersion,
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
                "External signal accepted as new run {RunId}; workflow {WorkflowId}, version {WorkflowVersion}, source {TriggerSource}.",
                runId,
                command.WorkflowId,
                command.WorkflowVersion,
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
            workflowVersion: command.WorkflowVersion,
            workflowName: command.Definition.Name,
            definition: command.Definition,
            definitionJson: JsonSerializer.Serialize(command.Definition, JsonOptions),
            inputJson: command.InputJson,
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

    private void LoadCheckpointedRuns()
    {
        try
        {
            var checkpoints = _checkpointStore
                .ListLatestAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            foreach (var checkpoint in checkpoints)
            {
                var state = CreateRunStateFromCheckpoint(checkpoint);
                if (_runs.TryAdd(state.RunId, state))
                {
                    TryIndexExternalSignalRun(state);
                }
            }

            if (checkpoints.Count > 0)
            {
                _logger.LogInformation(
                    "Workflow run checkpoints loaded: {CheckpointCount}.",
                    checkpoints.Count);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Workflow run checkpoints could not be loaded on startup.");
        }
    }

    private static WorkflowRunState CreateRunStateFromCheckpoint(StoredWorkflowRunCheckpoint checkpoint)
    {
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(checkpoint.DefinitionJson, JsonOptions)
                         ?? throw new InvalidOperationException(
                             $"Checkpoint definition for run '{checkpoint.RunId}' cannot be deserialized.");
        var nodeResults = checkpoint.RuntimeCheckpoint.NodeResults.Count > 0
            ? checkpoint.RuntimeCheckpoint.NodeResults.Select(CloneNodeResult).ToArray()
            : definition.Nodes.Select(node => new WorkflowNodeRunResult
            {
                NodeId = node.Id,
                NodeType = node.Type,
                NodeName = node.Name,
                Status = WorkflowNodeRunStatus.Pending
            }).ToArray();

        var state = new WorkflowRunState(
            runId: checkpoint.RunId,
            workflowId: checkpoint.WorkflowId,
            workflowVersion: checkpoint.WorkflowVersion,
            workflowName: checkpoint.WorkflowName,
            definition: definition,
            definitionJson: checkpoint.DefinitionJson,
            inputJson: checkpoint.InputJson,
            triggerType: checkpoint.TriggerType,
            triggerSource: checkpoint.TriggerSource,
            triggerPayloadJson: checkpoint.TriggerPayloadJson,
            idempotencyKey: checkpoint.IdempotencyKey,
            createdAtUtc: checkpoint.CreatedAtUtc,
            initialNodeResults: nodeResults)
        {
            Status = checkpoint.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running
                ? WorkflowRunStatus.Paused
                : checkpoint.Status,
            StartedAtUtc = checkpoint.StartedAtUtc,
            CompletedAtUtc = checkpoint.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running
                ? null
                : checkpoint.CompletedAtUtc,
            Error = checkpoint.Status == WorkflowRunStatus.Failed ? checkpoint.Error : null,
            OutputJson = checkpoint.OutputJson,
            CheckpointedAtUtc = checkpoint.RuntimeCheckpoint.CheckpointedAtUtc,
            LastCheckpoint = checkpoint.RuntimeCheckpoint
        };

        foreach (var logItem in checkpoint.RuntimeCheckpoint.Logs)
        {
            state.Logs.Add(CloneLogItem(logItem));
        }

        return state;
    }

    private static StartWorkflowRunCommand CreateResumeCommand(
        WorkflowRunState state,
        WorkflowRuntimeCheckpoint checkpoint)
    {
        return new StartWorkflowRunCommand
        {
            WorkflowId = state.WorkflowId,
            WorkflowVersion = state.WorkflowVersion,
            Definition = state.Definition,
            InputJson = state.InputJson,
            TriggerType = state.TriggerType,
            TriggerSource = state.TriggerSource,
            TriggerPayloadJson = state.TriggerPayloadJson,
            IdempotencyKey = state.IdempotencyKey,
            ResumeCheckpoint = checkpoint
        };
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

    private void TryIndexExternalSignalRun(WorkflowRunState state)
    {
        if (state.TriggerType != WorkflowRunTriggerType.ExternalSignal ||
            string.IsNullOrWhiteSpace(state.WorkflowId) ||
            string.IsNullOrWhiteSpace(state.IdempotencyKey))
        {
            return;
        }

        var dedupKey = $"{state.WorkflowId.Trim()}::{state.IdempotencyKey.Trim()}";
        _externalSignalRunIndex.TryAdd(dedupKey, state.RunId);
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

    public async Task<WorkflowRunSnapshot?> ResumeRunAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        runId = runId.Trim();
        if (!_runs.TryGetValue(runId, out var state))
        {
            var checkpoint = await _checkpointStore.TryReadLatestAsync(runId, cancellationToken)
                .ConfigureAwait(false);
            if (checkpoint is null)
            {
                return null;
            }

            state = CreateRunStateFromCheckpoint(checkpoint);
            _runs.TryAdd(runId, state);
            TryIndexExternalSignalRun(state);
        }

        WorkflowRuntimeCheckpoint? checkpointToResume;
        lock (state.SyncRoot)
        {
            if (state.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Succeeded)
            {
                return ToSnapshot(state);
            }

            checkpointToResume = state.LastCheckpoint;
            if (checkpointToResume is null)
            {
                return ToSnapshot(state);
            }

            state.Status = WorkflowRunStatus.Pending;
            state.CompletedAtUtc = null;
            state.Error = null;
        }

        var command = CreateResumeCommand(state, checkpointToResume);
        _logger.LogInformation(
            "Workflow run {RunId} resume accepted from checkpoint {CheckpointedAtUtc}.",
            runId,
            checkpointToResume.CheckpointedAtUtc);
        _metrics.OnRunStarted(state.TriggerType);
        StartBackgroundRun(runId, state, command);
        return ToSnapshot(state);
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
            ["WorkflowVersion"] = state.WorkflowVersion,
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
                state.Definition,
                new WorkflowRunRequest
                {
                    RunId = runId,
                    InputJson = state.InputJson,
                    ResumeCheckpoint = command.ResumeCheckpoint
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
                async (checkpoint, checkpointCancellationToken) =>
                {
                    lock (state.SyncRoot)
                    {
                        state.LastCheckpoint = checkpoint;
                        state.CheckpointedAtUtc = checkpoint.CheckpointedAtUtc;
                    }

                    await PersistCheckpointAsync(state, checkpoint, checkpointCancellationToken)
                        .ConfigureAwait(false);
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

            await PersistCheckpointAsync(
                    state,
                    CreateFinalCheckpoint(state),
                    CancellationToken.None)
                .ConfigureAwait(false);

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
            await PersistCheckpointAsync(
                    state,
                    CreateFinalCheckpoint(state),
                    CancellationToken.None)
                .ConfigureAwait(false);
            ReportRunCompletedMetrics(state);
        }
    }

    private async Task PersistCheckpointAsync(
        WorkflowRunState state,
        WorkflowRuntimeCheckpoint runtimeCheckpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            lock (state.SyncRoot)
            {
                state.LastCheckpoint = runtimeCheckpoint;
                state.CheckpointedAtUtc = runtimeCheckpoint.CheckpointedAtUtc;
            }

            await _checkpointStore
                .SaveAsync(CreateStoredCheckpoint(state, runtimeCheckpoint), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Workflow run checkpoint persist failed for run {RunId}.",
                state.RunId);
        }
    }

    private static StoredWorkflowRunCheckpoint CreateStoredCheckpoint(
        WorkflowRunState state,
        WorkflowRuntimeCheckpoint runtimeCheckpoint)
    {
        lock (state.SyncRoot)
        {
            return new StoredWorkflowRunCheckpoint
            {
                RunId = state.RunId,
                WorkflowId = state.WorkflowId,
                WorkflowVersion = state.WorkflowVersion,
                WorkflowName = state.WorkflowName,
                DefinitionJson = state.DefinitionJson,
                InputJson = state.InputJson,
                TriggerType = state.TriggerType,
                TriggerSource = state.TriggerSource,
                TriggerPayloadJson = state.TriggerPayloadJson,
                IdempotencyKey = state.IdempotencyKey,
                CreatedAtUtc = state.CreatedAtUtc,
                Status = state.Status,
                StartedAtUtc = state.StartedAtUtc,
                CompletedAtUtc = state.CompletedAtUtc,
                Error = state.Error,
                OutputJson = state.OutputJson,
                RuntimeCheckpoint = runtimeCheckpoint
            };
        }
    }

    private static WorkflowRuntimeCheckpoint CreateFinalCheckpoint(WorkflowRunState state)
    {
        lock (state.SyncRoot)
        {
            return new WorkflowRuntimeCheckpoint
            {
                RunId = state.RunId,
                WorkflowName = state.WorkflowName,
                Status = state.Status,
                CheckpointedAtUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = state.StartedAtUtc,
                CompletedAtUtc = state.CompletedAtUtc,
                LastNodeId = state.LastCheckpoint?.LastNodeId,
                Error = state.Error,
                OutputJson = state.OutputJson,
                NodeOutputsJson = state.LastCheckpoint?.NodeOutputsJson ??
                                  new Dictionary<string, string>(StringComparer.Ordinal),
                NodeResults = state.NodeResults.Select(CloneNodeResult).ToArray(),
                Logs = state.Logs.Select(CloneLogItem).ToArray()
            };
        }
    }

    private void ReportRunCompletedMetrics(WorkflowRunState state)
    {
        WorkflowRunMetricsRunRecord record;
        lock (state.SyncRoot)
        {
            record = new WorkflowRunMetricsRunRecord(
                RunId: state.RunId,
                WorkflowId: state.WorkflowId,
                WorkflowVersion: state.WorkflowVersion,
                WorkflowName: state.WorkflowName,
                TriggerType: state.TriggerType,
                Status: state.Status,
                CreatedAtUtc: state.CreatedAtUtc,
                StartedAtUtc: state.StartedAtUtc,
                CompletedAtUtc: state.CompletedAtUtc,
                Error: state.Error,
                NodeResults: state.NodeResults.Select(CloneNodeResult).ToArray());
        }

        _metrics.OnRunCompleted(record);
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
                WorkflowVersion = state.WorkflowVersion,
                WorkflowName = state.WorkflowName,
                TriggerType = state.TriggerType,
                TriggerSource = state.TriggerSource,
                TriggerPayloadJson = state.TriggerPayloadJson,
                IdempotencyKey = state.IdempotencyKey,
                WasDeduplicated = wasDeduplicated,
                CanResume = state.LastCheckpoint is not null &&
                            state.Status is not WorkflowRunStatus.Running and not WorkflowRunStatus.Succeeded,
                CheckpointedAtUtc = state.CheckpointedAtUtc,
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

    private static WorkflowExecutionLogItem CloneLogItem(WorkflowExecutionLogItem source)
    {
        return new WorkflowExecutionLogItem
        {
            TimestampUtc = source.TimestampUtc,
            NodeId = source.NodeId,
            Message = source.Message
        };
    }

    private sealed class WorkflowRunState
    {
        public WorkflowRunState(
            string runId,
            string? workflowId,
            int? workflowVersion,
            string workflowName,
            WorkflowDefinition definition,
            string definitionJson,
            string? inputJson,
            WorkflowRunTriggerType triggerType,
            string? triggerSource,
            string? triggerPayloadJson,
            string? idempotencyKey,
            DateTimeOffset createdAtUtc,
            IReadOnlyList<WorkflowNodeRunResult> initialNodeResults)
        {
            RunId = runId;
            WorkflowId = workflowId;
            WorkflowVersion = workflowVersion;
            WorkflowName = workflowName;
            Definition = definition;
            DefinitionJson = definitionJson;
            InputJson = inputJson;
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

        public int? WorkflowVersion { get; }

        public string WorkflowName { get; }

        public WorkflowDefinition Definition { get; }

        public string DefinitionJson { get; }

        public string? InputJson { get; }

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

        public DateTimeOffset? CheckpointedAtUtc { get; set; }

        public WorkflowRuntimeCheckpoint? LastCheckpoint { get; set; }

        public List<WorkflowNodeRunResult> NodeResults { get; }

        public Dictionary<string, int> NodeIndexById { get; }

        public List<WorkflowExecutionLogItem> Logs { get; } = new();

        public object SyncRoot { get; } = new();
    }
}
