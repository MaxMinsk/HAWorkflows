using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Definitions;
using Workflow.Engine.Runtime.Agents;
using Workflow.Engine.Runtime.Artifacts;
using Workflow.Engine.Runtime.Mcp;
using Workflow.Engine.Runtime.Nodes;
using Workflow.Engine.Runtime.Routing;
using Workflow.Engine.Validation;

namespace Workflow.Engine.Runtime;

/// <summary>
/// Что: детерминированный DAG runtime для MVP.
/// Зачем: исполнять граф в topological order с предсказуемыми node status/result.
/// Как: валидирует definition, исполняет ноды через runtime-каталог и возвращает timeline.
/// </summary>
public sealed class DeterministicWorkflowRuntime : IWorkflowRuntime
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly WorkflowDefinitionValidator _validator;
    private readonly IWorkflowNodeCatalog _nodeCatalog;
    private readonly IWorkflowArtifactStore _artifactStore;
    private readonly IAgentExecutorCatalog _agentExecutorCatalog;
    private readonly IMcpToolInvokerCatalog _mcpToolInvokerCatalog;
    private readonly IWorkflowModelRoutingPolicy _modelRoutingPolicy;

    public DeterministicWorkflowRuntime(
        WorkflowDefinitionValidator validator,
        IWorkflowNodeCatalog nodeCatalog,
        IWorkflowArtifactStore artifactStore,
        IAgentExecutorCatalog agentExecutorCatalog,
        IMcpToolInvokerCatalog mcpToolInvokerCatalog,
        IWorkflowModelRoutingPolicy modelRoutingPolicy)
    {
        _validator = validator;
        _nodeCatalog = nodeCatalog;
        _artifactStore = artifactStore;
        _agentExecutorCatalog = agentExecutorCatalog;
        _mcpToolInvokerCatalog = mcpToolInvokerCatalog;
        _modelRoutingPolicy = modelRoutingPolicy;
    }

    public async Task<WorkflowRunResult> ExecuteAsync(
        WorkflowDefinition definition,
        WorkflowRunRequest request,
        Func<WorkflowNodeRunResult, CancellationToken, Task>? onNodeStatusChanged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);

        var runId = string.IsNullOrWhiteSpace(request.RunId)
            ? Guid.NewGuid().ToString("N")
            : request.RunId.Trim();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var nodeResults = definition.Nodes
            .Select(node => new WorkflowNodeRunResult
            {
                NodeId = node.Id,
                NodeType = node.Type,
                NodeName = node.Name,
                Status = WorkflowNodeRunStatus.Pending
            })
            .ToDictionary(result => result.NodeId, StringComparer.Ordinal);
        var logs = new List<WorkflowExecutionLogItem>();

        var validationResult = _validator.Validate(definition);
        if (!validationResult.IsValid)
        {
            return new WorkflowRunResult
            {
                RunId = runId,
                WorkflowName = definition.Name,
                Status = WorkflowRunStatus.Failed,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Error = string.Join(" ", validationResult.Errors),
                NodeResults = nodeResults.Values.ToArray(),
                Logs = logs
            };
        }

        JsonObject runInputPayload;
        try
        {
            runInputPayload = WorkflowNodePayloadOperations.ParseInputPayload(request.InputJson);
        }
        catch (Exception exception)
        {
            return new WorkflowRunResult
            {
                RunId = runId,
                WorkflowName = definition.Name,
                Status = WorkflowRunStatus.Failed,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Error = $"Invalid run input payload: {exception.Message}",
                NodeResults = nodeResults.Values.ToArray(),
                Logs = logs
            };
        }

        var nodeById = definition.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var nodeOrderIndex = definition.Nodes
            .Select((node, index) => new { node.Id, index })
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        var incomingByTarget = definition.Edges
            .GroupBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WorkflowEdgeDefinition>)group
                    .OrderBy(edge => nodeOrderIndex[edge.SourceNodeId])
                    .ThenBy(edge => edge.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var nodeOutputs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        JsonObject? finalOutput = null;
        string? runError = null;

        foreach (var nodeId in validationResult.TopologicalOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodeById[nodeId];
            var nodeResult = nodeResults[nodeId];
            var modelRoute = CreateModelRoute(node);
            ApplyModelRoute(nodeResult, modelRoute);
            logs.Add(CreateModelRoutingLog(node.Id, modelRoute));

            nodeResult.Status = WorkflowNodeRunStatus.Running;
            nodeResult.StartedAtUtc = DateTimeOffset.UtcNow;
            await ReportNodeStatusChangedAsync(nodeResult, onNodeStatusChanged, cancellationToken);

            try
            {
                var inboundEdges = incomingByTarget.GetValueOrDefault(nodeId) ?? Array.Empty<WorkflowEdgeDefinition>();
                var inboundPayloads = new List<JsonObject>(inboundEdges.Count);
                var inboundPortValues = new List<WorkflowNodePortValue>(inboundEdges.Count);
                foreach (var edge in inboundEdges)
                {
                    if (!nodeOutputs.TryGetValue(edge.SourceNodeId, out var sourcePayload))
                    {
                        throw new InvalidOperationException(
                            $"Missing output for source node '{edge.SourceNodeId}'.");
                    }

                    var portValue = CreateInboundPortValue(definition, edge, sourcePayload);
                    inboundPortValues.Add(portValue);
                    inboundPayloads.Add(string.Equals(portValue.Channel, WorkflowPortChannels.Data, StringComparison.Ordinal)
                        ? WorkflowNodePayloadOperations.ReadDataEnvelopePayload(portValue.Value)
                        : WorkflowNodePayloadOperations.CloneObject(portValue.Value));
                }

                var outputPayload = await ExecuteNodeAsync(
                    runId,
                    node,
                    inboundPayloads,
                    inboundPortValues,
                    runInputPayload,
                    modelRoute,
                    logs,
                    cancellationToken);
                nodeOutputs[nodeId] = outputPayload;
                nodeResult.OutputJson = outputPayload.ToJsonString(JsonSerializerOptions);
                nodeResult.Status = WorkflowNodeRunStatus.Succeeded;
                nodeResult.CompletedAtUtc = DateTimeOffset.UtcNow;
                await ReportNodeStatusChangedAsync(nodeResult, onNodeStatusChanged, cancellationToken);

                if (_nodeCatalog.TryGetExecutor(node.Type, out var nodeExecutor) && nodeExecutor.Descriptor.ProducesRunOutput)
                {
                    finalOutput = WorkflowNodePayloadOperations.CloneObject(outputPayload);
                }
            }
            catch (Exception exception)
            {
                nodeResult.Status = WorkflowNodeRunStatus.Failed;
                nodeResult.Error = exception.Message;
                nodeResult.CompletedAtUtc = DateTimeOffset.UtcNow;
                await ReportNodeStatusChangedAsync(nodeResult, onNodeStatusChanged, cancellationToken);
                runError = $"Node '{node.Name}' ({node.Id}) failed: {exception.Message}";
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(runError))
        {
            foreach (var result in nodeResults.Values.Where(result => result.Status == WorkflowNodeRunStatus.Pending))
            {
                result.Status = WorkflowNodeRunStatus.Skipped;
                result.CompletedAtUtc = DateTimeOffset.UtcNow;
                await ReportNodeStatusChangedAsync(result, onNodeStatusChanged, cancellationToken);
            }

            return new WorkflowRunResult
            {
                RunId = runId,
                WorkflowName = definition.Name,
                Status = WorkflowRunStatus.Failed,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Error = runError,
                Artifacts = _artifactStore.ListRunArtifacts(runId),
                NodeResults = nodeResults.Values.ToArray(),
                Logs = logs
            };
        }

        if (finalOutput is null)
        {
            var lastExecutedNodeId = validationResult.TopologicalOrder.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(lastExecutedNodeId) &&
                nodeOutputs.TryGetValue(lastExecutedNodeId, out var lastPayload))
            {
                finalOutput = WorkflowNodePayloadOperations.CloneObject(lastPayload);
            }
            else
            {
                finalOutput = WorkflowNodePayloadOperations.CloneObject(runInputPayload);
            }
        }

        return new WorkflowRunResult
        {
            RunId = runId,
            WorkflowName = definition.Name,
            Status = WorkflowRunStatus.Succeeded,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            OutputJson = finalOutput.ToJsonString(JsonSerializerOptions),
            Artifacts = _artifactStore.ListRunArtifacts(runId),
            NodeResults = nodeResults.Values.ToArray(),
            Logs = logs
        };
    }

    private static async Task ReportNodeStatusChangedAsync(
        WorkflowNodeRunResult source,
        Func<WorkflowNodeRunResult, CancellationToken, Task>? onNodeStatusChanged,
        CancellationToken cancellationToken)
    {
        if (onNodeStatusChanged is null)
        {
            return;
        }

        await onNodeStatusChanged(
            new WorkflowNodeRunResult
            {
                NodeId = source.NodeId,
                NodeType = source.NodeType,
                NodeName = source.NodeName,
                Status = source.Status,
                StartedAtUtc = source.StartedAtUtc,
                CompletedAtUtc = source.CompletedAtUtc,
                Error = source.Error,
                OutputJson = source.OutputJson,
                RoutingStage = source.RoutingStage,
                SelectedTier = source.SelectedTier,
                SelectedModel = source.SelectedModel,
                ThinkingMode = source.ThinkingMode,
                RouteReason = source.RouteReason,
                RoutingConfidence = source.RoutingConfidence,
                RoutingRetryCount = source.RoutingRetryCount,
                RoutingBudgetRemaining = source.RoutingBudgetRemaining
            },
            cancellationToken);
    }

    private Task<JsonObject> ExecuteNodeAsync(
        string runId,
        WorkflowNodeDefinition node,
        IReadOnlyList<JsonObject> inboundPayloads,
        IReadOnlyList<WorkflowNodePortValue> inboundPortValues,
        JsonObject runInputPayload,
        WorkflowModelRoutingDecision modelRoute,
        List<WorkflowExecutionLogItem> logs,
        CancellationToken cancellationToken)
    {
        if (!_nodeCatalog.TryGetExecutor(node.Type, out var executor))
        {
            throw new InvalidOperationException($"Unsupported node type '{node.Type}'.");
        }

        return executor.ExecuteAsync(new WorkflowNodeExecutionContext
        {
            RunId = runId,
            Node = node,
            InboundPayloads = inboundPayloads,
            InboundPortValues = inboundPortValues,
            RunInputPayload = runInputPayload,
            ArtifactStore = _artifactStore,
            AgentExecutorCatalog = _agentExecutorCatalog,
            McpToolInvokerCatalog = _mcpToolInvokerCatalog,
            ModelRoute = modelRoute,
            Logs = logs
        }, cancellationToken);
    }

    private WorkflowModelRoutingDecision CreateModelRoute(WorkflowNodeDefinition node)
    {
        var usesModel = _nodeCatalog.TryGetExecutor(node.Type, out var executor) &&
                        executor.Descriptor.UsesModel;

        return _modelRoutingPolicy.Route(new WorkflowModelRoutingRequest
        {
            NodeId = node.Id,
            NodeType = node.Type,
            NodeName = node.Name,
            UsesModel = usesModel,
            Stage = ReadConfigString(node.Config, "stage") ??
                    ReadConfigString(node.Config, "routingStage") ??
                    ReadConfigString(node.Config, "modelStage"),
            Confidence = ReadConfigDouble(node.Config, "confidence"),
            RetryCount = ReadConfigInt(node.Config, "retryCount") ??
                         ReadConfigInt(node.Config, "retry_count"),
            BudgetRemaining = ReadConfigDouble(node.Config, "budgetRemaining") ??
                              ReadConfigDouble(node.Config, "budget_remaining")
        });
    }

    private static void ApplyModelRoute(
        WorkflowNodeRunResult nodeResult,
        WorkflowModelRoutingDecision decision)
    {
        nodeResult.RoutingStage = decision.Stage;
        nodeResult.SelectedTier = decision.SelectedTier;
        nodeResult.SelectedModel = decision.SelectedModel;
        nodeResult.ThinkingMode = decision.ThinkingMode;
        nodeResult.RouteReason = decision.RouteReason;
        nodeResult.RoutingConfidence = decision.TriggerSnapshot.Confidence;
        nodeResult.RoutingRetryCount = decision.TriggerSnapshot.RetryCount;
        nodeResult.RoutingBudgetRemaining = decision.TriggerSnapshot.BudgetRemaining;
    }

    private static WorkflowExecutionLogItem CreateModelRoutingLog(
        string nodeId,
        WorkflowModelRoutingDecision decision)
    {
        return new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = nodeId,
            Message = $"Model routing: stage '{decision.Stage}', policy '{decision.PolicyKey}', tier '{decision.SelectedTier}', model '{decision.SelectedModel}', thinking '{decision.ThinkingMode}', reason '{decision.RouteReason}', confidence '{FormatNullable(decision.TriggerSnapshot.Confidence)}', retry_count '{FormatNullable(decision.TriggerSnapshot.RetryCount)}', budget_remaining '{FormatNullable(decision.TriggerSnapshot.BudgetRemaining)}'."
        };
    }

    private static string? ReadConfigString(JsonElement config, string propertyName)
    {
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static double? ReadConfigDouble(JsonElement config, string propertyName)
    {
        var value = ReadConfigString(config, propertyName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ReadConfigInt(JsonElement config, string propertyName)
    {
        var value = ReadConfigString(config, propertyName);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string FormatNullable(double? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "null";
    }

    private static string FormatNullable(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "null";
    }

    private WorkflowNodePortValue CreateInboundPortValue(
        WorkflowDefinition definition,
        WorkflowEdgeDefinition edge,
        JsonObject sourcePayload)
    {
        var channel = ResolveSourcePortChannel(definition, edge);
        var value = string.Equals(channel, WorkflowPortChannels.Data, StringComparison.Ordinal)
            ? WorkflowNodePayloadOperations.CreateDataEnvelope(
                kind: $"{edge.SourceNodeId}.{edge.SourcePort}",
                payload: sourcePayload)
            : WorkflowNodePayloadOperations.CloneObject(sourcePayload);

        return new WorkflowNodePortValue(
            SourceNodeId: edge.SourceNodeId,
            SourcePort: edge.SourcePort,
            TargetPort: edge.TargetPort,
            Channel: channel,
            Value: value);
    }

    private string ResolveSourcePortChannel(WorkflowDefinition definition, WorkflowEdgeDefinition edge)
    {
        var sourceNode = definition.Nodes.FirstOrDefault(node =>
            string.Equals(node.Id, edge.SourceNodeId, StringComparison.Ordinal));
        if (sourceNode is null || !_nodeCatalog.TryGetExecutor(sourceNode.Type, out var executor))
        {
            return WorkflowPortChannels.Data;
        }

        var sourcePort = executor.Descriptor
            .GetOutputPorts()
            .FirstOrDefault(port => string.Equals(port.Id, edge.SourcePort, StringComparison.Ordinal));
        return sourcePort?.Channel ?? WorkflowPortChannels.Data;
    }
}
