import React from "react";
import type { RunNodeState, RunState, WorkflowArtifactDescriptor } from "../../shared/types/workflow";
import { useArtifactBrowser } from "./useArtifactBrowser";

/**
 * Что: компонент отображения timeline и логов run.
 * Зачем: отделить визуализацию run-прогресса от orchestration в App.
 * Как: рендерит статусы нод и последние лог-записи.
 */
interface RunTimelineProps {
  run: RunState | null;
  nodes: RunNodeState[];
  artifacts: WorkflowArtifactDescriptor[];
  onResumeRun: (runId: string) => void;
}

export function RunTimeline({ run, nodes, artifacts, onResumeRun }: RunTimelineProps) {
  const hasRun = Boolean(run);
  const runIdLabel = run?.runId || "none";
  const runNodes = Array.isArray(nodes) ? nodes : [];
  const runArtifacts = Array.isArray(artifacts) ? artifacts : [];
  const logs = Array.isArray(run?.logs) ? run.logs.slice(-8) : [];
  const {
    selectedArtifact,
    isLoadingArtifact,
    artifactError,
    copyStatus,
    openArtifact,
    copyArtifactRef
  } = useArtifactBrowser(run?.runId);

  return (
    <>
      <div className="meta-line">
        <span>Last Run:</span>
        <span>{runIdLabel}</span>
      </div>
      {hasRun && (
        <div className="meta-line">
          <span>Run Duration:</span>
          <span>{formatDuration(run?.startedAtUtc, run?.completedAtUtc)}</span>
        </div>
      )}
      {hasRun && (
        <div className="meta-line">
          <span>Workflow Version:</span>
          <span>{run?.workflowVersion ? `v${run.workflowVersion}` : "inline preview"}</span>
        </div>
      )}
      {hasRun && (
        <div className="meta-line">
          <span>Checkpoint:</span>
          <span>{formatUtcTime(run?.checkpointedAtUtc)}</span>
        </div>
      )}
      {hasRun && run?.canResume && (
        <div className="palette-actions">
          <button className="btn" type="button" onClick={() => onResumeRun(run.runId)}>
            Resume Run
          </button>
        </div>
      )}

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
                  start {formatUtcTime(node.startedAtUtc)} · end {formatUtcTime(node.completedAtUtc)} · duration{" "}
                  {formatDuration(node.startedAtUtc, node.completedAtUtc)}
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

      <h3>Artifacts</h3>
      <ul className="timeline-list artifact-list">
        {!hasRun && <li className="timeline-item">No run data</li>}
        {hasRun && runArtifacts.length === 0 && <li className="timeline-item">No artifacts yet</li>}
        {hasRun &&
          runArtifacts.map((artifact) => (
            <li className="timeline-item artifact-item" key={artifact.artifactId}>
              <div className="timeline-item-head">
                <span className="timeline-item-name">{artifact.name || artifact.artifactId}</span>
                <span className="timeline-status pending">{artifact.artifactType || "file"}</span>
              </div>
              <div className="timeline-item-meta">
                node {artifact.nodeId || "n/a"} · {formatBytes(artifact.sizeBytes)} · {formatUtcTime(artifact.createdAtUtc)}
              </div>
              <div className="timeline-item-meta">{artifact.uri || artifact.relativePath || artifact.artifactId}</div>
              <div className="artifact-actions">
                <button className="disconnect-btn" type="button" onClick={() => openArtifact(artifact)}>
                  View
                </button>
                <button className="disconnect-btn" type="button" onClick={() => copyArtifactRef(artifact)}>
                  Copy Ref
                </button>
              </div>
            </li>
          ))}
      </ul>

      {(isLoadingArtifact || selectedArtifact || artifactError || copyStatus) && (
        <div className="artifact-preview">
          <div className="timeline-item-head">
            <span className="timeline-item-name">
              {selectedArtifact?.descriptor.name || (isLoadingArtifact ? "Loading artifact..." : "Artifact")}
            </span>
            {selectedArtifact && (
              <span className="timeline-status pending">{selectedArtifact.descriptor.mediaType || "text/plain"}</span>
            )}
          </div>
          {artifactError && <div className="timeline-item-meta artifact-error">{artifactError}</div>}
          {copyStatus && <div className="timeline-item-meta artifact-copy-status">{copyStatus}</div>}
          {isLoadingArtifact && <div className="timeline-item-meta">Loading content...</div>}
          {selectedArtifact && (
            <pre className="artifact-content">{formatArtifactContent(selectedArtifact.content)}</pre>
          )}
        </div>
      )}
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

function formatDuration(startValue: string | null | undefined, endValue: string | null | undefined): string {
  if (!startValue || !endValue) {
    return "n/a";
  }

  const startedAt = new Date(startValue);
  const completedAt = new Date(endValue);
  if (Number.isNaN(startedAt.getTime()) || Number.isNaN(completedAt.getTime())) {
    return "n/a";
  }

  const durationMs = Math.max(0, completedAt.getTime() - startedAt.getTime());
  if (durationMs < 1000) {
    return `${durationMs}ms`;
  }

  return `${(durationMs / 1000).toFixed(2)}s`;
}

function formatBytes(value: number | null | undefined): string {
  if (typeof value !== "number" || Number.isNaN(value) || value < 0) {
    return "n/a";
  }

  if (value < 1024) {
    return `${value} B`;
  }

  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }

  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function formatArtifactContent(content: string): string {
  if (!content.trim()) {
    return "";
  }

  try {
    return JSON.stringify(JSON.parse(content), null, 2);
  } catch {
    return content;
  }
}
