using Workflow.Engine.Definitions;

namespace Workflow.Engine.Validation;

/// <summary>
/// Что: валидатор workflow definition для runtime.
/// Зачем: гарантировать исполнимый DAG перед запуском.
/// Как: проверяет структуру, ссылки, дубликаты и циклы (Kahn).
/// </summary>
public sealed class WorkflowDefinitionValidator
{
    private static readonly HashSet<string> SupportedNodeTypes =
    [
        "input",
        "transform",
        "log",
        "output"
    ];

    public WorkflowDefinitionValidationResult Validate(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();
        var nodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);

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

            if (!SupportedNodeTypes.Contains(node.Type))
            {
                errors.Add($"{prefix}.type '{node.Type}' is not supported.");
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
}

