import type {
  NodeTemplate,
  NodeTemplatePort,
  NodeTemplatesMap,
  ValidationResult,
  WorkflowDefinition,
  WorkflowEdge,
  WorkflowNode
} from "../../../shared/types/workflow";

/**
 * Что: структурная и графовая валидация workflow definition.
 * Зачем: гарантировать корректный payload перед сохранением/запуском.
 * Как: проверяет schema, типы, ссылки и ацикличность DAG.
 */
export function validateWorkflowDefinition(definition: WorkflowDefinition, nodeTemplates: NodeTemplatesMap): ValidationResult {
  const errors: string[] = [];

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

  const { nodeIds, nodeTemplatesById } = validateNodes(definition.nodes, nodeTemplates, errors);
  validateEdges(definition.edges, nodeIds, nodeTemplatesById, errors);

  return {
    isValid: errors.length === 0,
    errors
  };
}

interface NodeValidationContext {
  nodeIds: Set<string>;
  nodeTemplatesById: Map<string, NodeTemplate>;
}

function validateNodes(
  nodes: WorkflowNode[],
  nodeTemplates: NodeTemplatesMap,
  errors: string[]
): NodeValidationContext {
  const nodeIds = new Set<string>();
  const nodeTemplatesById = new Map<string, NodeTemplate>();

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

    if (typeof node.type !== "string" || !nodeTemplates[node.type]) {
      errors.push(`${prefix}.type '${node.type}' is not supported.`);
    } else if (typeof node.id === "string" && node.id.trim() !== "") {
      nodeTemplatesById.set(node.id, nodeTemplates[node.type]);
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

  return { nodeIds, nodeTemplatesById };
}

function validateEdges(
  edges: WorkflowEdge[],
  nodeIds: Set<string>,
  nodeTemplatesById: Map<string, NodeTemplate>,
  errors: string[]
): void {
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

    validatePortCompatibility(edge, prefix, nodeTemplatesById, errors);

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

function validatePortCompatibility(
  edge: WorkflowEdge,
  prefix: string,
  nodeTemplatesById: Map<string, NodeTemplate>,
  errors: string[]
): void {
  if (!edge.sourcePort || !edge.targetPort) {
    return;
  }

  const sourceTemplate = nodeTemplatesById.get(edge.sourceNodeId);
  const targetTemplate = nodeTemplatesById.get(edge.targetNodeId);
  if (!sourceTemplate || !targetTemplate) {
    return;
  }

  const sourcePort = getOutputPorts(sourceTemplate).find((port) => port.id === edge.sourcePort);
  if (!sourcePort) {
    errors.push(`${prefix}.sourcePort '${edge.sourcePort}' does not exist on node '${edge.sourceNodeId}' output ports.`);
    return;
  }

  const targetPort = getInputPorts(targetTemplate).find((port) => port.id === edge.targetPort);
  if (!targetPort) {
    errors.push(`${prefix}.targetPort '${edge.targetPort}' does not exist on node '${edge.targetNodeId}' input ports.`);
    return;
  }

  if (sourcePort.channel !== targetPort.channel) {
    errors.push(
      `${prefix} connects incompatible channels: source ${edge.sourceNodeId}.${sourcePort.id} ` +
      `(${sourcePort.channel}) -> target ${edge.targetNodeId}.${targetPort.id} (${targetPort.channel}).`
    );
  }
}

function getInputPorts(template: NodeTemplate): NodeTemplatePort[] {
  return normalizePorts(template.inputPorts, template.inputs, "input");
}

function getOutputPorts(template: NodeTemplate): NodeTemplatePort[] {
  return normalizePorts(template.outputPorts, template.outputs, "output");
}

function normalizePorts(
  ports: NodeTemplatePort[] | undefined,
  count: number,
  prefix: "input" | "output"
): NodeTemplatePort[] {
  if (ports && ports.length > 0) {
    return ports;
  }

  return Array.from({ length: Math.max(0, count) }, (_, index) => ({
    id: `${prefix}_${index + 1}`,
    label: `${prefix} ${index + 1}`,
    channel: "data"
  }));
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
