using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: запрос deterministic MCP tool call.
/// Зачем: workflow-нода должна вызывать конкретный tool без участия LLM и без provider-specific кода.
/// Как: `mcp_tool_call` собирает profile/tool/arguments и передает в MCP invoker catalog.
/// </summary>
public sealed class McpToolCallRequest
{
    public required string RunId { get; init; }

    public required string NodeId { get; init; }

    public required string ServerProfile { get; init; }

    public required string ToolName { get; init; }

    public JsonObject Arguments { get; init; } = new();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Что: запрос списка MCP tools для одного profile.
/// Зачем: Settings UI должен проверять подключение и показывать, какие tools доступны deterministic-ноду.
/// Как: catalog резолвит profile, открывает transport-specific session и возвращает нормализованные descriptors.
/// </summary>
public sealed class McpToolListRequest
{
    public required string ServerProfile { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Что: нормализованный результат MCP tool call.
/// Зачем: runtime/UI/следующие ноды должны получать одинаковый shape для mock, Glean, Unity MCP и других серверов.
/// Как: результат хранит сырой JSON result и metadata профиля/transport-а.
/// </summary>
public sealed class McpToolCallResult
{
    public required string ServerProfile { get; init; }

    public required string ServerType { get; init; }

    public required string ToolName { get; init; }

    public required string ResultJson { get; init; }

    public JsonObject Metadata { get; init; } = new();
}

/// <summary>
/// Что: нормализованный результат list-tools.
/// Зачем: UI и будущие ноды не должны зависеть от конкретного MCP transport/client type.
/// Как: invoker возвращает server metadata и компактный список tools.
/// </summary>
public sealed class McpToolListResult
{
    public required string ServerProfile { get; init; }

    public required string ServerType { get; init; }

    public IReadOnlyList<McpToolDescriptor> Tools { get; init; } = Array.Empty<McpToolDescriptor>();

    public JsonObject Metadata { get; init; } = new();
}

/// <summary>
/// Что: безопасный descriptor MCP tool для UI.
/// Зачем: показывать пользователю имя/описание tool без transport-specific объектов.
/// Как: Streamable HTTP и mock invoker маппят свои native tools в этот DTO.
/// </summary>
public sealed class McpToolDescriptor
{
    public required string Name { get; init; }

    public string? Description { get; init; }
}
