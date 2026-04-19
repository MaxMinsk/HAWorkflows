using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Nodes.Pipeline;

/// <summary>
/// Что: нода выбора pipeline-шаблона.
/// Зачем: ранняя маршрутизация run по типу задачи (bugfix/feature/incident/tech_debt) до этапов collect/plan.
/// Как: анализирует config + payload сигналы (issue type/labels/severity) и записывает template + branch flags.
/// </summary>
public sealed class TemplateSelectNodeExecutor : IWorkflowNodeExecutor
{
    private const string BugfixTemplate = "bugfix";
    private const string FeatureTemplate = "feature";
    private const string IncidentTemplate = "incident";
    private const string TechDebtTemplate = "tech_debt";

    private static readonly HashSet<string> AllowedTemplates =
    [
        BugfixTemplate,
        FeatureTemplate,
        IncidentTemplate,
        TechDebtTemplate
    ];

    public WorkflowNodeDescriptor Descriptor { get; } = new(
        Type: "template_select",
        Label: "Template Select",
        Description: "Select pipeline template from issue signals",
        Inputs: 1,
        Outputs: 4,
        Pack: WorkflowNodePacks.Core,
        Source: WorkflowNodeSources.BuiltIn,
        ConfigFields:
        [
            new WorkflowNodeConfigFieldDescriptor(
                Key: "defaultTemplate",
                Label: "Default Template",
                FieldType: "select",
                Description: "Template when heuristic signals are insufficient.",
                DefaultValue: BugfixTemplate,
                Options: CreateTemplateOptions()),
            new WorkflowNodeConfigFieldDescriptor(
                Key: "forceTemplate",
                Label: "Force Template",
                FieldType: "select",
                Description: "Override heuristic and always use selected template.",
                Options: CreateForceTemplateOptions())
        ],
        InputPorts:
        [
            new WorkflowNodePortDescriptor("input_1", "Data", WorkflowPortChannels.Data, Required: true)
        ],
        OutputPorts:
        [
            new WorkflowNodePortDescriptor("output_1", "Data", WorkflowPortChannels.Data),
            new WorkflowNodePortDescriptor(
                "output_2",
                "Requires Logs",
                WorkflowPortChannels.ControlOk,
                ControlConditionKey: "pipeline_branches.requires_logs_collect"),
            new WorkflowNodePortDescriptor(
                "output_3",
                "Requires Wiki",
                WorkflowPortChannels.ControlOk,
                ControlConditionKey: "pipeline_branches.requires_wiki_collect"),
            new WorkflowNodePortDescriptor(
                "output_4",
                "Requires Approval",
                WorkflowPortChannels.ControlApprovalRequired,
                ControlConditionKey: "pipeline_branches.requires_human_approve")
        ]);

    public Task<JsonObject> ExecuteAsync(WorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = context.InboundPayloads.Count == 0
            ? WorkflowNodePayloadOperations.CloneObject(context.RunInputPayload)
            : WorkflowNodePayloadOperations.MergePayloads(context.InboundPayloads);

        WorkflowNodePayloadOperations.ApplySetRemoveConfig(context.Node.Config, payload);

        var configForcedTemplate = NormalizeTemplate(WorkflowNodePayloadOperations.TryGetConfigString(
            context.Node.Config,
            "forceTemplate"));
        if (configForcedTemplate is not null)
        {
            ApplyTemplate(payload, configForcedTemplate, "config.forceTemplate");
            WriteLog(context, configForcedTemplate, "config.forceTemplate");
            return Task.FromResult(payload);
        }

        var payloadHintTemplate = NormalizeTemplate(ReadString(payload, "pipeline_template_hint"));
        if (payloadHintTemplate is not null)
        {
            ApplyTemplate(payload, payloadHintTemplate, "payload.pipeline_template_hint");
            WriteLog(context, payloadHintTemplate, "payload.pipeline_template_hint");
            return Task.FromResult(payload);
        }

        var signals = CollectSignals(payload);
        var selectedTemplate = SelectTemplate(signals, context.Node.Config);
        ApplyTemplate(payload, selectedTemplate, "heuristic");

        payload["pipeline_template_signals"] = new JsonObject
        {
            ["issue_type"] = signals.IssueType,
            ["severity"] = signals.Severity,
            ["labels"] = new JsonArray(signals.Labels.Select(label => (JsonNode?)label).ToArray())
        };

        WriteLog(context, selectedTemplate, "heuristic");
        return Task.FromResult(payload);
    }

    private static string SelectTemplate(TemplateSignals signals, JsonElement config)
    {
        if (LooksLikeIncident(signals))
        {
            return IncidentTemplate;
        }

        if (LooksLikeTechDebt(signals))
        {
            return TechDebtTemplate;
        }

        if (LooksLikeFeature(signals))
        {
            return FeatureTemplate;
        }

        if (LooksLikeBugfix(signals))
        {
            return BugfixTemplate;
        }

        var configuredDefault = NormalizeTemplate(
            WorkflowNodePayloadOperations.TryGetConfigString(config, "defaultTemplate"));
        return configuredDefault ?? BugfixTemplate;
    }

    private static bool LooksLikeIncident(TemplateSignals signals)
    {
        if (ContainsAny(signals.Severity, "critical", "blocker", "sev1", "sev0", "p0", "p1"))
        {
            return true;
        }

        return ContainsAnyLabel(signals.Labels, "incident", "outage", "production", "hotfix", "sev1", "sev0");
    }

    private static bool LooksLikeTechDebt(TemplateSignals signals)
    {
        if (ContainsAny(signals.IssueType, "tech debt", "technical debt", "refactor", "chore"))
        {
            return true;
        }

        return ContainsAnyLabel(signals.Labels, "tech_debt", "tech-debt", "technical_debt", "refactor", "chore");
    }

    private static bool LooksLikeFeature(TemplateSignals signals)
    {
        if (ContainsAny(signals.IssueType, "feature", "story", "epic", "enhancement"))
        {
            return true;
        }

        return ContainsAnyLabel(signals.Labels, "feature", "enhancement", "product");
    }

    private static bool LooksLikeBugfix(TemplateSignals signals)
    {
        if (ContainsAny(signals.IssueType, "bug", "defect", "fix", "hotfix"))
        {
            return true;
        }

        return ContainsAnyLabel(signals.Labels, "bug", "bugfix", "defect", "hotfix");
    }

    private static bool ContainsAny(string? value, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAnyLabel(IReadOnlyList<string> labels, params string[] needles)
    {
        if (labels.Count == 0)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (needles.Any(needle => label.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string? NormalizeTemplate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace('-', '_').ToLowerInvariant();
        return AllowedTemplates.Contains(normalized) ? normalized : null;
    }

    private static TemplateSignals CollectSignals(JsonObject payload)
    {
        var issueType = ReadString(payload, "jira_issue_type")
                        ?? ReadString(payload, "issue_type")
                        ?? ReadString(payload, "type");
        var severity = ReadString(payload, "jira_severity")
                       ?? ReadString(payload, "severity")
                       ?? ReadString(payload, "priority")
                       ?? ReadString(payload, "jira_priority");
        var labels = ReadStringArray(payload, "jira_labels");
        if (labels.Count == 0)
        {
            labels = ReadStringArray(payload, "labels");
        }

        return new TemplateSignals(
            IssueType: issueType?.Trim().ToLowerInvariant(),
            Severity: severity?.Trim().ToLowerInvariant(),
            Labels: labels.Select(label => label.Trim().ToLowerInvariant()).Where(label => label.Length > 0).ToArray());
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

    private static IReadOnlyList<string> ReadStringArray(JsonObject payload, string key)
    {
        if (!payload.TryGetPropertyValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        if (value is JsonArray array)
        {
            return array
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .ToArray();
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var single))
        {
            if (string.IsNullOrWhiteSpace(single))
            {
                return Array.Empty<string>();
            }

            return single
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static void ApplyTemplate(JsonObject payload, string template, string source)
    {
        payload["pipeline_template"] = template;
        payload["pipeline_template_source"] = source;

        var requiresLogs = template is BugfixTemplate or IncidentTemplate;
        var requiresWiki = template is FeatureTemplate or TechDebtTemplate;
        var requiresHumanApprove = template is FeatureTemplate or IncidentTemplate;

        payload["pipeline_branches"] = new JsonObject
        {
            ["requires_logs_collect"] = requiresLogs,
            ["requires_wiki_collect"] = requiresWiki,
            ["requires_human_approve"] = requiresHumanApprove
        };
    }

    private static void WriteLog(WorkflowNodeExecutionContext context, string selectedTemplate, string source)
    {
        context.Logs.Add(new WorkflowExecutionLogItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            NodeId = context.Node.Id,
            Message = $"Template selected: {selectedTemplate} (source: {source})."
        });
    }

    private sealed record TemplateSignals(string? IssueType, string? Severity, IReadOnlyList<string> Labels);

    private static IReadOnlyList<WorkflowNodeConfigFieldOptionDescriptor> CreateTemplateOptions()
    {
        return
        [
            new WorkflowNodeConfigFieldOptionDescriptor(BugfixTemplate, "Bugfix"),
            new WorkflowNodeConfigFieldOptionDescriptor(FeatureTemplate, "Feature"),
            new WorkflowNodeConfigFieldOptionDescriptor(IncidentTemplate, "Incident"),
            new WorkflowNodeConfigFieldOptionDescriptor(TechDebtTemplate, "Tech Debt")
        ];
    }

    private static IReadOnlyList<WorkflowNodeConfigFieldOptionDescriptor> CreateForceTemplateOptions()
    {
        return
        [
            new WorkflowNodeConfigFieldOptionDescriptor(string.Empty, "Auto (heuristic)"),
            new WorkflowNodeConfigFieldOptionDescriptor(BugfixTemplate, "Force Bugfix"),
            new WorkflowNodeConfigFieldOptionDescriptor(FeatureTemplate, "Force Feature"),
            new WorkflowNodeConfigFieldOptionDescriptor(IncidentTemplate, "Force Incident"),
            new WorkflowNodeConfigFieldOptionDescriptor(TechDebtTemplate, "Force Tech Debt")
        ];
    }
}
