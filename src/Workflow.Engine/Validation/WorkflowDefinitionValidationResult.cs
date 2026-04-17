namespace Workflow.Engine.Validation;

/// <summary>
/// Что: результат структурной валидации workflow-графа.
/// Зачем: отделить ошибки схемы/графа от фазы исполнения.
/// Как: содержит список ошибок и topological order для валидного DAG.
/// </summary>
public sealed class WorkflowDefinitionValidationResult
{
    public WorkflowDefinitionValidationResult(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> topologicalOrder)
    {
        Errors = errors;
        TopologicalOrder = topologicalOrder;
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> TopologicalOrder { get; }
}

