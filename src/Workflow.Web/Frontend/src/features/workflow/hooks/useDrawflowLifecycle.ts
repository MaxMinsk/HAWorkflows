import { useEffect, type MutableRefObject } from "react";
import Drawflow from "drawflow";
import type {
  DrawflowConnectionShape,
  GraphValidationPayload,
  StatusLevel
} from "../../../shared/types/workflow";

/**
 * Что: lifecycle Drawflow-экземпляра и подписки на события редактора.
 * Зачем: вынести объемный init/bind код из useDrawflowEditor.
 * Как: инициализирует Drawflow, вешает обработчики и синхронизирует React state.
 */
interface UseDrawflowLifecycleProps {
  editorContainerRef: MutableRefObject<HTMLDivElement | null>;
  editorRef: MutableRefObject<Drawflow | null>;
  connectionIndexRef: MutableRefObject<Map<string, DrawflowConnectionShape>>;
  selectedNodeIdRef: MutableRefObject<number | null>;
  setSelectedNodeId: (nodeId: number | null) => void;
  setIsEditorReady: (isReady: boolean) => void;
  syncInspector: (nodeId: number | null) => void;
  clearInspector: () => void;
  renderConnectionsForNode: (nodeId: number | null) => void;
  getConnectionKey: (connection: DrawflowConnectionShape) => string;
  restoreGraphFromLocalStorage: () => void;
  refreshEmptyState: () => void;
  validateCurrentGraph: () => GraphValidationPayload | null;
  onStatus: (text: string, level: StatusLevel) => void;
}

export function useDrawflowLifecycle({
  editorContainerRef,
  editorRef,
  connectionIndexRef,
  selectedNodeIdRef,
  setSelectedNodeId,
  setIsEditorReady,
  syncInspector,
  clearInspector,
  renderConnectionsForNode,
  getConnectionKey,
  restoreGraphFromLocalStorage,
  refreshEmptyState,
  validateCurrentGraph,
  onStatus
}: UseDrawflowLifecycleProps) {
  useEffect(() => {
    if (!editorContainerRef.current) {
      return undefined;
    }

    const editor = new Drawflow(editorContainerRef.current);
    editor.reroute = true;
    editor.start();
    editorRef.current = editor;
    setIsEditorReady(true);

    editor.on("nodeSelected", (nodeId: unknown) => {
      const id = Number(nodeId);
      setSelectedNodeId(id);
      syncInspector(id);
      onStatus(`Selected node ${id}`, "idle");
    });

    editor.on("nodeUnselected", () => {
      setSelectedNodeId(null);
      clearInspector();
      onStatus("Idle", "idle");
    });

    editor.on("nodeRemoved", (nodeIdRaw: unknown) => {
      const nodeId = Number(nodeIdRaw);
      const map = connectionIndexRef.current;
      Array.from(map.keys()).forEach((key) => {
        const connection = map.get(key);
        if (connection && (connection.output_id === nodeId || connection.input_id === nodeId)) {
          map.delete(key);
        }
      });

      if (selectedNodeIdRef.current === nodeId) {
        setSelectedNodeId(null);
        clearInspector();
      }

      refreshEmptyState();
      validateCurrentGraph();
      renderConnectionsForNode(selectedNodeIdRef.current);
    });

    editor.on("connectionCreated", (connection: DrawflowConnectionShape) => {
      connectionIndexRef.current.set(getConnectionKey(connection), connection);
      renderConnectionsForNode(selectedNodeIdRef.current);
      validateCurrentGraph();
      onStatus("Connection created", "active");
      window.setTimeout(() => onStatus("Idle", "idle"), 600);
    });

    editor.on("connectionRemoved", (connection: DrawflowConnectionShape) => {
      connectionIndexRef.current.delete(getConnectionKey(connection));
      renderConnectionsForNode(selectedNodeIdRef.current);
      validateCurrentGraph();
      onStatus("Connection removed", "idle");
    });

    restoreGraphFromLocalStorage();
    refreshEmptyState();
    validateCurrentGraph();

    return () => {
      editorRef.current = null;
      setIsEditorReady(false);
    };
  }, [
    clearInspector,
    connectionIndexRef,
    editorContainerRef,
    editorRef,
    getConnectionKey,
    onStatus,
    refreshEmptyState,
    renderConnectionsForNode,
    restoreGraphFromLocalStorage,
    selectedNodeIdRef,
    setIsEditorReady,
    setSelectedNodeId,
    syncInspector,
    validateCurrentGraph
  ]);
}
