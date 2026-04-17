import { useCallback, useEffect, useRef, useState, type MutableRefObject } from "react";
import type Drawflow from "drawflow";
import type { DrawflowConnectionShape, InspectorState } from "../../../shared/types/workflow";

/**
 * Что: локальное состояние inspector/selection/connectivity.
 * Зачем: убрать UI-логику инспектора из core-операций редактора.
 * Как: хранит выбранную ноду, поля инспектора и связи выбранной ноды.
 */
interface UseInspectorStateProps {
  editorRef: MutableRefObject<Drawflow | null>;
  connectionIndexRef: MutableRefObject<Map<string, DrawflowConnectionShape>>;
}

export function useInspectorState({ editorRef, connectionIndexRef }: UseInspectorStateProps) {
  const selectedNodeIdRef = useRef<number | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<number | null>(null);
  const [inspector, setInspector] = useState(createEmptyInspector());
  const [inspectorEnabled, setInspectorEnabled] = useState(false);
  const [connections, setConnections] = useState<DrawflowConnectionShape[]>([]);

  useEffect(() => {
    selectedNodeIdRef.current = selectedNodeId;
  }, [selectedNodeId]);

  const clearInspector = useCallback(() => {
    setInspector(createEmptyInspector());
    setInspectorEnabled(false);
    setConnections([]);
  }, []);

  const renderConnectionsForNode = useCallback((nodeId: number | null) => {
    if (nodeId === null || nodeId === undefined) {
      setConnections([]);
      return;
    }

    const allConnections = Array.from(connectionIndexRef.current.values());
    const related = allConnections.filter((connection) => connection.output_id === nodeId || connection.input_id === nodeId);
    setConnections(related);
  }, [connectionIndexRef]);

  const syncInspector = useCallback((nodeId: number | null) => {
    const editor = editorRef.current;
    if (!editor || nodeId === null || nodeId === undefined) {
      clearInspector();
      return;
    }

    const node = editor.getNodeFromId(nodeId);
    if (!node) {
      clearInspector();
      return;
    }

    setInspector({
      nodeId: String(nodeId),
      nodeType: node.data?.type ?? node.name ?? "",
      nodeName: node.data?.name ?? "",
      nodeConfigText: JSON.stringify(node.data?.config ?? {}, null, 2)
    });
    setInspectorEnabled(true);
    renderConnectionsForNode(nodeId);
  }, [clearInspector, editorRef, renderConnectionsForNode]);

  const setInspectorField = useCallback((field: keyof InspectorState, value: string) => {
    setInspector((previous) => ({
      ...previous,
      [field]: value
    }));
  }, []);

  return {
    selectedNodeIdRef,
    setSelectedNodeId,
    inspector,
    inspectorEnabled,
    connections,
    clearInspector,
    renderConnectionsForNode,
    syncInspector,
    setInspectorField
  };
}

function createEmptyInspector(): InspectorState {
  return {
    nodeId: "",
    nodeType: "",
    nodeName: "",
    nodeConfigText: ""
  };
}
