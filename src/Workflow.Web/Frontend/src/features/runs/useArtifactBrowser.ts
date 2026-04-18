import { useCallback, useEffect, useMemo, useState } from "react";
import { createWorkflowApiClient } from "../../shared/api/workflowApiClient";
import { getErrorMessage } from "../../shared/lib/errorMessage";
import type { WorkflowArtifactContent, WorkflowArtifactDescriptor } from "../../shared/types/workflow";

/**
 * Что: stateful-хук просмотра artifact-ов run.
 * Зачем: Run Timeline должен показывать созданные файлы без смешивания fetch/copy логики с JSX.
 * Как: по artifactId читает content через API, сбрасывает preview при смене run и копирует workspace ref в clipboard.
 */
export function useArtifactBrowser(runId: string | null | undefined) {
  const apiClient = useMemo(() => createWorkflowApiClient(window.localStorage), []);
  const [selectedArtifactId, setSelectedArtifactId] = useState<string | null>(null);
  const [selectedArtifact, setSelectedArtifact] = useState<WorkflowArtifactContent | null>(null);
  const [isLoadingArtifact, setIsLoadingArtifact] = useState(false);
  const [artifactError, setArtifactError] = useState<string | null>(null);
  const [copyStatus, setCopyStatus] = useState<string | null>(null);

  useEffect(() => {
    setSelectedArtifactId(null);
    setSelectedArtifact(null);
    setArtifactError(null);
    setCopyStatus(null);
  }, [runId]);

  const openArtifact = useCallback(async (artifact: WorkflowArtifactDescriptor) => {
    if (!runId || !artifact.artifactId) {
      return;
    }

    if (selectedArtifactId === artifact.artifactId && selectedArtifact) {
      setSelectedArtifactId(null);
      setSelectedArtifact(null);
      setArtifactError(null);
      setCopyStatus(null);
      return;
    }

    setSelectedArtifactId(artifact.artifactId);
    setSelectedArtifact(null);
    setIsLoadingArtifact(true);
    setArtifactError(null);
    setCopyStatus(null);

    try {
      const content = await apiClient.getRunArtifact(runId, artifact.artifactId);
      setSelectedArtifact(content);
    } catch (error) {
      setSelectedArtifact(null);
      setArtifactError(getErrorMessage(error, "Failed to load artifact"));
    } finally {
      setIsLoadingArtifact(false);
    }
  }, [apiClient, runId, selectedArtifact, selectedArtifactId]);

  const copyArtifactRef = useCallback(async (artifact: WorkflowArtifactDescriptor) => {
    const artifactRef = artifact.uri || artifact.artifactId;
    if (!artifactRef) {
      return;
    }

    setSelectedArtifactId(artifact.artifactId);
    setArtifactError(null);
    setCopyStatus(null);

    try {
      await navigator.clipboard.writeText(artifactRef);
      setCopyStatus("Artifact ref copied");
    } catch (error) {
      setArtifactError(getErrorMessage(error, "Failed to copy artifact ref"));
    }
  }, []);

  return {
    selectedArtifactId,
    selectedArtifact,
    isLoadingArtifact,
    artifactError,
    copyStatus,
    openArtifact,
    copyArtifactRef
  };
}
