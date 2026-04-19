import type { NodeTemplatePort } from "../../shared/types/workflow";

export interface NodeMarkupPorts {
  inputs?: NodeTemplatePort[];
  outputs?: NodeTemplatePort[];
}

/**
 * Что: генерация HTML-разметки для Drawflow node.
 * Зачем: централизовать шаблон node-card и быстро показывать IO-контракт прямо на графе.
 * Как: экранирует строки, добавляет краткие chips для input/output ports и возвращает html.
 */
export function makeNodeMarkup(label: string, type: string, description: string, ports?: NodeMarkupPorts): string {
  return (
    '<div class="workflow-node">' +
    `<div class="workflow-node-title">${escapeHtml(label)}</div>` +
    `<div class="workflow-node-type">${escapeHtml(type)} · ${escapeHtml(description)}</div>` +
    renderPorts(ports) +
    "</div>"
  );
}

function renderPorts(ports: NodeMarkupPorts | undefined): string {
  if (!ports || ((ports.inputs?.length ?? 0) === 0 && (ports.outputs?.length ?? 0) === 0)) {
    return "";
  }

  return (
    '<div class="workflow-node-ports">' +
    renderPortRow("in", ports.inputs ?? []) +
    renderPortRow("out", ports.outputs ?? []) +
    "</div>"
  );
}

function renderPortRow(direction: "in" | "out", ports: NodeTemplatePort[]): string {
  if (ports.length === 0) {
    return "";
  }

  const visiblePorts = ports.slice(0, 3);
  const hiddenCount = Math.max(0, ports.length - visiblePorts.length);
  return (
    '<div class="workflow-node-port-row">' +
    `<span class="workflow-node-port-direction">${direction}</span>` +
    visiblePorts.map(renderPortChip).join("") +
    (hiddenCount > 0 ? `<span class="workflow-node-port-more">+${hiddenCount}</span>` : "") +
    "</div>"
  );
}

function renderPortChip(port: NodeTemplatePort): string {
  const requirement = port.required ? " required" : "";
  const suffix = port.required ? " *" : "";
  const title = `${port.label} (${port.channel})${port.required ? ", required" : ""}`;

  return (
    `<span class="workflow-node-port-chip${requirement}" title="${escapeHtml(title)}">` +
    `${escapeHtml(port.label)}${suffix}` +
    "</span>"
  );
}

function escapeHtml(value: unknown): string {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
