import type {
  DrawflowEndpoint,
  DrawflowImportGraph,
  DrawflowImportNode,
  NodeTemplatesMap,
  WorkflowDefinition,
  WorkflowEdge
} from "../../../shared/types/workflow";

/**
 * Что: маппинг workflow definition -> Drawflow import JSON.
 * Зачем: загрузка сохраненного workflow обратно в canvas.
 * Как: нормализует id, строит inputs/outputs и восстанавливает connections.
 */
interface BuildDrawflowImportDependencies {
  nodeTemplates: NodeTemplatesMap;
  makeNodeMarkup: (label: string, type: string, description: string) => string;
}

interface NormalizedNodeEntry {
  id: number;
  type: string;
  name: string;
  config: Record<string, unknown>;
}

export function buildDrawflowImportFromDefinition(
  definition: WorkflowDefinition,
  dependencies: BuildDrawflowImportDependencies
): DrawflowImportGraph {
  const { nodeTemplates, makeNodeMarkup } = dependencies;
  const nodesById = new Map<string, NormalizedNodeEntry>();
  const orderedNodes = Array.isArray(definition.nodes) ? definition.nodes : [];

  orderedNodes.forEach((node, index) => {
    const numericId = Number.parseInt(node.id, 10);
    const effectiveId = Number.isNaN(numericId) ? index + 1 : numericId;
    nodesById.set(String(node.id), {
      id: effectiveId,
      type: node.type,
      name: node.name,
      config: node.config || {}
    });
  });

  const edges = Array.isArray(definition.edges) ? definition.edges : [];
  const incoming = new Map<string, WorkflowEdge[]>();
  const outgoing = new Map<string, WorkflowEdge[]>();
  orderedNodes.forEach((node) => {
    incoming.set(String(node.id), []);
    outgoing.set(String(node.id), []);
  });

  edges.forEach((edge) => {
    if (!incoming.has(edge.targetNodeId) || !outgoing.has(edge.sourceNodeId)) {
      return;
    }

    incoming.get(edge.targetNodeId)?.push(edge);
    outgoing.get(edge.sourceNodeId)?.push(edge);
  });

  const drawflowData: Record<string, DrawflowImportNode> = {};
  orderedNodes.forEach((node, index) => {
    const mapped = nodesById.get(String(node.id));
    if (!mapped) {
      return;
    }

    const nodeType = mapped.type;
    const template = nodeTemplates[nodeType] || nodeTemplates.transform;
    const inputCount = Math.max(template.inputs, incoming.get(String(node.id))?.length ?? 0);
    const outputCount = Math.max(template.outputs, outgoing.get(String(node.id))?.length ?? 0);

    const inputs: Record<string, DrawflowEndpoint> = {};
    const outputs: Record<string, DrawflowEndpoint> = {};
    for (let i = 1; i <= inputCount; i += 1) {
      inputs[`input_${i}`] = { connections: [] };
    }
    for (let i = 1; i <= outputCount; i += 1) {
      outputs[`output_${i}`] = { connections: [] };
    }

    drawflowData[String(mapped.id)] = {
      id: mapped.id,
      name: nodeType,
      data: {
        type: nodeType,
        name: mapped.name,
        config: mapped.config
      },
      class: "workflow-node",
      html: makeNodeMarkup(mapped.name, nodeType, template.description),
      typenode: false,
      inputs,
      outputs,
      pos_x: 80 + (index % 4) * 260,
      pos_y: 80 + Math.floor(index / 4) * 170
    };
  });

  edges.forEach((edge) => {
    const source = nodesById.get(String(edge.sourceNodeId));
    const target = nodesById.get(String(edge.targetNodeId));
    if (!source || !target) {
      return;
    }

    const sourceNode = drawflowData[String(source.id)];
    const targetNode = drawflowData[String(target.id)];
    if (!sourceNode || !targetNode) {
      return;
    }

    if (!sourceNode.outputs[edge.sourcePort]) {
      sourceNode.outputs[edge.sourcePort] = { connections: [] };
    }
    if (!targetNode.inputs[edge.targetPort]) {
      targetNode.inputs[edge.targetPort] = { connections: [] };
    }

    sourceNode.outputs[edge.sourcePort].connections.push({
      node: String(target.id),
      output: edge.targetPort
    });
    targetNode.inputs[edge.targetPort].connections.push({
      node: String(source.id),
      input: edge.sourcePort
    });
  });

  return {
    drawflow: {
      Home: {
        data: drawflowData
      }
    }
  };
}
