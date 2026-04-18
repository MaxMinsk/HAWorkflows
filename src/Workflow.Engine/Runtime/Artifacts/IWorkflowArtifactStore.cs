namespace Workflow.Engine.Runtime.Artifacts;

/// <summary>
/// Что: контракт workspace-хранилища artifact-ов workflow run.
/// Зачем: runtime и node executors не должны знать, файловая это система или будущий shared storage.
/// Как: executor пишет artifact и получает descriptor; API читает metadata/content по runId/artifactId.
/// </summary>
public interface IWorkflowArtifactStore
{
    WorkflowArtifactDescriptor WriteArtifact(WorkflowArtifactWriteRequest request);

    IReadOnlyList<WorkflowArtifactDescriptor> ListRunArtifacts(string runId);

    WorkflowArtifactContent? TryReadArtifact(string runId, string artifactId);
}
