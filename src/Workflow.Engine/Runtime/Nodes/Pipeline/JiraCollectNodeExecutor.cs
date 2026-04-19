using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Pipeline;

/// <summary>
/// Что: базовая local-first нода-заглушка для подготовки Jira-контекста.
/// Зачем: дать pipeline общий тип `jira_collect` без отдельного remote/local режима.
/// Как: читает config.issueKey/config.summary, добавляет jira_* поля и пишет лог.
/// </summary>
public sealed class JiraCollectNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "jira_collect",
        Label: "Jira Collect",
        Description: "Collect Jira issue context",
        Inputs: 2,
        Outputs: 1,
        Pack: WorkflowNodePacks.Core,
        Source: WorkflowNodeSources.BuiltIn,
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
        ],
        InputPorts:
        [
            new WorkflowNodePortDescriptor(
                "input_1",
                "Data",
                WorkflowPortChannels.Data,
                AcceptedKinds: ["task_text", "workflow_data"],
                Description: "Optional payload to enrich with manually configured Jira fields.",
                FallbackDescription: "When not connected, the node starts from run input and its own config.",
                ExampleSources: ["task_text_input.output_1", "template_select.output_1"]),
            new WorkflowNodePortDescriptor(
                "input_2",
                "Run Gate",
                WorkflowPortChannels.ControlOk,
                Description: "Optional control gate for conditional Jira collection.",
                FallbackDescription: "When not connected, the node is eligible to run.")
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor(
                "output_1",
                "Data",
                WorkflowPortChannels.Data,
                Description: "Payload enriched with Jira issue fields.",
                ProducesKinds: ["jira_issue", "workflow_data"])
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
                ? $"Jira context collected for {issueKey}."
                : "Jira context node executed."
        });

        return Task.FromResult(payload);
    }
}
