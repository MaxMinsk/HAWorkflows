import React from "react";
import type { RunNodeState, RunState } from "../../shared/types/workflow";

/**
 * Что: компонент отображения timeline и логов run.
 * Зачем: отделить визуализацию run-прогресса от orchestration в App.
 * Как: рендерит статусы нод и последние лог-записи.
 */
interface RunTimelineProps {
  run: RunState | null;
  nodes: RunNodeState[];
}

export function RunTimeline({ run, nodes }: RunTimelineProps) {
  const hasRun = Boolean(run);
  const runIdLabel = run?.runId || "none";
  const runNodes = Array.isArray(nodes) ? nodes : [];
  const logs = Array.isArray(run?.logs) ? run.logs.slice(-8) : [];

  return (
    <>
      <div className="meta-line">
        <span>Last Run:</span>
        <span>{runIdLabel}</span>
      </div>

      <ul className="timeline-list">
        {!hasRun && <li className="timeline-item">No run data</li>}
        {hasRun && runNodes.length === 0 && <li className="timeline-item">No node timeline yet</li>}
        {hasRun &&
          runNodes.map((node) => {
            const statusClass = normalizeStatusClass(node.status);
            return (
              <li className="timeline-item" key={node.nodeId}>
                <div className="timeline-item-head">
                  <span className="timeline-item-name">
                    {node.nodeName || node.nodeId} ({node.nodeId})
                  </span>
                  <span className={`timeline-status ${statusClass}`}>{normalizeStatusLabel(node.status)}</span>
                </div>
                <div className="timeline-item-meta">
                  start {formatUtcTime(node.startedAtUtc)} · end {formatUtcTime(node.completedAtUtc)}
                </div>
                {node.routeReason && (
                  <div className="timeline-item-meta">
                    route {node.routeReason} · tier {node.selectedTier || "n/a"} · model {node.selectedModel || "n/a"}
                  </div>
                )}
                {node.error && <div className="timeline-item-meta">error: {node.error}</div>}
              </li>
            );
          })}
      </ul>

      <ul className="timeline-list timeline-log-list">
        {logs.map((entry, index) => (
          <li className="timeline-item" key={`${entry.timestampUtc}-${index}`}>
            <div className="timeline-item-head">
              <span className="timeline-item-name">{entry.nodeId || "log"}</span>
              <span className="timeline-status pending">{formatUtcTime(entry.timestampUtc)}</span>
            </div>
            <div className="timeline-item-meta">{entry.message || ""}</div>
          </li>
        ))}
      </ul>
    </>
  );
}

function normalizeStatusLabel(status: string | null | undefined): string {
  if (!status || typeof status !== "string") {
    return "Unknown";
  }

  return status;
}

function normalizeStatusClass(status: string | null | undefined): string {
  if (!status || typeof status !== "string") {
    return "pending";
  }

  const lowered = status.toLowerCase();
  if (lowered === "running") {
    return "running";
  }

  if (lowered === "succeeded") {
    return "succeeded";
  }

  if (lowered === "failed") {
    return "failed";
  }

  if (lowered === "skipped") {
    return "skipped";
  }

  return "pending";
}

function formatUtcTime(value: string | null | undefined): string {
  if (!value) {
    return "n/a";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "n/a";
  }

  return date.toISOString().slice(11, 19);
}
