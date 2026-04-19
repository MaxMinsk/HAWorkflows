using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Builtin;

/// <summary>
/// Что: встроенная входная нода.
/// Зачем: точка старта pipeline, применяет базовую set/remove конфигурацию.
/// Как: берет merged inbound payload (или run input) и модифицирует его по config.
/// </summary>
public sealed class InputNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "input",
        Label: "Input",
        Description: "Start signal",
        Inputs: 0,
        Outputs: 1,
        OutputPorts:
        [
            new WorkflowNodePortDescriptor(
                "output_1",
                "Data",
                WorkflowPortChannels.Data,
                Description: "Initial run payload passed to the workflow.",
                ProducesKinds: ["run_input", "workflow_data"])
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
