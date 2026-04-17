import {
  type DrawflowExportGraph,
  type DrawflowNodeValue,
  type WorkflowDefinition,
  type WorkflowEdge,
  type WorkflowNode
} from "../../../shared/types/workflow";

/**
 * Что: маппинг Drawflow JSON -> workflow definition.
 * Зачем: изолировать преобразование графа от валидации и UI.
 * Как: нормализует nodes/edges в schemaVersion 1.0.
 */
export function buildWorkflowDefinitionFromDrawflow(graphJson: DrawflowExportGraph): WorkflowDefinition {
  const data: Record<string, DrawflowNodeValue> = graphJson?.drawflow?.Home?.data ?? {};
  const nodes: WorkflowNode[] = Object.values(data).map((node) => {
    const nodeType = node?.data?.type ?? node?.name ?? "";
    const nodeName = node?.data?.name ?? `${nodeType} Node`;
    const nodeConfig = node?.data?.config ?? {};
    return {
      id: String(node.id),
      type: String(nodeType),
      name: String(nodeName),
      config: nodeConfig
    };
  });

  const edges: WorkflowEdge[] = [];
  Object.values(data).forEach((node) => {
    Object.entries(node.outputs || {}).forEach(([outputClass, outputData]) => {
      (outputData.connections || []).forEach((connection) => {
        edges.push({
          id: `${node.id}|${outputClass}|${connection.node}|${connection.output}`,
          sourceNodeId: String(node.id),
          targetNodeId: String(connection.node),
          sourcePort: String(outputClass),
          targetPort: String(connection.output)
        });
      });
    });
  });

  return {
    schemaVersion: "1.0",
    name: "Draft Workflow",
    nodes,
    edges
  };
}
