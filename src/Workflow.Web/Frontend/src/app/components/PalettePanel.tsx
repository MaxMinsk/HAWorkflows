import React, { useRef } from "react";
import type { NodeTemplatesMap, StoredWorkflowSummary } from "../../shared/types/workflow";
import { StoredWorkflowList } from "../../features/workflow/StoredWorkflowList";

/**
 * Что: левая панель с нодами и workflow storage.
 * Зачем: изолировать palette/storage рендер от контейнера.
 * Как: принимает данные/handlers как props.
 */
interface PalettePanelProps {
  workflowName: string;
  currentWorkflowId: string | null;
  currentWorkflowVersion: number | null;
  currentPublishedVersion: number | null;
  storedWorkflows: StoredWorkflowSummary[];
  nodeTypes: string[];
  nodeTemplates: NodeTemplatesMap;
  onWorkflowNameChange: (name: string) => void;
  onAddNode: (type: string) => void;
  onRefreshStored: () => void;
  onOpenStoredWorkflow: (workflowId: string) => void;
  onExportProfile: () => void;
  onImportProfileFile: (file: File) => void;
}

export function PalettePanel({
  workflowName,
  currentWorkflowId,
  currentWorkflowVersion,
  currentPublishedVersion,
  storedWorkflows,
  nodeTypes,
  nodeTemplates,
  onWorkflowNameChange,
  onAddNode,
  onRefreshStored,
  onOpenStoredWorkflow,
  onExportProfile,
  onImportProfileFile
}: PalettePanelProps) {
  const nodeGroups = groupNodeTypesByPack(nodeTypes, nodeTemplates);
  const profileImportInputRef = useRef<HTMLInputElement | null>(null);

  function onImportInputChanged(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }

    onImportProfileFile(file);
  }

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
      <div className="meta-line">
        <span>Draft version:</span>
        <span>{currentWorkflowVersion ? `v${currentWorkflowVersion}` : "unsaved"}</span>
      </div>
      <div className="meta-line">
        <span>Published:</span>
        <span>{currentPublishedVersion ? `v${currentPublishedVersion}` : "none"}</span>
      </div>

      <div className="node-pack-list">
        {nodeGroups.map((group) => (
          <section className="node-pack-group" key={group.pack}>
            <div className="node-pack-title">
              <span>{formatPackLabel(group.pack)}</span>
            </div>
            {group.types.map((type) => (
              <button
                className="node-template"
                data-node-type={type}
                type="button"
                key={type}
                onClick={() => onAddNode(type)}
              >
                <span>{nodeTemplates[type]?.label || type}</span>
                <span className="node-template-source">{formatSourceLabel(nodeTemplates[type]?.source)}</span>
              </button>
            ))}
          </section>
        ))}
      </div>

      <h3>Stored Workflows</h3>
      <div className="palette-actions">
        <button className="btn" type="button" onClick={onRefreshStored}>
          Refresh
        </button>
        <button className="btn" type="button" onClick={onExportProfile}>
          Export Profile
        </button>
        <button className="btn" type="button" onClick={() => profileImportInputRef.current?.click()}>
          Import Profile
        </button>
      </div>
      <input
        ref={profileImportInputRef}
        type="file"
        accept="application/json,.json,.workflow-profile.json"
        hidden
        onChange={onImportInputChanged}
      />

      <StoredWorkflowList items={storedWorkflows} onOpen={onOpenStoredWorkflow} />
    </aside>
  );
}

interface NodePackGroup {
  pack: string;
  types: string[];
}

function groupNodeTypesByPack(nodeTypes: string[], nodeTemplates: NodeTemplatesMap): NodePackGroup[] {
  const groups = new Map<string, NodePackGroup>();

  nodeTypes.forEach((type) => {
    const template = nodeTemplates[type];
    const pack = template?.pack || "core";
    const group = groups.get(pack) ?? {
      pack,
      types: []
    };

    group.types.push(type);
    groups.set(pack, group);
  });

  return Array.from(groups.values()).sort((left, right) => {
    if (left.pack === "core") {
      return -1;
    }

    if (right.pack === "core") {
      return 1;
    }

    return left.pack.localeCompare(right.pack);
  });
}

function formatPackLabel(pack: string): string {
  return pack
    .split(/[_\s-]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function formatSourceLabel(source: string | undefined): string {
  return source || "built-in";
}
