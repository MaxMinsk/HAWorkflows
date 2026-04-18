import React from "react";

/**
 * Что: верхняя toolbar-панель.
 * Зачем: отделить командные действия UI от App-композиции.
 * Как: принимает callbacks и status из контейнера.
 */
interface ToolbarProps {
  statusText: string;
  statusDotColor: string;
  onLoad: () => void;
  onSave: () => void;
  onRun: () => void;
  onStop: () => void;
  onOpenSettings: () => void;
}

export function Toolbar({
  statusText,
  statusDotColor,
  onLoad,
  onSave,
  onRun,
  onStop,
  onOpenSettings
}: ToolbarProps) {
  return (
    <header className="toolbar" aria-label="Workflow toolbar">
      <div className="toolbar-brand">
        <strong>Workflow Builder</strong>
        <span className="version-pill">Schema v1 · React</span>
      </div>

      <div className="toolbar-actions">
        <button className="btn" type="button" onClick={onLoad}>
          Load
        </button>
        <button className="btn btn-primary" type="button" onClick={onSave}>
          Save
        </button>
        <button className="btn" type="button" onClick={onRun}>
          Run
        </button>
        <button className="btn" type="button" onClick={onStop}>
          Stop
        </button>
        <button className="btn" type="button" onClick={onOpenSettings}>
          Settings
        </button>
        <button className="btn" type="button" disabled>
          Deploy
        </button>
      </div>

      <div className="toolbar-status">
        <span className="status-dot" style={{ background: statusDotColor }} />
        <span>{statusText}</span>
      </div>
    </header>
  );
}
