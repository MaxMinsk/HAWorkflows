import { useEffect, useRef, type MutableRefObject } from "react";
import Drawflow from "drawflow";
import { exportGraph, removeConnection } from "../lib/drawflowGraphAdapter";
import type {
  ConnectionAssistantSource,
  DrawflowConnectionShape,
  DrawflowConnectionStartShape,
  GraphValidationPayload,
  NodeTemplatesMap,
  StatusLevel
} from "../../../shared/types/workflow";
import {
  applyConnectionTargetHighlights,
  clearConnectionTargetHighlights,
  createConnectionAssistantSource
} from "../lib/connectionAssistant";

/**
 * Что: lifecycle Drawflow-экземпляра и подписки на события редактора.
 * Зачем: вынести объемный init/bind код из useDrawflowEditor.
 * Как: инициализирует Drawflow, вешает обработчики и синхронизирует React state.
 */
interface UseDrawflowLifecycleProps {
  editorContainerRef: MutableRefObject<HTMLDivElement | null>;
  editorRef: MutableRefObject<Drawflow | null>;
  connectionIndexRef: MutableRefObject<Map<string, DrawflowConnectionShape>>;
  nodeTemplates: NodeTemplatesMap;
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
  validateConnection: (connection: DrawflowConnectionShape) => string[];
  onConnectionAssistantSource: (source: ConnectionAssistantSource | null) => void;
  onStatus: (text: string, level: StatusLevel) => void;
}

export function useDrawflowLifecycle({
  editorContainerRef,
  editorRef,
  connectionIndexRef,
  nodeTemplates,
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
  validateConnection,
  onConnectionAssistantSource,
  onStatus
}: UseDrawflowLifecycleProps) {
  const handlersRef = useRef({
    nodeTemplates,
    syncInspector,
    clearInspector,
    renderConnectionsForNode,
    getConnectionKey,
    restoreGraphFromLocalStorage,
    refreshEmptyState,
    validateCurrentGraph,
    validateConnection,
    onConnectionAssistantSource,
    onStatus
  });

  useEffect(() => {
    handlersRef.current = {
      nodeTemplates,
      syncInspector,
      clearInspector,
      renderConnectionsForNode,
      getConnectionKey,
      restoreGraphFromLocalStorage,
      refreshEmptyState,
      validateCurrentGraph,
      validateConnection,
      onConnectionAssistantSource,
      onStatus
    };
  }, [
    nodeTemplates,
    syncInspector,
    clearInspector,
    renderConnectionsForNode,
    getConnectionKey,
    restoreGraphFromLocalStorage,
    refreshEmptyState,
    validateCurrentGraph,
    validateConnection,
    onConnectionAssistantSource,
    onStatus
  ]);

  useEffect(() => {
    const editorContainer = editorContainerRef.current;
    if (!editorContainer || editorRef.current) {
      return undefined;
    }

    const editor = new Drawflow(editorContainer);
    editor.reroute = true;
    editor.start();
    const editorWithZoomReset = editor as Drawflow & { zoom_reset?: () => void };
    editorWithZoomReset.zoom_reset?.();
    editorRef.current = editor;
    setIsEditorReady(true);

    editor.on("nodeSelected", (nodeId: unknown) => {
      const id = Number(nodeId);
      handlersRef.current.onConnectionAssistantSource(null);
      setSelectedNodeId(id);
      handlersRef.current.syncInspector(id);
      handlersRef.current.onStatus(`Selected node ${id}`, "idle");
    });

    editor.on("nodeUnselected", () => {
      setSelectedNodeId(null);
      handlersRef.current.clearInspector();
      handlersRef.current.onStatus("Idle", "idle");
    });

    editor.on("connectionStart", (source: DrawflowConnectionStartShape) => {
      const graphJson = exportGraph(editor);
      const assistantSource = createConnectionAssistantSource(graphJson, source);
      handlersRef.current.onConnectionAssistantSource(assistantSource);

      if (assistantSource) {
        applyConnectionTargetHighlights(
          editorContainer,
          graphJson,
          handlersRef.current.nodeTemplates,
          assistantSource
        );
        handlersRef.current.onStatus("Connection assistant: compatible target ports highlighted", "active");
      }
    });

    editor.on("connectionCancel", () => {
      clearConnectionTargetHighlights(editorContainer);
      handlersRef.current.onStatus("Connection cancelled; suggested next nodes are available in Inspector", "idle");
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
        handlersRef.current.clearInspector();
      }

      handlersRef.current.refreshEmptyState();
      handlersRef.current.validateCurrentGraph();
      handlersRef.current.renderConnectionsForNode(selectedNodeIdRef.current);
    });

    editor.on("connectionCreated", (connection: DrawflowConnectionShape) => {
      clearConnectionTargetHighlights(editorContainer);
      handlersRef.current.onConnectionAssistantSource(null);
      const connectionErrors = handlersRef.current.validateConnection(connection);
      if (connectionErrors.length > 0) {
        removeConnection(editor, connection);
        connectionIndexRef.current.delete(handlersRef.current.getConnectionKey(connection));
        handlersRef.current.validateCurrentGraph();
        handlersRef.current.renderConnectionsForNode(selectedNodeIdRef.current);
        handlersRef.current.onStatus(connectionErrors[0] ?? "Connection is not allowed", "error");
        return;
      }

      connectionIndexRef.current.set(handlersRef.current.getConnectionKey(connection), connection);
      handlersRef.current.renderConnectionsForNode(selectedNodeIdRef.current);
      handlersRef.current.validateCurrentGraph();
      handlersRef.current.onStatus("Connection created", "active");
      window.setTimeout(() => handlersRef.current.onStatus("Idle", "idle"), 600);
    });

    editor.on("connectionRemoved", (connection: DrawflowConnectionShape) => {
      clearConnectionTargetHighlights(editorContainer);
      connectionIndexRef.current.delete(handlersRef.current.getConnectionKey(connection));
      handlersRef.current.renderConnectionsForNode(selectedNodeIdRef.current);
      handlersRef.current.validateCurrentGraph();
      handlersRef.current.onStatus("Connection removed", "idle");
    });

    handlersRef.current.restoreGraphFromLocalStorage();
    handlersRef.current.refreshEmptyState();
    handlersRef.current.validateCurrentGraph();

    return () => {
      if (editorRef.current === editor) {
        editorRef.current = null;
      }
      clearConnectionTargetHighlights(editorContainer);
      if (editorContainerRef.current) {
        editorContainerRef.current.innerHTML = "";
      }
      setIsEditorReady(false);
    };
  }, [
    editorContainerRef,
    editorRef,
    connectionIndexRef,
    selectedNodeIdRef,
    setIsEditorReady,
    setSelectedNodeId
  ]);
}
