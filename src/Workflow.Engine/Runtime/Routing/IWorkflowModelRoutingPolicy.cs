namespace Workflow.Engine.Runtime.Routing;

/// <summary>
/// Что: контракт stage-based routing policy.
/// Зачем: runtime и node executors должны зависеть от абстракции, а не от конкретной config-схемы.
/// Как: принимает request по ноде и возвращает selected tier/model/profile/reason.
/// </summary>
public interface IWorkflowModelRoutingPolicy
{
    WorkflowModelRoutingDecision Route(WorkflowModelRoutingRequest request);
}
