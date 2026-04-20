import React from "react";
import { ValidationList } from "../../features/editor/ValidationList";
import type {
  ConnectionAssistantSuggestion,
  DrawflowConnectionShape,
  InspectorState,
  NodeTemplateConfigField,
  NodeTemplatePort,
  NodeTemplatesMap,
  RunData
} from "../../shared/types/workflow";
import { formatPortRequirement, getInputPorts, getOutputPorts } from "../../features/workflow/ports/nodePorts";

/**
 * Что: правая панель инспектора ноды и run diagnostics.
 * Зачем: отделить большой блок форм/списков от App-композиции.
 * Как: получает состояние и обработчики через props.
 */
interface InspectorPanelProps {
  inspector: InspectorState;
  inspectorEnabled: boolean;
  nodeTemplates: NodeTemplatesMap;
  connections: DrawflowConnectionShape[];
  connectionAssistantSuggestions: ConnectionAssistantSuggestion[];
  validationErrors: string[];
  runData: RunData;
  getConnectionKey: (connection: DrawflowConnectionShape) => string;
  onInspectorFieldChange: (field: keyof InspectorState, value: string) => void;
  onUpdateNode: () => void;
  onDeleteNode: () => void;
  onDisconnectConnection: (connection: DrawflowConnectionShape) => void;
  onAddSuggestedNode: (suggestion: ConnectionAssistantSuggestion) => void;
  onOpenRunDetails: () => void;
}

export function InspectorPanel({
  inspector,
  inspectorEnabled,
  nodeTemplates,
  connections,
  connectionAssistantSuggestions,
  validationErrors,
  runData,
  getConnectionKey,
  onInspectorFieldChange,
  onUpdateNode,
  onDeleteNode,
  onDisconnectConnection,
  onAddSuggestedNode,
  onOpenRunDetails
}: InspectorPanelProps) {
  const selectedTemplate = nodeTemplates[inspector.nodeType];
  const configFields = selectedTemplate?.configFields ?? [];
  const inputPorts = getInputPorts(selectedTemplate);
  const outputPorts = getOutputPorts(selectedTemplate);
  const selectedNodeId = Number.parseInt(inspector.nodeId, 10);
  const parsedConfig = parseConfigObject(inspector.nodeConfigText);
  const missingRequiredFields = configFields.filter((field) => field.required && isEmptyFieldValue(readFieldValue(parsedConfig, field)));
  const hasConfigValidationErrors = missingRequiredFields.length > 0;

  function onConfigFieldChange(field: NodeTemplateConfigField, nextValue: string) {
    const nextConfig: Record<string, unknown> = { ...parsedConfig };
    if (!field.required && isEmptyFieldValue(nextValue)) {
      delete nextConfig[field.key];
    } else {
      nextConfig[field.key] = nextValue;
    }

    onInspectorFieldChange("nodeConfigText", JSON.stringify(nextConfig, null, 2));
  }

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

      {configFields.length > 0 && (
        <>
          <h3>Node Settings</h3>
          {configFields.map((field) => {
            const value = readFieldValue(parsedConfig, field);
            const elementId = `node-config-${field.key}`;

            return (
              <div className="typed-config-field" key={field.key}>
                <label className="field-label" htmlFor={elementId}>
                  {field.label}
                </label>
                {field.description && <p className="field-help">{field.description}</p>}
                {field.fieldType === "select" ? (
                  <select
                    id={elementId}
                    value={value}
                    onChange={(event) => onConfigFieldChange(field, event.target.value)}
                  >
                    {!field.required && !(field.options ?? []).some((option) => option.value === "") && (
                      <option value="">Not set</option>
                    )}
                    {(field.options ?? []).map((option) => (
                      <option key={`${field.key}:${option.value}`} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                ) : field.fieldType === "textarea" || field.multiline ? (
                  <textarea
                    id={elementId}
                    rows={4}
                    placeholder={field.placeholder ?? ""}
                    value={value}
                    onChange={(event) => onConfigFieldChange(field, event.target.value)}
                  />
                ) : (
                  <input
                    id={elementId}
                    type="text"
                    placeholder={field.placeholder ?? ""}
                    value={value}
                    onChange={(event) => onConfigFieldChange(field, event.target.value)}
                  />
                )}
              </div>
            );
          })}

          {missingRequiredFields.length > 0 && (
            <ul className="typed-config-errors">
              {missingRequiredFields.map((field) => (
                <li key={`required:${field.key}`}>{field.label}: required</li>
              ))}
            </ul>
          )}
        </>
      )}

      <details className="advanced-config">
        <summary>Advanced JSON Config</summary>
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
      </details>

      <div className="inspector-actions">
        <button
          className="btn btn-primary"
          type="button"
          onClick={onUpdateNode}
          disabled={!inspectorEnabled || hasConfigValidationErrors}
        >
          Update Node
        </button>
        <button className="btn btn-danger" type="button" onClick={onDeleteNode} disabled={!inspectorEnabled}>
          Delete Node
        </button>
      </div>

      {selectedTemplate && (
        <>
          <h3>Inputs / Outputs</h3>
          <div className="io-panel">
            <PortList
              title="Inputs"
              direction="input"
              ports={inputPorts}
              connections={connections}
              selectedNodeId={selectedNodeId}
            />
            <PortList
              title="Outputs"
              direction="output"
              ports={outputPorts}
              connections={connections}
              selectedNodeId={selectedNodeId}
            />
          </div>
        </>
      )}

      {connectionAssistantSuggestions.length > 0 && (
        <>
          <h3>Suggested Next Nodes</h3>
          <ul className="suggestion-list">
            {connectionAssistantSuggestions.map((suggestion) => (
              <li className="suggestion-item" key={suggestion.id}>
                <div className="suggestion-item-info">
                  <span className="suggestion-item-target">{suggestion.targetNodeLabel}</span>
                  <span className="suggestion-item-port">
                    {suggestion.sourcePortLabel} → {suggestion.targetPortLabel}
                  </span>
                  <span className="suggestion-item-reason">{suggestion.reason}</span>
                </div>
                <button
                  className="suggestion-add-btn"
                  type="button"
                  onClick={() => onAddSuggestedNode(suggestion)}
                >
                  Add
                </button>
              </li>
            ))}
          </ul>
        </>
      )}

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

      <h3>Run</h3>
      <RunSummaryCompact runData={runData} onOpenRunDetails={onOpenRunDetails} />
    </aside>
  );
}

interface PortListProps {
  title: string;
  direction: "input" | "output";
  ports: NodeTemplatePort[];
  connections: DrawflowConnectionShape[];
  selectedNodeId: number;
}

function PortList({ title, direction, ports, connections, selectedNodeId }: PortListProps) {
  return (
    <section className="io-port-group" aria-label={title}>
      <div className="io-port-group-title">{title}</div>
      {ports.length === 0 && <div className="io-port-empty">No {direction}s</div>}
      {ports.map((port) => {
        const portConnections = getConnectionsForPort(direction, selectedNodeId, port, connections);
        const isMissingRequiredInput = direction === "input" && port.required && portConnections.length === 0;

        return (
          <div className={`io-port-card ${isMissingRequiredInput ? "missing-required" : ""}`} key={`${direction}:${port.id}`}>
            <div className="io-port-head">
              <span className="io-port-name">{port.label}</span>
              <span className={`io-port-badge ${port.required ? "required" : ""}`}>
                {formatPortRequirement(port, direction)}
              </span>
            </div>
            <div className="io-port-meta">
              <span>{port.id}</span>
              <span>{port.channel}</span>
              <span>{port.allowMultiple === false ? "single connection" : "multiple connections allowed"}</span>
            </div>
            {port.description && <div className="io-port-description">{port.description}</div>}
            {port.acceptedKinds && port.acceptedKinds.length > 0 && (
              <div className="io-port-kinds">accepts: {port.acceptedKinds.join(", ")}</div>
            )}
            {port.producesKinds && port.producesKinds.length > 0 && (
              <div className="io-port-kinds">produces: {port.producesKinds.join(", ")}</div>
            )}
            {port.exampleSources && port.exampleSources.length > 0 && (
              <div className="io-port-kinds">examples: {port.exampleSources.join(", ")}</div>
            )}
            {port.controlConditionKey && (
              <div className="io-port-kinds">condition: {port.controlConditionKey}</div>
            )}
            <div className="io-port-state">{formatPortConnections(direction, portConnections)}</div>
            {direction === "input" && portConnections.length === 0 && port.fallbackDescription && (
              <div className="io-port-kinds">fallback: {port.fallbackDescription}</div>
            )}
            {isMissingRequiredInput && <div className="io-port-warning">Required input is not connected.</div>}
          </div>
        );
      })}
    </section>
  );
}

function getConnectionsForPort(
  direction: "input" | "output",
  selectedNodeId: number,
  port: NodeTemplatePort,
  connections: DrawflowConnectionShape[]
): DrawflowConnectionShape[] {
  if (!Number.isFinite(selectedNodeId)) {
    return [];
  }

  return connections.filter((connection) => {
    if (direction === "input") {
      return connection.input_id === selectedNodeId && connection.input_class === port.id;
    }

    return connection.output_id === selectedNodeId && connection.output_class === port.id;
  });
}

function formatPortConnections(direction: "input" | "output", connections: DrawflowConnectionShape[]): string {
  if (connections.length === 0) {
    return "not connected";
  }

  const labels = connections.map((connection) => (
    direction === "input"
      ? `${connection.output_id}:${connection.output_class}`
      : `${connection.input_id}:${connection.input_class}`
  ));

  return `connected to ${labels.join(", ")}`;
}

function parseConfigObject(configText: string): Record<string, unknown> {
  if (!configText.trim()) {
    return {};
  }

  try {
    const parsed = JSON.parse(configText);
    if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") {
      return {};
    }

    return parsed as Record<string, unknown>;
  } catch {
    return {};
  }
}

function readFieldValue(config: Record<string, unknown>, field: NodeTemplateConfigField): string {
  const rawValue = config[field.key];
  if (rawValue === undefined || rawValue === null) {
    return field.defaultValue ?? "";
  }

  if (typeof rawValue === "string") {
    return rawValue;
  }

  return String(rawValue);
}

function isEmptyFieldValue(value: string): boolean {
  return value.trim().length === 0;
}

function RunSummaryCompact({ runData, onOpenRunDetails }: { runData: RunData; onOpenRunDetails: () => void }) {
  const { run, nodes, artifacts } = runData;
  if (!run) {
    return <div className="run-summary-compact"><span style={{ color: "var(--muted)", fontSize: 12 }}>No run data</span></div>;
  }

  const succeeded = nodes.filter((n) => (n.status ?? "").toLowerCase() === "succeeded").length;
  const failed = nodes.filter((n) => (n.status ?? "").toLowerCase() === "failed").length;
  const statusClass = normalizeRunStatusClass(run.status);

  return (
    <div className="run-summary-compact">
      <div className="run-summary-status-line">
        <span className={`timeline-status ${statusClass}`}>{run.status}</span>
        <span style={{ fontSize: 11, color: "var(--muted)" }}>{run.runId.slice(0, 8)}</span>
      </div>
      <div className="run-summary-counts">
        <span>{nodes.length} nodes</span>
        <span style={{ color: "#16a34a" }}>{succeeded} ok</span>
        {failed > 0 && <span style={{ color: "#dc2626" }}>{failed} fail</span>}
        <span>{artifacts.length} artifacts</span>
      </div>
      {run.error && <div style={{ color: "#7f1d1d", fontSize: 11, marginTop: 2 }}>{run.error}</div>}
      <button className="btn" type="button" onClick={onOpenRunDetails} style={{ marginTop: 4 }}>
        Open Run Details
      </button>
    </div>
  );
}

function normalizeRunStatusClass(status: string): string {
  const lowered = status.toLowerCase();
  if (lowered === "running" || lowered === "pending") return "running";
  if (lowered === "succeeded") return "succeeded";
  if (lowered === "failed") return "failed";
  return "pending";
}
