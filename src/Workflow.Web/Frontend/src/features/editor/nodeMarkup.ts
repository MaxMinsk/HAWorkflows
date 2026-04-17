/**
 * Что: генерация HTML-разметки для Drawflow node.
 * Зачем: централизовать шаблон node-card.
 * Как: экранирует строки и возвращает html.
 */
export function makeNodeMarkup(label: string, type: string, description: string): string {
  return (
    '<div class="workflow-node">' +
    `<div class="workflow-node-title">${escapeHtml(label)}</div>` +
    `<div class="workflow-node-type">${escapeHtml(type)} · ${escapeHtml(description)}</div>` +
    "</div>"
  );
}

function escapeHtml(value: unknown): string {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
