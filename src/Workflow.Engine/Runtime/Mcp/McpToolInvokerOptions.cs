using System.Text.Json.Serialization;

namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: backend-конфигурация MCP profiles.
/// Зачем: graph JSON не должен хранить endpoint-ы, tokens и transport details.
/// Как: node config выбирает `serverProfile`, а catalog получает effective options из IMcpToolProfileSource.
/// </summary>
public sealed class McpToolInvokerOptions
{
    public string DefaultProfile { get; init; } = "mock";

    [JsonIgnore]
    public string? ConfigPath { get; init; }

    public Dictionary<string, McpServerProfileOptions> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Что: настройки одного MCP server profile.
/// Зачем: поддержать mock для smoke и real streamable HTTP MCP для Glean/Unity/других tools.
/// Как: Type=`mock` или `streamable_http`; secrets можно передать через BearerTokenEnvironmentVariable.
/// </summary>
public sealed class McpServerProfileOptions
{
    public bool Enabled { get; init; } = true;

    public string Type { get; init; } = "mock";

    public string? Transport { get; init; }

    public string? Endpoint { get; init; }

    public string? BearerToken { get; init; }

    public string? BearerTokenEnvironmentVariable { get; init; }

    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BlockedTools { get; init; } = Array.Empty<string>();

    public int TimeoutSeconds { get; init; } = 30;
}
