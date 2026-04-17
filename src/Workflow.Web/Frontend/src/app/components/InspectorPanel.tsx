import React from "react";
import { ValidationList } from "../../features/editor/ValidationList";
import { RunTimeline } from "../../features/runs/RunTimeline";
import type { DrawflowConnectionShape, InspectorState, RunData } from "../../shared/types/workflow";

/**
 * Что: правая панель инспектора ноды и run diagnostics.
 * Зачем: отделить большой блок форм/списков от App-композиции.
 * Как: получает состояние и обработчики через props.
 */
interface InspectorPanelProps {
  inspector: InspectorState;
  inspectorEnabled: boolean;
  connections: DrawflowConnectionShape[];
  validationErrors: string[];
  runData: RunData;
  getConnectionKey: (connection: DrawflowConnectionShape) => string;
  onInspectorFieldChange: (field: keyof InspectorState, value: string) => void;
  onUpdateNode: () => void;
  onDeleteNode: () => void;
  onDisconnectConnection: (connection: DrawflowConnectionShape) => void;
}

export function InspectorPanel({
  inspector,
  inspectorEnabled,
  connections,
  validationErrors,
  runData,
  getConnectionKey,
  onInspectorFieldChange,
  onUpdateNode,
  onDeleteNode,
  onDisconnectConnection
}: InspectorPanelProps) {
  return (
    <aside className="panel inspector-panel" aria-label="Node inspector">
      <h2>Inspector</h2>
      <p className="panel-caption">Select a node to edit basic properties.</p>

      <label className="field-label" htmlFor="nodeId">
        Node ID
      </label>
      <input id="nodeId" type="text" value={inspector.nodeId} disabled />

      <label className="field-label" htmlFor="nodeType">
        Type
      </label>
      <input id="nodeType" type="text" value={inspector.nodeType} disabled />

      <label className="field-label" htmlFor="nodeName">
        Name
      </label>
      <input
        id="nodeName"
        type="text"
        placeholder="Node name"
        value={inspector.nodeName}
        onChange={(event) => onInspectorFieldChange("nodeName", event.target.value)}
      />

      <label className="field-label" htmlFor="nodeConfig">
        Config (JSON)
      </label>
      <textarea
        id="nodeConfig"
        rows={7}
        placeholder="{}"
        value={inspector.nodeConfigText}
        onChange={(event) => onInspectorFieldChange("nodeConfigText", event.target.value)}
      />

      <div className="inspector-actions">
        <button className="btn btn-primary" type="button" onClick={onUpdateNode} disabled={!inspectorEnabled}>
          Update Node
        </button>
        <button className="btn btn-danger" type="button" onClick={onDeleteNode} disabled={!inspectorEnabled}>
          Delete Node
        </button>
      </div>

      <h3>Connections</h3>
      <ul className="connection-list">
        {connections.length === 0 && <li className="connection-item">No connections</li>}
        {connections.map((connection) => (
          <li className="connection-item" key={getConnectionKey(connection)}>
            <span>
              {connection.output_id}:{connection.output_class} → {connection.input_id}:{connection.input_class}
            </span>
            <button className="disconnect-btn" type="button" onClick={() => onDisconnectConnection(connection)}>
              Disconnect
            </button>
          </li>
        ))}
      </ul>

      <h3>Validation</h3>
      <ValidationList errors={validationErrors} />

      <h3>Run Timeline</h3>
      <RunTimeline run={runData.run} nodes={runData.nodes} />
    </aside>
  );
}
