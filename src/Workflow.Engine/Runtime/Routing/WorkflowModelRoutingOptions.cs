namespace Workflow.Engine.Runtime.Routing;

/// <summary>
/// Что: настройки stage-based routing моделей.
/// Зачем: выбирать cheap/medium/heavy модель и thinking mode конфигом, а не кодом ноды.
/// Как: `Policies` индексируются по stage/node_type, runtime передает trigger snapshot и получает deterministic decision.
/// </summary>
public sealed class WorkflowModelRoutingOptions
{
    public string DefaultPolicy { get; init; } = "agent_task";

    public Dictionary<string, WorkflowModelRoutingPolicyOptions> Policies { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Что: policy одного stage или node_type.
/// Зачем: описать default/escalation/fallback маршруты и guardrails бюджета.
/// Как: routing выбирает route по confidence/retry_count/budget_remaining и возвращает selected tier/model/profile.
/// </summary>
public sealed class WorkflowModelRoutingPolicyOptions
{
    public string DefaultTier { get; init; } = "medium";

    public string DefaultModel { get; init; } = "echo";

    public string DefaultAgentProfile { get; init; } = "echo";

    public string ThinkingMode { get; init; } = "auto";

    public string? EscalationTier { get; init; }

    public string? EscalationModel { get; init; }

    public string? EscalationAgentProfile { get; init; }

    public string? FallbackTier { get; init; }

    public string? FallbackModel { get; init; }

    public string? FallbackAgentProfile { get; init; }

    public double ConfidenceEscalationThreshold { get; init; } = 0.6d;

    public int RetryEscalationThreshold { get; init; } = 2;

    public double BudgetFallbackThreshold { get; init; } = 0.1d;
}
