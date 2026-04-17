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
}

export interface NodeTemplatesMap {
  [type: string]: NodeTemplate;
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
  listWorkflows: () => Promise<StoredWorkflowSummary[]>;
  getWorkflow: (workflowId: string) => Promise<StoredWorkflowDetails>;
  saveWorkflow: (payload: SaveWorkflowRequest) => Promise<StoredWorkflowSummary>;
  startRun: (payload: StartRunRequest) => Promise<{ runId: string }>;
  getRun: (runId: string) => Promise<RunState>;
  getRunNodes: (runId: string) => Promise<RunNodeState[]>;
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
  editorContainerRef: { current: HTMLDivElement | null };
  setWorkflowName: (name: string) => void;
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
