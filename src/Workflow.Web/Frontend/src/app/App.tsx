import React, { useCallback, useEffect, useMemo, useState } from "react";
import "drawflow/dist/drawflow.min.css";
import "./App.css";
import { Toolbar } from "./components/Toolbar";
import { PalettePanel } from "./components/PalettePanel";
import { CanvasPanel } from "./components/CanvasPanel";
import { InspectorPanel } from "./components/InspectorPanel";
import { RunDetailsPanel } from "../features/runs/RunDetailsPanel";
import { useWorkflowBuilder } from "../features/workflow/hooks/useWorkflowBuilder";
import { McpSettingsDialog } from "../features/settings/McpSettingsDialog";
import { useMcpSettingsDialog } from "../features/settings/useMcpSettingsDialog";
import { createWorkflowApiClient } from "../shared/api/workflowApiClient";
import { applyNodeStatusOverlays, clearNodeStatusOverlays } from "../features/workflow/lib/drawflowGraphAdapter";

/**
 * Что: корневая композиция React-приложения.
 * Зачем: держать App максимально "тонким" и читаемым.
 * Как: собирает layout из компонентов, а stateful orchestration берет из custom hook.
 *      apiClient и mcpSettings создаются здесь, чтобы features не импортировали друг друга напрямую.
 */
export function App() {
  const apiClient = useMemo(() => createWorkflowApiClient(window.localStorage), []);
  const mcpSettings = useMcpSettingsDialog(apiClient);
  const [isRunDetailsOpen, setIsRunDetailsOpen] = useState(false);
  const openRunDetails = useCallback(() => setIsRunDetailsOpen(true), []);
  const closeRunDetails = useCallback(() => setIsRunDetailsOpen(false), []);

  const {
    status,
    statusDotColor,
    toast,
    isCanvasEmpty,
    workflowName,
    currentWorkflowId,
    currentWorkflowVersion,
    currentPublishedVersion,
    storedWorkflows,
    inspector,
    inspectorEnabled,
    connections,
    validationErrors,
    runData,
    nodeTypes,
    nodeTemplates,
    editorContainerRef,
    setWorkflowName,
    updateInspectorField,
    disconnectConnection,
    removeSelectedNode,
    onUpdateNode,
    onLoad,
    onSave,
    onPublish,
    onExportProfile,
    onImportProfileFile,
    onRun,
    onResumeRun,
    onStop,
    onRefreshStored,
    onOpenStoredWorkflow,
    connectionAssistantSuggestions,
    addNode,
    addSuggestedNode,
    getConnectionKey
  } = useWorkflowBuilder({ apiClient, mcpSettings });

  useEffect(() => {
    const container = editorContainerRef.current;
    if (!container) return;

    if (runData.nodes.length > 0) {
      applyNodeStatusOverlays(
        container,
        runData.nodes.map((n) => ({ nodeId: n.nodeId, status: n.status ?? "Pending" }))
      );
    } else {
      clearNodeStatusOverlays(container);
    }
  }, [runData.nodes, editorContainerRef]);

  return (
    <div className="app-root">
      <Toolbar
        statusText={status.text}
        statusDotColor={statusDotColor}
        onLoad={onLoad}
        onSave={onSave}
        onPublish={onPublish}
        onRun={onRun}
        onStop={onStop}
        onOpenSettings={mcpSettings.open}
      />

      <main className="layout">
        <PalettePanel
          workflowName={workflowName}
          currentWorkflowId={currentWorkflowId}
          currentWorkflowVersion={currentWorkflowVersion}
          currentPublishedVersion={currentPublishedVersion}
          storedWorkflows={storedWorkflows}
          nodeTypes={nodeTypes}
          nodeTemplates={nodeTemplates}
          onWorkflowNameChange={setWorkflowName}
          onAddNode={addNode}
          onRefreshStored={onRefreshStored}
          onOpenStoredWorkflow={onOpenStoredWorkflow}
          onExportProfile={onExportProfile}
          onImportProfileFile={onImportProfileFile}
        />

        <CanvasPanel editorContainerRef={editorContainerRef} isCanvasEmpty={isCanvasEmpty} />

        <InspectorPanel
          inspector={inspector}
          inspectorEnabled={inspectorEnabled}
          nodeTemplates={nodeTemplates}
          connections={connections}
          connectionAssistantSuggestions={connectionAssistantSuggestions}
          validationErrors={validationErrors}
          runData={runData}
          getConnectionKey={getConnectionKey}
          onInspectorFieldChange={updateInspectorField}
          onUpdateNode={onUpdateNode}
          onDeleteNode={removeSelectedNode}
          onDisconnectConnection={disconnectConnection}
          onAddSuggestedNode={addSuggestedNode}
          onOpenRunDetails={openRunDetails}
        />
      </main>

      <div className={`toast ${toast.visible ? "visible" : ""}`} role="status" aria-live="polite">
        {toast.text}
      </div>

      {isRunDetailsOpen && (
        <RunDetailsPanel
          runData={runData}
          onResumeRun={onResumeRun}
          onClose={closeRunDetails}
        />
      )}

      <McpSettingsDialog
        isOpen={mcpSettings.isOpen}
        isBusy={mcpSettings.isBusy}
        configPath={mcpSettings.configPath}
        exists={mcpSettings.exists}
        settingsText={mcpSettings.settingsText}
        selectedProfile={mcpSettings.selectedProfile}
        error={mcpSettings.error}
        testResult={mcpSettings.testResult}
        onSettingsTextChange={mcpSettings.setSettingsText}
        onSelectedProfileChange={mcpSettings.setSelectedProfile}
        onSave={mcpSettings.save}
        onTest={mcpSettings.test}
        onClose={mcpSettings.close}
      />
    </div>
  );
}
