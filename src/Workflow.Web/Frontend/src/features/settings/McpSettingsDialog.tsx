import React from "react";
import type { TestMcpProfileResponse } from "../../shared/types/workflow";

/**
 * Что: MCP Settings окно.
 * Зачем: локальные MCP endpoints/secrets настраиваются вне workflow graph, но доступны из UI.
 * Как: редактирует JSON `mcp.json`, сохраняет его через backend и запускает connection/list-tools smoke.
 */
interface McpSettingsDialogProps {
  isOpen: boolean;
  isBusy: boolean;
  configPath: string;
  exists: boolean;
  settingsText: string;
  selectedProfile: string;
  error: string | null;
  testResult: TestMcpProfileResponse | null;
  onSettingsTextChange: (value: string) => void;
  onSelectedProfileChange: (value: string) => void;
  onSave: () => void;
  onTest: () => void;
  onClose: () => void;
}

export function McpSettingsDialog({
  isOpen,
  isBusy,
  configPath,
  exists,
  settingsText,
  selectedProfile,
  error,
  testResult,
  onSettingsTextChange,
  onSelectedProfileChange,
  onSave,
  onTest,
  onClose
}: McpSettingsDialogProps) {
  if (!isOpen) {
    return null;
  }

  return (
    <div className="modal-backdrop" role="presentation">
      <section className="settings-dialog" role="dialog" aria-modal="true" aria-labelledby="mcp-settings-title">
        <header className="settings-dialog-header">
          <div>
            <h2 id="mcp-settings-title">MCP Settings</h2>
            <p className="settings-dialog-caption">
              {configPath || "mcp.json"} · {exists ? "file exists" : "fallback from appsettings"}
            </p>
          </div>
          <button className="btn" type="button" onClick={onClose}>
            Close
          </button>
        </header>

        <label className="field-label" htmlFor="mcp-settings-json">
          mcp.json
        </label>
        <textarea
          id="mcp-settings-json"
          className="settings-json-editor"
          value={settingsText}
          spellCheck={false}
          onChange={(event) => onSettingsTextChange(event.target.value)}
        />

        <div className="settings-dialog-actions">
          <button className="btn btn-primary" type="button" disabled={isBusy} onClick={onSave}>
            Save
          </button>
          <div className="settings-test-form">
            <input
              aria-label="MCP profile"
              type="text"
              value={selectedProfile}
              placeholder="profile name"
              onChange={(event) => onSelectedProfileChange(event.target.value)}
            />
            <button className="btn" type="button" disabled={isBusy} onClick={onTest}>
              Test / List Tools
            </button>
          </div>
        </div>

        {isBusy && <p className="settings-state">Working...</p>}
        {error && <p className="settings-error">{error}</p>}
        {testResult && (
          <McpProfileTestResult result={testResult} />
        )}
      </section>
    </div>
  );
}

function McpProfileTestResult({ result }: { result: TestMcpProfileResponse }) {
  return (
    <section className="settings-test-result" aria-label="MCP test result">
      <h3>
        {result.profile} · {result.serverType} · {result.toolCount} tools
      </h3>
      <ul>
        {result.tools.length === 0 && <li>No tools exposed by this profile.</li>}
        {result.tools.map((tool) => (
          <li key={tool.name}>
            <strong>{tool.name}</strong>
            {tool.description && <span> — {tool.description}</span>}
          </li>
        ))}
      </ul>
    </section>
  );
}
