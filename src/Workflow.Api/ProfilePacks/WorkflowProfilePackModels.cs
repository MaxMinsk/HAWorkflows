using System.Text.Json;
using Workflow.Engine.Definitions;
using Workflow.Persistence.WorkflowDefinitions;

namespace Workflow.Api.ProfilePacks;

/// <summary>
/// Что: фабрика платформенного workflow profile pack.
/// Зачем: разработчики должны переносить workflow-графы между локальными инстансами вместе с важными execution policy refs.
/// Как: берет сохраненную workflow-версию, оставляет graph/node config как источник истины и извлекает refs на routing stages, agent profiles и MCP profiles.
/// </summary>
public static class WorkflowProfilePackFactory
{
    public const string CurrentSchemaVersion = "1.0";

    public static WorkflowProfilePackDocument Create(
        StoredWorkflowDefinition storedWorkflow,
        WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(storedWorkflow);
        ArgumentNullException.ThrowIfNull(definition);

        return new WorkflowProfilePackDocument
        {
            ProfilePackSchemaVersion = CurrentSchemaVersion,
            Metadata = new WorkflowProfilePackMetadata
            {
                Name = storedWorkflow.Name,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                SourceWorkflowId = storedWorkflow.WorkflowId,
                SourceWorkflowVersion = storedWorkflow.Version,
                SourceWorkflowStatus = storedWorkflow.Status,
                SourcePublishedAtUtc = storedWorkflow.PublishedAtUtc
            },
            Definition = definition,
            ExecutionPolicyRefs = ExtractExecutionPolicyRefs(definition)
        };
    }

    private static WorkflowProfileExecutionPolicyRefs ExtractExecutionPolicyRefs(WorkflowDefinition definition)
    {
        var nodeRefs = definition.Nodes
            .Select(CreateNodePolicyRef)
            .ToArray();

        return new WorkflowProfileExecutionPolicyRefs
        {
            NodeTypes = definition.Nodes
                .Select(node => node.Type)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            AgentProfiles = nodeRefs
                .Select(node => node.AgentProfile)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            McpServerProfiles = nodeRefs
                .Select(node => node.McpServerProfile)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            RoutingStages = nodeRefs
                .Select(node => node.RoutingStage)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            NodePolicyRefs = nodeRefs
                .Where(node =>
                    !string.IsNullOrWhiteSpace(node.AgentProfile) ||
                    !string.IsNullOrWhiteSpace(node.McpServerProfile) ||
                    !string.IsNullOrWhiteSpace(node.RoutingStage))
                .ToArray()
        };
    }

    private static WorkflowProfileNodePolicyRef CreateNodePolicyRef(WorkflowNodeDefinition node)
    {
        return new WorkflowProfileNodePolicyRef
        {
            NodeId = node.Id,
            NodeType = node.Type,
            NodeName = node.Name,
            RoutingStage = ReadConfigString(node.Config, "stage") ??
                           ReadConfigString(node.Config, "routingStage") ??
                           ReadConfigString(node.Config, "modelStage"),
            AgentProfile = ReadConfigString(node.Config, "agentProfile"),
            McpServerProfile = ReadConfigString(node.Config, "serverProfile")
        };
    }

    private static string? ReadConfigString(JsonElement config, string propertyName)
    {
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        var rawValue = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
        return string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();
    }
}

/// <summary>
/// Что: переносимый JSON-документ workflow profile.
/// Зачем: один файл должен содержать workflow graph, node configs и диагностические refs на внешние execution policies.
/// Как: `Definition` остается исполняемым контрактом, а `ExecutionPolicyRefs` помогает понять зависимости профиля до запуска.
/// </summary>
public sealed class WorkflowProfilePackDocument
{
    public string ProfilePackSchemaVersion { get; init; } = WorkflowProfilePackFactory.CurrentSchemaVersion;

    public WorkflowProfilePackMetadata Metadata { get; init; } = new();

    public WorkflowDefinition Definition { get; init; } = new();

    public WorkflowProfileExecutionPolicyRefs ExecutionPolicyRefs { get; init; } = new();
}

/// <summary>
/// Что: metadata источника profile pack.
/// Зачем: при обмене файлами важно видеть, откуда экспортирована настройка и какая версия workflow была источником.
/// Как: заполняется на export, но не влияет на import/runtime.
/// </summary>
public sealed class WorkflowProfilePackMetadata
{
    public string Name { get; init; } = string.Empty;

    public DateTimeOffset ExportedAtUtc { get; init; }

    public string? SourceWorkflowId { get; init; }

    public int? SourceWorkflowVersion { get; init; }

    public string? SourceWorkflowStatus { get; init; }

    public DateTimeOffset? SourcePublishedAtUtc { get; init; }
}

/// <summary>
/// Что: выжимка ссылок на внешние execution policies.
/// Зачем: profile pack должен явно показывать, какие agent/MCP/model routing настройки нужны для воспроизводимого запуска.
/// Как: значения извлекаются из config нод, но не дублируют саму config-структуру.
/// </summary>
public sealed class WorkflowProfileExecutionPolicyRefs
{
    public IReadOnlyList<string> NodeTypes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RoutingStages { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AgentProfiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> McpServerProfiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<WorkflowProfileNodePolicyRef> NodePolicyRefs { get; init; } =
        Array.Empty<WorkflowProfileNodePolicyRef>();
}

/// <summary>
/// Что: refs одной ноды на execution policies.
/// Зачем: при import/review профиля можно быстро понять, какая нода требует agent profile, MCP server profile или routing stage.
/// Как: это derived metadata; runtime продолжает читать реальные значения из node config.
/// </summary>
public sealed class WorkflowProfileNodePolicyRef
{
    public string NodeId { get; init; } = string.Empty;

    public string NodeType { get; init; } = string.Empty;

    public string NodeName { get; init; } = string.Empty;

    public string? RoutingStage { get; init; }

    public string? AgentProfile { get; init; }

    public string? McpServerProfile { get; init; }
}
