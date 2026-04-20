import { useEffect, useRef, useState } from "react";
import type {
  McpSettingsDialogState,
  NodeTemplatesMap,
  WorkflowApiClient,
  WorkflowBuilderViewModel
} from "../../../shared/types/workflow";
import { useDrawflowEditor } from "./useDrawflowEditor";
import { useWorkflowStorage } from "./useWorkflowStorage";
import { useRunPolling } from "./useRunPolling";
import { useUiFeedback } from "./useUiFeedback";
import { useRunActions } from "./useRunActions";

interface UseWorkflowBuilderProps {
  apiClient: WorkflowApiClient;
  mcpSettings: McpSettingsDialogState;
}

/**
 * Что: orchestration-хук для Workflow Builder.
 * Зачем: связать editor/storage/runs, оставив App-компонент декларативным и компактным.
 * Как: агрегирует под-хуки по доменам и отдает единый API слою представления.
 */
export function useWorkflowBuilder({ apiClient, mcpSettings }: UseWorkflowBuilderProps): WorkflowBuilderViewModel {
  const [nodeTemplates, setNodeTemplates] = useState<NodeTemplatesMap>({});
  const [isNodeCatalogReady, setIsNodeCatalogReady] = useState(false);
  const [workflowName, setWorkflowName] = useState("Draft Workflow");
  const onSaveRequestedRef = useRef<(() => Promise<void>) | (() => void)>(() => {});
  const ui = useUiFeedback();

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
    saveCurrentDraft: storage.saveCurrentDraft,
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
    currentWorkflowVersion: storage.currentWorkflowVersion,
    currentPublishedVersion: storage.currentPublishedVersion,
    storedWorkflows: storage.storedWorkflows,
    inspector: editor.inspector,
    inspectorEnabled: editor.inspectorEnabled,
    connectionAssistantSuggestions: editor.connectionAssistantSuggestions,
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
    addSuggestedNode: editor.addSuggestedNode,
    removeSelectedNode: editor.removeSelectedNode,
    getConnectionKey: editor.getConnectionKey,
    disconnectConnection: editor.disconnectConnection,
    onUpdateNode: editor.onUpdateNode,
    onLoad: storage.onLoad,
    onSave: storage.onSave,
    onPublish: storage.onPublish,
    onExportProfile: storage.onExportProfile,
    onImportProfileFile: storage.onImportProfileFile,
    onRun: runActions.onRun,
    onResumeRun: runActions.onResume,
    onStop: runActions.onStop,
    onRefreshStored: storage.onRefreshStored,
    onOpenStoredWorkflow: storage.onOpenStoredWorkflow
  };
}
