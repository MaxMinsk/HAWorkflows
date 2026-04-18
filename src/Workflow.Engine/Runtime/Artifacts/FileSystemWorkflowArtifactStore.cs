using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Workflow.Engine.Runtime.Artifacts;

/// <summary>
/// Что: filesystem-реализация artifact store.
/// Зачем: локальный workflow должен иметь воспроизводимый workspace без отдельной БД/облака.
/// Как: по умолчанию пишет файлы в `workspace/runs/<runId>/artifacts/`; pipeline-ноды могут задать stable workspace directory.
/// </summary>
public sealed class FileSystemWorkflowArtifactStore : IWorkflowArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _workspacePath;
    private readonly object _syncRoot = new();

    public FileSystemWorkflowArtifactStore(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path must be a non-empty string.", nameof(workspacePath));
        }

        _workspacePath = Path.GetFullPath(workspacePath);
        Directory.CreateDirectory(_workspacePath);
    }

    public WorkflowArtifactDescriptor WriteArtifact(WorkflowArtifactWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runId = SanitizeSegment(request.RunId, "run");
        var nodeId = SanitizeSegment(request.NodeId, "node");
        var name = SanitizeFileName(request.Name, "artifact.txt");
        var content = request.Content ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(content);
        var artifactId = Guid.NewGuid().ToString("N");
        var artifactDirectory = ResolveArtifactDirectory(runId, request.WorkspaceRelativeDirectory);
        var fileName = request.UseStableFileName ? name : $"{artifactId}-{name}";
        var absolutePath = Path.Combine(artifactDirectory, fileName);
        var relativePath = ToRelativeWorkspacePath(absolutePath);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var uri = string.IsNullOrWhiteSpace(request.WorkspaceRelativeDirectory)
            ? $"workspace://runs/{runId}/artifacts/{artifactId}"
            : $"workspace://{relativePath}";

        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllBytes(absolutePath, bytes);

        var descriptor = new WorkflowArtifactDescriptor(
            ArtifactId: artifactId,
            RunId: runId,
            NodeId: nodeId,
            Name: name,
            ArtifactType: string.IsNullOrWhiteSpace(request.ArtifactType) ? "file" : request.ArtifactType.Trim(),
            MediaType: string.IsNullOrWhiteSpace(request.MediaType) ? "application/octet-stream" : request.MediaType.Trim(),
            RelativePath: relativePath,
            Uri: uri,
            SizeBytes: bytes.LongLength,
            Sha256: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            CreatedAtUtc: createdAtUtc);

        lock (_syncRoot)
        {
            var descriptors = ReadMetadata(runId).ToList();
            descriptors.Add(descriptor);
            WriteMetadata(runId, descriptors);
        }

        return descriptor;
    }

    public IReadOnlyList<WorkflowArtifactDescriptor> ListRunArtifacts(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Array.Empty<WorkflowArtifactDescriptor>();
        }

        lock (_syncRoot)
        {
            return ReadMetadata(SanitizeSegment(runId, "run"));
        }
    }

    public WorkflowArtifactContent? TryReadArtifact(string runId, string artifactId)
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(artifactId))
        {
            return null;
        }

        var sanitizedRunId = SanitizeSegment(runId, "run");
        var sanitizedArtifactId = SanitizeSegment(artifactId, "artifact");
        WorkflowArtifactDescriptor? descriptor;
        lock (_syncRoot)
        {
            descriptor = ReadMetadata(sanitizedRunId)
                .FirstOrDefault(item => string.Equals(item.ArtifactId, sanitizedArtifactId, StringComparison.Ordinal));
        }

        if (descriptor is null)
        {
            return null;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(_workspacePath, descriptor.RelativePath));
        var relativeToWorkspace = Path.GetRelativePath(_workspacePath, absolutePath);
        if (relativeToWorkspace.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToWorkspace) || !File.Exists(absolutePath))
        {
            return null;
        }

        return new WorkflowArtifactContent(descriptor, File.ReadAllText(absolutePath, Encoding.UTF8));
    }

    private IReadOnlyList<WorkflowArtifactDescriptor> ReadMetadata(string runId)
    {
        var metadataPath = GetMetadataPath(runId);
        if (!File.Exists(metadataPath))
        {
            return Array.Empty<WorkflowArtifactDescriptor>();
        }

        try
        {
            var json = File.ReadAllText(metadataPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<IReadOnlyList<WorkflowArtifactDescriptor>>(json, JsonOptions)
                   ?? Array.Empty<WorkflowArtifactDescriptor>();
        }
        catch
        {
            return Array.Empty<WorkflowArtifactDescriptor>();
        }
    }

    private void WriteMetadata(string runId, IReadOnlyList<WorkflowArtifactDescriptor> descriptors)
    {
        var metadataPath = GetMetadataPath(runId);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(descriptors, JsonOptions), Encoding.UTF8);
    }

    private string GetRunArtifactsDirectory(string runId)
    {
        return Path.Combine(_workspacePath, "runs", runId, "artifacts");
    }

    private string ResolveArtifactDirectory(string runId, string? workspaceRelativeDirectory)
    {
        if (string.IsNullOrWhiteSpace(workspaceRelativeDirectory))
        {
            return GetRunArtifactsDirectory(runId);
        }

        return Path.Combine(_workspacePath, SanitizeRelativePath(workspaceRelativeDirectory));
    }

    private string GetMetadataPath(string runId)
    {
        return Path.Combine(GetRunArtifactsDirectory(runId), "artifacts.json");
    }

    private string ToRelativeWorkspacePath(string absolutePath)
    {
        return Path
            .GetRelativePath(_workspacePath, absolutePath)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string SanitizeSegment(string value, string fallback)
    {
        var sanitized = SanitizeFileName(value, fallback);
        return sanitized.Replace('.', '_');
    }

    private static string SanitizeRelativePath(string value)
    {
        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => SanitizeSegment(segment, "segment"))
            .Where(segment => segment.Length > 0)
            .ToArray();

        return segments.Length == 0
            ? "artifacts"
            : Path.Combine(segments);
    }

    private static string SanitizeFileName(string value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
