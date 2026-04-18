namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: profile-aware facade для MCP tool calls.
/// Зачем: workflow-нода указывает `serverProfile`, а не transport/client implementation.
/// Как: catalog резолвит profile из backend config и делегирует вызов нужному IMcpToolInvoker.
/// </summary>
public interface IMcpToolInvokerCatalog
{
    Task<McpToolListResult> ListToolsAsync(McpToolListRequest request, CancellationToken cancellationToken);

    Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken);
}
