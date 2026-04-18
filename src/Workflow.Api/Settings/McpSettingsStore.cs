using System.Text.Json;
using Workflow.Engine.Runtime.Mcp;

namespace Workflow.Api.Settings;

/// <summary>
/// Что: file-backed store для локального `mcp.json`.
/// Зачем: MCP endpoints/secrets должны настраиваться отдельно от workflow graph и не попадать в git/export.
/// Как: читает `mcp.json`, мержит его с appsettings fallback и отдает effective snapshot runtime catalog-у.
/// </summary>
public sealed class McpSettingsStore : IMcpToolProfileSource
{
    public const string RedactedSecretValue = ":redacted";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _configPath;
    private readonly McpToolInvokerOptions _fallbackOptions;
    private readonly ILogger<McpSettingsStore> _logger;

    public McpSettingsStore(
        string configPath,
        McpToolInvokerOptions fallbackOptions,
        ILogger<McpSettingsStore> logger)
    {
        _configPath = configPath;
        _fallbackOptions = fallbackOptions;
        _logger = logger;
    }

    public string ConfigPath => _configPath;

    public McpToolInvokerOptions GetSnapshot()
    {
        var fileOptions = TryReadFileOptions(logErrors: true);
        return MergeOptions(_fallbackOptions, fileOptions);
    }

    public Task<McpSettingsReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exists = File.Exists(_configPath);
        var fileOptions = TryReadFileOptions(logErrors: false);
        var effectiveOptions = MergeOptions(_fallbackOptions, fileOptions);
        return Task.FromResult(new McpSettingsReadResult(
            _configPath,
            exists,
            CloneOptions(effectiveOptions, redactSecrets: true)));
    }

    public async Task<McpSettingsReadResult> SaveAsync(
        McpToolInvokerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var existingFileOptions = TryReadFileOptions(logErrors: false);
        var normalized = NormalizeOptions(options, existingFileOptions ?? _fallbackOptions);
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        var temporaryPath = $"{_configPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _configPath, overwrite: true);

        _logger.LogInformation(
            "MCP settings saved to {ConfigPath}; profiles {ProfileCount}.",
            _configPath,
            normalized.Profiles.Count);

        return new McpSettingsReadResult(
            _configPath,
            Exists: true,
            CloneOptions(MergeOptions(_fallbackOptions, normalized), redactSecrets: true));
    }

    private McpToolInvokerOptions? TryReadFileOptions(bool logErrors)
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<McpToolInvokerOptions>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            if (logErrors)
            {
                _logger.LogWarning(
                    exception,
                    "MCP settings file {ConfigPath} cannot be read; falling back to appsettings.",
                    _configPath);
            }

            return null;
        }
    }

    private static McpToolInvokerOptions MergeOptions(
        McpToolInvokerOptions fallbackOptions,
        McpToolInvokerOptions? fileOptions)
    {
        if (fileOptions is null)
        {
            return CloneOptions(fallbackOptions, redactSecrets: false);
        }

        var profiles = new Dictionary<string, McpServerProfileOptions>(
            fallbackOptions.Profiles,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (profileName, profile) in fileOptions.Profiles)
        {
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                profiles[profileName.Trim()] = CloneProfile(profile, redactSecrets: false);
            }
        }

        return new McpToolInvokerOptions
        {
            DefaultProfile = string.IsNullOrWhiteSpace(fileOptions.DefaultProfile)
                ? fallbackOptions.DefaultProfile
                : fileOptions.DefaultProfile.Trim(),
            ConfigPath = fallbackOptions.ConfigPath,
            Profiles = profiles
        };
    }

    private static McpToolInvokerOptions NormalizeOptions(
        McpToolInvokerOptions options,
        McpToolInvokerOptions secretSource)
    {
        var profiles = new Dictionary<string, McpServerProfileOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var (profileName, profile) in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                continue;
            }

            var normalizedName = profileName.Trim();
            secretSource.Profiles.TryGetValue(normalizedName, out var existingProfile);
            profiles[normalizedName] = CloneProfile(profile, redactSecrets: false, existingProfile);
        }

        if (profiles.Count == 0)
        {
            profiles["mock"] = new McpServerProfileOptions
            {
                Type = "mock",
                TimeoutSeconds = 30
            };
        }

        var defaultProfile = string.IsNullOrWhiteSpace(options.DefaultProfile)
            ? profiles.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).First()
            : options.DefaultProfile.Trim();

        return new McpToolInvokerOptions
        {
            DefaultProfile = defaultProfile,
            Profiles = profiles
        };
    }

    private static McpToolInvokerOptions CloneOptions(McpToolInvokerOptions source, bool redactSecrets)
    {
        return new McpToolInvokerOptions
        {
            DefaultProfile = source.DefaultProfile,
            ConfigPath = null,
            Profiles = source.Profiles.ToDictionary(
                pair => pair.Key,
                pair => CloneProfile(pair.Value, redactSecrets),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static McpServerProfileOptions CloneProfile(
        McpServerProfileOptions source,
        bool redactSecrets,
        McpServerProfileOptions? existingProfile = null)
    {
        var bearerToken = source.BearerToken;
        if (bearerToken == RedactedSecretValue)
        {
            bearerToken = existingProfile?.BearerToken;
        }

        return new McpServerProfileOptions
        {
            Enabled = source.Enabled,
            Type = source.Type,
            Transport = source.Transport,
            Endpoint = source.Endpoint,
            BearerToken = redactSecrets && !string.IsNullOrWhiteSpace(bearerToken)
                ? RedactedSecretValue
                : bearerToken,
            BearerTokenEnvironmentVariable = source.BearerTokenEnvironmentVariable,
            Headers = source.Headers is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(source.Headers, StringComparer.OrdinalIgnoreCase),
            AllowedTools = source.AllowedTools?.ToArray() ?? Array.Empty<string>(),
            BlockedTools = source.BlockedTools?.ToArray() ?? Array.Empty<string>(),
            TimeoutSeconds = source.TimeoutSeconds > 0 ? source.TimeoutSeconds : 30
        };
    }
}

public sealed record McpSettingsReadResult(
    string ConfigPath,
    bool Exists,
    McpToolInvokerOptions Settings);
