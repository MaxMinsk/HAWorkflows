using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Local;

/// <summary>
/// Что: локальная нода-заглушка для подготовки Jira-контекста.
/// Зачем: предоставить local-only тип ноды для пайплайнов разработки без влияния на release-профиль.
/// Как: читает config.issueKey/config.summary, добавляет jira_* поля и пишет лог.
/// </summary>
public sealed class JiraCollectLocalNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "jira_collect",
        Label: "Jira Collect (Local)",
        Description: "Collect Jira issue context (local pipeline)",
        Inputs: 1,
        Outputs: 1,
        IsLocal: true,
        Pack: WorkflowNodePacks.LocalDevelopment,
        Source: WorkflowNodeSources.Local,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "issueKey",
                Label: "Issue Key",
                FieldType: "text",
                Description: "Jira key (example: GAME-1234).",
                Placeholder: "GAME-1234"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "summary",
                Label: "Issue Summary",
                FieldType: "textarea",
                Description: "Optional short summary to attach to payload.",
                Multiline: true,
                Placeholder: "Fix null reference in reward pipeline..."),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "labels",
                Label: "Labels",
                FieldType: "text",
                Description: "Comma-separated labels for local routing hints.",
                Placeholder: "bugfix,backend,hotfix"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "issueType",
                Label: "Issue Type",
                FieldType: "text",
                Description: "Optional issue type hint (Bug, Story, Feature).",
                Placeholder: "Bug"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "severity",
                Label: "Severity",
                FieldType: "text",
                Description: "Optional severity/priority hint (P1, critical...).",
                Placeholder: "P2")
        ]);

    public Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);

        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);

        var issueKey = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "issueKey");
        if (!string.IsNullOrWhiteSpace(issueKey))
        {
            payload["jira_issue_key"] = issueKey;
        }

        var summary = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "summary");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            payload["jira_summary"] = summary;
        }

        var labels = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "labels");
        if (!string.IsNullOrWhiteSpace(labels))
        {
            payload["jira_labels"] = new JsonArray(
                labels
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(label => (JsonNode?)label)
                    .ToArray());
        }

        var issueType = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "issueType");
        if (!string.IsNullOrWhiteSpace(issueType))
        {
            payload["jira_issue_type"] = issueType.Trim();
        }

        var severity = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "severity");
        if (!string.IsNullOrWhiteSpace(severity))
        {
            payload["jira_severity"] = severity.Trim();
        }

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = !string.IsNullOrWhiteSpace(issueKey)
                ? $"Local Jira context collected for {issueKey}."
                : "Local Jira context node executed."
        });

        return Task.FromResult(payload);
    }
}
