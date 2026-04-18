using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Runtime.Artifacts;

namespace Workflow.Engine.Runtime.Nodes.Pipeline;

/// <summary>
/// Что: нода подготовки raw workspace для локального pipeline.
/// Зачем: ранним шагом создать воспроизводимую папку задачи и сохранить входной контекст без code discovery и догадок.
/// Как: определяет work item id, пишет стабильные raw artifacts в `tasks/<id>/runs/<run>/` и добавляет workspace refs в payload.
/// </summary>
public sealed class WorkspacePrepareRawNodeExecutor : IWorkflowNodeExecutor
{
    private const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "workspace_prepare_raw",
        Label: "Workspace Prepare Raw",
        Description: "Create task workspace and save raw context artifacts",
        Inputs: 1,
        Outputs: 1,
        Pack: WorkflowNodePacks.Core,
        Source: WorkflowNodeSources.BuiltIn,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "workItemId",
                Label: "Work Item Id",
                FieldType: "text",
                Description: "Optional stable task folder id. Defaults to Jira key or text-task hash.",
                Placeholder: "GAME-1234"),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "writeEmptyOptionalArtifacts",
                Label: "Write Empty Optional Artifacts",
                FieldType: "select",
                Description: "Create empty logs/wiki raw artifacts even when payload has no such data.",
                DefaultValue: "false",
                Options:
                [
                    new WorkflowNodeConfigFieldOptionDescriptor("false", "No"),
                    new WorkflowNodeConfigFieldOptionDescriptor("true", "Yes")
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

        var createdAtUtc = DateTimeOffset.UtcNow;
        var workItemId = ResolveWorkItemId(context, payload);
        var taskDirectory = $"tasks/{workItemId}";
        var runDirectory = $"{taskDirectory}/runs/{SanitizePathSegment(context.RunId, "run")}";
        var artifacts = new JsonObject();

        if (TryCreateTicketContext(payload, createdAtUtc, out var ticketContext))
        {
            WriteJsonAndMarkdownArtifactPair(
                context,
                runDirectory,
                "ticket",
                ticketContext.Json,
                ticketContext.Markdown,
                artifacts);
        }

        if (TryCreateTaskContext(payload, createdAtUtc, out var taskContext))
        {
            WriteJsonAndMarkdownArtifactPair(
                context,
                runDirectory,
                "task",
                taskContext.Json,
                taskContext.Markdown,
                artifacts);
        }

        var writeEmptyOptionalArtifacts = IsTrue(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "writeEmptyOptionalArtifacts"));

        if (TryCreateOptionalRawContext(payload, createdAtUtc, "logs", out var logsContext) || writeEmptyOptionalArtifacts)
        {
            logsContext ??= CreateEmptyOptionalContext("logs", createdAtUtc);
            WriteJsonAndMarkdownArtifactPair(
                context,
                runDirectory,
                "logs",
                logsContext.Value.Json,
                logsContext.Value.Markdown,
                artifacts);
        }

        if (TryCreateOptionalRawContext(payload, createdAtUtc, "wiki", out var wikiContext) || writeEmptyOptionalArtifacts)
        {
            wikiContext ??= CreateEmptyOptionalContext("wiki", createdAtUtc);
            WriteJsonAndMarkdownArtifactPair(
                context,
                runDirectory,
                "wiki",
                wikiContext.Value.Json,
                wikiContext.Value.Markdown,
                artifacts);
        }

        var implementationLog = CreateImplementationLog(workItemId, context.RunId, createdAtUtc);
        var implementationLogDescriptor = context.ArtifactStore.WriteArtifact(new WorkflowArtifactWriteRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Name = "implementation_log.md",
            ArtifactType = "markdown",
            MediaType = "text/markdown",
            Content = implementationLog,
            WorkspaceRelativeDirectory = runDirectory,
            UseStableFileName = true
        });
        artifacts["implementation_log"] = WorkflowNodePayloadOperations.CreateArtifactReference(implementationLogDescriptor);

        var workspace = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["workItemId"] = workItemId,
            ["runId"] = context.RunId,
            ["taskDirectory"] = taskDirectory,
            ["runDirectory"] = runDirectory,
            ["createdAtUtc"] = createdAtUtc.ToString("O"),
            ["rawArtifacts"] = artifacts.DeepClone()
        };

        var latestRunDescriptor = context.ArtifactStore.WriteArtifact(new WorkflowArtifactWriteRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Name = "latest_run.json",
            ArtifactType = "json",
            MediaType = "application/json",
            Content = workspace.ToJsonString(JsonOptions),
            WorkspaceRelativeDirectory = taskDirectory,
            UseStableFileName = true
        });

        workspace["latestRun"] = WorkflowNodePayloadOperations.CreateArtifactReference(latestRunDescriptor);
        payload["workspace"] = workspace;

        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = createdAtUtc,
            NodeId = context.Node.Id,
            Message = $"Workspace prepared for {workItemId}: {runDirectory}."
        });

        return Task.FromResult(payload);
    }

    private static string ResolveWorkItemId(WorkflowNodeExecutionContext context, JsonObject payload)
    {
        var configured = WorkflowNodePayloadOperations.TryGetConfigString(context.Node.Config, "workItemId");
        var source = FirstNonEmpty(
            configured,
            ReadString(payload, "jira_issue_key"),
            ReadString(payload, "issueKey"),
            ReadString(payload, "issue_key"),
            ReadString(payload, "work_item_id"),
            ReadNestedString(payload, "task", "id"));

        if (!string.IsNullOrWhiteSpace(source))
        {
            return SanitizePathSegment(source, "work-item").ToUpperInvariant();
        }

        var taskText = ReadString(payload, "task_text")
                       ?? ReadNestedString(payload, "task", "text")
                       ?? payload.ToJsonString(JsonOptions);

        return $"TEXT-{CreateShortHash(taskText)}";
    }

    private static bool TryCreateTicketContext(
        JsonObject payload,
        DateTimeOffset createdAtUtc,
        out RawContextPair contextPair)
    {
        var issueKey = ReadString(payload, "jira_issue_key")
                       ?? ReadString(payload, "issueKey")
                       ?? ReadString(payload, "issue_key");
        if (string.IsNullOrWhiteSpace(issueKey))
        {
            contextPair = default;
            return false;
        }

        var ticket = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["source"] = "jira",
            ["capturedAtUtc"] = createdAtUtc.ToString("O"),
            ["issueKey"] = issueKey,
            ["summary"] = ReadString(payload, "jira_summary") ?? ReadString(payload, "summary"),
            ["description"] = ReadString(payload, "jira_description") ?? ReadString(payload, "description"),
            ["issueType"] = ReadString(payload, "jira_issue_type") ?? ReadString(payload, "issue_type"),
            ["severity"] = ReadString(payload, "jira_severity") ?? ReadString(payload, "severity"),
            ["priority"] = ReadString(payload, "jira_priority") ?? ReadString(payload, "priority"),
            ["labels"] = CloneOrEmptyArray(payload, "jira_labels", "labels"),
            ["acceptanceCriteria"] = ReadString(payload, "jira_acceptance_criteria")
                                    ?? ReadString(payload, "acceptance_criteria"),
            ["links"] = CloneOrNull(payload, "jira_links", "links"),
            ["comments"] = CloneOrNull(payload, "jira_comments", "comments")
        };

        contextPair = new RawContextPair(ticket, FormatTicketMarkdown(ticket));
        return true;
    }

    private static bool TryCreateTaskContext(
        JsonObject payload,
        DateTimeOffset createdAtUtc,
        out RawContextPair contextPair)
    {
        var taskText = ReadString(payload, "task_text") ?? ReadNestedString(payload, "task", "text");
        if (string.IsNullOrWhiteSpace(taskText))
        {
            contextPair = default;
            return false;
        }

        var task = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["source"] = ReadString(payload, "task_source") ?? "task_text_input",
            ["capturedAtUtc"] = createdAtUtc.ToString("O"),
            ["text"] = taskText,
            ["task"] = CloneOrNull(payload, "task")
        };

        contextPair = new RawContextPair(task, FormatTaskMarkdown(task));
        return true;
    }

    private static bool TryCreateOptionalRawContext(
        JsonObject payload,
        DateTimeOffset createdAtUtc,
        string kind,
        out RawContextPair? contextPair)
    {
        var candidates = kind switch
        {
            "logs" => new[] { "logs", "raw_logs", "log_events", "logs_text" },
            "wiki" => new[] { "wiki", "wiki_pages", "wiki_context", "wiki_text", "documentation" },
            _ => Array.Empty<string>()
        };

        foreach (var candidate in candidates)
        {
            if (!payload.TryGetPropertyValue(candidate, out var value) || value is null)
            {
                continue;
            }

            var json = new JsonObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["kind"] = kind,
                ["sourceField"] = candidate,
                ["capturedAtUtc"] = createdAtUtc.ToString("O"),
                ["data"] = value.DeepClone()
            };
            contextPair = new RawContextPair(json, FormatOptionalMarkdown(kind, candidate, value));
            return true;
        }

        contextPair = null;
        return false;
    }

    private static RawContextPair CreateEmptyOptionalContext(string kind, DateTimeOffset createdAtUtc)
    {
        var json = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["kind"] = kind,
            ["capturedAtUtc"] = createdAtUtc.ToString("O"),
            ["data"] = new JsonArray()
        };

        return new RawContextPair(
            json,
            $"# Raw {ToTitle(kind)}{Environment.NewLine}{Environment.NewLine}No {kind} context was provided.{Environment.NewLine}");
    }

    private static void WriteJsonAndMarkdownArtifactPair(
        WorkflowNodeExecutionContext context,
        string runDirectory,
        string baseName,
        JsonObject json,
        string markdown,
        JsonObject artifacts)
    {
        var jsonDescriptor = context.ArtifactStore.WriteArtifact(new WorkflowArtifactWriteRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Name = $"{baseName}.json",
            ArtifactType = "json",
            MediaType = "application/json",
            Content = json.ToJsonString(JsonOptions),
            WorkspaceRelativeDirectory = runDirectory,
            UseStableFileName = true
        });

        var markdownDescriptor = context.ArtifactStore.WriteArtifact(new WorkflowArtifactWriteRequest
        {
            RunId = context.RunId,
            NodeId = context.Node.Id,
            Name = $"{baseName}.md",
            ArtifactType = "markdown",
            MediaType = "text/markdown",
            Content = markdown,
            WorkspaceRelativeDirectory = runDirectory,
            UseStableFileName = true
        });

        artifacts[baseName] = new JsonObject
        {
            ["json"] = WorkflowNodePayloadOperations.CreateArtifactReference(jsonDescriptor),
            ["markdown"] = WorkflowNodePayloadOperations.CreateArtifactReference(markdownDescriptor)
        };
    }

    private static string CreateImplementationLog(string workItemId, string runId, DateTimeOffset createdAtUtc)
    {
        return $"""
            # Implementation Log

            Work item: `{workItemId}`
            Run: `{runId}`
            Created (UTC): `{createdAtUtc:O}`

            ## Entries

            - Workspace prepared. No implementation steps have run yet.
            """;
    }

    private static string FormatTicketMarkdown(JsonObject ticket)
    {
        return $"""
            # Ticket

            - Issue key: `{ReadString(ticket, "issueKey") ?? "unknown"}`
            - Type: `{ReadString(ticket, "issueType") ?? "unknown"}`
            - Severity: `{ReadString(ticket, "severity") ?? "unknown"}`
            - Priority: `{ReadString(ticket, "priority") ?? "unknown"}`

            ## Summary

            {ReadString(ticket, "summary") ?? "No summary provided."}

            ## Description

            {ReadString(ticket, "description") ?? "No description provided."}

            ## Acceptance Criteria

            {ReadString(ticket, "acceptanceCriteria") ?? "No acceptance criteria provided."}
            """;
    }

    private static string FormatTaskMarkdown(JsonObject task)
    {
        return $"""
            # Task

            Source: `{ReadString(task, "source") ?? "unknown"}`

            ## Text

            {ReadString(task, "text") ?? "No task text provided."}
            """;
    }

    private static string FormatOptionalMarkdown(string kind, string sourceField, JsonNode value)
    {
        return $"""
            # Raw {ToTitle(kind)}

            Source field: `{sourceField}`

            ```json
            {value.ToJsonString(JsonOptions)}
            ```
            """;
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

    private static JsonArray CloneOrEmptyArray(JsonObject payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!payload.TryGetPropertyValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value is JsonArray array)
            {
                return new JsonArray(array.Select(item => item?.DeepClone()).ToArray());
            }

            if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
            {
                return new JsonArray(
                    stringValue
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Select(item => (JsonNode?)item)
                        .ToArray());
            }

            return new JsonArray(value.DeepClone());
        }

        return new JsonArray();
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

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes)[..10];
    }

    private static string SanitizePathSegment(string value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            builder.Append(invalidChars.Contains(character) || character is '/' or '\\' ? '_' : character);
        }

        var result = builder.ToString().Trim().Replace('.', '_');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static string ToTitle(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private readonly record struct RawContextPair(JsonObject Json, string Markdown);
}
