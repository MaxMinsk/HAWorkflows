import type {
  DrawflowConnectionShape,
  DrawflowExportGraph,
  DrawflowImportGraph,
  GraphValidationPayload,
  NodeTemplate,
  NodeTemplatePort,
  NodeTemplatesMap,
  ValidationResult,
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
  validateConnection: (graphJson: DrawflowExportGraph, connection: DrawflowConnectionShape) => ValidationResult;
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

  return {
    buildValidationPayload(graphJson: DrawflowExportGraph): GraphValidationPayload {
      const workflowDefinition = buildWorkflowDefinitionFromDrawflow(graphJson);
      const validationResult = validateWorkflowDefinition(workflowDefinition, nodeTemplates);
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
    },
    validateConnection(graphJson: DrawflowExportGraph, connection: DrawflowConnectionShape): ValidationResult {
      const errors = validateConnectionCompatibility(graphJson, connection, nodeTemplates);
      return {
        isValid: errors.length === 0,
        errors
      };
    }
  };
}

function validateConnectionCompatibility(
  graphJson: DrawflowExportGraph,
  connection: DrawflowConnectionShape,
  nodeTemplates: NodeTemplatesMap
): string[] {
  const nodes = graphJson?.drawflow?.Home?.data ?? {};
  const sourceNode = nodes[String(connection.output_id)];
  const targetNode = nodes[String(connection.input_id)];
  if (!sourceNode || !targetNode) {
    return [];
  }

  const sourceType = sourceNode.data?.type ?? sourceNode.name;
  const targetType = targetNode.data?.type ?? targetNode.name;
  const sourceTemplate = nodeTemplates[String(sourceType)];
  const targetTemplate = nodeTemplates[String(targetType)];
  if (!sourceTemplate || !targetTemplate) {
    return [];
  }

  const sourcePort = getOutputPorts(sourceTemplate).find((port) => port.id === connection.output_class);
  const targetPort = getInputPorts(targetTemplate).find((port) => port.id === connection.input_class);
  if (!sourcePort || !targetPort) {
    return [
      `Connection ${connection.output_id}:${connection.output_class} -> ` +
      `${connection.input_id}:${connection.input_class} uses an unknown port.`
    ];
  }

  if (sourcePort.channel !== targetPort.channel) {
    return [
      `Connection ${connection.output_id}:${sourcePort.id} (${sourcePort.channel}) -> ` +
      `${connection.input_id}:${targetPort.id} (${targetPort.channel}) is not allowed.`
    ];
  }

  return [];
}

function getInputPorts(template: NodeTemplate): NodeTemplatePort[] {
  return normalizePorts(template.inputPorts, template.inputs, "input");
}

function getOutputPorts(template: NodeTemplate): NodeTemplatePort[] {
  return normalizePorts(template.outputPorts, template.outputs, "output");
}

function normalizePorts(
  ports: NodeTemplatePort[] | undefined,
  count: number,
  prefix: "input" | "output"
): NodeTemplatePort[] {
  if (ports && ports.length > 0) {
    return ports;
  }

  return Array.from({ length: Math.max(0, count) }, (_, index) => ({
    id: `${prefix}_${index + 1}`,
    label: `${prefix} ${index + 1}`,
    channel: "data"
  }));
}
