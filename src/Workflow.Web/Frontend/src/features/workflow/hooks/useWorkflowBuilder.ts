import { useEffect, useMemo, useRef, useState } from "react";
import { createWorkflowApiClient } from "../../../shared/api/workflowApiClient";
import type { NodeTemplatesMap, WorkflowBuilderViewModel } from "../../../shared/types/workflow";
import { useDrawflowEditor } from "./useDrawflowEditor";
import { useWorkflowStorage } from "./useWorkflowStorage";
import { useRunPolling } from "./useRunPolling";
import { useUiFeedback } from "./useUiFeedback";
import { useRunActions } from "./useRunActions";
import { useMcpSettingsDialog } from "../../settings/useMcpSettingsDialog";

/**
 * Что: orchestration-хук для Workflow Builder.
 * Зачем: связать editor/storage/runs, оставив App-компонент декларативным и компактным.
 * Как: агрегирует под-хуки по доменам и отдает единый API слою представления.
 */
export function useWorkflowBuilder(): WorkflowBuilderViewModel {
  const apiClient = useMemo(() => createWorkflowApiClient(window.localStorage), []);
  const [nodeTemplates, setNodeTemplates] = useState<NodeTemplatesMap>({});
  const [isNodeCatalogReady, setIsNodeCatalogReady] = useState(false);
  const [workflowName, setWorkflowName] = useState("Draft Workflow");
  const onSaveRequestedRef = useRef<(() => Promise<void>) | (() => void)>(() => {});
  const ui = useUiFeedback();
  const mcpSettings = useMcpSettingsDialog(apiClient);

  const editor = useDrawflowEditor({
    nodeTemplates,
    setWorkflowName,
    onStatus: ui.setStatusMessage,
    onToast: ui.showToast,
    onSaveRequestedRef
  });

  const storage = useWorkflowStorage({
    apiClient,
    workflowName,
    setWorkflowName,
    validateCurrentGraph: editor.validateCurrentGraph,
    importWorkflowDefinition: editor.importWorkflowDefinition,
    onStatus: ui.setStatusMessage,
    onToast: ui.showToast
  });

  const runs = useRunPolling({
    apiClient,
    onStatus: ui.setStatusMessage,
    onToast: ui.showToast
  });

  const runActions = useRunActions({
    apiClient,
    validateCurrentGraph: editor.validateCurrentGraph,
    workflowName,
    currentWorkflowId: storage.currentWorkflowId,
    startRunPolling: runs.startRunPolling,
    stopRunPolling: runs.stopRunPolling,
    onStatus: ui.setStatusMessage,
    onToast: ui.showToast
  });

  useEffect(() => {
    onSaveRequestedRef.current = storage.onSave;
  }, [storage.onSave]);

  useEffect(() => {
    let isActive = true;

    apiClient
      .getNodeTemplates()
      .then((templates) => {
        if (!isActive) {
          return;
        }

        if (Object.keys(templates).length === 0) {
          throw new Error("Node catalog is empty.");
        }

        setNodeTemplates(templates);
        setIsNodeCatalogReady(true);
        ui.setStatusMessage(`Node catalog loaded (${Object.keys(templates).length})`, "idle");
      })
      .catch(() => {
        if (!isActive) {
          return;
        }

        ui.setStatusMessage("Node catalog load failed", "error");
        ui.showToast("Node catalog is unavailable. Check backend /node-types.");
      });

    return () => {
      isActive = false;
    };
  }, [apiClient, ui.setStatusMessage, ui.showToast]);

  useEffect(() => {
    if (editor.isEditorReady && isNodeCatalogReady) {
      storage.initializeStorage();
    }
  }, [editor.isEditorReady, isNodeCatalogReady, storage.initializeStorage]);
  const nodeTypes = Object.keys(nodeTemplates);

  return {
    status: ui.status,
    statusDotColor: ui.statusDotColor,
    toast: ui.toast,
    isCanvasEmpty: editor.isCanvasEmpty,
    workflowName,
    currentWorkflowId: storage.currentWorkflowId,
    storedWorkflows: storage.storedWorkflows,
    inspector: editor.inspector,
    inspectorEnabled: editor.inspectorEnabled,
    connections: editor.connections,
    validationErrors: editor.validationErrors,
    runData: runs.runData,
    nodeTypes,
    nodeTemplates,
    editorContainerRef: editor.editorContainerRef,
    setWorkflowName,
    mcpSettings,
    updateInspectorField: editor.setInspectorField,
    addNode: editor.addNode,
    removeSelectedNode: editor.removeSelectedNode,
    getConnectionKey: editor.getConnectionKey,
    disconnectConnection: editor.disconnectConnection,
    onUpdateNode: editor.onUpdateNode,
    onLoad: storage.onLoad,
    onSave: storage.onSave,
    onRun: runActions.onRun,
    onStop: runActions.onStop,
    onRefreshStored: storage.onRefreshStored,
    onOpenStoredWorkflow: storage.onOpenStoredWorkflow
  };
}
