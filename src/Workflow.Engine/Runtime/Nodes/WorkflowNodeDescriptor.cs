namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: описание типа workflow-ноды для UI и runtime-реестра.
/// Зачем: единый контракт метаданных (label/ports/scope) для автопоиска нод.
/// Как: формируется каждым node executor и отдается через каталог нод.
/// </summary>
public sealed record WorkflowNodeDescriptor(
    string Type,
    string Label,
    string Description,
    int Inputs,
    int Outputs,
    bool ProducesRunOutput = false,
    IReadOnlyList<WorkflowNodeConfigFieldDescriptor>? ConfigFields = null,
    IReadOnlyList<WorkflowNodePortDescriptor>? InputPorts = null,
    IReadOnlyList<WorkflowNodePortDescriptor>? OutputPorts = null,
    string Pack = WorkflowNodePacks.Core,
    string Source = WorkflowNodeSources.BuiltIn,
    bool UsesModel = false)
{
    public IReadOnlyList<WorkflowNodePortDescriptor> GetInputPorts()
    {
        return NormalizePorts(InputPorts, Inputs, "input");
    }

    public IReadOnlyList<WorkflowNodePortDescriptor> GetOutputPorts()
    {
        return NormalizePorts(OutputPorts, Outputs, "output");
    }

    private static IReadOnlyList<WorkflowNodePortDescriptor> NormalizePorts(
        IReadOnlyList<WorkflowNodePortDescriptor>? configuredPorts,
        int count,
        string prefix)
    {
        if (configuredPorts is { Count: > 0 })
        {
            return configuredPorts;
        }

        if (count <= 0)
        {
            return Array.Empty<WorkflowNodePortDescriptor>();
        }

        return Enumerable
            .Range(1, count)
            .Select(index => new WorkflowNodePortDescriptor(
                Id: $"{prefix}_{index}",
                Label: $"{prefix} {index}",
                Channel: WorkflowPortChannels.Data))
            .ToArray();
    }
}

/// <summary>
/// Что: канонические имена node pack-ов.
/// Зачем: группировать ноды по capability/package без product split-а remote/local.
/// Как: descriptor каждой ноды указывает Pack, а catalog может отключать pack-и через policy.
/// </summary>
public static class WorkflowNodePacks
{
    public const string Core = "core";
}

/// <summary>
/// Что: источник node descriptor-а.
/// Зачем: UI и diagnostics должны понимать происхождение ноды без remote/local режима.
/// Как: значение отдается через `/node-types` вместе с Pack.
/// </summary>
public static class WorkflowNodeSources
{
    public const string BuiltIn = "built_in";
}

/// <summary>
/// Что: описание одного входного или выходного порта ноды.
/// Зачем: валидировать совместимость соединений по каналам (`data`, `artifact_ref`, `memory_ref`, `control_*`).
/// Как: id совпадает с Drawflow class (`input_1`/`output_1`), channel задает допустимый тип связи.
/// </summary>
public sealed record WorkflowNodePortDescriptor(
    string Id,
    string Label,
    string Channel,
    bool Required = false,
    IReadOnlyList<string>? AcceptedKinds = null,
    string? ControlConditionKey = null);

/// <summary>
/// Что: канонические channel-типы портов.
/// Зачем: не размазывать string literals по runtime, validator и API.
/// Как: точное совпадение channel-ов используется как базовое правило совместимости edge.
/// </summary>
public static class WorkflowPortChannels
{
    public const string Data = "data";
    public const string ArtifactRef = "artifact_ref";
    public const string MemoryRef = "memory_ref";
    public const string ControlOk = "control_ok";
    public const string ControlFail = "control_fail";
    public const string ControlApprovalRequired = "control_approval_required";
}

/// <summary>
/// Что: описание поля конфигурации ноды для UI Inspector.
/// Зачем: рендерить typed-форму вместо обязательного raw JSON редактирования.
/// Как: задается node executor-ом и отдается через `/node-types`.
/// </summary>
public sealed record WorkflowNodeConfigFieldDescriptor(
    string Key,
    string Label,
    string FieldType,
    string? Description = null,
    bool Required = false,
    bool Multiline = false,
    string? Placeholder = null,
    string? DefaultValue = null,
    IReadOnlyList<WorkflowNodeConfigFieldOptionDescriptor>? Options = null);

/// <summary>
/// Что: вариант значения для select-полей конфигурации ноды.
/// Зачем: дать Inspector фиксированный список допустимых значений.
/// Как: используется внутри `WorkflowNodeConfigFieldDescriptor.Options`.
/// </summary>
public sealed record WorkflowNodeConfigFieldOptionDescriptor(
    string Value,
    string Label);
