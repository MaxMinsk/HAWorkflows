using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: real MCP invoker через официальный ModelContextProtocol Streamable HTTP transport.
/// Зачем: deterministic workflow должен уметь вызывать Glean/Unity/другие MCP tools без LLM tool-calling.
/// Как: открывает короткую MCP session, находит tool по имени, вызывает InvokeAsync и сериализует результат.
/// </summary>
public sealed class StreamableHttpMcpToolInvoker : IMcpToolInvoker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public string Type => "streamable_http";

    public async Task<McpToolListResult> ListToolsAsync(
        McpToolListRequest request,
        McpServerProfileOptions profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var endpoint = ResolveEndpoint(request.ServerProfile, profile);
        using var httpClient = new HttpClient();
        await using var transport = new HttpClientTransport(
            CreateTransportOptions(request.ServerProfile, request.Timeout, profile, endpoint),
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);

        await using var client = await McpClient.CreateAsync(
            transport,
            CreateClientOptions(request.Timeout),
            NullLoggerFactory.Instance,
            cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return new McpToolListResult
        {
            ServerProfile = request.ServerProfile,
            ServerType = Type,
            Tools = tools
                .Select(tool => new McpToolDescriptor
                {
                    Name = tool.Name,
                    Description = tool.Description
                })
                .ToArray(),
            Metadata = new JsonObject
            {
                ["transport"] = Type,
                ["endpointHost"] = endpoint.Host,
                ["availableToolCount"] = tools.Count
            }
        };
    }

    public async Task<McpToolCallResult> InvokeAsync(
        McpToolCallRequest request,
        McpServerProfileOptions profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var endpoint = ResolveEndpoint(request.ServerProfile, profile);
        using var httpClient = new HttpClient();
        await using var transport = new HttpClientTransport(
            CreateTransportOptions(request.ServerProfile, request.Timeout, profile, endpoint),
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);

        await using var client = await McpClient.CreateAsync(
            transport,
            CreateClientOptions(request.Timeout),
            NullLoggerFactory.Instance,
            cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var tool = tools.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, request.ToolName, StringComparison.Ordinal));
        if (tool is null)
        {
            throw new InvalidOperationException(
                $"MCP tool '{request.ToolName}' was not found in profile '{request.ServerProfile}'.");
        }

        var arguments = ParseArguments(request.Arguments);
        var result = await tool.InvokeAsync(new AIFunctionArguments(arguments), cancellationToken);
        var resultJson = JsonSerializer.Serialize(result, JsonOptions);

        return new McpToolCallResult
        {
            ServerProfile = request.ServerProfile,
            ServerType = Type,
            ToolName = request.ToolName,
            ResultJson = resultJson,
            Metadata = new JsonObject
            {
                ["transport"] = Type,
                ["endpointHost"] = endpoint.Host,
                ["availableToolCount"] = tools.Count
            }
        };
    }

    private static Uri ResolveEndpoint(string serverProfile, McpServerProfileOptions profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Endpoint) ||
            !Uri.TryCreate(profile.Endpoint.Trim(), UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                $"MCP profile '{serverProfile}' has invalid or missing endpoint.");
        }

        return endpoint;
    }

    private static HttpClientTransportOptions CreateTransportOptions(
        string serverProfile,
        TimeSpan timeout,
        McpServerProfileOptions profile,
        Uri endpoint)
    {
        var headers = new Dictionary<string, string>(profile.Headers, StringComparer.OrdinalIgnoreCase);
        var bearerToken = ResolveBearerToken(profile);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            headers["Authorization"] = $"Bearer {bearerToken.Trim()}";
        }

        return new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = $"Workflow MCP {serverProfile}",
            ConnectionTimeout = timeout,
            AdditionalHeaders = headers
        };
    }

    private static McpClientOptions CreateClientOptions(TimeSpan timeout)
    {
        return new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "HAWorkflows",
                Version = "0.1.0"
            },
            InitializationTimeout = timeout
        };
    }

    private static string? ResolveBearerToken(McpServerProfileOptions profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.BearerTokenEnvironmentVariable))
        {
            var value = Environment.GetEnvironmentVariable(profile.BearerTokenEnvironmentVariable.Trim());
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return profile.BearerToken;
    }

    private static Dictionary<string, object?> ParseArguments(JsonObject arguments)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                   arguments.ToJsonString(),
                   JsonOptions)
               ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
