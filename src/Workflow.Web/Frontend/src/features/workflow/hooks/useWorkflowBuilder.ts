import { useEffect, useMemo, useRef, useState } from "react";
import { NODE_TEMPLATES } from "../../../shared/config/nodeTemplates";
import { createWorkflowApiClient } from "../../../shared/api/workflowApiClient";
import type { WorkflowBuilderViewModel } from "../../../shared/types/workflow";
import { useDrawflowEditor } from "./useDrawflowEditor";
import { useWorkflowStorage } from "./useWorkflowStorage";
import { useRunPolling } from "./useRunPolling";
import { useUiFeedback } from "./useUiFeedback";
import { useRunActions } from "./useRunActions";

/**
 * Что: orchestration-хук для Workflow Builder.
 * Зачем: связать editor/storage/runs, оставив App-компонент декларативным и компактным.
 * Как: агрегирует под-хуки по доменам и отдает единый API слою представления.
 */
export function useWorkflowBuilder(): WorkflowBuilderViewModel {
  const apiClient = useMemo(() => createWorkflowApiClient(window.localStorage), []);
  const [workflowName, setWorkflowName] = useState("Draft Workflow");
  const onSaveRequestedRef = useRef<(() => Promise<void>) | (() => void)>(() => {});
  const ui = useUiFeedback();

  const editor = useDrawflowEditor({
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
    if (editor.isEditorReady) {
      storage.initializeStorage();
    }
  }, [editor.isEditorReady, storage.initializeStorage]);
  const nodeTypes = Object.keys(NODE_TEMPLATES);

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
    editorContainerRef: editor.editorContainerRef,
    setWorkflowName,
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
