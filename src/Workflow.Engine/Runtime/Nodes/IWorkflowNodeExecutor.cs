using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: контракт исполняемой workflow-ноды.
/// Зачем: отвязать runtime от switch-case по типам нод и поддержать auto-discovery.
/// Как: каждый executor объявляет Descriptor и реализует Execute(context).
/// </summary>
public interface IWorkflowNodeExecutor
{
    WorkflowNodeDescriptor Descriptor { get; }

    Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken);
}
