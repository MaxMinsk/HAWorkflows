using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Runtime.Artifacts;

namespace Workflow.Engine.Runtime.Nodes.Builtin;

/// <summary>
/// Что: встроенная нода записи workspace artifact.
/// Зачем: сохранять промежуточный результат run как файл и передавать дальше компактный `artifact_ref`.
/// Как: берет merged data input, пишет JSON/Markdown/Text artifact через ArtifactStore и возвращает descriptor-reference.
/// </summary>
public sealed class ArtifactWriteNodeExecutor : IWorkflowNodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "artifact_write",
        Label: "Artifact Write",
        Description: "Save payload as workspace artifact",
        Inputs: 1,
        Outputs: 1,
        IsLocal: false,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "fileName",
                Label: "File Name",
                FieldType: "text",
                Description: "Workspace artifact file name.",
                Placeholder: "evidence_pack.json"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "artifactType",
                Label: "Artifact Type",
                FieldType: "select",
                Description: "How to serialize the inbound payload.",
                DefaultValue: "json",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("json", "JSON"),
                    new WorkflowNodeConfigFieldOptionDescriptor("markdown", "Markdown"),
                    new WorkflowNodeConfigFieldOptionDescriptor("text", "Text")
                ])
        ],
        InputPorts:
        [
            new WorkflowNodePortDescriptor("input_1", "data", WorkflowPortChannels.Data, Required: true)
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor("output_1", "artifact", WorkflowPortChannels.ArtifactRef)
        ]);

    public Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);

        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);

        var artifactType = NormalizeArtifactType(
            WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "artifactType"));
        var fileName = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "fileName");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{context.Node.Name}.{GetFileExtension(artifactType)}";
        }

        var content = SerializePayload(payload, artifactType, context.Node.Name);
        var descriptor = context.ArtifactStore.WriteArtifact(new WorkflowArtifactWriteRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Name = EnsureExtension(fileName, artifactType),
            ArtifactType = artifactType,
            MediaType = GetMediaType(artifactType),
            Content = content
        });

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"Artifact '{descriptor.Name}' saved ({descriptor.SizeBytes} bytes)."
        });

        return Task.FromResult(WorkflowNodePayloadOperations.CreateArtifactReference(descriptor));
    }

    private static string NormalizeArtifactType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "json";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "markdown" or "md" ? "markdown" :
            normalized is "text" or "txt" ? "text" :
            "json";
    }

    private static string SerializePayload(JsonObject payload, string artifactType, string nodeName)
    {
        var json = payload.ToJsonString(JsonOptions);
        return artifactType switch
        {
            "markdown" => $"# {nodeName}{Environment.NewLine}{Environment.NewLine}```json{Environment.NewLine}{json}{Environment.NewLine}```{Environment.NewLine}",
            "text" => json,
            _ => json
        };
    }

    private static string EnsureExtension(string fileName, string artifactType)
    {
        var extension = $".{GetFileExtension(artifactType)}";
        return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}{extension}";
    }

    private static string GetFileExtension(string artifactType)
    {
        return artifactType switch
        {
            "markdown" => "md",
            "text" => "txt",
            _ => "json"
        };
    }

    private static string GetMediaType(string artifactType)
    {
        return artifactType switch
        {
            "markdown" => "text/markdown",
            "text" => "text/plain",
            _ => "application/json"
        };
    }
}
