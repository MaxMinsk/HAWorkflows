import React from "react";
import { NODE_TEMPLATES } from "../../shared/config/nodeTemplates";
import type { StoredWorkflowSummary } from "../../shared/types/workflow";
import { StoredWorkflowList } from "../../features/workflow/StoredWorkflowList";

/**
 * Что: левая панель с нодами и workflow storage.
 * Зачем: изолировать palette/storage рендер от контейнера.
 * Как: принимает данные/handlers как props.
 */
interface PalettePanelProps {
  workflowName: string;
  currentWorkflowId: string | null;
  storedWorkflows: StoredWorkflowSummary[];
  nodeTypes: string[];
  onWorkflowNameChange: (name: string) => void;
  onAddNode: (type: string) => void;
  onRefreshStored: () => void;
  onOpenStoredWorkflow: (workflowId: string) => void;
}

export function PalettePanel({
  workflowName,
  currentWorkflowId,
  storedWorkflows,
  nodeTypes,
  onWorkflowNameChange,
  onAddNode,
  onRefreshStored,
  onOpenStoredWorkflow
}: PalettePanelProps) {
  return (
    <aside className="panel palette-panel" aria-label="Node palette">
      <h2>Nodes</h2>
      <p className="panel-caption">Click to add a node to the canvas.</p>

      <label className="field-label" htmlFor="workflowName">
        Workflow Name
      </label>
      <input
        id="workflowName"
        type="text"
        value={workflowName}
        onChange={(event) => onWorkflowNameChange(event.target.value)}
      />

      <div className="meta-line">
        <span>Current ID:</span>
        <span>{currentWorkflowId || "new"}</span>
      </div>

      {nodeTypes.map((type) => (
        <button className="node-template" data-node-type={type} type="button" key={type} onClick={() => onAddNode(type)}>
          {NODE_TEMPLATES[type]?.label || type}
        </button>
      ))}

      <h3>Stored Workflows</h3>
      <div className="palette-actions">
        <button className="btn" type="button" onClick={onRefreshStored}>
          Refresh
        </button>
      </div>

      <StoredWorkflowList items={storedWorkflows} onOpen={onOpenStoredWorkflow} />
    </aside>
  );
}
