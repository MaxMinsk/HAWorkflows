using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workflow.Engine.Definitions;

/// <summary>
/// Что: контракт описания workflow-графа.
/// Зачем: единая строгая модель для валидации и исполнения DAG.
/// Как: хранит schemaVersion, имя, ноды и ребра, соответствующие schema v1.
/// </summary>
public sealed class WorkflowDefinition
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("nodes")]
    public IReadOnlyList<WorkflowNodeDefinition> Nodes { get; init; } = Array.Empty<WorkflowNodeDefinition>();

    [JsonPropertyName("edges")]
    public IReadOnlyList<WorkflowEdgeDefinition> Edges { get; init; } = Array.Empty<WorkflowEdgeDefinition>();
}

/// <summary>
/// Что: описание ноды workflow.
/// Зачем: задает id/type/name/config для исполнения конкретного шага.
/// Как: type сверяется с активным runtime-каталогом нод.
/// </summary>
public sealed class WorkflowNodeDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("config")]
    public JsonElement Config { get; init; }
}

/// <summary>
/// Что: направленное ребро между нодами.
/// Зачем: определяет порядок передачи typed-channel данных между шагами графа.
/// Как: source/target + port ids сверяются с node descriptor-ами (`data`, `artifact_ref`, `memory_ref`, `control_*`).
/// </summary>
public sealed class WorkflowEdgeDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("sourceNodeId")]
    public string SourceNodeId { get; init; } = string.Empty;

    [JsonPropertyName("targetNodeId")]
    public string TargetNodeId { get; init; } = string.Empty;

    [JsonPropertyName("sourcePort")]
    public string SourcePort { get; init; } = string.Empty;

    [JsonPropertyName("targetPort")]
    public string TargetPort { get; init; } = string.Empty;
}
