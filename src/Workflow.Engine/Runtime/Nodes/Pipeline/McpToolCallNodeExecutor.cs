using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Runtime.Artifacts;
using Workflow.Engine.Runtime.Mcp;

namespace Workflow.Engine.Runtime.Nodes.Pipeline;

/// <summary>
/// Что: deterministic нода вызова MCP tool.
/// Зачем: получать Jira/wiki/logs/test results через MCP без LLM и без токенов.
/// Как: берет backend `serverProfile`, вызывает точный `toolName` с JSON arguments и возвращает нормализованный result payload.
/// </summary>
public sealed class McpToolCallNodeExecutor : IWorkflowNodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "mcp_tool_call",
        Label: "MCP Tool Call",
        Description: "Call a deterministic MCP tool",
        Inputs: 2,
        Outputs: 1,
        Pack: WorkflowNodePacks.Core,
        Source: WorkflowNodeSources.BuiltIn,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "serverProfile",
                Label: "Server Profile",
                FieldType: "text",
                Description: "Backend-configured MCP profile. Endpoint/secrets are not stored in graph JSON.",
                Placeholder: "mock",
                DefaultValue: "mock"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "toolName",
                Label: "Tool Name",
                FieldType: "text",
                Description: "Exact MCP tool name.",
                Required: true,
                Placeholder: "get_ticket"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "argumentsJson",
                Label: "Arguments JSON",
                FieldType: "textarea",
                Description: "JSON object. Top-level payload fields can be inserted as {{fieldName}}.",
                Multiline: true,
                Placeholder: "{\"query\":\"{{task_text}}\"}"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "timeoutSeconds",
                Label: "Timeout Seconds",
                FieldType: "text",
                Description: "Per-node timeout override.",
                Placeholder: "30",
                DefaultValue: "30"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "outputArtifactType",
                Label: "Output Artifact Type",
                FieldType: "select",
                Description: "Optionally save MCP result as artifact.",
                DefaultValue: "none",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("none", "Do not save"),
                    new WorkflowNodeConfigFieldOptionDescriptor("json", "JSON"),
                    new WorkflowNodeConfigFieldOptionDescriptor("markdown", "Markdown"),
                    new WorkflowNodeConfigFieldOptionDescriptor("text", "Text")
                ]),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "artifactFileName",
                Label: "Artifact File Name",
                FieldType: "text",
                Description: "Optional artifact file name.",
                Placeholder: "mcp-result.json")
        ],
        InputPorts:
        [
            new WorkflowNodePortDescriptor(
                "input_1",
                "Data",
                WorkflowPortChannels.Data,
                AcceptedKinds: ["task_text", "jira_issue", "workspace_context", "workflow_data"],
                Description: "Payload used to render argumentsJson and provide default MCP arguments.",
                FallbackDescription: "When not connected, the node uses the run input payload and its config.",
                ExampleSources: ["task_text_input.output_1", "template_select.output_1", "workspace_prepare_raw.output_1"]),
            new WorkflowNodePortDescriptor(
                "input_2",
                "Run Gate",
                WorkflowPortChannels.ControlOk,
                Description: "Optional control gate for conditional MCP execution.",
                FallbackDescription: "When not connected, the node is eligible to run.")
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor(
                "output_1",
                "data",
                WorkflowPortChannels.Data,
                Description: "Payload enriched with normalized MCP result and optional artifact refs.",
                ProducesKinds: ["mcp_tool_result", "workflow_data"])
        ]);

    public async Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);
        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);

        var serverProfile = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "serverProfile") ?? "mock";
        var toolName = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "toolName");
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new InvalidOperationException("MCP toolName is required.");
        }

        var arguments = BuildArguments(context.Node.Config, payload);
        var timeout = ResolveTimeout(context.Node.Config);

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"MCP tool call started with profile '{serverProfile}', tool '{toolName.Trim()}'."
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var result = await context.McpToolInvokerCatalog.InvokeAsync(new McpToolCallRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            ServerProfile = serverProfile,
            ToolName = toolName.Trim(),
            Arguments = arguments,
            Timeout = timeout
        }, timeoutCts.Token);

        ApplyMcpResult(payload, result, arguments);
        MaybeSaveResultArtifact(context, payload, result);

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"MCP tool call completed with profile '{result.ServerProfile}', tool '{result.ToolName}'."
        });

        return payload;
    }

    private static JsonObject BuildArguments(JsonElement config, JsonObject payload)
    {
        var argumentsTemplate = WorkflowNodePayloadOperations.TryGetConfigString(config, "argumentsJson");
        if (string.IsNullOrWhiteSpace(argumentsTemplate))
        {
            return WorkflowNodePayloadOperations.CloneObject(payload);
        }

        var rendered = RenderTemplate(argumentsTemplate, payload);
        try
        {
            var parsed = JsonNode.Parse(rendered);
            if (parsed is JsonObject arguments)
            {
                return (JsonObject)arguments.DeepClone();
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"MCP argumentsJson is not valid JSON: {exception.Message}", exception);
        }

        throw new InvalidOperationException("MCP argumentsJson must be a JSON object.");
    }

    private static string RenderTemplate(string template, JsonObject payload)
    {
        var rendered = template;
        foreach (var (key, value) in payload)
        {
            rendered = rendered.Replace($"{{{{{key}}}}}", JsonNodeToString(value), StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string JsonNodeToString(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : node.ToJsonString(JsonOptions);
    }

    private static TimeSpan ResolveTimeout(JsonElement config)
    {
        var configured = WorkflowNodePayloadOperations.TryGetConfigString(config, "timeoutSeconds");
        return int.TryParse(configured, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(Math.Min(seconds, 600))
            : TimeSpan.FromSeconds(30);
    }

    private static void ApplyMcpResult(JsonObject payload, McpToolCallResult result, JsonObject arguments)
    {
        payload["mcp_server_profile"] = result.ServerProfile;
        payload["mcp_server_type"] = result.ServerType;
        payload["mcp_tool_name"] = result.ToolName;
        payload["mcp_arguments"] = arguments.DeepClone();
        payload["mcp_result_json"] = result.ResultJson;
        payload["mcp_result"] = TryParseResult(result.ResultJson);
        payload["mcp_metadata"] = result.Metadata.DeepClone();
    }

    private static JsonNode TryParseResult(string resultJson)
    {
        try
        {
            return JsonNode.Parse(resultJson) ?? JsonValue.Create(resultJson)!;
        }
        catch (JsonException)
        {
            return JsonValue.Create(resultJson)!;
        }
    }

    private static void MaybeSaveResultArtifact(
        WorkflowNodeExecutionContext context,
        JsonObject payload,
        McpToolCallResult result)
    {
        var artifactType = NormalizeArtifactType(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "outputArtifactType"));
        if (artifactType == "none")
        {
            return;
        }

        var fileName = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "artifactFileName");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{context.Node.Name}.{GetFileExtension(artifactType)}";
        }

        var descriptor = context.ArtifactStore.WriteArtifact(new WorkflowArtifactWriteRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Name = EnsureExtension(fileName, artifactType),
            ArtifactType = artifactType,
            MediaType = GetMediaType(artifactType),
            Content = SerializeResult(result, artifactType)
        });

        payload["mcp_result_artifact"] = WorkflowNodePayloadOperations.CreateArtifactReference(descriptor);
    }

    private static string NormalizeArtifactType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "json" ? "json" :
            normalized is "markdown" or "md" ? "markdown" :
            normalized is "text" or "txt" ? "text" :
            "none";
    }

    private static string SerializeResult(McpToolCallResult result, string artifactType)
    {
        return artifactType switch
        {
            "markdown" => $"# MCP Tool Result{Environment.NewLine}{Environment.NewLine}- Profile: `{result.ServerProfile}`{Environment.NewLine}- Tool: `{result.ToolName}`{Environment.NewLine}{Environment.NewLine}```json{Environment.NewLine}{result.ResultJson}{Environment.NewLine}```{Environment.NewLine}",
            "text" => result.ResultJson,
            _ => result.ResultJson
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
