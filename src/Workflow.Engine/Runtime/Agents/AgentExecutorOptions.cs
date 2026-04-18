namespace Workflow.Engine.Runtime.Agents;

/// <summary>
/// Что: конфигурация agent profiles.
/// Зачем: менять соответствие stage/profile -> adapter без изменения graph JSON и кода.
/// Как: backend config задает DefaultProfile и Profiles[profile].Adapter.
/// </summary>
public sealed class AgentExecutorOptions
{
    public string DefaultProfile { get; init; } = "echo";

    public Dictionary<string, AgentProfileOptions> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Что: описание одного agent profile.
/// Зачем: один и тот же adapter можно переиспользовать под разными stage/profile настройками.
/// Как: Adapter выбирает IAgentExecutor, Settings оставлены как future extension для Cursor/Claude/local CLI.
/// </summary>
public sealed class AgentProfileOptions
{
    public string Adapter { get; init; } = "echo";

    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
