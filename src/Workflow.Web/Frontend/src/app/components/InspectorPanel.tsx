import React from "react";
import { ValidationList } from "../../features/editor/ValidationList";
import { RunTimeline } from "../../features/runs/RunTimeline";
import type {
  DrawflowConnectionShape,
  InspectorState,
  NodeTemplateConfigField,
  NodeTemplatesMap,
  RunData
} from "../../shared/types/workflow";

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
  nodeTemplates,
  connections,
  validationErrors,
  runData,
  getConnectionKey,
  onInspectorFieldChange,
  onUpdateNode,
  onDeleteNode,
  onDisconnectConnection
}: InspectorPanelProps) {
  const selectedTemplate = nodeTemplates[inspector.nodeType];
  const configFields = selectedTemplate?.configFields ?? [];
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
