using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Definitions;
using Workflow.Engine.Validation;

namespace Workflow.Engine.Runtime;

/// <summary>
/// Что: детерминированный DAG runtime для MVP.
/// Зачем: исполнять граф в topological order с предсказуемыми node status/result.
/// Как: валидирует definition, выполняет ноды input/transform/log/output и возвращает timeline.
/// </summary>
public sealed class DeterministicWorkflowRuntime : IWorkflowRuntime
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly WorkflowDefinitionValidator _validator;

    public DeterministicWorkflowRuntime()
        : this(new WorkflowDefinitionValidator())
    {
    }

    public DeterministicWorkflowRuntime(WorkflowDefinitionValidator validator)
    {
        _validator = validator;
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

        var runId = Guid.NewGuid().ToString("N");
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
            runInputPayload = ParseInputPayload(request.InputJson);
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
            nodeResult.Status = WorkflowNodeRunStatus.Running;
            nodeResult.StartedAtUtc = DateTimeOffset.UtcNow;
            await ReportNodeStatusChangedAsync(nodeResult, onNodeStatusChanged, cancellationToken);

            try
            {
                var inboundEdges = incomingByTarget.GetValueOrDefault(nodeId) ?? Array.Empty<WorkflowEdgeDefinition>();
                var inboundPayloads = new List<JsonObject>(inboundEdges.Count);
                foreach (var edge in inboundEdges)
                {
                    if (!nodeOutputs.TryGetValue(edge.SourceNodeId, out var sourcePayload))
                    {
                        throw new InvalidOperationException(
                            $"Missing output for source node '{edge.SourceNodeId}'.");
                    }

                    inboundPayloads.Add(sourcePayload);
                }

                var outputPayload = ExecuteNode(node, inboundPayloads, runInputPayload, logs);
                nodeOutputs[nodeId] = outputPayload;
                nodeResult.OutputJson = outputPayload.ToJsonString(JsonSerializerOptions);
                nodeResult.Status = WorkflowNodeRunStatus.Succeeded;
                nodeResult.CompletedAtUtc = DateTimeOffset.UtcNow;
                await ReportNodeStatusChangedAsync(nodeResult, onNodeStatusChanged, cancellationToken);

                if (string.Equals(node.Type, "output", StringComparison.Ordinal))
                {
                    finalOutput = CloneObject(outputPayload);
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
                finalOutput = CloneObject(lastPayload);
            }
            else
            {
                finalOutput = CloneObject(runInputPayload);
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
                OutputJson = source.OutputJson
            },
            cancellationToken);
    }

    private static JsonObject ExecuteNode(
        WorkflowNodeDefinition node,
        IReadOnlyList<JsonObject> inboundPayloads,
        JsonObject runInputPayload,
        List<WorkflowExecutionLogItem> logs)
    {
        var payload = inboundPayloads.Count == 0
            ? CloneObject(runInputPayload)
            : MergePayloads(inboundPayloads);

        switch (node.Type)
        {
            case "input":
                ApplySetRemoveConfig(node.Config, payload);
                return payload;
            case "transform":
                ApplySetRemoveConfig(node.Config, payload);
                return payload;
            case "log":
            {
                ApplySetRemoveConfig(node.Config, payload);
                var message = TryGetConfigString(node.Config, "message") ?? $"Node '{node.Name}' executed.";
                logs.Add(new WorkflowExecutionLogItem
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    NodeId = node.Id,
                    Message = message
                });
                return payload;
            }
            case "output":
                ApplySetRemoveConfig(node.Config, payload);
                return payload;
            default:
                throw new InvalidOperationException($"Unsupported node type '{node.Type}'.");
        }
    }

    private static JsonObject ParseInputPayload(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return new JsonObject();
        }

        var parsedNode = JsonNode.Parse(inputJson);
        if (parsedNode is JsonObject parsedObject)
        {
            return CloneObject(parsedObject);
        }

        return new JsonObject
        {
            ["value"] = parsedNode?.DeepClone()
        };
    }

    private static JsonObject MergePayloads(IReadOnlyList<JsonObject> payloads)
    {
        var result = new JsonObject();
        foreach (var payload in payloads)
        {
            foreach (var (key, value) in payload)
            {
                result[key] = value?.DeepClone();
            }
        }

        return result;
    }

    private static JsonObject CloneObject(JsonObject payload)
    {
        return (JsonObject)payload.DeepClone();
    }

    private static string? TryGetConfigString(JsonElement config, string propertyName)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!config.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static void ApplySetRemoveConfig(JsonElement config, JsonObject payload)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (config.TryGetProperty("set", out var setObject) && setObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in setObject.EnumerateObject())
            {
                payload[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        if (config.TryGetProperty("remove", out var removeArray) && removeArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in removeArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    payload.Remove(item.GetString() ?? string.Empty);
                }
            }
        }
    }
}
