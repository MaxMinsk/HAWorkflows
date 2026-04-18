namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: настройки фильтрации нод по профилю окружения.
/// Зачем: отделить публичные built-in pack-и от local-only pack-ов без форка кода.
/// Как: Profile/IncludeLocalNodes дают дефолт, EnabledPacks/DisabledPacks явно включают или исключают pack-и.
/// </summary>
public sealed class WorkflowNodeCatalogOptions
{
    public string Profile { get; init; } = "Release";

    public bool IncludeLocalNodes { get; init; } = false;

    public string[] EnabledPacks { get; init; } = [];

    public string[] DisabledPacks { get; init; } = [];
}
