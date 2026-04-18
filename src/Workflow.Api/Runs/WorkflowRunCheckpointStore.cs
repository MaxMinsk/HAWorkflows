using Workflow.Engine.Runtime;

namespace Workflow.Api.Runs;

/// <summary>
/// Что: durable-хранилище последнего checkpoint-а workflow run.
/// Зачем: in-memory run service теряет state при рестарте worker, а local pipeline должен уметь продолжать работу.
/// Как: API слой сохраняет runtime checkpoint вместе с definition/input metadata и потом восстанавливает run state.
/// </summary>
public interface IWorkflowRunCheckpointStore
{
    Task SaveAsync(StoredWorkflowRunCheckpoint checkpoint, CancellationToken cancellationToken);

    Task<StoredWorkflowRunCheckpoint?> TryReadLatestAsync(string runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredWorkflowRunCheckpoint>> ListLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Что: сериализуемый checkpoint run-а.
/// Зачем: хранить не только runtime node outputs, но и metadata, нужную для resume после restart.
/// Как: один документ соответствует последнему известному состоянию конкретного runId.
/// </summary>
public sealed class StoredWorkflowRunCheckpoint
{
    public required string RunId { get; init; }

    public string? WorkflowId { get; init; }

    public int? WorkflowVersion { get; init; }

    public required string WorkflowName { get; init; }

    public required string DefinitionJson { get; init; }

    public string? InputJson { get; init; }

    public WorkflowRunTriggerType TriggerType { get; init; } = WorkflowRunTriggerType.Manual;

    public string? TriggerSource { get; init; }

    public string? TriggerPayloadJson { get; init; }

    public string? IdempotencyKey { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required WorkflowRunStatus Status { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? Error { get; init; }

    public string? OutputJson { get; init; }

    public required WorkflowRuntimeCheckpoint RuntimeCheckpoint { get; init; }
}
