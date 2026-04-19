using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Builtin;

/// <summary>
/// Что: встроенная выходная нода.
/// Зачем: обозначает финальный payload run.
/// Как: возвращает обработанный payload и помечена как ProducesRunOutput.
/// </summary>
public sealed class OutputNodeExecutor : IWorkflowNodeExecutor
{
    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "output",
        Label: "Output",
        Description: "Final result",
        Inputs: 1,
        Outputs: 0,
        ProducesRunOutput: true,
        InputPorts:
        [
            new WorkflowNodePortDescriptor(
                "input_1",
                "Data",
                WorkflowPortChannels.Data,
                Required: true,
                AcceptedKinds: ["workflow_data", "agent_result", "evidence_pack"],
                Description: "Final payload captured as workflow run output.",
                ExampleSources: ["agent_task.output_1", "evidence_pack_builder.output_1", "transform.output_1"])
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
