using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Runtime.Artifacts;

namespace Workflow.Engine.Runtime.Nodes.Pipeline;

/// <summary>
/// Что: deterministic нода сборки context pack.
/// Зачем: агрегировать evidence_pack + optional memory slice в единый context_pack,
///        пригодный для prompt-сборки agent-нодами (whitelist slice вместо полного payload).
/// Как: читает evidence_pack из payload, вытягивает ключевые секции,
///      добавляет memory slice (если подключен), применяет trimming и пишет context_pack.json/md.
/// </summary>
public sealed class ContextPackBuilderNodeExecutor : IWorkflowNodeExecutor
{
    private const string SchemaVersion = "1.0";
    private const int DefaultMaxContextCharacters = 24000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "context_pack_builder",
        Label: "Context Pack Builder",
        Description: "Build context_pack.json/md from evidence_pack and optional memory slice for agent prompt assembly",
        Inputs: 1,
        Outputs: 1,
        Pack: WorkflowNodePacks.Core,
        Source: WorkflowNodeSources.BuiltIn,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "maxContextCharacters",
                Label: "Max Context Characters",
                FieldType: "text",
                Description: "Token budget proxy: maximum total characters in the assembled context pack.",
                DefaultValue: DefaultMaxContextCharacters.ToString(),
                Placeholder: DefaultMaxContextCharacters.ToString()),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "includeSections",
                Label: "Include Sections",
                FieldType: "text",
                Description: "Comma-separated whitelist of section keys to include (empty = all). E.g. task,ticket,mcp_tool_result",
                Placeholder: "task,ticket,mcp_tool_result,workspace_task,workspace_ticket"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "excludeSections",
                Label: "Exclude Sections",
                FieldType: "text",
                Description: "Comma-separated blacklist of section keys to exclude.",
                Placeholder: ""),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "includeEvidenceData",
                Label: "Include Evidence Data",
                FieldType: "select",
                Description: "Inline evidence item data into context sections.",
                DefaultValue: "true",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("true", "Yes"),
                    new WorkflowNodeConfigFieldOptionDescriptor("false", "No")
                ])
        ],
        InputPorts:
        [
            new WorkflowNodePortDescriptor(
                "input_1",
                "Data",
                WorkflowPortChannels.Data,
                Required: true,
                AcceptedKinds: ["evidence_pack", "workspace_context", "task_text", "workflow_data"],
                Description: "Evidence pack plus workspace/pipeline context used to build context_pack.json/md.",
                ExampleSources: ["evidence_pack_builder.output_1"])
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor(
                "output_1",
                "Data",
                WorkflowPortChannels.Data,
                Description: "Payload enriched with context_pack.json/md artifact refs for downstream agent nodes.",
                ProducesKinds: ["context_pack", "artifact_refs", "workflow_data"])
        ]);

    public Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);

        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);

        var maxContextCharacters = ResolveMaxContextCharacters(context.Node.Config);
        var includeEvidenceData = !IsFalse(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "includeEvidenceData"));
        var includeSections = ParseCsvConfig(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "includeSections"));
        var excludeSections = ParseCsvConfig(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "excludeSections"));

        var workItemId = ReadNestedString(payload, "workspace", "workItemId")
                         ?? ReadString(payload, "work_item_id")
                         ?? "unknown";
        var runDirectory = ReadNestedString(payload, "workspace", "runDirectory");
        var pipelineTemplate = ReadString(payload, "pipeline_template");

        var sections = new JsonArray();
        var gaps = new JsonArray();
        var charBudget = new CharBudget(maxContextCharacters);

        BuildSectionsFromEvidencePack(context, payload, sections, gaps, charBudget, includeEvidenceData, includeSections, excludeSections);
        BuildTaskSection(payload, sections, gaps, charBudget, includeSections, excludeSections);
        BuildMemorySection(context, payload, sections, gaps, charBudget, includeSections, excludeSections);
        BuildPipelineMetadataSection(payload, sections, charBudget, includeSections, excludeSections);

        if (sections.Count == 0)
        {
            gaps.Add("No evidence, task, or memory data found in payload.");
        }

        var contextPack = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["kind"] = "context_pack",
            ["runId"] = context.RunId,
            ["workItemId"] = workItemId,
            ["pipelineTemplate"] = pipelineTemplate,
            ["sourceWorkspace"] = CreateSourceWorkspace(payload),
            ["totalCharacters"] = charBudget.Used,
            ["maxCharacters"] = maxContextCharacters,
            ["sections"] = sections,
            ["gaps"] = gaps
        };

        var markdown = RenderMarkdown(contextPack);
        var jsonDescriptor = WriteArtifact(
            context,
            runDirectory,
            "context_pack.json",
            "json",
            "application/json",
            contextPack.ToJsonString(JsonOptions));
        var markdownDescriptor = WriteArtifact(
            context,
            runDirectory,
            "context_pack.md",
            "markdown",
            "text/markdown",
            markdown);

        payload["context_pack"] = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["kind"] = "context_pack",
            ["runId"] = context.RunId,
            ["workItemId"] = workItemId,
            ["sectionCount"] = sections.Count,
            ["gapCount"] = gaps.Count,
            ["totalCharacters"] = charBudget.Used,
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
            Message = $"Context pack built with {sections.Count} sections, {gaps.Count} gaps, {charBudget.Used}/{maxContextCharacters} chars used."
        });

        return Task.FromResult(payload);
    }

    private static void BuildSectionsFromEvidencePack(
        WorkflowNodeExecutionContext context,
        JsonObject payload,
        JsonArray sections,
        JsonArray gaps,
        CharBudget charBudget,
        bool includeEvidenceData,
        HashSet<string>? includeSections,
        HashSet<string>? excludeSections)
    {
        if (!TryReadObjectPath(payload, out var evidencePack, "evidence_pack"))
        {
            gaps.Add("evidence_pack not found in payload; context will rely on direct task/memory data only.");
            return;
        }

        var evidenceItems = ReadEvidenceItems(context, evidencePack);
        if (evidenceItems.Count == 0)
        {
            gaps.Add("evidence_pack contains no items.");
            return;
        }

        foreach (var item in evidenceItems)
        {
            var itemType = ReadString(item, "type") ?? "unknown";
            if (!ShouldIncludeSection(itemType, includeSections, excludeSections))
            {
                continue;
            }

            if (charBudget.IsExhausted)
            {
                gaps.Add($"Budget exhausted; skipped evidence item '{ReadString(item, "id") ?? itemType}'.");
                continue;
            }

            var section = new JsonObject
            {
                ["id"] = ReadString(item, "id") ?? $"evidence:{itemType}",
                ["type"] = itemType,
                ["source"] = ReadString(item, "source") ?? "evidence_pack",
                ["title"] = ReadString(item, "title") ?? itemType,
                ["summary"] = charBudget.Consume(ReadString(item, "summary"), 500)
            };

            if (includeEvidenceData && item.TryGetPropertyValue("data", out var data) && data is not null)
            {
                var dataJson = data.ToJsonString(JsonOptions);
                section["data"] = charBudget.CanFit(dataJson.Length)
                    ? data.DeepClone()
                    : CreateTruncatedPlaceholder(dataJson.Length, charBudget.Remaining);
            }

            sections.Add(section);
        }
    }

    private static List<JsonObject> ReadEvidenceItems(
        WorkflowNodeExecutionContext context,
        JsonObject evidencePack)
    {
        if (evidencePack.TryGetPropertyValue("items", out var itemsNode) && itemsNode is JsonArray itemsArray)
        {
            return itemsArray.OfType<JsonObject>().ToList();
        }

        var jsonArtifactRef = TryNavigate(evidencePack, "artifacts", "json");
        if (jsonArtifactRef is JsonObject artifactRef)
        {
            var artifactId = ReadString(artifactRef, "artifactId");
            var runId = ReadString(artifactRef, "runId") ?? context.RunId;
            if (!string.IsNullOrWhiteSpace(artifactId))
            {
                var content = context.ArtifactStore.TryReadArtifact(runId, artifactId);
                if (content is not null)
                {
                    try
                    {
                        var parsed = JsonNode.Parse(content.Content);
                        if (parsed is JsonObject parsedObject &&
                            parsedObject.TryGetPropertyValue("items", out var parsedItems) &&
                            parsedItems is JsonArray parsedArray)
                        {
                            return parsedArray.OfType<JsonObject>().ToList();
                        }
                    }
                    catch (JsonException)
                    {
                        // fall through
                    }
                }
            }
        }

        return [];
    }

    private static void BuildTaskSection(
        JsonObject payload,
        JsonArray sections,
        JsonArray gaps,
        CharBudget charBudget,
        HashSet<string>? includeSections,
        HashSet<string>? excludeSections)
    {
        if (!ShouldIncludeSection("task", includeSections, excludeSections))
        {
            return;
        }

        if (SectionsContainType(sections, "task"))
        {
            return;
        }

        var taskText = ReadString(payload, "task_text") ?? ReadNestedString(payload, "task", "text");
        if (string.IsNullOrWhiteSpace(taskText))
        {
            return;
        }

        if (charBudget.IsExhausted)
        {
            gaps.Add("Budget exhausted; skipped direct task section.");
            return;
        }

        sections.Add(new JsonObject
        {
            ["id"] = "direct:task",
            ["type"] = "task",
            ["source"] = "payload.task_text",
            ["title"] = "Task",
            ["summary"] = charBudget.Consume(taskText, 500)
        });
    }

    private static void BuildMemorySection(
        WorkflowNodeExecutionContext context,
        JsonObject payload,
        JsonArray sections,
        JsonArray gaps,
        CharBudget charBudget,
        HashSet<string>? includeSections,
        HashSet<string>? excludeSections)
    {
        if (!ShouldIncludeSection("memory", includeSections, excludeSections))
        {
            return;
        }

        JsonObject? memorySlice = null;

        foreach (var portValue in context.InboundPortValues)
        {
            if (string.Equals(portValue.Channel, WorkflowPortChannels.MemoryRef, StringComparison.Ordinal))
            {
                memorySlice = portValue.Value;
                break;
            }
        }

        if (memorySlice is null && payload.TryGetPropertyValue("memory_slice", out var memoryNode) && memoryNode is JsonObject memoryObject)
        {
            memorySlice = memoryObject;
        }

        if (memorySlice is null)
        {
            return;
        }

        if (charBudget.IsExhausted)
        {
            gaps.Add("Budget exhausted; skipped memory slice.");
            return;
        }

        var memoryJson = memorySlice.ToJsonString(JsonOptions);
        sections.Add(new JsonObject
        {
            ["id"] = "memory:slice",
            ["type"] = "memory",
            ["source"] = "memory_ref_input",
            ["title"] = "Memory Slice",
            ["summary"] = charBudget.Consume($"Memory context with {memorySlice.Count} entries", 200),
            ["data"] = charBudget.CanFit(memoryJson.Length)
                ? memorySlice.DeepClone()
                : CreateTruncatedPlaceholder(memoryJson.Length, charBudget.Remaining)
        });
    }

    private static void BuildPipelineMetadataSection(
        JsonObject payload,
        JsonArray sections,
        CharBudget charBudget,
        HashSet<string>? includeSections,
        HashSet<string>? excludeSections)
    {
        if (!ShouldIncludeSection("pipeline_metadata", includeSections, excludeSections))
        {
            return;
        }

        var pipelineTemplate = ReadString(payload, "pipeline_template");
        var branchFlags = new JsonObject();
        var hasBranchFlags = false;
        foreach (var flag in new[] { "requires_logs_collect", "requires_wiki_collect", "requires_human_approve" })
        {
            if (payload.TryGetPropertyValue(flag, out var flagValue) && flagValue is not null)
            {
                branchFlags[flag] = flagValue.DeepClone();
                hasBranchFlags = true;
            }
        }

        if (pipelineTemplate is null && !hasBranchFlags)
        {
            return;
        }

        if (charBudget.IsExhausted)
        {
            return;
        }

        var data = new JsonObject();
        if (pipelineTemplate is not null) data["pipelineTemplate"] = pipelineTemplate;
        if (hasBranchFlags) data["branchFlags"] = branchFlags;

        sections.Add(new JsonObject
        {
            ["id"] = "pipeline:metadata",
            ["type"] = "pipeline_metadata",
            ["source"] = "payload",
            ["title"] = "Pipeline Metadata",
            ["summary"] = charBudget.Consume($"Template: {pipelineTemplate ?? "unknown"}", 100),
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

    private static string RenderMarkdown(JsonObject contextPack)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Context Pack");
        builder.AppendLine();
        builder.AppendLine($"- Work item: `{ReadString(contextPack, "workItemId") ?? "unknown"}`");
        builder.AppendLine($"- Run: `{ReadString(contextPack, "runId") ?? "unknown"}`");
        builder.AppendLine($"- Pipeline template: `{ReadString(contextPack, "pipelineTemplate") ?? "unknown"}`");
        builder.AppendLine($"- Characters: {ReadInt(contextPack, "totalCharacters")}/{ReadInt(contextPack, "maxCharacters")}");
        builder.AppendLine();

        var sections = contextPack["sections"] as JsonArray ?? [];
        builder.AppendLine("## Sections");
        builder.AppendLine();
        if (sections.Count == 0)
        {
            builder.AppendLine("No context sections were assembled.");
            builder.AppendLine();
        }

        foreach (var section in sections.OfType<JsonObject>())
        {
            builder.AppendLine($"### {ReadString(section, "title") ?? ReadString(section, "id") ?? "Section"}");
            builder.AppendLine();
            builder.AppendLine($"- Type: `{ReadString(section, "type") ?? "unknown"}`");
            builder.AppendLine($"- Source: `{ReadString(section, "source") ?? "unknown"}`");
            builder.AppendLine($"- Summary: {ReadString(section, "summary") ?? "No summary."}");

            if (section.TryGetPropertyValue("data", out var data) && data is not null)
            {
                builder.AppendLine();
                builder.AppendLine("```json");
                builder.AppendLine(data.ToJsonString(JsonOptions));
                builder.AppendLine("```");
            }

            builder.AppendLine();
        }

        var gaps = contextPack["gaps"] as JsonArray ?? [];
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

    private static JsonNode CreateTruncatedPlaceholder(int originalLength, int remaining)
    {
        return new JsonObject
        {
            ["contentKind"] = "truncated",
            ["originalLength"] = originalLength,
            ["budgetRemaining"] = remaining,
            ["message"] = $"Data truncated: {originalLength} chars exceeds remaining budget of {remaining} chars."
        };
    }

    private sealed class CharBudget(int max)
    {
        public int Used { get; private set; }
        public int Remaining => Math.Max(0, max - Used);
        public bool IsExhausted => Used >= max;

        public bool CanFit(int length) => Remaining >= length;

        public string? Consume(string? text, int sectionLimit)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var effectiveLimit = Math.Min(sectionLimit, Remaining);
            if (effectiveLimit <= 0)
            {
                return null;
            }

            var result = text.Length <= effectiveLimit
                ? text
                : $"{text[..effectiveLimit]}... [truncated]";
            Used += result.Length;
            return result;
        }
    }

    private static bool ShouldIncludeSection(string sectionType, HashSet<string>? include, HashSet<string>? exclude)
    {
        if (exclude is { Count: > 0 } && exclude.Contains(sectionType))
        {
            return false;
        }

        if (include is { Count: > 0 })
        {
            return include.Contains(sectionType);
        }

        return true;
    }

    private static bool SectionsContainType(JsonArray sections, string type)
    {
        return sections.OfType<JsonObject>().Any(s =>
            string.Equals(ReadString(s, "type"), type, StringComparison.Ordinal));
    }

    private static HashSet<string>? ParseCsvConfig(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(part);
        }

        return result.Count == 0 ? null : result;
    }

    private static int ResolveMaxContextCharacters(JsonElement config)
    {
        var configured = WorkflowNodePayloadOperations.TryGetConfigString(config, "maxContextCharacters");
        return int.TryParse(configured, out var value) && value > 0
            ? Math.Clamp(value, 1000, 200000)
            : DefaultMaxContextCharacters;
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

    private static JsonNode? TryNavigate(JsonObject root, params string[] path)
    {
        JsonNode? current = root;
        foreach (var segment in path)
        {
            if (current is not JsonObject currentObject ||
                !currentObject.TryGetPropertyValue(segment, out current))
            {
                return null;
            }
        }

        return current;
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

    private static int ReadInt(JsonObject payload, string key)
    {
        if (payload.TryGetPropertyValue(key, out var value) && value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue)) return intValue;
            if (jsonValue.TryGetValue<long>(out var longValue)) return (int)longValue;
        }

        return 0;
    }

    private static bool IsFalse(string? value)
    {
        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }
}
