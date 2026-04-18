import { useCallback, useMemo, useRef, useState, type MutableRefObject } from "react";
import type Drawflow from "drawflow";
import { getErrorMessage } from "../../../shared/lib/errorMessage";
import { makeNodeMarkup } from "../../editor/nodeMarkup";
import type {
  ClipboardNode,
  DrawflowConnectionShape,
  GraphValidationPayload,
  NodeTemplatesMap,
  StatusLevel,
  WorkflowDefinition
} from "../../../shared/types/workflow";
import { useDrawflowKeyboardShortcuts } from "./useDrawflowKeyboardShortcuts";
import { useDrawflowLifecycle } from "./useDrawflowLifecycle";
import { useInspectorState } from "./useInspectorState";
import { createWorkflowStorageAdapter } from "../lib/workflowStorageAdapter";
import { createWorkflowGraphService } from "../lib/workflowGraphService";
import {
  addNodeToCanvas,
  applyNodeTitle,
  exportGraph,
  getConnectionKey as getConnectionKeyValue,
  getNode,
  getNodeCount,
  importGraph,
  rebuildConnectionIndex,
  removeConnection,
  removeNode as removeNodeFromCanvas,
  selectNode,
  updateNodeData
} from "../lib/drawflowGraphAdapter";

/**
 * Что: хук интеграции Drawflow-редактора с React state.
 * Зачем: отделить canvas/inspector/validation/shortcuts от storage/runtime.
 * Как: оркестрирует drawflow lifecycle, операции графа и состояние инспектора.
 */
interface UseDrawflowEditorProps {
  nodeTemplates: NodeTemplatesMap;
  setWorkflowName: (name: string) => void;
  onStatus: (text: string, level: StatusLevel) => void;
  onToast: (message: string) => void;
  onSaveRequestedRef: MutableRefObject<(() => Promise<void>) | (() => void)>;
}

export function useDrawflowEditor({
  nodeTemplates,
  setWorkflowName,
  onStatus,
  onToast,
  onSaveRequestedRef
}: UseDrawflowEditorProps) {
  const storageAdapter = useMemo(() => createWorkflowStorageAdapter(window.localStorage), []);
  const graphService = useMemo(() => createWorkflowGraphService({
    nodeTemplates,
    makeNodeMarkup
  }), [nodeTemplates]);
  const editorContainerRef = useRef<HTMLDivElement | null>(null);
  const editorRef = useRef<Drawflow | null>(null);
  const connectionIndexRef = useRef<Map<string, DrawflowConnectionShape>>(new Map());
  const clipboardNodeRef = useRef<ClipboardNode | null>(null);

  const [isEditorReady, setIsEditorReady] = useState(false);
  const [isCanvasEmpty, setIsCanvasEmpty] = useState(true);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);

  const {
    selectedNodeIdRef,
    setSelectedNodeId,
    inspector,
    inspectorEnabled,
    connections,
    clearInspector,
    renderConnectionsForNode,
    syncInspector,
    setInspectorField
  } = useInspectorState({
    editorRef,
    connectionIndexRef
  });

  const getConnectionKey = useCallback((connection: DrawflowConnectionShape): string => {
    return getConnectionKeyValue(connection);
  }, []);

  const refreshEmptyState = useCallback(() => {
    const editor = editorRef.current;
    if (!editor) {
      setIsCanvasEmpty(true);
      return;
    }

    setIsCanvasEmpty(getNodeCount(editor) === 0);
  }, []);

  const syncNodeMarkup = useCallback((nodeId: number, name: string) => {
    applyNodeTitle(nodeId, name);
  }, []);

  const rebuildConnectionIndexAndMarkup = useCallback(() => {
    const editor = editorRef.current;
    if (!editor) {
      connectionIndexRef.current = new Map();
      return;
    }

    connectionIndexRef.current = rebuildConnectionIndex(editor);
  }, []);

  const validateCurrentGraph = useCallback((): GraphValidationPayload | null => {
    const editor = editorRef.current;
    if (!editor) {
      return null;
    }

    const graphJson = exportGraph(editor);
    const validationPayload = graphService.buildValidationPayload(graphJson);
    setValidationErrors(validationPayload.validationResult.errors);
    return validationPayload;
  }, [graphService]);

  const validateConnection = useCallback((connection: DrawflowConnectionShape): string[] => {
    const editor = editorRef.current;
    if (!editor) {
      return [];
    }

    return graphService.validateConnection(exportGraph(editor), connection).errors;
  }, [graphService]);

  const importWorkflowDefinition = useCallback((definition: WorkflowDefinition) => {
    const editor = editorRef.current;
    if (!editor) {
      throw new Error("Editor is not ready.");
    }

    const drawflowImport = graphService.buildDrawflowImport(definition);

    importGraph(editor, drawflowImport);
    rebuildConnectionIndexAndMarkup();
    setSelectedNodeId(null);
    clearInspector();
    refreshEmptyState();
    validateCurrentGraph();
  }, [clearInspector, graphService, rebuildConnectionIndexAndMarkup, refreshEmptyState, setSelectedNodeId, validateCurrentGraph]);

  const restoreGraphFromLocalStorage = useCallback(() => {
    const editor = editorRef.current;
    if (!editor) {
      return;
    }

    const persistedGraph = storageAdapter.readPersistedGraph();
    if (persistedGraph.status === "empty") {
      clearInspector();
      refreshEmptyState();
      setValidationErrors(["Local graph is empty."]);
      return;
    }

    if (persistedGraph.status === "invalid") {
      clearInspector();
      refreshEmptyState();
      setValidationErrors(["Stored graph cannot be parsed."]);
      onStatus("Restore failed", "error");
      onToast("Stored workflow is invalid");
      return;
    }

    try {
      importGraph(editor, persistedGraph.graphJson);
      rebuildConnectionIndexAndMarkup();
      clearInspector();
      refreshEmptyState();
      const validated = validateCurrentGraph();
      if (validated && typeof setWorkflowName === "function") {
        setWorkflowName(validated.workflowDefinition.name);
      }
      onToast("Workflow restored from local storage");
    } catch {
      clearInspector();
      refreshEmptyState();
      setValidationErrors(["Stored graph cannot be parsed."]);
      onStatus("Restore failed", "error");
      onToast("Stored workflow is invalid");
    }
  }, [clearInspector, onStatus, onToast, rebuildConnectionIndexAndMarkup, refreshEmptyState, setWorkflowName, storageAdapter, validateCurrentGraph]);

  const addNode = useCallback((type: string, x?: number, y?: number) => {
    const editor = editorRef.current;
    if (!editor) {
      return;
    }

    const template = nodeTemplates[type];
    if (!template) {
      onToast(`Unknown node type: ${type}`);
      return;
    }

    const nodeId = addNodeToCanvas(editor, {
      type,
      template,
      container: editorContainerRef.current,
      x,
      y,
      makeNodeMarkup
    });

    refreshEmptyState();
    const selectedByEditorApi = selectNode(editor, nodeId);
    if (!selectedByEditorApi) {
      setSelectedNodeId(nodeId);
      syncInspector(nodeId);
    }
    validateCurrentGraph();
    onToast(`${template.label} added`);
  }, [nodeTemplates, onToast, refreshEmptyState, setSelectedNodeId, syncInspector, validateCurrentGraph]);

  const removeNode = useCallback((nodeId: number | null) => {
    const editor = editorRef.current;
    if (!editor || nodeId === null || nodeId === undefined) {
      return;
    }

    removeNodeFromCanvas(editor, nodeId);
    if (selectedNodeIdRef.current === nodeId) {
      setSelectedNodeId(null);
      clearInspector();
    }

    refreshEmptyState();
    onToast("Node removed");
  }, [clearInspector, onToast, refreshEmptyState, selectedNodeIdRef, setSelectedNodeId]);

  const removeSelectedNode = useCallback(() => {
    removeNode(selectedNodeIdRef.current);
  }, [removeNode, selectedNodeIdRef]);

  const disconnectConnection = useCallback((connection: DrawflowConnectionShape) => {
    const editor = editorRef.current;
    if (!editor) {
      return;
    }

    removeConnection(editor, connection);
  }, []);

  const onUpdateNode = useCallback(() => {
    const editor = editorRef.current;
    if (!editor || selectedNodeIdRef.current === null) {
      return;
    }

    const node = getNode(editor, selectedNodeIdRef.current);
    if (!node) {
      return;
    }

    let parsedConfig = {};
    try {
      if (inspector.nodeConfigText.trim() !== "") {
        parsedConfig = JSON.parse(inspector.nodeConfigText);
      }
      if (parsedConfig === null || Array.isArray(parsedConfig) || typeof parsedConfig !== "object") {
        throw new Error("Config must be a JSON object.");
      }
    } catch (error) {
      onStatus("Config validation error", "error");
      onToast(getErrorMessage(error, "Invalid node config"));
      return;
    }

    const nextName = (inspector.nodeName || "").trim() || `${inspector.nodeType} Node`;
    const nextData = { ...node.data, name: nextName, config: parsedConfig };
    updateNodeData(editor, selectedNodeIdRef.current, nextData);
    syncNodeMarkup(selectedNodeIdRef.current, nextName);
    syncInspector(selectedNodeIdRef.current);
    validateCurrentGraph();
    onStatus("Node updated", "idle");
    onToast("Node updated");
  }, [inspector, onStatus, onToast, selectedNodeIdRef, syncInspector, syncNodeMarkup, validateCurrentGraph]);

  useDrawflowLifecycle({
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
    validateConnection,
    onStatus
  });

  useDrawflowKeyboardShortcuts({
    editorRef,
    editorContainerRef,
    selectedNodeIdRef,
    clipboardNodeRef,
    onSaveRequestedRef,
    onToast,
    addNode,
    removeNode,
    syncInspector,
    syncNodeMarkup
  });

  return {
    isEditorReady,
    isCanvasEmpty,
    inspector,
    inspectorEnabled,
    connections,
    validationErrors,
    editorContainerRef,
    getConnectionKey,
    setInspectorField,
    addNode,
    removeSelectedNode,
    disconnectConnection,
    onUpdateNode,
    validateCurrentGraph,
    importWorkflowDefinition
  };
}
