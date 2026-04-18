namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: transport-specific MCP tool invoker.
/// Зачем: node/runtime не должны знать, это mock, Streamable HTTP, будущий stdio или корпоративный proxy.
/// Как: catalog выбирает invoker по profile.Type и передает ему request + resolved profile options.
/// </summary>
public interface IMcpToolInvoker
{
    string Type { get; }

    Task<McpToolListResult> ListToolsAsync(
        McpToolListRequest request,
        McpServerProfileOptions profile,
        CancellationToken cancellationToken);

    Task<McpToolCallResult> InvokeAsync(
        McpToolCallRequest request,
        McpServerProfileOptions profile,
        CancellationToken cancellationToken);
}
