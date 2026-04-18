using Workflow.Engine.Runtime.Routing;

namespace Workflow.Engine.Runtime.Agents;

/// <summary>
/// Что: реестр agent profiles и adapter-ов.
/// Зачем: node config хранит профиль (`cheap_discover`, `heavy_plan`, `echo`), а не конкретный класс adapter-а.
/// Как: catalog читает WorkflowAgents options и возвращает executor вместе с resolved profile metadata.
/// </summary>
public interface IAgentExecutorCatalog
{
    AgentExecutorResolution Resolve(string? profileName, WorkflowModelRoutingDecision? routingDecision = null);
}

public sealed record AgentExecutorResolution(
    string ProfileName,
    string AdapterName,
    IAgentExecutor Executor,
    WorkflowModelRoutingDecision RoutingDecision);
