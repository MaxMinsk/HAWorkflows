using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Runtime.Artifacts;

namespace Workflow.Engine.Runtime.Nodes.Pipeline;

/// <summary>
/// Что: deterministic нода сборки evidence pack.
/// Зачем: превратить сырой task/Jira-via-MCP/workspace контекст в один структурный пакет для следующих context/agent шагов.
/// Как: читает payload и workspace artifact refs, нормализует найденные доказательства и пишет `evidence_pack.json/md` в run workspace.
/// </summary>
public sealed class EvidencePackBuilderNodeExecutor : IWorkflowNodeExecutor
{
    private const string SchemaVersion = "1.0";
    private const int DefaultMaxInlineCharacters = 12000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "evidence_pack_builder",
        Label: "Evidence Pack Builder",
        Description: "Build deterministic evidence_pack.json/md from raw workspace and MCP results",
        Inputs: 1,
        Outputs: 1,
        Pack: WorkflowNodePacks.Core,
        Source: WorkflowNodeSources.BuiltIn,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "maxInlineCharacters",
                Label: "Max Inline Characters",
                FieldType: "text",
                Description: "Maximum artifact/result content stored inline per evidence item.",
                DefaultValue: DefaultMaxInlineCharacters.ToString(),
                Placeholder: DefaultMaxInlineCharacters.ToString()),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "includeArtifactContent",
                Label: "Include Artifact Content",
                FieldType: "select",
                Description: "Inline readable raw artifact content into evidence JSON when small enough.",
                DefaultValue: "true",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("true", "Yes"),
                    new WorkflowNodeConfigFieldOptionDescriptor("false", "No")
                ])
        ],
        InputPorts:
        [
            new WorkflowNodePortDescriptor("input_1", "Data", WorkflowPortChannels.Data)
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

        var maxInlineCharacters = ResolveMaxInlineCharacters(context.Node.Config);
        var includeArtifactContent = !IsFalse(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "includeArtifactContent"));
        var workItemId = ReadNestedString(payload, "workspace", "workItemId")
                         ?? ReadString(payload, "jira_issue_key")
                         ?? ReadString(payload, "work_item_id")
                         ?? "unknown";
        var runDirectory = ReadNestedString(payload, "workspace", "runDirectory");
        var generatedFrom = ReadNestedString(payload, "workspace", "createdAtUtc") ?? "input-payload";

        var items = new JsonArray();
        var gaps = new JsonArray();
        var workspaceKinds = AddWorkspaceRawEvidence(
            context,
            payload,
            items,
            gaps,
            maxInlineCharacters,
            includeArtifactContent);

        if (!workspaceKinds.Contains("task"))
        {
            AddTaskEvidence(payload, items, maxInlineCharacters);
        }

        if (!workspaceKinds.Contains("ticket"))
        {
            AddTicketEvidence(payload, items, maxInlineCharacters);
        }

        AddMcpEvidence(payload, items, maxInlineCharacters);

        if (items.Count == 0)
        {
            gaps.Add("No task, ticket, workspace raw artifacts or MCP result were found in payload.");
        }

        var evidencePack = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["kind"] = "evidence_pack",
            ["runId"] = context.RunId,
            ["workItemId"] = workItemId,
            ["pipelineTemplate"] = ReadString(payload, "pipeline_template"),
            ["generatedFrom"] = generatedFrom,
            ["sourceWorkspace"] = CreateSourceWorkspace(payload),
            ["items"] = items,
            ["gaps"] = gaps
        };

        var markdown = RenderMarkdown(evidencePack);
        var jsonDescriptor = WriteArtifact(
            context,
            runDirectory,
            "evidence_pack.json",
            "json",
            "application/json",
            evidencePack.ToJsonString(JsonOptions));
        var markdownDescriptor = WriteArtifact(
            context,
            runDirectory,
            "evidence_pack.md",
            "markdown",
            "text/markdown",
            markdown);

        payload["evidence_pack"] = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["kind"] = "evidence_pack",
            ["runId"] = context.RunId,
            ["workItemId"] = workItemId,
            ["itemCount"] = items.Count,
            ["gapCount"] = gaps.Count,
            ["artifacts"] = new JsonObject
            {
                ["json"] = WorkflowNodePayloadOperations.CreateArtifactReference(jsonDescriptor),
                ["markdown"] = WorkflowNodePayloadOperations.CreateArtifactReference(markdownDescriptor)
            }
        };

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"Evidence pack built with {items.Count} items and {gaps.Count} gaps."
        });

        return Task.FromResult(payload);
    }

    private static HashSet<string> AddWorkspaceRawEvidence(
        WorkflowNodeExecutionContext context,
        JsonObject payload,
        JsonArray items,
        JsonArray gaps,
        int maxInlineCharacters,
        bool includeArtifactContent)
    {
        var workspaceKinds = new HashSet<string>(StringComparer.Ordinal);
        if (!TryReadObjectPath(payload, out var rawArtifacts, "workspace", "rawArtifacts"))
        {
            gaps.Add("workspace.rawArtifacts is absent; using payload-level evidence only.");
            return workspaceKinds;
        }

        foreach (var (kind, value) in rawArtifacts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (value is null)
            {
                continue;
            }

            workspaceKinds.Add(kind);
            var artifactRefs = CloneArtifactRefs(value);
            var data = CreateWorkspaceArtifactData(
                context,
                value,
                maxInlineCharacters,
                includeArtifactContent,
                gaps,
                $"workspace.rawArtifacts.{kind}");

            items.Add(new JsonObject
            {
                ["id"] = $"workspace:{kind}",
                ["type"] = $"workspace_{kind}",
                ["source"] = $"workspace.rawArtifacts.{kind}",
                ["title"] = ToTitle(kind),
                ["summary"] = CreateSummary(data, $"Workspace artifact group '{kind}'.", maxInlineCharacters),
                ["artifactRefs"] = artifactRefs,
                ["data"] = data
            });
        }

        return workspaceKinds;
    }

    private static JsonObject CreateWorkspaceArtifactData(
        WorkflowNodeExecutionContext context,
        JsonNode artifactGroup,
        int maxInlineCharacters,
        bool includeArtifactContent,
        JsonArray gaps,
        string source)
    {
        var data = new JsonObject();

        if (artifactGroup is JsonObject groupObject)
        {
            foreach (var (key, value) in groupObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (value is JsonObject artifactRef && IsArtifactRef(artifactRef))
                {
                    data[key] = CreateArtifactContentNode(
                        context,
                        artifactRef,
                        maxInlineCharacters,
                        includeArtifactContent,
                        gaps,
                        $"{source}.{key}");
                }
                else
                {
                    data[key] = value?.DeepClone();
                }
            }

            return data;
        }

        if (artifactGroup is JsonObject directRef && IsArtifactRef(directRef))
        {
            data["artifact"] = CreateArtifactContentNode(
                context,
                directRef,
                maxInlineCharacters,
                includeArtifactContent,
                gaps,
                source);
            return data;
        }

        data["value"] = artifactGroup.DeepClone();
        return data;
    }

    private static JsonObject CreateArtifactContentNode(
        WorkflowNodeExecutionContext context,
        JsonObject artifactRef,
        int maxInlineCharacters,
        bool includeArtifactContent,
        JsonArray gaps,
        string source)
    {
        var artifactId = ReadString(artifactRef, "artifactId");
        var runId = ReadString(artifactRef, "runId") ?? context.RunId;
        var result = new JsonObject
        {
            ["ref"] = artifactRef.DeepClone()
        };

        if (string.IsNullOrWhiteSpace(artifactId))
        {
            gaps.Add($"{source} does not contain artifactId.");
            return result;
        }

        var artifactContent = context.ArtifactStore.TryReadArtifact(runId, artifactId);
        if (artifactContent is null)
        {
            gaps.Add($"{source} artifact '{artifactId}' could not be read.");
            return result;
        }

        result["descriptor"] = WorkflowNodePayloadOperations.CreateArtifactReference(artifactContent.Descriptor);
        if (!includeArtifactContent)
        {
            result["contentIncluded"] = false;
            return result;
        }

        result["contentIncluded"] = true;
        result["content"] = NormalizeContent(artifactContent.Content, artifactContent.Descriptor.MediaType, maxInlineCharacters);
        return result;
    }

    private static void AddTaskEvidence(JsonObject payload, JsonArray items, int maxInlineCharacters)
    {
        var taskText = ReadString(payload, "task_text") ?? ReadNestedString(payload, "task", "text");
        if (string.IsNullOrWhiteSpace(taskText))
        {
            return;
        }

        var data = new JsonObject
        {
            ["source"] = ReadString(payload, "task_source") ?? "task_text_input",
            ["text"] = Truncate(taskText, maxInlineCharacters),
            ["task"] = CloneOrNull(payload, "task")
        };

        items.Add(new JsonObject
        {
            ["id"] = "payload:task",
            ["type"] = "task",
            ["source"] = "payload.task_text",
            ["title"] = "Task",
            ["summary"] = CreateSummary(data, taskText, maxInlineCharacters),
            ["data"] = data
        });
    }

    private static void AddTicketEvidence(JsonObject payload, JsonArray items, int maxInlineCharacters)
    {
        var issueKey = ReadString(payload, "jira_issue_key")
                       ?? ReadString(payload, "issueKey")
                       ?? ReadString(payload, "issue_key");
        if (string.IsNullOrWhiteSpace(issueKey))
        {
            return;
        }

        var data = new JsonObject
        {
            ["issueKey"] = issueKey,
            ["summary"] = ReadString(payload, "jira_summary") ?? ReadString(payload, "summary"),
            ["description"] = Truncate(
                ReadString(payload, "jira_description") ?? ReadString(payload, "description"),
                maxInlineCharacters),
            ["issueType"] = ReadString(payload, "jira_issue_type") ?? ReadString(payload, "issue_type"),
            ["severity"] = ReadString(payload, "jira_severity") ?? ReadString(payload, "severity"),
            ["labels"] = CloneOrNull(payload, "jira_labels", "labels")
        };

        items.Add(new JsonObject
        {
            ["id"] = $"payload:ticket:{issueKey}",
            ["type"] = "ticket",
            ["source"] = "payload.jira_*",
            ["title"] = $"Ticket {issueKey}",
            ["summary"] = ReadString(data, "summary") ?? issueKey,
            ["data"] = data
        });
    }

    private static void AddMcpEvidence(JsonObject payload, JsonArray items, int maxInlineCharacters)
    {
        if (!payload.TryGetPropertyValue("mcp_result", out var mcpResult) &&
            !payload.TryGetPropertyValue("mcp_result_json", out mcpResult))
        {
            return;
        }

        var toolName = ReadString(payload, "mcp_tool_name") ?? "unknown";
        var serverProfile = ReadString(payload, "mcp_server_profile") ?? "unknown";
        var data = new JsonObject
        {
            ["serverProfile"] = serverProfile,
            ["toolName"] = toolName,
            ["arguments"] = CloneOrNull(payload, "mcp_arguments"),
            ["metadata"] = CloneOrNull(payload, "mcp_metadata"),
            ["result"] = NormalizeJsonNode(mcpResult, maxInlineCharacters)
        };

        if (payload.TryGetPropertyValue("mcp_result_artifact", out var artifactRef) && artifactRef is JsonObject artifactRefObject)
        {
            data["artifactRef"] = artifactRefObject.DeepClone();
        }

        items.Add(new JsonObject
        {
            ["id"] = $"mcp:{serverProfile}:{toolName}",
            ["type"] = "mcp_tool_result",
            ["source"] = "payload.mcp_result",
            ["title"] = $"MCP {serverProfile}/{toolName}",
            ["summary"] = CreateSummary(data, $"MCP tool {toolName} completed through {serverProfile}.", maxInlineCharacters),
            ["data"] = data
        });
    }

    private static WorkflowArtifactDescriptor WriteArtifact(
        WorkflowNodeExecutionContext context,
        string? runDirectory,
        string fileName,
        string artifactType,
        string mediaType,
        string content)
    {
        return context.ArtifactStore.WriteArtifact(new WorkflowArtifactWriteRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Name = fileName,
            ArtifactType = artifactType,
            MediaType = mediaType,
            Content = content,
            WorkspaceRelativeDirectory = runDirectory,
            UseStableFileName = true
        });
    }

    private static JsonObject CreateSourceWorkspace(JsonObject payload)
    {
        if (!TryReadObjectPath(payload, out var workspace, "workspace"))
        {
            return new JsonObject();
        }

        return new JsonObject
        {
            ["workItemId"] = ReadString(workspace, "workItemId"),
            ["taskDirectory"] = ReadString(workspace, "taskDirectory"),
            ["runDirectory"] = ReadString(workspace, "runDirectory"),
            ["latestRun"] = CloneOrNull(workspace, "latestRun")
        };
    }

    private static JsonObject CloneArtifactRefs(JsonNode value)
    {
        if (value is JsonObject objectValue)
        {
            return (JsonObject)objectValue.DeepClone();
        }

        return new JsonObject
        {
            ["value"] = value.DeepClone()
        };
    }

    private static JsonNode NormalizeContent(string content, string mediaType, int maxInlineCharacters)
    {
        if (content.Length > maxInlineCharacters)
        {
            return new JsonObject
            {
                ["contentKind"] = "text_preview",
                ["truncated"] = true,
                ["length"] = content.Length,
                ["preview"] = Truncate(content, maxInlineCharacters)
            };
        }

        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            content.TrimStart().StartsWith('{') ||
            content.TrimStart().StartsWith('['))
        {
            try
            {
                return JsonNode.Parse(content) ?? JsonValue.Create(content)!;
            }
            catch (JsonException)
            {
                return JsonValue.Create(content)!;
            }
        }

        return JsonValue.Create(content)!;
    }

    private static JsonNode? NormalizeJsonNode(JsonNode? value, int maxInlineCharacters)
    {
        if (value is null)
        {
            return null;
        }

        var serialized = value.ToJsonString(JsonOptions);
        if (serialized.Length <= maxInlineCharacters)
        {
            return value.DeepClone();
        }

        return new JsonObject
        {
            ["contentKind"] = "json_preview",
            ["truncated"] = true,
            ["length"] = serialized.Length,
            ["preview"] = Truncate(serialized, maxInlineCharacters)
        };
    }

    private static string RenderMarkdown(JsonObject evidencePack)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Evidence Pack");
        builder.AppendLine();
        builder.AppendLine($"- Work item: `{ReadString(evidencePack, "workItemId") ?? "unknown"}`");
        builder.AppendLine($"- Run: `{ReadString(evidencePack, "runId") ?? "unknown"}`");
        builder.AppendLine($"- Pipeline template: `{ReadString(evidencePack, "pipelineTemplate") ?? "unknown"}`");
        builder.AppendLine();

        var items = evidencePack["items"] as JsonArray ?? [];
        builder.AppendLine("## Items");
        builder.AppendLine();
        if (items.Count == 0)
        {
            builder.AppendLine("No evidence items were found.");
            builder.AppendLine();
        }

        foreach (var item in items.OfType<JsonObject>())
        {
            builder.AppendLine($"### {ReadString(item, "title") ?? ReadString(item, "id") ?? "Evidence"}");
            builder.AppendLine();
            builder.AppendLine($"- Type: `{ReadString(item, "type") ?? "unknown"}`");
            builder.AppendLine($"- Source: `{ReadString(item, "source") ?? "unknown"}`");
            builder.AppendLine($"- Summary: {ReadString(item, "summary") ?? "No summary."}");
            if (item.TryGetPropertyValue("artifactRefs", out var artifactRefs) && artifactRefs is not null)
            {
                builder.AppendLine("- Artifact refs:");
                builder.AppendLine();
                builder.AppendLine("```json");
                builder.AppendLine(artifactRefs.ToJsonString(JsonOptions));
                builder.AppendLine("```");
            }

            builder.AppendLine();
        }

        var gaps = evidencePack["gaps"] as JsonArray ?? [];
        if (gaps.Count > 0)
        {
            builder.AppendLine("## Gaps");
            builder.AppendLine();
            foreach (var gap in gaps)
            {
                builder.AppendLine($"- {gap}");
            }
        }

        return builder.ToString();
    }

    private static string CreateSummary(JsonNode? data, string fallback, int maxInlineCharacters)
    {
        if (data is JsonObject objectData)
        {
            foreach (var key in new[] { "summary", "text", "issueKey", "toolName" })
            {
                var value = ReadString(objectData, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return Truncate(value, Math.Min(maxInlineCharacters, 320)) ?? fallback;
                }
            }
        }

        return Truncate(fallback, Math.Min(maxInlineCharacters, 320)) ?? string.Empty;
    }

    private static int ResolveMaxInlineCharacters(JsonElement config)
    {
        var configured = WorkflowNodePayloadOperations.TryGetConfigString(config, "maxInlineCharacters");
        return int.TryParse(configured, out var value) && value > 0
            ? Math.Clamp(value, 500, 100000)
            : DefaultMaxInlineCharacters;
    }

    private static bool TryReadObjectPath(JsonObject payload, out JsonObject jsonObject, params string[] path)
    {
        JsonNode? current = payload;
        foreach (var segment in path)
        {
            if (current is not JsonObject currentObject ||
                !currentObject.TryGetPropertyValue(segment, out current))
            {
                jsonObject = null!;
                return false;
            }
        }

        if (current is JsonObject result)
        {
            jsonObject = result;
            return true;
        }

        jsonObject = null!;
        return false;
    }

    private static JsonNode? CloneOrNull(JsonObject payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetPropertyValue(key, out var value) && value is not null)
            {
                return value.DeepClone();
            }
        }

        return null;
    }

    private static string? ReadString(JsonObject payload, string key)
    {
        if (!payload.TryGetPropertyValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue) => stringValue,
            _ => value.ToString()
        };
    }

    private static string? ReadNestedString(JsonObject payload, string objectKey, string propertyKey)
    {
        return payload.TryGetPropertyValue(objectKey, out var value) && value is JsonObject jsonObject
            ? ReadString(jsonObject, propertyKey)
            : null;
    }

    private static bool IsArtifactRef(JsonObject value)
    {
        return !string.IsNullOrWhiteSpace(ReadString(value, "artifactId"));
    }

    private static bool IsFalse(string? value)
    {
        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Truncate(string? value, int maxCharacters)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxCharacters)
        {
            return value;
        }

        return $"{value[..maxCharacters]}... [truncated {value.Length - maxCharacters} chars]";
    }

    private static string ToTitle(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].Replace('_', ' ');
    }
}
