using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Builtin;

/// <summary>
/// Что: встроенная стартовая нода plain-text задачи.
/// Зачем: запускать workflow без Jira, когда вход — только текст задачи.
/// Как: читает config.taskText (или config.text), нормализует payload и пишет диагностический лог.
/// </summary>
public sealed class TaskTextInputNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "task_text_input",
        Label: "Task Text Input",
        Description: "Start from plain text task",
        Inputs: 0,
        Outputs: 1,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "taskText",
                Label: "Task Text",
                FieldType: "textarea",
                Description: "Plain-text task description used as workflow input.",
                Required: true,
                Multiline: true,
                Placeholder: "Paste or type task details...")
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor("output_1", "Task", WorkflowPortChannels.Data)
        ]);

    public Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);

        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);

        var taskText = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "taskText")
                       ?? WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "text");
        if (string.IsNullOrWhiteSpace(taskText))
        {
            throw new InvalidOperationException(
                "task_text_input requires non-empty config.taskText (or config.text).");
        }

        taskText = taskText.Trim();
        payload["task_source"] = "task_text_input";
        payload["task_text"] = taskText;
        payload["task"] = new JsonObject
        {
            ["type"] = "text",
            ["text"] = taskText,
            ["inputMode"] = "plain_text",
            ["nodeId"] = context.Node.Id,
            ["nodeName"] = context.Node.Name
        };

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"Task text input captured ({taskText.Length} chars)."
        });

        return Task.FromResult(payload);
    }
}
