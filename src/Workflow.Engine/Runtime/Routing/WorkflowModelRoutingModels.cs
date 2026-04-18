namespace Workflow.Engine.Runtime.Routing;

/// <summary>
/// Что: входные данные для routing decision.
/// Зачем: runtime должен выбирать модель на основании stage/node_type и runtime-триггеров.
/// Как: node config заполняет stage/confidence/retry/budget, descriptor сообщает UsesModel.
/// </summary>
public sealed class WorkflowModelRoutingRequest
{
    public required string NodeId { get; init; }

    public required string NodeType { get; init; }

    public required string NodeName { get; init; }

    public required bool UsesModel { get; init; }

    public string? Stage { get; init; }

    public double? Confidence { get; init; }

    public int? RetryCount { get; init; }

    public double? BudgetRemaining { get; init; }
}

/// <summary>
/// Что: итоговое routing-решение для одной ноды.
/// Зачем: node output, logs и будущие metrics должны видеть один и тот же selected tier/model/reason.
/// Как: policy возвращает decision, runtime кладет его в WorkflowNodeExecutionContext.
/// </summary>
public sealed class WorkflowModelRoutingDecision
{
    public required string Stage { get; init; }

    public required string PolicyKey { get; init; }

    public required string SelectedTier { get; init; }

    public required string SelectedModel { get; init; }

    public string? SelectedAgentProfile { get; init; }

    public required string ThinkingMode { get; init; }

    public required string RouteReason { get; init; }

    public required WorkflowModelRoutingTriggerSnapshot TriggerSnapshot { get; init; }

    public bool UsesModel { get; init; }
}

/// <summary>
/// Что: snapshot сигналов routing на момент выбора.
/// Зачем: потом сравнивать pipeline profiles и объяснять, почему выбран cheap/medium/heavy route.
/// Как: значения приходят из node config/payload и пишутся в diagnostics.
/// </summary>
public sealed class WorkflowModelRoutingTriggerSnapshot
{
    public double? Confidence { get; init; }

    public int? RetryCount { get; init; }

    public double? BudgetRemaining { get; init; }
}

public static class WorkflowModelRouteReasons
{
    public const string Default = "default";
    public const string Escalation = "escalation";
    public const string Fallback = "fallback";
    public const string ForcedNoLlm = "forced_no_llm";
    public const string BudgetGuard = "budget_guard";
}

public static class WorkflowModelRoutingConstants
{
    public const string NoLlmTier = "no_llm";
    public const string NoLlmModel = "none";
    public const string NoThinking = "off";
}
