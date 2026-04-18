namespace Workflow.Engine.Runtime.Agents;

/// <summary>
/// Что: provider-neutral контракт agent adapter-а.
/// Зачем: workflow runtime не должен знать, кто исполняет задачу: Cursor, Claude Code, локальный CLI или test adapter.
/// Как: AgentTask node выбирает profile, catalog резолвит adapter, затем вызывает Ask/CreateTask/GetStatus/GetResult.
/// </summary>
public interface IAgentExecutor
{
    string AdapterName { get; }

    Task<AgentAskResult> AskAsync(AgentAskRequest request, CancellationToken cancellationToken);

    Task<AgentTaskHandle> CreateTaskAsync(AgentTaskCreateRequest request, CancellationToken cancellationToken);

    Task<AgentTaskStatusResult> GetStatusAsync(AgentTaskStatusRequest request, CancellationToken cancellationToken);

    Task<AgentTaskResult> GetResultAsync(AgentTaskResultRequest request, CancellationToken cancellationToken);
}
