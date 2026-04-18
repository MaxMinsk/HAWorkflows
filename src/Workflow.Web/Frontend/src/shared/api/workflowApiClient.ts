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
  type WorkflowApiClient
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
          isLocal: nodeType.isLocal,
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
  isLocal: boolean;
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
