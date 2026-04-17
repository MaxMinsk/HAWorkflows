import React from "react";
import type { StoredWorkflowSummary } from "../../shared/types/workflow";

/**
 * Что: список сохраненных workflow с действием Open.
 * Зачем: изолировать рендер workflow storage списка.
 * Как: отображает name/version/id и вызывает onOpen(workflowId).
 */
interface StoredWorkflowListProps {
  items: StoredWorkflowSummary[];
  onOpen: (workflowId: string) => void;
}

export function StoredWorkflowList({ items, onOpen }: StoredWorkflowListProps) {
  if (!Array.isArray(items) || items.length === 0) {
    return (
      <ul className="workflow-list">
        <li className="workflow-list-item">No workflows in storage</li>
      </ul>
    );
  }

  return (
    <ul className="workflow-list">
      {items.map((workflow) => (
        <li className="workflow-list-item" key={workflow.workflowId}>
          <div>
            <div className="workflow-list-title">{workflow.name}</div>
            <div className="workflow-list-meta">
              {workflow.workflowId} · v{workflow.version}
            </div>
          </div>
          <button className="disconnect-btn" type="button" onClick={() => onOpen(workflow.workflowId)}>
            Open
          </button>
        </li>
      ))}
    </ul>
  );
}
