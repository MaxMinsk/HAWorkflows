using System.Text.Json.Serialization;

namespace Workflow.Engine.Runtime;

/// <summary>
/// Что: входные параметры запуска workflow.
/// Зачем: передать начальный payload в runtime без привязки к конкретному transport.
/// Как: InputJson (JSON-строка) интерпретируется как стартовый контекст графа.
/// </summary>
public sealed class WorkflowRunRequest
{
    public string? RunId { get; init; }

    public string? InputJson { get; init; }
}

/// <summary>
/// Что: статусы общего выполнения workflow.
/// Зачем: единообразный runtime-state для API/UI.
/// Как: pending/running/succeeded/failed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowRunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>
/// Что: статусы выполнения отдельной ноды.
/// Зачем: показать прогресс графа по шагам.
/// Как: pending/running/succeeded/failed/skipped.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowNodeRunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}

/// <summary>
/// Что: runtime-диагностика по одной ноде.
/// Зачем: отдать UI/API подробный timeline и результат каждого шага.
/// Как: содержит статусы, timestamps, error и JSON-выход ноды.
/// </summary>
public sealed class WorkflowNodeRunResult
{
    public required string NodeId { get; init; }

    public required string NodeType { get; init; }

    public required string NodeName { get; init; }

    public WorkflowNodeRunStatus Status { get; set; } = WorkflowNodeRunStatus.Pending;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? Error { get; set; }

    public string? OutputJson { get; set; }

    public string? RoutingStage { get; set; }

    public string? SelectedTier { get; set; }

    public string? SelectedModel { get; set; }

    public string? ThinkingMode { get; set; }

    public string? RouteReason { get; set; }

    public double? RoutingConfidence { get; set; }

    public int? RoutingRetryCount { get; set; }

    public double? RoutingBudgetRemaining { get; set; }
}

/// <summary>
/// Что: запись runtime-лога в ходе исполнения.
/// Зачем: хранить детерминированный trace действий нод.
/// Как: формируется runtime и накапливается в порядке исполнения.
/// </summary>
public sealed class WorkflowExecutionLogItem
{
    public required DateTimeOffset TimestampUtc { get; init; }

    public required string NodeId { get; init; }

    public required string Message { get; init; }
}

/// <summary>
/// Что: итог исполнения workflow.
/// Зачем: универсальный контракт для API/UI при запуске графа.
/// Как: агрегирует общий status, node results, logs и финальный output.
/// </summary>
public sealed class WorkflowRunResult
{
    public required string RunId { get; init; }

    public required string WorkflowName { get; init; }

    public WorkflowRunStatus Status { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public string? Error { get; init; }

    public string? OutputJson { get; init; }

    public IReadOnlyList<Artifacts.WorkflowArtifactDescriptor> Artifacts { get; init; } = Array.Empty<Artifacts.WorkflowArtifactDescriptor>();

    public IReadOnlyList<WorkflowExecutionLogItem> Logs { get; init; } = Array.Empty<WorkflowExecutionLogItem>();

    public IReadOnlyList<WorkflowNodeRunResult> NodeResults { get; init; } = Array.Empty<WorkflowNodeRunResult>();
}
