namespace Workflow.Persistence.WorkflowDefinitions;

/// <summary>
/// Что: контракт хранилища определений workflow.
/// Зачем: API должен сохранять и читать версии графов независимо от способа хранения.
/// Как: реализация возвращает последние версии workflow и создает новую версию при каждом сохранении.
/// </summary>
public interface IWorkflowDefinitionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowDefinitionSummary>> GetLatestAsync(CancellationToken cancellationToken);

    Task<StoredWorkflowDefinition?> GetLatestByIdAsync(string workflowId, CancellationToken cancellationToken);

    Task<StoredWorkflowDefinition> SaveAsync(
        string? workflowId,
        string name,
        string definitionJson,
        CancellationToken cancellationToken);
}
