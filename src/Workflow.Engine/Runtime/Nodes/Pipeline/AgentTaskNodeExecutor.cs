using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Runtime.Agents;
using Workflow.Engine.Runtime.Artifacts;
using Workflow.Engine.Runtime.Routing;

namespace Workflow.Engine.Runtime.Nodes.Pipeline;

/// <summary>
/// Что: базовая нода выполнения agent task через provider-neutral adapter.
/// Зачем: запускать Cursor/Claude/другой coding-agent как шаг workflow, не вшивая provider в runtime.
/// Как: резолвит `agentProfile` через IAgentExecutorCatalog и вызывает Ask или CreateTask/GetStatus/GetResult.
/// </summary>
public sealed class AgentTaskNodeExecutor : IWorkflowNodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "agent_task",
        Label: "Agent Task",
        Description: "Run an agent adapter task",
        Inputs: 3,
        Outputs: 1,
        Pack: WorkflowNodePacks.Core,
        Source: WorkflowNodeSources.BuiltIn,
        UsesModel: true,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "stage",
                Label: "Routing Stage",
                FieldType: "text",
                Description: "Stage key for model routing policy.",
                Placeholder: "cheap_discover"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "agentProfile",
                Label: "Agent Profile",
                FieldType: "text",
                Description: "Optional backend agent profile override. Empty means use routing policy.",
                Placeholder: "echo",
                DefaultValue: ""),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "confidence",
                Label: "Confidence",
                FieldType: "text",
                Description: "Optional 0..1 confidence signal for escalation.",
                Placeholder: "0.8"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "retryCount",
                Label: "Retry Count",
                FieldType: "text",
                Description: "Optional retry count signal for escalation.",
                Placeholder: "0"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "budgetRemaining",
                Label: "Budget Remaining",
                FieldType: "text",
                Description: "Optional 0..1 budget remaining signal for fallback/budget guard.",
                Placeholder: "1.0"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "mode",
                Label: "Mode",
                FieldType: "select",
                Description: "Ask is one call. Task uses CreateTask/GetStatus/GetResult once.",
                DefaultValue: "ask",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("ask", "Ask"),
                    new WorkflowNodeConfigFieldOptionDescriptor("task", "Task")
                ]),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "prompt",
                Label: "Prompt",
                FieldType: "textarea",
                Description: "Prompt template. Top-level payload fields can be inserted as {{fieldName}}.",
                Required: true,
                Multiline: true,
                Placeholder: "Analyze this task: {{task_text}}"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "saveResultAsArtifact",
                Label: "Save Result Artifact",
                FieldType: "select",
                Description: "Optionally persist agent result in workspace artifacts.",
                DefaultValue: "false",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("false", "No"),
                    new WorkflowNodeConfigFieldOptionDescriptor("true", "Yes")
                ]),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "outputArtifactType",
                Label: "Output Artifact Type",
                FieldType: "select",
                Description: "Artifact serialization when saving result.",
                DefaultValue: "markdown",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("markdown", "Markdown"),
                    new WorkflowNodeConfigFieldOptionDescriptor("text", "Text"),
                    new WorkflowNodeConfigFieldOptionDescriptor("json", "JSON")
                ]),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "artifactFileName",
                Label: "Artifact File Name",
                FieldType: "text",
                Description: "Optional artifact file name.",
                Placeholder: "agent_result.md")
        ],
        InputPorts:
        [
            new WorkflowNodePortDescriptor(
                "input_1",
                "Data",
                WorkflowPortChannels.Data,
                AcceptedKinds: ["evidence_pack", "workspace_context", "task_text", "agent_result", "workflow_data"],
                Description: "Context payload used to render the agent prompt template.",
                FallbackDescription: "When not connected, the agent receives run input plus node config/template only.",
                ExampleSources: ["evidence_pack_builder.output_1", "workspace_prepare_raw.output_1", "agent_task.output_1"]),
            new WorkflowNodePortDescriptor(
                "input_2",
                "Run Gate",
                WorkflowPortChannels.ControlOk,
                Description: "Optional control gate for conditional agent execution.",
                FallbackDescription: "When not connected, the node is eligible to run."),
            new WorkflowNodePortDescriptor(
                "input_3",
                "Approval Gate",
                WorkflowPortChannels.ControlApprovalRequired,
                Description: "Optional human approval branch gate.",
                FallbackDescription: "When not connected, no approval gate is required by the graph.")
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor(
                "output_1",
                "data",
                WorkflowPortChannels.Data,
                Description: "Payload enriched with agent response, routing diagnostics and optional artifact refs.",
                ProducesKinds: ["agent_result", "workflow_data"])
        ]);

    public async Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);
        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);

        var mode = NormalizeMode(WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "mode"));
        var prompt = BuildPrompt(context.Node.Config, payload);
        var profileName = ResolveAgentProfile(context);
        var resolvedAgent = context.AgentExecutorCatalog.Resolve(profileName, context.ModelRoute);

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"Agent task started with profile '{resolvedAgent.ProfileName}', adapter '{resolvedAgent.AdapterName}', mode '{mode}', selected model '{context.ModelRoute.SelectedModel}', tier '{context.ModelRoute.SelectedTier}', route reason '{context.ModelRoute.RouteReason}'."
        });

        var result = mode == "task"
            ? await ExecuteTaskModeAsync(context, payload, prompt, resolvedAgent, cancellationToken)
            : await ExecuteAskModeAsync(context, payload, prompt, resolvedAgent, cancellationToken);

        ApplyAgentResult(payload, resolvedAgent, mode, context.ModelRoute, result);
        MaybeSaveResultArtifact(context, payload, result);

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"Agent task completed with profile '{resolvedAgent.ProfileName}', adapter '{resolvedAgent.AdapterName}', mode '{mode}', selected model '{context.ModelRoute.SelectedModel}'."
        });

        return payload;
    }

    private static async Task<AgentTaskResult> ExecuteAskModeAsync(
        WorkflowNodeExecutionContext context,
        JsonObject payload,
        string prompt,
        AgentExecutorResolution resolvedAgent,
        CancellationToken cancellationToken)
    {
        var askResult = await resolvedAgent.Executor.AskAsync(new AgentAskRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Profile = resolvedAgent.ProfileName,
            SelectedTier = resolvedAgent.RoutingDecision.SelectedTier,
            SelectedModel = resolvedAgent.RoutingDecision.SelectedModel,
            ThinkingMode = resolvedAgent.RoutingDecision.ThinkingMode,
            RouteReason = resolvedAgent.RoutingDecision.RouteReason,
            Prompt = prompt,
            Input = WorkflowNodePayloadOperations.CloneObject(payload)
        }, cancellationToken);

        return new AgentTaskResult
        {
            TaskId = string.Empty,
            Text = askResult.Text,
            Status = askResult.Status,
            Metadata = askResult.Metadata
        };
    }

    private static async Task<AgentTaskResult> ExecuteTaskModeAsync(
        WorkflowNodeExecutionContext context,
        JsonObject payload,
        string prompt,
        AgentExecutorResolution resolvedAgent,
        CancellationToken cancellationToken)
    {
        var task = await resolvedAgent.Executor.CreateTaskAsync(new AgentTaskCreateRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Profile = resolvedAgent.ProfileName,
            SelectedTier = resolvedAgent.RoutingDecision.SelectedTier,
            SelectedModel = resolvedAgent.RoutingDecision.SelectedModel,
            ThinkingMode = resolvedAgent.RoutingDecision.ThinkingMode,
            RouteReason = resolvedAgent.RoutingDecision.RouteReason,
            Prompt = prompt,
            Input = WorkflowNodePayloadOperations.CloneObject(payload)
        }, cancellationToken);

        var status = await resolvedAgent.Executor.GetStatusAsync(new AgentTaskStatusRequest
        {
            TaskId = task.TaskId,
            Profile = resolvedAgent.ProfileName
        }, cancellationToken);

        if (!string.Equals(status.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent task '{task.TaskId}' has status '{status.Status}'. Checkpoint/resume polling is not implemented yet.");
        }

        return await resolvedAgent.Executor.GetResultAsync(new AgentTaskResultRequest
        {
            TaskId = task.TaskId,
            Profile = resolvedAgent.ProfileName
        }, cancellationToken);
    }

    private static void ApplyAgentResult(
        JsonObject payload,
        AgentExecutorResolution resolvedAgent,
        string mode,
        WorkflowModelRoutingDecision modelRoute,
        AgentTaskResult result)
    {
        payload["agent_profile"] = resolvedAgent.ProfileName;
        payload["agent_adapter"] = resolvedAgent.AdapterName;
        payload["agent_mode"] = mode;
        payload["agent_response_text"] = result.Text;
        payload["model_routing"] = CreateModelRoutingPayload(modelRoute);
        payload["agent_result"] = new JsonObject
        {
            ["taskId"] = result.TaskId,
            ["status"] = result.Status,
            ["text"] = result.Text,
            ["metadata"] = result.Metadata.DeepClone()
        };
    }

    private static string? ResolveAgentProfile(WorkflowNodeExecutionContext context)
    {
        var configuredProfile = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "agentProfile");
        return !string.IsNullOrWhiteSpace(configuredProfile)
            ? configuredProfile
            : context.ModelRoute.SelectedAgentProfile;
    }

    private static JsonObject CreateModelRoutingPayload(WorkflowModelRoutingDecision decision)
    {
        return new JsonObject
        {
            ["stage"] = decision.Stage,
            ["policyKey"] = decision.PolicyKey,
            ["selectedTier"] = decision.SelectedTier,
            ["selectedModel"] = decision.SelectedModel,
            ["selectedAgentProfile"] = decision.SelectedAgentProfile,
            ["thinkingMode"] = decision.ThinkingMode,
            ["routeReason"] = decision.RouteReason,
            ["usesModel"] = decision.UsesModel,
            ["triggerSnapshot"] = new JsonObject
            {
                ["confidence"] = decision.TriggerSnapshot.Confidence,
                ["retryCount"] = decision.TriggerSnapshot.RetryCount,
                ["budgetRemaining"] = decision.TriggerSnapshot.BudgetRemaining
            }
        };
    }

    private static void MaybeSaveResultArtifact(
        WorkflowNodeExecutionContext context,
        JsonObject payload,
        AgentTaskResult result)
    {
        if (!IsTrue(WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "saveResultAsArtifact")))
        {
            return;
        }

        var artifactType = NormalizeArtifactType(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "outputArtifactType"));
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

        payload["agent_result_artifact"] = WorkflowNodePayloadOperations.CreateArtifactReference(descriptor);
    }

    private static string BuildPrompt(JsonElement config, JsonObject payload)
    {
        var template = WorkflowNodePayloadOperations.TryGetConfigString(config, "prompt");
        if (string.IsNullOrWhiteSpace(template))
        {
            return ReadString(payload, "task_text") ??
                   ReadString(payload, "task") ??
                   payload.ToJsonString(JsonOptions);
        }

        var rendered = template;
        foreach (var (key, value) in payload)
        {
            rendered = rendered.Replace($"{{{{{key}}}}}", JsonNodeToString(value), StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string? ReadString(JsonObject payload, string key)
    {
        return payload.TryGetPropertyValue(key, out var value) && value is JsonValue jsonValue &&
               jsonValue.TryGetValue<string>(out var text)
            ? text
            : null;
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

    private static string NormalizeMode(string? value)
    {
        return string.Equals(value, "task", StringComparison.OrdinalIgnoreCase) ? "task" : "ask";
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArtifactType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "markdown";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "json" ? "json" :
            normalized is "text" or "txt" ? "text" :
            "markdown";
    }

    private static string SerializeResult(AgentTaskResult result, string artifactType)
    {
        return artifactType switch
        {
            "json" => new JsonObject
            {
                ["taskId"] = result.TaskId,
                ["status"] = result.Status,
                ["text"] = result.Text,
                ["metadata"] = result.Metadata.DeepClone()
            }.ToJsonString(JsonOptions),
            "text" => result.Text,
            _ => $"# Agent Result{Environment.NewLine}{Environment.NewLine}{result.Text}{Environment.NewLine}"
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
            "json" => "json",
            "text" => "txt",
            _ => "md"
        };
    }

    private static string GetMediaType(string artifactType)
    {
        return artifactType switch
        {
            "json" => "application/json",
            "text" => "text/plain",
            _ => "text/markdown"
        };
    }
}
