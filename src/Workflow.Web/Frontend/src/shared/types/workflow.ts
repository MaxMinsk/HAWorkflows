export type StatusLevel = "idle" | "active" | "error";

export interface StatusState {
  text: string;
  level: StatusLevel;
}

export interface ToastState {
  text: string;
  visible: boolean;
}

export interface WorkflowNode {
  id: string;
  type: string;
  name: string;
  config: Record<string, unknown>;
}

export interface WorkflowEdge {
  id?: string;
  sourceNodeId: string;
  targetNodeId: string;
  sourcePort: string;
  targetPort: string;
}

export interface WorkflowDefinition {
  schemaVersion: "1.0";
  name: string;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
}

export interface ValidationResult {
  isValid: boolean;
  errors: string[];
}

export interface GraphValidationPayload {
  graphJson: DrawflowExportGraph;
  workflowDefinition: WorkflowDefinition;
  validationResult: ValidationResult;
}

export interface StoredWorkflowSummary {
  workflowId: string;
  name: string;
  version: number;
}

export interface StoredWorkflowDetails extends StoredWorkflowSummary {
  definition: WorkflowDefinition;
}

export interface SaveWorkflowRequest {
  id?: string | null;
  name: string;
  definition: WorkflowDefinition;
}

export interface StartRunRequest {
  workflowId?: string | null;
  definition: WorkflowDefinition;
}

export interface RunLogEntry {
  timestampUtc: string;
  nodeId?: string | null;
  message?: string | null;
}

export interface RunState {
  runId: string;
  status: string;
  error?: string | null;
  logs?: RunLogEntry[] | null;
}

export interface RunNodeState {
  nodeId: string;
  nodeName?: string | null;
  status?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  error?: string | null;
  routingStage?: string | null;
  selectedTier?: string | null;
  selectedModel?: string | null;
  thinkingMode?: string | null;
  routeReason?: string | null;
  routingConfidence?: number | null;
  routingRetryCount?: number | null;
  routingBudgetRemaining?: number | null;
}

export interface RunData {
  run: RunState | null;
  nodes: RunNodeState[];
}

export interface NodeTemplate {
  inputs: number;
  outputs: number;
  label: string;
  description: string;
  pack?: string;
  source?: string;
  isLocal?: boolean;
  usesModel?: boolean;
  inputPorts?: NodeTemplatePort[];
  outputPorts?: NodeTemplatePort[];
  configFields?: NodeTemplateConfigField[];
}

export interface NodeTemplatesMap {
  [type: string]: NodeTemplate;
}

export interface NodeTemplateConfigFieldOption {
  value: string;
  label: string;
}

export interface NodeTemplateConfigField {
  key: string;
  label: string;
  fieldType: "text" | "textarea" | "select";
  description?: string | null;
  required?: boolean;
  multiline?: boolean;
  placeholder?: string | null;
  defaultValue?: string | null;
  options?: NodeTemplateConfigFieldOption[];
}

export type WorkflowPortChannel =
  | "data"
  | "artifact_ref"
  | "memory_ref"
  | "control_ok"
  | "control_fail"
  | "control_approval_required"
  | string;

export interface NodeTemplatePort {
  id: string;
  label: string;
  channel: WorkflowPortChannel;
  required?: boolean;
  acceptedKinds?: string[];
}

export interface WorkflowDataEnvelope<TPayload = unknown> {
  kind: string;
  schemaVersion: string;
  payload: TPayload;
}

export interface ClipboardNode {
  type: string;
  name: string;
  config: Record<string, unknown>;
}

export interface DrawflowEndpointConnection {
  node: string;
  output?: string;
  input?: string;
}

export interface DrawflowEndpoint {
  connections: DrawflowEndpointConnection[];
}

export interface DrawflowNodeDataValue {
  type?: string;
  name?: string;
  config?: Record<string, unknown>;
}

export interface DrawflowNodeValue {
  id: number | string;
  name: string;
  data?: DrawflowNodeDataValue;
  outputs?: Record<string, DrawflowEndpoint>;
  inputs?: Record<string, DrawflowEndpoint>;
}

export interface DrawflowHomeValue {
  data: Record<string, DrawflowNodeValue>;
}

export interface DrawflowExportGraph {
  drawflow: {
    Home: DrawflowHomeValue;
  };
}

export interface DrawflowImportGraph {
  drawflow: {
    Home: {
      data: Record<string, DrawflowImportNode>;
    };
  };
}

export interface DrawflowImportNode {
  id: number;
  name: string;
  data: {
    type: string;
    name: string;
    config: Record<string, unknown>;
  };
  class: string;
  html: string;
  typenode: boolean;
  inputs: Record<string, DrawflowEndpoint>;
  outputs: Record<string, DrawflowEndpoint>;
  pos_x: number;
  pos_y: number;
}

export interface DrawflowConnectionShape {
  output_id: number;
  input_id: number;
  output_class: string;
  input_class: string;
}

export interface InspectorState {
  nodeId: string;
  nodeType: string;
  nodeName: string;
  nodeConfigText: string;
}

export interface WorkflowApiClient {
  getNodeTemplates: () => Promise<NodeTemplatesMap>;
  listWorkflows: () => Promise<StoredWorkflowSummary[]>;
  getWorkflow: (workflowId: string) => Promise<StoredWorkflowDetails>;
  saveWorkflow: (payload: SaveWorkflowRequest) => Promise<StoredWorkflowSummary>;
  startRun: (payload: StartRunRequest) => Promise<{ runId: string }>;
  getRun: (runId: string) => Promise<RunState>;
  getRunNodes: (runId: string) => Promise<RunNodeState[]>;
  getMcpSettings: () => Promise<McpSettingsResponse>;
  saveMcpSettings: (settings: McpSettingsDocument) => Promise<McpSettingsResponse>;
  testMcpProfile: (request: TestMcpProfileRequest) => Promise<TestMcpProfileResponse>;
}

export interface McpSettingsResponse {
  configPath: string;
  exists: boolean;
  settings: McpSettingsDocument;
}

export interface McpSettingsDocument {
  defaultProfile: string;
  profiles: Record<string, McpServerProfile>;
}

export interface McpServerProfile {
  enabled?: boolean;
  type?: string;
  transport?: string | null;
  endpoint?: string | null;
  bearerToken?: string | null;
  bearerTokenEnvironmentVariable?: string | null;
  headers?: Record<string, string>;
  allowedTools?: string[];
  blockedTools?: string[];
  timeoutSeconds?: number;
}

export interface TestMcpProfileRequest {
  serverProfile: string;
  timeoutSeconds?: number;
}

export interface TestMcpProfileResponse {
  profile: string;
  serverType: string;
  toolCount: number;
  tools: McpToolDescriptor[];
  metadata?: Record<string, unknown>;
}

export interface McpToolDescriptor {
  name: string;
  description?: string | null;
}

export interface WorkflowStorageAdapter {
  getCurrentWorkflowId: () => string | null;
  setCurrentWorkflowId: (workflowId: string | null) => void;
  persistLocalSnapshot: (graphJson: DrawflowExportGraph, workflowDefinition: WorkflowDefinition) => void;
  readPersistedGraph: () => LocalGraphReadResult;
}

export type LocalGraphReadResult =
  | { status: "empty" }
  | { status: "invalid" }
  | { status: "ok"; graphJson: DrawflowExportGraph };

export interface WorkflowBuilderViewModel {
  status: StatusState;
  statusDotColor: string;
  toast: ToastState;
  isCanvasEmpty: boolean;
  workflowName: string;
  currentWorkflowId: string | null;
  storedWorkflows: StoredWorkflowSummary[];
  inspector: InspectorState;
  inspectorEnabled: boolean;
  connections: DrawflowConnectionShape[];
  validationErrors: string[];
  runData: RunData;
  nodeTypes: string[];
  nodeTemplates: NodeTemplatesMap;
  editorContainerRef: { current: HTMLDivElement | null };
  setWorkflowName: (name: string) => void;
  mcpSettings: McpSettingsDialogState;
  updateInspectorField: (field: keyof InspectorState, value: string) => void;
  addNode: (type: string, x?: number, y?: number) => void;
  removeSelectedNode: () => void;
  getConnectionKey: (connection: DrawflowConnectionShape) => string;
  disconnectConnection: (connection: DrawflowConnectionShape) => void;
  onUpdateNode: () => void;
  onLoad: () => Promise<void>;
  onSave: () => Promise<void>;
  onRun: () => Promise<void>;
  onStop: () => void;
  onRefreshStored: () => Promise<void>;
  onOpenStoredWorkflow: (workflowId: string) => Promise<void>;
}

export interface McpSettingsDialogState {
  isOpen: boolean;
  isBusy: boolean;
  configPath: string;
  exists: boolean;
  settingsText: string;
  selectedProfile: string;
  error: string | null;
  testResult: TestMcpProfileResponse | null;
  open: () => Promise<void>;
  close: () => void;
  save: () => Promise<void>;
  test: () => Promise<void>;
  setSettingsText: (value: string) => void;
  setSelectedProfile: (value: string) => void;
}
