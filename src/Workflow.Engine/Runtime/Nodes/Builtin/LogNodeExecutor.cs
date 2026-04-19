using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Builtin;

/// <summary>
/// Что: встроенная логирующая нода.
/// Зачем: писать диагностические сообщения в timeline run.
/// Как: применяет set/remove и добавляет лог-сообщение из config.message.
/// </summary>
public sealed class LogNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "log",
        Label: "Log",
        Description: "Write execution log",
        Inputs: 1,
        Outputs: 1,
        InputPorts:
        [
            new WorkflowNodePortDescriptor("input_1", "Data", WorkflowPortChannels.Data, Required: true)
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor("output_1", "Data", WorkflowPortChannels.Data)
        ]);

    public Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);

        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);
        var message = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "message")
                      ?? $"Node '{context.Node.Name}' executed.";

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = message
        });

        return Task.FromResult(payload);
    }
}
