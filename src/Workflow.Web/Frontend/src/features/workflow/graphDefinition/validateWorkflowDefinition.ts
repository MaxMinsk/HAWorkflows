import type { ValidationResult, WorkflowDefinition, WorkflowEdge, WorkflowNode } from "../../../shared/types/workflow";

/**
 * Что: структурная и графовая валидация workflow definition.
 * Зачем: гарантировать корректный payload перед сохранением/запуском.
 * Как: проверяет schema, типы, ссылки и ацикличность DAG.
 */
export function validateWorkflowDefinition(definition: WorkflowDefinition, supportedNodeTypes: string[]): ValidationResult {
  const errors: string[] = [];
  const allowedNodeTypes = new Set(supportedNodeTypes);

  if (definition?.schemaVersion !== "1.0") {
    errors.push("schemaVersion must be '1.0'.");
  }

  if (!Array.isArray(definition?.nodes)) {
    errors.push("nodes must be an array.");
    return { isValid: false, errors };
  }

  if (!Array.isArray(definition?.edges)) {
    errors.push("edges must be an array.");
    return { isValid: false, errors };
  }

  const nodeIds = validateNodes(definition.nodes, allowedNodeTypes, errors);
  validateEdges(definition.edges, nodeIds, errors);

  return {
    isValid: errors.length === 0,
    errors
  };
}

function validateNodes(nodes: WorkflowNode[], allowedNodeTypes: Set<string>, errors: string[]): Set<string> {
  const nodeIds = new Set<string>();

  nodes.forEach((node, index) => {
    const prefix = `nodes[${index}]`;
    if (!node || typeof node !== "object") {
      errors.push(`${prefix} must be an object.`);
      return;
    }

    if (typeof node.id !== "string" || node.id.trim() === "") {
      errors.push(`${prefix}.id must be a non-empty string.`);
    } else if (nodeIds.has(node.id)) {
      errors.push(`${prefix}.id '${node.id}' is duplicated.`);
    } else {
      nodeIds.add(node.id);
    }

    if (typeof node.type !== "string" || !allowedNodeTypes.has(node.type)) {
      errors.push(`${prefix}.type '${node.type}' is not supported.`);
    }

    if (typeof node.name !== "string" || node.name.trim() === "") {
      errors.push(`${prefix}.name must be a non-empty string.`);
    }

    const isConfigObject =
      node.config !== null &&
      typeof node.config === "object" &&
      !Array.isArray(node.config);
    if (!isConfigObject) {
      errors.push(`${prefix}.config must be a JSON object.`);
    }
  });

  return nodeIds;
}

function validateEdges(edges: WorkflowEdge[], nodeIds: Set<string>, errors: string[]): void {
  const edgeKeys = new Set<string>();
  const indegree = new Map<string, number>();
  const adjacency = new Map<string, string[]>();

  nodeIds.forEach((id) => {
    indegree.set(id, 0);
    adjacency.set(id, []);
  });

  edges.forEach((edge, index) => {
    const prefix = `edges[${index}]`;
    if (!edge || typeof edge !== "object") {
      errors.push(`${prefix} must be an object.`);
      return;
    }

    if (typeof edge.sourceNodeId !== "string" || edge.sourceNodeId.trim() === "") {
      errors.push(`${prefix}.sourceNodeId must be a non-empty string.`);
    }

    if (typeof edge.targetNodeId !== "string" || edge.targetNodeId.trim() === "") {
      errors.push(`${prefix}.targetNodeId must be a non-empty string.`);
    }

    if (typeof edge.sourcePort !== "string" || edge.sourcePort.trim() === "") {
      errors.push(`${prefix}.sourcePort must be a non-empty string.`);
    }

    if (typeof edge.targetPort !== "string" || edge.targetPort.trim() === "") {
      errors.push(`${prefix}.targetPort must be a non-empty string.`);
    }

    if (!nodeIds.has(edge.sourceNodeId)) {
      errors.push(`${prefix}.sourceNodeId '${edge.sourceNodeId}' does not exist in nodes.`);
    }

    if (!nodeIds.has(edge.targetNodeId)) {
      errors.push(`${prefix}.targetNodeId '${edge.targetNodeId}' does not exist in nodes.`);
    }

    if (edge.sourceNodeId === edge.targetNodeId) {
      errors.push(`${prefix} creates self-loop for node '${edge.sourceNodeId}'.`);
    }

    const edgeKey = `${edge.sourceNodeId}|${edge.sourcePort}|${edge.targetNodeId}|${edge.targetPort}`;
    if (edgeKeys.has(edgeKey)) {
      errors.push(`${prefix} duplicates connection '${edgeKey}'.`);
    } else {
      edgeKeys.add(edgeKey);
    }

    if (nodeIds.has(edge.sourceNodeId) && nodeIds.has(edge.targetNodeId) && edge.sourceNodeId !== edge.targetNodeId) {
      const adjacentNodes = adjacency.get(edge.sourceNodeId);
      if (adjacentNodes) {
        adjacentNodes.push(edge.targetNodeId);
      }

      indegree.set(edge.targetNodeId, (indegree.get(edge.targetNodeId) ?? 0) + 1);
    }
  });

  if (nodeIds.size > 0 && hasCycle(nodeIds, indegree, adjacency)) {
    errors.push("Graph must be acyclic. A cycle was detected.");
  }
}

function hasCycle(nodeIds: Set<string>, indegree: Map<string, number>, adjacency: Map<string, string[]>): boolean {
  const queue: string[] = [];
  indegree.forEach((value, key) => {
    if (value === 0) {
      queue.push(key);
    }
  });

  let visited = 0;
  while (queue.length > 0) {
    const current = queue.shift();
    if (!current) {
      continue;
    }
    visited += 1;

    (adjacency.get(current) || []).forEach((next) => {
      const nextValue = (indegree.get(next) ?? 0) - 1;
      indegree.set(next, nextValue);
      if (nextValue === 0) {
        queue.push(next);
      }
    });
  }

  return visited !== nodeIds.size;
}
