import {
  type McpSettingsDocument,
  type McpSettingsResponse,
  type NodeTemplatesMap,
  type RunNodeState,
  type RunState,
  type SaveWorkflowRequest,
  type StartRunRequest,
  type StoredWorkflowDetails,
  type StoredWorkflowSummary,
  type TestMcpProfileRequest,
  type TestMcpProfileResponse,
  type WorkflowApiClient,
  type WorkflowArtifactContent,
  type WorkflowArtifactDescriptor,
  type WorkflowMetricsSnapshot,
  type WorkflowProfilePackDocument
} from "../types/workflow";

/**
 * Что: HTTP-клиент backend API.
 * Зачем: изолировать fetch и обработку ошибок от UI.
 * Как: предоставляет доменные методы workflows/runs.
 */
export function createWorkflowApiClient(storage: Storage = window.localStorage): WorkflowApiClient {
  const configuredApiBaseUrl = storage.getItem("workflow.api.baseUrl");
  const apiBaseUrl = resolveApiBaseUrl(configuredApiBaseUrl, window.location.pathname);

  async function request<TResponse>(path: string, options?: RequestInit): Promise<TResponse> {
    const response = await fetch(`${apiBaseUrl}${path}`, options);
    if (!response.ok) {
      throw new Error(await parseApiError(response));
    }

    return (await response.json()) as TResponse;
  }

  return {
    async getNodeTemplates(): Promise<NodeTemplatesMap> {
      const nodeTypes = await request<NodeTypeResponse[]>("/node-types");
      const templates: NodeTemplatesMap = {};

      nodeTypes.forEach((nodeType) => {
        templates[nodeType.type] = {
          inputs: nodeType.inputs,
          outputs: nodeType.outputs,
          label: nodeType.label,
          description: nodeType.description,
          pack: nodeType.pack,
          source: nodeType.source,
          usesModel: nodeType.usesModel,
          inputPorts: nodeType.inputPorts,
          outputPorts: nodeType.outputPorts,
          configFields: nodeType.configFields
        };
      });

      return templates;
    },
    listWorkflows(): Promise<StoredWorkflowSummary[]> {
      return request<StoredWorkflowSummary[]>("/workflows");
    },
    getWorkflow(workflowId: string): Promise<StoredWorkflowDetails> {
      return request<StoredWorkflowDetails>(`/workflows/${encodeURIComponent(workflowId)}`);
    },
    getWorkflowVersion(workflowId: string, version: number): Promise<StoredWorkflowDetails> {
      return request<StoredWorkflowDetails>(
        `/workflows/${encodeURIComponent(workflowId)}/versions/${encodeURIComponent(String(version))}`
      );
    },
    saveWorkflow(payload: SaveWorkflowRequest): Promise<StoredWorkflowSummary> {
      return request<StoredWorkflowSummary>("/workflows", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
    },
    publishWorkflowVersion(workflowId: string, version: number): Promise<StoredWorkflowDetails> {
      return request<StoredWorkflowDetails>(
        `/workflows/${encodeURIComponent(workflowId)}/versions/${encodeURIComponent(String(version))}/publish`,
        {
          method: "POST"
        }
      );
    },
    exportWorkflowProfilePack(workflowId: string, version?: number | null): Promise<WorkflowProfilePackDocument> {
      const query = version ? `?version=${encodeURIComponent(String(version))}` : "";
      return request<WorkflowProfilePackDocument>(
        `/workflows/${encodeURIComponent(workflowId)}/profile-pack${query}`
      );
    },
    importWorkflowProfilePack(profilePack: WorkflowProfilePackDocument, name?: string): Promise<StoredWorkflowDetails> {
      return request<StoredWorkflowDetails>("/workflow-profile-packs/import", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ profilePack, name })
      });
    },
    startRun(payload: StartRunRequest): Promise<{ runId: string }> {
      return request<{ runId: string }>("/runs", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
    },
    resumeRun(runId: string): Promise<{ runId: string }> {
      return request<{ runId: string }>(`/runs/${encodeURIComponent(runId)}/resume`, {
        method: "POST"
      });
    },
    getRun(runId: string): Promise<RunState> {
      return request<RunState>(`/runs/${encodeURIComponent(runId)}`);
    },
    getRunNodes(runId: string): Promise<RunNodeState[]> {
      return request<RunNodeState[]>(`/runs/${encodeURIComponent(runId)}/nodes`);
    },
    getRunArtifacts(runId: string): Promise<WorkflowArtifactDescriptor[]> {
      return request<WorkflowArtifactDescriptor[]>(`/runs/${encodeURIComponent(runId)}/artifacts`);
    },
    getRunArtifact(runId: string, artifactId: string): Promise<WorkflowArtifactContent> {
      return request<WorkflowArtifactContent>(
        `/runs/${encodeURIComponent(runId)}/artifacts/${encodeURIComponent(artifactId)}`
      );
    },
    getMetrics(): Promise<WorkflowMetricsSnapshot> {
      return request<WorkflowMetricsSnapshot>("/metrics");
    },
    getMcpSettings(): Promise<McpSettingsResponse> {
      return request<McpSettingsResponse>("/settings/mcp");
    },
    saveMcpSettings(settings: McpSettingsDocument): Promise<McpSettingsResponse> {
      return request<McpSettingsResponse>("/settings/mcp", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ settings })
      });
    },
    testMcpProfile(payload: TestMcpProfileRequest): Promise<TestMcpProfileResponse> {
      return request<TestMcpProfileResponse>("/settings/mcp/test", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
    }
  };
}

function resolveApiBaseUrl(configuredApiBaseUrl: string | null, pathname: string): string {
  const ingressPrefix = detectIngressPrefix(pathname);
  const configured = configuredApiBaseUrl?.trim();

  if (configured) {
    return normalizeApiBaseUrl(configured, ingressPrefix);
  }

  if (ingressPrefix) {
    return `${ingressPrefix}/api`;
  }

  return "/api";
}

function detectIngressPrefix(pathname: string): string | null {
  const ingressMatch = pathname.match(/^\/api\/hassio_ingress\/[^/]+/);
  return ingressMatch?.[0] ?? null;
}

function normalizeApiBaseUrl(value: string, ingressPrefix: string | null): string {
  const withoutTrailingSlash = value.replace(/\/+$/, "");
  if (/^https?:\/\//i.test(withoutTrailingSlash) || withoutTrailingSlash.startsWith("/")) {
    return withoutTrailingSlash;
  }

  const relativePath = withoutTrailingSlash.replace(/^\.?\/*/, "");
  if (!relativePath) {
    return ingressPrefix ?? "/";
  }

  if (ingressPrefix) {
    return `${ingressPrefix}/${relativePath}`;
  }

  return `/${relativePath}`;
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

interface NodeTypeResponse {
  type: string;
  label: string;
  description: string;
  inputs: number;
  outputs: number;
  pack?: string;
  source?: string;
  usesModel?: boolean;
  inputPorts?: NodeTypePortResponse[];
  outputPorts?: NodeTypePortResponse[];
  configFields?: NodeTypeConfigFieldResponse[];
}

interface NodeTypePortResponse {
  id: string;
  label: string;
  channel: string;
  required?: boolean;
  acceptedKinds?: string[];
  controlConditionKey?: string | null;
  description?: string | null;
  producesKinds?: string[];
  fallbackDescription?: string | null;
  exampleSources?: string[];
  allowMultiple?: boolean;
}

interface NodeTypeConfigFieldResponse {
  key: string;
  label: string;
  fieldType: "text" | "textarea" | "select";
  description?: string | null;
  required?: boolean;
  multiline?: boolean;
  placeholder?: string | null;
  defaultValue?: string | null;
  options?: NodeTypeConfigFieldOptionResponse[];
}

interface NodeTypeConfigFieldOptionResponse {
  value: string;
  label: string;
}
