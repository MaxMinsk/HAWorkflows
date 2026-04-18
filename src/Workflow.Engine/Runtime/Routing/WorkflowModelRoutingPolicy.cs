namespace Workflow.Engine.Runtime.Routing;

/// <summary>
/// Что: config-driven stage-based routing policy.
/// Зачем: local pipeline может экономить токены, выбирая no-llm/cheap/medium/heavy по stage и trigger snapshot.
/// Как: deterministic-ноды получают forced_no_llm; model-ноды выбирают default/escalation/budget_guard по policy.
/// </summary>
public sealed class WorkflowModelRoutingPolicy : IWorkflowModelRoutingPolicy
{
    private readonly WorkflowModelRoutingOptions _options;

    public WorkflowModelRoutingPolicy(WorkflowModelRoutingOptions? options = null)
    {
        _options = options ?? new WorkflowModelRoutingOptions();
    }

    public WorkflowModelRoutingDecision Route(WorkflowModelRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stage = NormalizeStage(request.Stage, request.NodeType);
        var triggerSnapshot = new WorkflowModelRoutingTriggerSnapshot
        {
            Confidence = request.Confidence,
            RetryCount = request.RetryCount,
            BudgetRemaining = request.BudgetRemaining
        };

        if (!request.UsesModel)
        {
            return new WorkflowModelRoutingDecision
            {
                Stage = stage,
                PolicyKey = "deterministic",
                SelectedTier = WorkflowModelRoutingConstants.NoLlmTier,
                SelectedModel = WorkflowModelRoutingConstants.NoLlmModel,
                ThinkingMode = WorkflowModelRoutingConstants.NoThinking,
                RouteReason = WorkflowModelRouteReasons.ForcedNoLlm,
                TriggerSnapshot = triggerSnapshot,
                UsesModel = false
            };
        }

        var policyKey = ResolvePolicyKey(stage, request.NodeType);
        var policy = ResolvePolicy(policyKey);

        if (ShouldUseBudgetGuard(request, policy) &&
            HasRoute(policy.FallbackTier, policy.FallbackModel, policy.FallbackAgentProfile))
        {
            return CreateDecision(
                stage,
                policyKey,
                selectedTier: policy.FallbackTier ?? policy.DefaultTier,
                selectedModel: policy.FallbackModel ?? policy.DefaultModel,
                selectedAgentProfile: policy.FallbackAgentProfile ?? policy.DefaultAgentProfile,
                thinkingMode: policy.ThinkingMode,
                routeReason: WorkflowModelRouteReasons.BudgetGuard,
                triggerSnapshot);
        }

        if (ShouldEscalate(request, policy) &&
            HasRoute(policy.EscalationTier, policy.EscalationModel, policy.EscalationAgentProfile))
        {
            return CreateDecision(
                stage,
                policyKey,
                selectedTier: policy.EscalationTier ?? policy.DefaultTier,
                selectedModel: policy.EscalationModel ?? policy.DefaultModel,
                selectedAgentProfile: policy.EscalationAgentProfile ?? policy.DefaultAgentProfile,
                thinkingMode: policy.ThinkingMode,
                routeReason: WorkflowModelRouteReasons.Escalation,
                triggerSnapshot);
        }

        return CreateDecision(
            stage,
            policyKey,
            selectedTier: policy.DefaultTier,
            selectedModel: policy.DefaultModel,
            selectedAgentProfile: policy.DefaultAgentProfile,
            thinkingMode: policy.ThinkingMode,
            routeReason: WorkflowModelRouteReasons.Default,
            triggerSnapshot);
    }

    private WorkflowModelRoutingPolicyOptions ResolvePolicy(string policyKey)
    {
        if (_options.Policies.TryGetValue(policyKey, out var policy))
        {
            return policy;
        }

        if (!string.IsNullOrWhiteSpace(_options.DefaultPolicy) &&
            _options.Policies.TryGetValue(_options.DefaultPolicy, out var defaultPolicy))
        {
            return defaultPolicy;
        }

        return new WorkflowModelRoutingPolicyOptions();
    }

    private string ResolvePolicyKey(string stage, string nodeType)
    {
        if (_options.Policies.ContainsKey(stage))
        {
            return stage;
        }

        if (_options.Policies.ContainsKey(nodeType))
        {
            return nodeType;
        }

        return string.IsNullOrWhiteSpace(_options.DefaultPolicy)
            ? stage
            : _options.DefaultPolicy;
    }

    private static WorkflowModelRoutingDecision CreateDecision(
        string stage,
        string policyKey,
        string selectedTier,
        string selectedModel,
        string? selectedAgentProfile,
        string thinkingMode,
        string routeReason,
        WorkflowModelRoutingTriggerSnapshot triggerSnapshot)
    {
        return new WorkflowModelRoutingDecision
        {
            Stage = stage,
            PolicyKey = policyKey,
            SelectedTier = NormalizeValue(selectedTier, "medium"),
            SelectedModel = NormalizeValue(selectedModel, "echo"),
            SelectedAgentProfile = string.IsNullOrWhiteSpace(selectedAgentProfile) ? null : selectedAgentProfile.Trim(),
            ThinkingMode = NormalizeValue(thinkingMode, "auto"),
            RouteReason = routeReason,
            TriggerSnapshot = triggerSnapshot,
            UsesModel = true
        };
    }

    private static bool ShouldUseBudgetGuard(
        WorkflowModelRoutingRequest request,
        WorkflowModelRoutingPolicyOptions policy)
    {
        return request.BudgetRemaining is not null &&
               request.BudgetRemaining.Value <= policy.BudgetFallbackThreshold;
    }

    private static bool ShouldEscalate(
        WorkflowModelRoutingRequest request,
        WorkflowModelRoutingPolicyOptions policy)
    {
        return request.Confidence is not null &&
                   request.Confidence.Value < policy.ConfidenceEscalationThreshold ||
               request.RetryCount is not null &&
                   request.RetryCount.Value >= policy.RetryEscalationThreshold;
    }

    private static bool HasRoute(string? tier, string? model, string? agentProfile)
    {
        return !string.IsNullOrWhiteSpace(tier) ||
               !string.IsNullOrWhiteSpace(model) ||
               !string.IsNullOrWhiteSpace(agentProfile);
    }

    private static string NormalizeStage(string? stage, string nodeType)
    {
        return string.IsNullOrWhiteSpace(stage) ? nodeType.Trim() : stage.Trim();
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
