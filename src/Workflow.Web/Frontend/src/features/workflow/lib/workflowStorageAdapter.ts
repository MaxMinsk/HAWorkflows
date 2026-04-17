import type {
  DrawflowExportGraph,
  LocalGraphReadResult,
  WorkflowDefinition,
  WorkflowStorageAdapter
} from "../../../shared/types/workflow";

const CURRENT_WORKFLOW_ID_KEY = "workflow.currentId";
const LOCAL_GRAPH_KEY = "workflow.graph.v1";
const LOCAL_DEFINITION_KEY = "workflow.definition.v1";

/**
 * Что: адаптер доступа к browser localStorage для workflow frontend.
 * Зачем: убрать storage-детали из orchestration/hooks слоя.
 * Как: инкапсулирует ключи и операции чтения/записи локального snapshot.
 */
export function createWorkflowStorageAdapter(storage: Storage = window.localStorage): WorkflowStorageAdapter {
  return {
    getCurrentWorkflowId(): string | null {
      return storage.getItem(CURRENT_WORKFLOW_ID_KEY);
    },
    setCurrentWorkflowId(workflowId: string | null): void {
      if (workflowId) {
        storage.setItem(CURRENT_WORKFLOW_ID_KEY, workflowId);
        return;
      }

      storage.removeItem(CURRENT_WORKFLOW_ID_KEY);
    },
    persistLocalSnapshot(graphJson: DrawflowExportGraph, workflowDefinition: WorkflowDefinition): void {
      storage.setItem(LOCAL_GRAPH_KEY, JSON.stringify(graphJson));
      storage.setItem(LOCAL_DEFINITION_KEY, JSON.stringify(workflowDefinition, null, 2));
    },
    readPersistedGraph(): LocalGraphReadResult {
      const raw = storage.getItem(LOCAL_GRAPH_KEY);
      if (!raw) {
        return { status: "empty" };
      }

      try {
        return { status: "ok", graphJson: JSON.parse(raw) as DrawflowExportGraph };
      } catch {
        return { status: "invalid" };
      }
    }
  };
}
