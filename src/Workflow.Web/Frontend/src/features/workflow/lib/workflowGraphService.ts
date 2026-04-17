import type {
  DrawflowExportGraph,
  DrawflowImportGraph,
  GraphValidationPayload,
  NodeTemplatesMap,
  WorkflowDefinition
} from "../../../shared/types/workflow";
import {
  buildDrawflowImportFromDefinition,
  buildWorkflowDefinitionFromDrawflow,
  validateWorkflowDefinition
} from "../graphDefinition";

interface CreateWorkflowGraphServiceDependencies {
  nodeTemplates: NodeTemplatesMap;
  makeNodeMarkup: (label: string, type: string, description: string) => string;
}

export interface WorkflowGraphService {
  buildValidationPayload: (graphJson: DrawflowExportGraph) => GraphValidationPayload;
  buildDrawflowImport: (definition: WorkflowDefinition) => DrawflowImportGraph;
}

/**
 * Что: доменный сервис преобразования/валидации workflow-графа.
 * Зачем: убрать domain-логику из orchestration hooks и переиспользовать ее единообразно.
 * Как: инкапсулирует маппинг Drawflow<->WorkflowDefinition и структурную валидацию.
 */
export function createWorkflowGraphService(
  dependencies: CreateWorkflowGraphServiceDependencies
): WorkflowGraphService {
  const { nodeTemplates, makeNodeMarkup } = dependencies;
  const supportedNodeTypes = Object.keys(nodeTemplates);

  return {
    buildValidationPayload(graphJson: DrawflowExportGraph): GraphValidationPayload {
      const workflowDefinition = buildWorkflowDefinitionFromDrawflow(graphJson);
      const validationResult = validateWorkflowDefinition(workflowDefinition, supportedNodeTypes);
      return {
        graphJson,
        workflowDefinition,
        validationResult
      };
    },
    buildDrawflowImport(definition: WorkflowDefinition): DrawflowImportGraph {
      return buildDrawflowImportFromDefinition(definition, {
        nodeTemplates,
        makeNodeMarkup
      });
    }
  };
}
