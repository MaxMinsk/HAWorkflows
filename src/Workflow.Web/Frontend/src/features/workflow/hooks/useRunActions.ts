import { useCallback } from "react";
import { getErrorMessage } from "../../../shared/lib/errorMessage";
import type { GraphValidationPayload, StatusLevel, WorkflowApiClient } from "../../../shared/types/workflow";

/**
 * Что: orchestration действий запуска/остановки workflow run.
 * Зачем: изолировать run-start/run-stop сценарии от composition hook.
 * Как: валидирует граф, стартует run через API и подключает polling.
 */
interface UseRunActionsProps {
  apiClient: WorkflowApiClient;
  validateCurrentGraph: () => GraphValidationPayload | null;
  workflowName: string;
  currentWorkflowId: string | null;
  startRunPolling: (runId: string) => Promise<void>;
  stopRunPolling: () => void;
  onStatus: (text: string, level: StatusLevel) => void;
  onToast: (message: string) => void;
}

export function useRunActions({
  apiClient,
  validateCurrentGraph,
  workflowName,
  currentWorkflowId,
  startRunPolling,
  stopRunPolling,
  onStatus,
  onToast
}: UseRunActionsProps) {
  const onRun = useCallback(async () => {
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
      const run = await apiClient.startRun({
        workflowId: currentWorkflowId,
        definition: workflowDefinition
      });

      if (!run || !run.runId) {
        throw new Error("Run response is invalid.");
      }

      onStatus(`Run ${run.runId.slice(0, 8)} started`, "active");
      onToast("Run started");
      await startRunPolling(run.runId);
    } catch (error) {
      onStatus("Run failed to start", "error");
      onToast(getErrorMessage(error, "Failed to start run"));
    }
  }, [apiClient, currentWorkflowId, onStatus, onToast, startRunPolling, validateCurrentGraph, workflowName]);

  const onStop = useCallback(() => {
    stopRunPolling();
    onStatus("Run polling stopped", "idle");
    onToast("Polling stopped; run continues on server");
  }, [onStatus, onToast, stopRunPolling]);

  return {
    onRun,
    onStop
  };
}
