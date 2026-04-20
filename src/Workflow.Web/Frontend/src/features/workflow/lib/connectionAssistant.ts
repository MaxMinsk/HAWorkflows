import type {
  ConnectionAssistantSource,
  ConnectionAssistantSuggestion,
  DrawflowConnectionStartShape,
  DrawflowExportGraph,
  NodeTemplatePort,
  NodeTemplatesMap
} from "../../../shared/types/workflow";
import { getInputPorts, getOutputPorts } from "../ports/nodePorts";

const PREFERRED_NEXT_NODE_TYPES: Record<string, string[]> = {
  "task_text_input.output_1": ["mcp_tool_call", "template_select", "workspace_prepare_raw", "agent_task", "output"],
  "mcp_tool_call.output_1": ["workspace_prepare_raw", "evidence_pack_builder", "agent_task", "artifact_write", "output"],
  "workspace_prepare_raw.output_1": ["evidence_pack_builder", "agent_task", "artifact_write", "output"],
  "evidence_pack_builder.output_1": ["context_pack_builder", "agent_task", "artifact_write", "output"],
  "context_pack_builder.output_1": ["agent_task", "artifact_write", "output"],
  "agent_task.output_1": ["artifact_write", "output", "agent_task"]
};

interface BuildConnectionAssistantSuggestionsOptions {
  nodeTemplates: NodeTemplatesMap;
  source: ConnectionAssistantSource;
  limit?: number;
}

/**
 * Что: доменная логика connection assistant.
 * Зачем: выбирать следующие compatible ноды по channel + rich IO metadata без frontend hardcode в компонентах.
 * Как: сравнивает source output с target input ports, ранжирует по preferred path и пересечению payload kinds.
 */
export function buildConnectionAssistantSuggestions({
  nodeTemplates,
  source,
  limit = 6
}: BuildConnectionAssistantSuggestionsOptions): ConnectionAssistantSuggestion[] {
  const sourceTemplate = nodeTemplates[source.sourceNodeType];
  const sourcePort = getOutputPorts(sourceTemplate).find((port) => port.id === source.sourcePortId);
  if (!sourceTemplate || !sourcePort) {
    return [];
  }

  return Object.entries(nodeTemplates)
    .flatMap(([targetNodeType, targetTemplate]) => {
      return getInputPorts(targetTemplate).map((targetPort) => ({
        targetNodeType,
        targetTemplate,
        targetPort,
        match: evaluatePortConnection(sourcePort, targetPort),
        score: scoreSuggestion(source.sourceNodeType, source.sourcePortId, sourcePort, targetNodeType, targetPort)
      }));
    })
    .filter((candidate) => candidate.match.isCompatible)
    .sort((left, right) => right.score - left.score || left.targetTemplate.label.localeCompare(right.targetTemplate.label))
    .slice(0, limit)
    .map((candidate) => ({
      id: [
        source.sourceNodeId,
        source.sourcePortId,
        candidate.targetNodeType,
        candidate.targetPort.id
      ].join("|"),
      sourceNodeId: source.sourceNodeId,
      sourceNodeType: source.sourceNodeType,
      sourceNodeLabel: sourceTemplate.label,
      sourcePortId: source.sourcePortId,
      sourcePortLabel: sourcePort.label,
      targetNodeType: candidate.targetNodeType,
      targetNodeLabel: candidate.targetTemplate.label,
      targetPortId: candidate.targetPort.id,
      targetPortLabel: candidate.targetPort.label,
      reason: candidate.match.reason
    }));
}

export function createConnectionAssistantSource(
  graphJson: DrawflowExportGraph,
  source: DrawflowConnectionStartShape
): ConnectionAssistantSource | null {
  const sourceNodeId = Number(source.output_id);
  const sourceNode = graphJson.drawflow.Home.data[String(sourceNodeId)];
  if (!sourceNode) {
    return null;
  }

  const sourceNodeType = String(sourceNode.data?.type ?? sourceNode.name ?? "");
  if (!sourceNodeType || !source.output_class) {
    return null;
  }

  return {
    sourceNodeId,
    sourceNodeType,
    sourcePortId: String(source.output_class)
  };
}

export function evaluatePortConnection(
  sourcePort: NodeTemplatePort,
  targetPort: NodeTemplatePort
): { isCompatible: boolean; reason: string } {
  if (sourcePort.channel !== targetPort.channel) {
    return {
      isCompatible: false,
      reason: `channel mismatch: ${sourcePort.channel} -> ${targetPort.channel}`
    };
  }

  const producedKinds = sourcePort.producesKinds ?? [];
  const acceptedKinds = targetPort.acceptedKinds ?? [];
  const matchingKinds = producedKinds.filter((kind) => acceptedKinds.includes(kind));
  if (matchingKinds.length > 0) {
    return {
      isCompatible: true,
      reason: `matches ${matchingKinds.join(", ")}`
    };
  }

  if (producedKinds.length > 0 && acceptedKinds.length > 0) {
    return {
      isCompatible: true,
      reason: `channel ${sourcePort.channel} matches; verify kind ${producedKinds.join(", ")} -> ${acceptedKinds.join(", ")}`
    };
  }

  return {
    isCompatible: true,
    reason: `channel ${sourcePort.channel} matches`
  };
}

export function applyConnectionTargetHighlights(
  container: HTMLElement,
  graphJson: DrawflowExportGraph,
  nodeTemplates: NodeTemplatesMap,
  source: ConnectionAssistantSource
): void {
  clearConnectionTargetHighlights(container);

  const sourceTemplate = nodeTemplates[source.sourceNodeType];
  const sourcePort = getOutputPorts(sourceTemplate).find((port) => port.id === source.sourcePortId);
  if (!sourceTemplate || !sourcePort) {
    return;
  }

  container.classList.add("connection-assistant-active");
  const sourceOutputElement = container.querySelector<HTMLElement>(
    `#node-${source.sourceNodeId} .outputs .${source.sourcePortId}`
  );
  sourceOutputElement?.classList.add("connection-assistant-source");

  container.querySelectorAll<HTMLElement>(".drawflow-node .inputs .input").forEach((inputElement) => {
    const targetNodeElement = inputElement.closest<HTMLElement>(".drawflow-node");
    const targetNodeId = readNodeId(targetNodeElement);
    const targetPortId = readPortClass(inputElement, "input");
    const targetNode = targetNodeId === null ? null : graphJson.drawflow.Home.data[String(targetNodeId)];
    const targetNodeType = String(targetNode?.data?.type ?? targetNode?.name ?? "");
    const targetTemplate = nodeTemplates[targetNodeType];
    const targetPort = getInputPorts(targetTemplate).find((port) => port.id === targetPortId);

    if (targetNodeId === source.sourceNodeId) {
      markInput(inputElement, false, "cannot connect a node to itself");
      return;
    }

    if (!targetTemplate || !targetPort) {
      markInput(inputElement, false, "unknown target port");
      return;
    }

    const match = evaluatePortConnection(sourcePort, targetPort);
    markInput(inputElement, match.isCompatible, match.reason);
  });
}

export function clearConnectionTargetHighlights(container: HTMLElement): void {
  container.classList.remove("connection-assistant-active");
  container.querySelectorAll<HTMLElement>(".connection-compatible, .connection-incompatible, .connection-assistant-source")
    .forEach((element) => {
      element.classList.remove("connection-compatible", "connection-incompatible", "connection-assistant-source");
      element.removeAttribute("title");
    });
}

function scoreSuggestion(
  sourceNodeType: string,
  sourcePortId: string,
  sourcePort: NodeTemplatePort,
  targetNodeType: string,
  targetPort: NodeTemplatePort
): number {
  const preferred = PREFERRED_NEXT_NODE_TYPES[`${sourceNodeType}.${sourcePortId}`] ?? [];
  const preferredIndex = preferred.indexOf(targetNodeType);
  const preferredScore = preferredIndex >= 0 ? 1000 - preferredIndex * 20 : 0;
  const matchingKindCount = (sourcePort.producesKinds ?? [])
    .filter((kind) => (targetPort.acceptedKinds ?? []).includes(kind))
    .length;
  const requiredScore = targetPort.required ? 5 : 0;

  return preferredScore + matchingKindCount * 50 + requiredScore;
}

function markInput(element: HTMLElement, isCompatible: boolean, reason: string): void {
  element.classList.add(isCompatible ? "connection-compatible" : "connection-incompatible");
  element.title = isCompatible ? `Compatible: ${reason}` : `Not compatible: ${reason}`;
}

function readNodeId(element: HTMLElement | null): number | null {
  const rawId = element?.id?.startsWith("node-") ? element.id.slice("node-".length) : "";
  const parsed = Number(rawId);
  return Number.isFinite(parsed) ? parsed : null;
}

function readPortClass(element: HTMLElement, prefix: "input" | "output"): string {
  return Array.from(element.classList).find((className) => className.startsWith(`${prefix}_`)) ?? "";
}
