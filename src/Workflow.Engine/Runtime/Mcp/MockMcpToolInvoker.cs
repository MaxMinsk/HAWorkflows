using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: mock MCP invoker для локального smoke и демо-графов.
/// Зачем: `mcp_tool_call` можно проверять без настоящего Glean/Unity MCP сервера.
/// Как: возвращает JSON с toolName/profile/arguments, сохраняя тот же output shape, что real invoker.
/// </summary>
public sealed class MockMcpToolInvoker : IMcpToolInvoker
{
    public string Type => "mock";

    public Task<McpToolListResult> ListToolsAsync(
        McpToolListRequest request,
        McpServerProfileOptions profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new McpToolListResult
        {
            ServerProfile = request.ServerProfile,
            ServerType = Type,
            Tools =
            [
                new McpToolDescriptor
                {
                    Name = "echo",
                    Description = "Returns the provided arguments for smoke testing."
                },
                new McpToolDescriptor
                {
                    Name = "get_ticket",
                    Description = "Mock Jira ticket lookup."
                },
                new McpToolDescriptor
                {
                    Name = "unity_run_tests",
                    Description = "Mock Unity test run."
                }
            ],
            Metadata = new JsonObject
            {
                ["transport"] = "mock"
            }
        });
    }

    public Task<McpToolCallResult> InvokeAsync(
        McpToolCallRequest request,
        McpServerProfileOptions profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new JsonObject
        {
            ["mock"] = true,
            ["serverProfile"] = request.ServerProfile,
            ["toolName"] = request.ToolName,
            ["arguments"] = request.Arguments.DeepClone(),
            ["runId"] = request.RunId,
            ["nodeId"] = request.NodeId
        };

        return Task.FromResult(new McpToolCallResult
        {
            ServerProfile = request.ServerProfile,
            ServerType = Type,
            ToolName = request.ToolName,
            ResultJson = result.ToJsonString(),
            Metadata = new JsonObject
            {
                ["transport"] = "mock"
            }
        });
    }
}
