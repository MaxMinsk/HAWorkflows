import { useCallback, useMemo, useRef, useState } from "react";
import { getErrorMessage } from "../../../shared/lib/errorMessage";
import type {
  GraphValidationPayload,
  StatusLevel,
  StoredWorkflowSummary,
  WorkflowApiClient,
  WorkflowDefinition
} from "../../../shared/types/workflow";
import { createWorkflowStorageAdapter } from "../lib/workflowStorageAdapter";

/**
 * Что: хук управления workflow storage (API + local persistence).
 * Зачем: отделить сохранение/загрузку workflow от editor/runtime логики.
 * Как: инкапсулирует CRUD сценарии и инициализацию списка workflow.
 */
interface UseWorkflowStorageProps {
  apiClient: WorkflowApiClient;
  workflowName: string;
  setWorkflowName: (name: string) => void;
  validateCurrentGraph: () => GraphValidationPayload | null;
  importWorkflowDefinition: (definition: WorkflowDefinition) => void;
  onStatus: (text: string, level: StatusLevel) => void;
  onToast: (message: string) => void;
}

export function useWorkflowStorage({
  apiClient,
  workflowName,
  setWorkflowName,
  validateCurrentGraph,
  importWorkflowDefinition,
  onStatus,
  onToast
}: UseWorkflowStorageProps) {
  const storageAdapter = useMemo(() => createWorkflowStorageAdapter(window.localStorage), []);
  const [currentWorkflowId, setCurrentWorkflowIdState] = useState<string | null>(() => storageAdapter.getCurrentWorkflowId());
  const [storedWorkflows, setStoredWorkflows] = useState<StoredWorkflowSummary[]>([]);
  const initializedRef = useRef(false);

  const setCurrentWorkflowId = useCallback((workflowId: string | null) => {
    storageAdapter.setCurrentWorkflowId(workflowId);
    setCurrentWorkflowIdState(workflowId || null);
  }, [storageAdapter]);

  const refreshStoredWorkflows = useCallback(async () => {
    const workflows = await apiClient.listWorkflows();
    if (!Array.isArray(workflows)) {
      setStoredWorkflows([]);
      return;
    }

    setStoredWorkflows(workflows);
  }, [apiClient]);

  const loadWorkflowById = useCallback(async (workflowId: string): Promise<void> => {
    if (!workflowId) {
      return;
    }

    const workflow = await apiClient.getWorkflow(workflowId);
    if (!workflow || !workflow.definition) {
      throw new Error("Workflow payload is invalid.");
    }

    importWorkflowDefinition(workflow.definition);

    const name = workflow.name || "Draft Workflow";
    setWorkflowName(name);
    setCurrentWorkflowId(workflow.workflowId);
    onToast(`Workflow '${name}' loaded`);
  }, [apiClient, importWorkflowDefinition, onToast, setCurrentWorkflowId, setWorkflowName]);

  const onSave = useCallback(async () => {
    const validated = validateCurrentGraph();
    if (!validated) {
      return;
    }

    if (!validated.validationResult.isValid) {
      onStatus(`Validation failed (${validated.validationResult.errors.length})`, "error");
      onToast("Workflow is invalid. Check Validation panel.");
      return;
    }

    const workflowDefinition = validated.workflowDefinition;
    workflowDefinition.name = (workflowName || "Draft Workflow").trim() || "Draft Workflow";

    try {
      const savedWorkflow = await apiClient.saveWorkflow({
        id: currentWorkflowId,
        name: workflowDefinition.name,
        definition: workflowDefinition
      });

      setCurrentWorkflowId(savedWorkflow.workflowId);
      setWorkflowName(savedWorkflow.name || workflowDefinition.name);

      storageAdapter.persistLocalSnapshot(validated.graphJson, workflowDefinition);
      onStatus(`Saved v${savedWorkflow.version}`, "idle");
      onToast(`Workflow saved (v${savedWorkflow.version})`);
      await refreshStoredWorkflows();
    } catch (error) {
      onStatus("Save failed", "error");
      onToast(getErrorMessage(error, "Failed to save workflow"));
    }
  }, [apiClient, currentWorkflowId, onStatus, onToast, refreshStoredWorkflows, setCurrentWorkflowId, setWorkflowName, storageAdapter, validateCurrentGraph, workflowName]);

  const onLoad = useCallback(async () => {
    try {
      if (currentWorkflowId) {
        await loadWorkflowById(currentWorkflowId);
        onStatus("Workflow loaded", "idle");
        return;
      }

      const workflows = await apiClient.listWorkflows();
      if (!Array.isArray(workflows) || workflows.length === 0) {
        onToast("No workflows available in API storage");
        return;
      }

      await loadWorkflowById(workflows[0].workflowId);
      onStatus("Latest workflow loaded", "idle");
    } catch (error) {
      onStatus("Load failed", "error");
      onToast(getErrorMessage(error, "Failed to load workflow"));
    }
  }, [apiClient, currentWorkflowId, loadWorkflowById, onStatus, onToast]);

  const onOpenStoredWorkflow = useCallback(async (workflowId: string) => {
    try {
      await loadWorkflowById(workflowId);
      onStatus("Workflow loaded", "idle");
    } catch (error) {
      onStatus("Load failed", "error");
      onToast(getErrorMessage(error, "Failed to load workflow"));
    }
  }, [loadWorkflowById, onStatus, onToast]);

  const onRefreshStored = useCallback(async () => {
    try {
      await refreshStoredWorkflows();
      onToast("Stored workflows refreshed");
    } catch (error) {
      onStatus("Refresh failed", "error");
      onToast(getErrorMessage(error, "Failed to refresh stored workflows"));
    }
  }, [onStatus, onToast, refreshStoredWorkflows]);

  const initializeStorage = useCallback(async () => {
    if (initializedRef.current) {
      return;
    }

    initializedRef.current = true;
    try {
      await refreshStoredWorkflows();
      if (currentWorkflowId) {
        await loadWorkflowById(currentWorkflowId);
      }
    } catch (error) {
      onStatus("Storage sync failed", "error");
      onToast(getErrorMessage(error, "Failed to sync API workflows"));
    }
  }, [currentWorkflowId, loadWorkflowById, onStatus, onToast, refreshStoredWorkflows]);

  return {
    currentWorkflowId,
    storedWorkflows,
    onSave,
    onLoad,
    onOpenStoredWorkflow,
    onRefreshStored,
    initializeStorage
  };
}
