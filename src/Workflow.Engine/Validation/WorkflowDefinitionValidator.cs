using Workflow.Engine.Definitions;
using Workflow.Engine.Runtime.Nodes;

namespace Workflow.Engine.Validation;

/// <summary>
/// Что: валидатор workflow definition для runtime.
/// Зачем: гарантировать исполнимый DAG перед запуском.
/// Как: проверяет структуру, ссылки, дубликаты и циклы (Kahn).
/// </summary>
public sealed class WorkflowDefinitionValidator
{
    private readonly IReadOnlyDictionary<string, WorkflowNodeDescriptor> _nodeDescriptorsByType;

    public WorkflowDefinitionValidator()
        : this(new[]
        {
            new WorkflowNodeDescriptor("input", "Input", "Start signal", 0, 1, false),
            new WorkflowNodeDescriptor("transform", "Transform", "Deterministic mapping", 1, 1, false),
            new WorkflowNodeDescriptor("log", "Log", "Write execution log", 1, 1, false),
            new WorkflowNodeDescriptor("output", "Output", "Final result", 1, 0, false)
        })
    {
    }

    public WorkflowDefinitionValidator(IEnumerable<string> supportedNodeTypes)
        : this(supportedNodeTypes.Select(nodeType => new WorkflowNodeDescriptor(
            Type: nodeType,
            Label: nodeType,
            Description: string.Empty,
            Inputs: 1,
            Outputs: 1,
            IsLocal: false)))
    {
    }

    public WorkflowDefinitionValidator(IEnumerable<WorkflowNodeDescriptor> nodeDescriptors)
    {
        ArgumentNullException.ThrowIfNull(nodeDescriptors);
        _nodeDescriptorsByType = nodeDescriptors
            .GroupBy(descriptor => descriptor.Type, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
    }

    public WorkflowDefinitionValidationResult Validate(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();
        var nodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeDescriptorsById = new Dictionary<string, WorkflowNodeDescriptor>(StringComparer.Ordinal);

        if (!string.Equals(definition.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            errors.Add("schemaVersion must be '1.0'.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add("name must be a non-empty string.");
        }

        for (var i = 0; i < definition.Nodes.Count; i += 1)
        {
            var node = definition.Nodes[i];
            var prefix = $"nodes[{i}]";
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add($"{prefix}.id must be a non-empty string.");
                continue;
            }

            if (!nodeIds.Add(node.Id))
            {
                errors.Add($"{prefix}.id '{node.Id}' is duplicated.");
                continue;
            }

            nodeIndex[node.Id] = i;

            if (string.IsNullOrWhiteSpace(node.Name))
            {
                errors.Add($"{prefix}.name must be a non-empty string.");
            }

            if (!_nodeDescriptorsByType.TryGetValue(node.Type, out var nodeDescriptor))
            {
                errors.Add($"{prefix}.type '{node.Type}' is not supported.");
            }
            else
            {
                nodeDescriptorsById[node.Id] = nodeDescriptor;
            }
        }

        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        var indegree = nodeIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var adjacency = nodeIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        for (var i = 0; i < definition.Edges.Count; i += 1)
        {
            var edge = definition.Edges[i];
            var prefix = $"edges[{i}]";
            if (string.IsNullOrWhiteSpace(edge.Id))
            {
                errors.Add($"{prefix}.id must be a non-empty string.");
            }

            if (string.IsNullOrWhiteSpace(edge.SourceNodeId))
            {
                errors.Add($"{prefix}.sourceNodeId must be a non-empty string.");
            }

            if (string.IsNullOrWhiteSpace(edge.TargetNodeId))
            {
                errors.Add($"{prefix}.targetNodeId must be a non-empty string.");
            }

            if (!nodeIds.Contains(edge.SourceNodeId))
            {
                errors.Add($"{prefix}.sourceNodeId '{edge.SourceNodeId}' does not exist in nodes.");
            }

            if (!nodeIds.Contains(edge.TargetNodeId))
            {
                errors.Add($"{prefix}.targetNodeId '{edge.TargetNodeId}' does not exist in nodes.");
            }

            ValidatePortCompatibility(edge, prefix, nodeDescriptorsById, errors);

            if (string.Equals(edge.SourceNodeId, edge.TargetNodeId, StringComparison.Ordinal))
            {
                errors.Add($"{prefix} creates self-loop for node '{edge.SourceNodeId}'.");
            }

            var edgeKey = $"{edge.SourceNodeId}|{edge.SourcePort}|{edge.TargetNodeId}|{edge.TargetPort}";
            if (!edgeKeys.Add(edgeKey))
            {
                errors.Add($"{prefix} duplicates connection '{edgeKey}'.");
            }

            if (nodeIds.Contains(edge.SourceNodeId) && nodeIds.Contains(edge.TargetNodeId))
            {
                adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
                indegree[edge.TargetNodeId] += 1;
            }
        }

        if (errors.Count > 0)
        {
            return new WorkflowDefinitionValidationResult(errors, Array.Empty<string>());
        }

        var queue = new PriorityQueue<string, int>();
        foreach (var (nodeId, degree) in indegree)
        {
            if (degree == 0)
            {
                queue.Enqueue(nodeId, nodeIndex[nodeId]);
            }
        }

        var visited = 0;
        var topologicalOrder = new List<string>(definition.Nodes.Count);
        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            topologicalOrder.Add(nodeId);
            visited += 1;

            foreach (var next in adjacency[nodeId])
            {
                indegree[next] -= 1;
                if (indegree[next] == 0)
                {
                    queue.Enqueue(next, nodeIndex[next]);
                }
            }
        }

        if (visited != definition.Nodes.Count)
        {
            errors.Add("Graph must be acyclic. A cycle was detected.");
            return new WorkflowDefinitionValidationResult(errors, Array.Empty<string>());
        }

        return new WorkflowDefinitionValidationResult(Array.Empty<string>(), topologicalOrder);
    }

    private static void ValidatePortCompatibility(
        WorkflowEdgeDefinition edge,
        string prefix,
        IReadOnlyDictionary<string, WorkflowNodeDescriptor> nodeDescriptorsById,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(edge.SourcePort) || string.IsNullOrWhiteSpace(edge.TargetPort))
        {
            return;
        }

        if (!nodeDescriptorsById.TryGetValue(edge.SourceNodeId, out var sourceDescriptor) ||
            !nodeDescriptorsById.TryGetValue(edge.TargetNodeId, out var targetDescriptor))
        {
            return;
        }

        var sourcePort = sourceDescriptor
            .GetOutputPorts()
            .FirstOrDefault(port => string.Equals(port.Id, edge.SourcePort, StringComparison.Ordinal));
        if (sourcePort is null)
        {
            errors.Add($"{prefix}.sourcePort '{edge.SourcePort}' does not exist on node '{edge.SourceNodeId}' output ports.");
            return;
        }

        var targetPort = targetDescriptor
            .GetInputPorts()
            .FirstOrDefault(port => string.Equals(port.Id, edge.TargetPort, StringComparison.Ordinal));
        if (targetPort is null)
        {
            errors.Add($"{prefix}.targetPort '{edge.TargetPort}' does not exist on node '{edge.TargetNodeId}' input ports.");
            return;
        }

        if (!string.Equals(sourcePort.Channel, targetPort.Channel, StringComparison.Ordinal))
        {
            errors.Add(
                $"{prefix} connects incompatible channels: source {edge.SourceNodeId}.{sourcePort.Id} " +
                $"({sourcePort.Channel}) -> target {edge.TargetNodeId}.{targetPort.Id} ({targetPort.Channel}).");
        }
    }
}
