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
    string Status,
    string DefinitionJson,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PublishedAtUtc);

/// <summary>
/// Что: краткое представление workflow для списков.
/// Зачем: на странице списка не нужно передавать весь JSON-граф, но нужен draft/published статус.
/// Как: возвращается последняя draft-версия и optional published version для каждого WorkflowId.
/// </summary>
public sealed record WorkflowDefinitionSummary(
    string WorkflowId,
    string Name,
    int Version,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    int? PublishedVersion,
    DateTimeOffset? PublishedAtUtc);

/// <summary>
/// Что: lifecycle-статусы версий workflow.
/// Зачем: runs должны запускаться от конкретной draft/published версии, а не от размытого latest.
/// Как: SQLite хранит строку; constants защищают от опечаток в API/runtime.
/// </summary>
public static class WorkflowDefinitionStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
}
