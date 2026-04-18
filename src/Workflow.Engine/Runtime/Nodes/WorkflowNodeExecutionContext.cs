using System.Text.Json.Nodes;
using Workflow.Engine.Definitions;
using Workflow.Engine.Runtime.Agents;
using Workflow.Engine.Runtime.Artifacts;
using Workflow.Engine.Runtime.Mcp;
using Workflow.Engine.Runtime.Routing;

namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: контекст выполнения конкретной ноды.
/// Зачем: передать executor-у входы, run-input и общий лог-буфер без связки с orchestration.
/// Как: runtime формирует immutable контекст и вызывает executor.Execute(...).
/// </summary>
public sealed class WorkflowNodeExecutionContext
{
    public required string RunId { get; init; }

    public required WorkflowNodeDefinition Node { get; init; }

    public required IReadOnlyList<JsonObject> InboundPayloads { get; init; }

    public IReadOnlyList<WorkflowNodePortValue> InboundPortValues { get; init; } = Array.Empty<WorkflowNodePortValue>();

    public required JsonObject RunInputPayload { get; init; }

    public required IWorkflowArtifactStore ArtifactStore { get; init; }

    public required IAgentExecutorCatalog AgentExecutorCatalog { get; init; }

    public required IMcpToolInvokerCatalog McpToolInvokerCatalog { get; init; }

    public required WorkflowModelRoutingDecision ModelRoute { get; init; }

    public required List<WorkflowExecutionLogItem> Logs { get; init; }
}

/// <summary>
/// Что: значение, пришедшее в ноду через конкретное edge/port-соединение.
/// Зачем: сохранить channel-aware контракт, пока legacy executor-ы продолжают работать с merged payload.
/// Как: для `data` channel Value хранится как envelope `{ kind, schemaVersion, payload }`.
/// </summary>
public sealed record WorkflowNodePortValue(
    string SourceNodeId,
    string SourcePort,
    string TargetPort,
    string Channel,
    JsonObject Value);
