using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Workflow.Engine.Runtime.Agents;

/// <summary>
/// Что: agent adapter для локальных coding-агентов (Claude Code CLI, Cursor CLI).
/// Зачем: workflow runtime запускает реальные agent-задачи через subprocess, а не echo stub.
/// Как: резолвит agentType из profile settings, запускает CLI subprocess, пайпит промпт через stdin,
///      парсит JSON output (для claude_code) или raw text и возвращает результат через IAgentExecutor контракт.
///
/// Profile Settings (AgentProfileOptions.Settings):
///   agentType       — "claude_code" (default) | "cursor"
///   executable      — path to CLI binary (default: auto-detect from agentType)
///   workingDirectory — base working directory for the agent process
///   timeoutSeconds  — max execution time (default: 300)
///   maxTurns        — max conversation turns (default: unset = agent decides)
///   model           — model override (optional, passed to CLI)
/// </summary>
public sealed class LocalAgentExecutor : IAgentExecutor
{
    private const int DefaultTimeoutSeconds = 300;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AgentExecutorOptions _options;
    private readonly ILogger<LocalAgentExecutor> _logger;
    private readonly ConcurrentDictionary<string, AgentTaskResult> _taskResults = new(StringComparer.Ordinal);

    public LocalAgentExecutor(AgentExecutorOptions options, ILogger<LocalAgentExecutor> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string AdapterName => "local";

    public async Task<AgentAskResult> AskAsync(AgentAskRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = ResolveSettings(request.Profile);
        var workDir = ResolveWorkingDirectory(settings, request.Input);

        _logger.LogInformation(
            "LocalAgent ask: profile={Profile}, agentType={AgentType}, executable={Executable}, workDir={WorkDir}",
            request.Profile, settings.AgentType, settings.Executable, workDir);

        var processResult = await RunAgentProcessAsync(
            request.Prompt, settings, workDir, isAsk: true, cancellationToken);

        return new AgentAskResult
        {
            Text = processResult.ResultText,
            Status = processResult.IsError ? "failed" : "succeeded",
            Metadata = BuildMetadata(request.Profile, settings, processResult)
        };
    }

    public async Task<AgentTaskHandle> CreateTaskAsync(
        AgentTaskCreateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = ResolveSettings(request.Profile);
        var workDir = ResolveWorkingDirectory(settings, request.Input);
        var taskId = $"local:{request.RunId}:{request.NodeId}:{Guid.NewGuid():N}";

        _logger.LogInformation(
            "LocalAgent task: taskId={TaskId}, profile={Profile}, agentType={AgentType}, workDir={WorkDir}",
            taskId, request.Profile, settings.AgentType, workDir);

        var processResult = await RunAgentProcessAsync(
            request.Prompt, settings, workDir, isAsk: false, cancellationToken);

        var taskResult = new AgentTaskResult
        {
            TaskId = taskId,
            Text = processResult.ResultText,
            Status = processResult.IsError ? "failed" : "succeeded",
            Metadata = BuildMetadata(request.Profile, settings, processResult)
        };
        _taskResults[taskId] = taskResult;

        return new AgentTaskHandle
        {
            TaskId = taskId,
            Status = taskResult.Status,
            Metadata = new JsonObject { ["adapter"] = AdapterName, ["agentType"] = settings.AgentType }
        };
    }

    public Task<AgentTaskStatusResult> GetStatusAsync(
        AgentTaskStatusRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = _taskResults.ContainsKey(request.TaskId) ? "succeeded" : "not_found";
        if (_taskResults.TryGetValue(request.TaskId, out var result) &&
            string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            status = "failed";
        }

        return Task.FromResult(new AgentTaskStatusResult
        {
            TaskId = request.TaskId,
            Status = status,
            Metadata = new JsonObject { ["adapter"] = AdapterName }
        });
    }

    public Task<AgentTaskResult> GetResultAsync(
        AgentTaskResultRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_taskResults.TryGetValue(request.TaskId, out var result))
        {
            throw new InvalidOperationException(
                $"Local agent task '{request.TaskId}' was not found.");
        }

        return Task.FromResult(result);
    }

    private async Task<AgentProcessResult> RunAgentProcessAsync(
        string prompt,
        ResolvedSettings settings,
        string? workingDirectory,
        bool isAsk,
        CancellationToken cancellationToken)
    {
        var promptFile = Path.Combine(Path.GetTempPath(), $"hawf-agent-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(promptFile, prompt, Encoding.UTF8, cancellationToken);

            var psi = settings.AgentType switch
            {
                "cursor" => BuildCursorProcessStartInfo(settings, promptFile, workingDirectory),
                _ => BuildClaudeCodeProcessStartInfo(settings, promptFile, workingDirectory, isAsk)
            };

            _logger.LogDebug(
                "Starting agent process: {FileName} {Arguments}",
                psi.FileName, string.Join(' ', psi.ArgumentList));

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            var sw = Stopwatch.StartNew();

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) stderrBuilder.AppendLine(e.Data);
            };

            if (!process.Start())
            {
                return AgentProcessResult.Error(
                    $"Failed to start {settings.AgentType} process: {psi.FileName}", 0);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKillProcess(process);
                sw.Stop();
                return AgentProcessResult.Error(
                    $"Agent process timed out after {settings.TimeoutSeconds}s", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                throw;
            }

            sw.Stop();

            var stdout = stdoutBuilder.ToString();
            var stderr = stderrBuilder.ToString();
            var exitCode = process.ExitCode;

            _logger.LogInformation(
                "Agent process completed: exitCode={ExitCode}, durationMs={Duration}, stdoutLen={StdoutLen}",
                exitCode, sw.ElapsedMilliseconds, stdout.Length);

            if (exitCode != 0)
            {
                var errorText = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                return AgentProcessResult.Error(
                    $"Agent process exited with code {exitCode}: {Truncate(errorText, 2000)}",
                    sw.ElapsedMilliseconds,
                    exitCode);
            }

            return settings.AgentType switch
            {
                "cursor" => ParseRawOutput(stdout, sw.ElapsedMilliseconds, exitCode),
                _ => ParseClaudeCodeOutput(stdout, sw.ElapsedMilliseconds, exitCode)
            };
        }
        finally
        {
            TryDeleteFile(promptFile);
        }
    }

    private static ProcessStartInfo BuildClaudeCodeProcessStartInfo(
        ResolvedSettings settings,
        string promptFilePath,
        string? workingDirectory,
        bool isAsk)
    {
        var psi = new ProcessStartInfo
        {
            FileName = settings.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        if (!string.IsNullOrWhiteSpace(settings.Model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(settings.Model);
        }

        if (settings.MaxTurns is > 0)
        {
            psi.ArgumentList.Add("--max-turns");
            psi.ArgumentList.Add(settings.MaxTurns.Value.ToString());
        }
        else if (isAsk)
        {
            psi.ArgumentList.Add("--max-turns");
            psi.ArgumentList.Add("1");
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(File.ReadAllText(promptFilePath));

        return psi;
    }

    private static ProcessStartInfo BuildCursorProcessStartInfo(
        ResolvedSettings settings,
        string promptFilePath,
        string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = settings.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("agent");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(File.ReadAllText(promptFilePath));

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return psi;
    }

    private static AgentProcessResult ParseClaudeCodeOutput(
        string stdout,
        long durationMs,
        int exitCode)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return AgentProcessResult.Error("Empty output from Claude Code CLI", durationMs, exitCode);
        }

        try
        {
            var jsonStart = stdout.IndexOf('{');
            var jsonEnd = stdout.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                return new AgentProcessResult(stdout.Trim(), false, durationMs, exitCode, null);
            }

            var jsonText = stdout[jsonStart..(jsonEnd + 1)];
            var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var resultText = root.TryGetProperty("result", out var resultProp)
                ? resultProp.GetString() ?? ""
                : stdout.Trim();

            var isError = root.TryGetProperty("is_error", out var isErrorProp)
                          && isErrorProp.ValueKind == JsonValueKind.True;

            var metadata = new JsonObject
            {
                ["source"] = "claude_code_cli"
            };

            if (root.TryGetProperty("cost_usd", out var costProp))
                metadata["costUsd"] = costProp.GetDouble();
            if (root.TryGetProperty("duration_ms", out var durProp))
                metadata["cliDurationMs"] = durProp.GetInt64();
            if (root.TryGetProperty("duration_api_ms", out var apiDurProp))
                metadata["apiDurationMs"] = apiDurProp.GetInt64();
            if (root.TryGetProperty("num_turns", out var turnsProp))
                metadata["numTurns"] = turnsProp.GetInt32();
            if (root.TryGetProperty("session_id", out var sessionProp))
                metadata["sessionId"] = sessionProp.GetString();

            return new AgentProcessResult(resultText, isError, durationMs, exitCode, metadata);
        }
        catch (JsonException)
        {
            return new AgentProcessResult(stdout.Trim(), false, durationMs, exitCode, null);
        }
    }

    private static AgentProcessResult ParseRawOutput(string stdout, long durationMs, int exitCode)
    {
        return new AgentProcessResult(
            stdout.Trim(),
            exitCode != 0,
            durationMs,
            exitCode,
            new JsonObject { ["source"] = "cursor_cli" });
    }

    private ResolvedSettings ResolveSettings(string profileName)
    {
        var profileSettings = _options.Profiles.TryGetValue(profileName, out var profile)
            ? profile.Settings
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var agentType = GetSetting(profileSettings, "agentType", "claude_code").ToLowerInvariant();
        var defaultExecutable = agentType == "cursor" ? "cursor" : "claude";
        var executable = GetSetting(profileSettings, "executable", defaultExecutable);
        var workDir = GetSetting(profileSettings, "workingDirectory", null);
        var timeout = int.TryParse(
            GetSetting(profileSettings, "timeoutSeconds", null), out var t) && t > 0
            ? t
            : DefaultTimeoutSeconds;
        var maxTurns = int.TryParse(
            GetSetting(profileSettings, "maxTurns", null), out var mt) && mt > 0
            ? (int?)mt
            : null;
        var model = GetSetting(profileSettings, "model", null);

        return new ResolvedSettings(agentType, executable, workDir, timeout, maxTurns, model);
    }

    private static string? ResolveWorkingDirectory(ResolvedSettings settings, JsonObject input)
    {
        if (!string.IsNullOrWhiteSpace(settings.WorkingDirectory))
            return settings.WorkingDirectory;

        if (input.TryGetPropertyValue("workspace", out var wsNode) &&
            wsNode is JsonObject workspace)
        {
            if (workspace.TryGetPropertyValue("taskDirectory", out var tdNode) &&
                tdNode is JsonValue tdValue && tdValue.TryGetValue<string>(out var taskDir) &&
                !string.IsNullOrWhiteSpace(taskDir))
            {
                return taskDir;
            }

            if (workspace.TryGetPropertyValue("runDirectory", out var rdNode) &&
                rdNode is JsonValue rdValue && rdValue.TryGetValue<string>(out var runDir) &&
                !string.IsNullOrWhiteSpace(runDir))
            {
                return runDir;
            }
        }

        return null;
    }

    private static JsonObject BuildMetadata(
        string profileName,
        ResolvedSettings settings,
        AgentProcessResult processResult)
    {
        var metadata = processResult.CliMetadata?.DeepClone() as JsonObject ?? new JsonObject();
        metadata["adapter"] = "local";
        metadata["profile"] = profileName;
        metadata["agentType"] = settings.AgentType;
        metadata["executable"] = settings.Executable;
        metadata["durationMs"] = processResult.DurationMs;
        metadata["exitCode"] = processResult.ExitCode;
        return metadata;
    }

    private static string GetSetting(
        Dictionary<string, string> settings,
        string key,
        string? defaultValue)
    {
        return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue ?? string.Empty;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "... [truncated]";
    }

    private void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill agent process");
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp prompt file: {Path}", path);
        }
    }

    private sealed record ResolvedSettings(
        string AgentType,
        string Executable,
        string? WorkingDirectory,
        int TimeoutSeconds,
        int? MaxTurns,
        string? Model);

    private sealed record AgentProcessResult(
        string ResultText,
        bool IsError,
        long DurationMs,
        int ExitCode,
        JsonObject? CliMetadata)
    {
        public static AgentProcessResult Error(string message, long durationMs, int exitCode = -1)
        {
            return new AgentProcessResult(message, true, durationMs, exitCode, null);
        }
    }
}
