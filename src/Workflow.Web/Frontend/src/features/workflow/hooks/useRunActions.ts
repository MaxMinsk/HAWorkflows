import { useCallback } from "react";
import { getErrorMessage } from "../../../shared/lib/errorMessage";
import type { StatusLevel, StoredWorkflowSummary, WorkflowApiClient } from "../../../shared/types/workflow";

/**
 * Что: orchestration действий запуска/остановки workflow run.
 * Зачем: изолировать run-start/run-stop сценарии от composition hook.
 * Как: валидирует граф, стартует run через API и подключает polling.
 */
interface UseRunActionsProps {
  apiClient: WorkflowApiClient;
  saveCurrentDraft: () => Promise<StoredWorkflowSummary | null>;
  startRunPolling: (runId: string) => Promise<void>;
  stopRunPolling: () => void;
  onStatus: (text: string, level: StatusLevel) => void;
  onToast: (message: string) => void;
}

export function useRunActions({
  apiClient,
  saveCurrentDraft,
  startRunPolling,
  stopRunPolling,
  onStatus,
  onToast
}: UseRunActionsProps) {
  const onRun = useCallback(async () => {
    const savedWorkflow = await saveCurrentDraft();
    if (!savedWorkflow) {
      return;
    }

    try {
      const run = await apiClient.startRun({
        workflowId: savedWorkflow.workflowId,
        workflowVersion: savedWorkflow.version
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
  }, [apiClient, onStatus, onToast, saveCurrentDraft, startRunPolling]);

  const onStop = useCallback(() => {
    stopRunPolling();
    onStatus("Run polling stopped", "idle");
    onToast("Polling stopped; run continues on server");
  }, [onStatus, onToast, stopRunPolling]);

  const onResume = useCallback(async (runId: string) => {
    if (!runId) {
      return;
    }

    try {
      const run = await apiClient.resumeRun(runId);
      if (!run || !run.runId) {
        throw new Error("Resume response is invalid.");
      }

      onStatus(`Run ${run.runId.slice(0, 8)} resumed`, "active");
      onToast("Run resumed");
      await startRunPolling(run.runId);
    } catch (error) {
      onStatus("Run failed to resume", "error");
      onToast(getErrorMessage(error, "Failed to resume run"));
    }
  }, [apiClient, onStatus, onToast, startRunPolling]);

  return {
    onRun,
    onStop,
    onResume
  };
}
