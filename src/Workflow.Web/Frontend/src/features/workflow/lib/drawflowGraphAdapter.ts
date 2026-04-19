import type Drawflow from "drawflow";
import type { NodeMarkupPorts } from "../../editor/nodeMarkup";
import type {
  DrawflowConnectionShape,
  DrawflowExportGraph,
  DrawflowImportGraph,
  DrawflowNodeValue,
  NodeTemplate
} from "../../../shared/types/workflow";
import { getInputPorts, getOutputPorts } from "../ports/nodePorts";

interface AddNodeToCanvasOptions {
  type: string;
  template: NodeTemplate;
  container: HTMLDivElement | null;
  x?: number;
  y?: number;
  makeNodeMarkup: (label: string, type: string, description: string, ports?: NodeMarkupPorts) => string;
}

/**
 * Что: adapter-слой для low-level Drawflow операций.
 * Зачем: убрать прямые манипуляции редактором/DOM из orchestration-хуков.
 * Как: предоставляет типизированные операции export/import/add/update/remove/connectivity.
 */
export function getConnectionKey(connection: DrawflowConnectionShape): string {
  return `${connection.output_id}|${connection.output_class}|${connection.input_id}|${connection.input_class}`;
}

export function exportGraph(editor: Drawflow): DrawflowExportGraph {
  return editor.export() as DrawflowExportGraph;
}

export function getNodeCount(editor: Drawflow): number {
  return Object.keys(exportGraph(editor).drawflow.Home.data).length;
}

export function applyNodeTitle(nodeId: number, name: string): void {
  const titleElement = document.querySelector(`#node-${nodeId} .workflow-node-title`);
  if (titleElement) {
    titleElement.textContent = name;
  }
}

export function rebuildConnectionIndex(editor: Drawflow): Map<string, DrawflowConnectionShape> {
  const map = new Map<string, DrawflowConnectionShape>();
  const data = exportGraph(editor).drawflow.Home.data;

  Object.values(data).forEach((node) => {
    const name = node.data?.name ?? node.name;
    applyNodeTitle(Number(node.id), name);

    const outputs = node.outputs ?? {};
    Object.keys(outputs).forEach((outputClass) => {
      (outputs[outputClass]?.connections || []).forEach((connection) => {
        const shape: DrawflowConnectionShape = {
          output_id: Number(node.id),
          input_id: Number(connection.node),
          output_class: outputClass,
          input_class: String(connection.output ?? "")
        };
        map.set(getConnectionKey(shape), shape);
      });
    });
  });

  return map;
}

export function importGraph(editor: Drawflow, graph: DrawflowImportGraph | DrawflowExportGraph): void {
  editor.clear();
  editor.import(graph);
}

export function addNodeToCanvas(editor: Drawflow, options: AddNodeToCanvasOptions): number {
  const { type, template, container, x, y, makeNodeMarkup } = options;
  const rect = container?.getBoundingClientRect();
  const fallbackRect = { width: 640, height: 380 };
  const safeRect = rect ?? fallbackRect;

  const editorWithViewport = editor as Drawflow & {
    zoom?: number;
    canvas_x?: number;
    canvas_y?: number;
  };
  const zoom = Number.isFinite(editorWithViewport.zoom) && (editorWithViewport.zoom ?? 0) > 0
    ? (editorWithViewport.zoom as number)
    : 1;
  const canvasX = Number.isFinite(editorWithViewport.canvas_x) ? (editorWithViewport.canvas_x as number) : 0;
  const canvasY = Number.isFinite(editorWithViewport.canvas_y) ? (editorWithViewport.canvas_y as number) : 0;

  const viewportCenterX = typeof x === "number" ? x : safeRect.width / 2;
  const viewportCenterY = typeof y === "number" ? y : safeRect.height / 2;
  const randomJitterX = Math.random() * 30 - 15;
  const randomJitterY = Math.random() * 30 - 15;

  const posX = Math.round((viewportCenterX - canvasX) / zoom - 90 + randomJitterX);
  const posY = Math.round((viewportCenterY - canvasY) / zoom - 30 + randomJitterY);
  const defaultName = `${template.label} Node`;

  return editor.addNode(
    type,
    template.inputs,
    template.outputs,
    posX,
    posY,
    "workflow-node",
    { type, name: defaultName, config: {} },
    makeNodeMarkup(defaultName, type, template.description, {
      inputs: getInputPorts(template),
      outputs: getOutputPorts(template)
    }),
    false
  );
}

export function selectNode(editor: Drawflow, nodeId: number): boolean {
  const selectNodeId = (editor as Drawflow & { selectNodeId?: (nodeDomId: string) => void }).selectNodeId;
  if (typeof selectNodeId === "function") {
    selectNodeId(`node-${nodeId}`);
    return true;
  }

  return false;
}

export function removeNode(editor: Drawflow, nodeId: number): void {
  editor.removeNodeId(`node-${nodeId}`);
}

export function removeConnection(editor: Drawflow, connection: DrawflowConnectionShape): void {
  editor.removeSingleConnection(
    connection.output_id,
    connection.input_id,
    connection.output_class,
    connection.input_class
  );
}

export function getNode(editor: Drawflow, nodeId: number): DrawflowNodeValue | null {
  const node = editor.getNodeFromId(nodeId) as DrawflowNodeValue | null;
  return node ?? null;
}

export function updateNodeData(editor: Drawflow, nodeId: number, data: Record<string, unknown>): void {
  editor.updateNodeDataFromId(nodeId, data);
}
