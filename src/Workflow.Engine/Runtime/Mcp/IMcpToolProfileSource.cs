namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: источник effective MCP profiles для runtime.
/// Зачем: deterministic-ноды не должны знать, пришли MCP settings из appsettings, mcp.json или будущего secret storage.
/// Как: catalog на каждый вызов берет snapshot и резолвит profile уже из него.
/// </summary>
public interface IMcpToolProfileSource
{
    McpToolInvokerOptions GetSnapshot();
}

/// <summary>
/// Что: статичный источник MCP profiles.
/// Зачем: сохранить простой fallback/headless режим, когда mcp.json еще не настроен.
/// Как: возвращает options, загруженные из appsettings/env на старте приложения.
/// </summary>
public sealed class StaticMcpToolProfileSource(McpToolInvokerOptions options) : IMcpToolProfileSource
{
    public McpToolInvokerOptions GetSnapshot() => options;
}
