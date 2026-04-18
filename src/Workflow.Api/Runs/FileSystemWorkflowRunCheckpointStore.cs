using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workflow.Api.Runs;

/// <summary>
/// Что: файловая реализация checkpoint store.
/// Зачем: локальный workflow должен переживать restart без внешней БД и сложной инфраструктуры.
/// Как: пишет latest checkpoint в `{root}/{runId}/checkpoint.json` через temp-file replace.
/// </summary>
public sealed class FileSystemWorkflowRunCheckpointStore : IWorkflowRunCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _rootPath;
    private readonly ILogger<FileSystemWorkflowRunCheckpointStore> _logger;

    public FileSystemWorkflowRunCheckpointStore(
        string rootPath,
        ILogger<FileSystemWorkflowRunCheckpointStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
        _logger = logger;
    }

    public async Task SaveAsync(StoredWorkflowRunCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var runDirectory = GetRunDirectory(checkpoint.RunId);
        Directory.CreateDirectory(runDirectory);

        var targetPath = GetCheckpointPath(checkpoint.RunId);
        var tempPath = Path.Combine(runDirectory, $"checkpoint.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(checkpoint, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, targetPath, overwrite: true);
    }

    public async Task<StoredWorkflowRunCheckpoint?> TryReadLatestAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var checkpointPath = GetCheckpointPath(runId.Trim());
        if (!File.Exists(checkpointPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(checkpointPath);
            return await JsonSerializer.DeserializeAsync<StoredWorkflowRunCheckpoint>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Workflow run checkpoint read failed for run {RunId}.",
                runId);
            return null;
        }
    }

    public async Task<IReadOnlyList<StoredWorkflowRunCheckpoint>> ListLatestAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootPath))
        {
            return Array.Empty<StoredWorkflowRunCheckpoint>();
        }

        var checkpoints = new List<StoredWorkflowRunCheckpoint>();
        foreach (var checkpointPath in Directory.EnumerateFiles(_rootPath, "checkpoint.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(checkpointPath);
                var checkpoint = await JsonSerializer.DeserializeAsync<StoredWorkflowRunCheckpoint>(
                        stream,
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint is not null)
                {
                    checkpoints.Add(checkpoint);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    exception,
                    "Workflow run checkpoint read failed for path {CheckpointPath}.",
                    checkpointPath);
            }
        }

        return checkpoints
            .OrderByDescending(checkpoint => checkpoint.RuntimeCheckpoint.CheckpointedAtUtc)
            .ToArray();
    }

    private string GetRunDirectory(string runId)
    {
        return Path.Combine(_rootPath, runId);
    }

    private string GetCheckpointPath(string runId)
    {
        return Path.Combine(GetRunDirectory(runId), "checkpoint.json");
    }
}
