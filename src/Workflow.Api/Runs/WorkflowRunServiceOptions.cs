namespace Workflow.Api.Runs;

/// <summary>
/// Что: runtime-настройки сервиса запусков workflow.
/// Зачем: управлять trigger-политикой без хардкода в коде.
/// Как: задает suppression window (секунды) для idempotency external сигналов.
/// </summary>
public sealed class WorkflowRunServiceOptions
{
    public int ExternalSignalSuppressionWindowSeconds { get; init; } = 300;
}
