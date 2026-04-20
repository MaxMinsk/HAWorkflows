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
  status?: "draft" | "published" | string;
  updatedAtUtc?: string;
  publishedVersion?: number | null;
  publishedAtUtc?: string | null;
}

export interface StoredWorkflowDetails extends StoredWorkflowSummary {
  definition: WorkflowDefinition;
}

export interface WorkflowProfilePackDocument {
  profilePackSchemaVersion: "1.0" | string;
  metadata: WorkflowProfilePackMetadata;
  definition: WorkflowDefinition;
  executionPolicyRefs: WorkflowProfileExecutionPolicyRefs;
}

export interface WorkflowProfilePackMetadata {
  name: string;
  exportedAtUtc: string;
  sourceWorkflowId?: string | null;
  sourceWorkflowVersion?: number | null;
  sourceWorkflowStatus?: string | null;
  sourcePublishedAtUtc?: string | null;
}

export interface WorkflowProfileExecutionPolicyRefs {
  nodeTypes: string[];
  routingStages: string[];
  agentProfiles: string[];
  mcpServerProfiles: string[];
  nodePolicyRefs: WorkflowProfileNodePolicyRef[];
}

export interface WorkflowProfileNodePolicyRef {
  nodeId: string;
  nodeType: string;
  nodeName: string;
  routingStage?: string | null;
  agentProfile?: string | null;
  mcpServerProfile?: string | null;
}

export interface SaveWorkflowRequest {
  id?: string | null;
  name: string;
  definition: WorkflowDefinition;
}

export interface StartRunRequest {
  workflowId?: string | null;
  workflowVersion?: number | null;
  definition?: WorkflowDefinition;
}

export interface RunLogEntry {
  timestampUtc: string;
  nodeId?: string | null;
  message?: string | null;
}

export interface RunState {
  runId: string;
  workflowId?: string | null;
  workflowVersion?: number | null;
  canResume?: boolean;
  checkpointedAtUtc?: string | null;
  status: string;
  createdAtUtc?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  error?: string | null;
  logs?: RunLogEntry[] | null;
}

export interface WorkflowArtifactDescriptor {
  artifactId: string;
  runId: string;
  nodeId: string;
  name: string;
  artifactType: string;
  mediaType: string;
  relativePath: string;
  uri: string;
  sizeBytes: number;
  sha256: string;
  createdAtUtc: string;
}

export interface WorkflowArtifactContent {
  descriptor: WorkflowArtifactDescriptor;
  content: string;
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
  artifacts: WorkflowArtifactDescriptor[];
}

export interface NodeTemplate {
  inputs: number;
  outputs: number;
  label: string;
  description: string;
  pack?: string;
  source?: string;
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
  controlConditionKey?: string | null;
  description?: string | null;
  producesKinds?: string[];
  fallbackDescription?: string | null;
  exampleSources?: string[];
  allowMultiple?: boolean;
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

export interface DrawflowConnectionStartShape {
  output_id: number | string;
  output_class: string;
}

export interface InspectorState {
  nodeId: string;
  nodeType: string;
  nodeName: string;
  nodeConfigText: string;
}

export interface ConnectionAssistantSource {
  sourceNodeId: number;
  sourceNodeType: string;
  sourcePortId: string;
}

export interface ConnectionAssistantSuggestion {
  id: string;
  sourceNodeId: number;
  sourceNodeType: string;
  sourceNodeLabel: string;
  sourcePortId: string;
  sourcePortLabel: string;
  targetNodeType: string;
  targetNodeLabel: string;
  targetPortId: string;
  targetPortLabel: string;
  reason: string;
}

export interface WorkflowApiClient {
  getNodeTemplates: () => Promise<NodeTemplatesMap>;
  listWorkflows: () => Promise<StoredWorkflowSummary[]>;
  getWorkflow: (workflowId: string) => Promise<StoredWorkflowDetails>;
  getWorkflowVersion: (workflowId: string, version: number) => Promise<StoredWorkflowDetails>;
  saveWorkflow: (payload: SaveWorkflowRequest) => Promise<StoredWorkflowSummary>;
  publishWorkflowVersion: (workflowId: string, version: number) => Promise<StoredWorkflowDetails>;
  exportWorkflowProfilePack: (workflowId: string, version?: number | null) => Promise<WorkflowProfilePackDocument>;
  importWorkflowProfilePack: (profilePack: WorkflowProfilePackDocument, name?: string) => Promise<StoredWorkflowDetails>;
  startRun: (payload: StartRunRequest) => Promise<{ runId: string }>;
  resumeRun: (runId: string) => Promise<{ runId: string }>;
  getRun: (runId: string) => Promise<RunState>;
  getRunNodes: (runId: string) => Promise<RunNodeState[]>;
  getRunArtifacts: (runId: string) => Promise<WorkflowArtifactDescriptor[]>;
  getRunArtifact: (runId: string, artifactId: string) => Promise<WorkflowArtifactContent>;
  getMetrics: () => Promise<WorkflowMetricsSnapshot>;
  getMcpSettings: () => Promise<McpSettingsResponse>;
  saveMcpSettings: (settings: McpSettingsDocument) => Promise<McpSettingsResponse>;
  testMcpProfile: (request: TestMcpProfileRequest) => Promise<TestMcpProfileResponse>;
}

export interface WorkflowMetricsSnapshot {
  capturedAtUtc: string;
  totalRunsStarted: number;
  activeRuns: number;
  totalRunsCompleted: number;
  totalRunsSucceeded: number;
  totalRunsFailed: number;
  totalRunsDeduplicated: number;
  totalNodeStatusUpdates: number;
  averageCompletedRunDurationMs: number;
  totalCompletedNodes: number;
  totalSucceededNodes: number;
  totalFailedNodes: number;
  totalSkippedNodes: number;
  totalAgentNodes: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  totalCostUsd: number;
  recentRuns: WorkflowMetricsRunSample[];
  nodeTypeMetrics: WorkflowMetricsAggregate[];
  stageMetrics: WorkflowMetricsAggregate[];
  modelRouteMetrics: WorkflowMetricsAggregate[];
  routeReasonMetrics: WorkflowMetricsAggregate[];
}

export interface WorkflowMetricsRunSample {
  runId: string;
  workflowId?: string | null;
  workflowVersion?: number | null;
  workflowName: string;
  triggerType: string;
  status: string;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  durationMs: number;
  nodeCount: number;
  succeededNodeCount: number;
  failedNodeCount: number;
  skippedNodeCount: number;
  agentNodeCount: number;
  error?: string | null;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  costUsd: number;
  nodes: WorkflowMetricsNodeSample[];
}

export interface WorkflowMetricsNodeSample {
  nodeId: string;
  nodeType: string;
  nodeName: string;
  status: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  durationMs: number;
  routingStage?: string | null;
  selectedTier?: string | null;
  selectedModel?: string | null;
  thinkingMode?: string | null;
  routeReason?: string | null;
  routingConfidence?: number | null;
  routingRetryCount?: number | null;
  routingBudgetRemaining?: number | null;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  costUsd: number;
}

export interface WorkflowMetricsAggregate {
  key: string;
  completedNodes: number;
  succeededNodes: number;
  failedNodes: number;
  skippedNodes: number;
  totalDurationMs: number;
  averageDurationMs: number;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  costUsd: number;
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
  currentWorkflowVersion: number | null;
  currentPublishedVersion: number | null;
  storedWorkflows: StoredWorkflowSummary[];
  inspector: InspectorState;
  inspectorEnabled: boolean;
  connectionAssistantSuggestions: ConnectionAssistantSuggestion[];
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
  addSuggestedNode: (suggestion: ConnectionAssistantSuggestion) => void;
  removeSelectedNode: () => void;
  getConnectionKey: (connection: DrawflowConnectionShape) => string;
  disconnectConnection: (connection: DrawflowConnectionShape) => void;
  onUpdateNode: () => void;
  onLoad: () => Promise<void>;
  onSave: () => Promise<void>;
  onPublish: () => Promise<void>;
  onExportProfile: () => Promise<void>;
  onImportProfileFile: (file: File) => Promise<void>;
  onRun: () => Promise<void>;
  onResumeRun: (runId: string) => Promise<void>;
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
