namespace Workflow.Engine.Runtime.Artifacts;

/// <summary>
/// Что: метаданные artifact, созданного node executor-ом во время run.
/// Зачем: передавать между нодами компактный `artifact_ref`, не гоняя полный файл в payload.
/// Как: descriptor содержит id, run/node ownership, тип, размер, checksum и workspace uri.
/// </summary>
public sealed record WorkflowArtifactDescriptor(
    string ArtifactId,
    string RunId,
    string NodeId,
    string Name,
    string ArtifactType,
    string MediaType,
    string RelativePath,
    string Uri,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Что: запрос на запись artifact в workspace.
/// Зачем: дать executor-ам простой контракт сохранения JSON/MD/text результатов.
/// Как: store получает content строкой, пишет файл и возвращает descriptor; для pipeline workspace можно задать stable relative directory/name.
/// </summary>
public sealed class WorkflowArtifactWriteRequest
{
    public required string RunId { get; init; }

    public required string NodeId { get; init; }

    public required string Name { get; init; }

    public required string ArtifactType { get; init; }

    public required string MediaType { get; init; }

    public required string Content { get; init; }

    public string? WorkspaceRelativeDirectory { get; init; }

    public bool UseStableFileName { get; init; } = false;
}

/// <summary>
/// Что: содержимое artifact для API/debug сценариев.
/// Зачем: HAWF-034 требует доступность metadata/API, а content-read нужен для smoke-check и будущего UI browser.
/// Как: объединяет descriptor и текст файла.
/// </summary>
public sealed record WorkflowArtifactContent(
    WorkflowArtifactDescriptor Descriptor,
    string Content);
