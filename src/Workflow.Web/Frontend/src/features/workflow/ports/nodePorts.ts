import type { NodeTemplate, NodeTemplatePort } from "../../../shared/types/workflow";

/**
 * Что: frontend helper для нормализации IO-портов node template.
 * Зачем: Inspector, карточка ноды и validator должны одинаково понимать required/optional порты.
 * Как: использует явные descriptor ports, а для старых templates строит fallback `input_N`/`output_N`.
 */
export function getInputPorts(template: NodeTemplate | null | undefined): NodeTemplatePort[] {
  if (!template) {
    return [];
  }

  return normalizePorts(template.inputPorts, template.inputs, "input");
}

export function getOutputPorts(template: NodeTemplate | null | undefined): NodeTemplatePort[] {
  if (!template) {
    return [];
  }

  return normalizePorts(template.outputPorts, template.outputs, "output");
}

export function formatPortRequirement(port: NodeTemplatePort, direction: "input" | "output"): string {
  if (direction === "output") {
    return "output";
  }

  return port.required ? "required" : "optional";
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
