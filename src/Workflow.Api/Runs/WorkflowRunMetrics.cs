using System.Diagnostics.Metrics;
using Workflow.Engine.Runtime;

namespace Workflow.Api.Runs;

/// <summary>
/// Что: in-memory метрики запуска workflow.
/// Зачем: дать быстрый observability baseline без внешнего telemetry backend.
/// Как: считает counters/histogram и отдает агрегированный snapshot через API.
/// </summary>
public sealed class WorkflowRunMetrics
{
    private readonly Meter _meter = new("Workflow.Api.Runs", "0.1.0");
    private readonly Counter<long> _runsStartedCounter;
    private readonly Counter<long> _runsCompletedCounter;
    private readonly Counter<long> _runsSucceededCounter;
    private readonly Counter<long> _runsFailedCounter;
    private readonly Counter<long> _runsDeduplicatedCounter;
    private readonly Counter<long> _nodeStatusUpdatesCounter;
    private readonly Histogram<double> _runDurationMsHistogram;

    private long _totalRunsStarted;
    private long _activeRuns;
    private long _totalRunsCompleted;
    private long _totalRunsSucceeded;
    private long _totalRunsFailed;
    private long _totalRunsDeduplicated;
    private long _totalNodeStatusUpdates;
    private long _totalCompletedRunDurationMs;

    public WorkflowRunMetrics()
    {
        _runsStartedCounter = _meter.CreateCounter<long>("workflow_runs_started_total");
        _runsCompletedCounter = _meter.CreateCounter<long>("workflow_runs_completed_total");
        _runsSucceededCounter = _meter.CreateCounter<long>("workflow_runs_succeeded_total");
        _runsFailedCounter = _meter.CreateCounter<long>("workflow_runs_failed_total");
        _runsDeduplicatedCounter = _meter.CreateCounter<long>("workflow_runs_deduplicated_total");
        _nodeStatusUpdatesCounter = _meter.CreateCounter<long>("workflow_node_status_updates_total");
        _runDurationMsHistogram = _meter.CreateHistogram<double>("workflow_run_duration_ms");
    }

    public void OnRunStarted(WorkflowRunTriggerType triggerType)
    {
        Interlocked.Increment(ref _totalRunsStarted);
        Interlocked.Increment(ref _activeRuns);
        _runsStartedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));
    }

    public void OnRunDeduplicated(WorkflowRunTriggerType triggerType)
    {
        Interlocked.Increment(ref _totalRunsDeduplicated);
        _runsDeduplicatedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));
    }

    public void OnNodeStatusChanged(WorkflowNodeRunStatus status)
    {
        Interlocked.Increment(ref _totalNodeStatusUpdates);
        _nodeStatusUpdatesCounter.Add(1, new KeyValuePair<string, object?>("node_status", status.ToString()));
    }

    public void OnRunCompleted(
        WorkflowRunStatus status,
        TimeSpan duration,
        WorkflowRunTriggerType triggerType)
    {
        Interlocked.Decrement(ref _activeRuns);
        Interlocked.Increment(ref _totalRunsCompleted);
        Interlocked.Add(ref _totalCompletedRunDurationMs, Math.Max(0L, (long)Math.Round(duration.TotalMilliseconds)));

        _runsCompletedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));
        _runDurationMsHistogram.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("status", status.ToString()),
            new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));

        if (status == WorkflowRunStatus.Succeeded)
        {
            Interlocked.Increment(ref _totalRunsSucceeded);
            _runsSucceededCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));
            return;
        }

        if (status == WorkflowRunStatus.Failed)
        {
            Interlocked.Increment(ref _totalRunsFailed);
            _runsFailedCounter.Add(1, new KeyValuePair<string, object?>("trigger_type", triggerType.ToString()));
        }
    }

    public WorkflowRunMetricsSnapshot GetSnapshot()
    {
        var completedRuns = Interlocked.Read(ref _totalRunsCompleted);
        var completedDurationMs = Interlocked.Read(ref _totalCompletedRunDurationMs);
        var averageDurationMs = completedRuns == 0
            ? 0d
            : (double)completedDurationMs / completedRuns;

        return new WorkflowRunMetricsSnapshot(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            TotalRunsStarted: Interlocked.Read(ref _totalRunsStarted),
            ActiveRuns: Interlocked.Read(ref _activeRuns),
            TotalRunsCompleted: completedRuns,
            TotalRunsSucceeded: Interlocked.Read(ref _totalRunsSucceeded),
            TotalRunsFailed: Interlocked.Read(ref _totalRunsFailed),
            TotalRunsDeduplicated: Interlocked.Read(ref _totalRunsDeduplicated),
            TotalNodeStatusUpdates: Interlocked.Read(ref _totalNodeStatusUpdates),
            AverageCompletedRunDurationMs: averageDurationMs);
    }
}

public sealed record WorkflowRunMetricsSnapshot(
    DateTimeOffset CapturedAtUtc,
    long TotalRunsStarted,
    long ActiveRuns,
    long TotalRunsCompleted,
    long TotalRunsSucceeded,
    long TotalRunsFailed,
    long TotalRunsDeduplicated,
    long TotalNodeStatusUpdates,
    double AverageCompletedRunDurationMs);
