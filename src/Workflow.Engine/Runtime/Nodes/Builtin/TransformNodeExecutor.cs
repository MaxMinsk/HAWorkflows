using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Builtin;

/// <summary>
/// Что: встроенная нода детерминированной трансформации.
/// Зачем: промежуточное преобразование payload в DAG.
/// Как: применяет set/remove конфиг к входному merged payload.
/// </summary>
public sealed class TransformNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "transform",
        Label: "Transform",
        Description: "Deterministic mapping",
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
        return Task.FromResult(payload);
    }
}
