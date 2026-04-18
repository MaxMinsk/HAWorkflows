namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: настройки capability-based фильтрации нод.
/// Зачем: включать/отключать packs без отдельного product split-а remote/local.
/// Как: EnabledPacks задает allow-list, DisabledPacks задает deny-list; пустой EnabledPacks включает все зарегистрированные packs.
/// </summary>
public sealed class WorkflowNodeCatalogOptions
{
    public string[] EnabledPacks { get; init; } = [];

    public string[] DisabledPacks { get; init; } = [];
}
