namespace Workflow.Persistence.WorkflowDefinitions;

/// <summary>
/// Что: полная запись сохраненного workflow.
/// Зачем: API нужен и метаданные, и JSON-граф для загрузки в UI.
/// Как: одна запись соответствует одной версии workflow в хранилище.
/// </summary>
public sealed record StoredWorkflowDefinition(
    string WorkflowId,
    string Name,
    int Version,
    string DefinitionJson,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Что: краткое представление workflow для списков.
/// Зачем: на странице списка не нужно передавать весь JSON-граф.
/// Как: возвращается только последняя версия на каждый WorkflowId.
/// </summary>
public sealed record WorkflowDefinitionSummary(
    string WorkflowId,
    string Name,
    int Version,
    DateTimeOffset UpdatedAtUtc);
