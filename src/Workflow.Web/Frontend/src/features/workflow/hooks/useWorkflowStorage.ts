import { useCallback, useMemo, useRef, useState } from "react";
import { getErrorMessage } from "../../../shared/lib/errorMessage";
import type {
  GraphValidationPayload,
  StatusLevel,
  StoredWorkflowSummary,
  WorkflowApiClient,
  WorkflowDefinition,
  WorkflowProfilePackDocument
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
  const [currentWorkflowVersion, setCurrentWorkflowVersion] = useState<number | null>(null);
  const [currentPublishedVersion, setCurrentPublishedVersion] = useState<number | null>(null);
  const [storedWorkflows, setStoredWorkflows] = useState<StoredWorkflowSummary[]>([]);
  const initializedRef = useRef(false);

  const setCurrentWorkflowMetadata = useCallback((
    workflowId: string | null,
    version: number | null,
    publishedVersion: number | null
  ) => {
    storageAdapter.setCurrentWorkflowId(workflowId);
    setCurrentWorkflowIdState(workflowId || null);
    setCurrentWorkflowVersion(version);
    setCurrentPublishedVersion(publishedVersion);
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
    setCurrentWorkflowMetadata(workflow.workflowId, workflow.version, workflow.publishedVersion ?? null);
    onToast(`Workflow '${name}' loaded (v${workflow.version})`);
  }, [apiClient, importWorkflowDefinition, onToast, setCurrentWorkflowMetadata, setWorkflowName]);

  const saveCurrentDraft = useCallback(async (): Promise<StoredWorkflowSummary | null> => {
    const validated = validateCurrentGraph();
    if (!validated) {
      return null;
    }

    if (!validated.validationResult.isValid) {
      onStatus(`Validation failed (${validated.validationResult.errors.length})`, "error");
      onToast("Workflow is invalid. Check Validation panel.");
      return null;
    }

    const workflowDefinition = validated.workflowDefinition;
    workflowDefinition.name = (workflowName || "Draft Workflow").trim() || "Draft Workflow";

    try {
      const savedWorkflow = await apiClient.saveWorkflow({
        id: currentWorkflowId,
        name: workflowDefinition.name,
        definition: workflowDefinition
      });

      setCurrentWorkflowMetadata(
        savedWorkflow.workflowId,
        savedWorkflow.version,
        savedWorkflow.publishedVersion ?? currentPublishedVersion
      );
      setWorkflowName(savedWorkflow.name || workflowDefinition.name);

      storageAdapter.persistLocalSnapshot(validated.graphJson, workflowDefinition);
      onStatus(`Saved v${savedWorkflow.version}`, "idle");
      onToast(`Workflow saved (v${savedWorkflow.version})`);
      await refreshStoredWorkflows();
      return savedWorkflow;
    } catch (error) {
      onStatus("Save failed", "error");
      onToast(getErrorMessage(error, "Failed to save workflow"));
      return null;
    }
  }, [
    apiClient,
    currentPublishedVersion,
    currentWorkflowId,
    onStatus,
    onToast,
    refreshStoredWorkflows,
    setCurrentWorkflowMetadata,
    setWorkflowName,
    storageAdapter,
    validateCurrentGraph,
    workflowName
  ]);

  const onSave = useCallback(async () => {
    await saveCurrentDraft();
  }, [saveCurrentDraft]);

  const onPublish = useCallback(async () => {
    const savedWorkflow = await saveCurrentDraft();
    if (!savedWorkflow) {
      return;
    }

    try {
      const publishedWorkflow = await apiClient.publishWorkflowVersion(savedWorkflow.workflowId, savedWorkflow.version);
      setCurrentWorkflowMetadata(
        publishedWorkflow.workflowId,
        publishedWorkflow.version,
        publishedWorkflow.version
      );
      setWorkflowName(publishedWorkflow.name || savedWorkflow.name);
      onStatus(`Published v${publishedWorkflow.version}`, "idle");
      onToast(`Workflow published (v${publishedWorkflow.version})`);
      await refreshStoredWorkflows();
    } catch (error) {
      onStatus("Publish failed", "error");
      onToast(getErrorMessage(error, "Failed to publish workflow"));
    }
  }, [
    apiClient,
    onStatus,
    onToast,
    refreshStoredWorkflows,
    saveCurrentDraft,
    setCurrentWorkflowMetadata,
    setWorkflowName
  ]);

  const onExportProfile = useCallback(async () => {
    const savedWorkflow = await saveCurrentDraft();
    if (!savedWorkflow) {
      return;
    }

    try {
      const profilePack = await apiClient.exportWorkflowProfilePack(
        savedWorkflow.workflowId,
        savedWorkflow.version
      );
      downloadJson(
        createProfilePackFileName(savedWorkflow.name, savedWorkflow.version),
        profilePack
      );
      onStatus(`Exported profile v${savedWorkflow.version}`, "idle");
      onToast("Workflow profile exported");
    } catch (error) {
      onStatus("Export failed", "error");
      onToast(getErrorMessage(error, "Failed to export profile pack"));
    }
  }, [apiClient, onStatus, onToast, saveCurrentDraft]);

  const onImportProfileFile = useCallback(async (file: File) => {
    try {
      const profilePack = parseProfilePack(await file.text());
      const importedWorkflow = await apiClient.importWorkflowProfilePack(profilePack);
      importWorkflowDefinition(importedWorkflow.definition);
      setCurrentWorkflowMetadata(
        importedWorkflow.workflowId,
        importedWorkflow.version,
        importedWorkflow.publishedVersion ?? null
      );
      setWorkflowName(importedWorkflow.name || profilePack.metadata.name || "Imported Workflow Profile");
      await refreshStoredWorkflows();
      onStatus(`Imported profile v${importedWorkflow.version}`, "idle");
      onToast(`Workflow profile imported: ${importedWorkflow.name}`);
    } catch (error) {
      onStatus("Import failed", "error");
      onToast(getErrorMessage(error, "Failed to import profile pack"));
    }
  }, [
    apiClient,
    importWorkflowDefinition,
    onStatus,
    onToast,
    refreshStoredWorkflows,
    setCurrentWorkflowMetadata,
    setWorkflowName
  ]);

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
    currentWorkflowVersion,
    currentPublishedVersion,
    storedWorkflows,
    saveCurrentDraft,
    onSave,
    onPublish,
    onExportProfile,
    onImportProfileFile,
    onLoad,
    onOpenStoredWorkflow,
    onRefreshStored,
    initializeStorage
  };
}

function parseProfilePack(text: string): WorkflowProfilePackDocument {
  const parsed = JSON.parse(text) as unknown;
  if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") {
    throw new Error("Profile pack file must contain a JSON object.");
  }

  const profilePack = parsed as WorkflowProfilePackDocument;
  if (profilePack.profilePackSchemaVersion !== "1.0") {
    throw new Error(`Unsupported profile pack schema '${String(profilePack.profilePackSchemaVersion)}'.`);
  }

  if (!profilePack.definition || profilePack.definition.schemaVersion !== "1.0") {
    throw new Error("Profile pack does not contain a workflow schema v1 definition.");
  }

  return profilePack;
}

function downloadJson(fileName: string, payload: unknown): void {
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function createProfilePackFileName(workflowName: string, version: number): string {
  const safeName = workflowName
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "") || "workflow-profile";
  return `${safeName}-v${version}.workflow-profile.json`;
}
