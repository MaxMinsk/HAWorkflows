namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: реестр доступных node executors для текущего runtime-профиля.
/// Зачем: предоставить единый источник truth для валидации, исполнения и UI-палитры.
/// Как: хранит набор executor-ов после capability-based pack policy.
/// </summary>
public interface IWorkflowNodeCatalog
{
    IReadOnlyCollection<WorkflowNodeDescriptor> GetDescriptors();

    IReadOnlyCollection<string> GetSupportedNodeTypes();

    bool TryGetExecutor(string nodeType, out IWorkflowNodeExecutor executor);
}
