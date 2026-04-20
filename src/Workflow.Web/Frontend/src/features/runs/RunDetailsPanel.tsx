import React, { useMemo, useState } from "react";
import type { RunData, RunNodeState, RunState, WorkflowArtifactDescriptor } from "../../shared/types/workflow";
import { useArtifactBrowser } from "./useArtifactBrowser";

interface RunDetailsPanelProps {
  runData: RunData;
  onResumeRun: (runId: string) => void;
  onClose: () => void;
}

type NodeStatusFilter = "all" | "running" | "succeeded" | "failed" | "skipped";

export function RunDetailsPanel({ runData, onResumeRun, onClose }: RunDetailsPanelProps) {
  const { run, nodes, artifacts } = runData;
  const [statusFilter, setStatusFilter] = useState<NodeStatusFilter>("all");
  const {
    selectedArtifactId,
    selectedArtifact,
    isLoadingArtifact,
    artifactError,
    copyStatus,
    openArtifact,
    copyArtifactRef
  } = useArtifactBrowser(run?.runId);

  const filteredNodes = useMemo(() => {
    if (statusFilter === "all") return nodes;
    return nodes.filter((n) => normalizeStatusClass(n.status) === statusFilter);
  }, [nodes, statusFilter]);

  const logs = Array.isArray(run?.logs) ? run.logs : [];
  const statusCounts = useMemo(() => countStatuses(nodes), [nodes]);

  return (
    <div className="run-details-backdrop" onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="run-details-panel" role="dialog" aria-label="Run Details">
        <div className="run-details-header">
          <h2>Run Details</h2>
          <button className="btn" type="button" onClick={onClose}>Close</button>
        </div>

        <div className="run-details-body">
          <RunInfoSection run={run} onResumeRun={onResumeRun} />

          {run?.error && (
            <section className="run-details-section">
              <h3>Error</h3>
              <div className="run-details-error">{run.error}</div>
            </section>
          )}

          <section className="run-details-section">
            <h3>Node Timeline ({filteredNodes.length}/{nodes.length})</h3>
            <div className="run-details-filters">
              {(["all", "running", "succeeded", "failed", "skipped"] as const).map((f) => (
                <button
                  key={f}
                  className={`run-details-filter-btn ${statusFilter === f ? "active" : ""}`}
                  type="button"
                  onClick={() => setStatusFilter(f)}
                >
                  {f === "all" ? `All (${nodes.length})` : `${f} (${statusCounts[f]})`}
                </button>
              ))}
            </div>
            <ul className="timeline-list">
              {filteredNodes.length === 0 && <li className="timeline-item">No nodes match filter</li>}
              {filteredNodes.map((node) => (
                <NodeTimelineItem key={node.nodeId} node={node} />
              ))}
            </ul>
          </section>

          <section className="run-details-section">
            <h3>Logs ({logs.length})</h3>
            <div className="run-details-log-stream">
              {logs.length === 0 && <div>No logs yet</div>}
              {logs.map((entry, index) => (
                <div className="run-details-log-entry" key={`${entry.timestampUtc}-${index}`}>
                  <span className="run-details-log-time">{formatUtcTime(entry.timestampUtc)}</span>
                  <span className="run-details-log-node">{entry.nodeId || "sys"}</span>
                  <span>{entry.message || ""}</span>
                </div>
              ))}
            </div>
          </section>

          <section className="run-details-section">
            <h3>Artifacts ({artifacts.length})</h3>
            <ArtifactsList
              runId={run?.runId}
              artifacts={artifacts}
              selectedArtifactId={selectedArtifactId}
              selectedArtifact={selectedArtifact}
              isLoadingArtifact={isLoadingArtifact}
              artifactError={artifactError}
              copyStatus={copyStatus}
              openArtifact={openArtifact}
              copyArtifactRef={copyArtifactRef}
            />
          </section>
        </div>
      </div>
    </div>
  );
}

function RunInfoSection({ run, onResumeRun }: { run: RunState | null; onResumeRun: (runId: string) => void }) {
  if (!run) {
    return (
      <section className="run-details-section">
        <h3>Run Info</h3>
        <p style={{ color: "var(--muted)", fontSize: 12 }}>No run data available.</p>
      </section>
    );
  }

  return (
    <section className="run-details-section">
      <h3>Run Info</h3>
      <dl className="run-details-meta">
        <dt>Run ID</dt>
        <dd>{run.runId}</dd>
        <dt>Status</dt>
        <dd><span className={`timeline-status ${normalizeStatusClass(run.status)}`}>{run.status}</span></dd>
        <dt>Workflow</dt>
        <dd>{run.workflowId ? `${run.workflowId} v${run.workflowVersion ?? "?"}` : "inline preview"}</dd>
        <dt>Duration</dt>
        <dd>{formatDuration(run.startedAtUtc, run.completedAtUtc)}</dd>
        <dt>Started</dt>
        <dd>{formatUtcTime(run.startedAtUtc)}</dd>
        <dt>Completed</dt>
        <dd>{formatUtcTime(run.completedAtUtc)}</dd>
        <dt>Checkpoint</dt>
        <dd>{formatUtcTime(run.checkpointedAtUtc)}</dd>
      </dl>
      {run.canResume && (
        <div style={{ marginTop: 8 }}>
          <button className="btn" type="button" onClick={() => onResumeRun(run.runId)}>
            Resume Checkpoint
          </button>
        </div>
      )}
    </section>
  );
}

function NodeTimelineItem({ node }: { node: RunNodeState }) {
  const statusClass = normalizeStatusClass(node.status);
  return (
    <li className="timeline-item">
      <div className="timeline-item-head">
        <span className="timeline-item-name">{node.nodeName || node.nodeId} ({node.nodeId})</span>
        <span className={`timeline-status ${statusClass}`}>{node.status || "Pending"}</span>
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
      {node.error && <div className="timeline-item-meta" style={{ color: "#7f1d1d" }}>error: {node.error}</div>}
    </li>
  );
}

interface ArtifactsListProps {
  runId: string | null | undefined;
  artifacts: WorkflowArtifactDescriptor[];
  selectedArtifactId: string | null;
  selectedArtifact: { descriptor: WorkflowArtifactDescriptor; content: string } | null;
  isLoadingArtifact: boolean;
  artifactError: string | null;
  copyStatus: string | null;
  openArtifact: (artifact: WorkflowArtifactDescriptor) => void;
  copyArtifactRef: (artifact: WorkflowArtifactDescriptor) => void;
}

function ArtifactsList({
  artifacts,
  selectedArtifactId,
  selectedArtifact,
  isLoadingArtifact,
  artifactError,
  copyStatus,
  openArtifact,
  copyArtifactRef
}: ArtifactsListProps) {
  if (artifacts.length === 0) {
    return <div style={{ color: "var(--muted)", fontSize: 12 }}>No artifacts yet</div>;
  }

  return (
    <ul className="timeline-list artifact-list">
      {artifacts.map((artifact) => {
        const isSelected = selectedArtifactId === artifact.artifactId;
        return (
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
                {isSelected && isLoadingArtifact ? "Loading..." : isSelected && selectedArtifact ? "Hide" : "View"}
              </button>
              <button className="disconnect-btn" type="button" onClick={() => copyArtifactRef(artifact)}>
                Copy Ref
              </button>
            </div>
            {isSelected && (isLoadingArtifact || selectedArtifact || artifactError || copyStatus) && (
              <div className="artifact-preview">
                <div className="timeline-item-head">
                  <span className="timeline-item-name">
                    {selectedArtifact?.descriptor.name || (isLoadingArtifact ? "Loading..." : artifact.name)}
                  </span>
                  {selectedArtifact && (
                    <span className="timeline-status pending">{selectedArtifact.descriptor.mediaType}</span>
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
          </li>
        );
      })}
    </ul>
  );
}

function countStatuses(nodes: RunNodeState[]): Record<string, number> {
  const counts: Record<string, number> = { running: 0, succeeded: 0, failed: 0, skipped: 0 };
  for (const node of nodes) {
    const key = normalizeStatusClass(node.status);
    counts[key] = (counts[key] ?? 0) + 1;
  }
  return counts;
}

function normalizeStatusClass(status: string | null | undefined): string {
  const lowered = (status ?? "").toLowerCase();
  if (lowered === "running") return "running";
  if (lowered === "succeeded") return "succeeded";
  if (lowered === "failed") return "failed";
  if (lowered === "skipped") return "skipped";
  return "pending";
}

function formatUtcTime(value: string | null | undefined): string {
  if (!value) return "n/a";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "n/a" : date.toISOString().slice(11, 19);
}

function formatDuration(startValue: string | null | undefined, endValue: string | null | undefined): string {
  if (!startValue || !endValue) return "n/a";
  const durationMs = Math.max(0, new Date(endValue).getTime() - new Date(startValue).getTime());
  return durationMs < 1000 ? `${durationMs}ms` : `${(durationMs / 1000).toFixed(2)}s`;
}

function formatBytes(value: number | null | undefined): string {
  if (typeof value !== "number" || value < 0) return "n/a";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function formatArtifactContent(content: string): string {
  if (!content.trim()) return "";
  try { return JSON.stringify(JSON.parse(content), null, 2); } catch { return content; }
}
