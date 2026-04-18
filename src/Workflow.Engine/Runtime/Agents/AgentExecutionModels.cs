using System.Text.Json.Nodes;

namespace Workflow.Engine.Runtime.Agents;

/// <summary>
/// Что: входные данные для синхронного agent ask.
/// Зачем: отделить workflow-ноду от конкретного Cursor/Claude/другого adapter-а.
/// Как: AgentTask node собирает prompt/input/context и передает их выбранному IAgentExecutor.
/// </summary>
public sealed class AgentAskRequest
{
    public required string RunId { get; init; }

    public required string NodeId { get; init; }

    public required string Profile { get; init; }

    public string? SelectedTier { get; init; }

    public string? SelectedModel { get; init; }

    public string? ThinkingMode { get; init; }

    public string? RouteReason { get; init; }

    public required string Prompt { get; init; }

    public JsonObject Input { get; init; } = new();
}

/// <summary>
/// Что: результат agent ask.
/// Зачем: вернуть текст ответа и диагностические поля без знания конкретного provider-а.
/// Как: adapter заполняет Text/Metadata, runtime сериализует это в node output.
/// </summary>
public sealed class AgentAskResult
{
    public required string Text { get; init; }

    public string Status { get; init; } = "succeeded";

    public JsonObject Metadata { get; init; } = new();
}

/// <summary>
/// Что: запрос на создание долгой agent-задачи.
/// Зачем: поддержать future flow Cursor/Claude agent mode, где задача живет отдельно от одного HTTP call.
/// Как: adapter возвращает task id, а node или будущий checkpoint layer опрашивает status/result.
/// </summary>
public sealed class AgentTaskCreateRequest
{
    public required string RunId { get; init; }

    public required string NodeId { get; init; }

    public required string Profile { get; init; }

    public string? SelectedTier { get; init; }

    public string? SelectedModel { get; init; }

    public string? ThinkingMode { get; init; }

    public string? RouteReason { get; init; }

    public required string Prompt { get; init; }

    public JsonObject Input { get; init; } = new();
}

/// <summary>
/// Что: handle созданной agent-задачи.
/// Зачем: унифицировать CreateTask независимо от того, это local process, Cursor API или Claude Code.
/// Как: TaskId хранится в node output/logs и используется GetStatus/GetResult.
/// </summary>
public sealed class AgentTaskHandle
{
    public required string TaskId { get; init; }

    public string Status { get; init; } = "created";

    public JsonObject Metadata { get; init; } = new();
}

/// <summary>
/// Что: запрос статуса agent-задачи.
/// Зачем: отделить polling/checkpoint логику от конкретного adapter-а.
/// Как: future checkpoint runtime сможет повторять GetStatus без повторного CreateTask.
/// </summary>
public sealed class AgentTaskStatusRequest
{
    public required string TaskId { get; init; }

    public required string Profile { get; init; }
}

/// <summary>
/// Что: статус agent-задачи.
/// Зачем: runtime и UI должны видеть единый статус created/running/succeeded/failed.
/// Как: adapter маппит свой provider-specific status в этот контракт.
/// </summary>
public sealed class AgentTaskStatusResult
{
    public required string TaskId { get; init; }

    public required string Status { get; init; }

    public JsonObject Metadata { get; init; } = new();
}

/// <summary>
/// Что: запрос финального результата agent-задачи.
/// Зачем: забрать итоговую выдачу task-mode adapter-а.
/// Как: вызывается после succeeded status или при future resume.
/// </summary>
public sealed class AgentTaskResultRequest
{
    public required string TaskId { get; init; }

    public required string Profile { get; init; }
}

/// <summary>
/// Что: финальный результат agent-задачи.
/// Зачем: единый output для Cursor/Claude/local adapters.
/// Как: Text идет дальше в workflow payload, Metadata хранит provider diagnostics.
/// </summary>
public sealed class AgentTaskResult
{
    public required string TaskId { get; init; }

    public required string Text { get; init; }

    public string Status { get; init; } = "succeeded";

    public JsonObject Metadata { get; init; } = new();
}
