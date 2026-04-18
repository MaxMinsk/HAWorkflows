using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Agents;

/// <summary>
/// Что: встроенный smoke adapter для agent contract.
/// Зачем: проверить весь путь `AgentTask node -> IAgentExecutor` без зависимости от Cursor/Claude API.
/// Как: возвращает prompt и краткую диагностику; task-mode хранит результат в памяти singleton-а.
/// </summary>
public sealed class EchoAgentExecutor : IAgentExecutor
{
    private readonly ConcurrentDictionary<string, AgentTaskResult> _taskResults = new(StringComparer.Ordinal);

    public string AdapterName => "echo";

    public Task<AgentAskResult> AskAsync(AgentAskRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AgentAskResult
        {
            Text = BuildEchoText("ask", request.Prompt),
            Metadata = new JsonObject
            {
                ["adapter"] = AdapterName,
                ["profile"] = request.Profile,
                ["selectedTier"] = request.SelectedTier,
                ["selectedModel"] = request.SelectedModel,
                ["thinkingMode"] = request.ThinkingMode,
                ["routeReason"] = request.RouteReason,
                ["runId"] = request.RunId,
                ["nodeId"] = request.NodeId,
                ["inputFields"] = request.Input.Count
            }
        });
    }

    public Task<AgentTaskHandle> CreateTaskAsync(AgentTaskCreateRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var taskId = $"{request.RunId}:{request.NodeId}:{Guid.NewGuid():N}";
        _taskResults[taskId] = new AgentTaskResult
        {
            TaskId = taskId,
            Text = BuildEchoText("task", request.Prompt),
            Metadata = new JsonObject
            {
                ["adapter"] = AdapterName,
                ["profile"] = request.Profile,
                ["selectedTier"] = request.SelectedTier,
                ["selectedModel"] = request.SelectedModel,
                ["thinkingMode"] = request.ThinkingMode,
                ["routeReason"] = request.RouteReason,
                ["runId"] = request.RunId,
                ["nodeId"] = request.NodeId,
                ["inputFields"] = request.Input.Count
            }
        };

        return Task.FromResult(new AgentTaskHandle
        {
            TaskId = taskId,
            Status = "succeeded",
            Metadata = new JsonObject
            {
                ["adapter"] = AdapterName
            }
        });
    }

    public Task<AgentTaskStatusResult> GetStatusAsync(AgentTaskStatusRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = _taskResults.ContainsKey(request.TaskId) ? "succeeded" : "not_found";
        return Task.FromResult(new AgentTaskStatusResult
        {
            TaskId = request.TaskId,
            Status = status,
            Metadata = new JsonObject
            {
                ["adapter"] = AdapterName,
                ["profile"] = request.Profile
            }
        });
    }

    public Task<AgentTaskResult> GetResultAsync(AgentTaskResultRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_taskResults.TryGetValue(request.TaskId, out var result))
        {
            throw new InvalidOperationException($"Agent task '{request.TaskId}' was not found.");
        }

        return Task.FromResult(result);
    }

    private static string BuildEchoText(string mode, string prompt)
    {
        return $"Echo agent ({mode}) received prompt:{Environment.NewLine}{prompt}";
    }
}
