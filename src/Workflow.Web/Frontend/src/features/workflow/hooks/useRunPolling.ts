import { useCallback, useEffect, useRef, useState } from "react";
import { getErrorMessage } from "../../../shared/lib/errorMessage";
import type { RunData, StatusLevel, WorkflowApiClient } from "../../../shared/types/workflow";

/**
 * Что: хук управления lifecycle run polling.
 * Зачем: изолировать run timeline/polling от storage/editor логики.
 * Как: хранит run snapshot и опрашивает `/runs/{id}` + `/runs/{id}/nodes` до terminal state.
 */
interface UseRunPollingProps {
  apiClient: WorkflowApiClient;
  onStatus: (text: string, level: StatusLevel) => void;
  onToast: (message: string) => void;
}

export function useRunPolling({ apiClient, onStatus, onToast }: UseRunPollingProps) {
  const [runData, setRunData] = useState<RunData>({ run: null, nodes: [], artifacts: [] });
  const runPollingTimerRef = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (runPollingTimerRef.current !== null) {
        window.clearInterval(runPollingTimerRef.current);
      }
    };
  }, []);

  const stopRunPolling = useCallback(() => {
    if (runPollingTimerRef.current !== null) {
      window.clearInterval(runPollingTimerRef.current);
      runPollingTimerRef.current = null;
    }
  }, []);

  const fetchRunState = useCallback(async (runId: string): Promise<boolean> => {
    const [run, nodes, artifacts] = await Promise.all([
      apiClient.getRun(runId),
      apiClient.getRunNodes(runId),
      apiClient.getRunArtifacts(runId)
    ]);

    setRunData({ run, nodes, artifacts });

    const statusValue = String(run.status || "").toLowerCase();
    if (statusValue === "running" || statusValue === "pending") {
      onStatus(`Run ${runId.slice(0, 8)} is ${run.status}`, "active");
      return false;
    }

    if (statusValue === "succeeded") {
      onStatus(`Run ${runId.slice(0, 8)} succeeded`, "idle");
      onToast("Run completed");
      return true;
    }

    if (statusValue === "paused") {
      onStatus(`Run ${runId.slice(0, 8)} paused`, "idle");
      return true;
    }

    onStatus(`Run ${runId.slice(0, 8)} failed`, "error");
    onToast(run.error || "Run failed");
    return true;
  }, [apiClient, onStatus, onToast]);

  const startRunPolling = useCallback(async (runId: string): Promise<void> => {
    stopRunPolling();
    const firstStateIsTerminal = await fetchRunState(runId);
    if (firstStateIsTerminal) {
      return;
    }

    runPollingTimerRef.current = window.setInterval(async () => {
      try {
        const isTerminal = await fetchRunState(runId);
        if (isTerminal) {
          stopRunPolling();
        }
      } catch (error) {
        stopRunPolling();
        onStatus("Run polling failed", "error");
        onToast(getErrorMessage(error, "Failed to poll run status"));
      }
    }, 900);
  }, [fetchRunState, onStatus, onToast, stopRunPolling]);

  return {
    runData,
    startRunPolling,
    stopRunPolling
  };
}
