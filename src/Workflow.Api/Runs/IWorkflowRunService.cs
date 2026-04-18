using System.Text.Json.Serialization;
using Workflow.Engine.Definitions;
using Workflow.Engine.Runtime;

namespace Workflow.Api.Runs;

/// <summary>
/// Что: контракт сервиса управления запусками workflow.
/// Зачем: API должно уметь запускать граф и отдавать состояние run/timeline.
/// Как: создает асинхронный run и предоставляет snapshot run и node statuses.
/// </summary>
public interface IWorkflowRunService
{
    Task<WorkflowRunSnapshot> StartRunAsync(
        StartWorkflowRunCommand command,
        CancellationToken cancellationToken);

    Task<WorkflowRunSnapshot?> ResumeRunAsync(string runId, CancellationToken cancellationToken);

    WorkflowRunSnapshot? GetRun(string runId);

    IReadOnlyList<WorkflowNodeRunResult>? GetRunNodes(string runId);
}

/// <summary>
/// Что: тип источника запуска workflow.
/// Зачем: различать ручные запуски UI и внешние webhook-сигналы.
/// Как: используется в run metadata и trigger policy (idempotency для external signal).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowRunTriggerType
{
    Manual = 0,
    ExternalSignal = 1
}

/// <summary>
/// Что: команда запуска workflow.
/// Зачем: нормализовать входы для runtime независимо от transport-слоя API.
/// Как: хранит definition, optional workflowId и optional input JSON.
/// </summary>
public sealed class StartWorkflowRunCommand
{
    public string? WorkflowId { get; init; }

    public int? WorkflowVersion { get; init; }

    public required WorkflowDefinition Definition { get; init; }

    public string? InputJson { get; init; }

    public WorkflowRunTriggerType TriggerType { get; init; } = WorkflowRunTriggerType.Manual;

    public string? TriggerSource { get; init; }

    public string? TriggerPayloadJson { get; init; }

    public string? IdempotencyKey { get; init; }

    public WorkflowRuntimeCheckpoint? ResumeCheckpoint { get; init; }
}

/// <summary>
/// Что: срез состояния одного запуска.
/// Зачем: единый DTO для API ответов и polling в UI.
/// Как: включает общий статус, итог, node statuses и runtime logs.
/// </summary>
public sealed class WorkflowRunSnapshot
{
    public required string RunId { get; init; }

    public string? WorkflowId { get; init; }

    public int? WorkflowVersion { get; init; }

    public required string WorkflowName { get; init; }

    public WorkflowRunTriggerType TriggerType { get; init; } = WorkflowRunTriggerType.Manual;

    public string? TriggerSource { get; init; }

    public string? TriggerPayloadJson { get; init; }

    public string? IdempotencyKey { get; init; }

    public bool WasDeduplicated { get; init; }

    public bool CanResume { get; init; }

    public DateTimeOffset? CheckpointedAtUtc { get; init; }

    public required WorkflowRunStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? Error { get; init; }

    public string? OutputJson { get; init; }

    public IReadOnlyList<WorkflowNodeRunResult> NodeResults { get; init; } = Array.Empty<WorkflowNodeRunResult>();

    public IReadOnlyList<WorkflowExecutionLogItem> Logs { get; init; } = Array.Empty<WorkflowExecutionLogItem>();
}
