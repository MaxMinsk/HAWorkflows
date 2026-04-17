import {
  type RunNodeState,
  type RunState,
  type SaveWorkflowRequest,
  type StartRunRequest,
  type StoredWorkflowDetails,
  type StoredWorkflowSummary,
  type WorkflowApiClient
} from "../types/workflow";

/**
 * Что: HTTP-клиент backend API.
 * Зачем: изолировать fetch и обработку ошибок от UI.
 * Как: предоставляет доменные методы workflows/runs.
 */
export function createWorkflowApiClient(storage: Storage = window.localStorage): WorkflowApiClient {
  const configuredApiBaseUrl = storage.getItem("workflow.api.baseUrl");
  const apiBaseUrl = (configuredApiBaseUrl || "/api").replace(/\/+$/, "");

  async function request<TResponse>(path: string, options?: RequestInit): Promise<TResponse> {
    const response = await fetch(`${apiBaseUrl}${path}`, options);
    if (!response.ok) {
      throw new Error(await parseApiError(response));
    }

    return (await response.json()) as TResponse;
  }

  return {
    listWorkflows(): Promise<StoredWorkflowSummary[]> {
      return request<StoredWorkflowSummary[]>("/workflows");
    },
    getWorkflow(workflowId: string): Promise<StoredWorkflowDetails> {
      return request<StoredWorkflowDetails>(`/workflows/${encodeURIComponent(workflowId)}`);
    },
    saveWorkflow(payload: SaveWorkflowRequest): Promise<StoredWorkflowSummary> {
      return request<StoredWorkflowSummary>("/workflows", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
    },
    startRun(payload: StartRunRequest): Promise<{ runId: string }> {
      return request<{ runId: string }>("/runs", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
    },
    getRun(runId: string): Promise<RunState> {
      return request<RunState>(`/runs/${encodeURIComponent(runId)}`);
    },
    getRunNodes(runId: string): Promise<RunNodeState[]> {
      return request<RunNodeState[]>(`/runs/${encodeURIComponent(runId)}/nodes`);
    }
  };
}

async function parseApiError(response: Response): Promise<string> {
  try {
    const payload = await response.json();
    if (payload && typeof payload.error === "string") {
      return payload.error;
    }
  } catch {
    // no-op
  }

  return `HTTP ${response.status}`;
}
