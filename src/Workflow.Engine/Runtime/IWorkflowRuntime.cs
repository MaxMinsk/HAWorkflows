using Workflow.Engine.Definitions;

namespace Workflow.Engine.Runtime;

/// <summary>
/// Что: абстракция движка исполнения workflow-графа.
/// Зачем: отделить runtime от API/persistence и упростить расширение.
/// Как: принимает definition + run request и возвращает детальный результат.
/// </summary>
public interface IWorkflowRuntime
{
    Task<WorkflowRunResult> ExecuteAsync(
        WorkflowDefinition definition,
        WorkflowRunRequest request,
        Func<WorkflowNodeRunResult, CancellationToken, Task>? onNodeStatusChanged,
        Func<WorkflowRuntimeCheckpoint, CancellationToken, Task>? onCheckpointCreated,
        CancellationToken cancellationToken);
}
