namespace Workflow.Engine.Runtime.Mcp;

/// <summary>
/// Что: каталог MCP profiles и invoker-ов.
/// Зачем: один workflow graph должен переноситься между машинами, а реальные MCP endpoint-ы задаются локальным backend config.
/// Как: DefaultProfile/Profiles выбирают profile, Type выбирает registered IMcpToolInvoker.
/// </summary>
public sealed class McpToolInvokerCatalog : IMcpToolInvokerCatalog
{
    private readonly IReadOnlyDictionary<string, IMcpToolInvoker> _invokersByType;
    private readonly IMcpToolProfileSource _profileSource;

    public McpToolInvokerCatalog(IEnumerable<IMcpToolInvoker> invokers, IMcpToolProfileSource profileSource)
    {
        ArgumentNullException.ThrowIfNull(invokers);
        ArgumentNullException.ThrowIfNull(profileSource);

        _profileSource = profileSource;
        _invokersByType = invokers
            .GroupBy(invoker => invoker.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var items = group.ToArray();
                    if (items.Length > 1)
                    {
                        throw new InvalidOperationException($"Duplicate MCP invoker type '{group.Key}' registered.");
                    }

                    return items[0];
                },
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<McpToolListResult> ListToolsAsync(
        McpToolListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = ResolveProfile(request.ServerProfile);
        var effectiveRequest = new McpToolListRequest
        {
            ServerProfile = resolved.ProfileName,
            Timeout = request.Timeout
        };

        var result = await resolved.Invoker.ListToolsAsync(effectiveRequest, resolved.Profile, cancellationToken);
        return new McpToolListResult
        {
            ServerProfile = result.ServerProfile,
            ServerType = result.ServerType,
            Tools = FilterTools(result.Tools, resolved.Profile).ToArray(),
            Metadata = result.Metadata
        };
    }

    public Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = ResolveProfile(request.ServerProfile);
        EnsureToolAllowed(request.ToolName, resolved.Profile, resolved.ProfileName);

        var effectiveRequest = new McpToolCallRequest
        {
            RunId = request.RunId,
            NodeId = request.NodeId,
            ServerProfile = resolved.ProfileName,
            ToolName = request.ToolName,
            Arguments = request.Arguments,
            Timeout = request.Timeout
        };

        return resolved.Invoker.InvokeAsync(effectiveRequest, resolved.Profile, cancellationToken);
    }

    private ResolvedMcpProfile ResolveProfile(string? requestedProfile)
    {
        var options = _profileSource.GetSnapshot();
        var profileName = string.IsNullOrWhiteSpace(requestedProfile)
            ? options.DefaultProfile
            : requestedProfile.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName = "mock";
        }

        var profile = options.Profiles.TryGetValue(profileName, out var configuredProfile)
            ? configuredProfile
            : new McpServerProfileOptions { Type = profileName };
        if (!profile.Enabled)
        {
            throw new InvalidOperationException($"MCP profile '{profileName}' is disabled.");
        }

        var profileType = ResolveProfileType(profile);
        if (!_invokersByType.TryGetValue(profileType, out var invoker))
        {
            throw new InvalidOperationException(
                $"MCP invoker type '{profileType}' for profile '{profileName}' is not registered.");
        }

        return new ResolvedMcpProfile(profileName, profile, invoker);
    }

    private static string ResolveProfileType(McpServerProfileOptions profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Transport))
        {
            return profile.Transport.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profile.Type))
        {
            return profile.Type.Trim();
        }

        return "mock";
    }

    private static IEnumerable<McpToolDescriptor> FilterTools(
        IEnumerable<McpToolDescriptor> tools,
        McpServerProfileOptions profile)
    {
        var allowedTools = NormalizeToolSet(profile.AllowedTools);
        var blockedTools = NormalizeToolSet(profile.BlockedTools);
        foreach (var tool in tools)
        {
            if (allowedTools.Count > 0 && !allowedTools.Contains(tool.Name))
            {
                continue;
            }

            if (blockedTools.Contains(tool.Name))
            {
                continue;
            }

            yield return tool;
        }
    }

    private static void EnsureToolAllowed(string toolName, McpServerProfileOptions profile, string profileName)
    {
        var allowedTools = NormalizeToolSet(profile.AllowedTools);
        if (allowedTools.Count > 0 && !allowedTools.Contains(toolName))
        {
            throw new InvalidOperationException(
                $"MCP tool '{toolName}' is not allowed for profile '{profileName}'.");
        }

        var blockedTools = NormalizeToolSet(profile.BlockedTools);
        if (blockedTools.Contains(toolName))
        {
            throw new InvalidOperationException(
                $"MCP tool '{toolName}' is blocked for profile '{profileName}'.");
        }
    }

    private static HashSet<string> NormalizeToolSet(IEnumerable<string>? tools)
    {
        return tools?
            .Where(tool => !string.IsNullOrWhiteSpace(tool))
            .Select(tool => tool.Trim())
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private sealed record ResolvedMcpProfile(
        string ProfileName,
        McpServerProfileOptions Profile,
        IMcpToolInvoker Invoker);
}
