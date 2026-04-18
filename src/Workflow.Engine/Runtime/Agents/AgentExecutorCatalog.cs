using Workflow.Engine.Runtime.Routing;

namespace Workflow.Engine.Runtime.Agents;

/// <summary>
/// Что: profile-aware registry для agent adapter-ов.
/// Зачем: `agent_task` должен выбирать adapter по profile из config, не создавая зависимость от конкретного provider-а.
/// Как: строит словарь adapterName->IAgentExecutor и резолвит WorkflowAgents:Profiles.
/// </summary>
public sealed class AgentExecutorCatalog : IAgentExecutorCatalog
{
    private readonly AgentExecutorOptions _options;
    private readonly IReadOnlyDictionary<string, IAgentExecutor> _executorsByAdapter;

    public AgentExecutorCatalog(IEnumerable<IAgentExecutor> executors, AgentExecutorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(executors);

        _options = options ?? new AgentExecutorOptions();
        _executorsByAdapter = executors
            .GroupBy(executor => executor.AdapterName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var items = group.ToArray();
                    if (items.Length > 1)
                    {
                        throw new InvalidOperationException(
                            $"Duplicate agent adapter '{group.Key}' registered.");
                    }

                    return items[0];
                },
                StringComparer.OrdinalIgnoreCase);
    }

    public AgentExecutorResolution Resolve(string? profileName, WorkflowModelRoutingDecision? routingDecision = null)
    {
        var resolvedProfileName = string.IsNullOrWhiteSpace(profileName)
            ? _options.DefaultProfile
            : profileName.Trim();

        if (string.IsNullOrWhiteSpace(resolvedProfileName))
        {
            resolvedProfileName = "echo";
        }

        var profile = _options.Profiles.TryGetValue(resolvedProfileName, out var configuredProfile)
            ? configuredProfile
            : new AgentProfileOptions { Adapter = resolvedProfileName };
        var adapterName = string.IsNullOrWhiteSpace(profile.Adapter)
            ? resolvedProfileName
            : profile.Adapter.Trim();

        if (!_executorsByAdapter.TryGetValue(adapterName, out var executor))
        {
            throw new InvalidOperationException(
                $"Agent adapter '{adapterName}' for profile '{resolvedProfileName}' is not registered.");
        }

        return new AgentExecutorResolution(
            resolvedProfileName,
            adapterName,
            executor,
            routingDecision ?? CreateFallbackRoutingDecision(resolvedProfileName));
    }

    private static WorkflowModelRoutingDecision CreateFallbackRoutingDecision(string profileName)
    {
        return new WorkflowModelRoutingDecision
        {
            Stage = profileName,
            PolicyKey = profileName,
            SelectedTier = "medium",
            SelectedModel = profileName,
            SelectedAgentProfile = profileName,
            ThinkingMode = "auto",
            RouteReason = WorkflowModelRouteReasons.Default,
            TriggerSnapshot = new WorkflowModelRoutingTriggerSnapshot(),
            UsesModel = true
        };
    }
}
