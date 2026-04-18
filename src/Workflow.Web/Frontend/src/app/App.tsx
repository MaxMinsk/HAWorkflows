import React from "react";
import "drawflow/dist/drawflow.min.css";
import "./App.css";
import { Toolbar } from "./components/Toolbar";
import { PalettePanel } from "./components/PalettePanel";
import { CanvasPanel } from "./components/CanvasPanel";
import { InspectorPanel } from "./components/InspectorPanel";
import { useWorkflowBuilder } from "../features/workflow/hooks/useWorkflowBuilder";
import { McpSettingsDialog } from "../features/settings/McpSettingsDialog";

/**
 * Что: корневая композиция React-приложения.
 * Зачем: держать App максимально "тонким" и читаемым.
 * Как: собирает layout из компонентов, а stateful orchestration берет из custom hook.
 */
export function App() {
  const {
    status,
    statusDotColor,
    toast,
    isCanvasEmpty,
    workflowName,
    currentWorkflowId,
    storedWorkflows,
    inspector,
    inspectorEnabled,
    connections,
    validationErrors,
    runData,
    nodeTypes,
    nodeTemplates,
    mcpSettings,
    editorContainerRef,
    setWorkflowName,
    updateInspectorField,
    disconnectConnection,
    removeSelectedNode,
    onUpdateNode,
    onLoad,
    onSave,
    onRun,
    onStop,
    onRefreshStored,
    onOpenStoredWorkflow,
    addNode,
    getConnectionKey
  } = useWorkflowBuilder();

  return (
    <div className="app-root">
      <Toolbar
        statusText={status.text}
        statusDotColor={statusDotColor}
        onLoad={onLoad}
        onSave={onSave}
        onRun={onRun}
        onStop={onStop}
        onOpenSettings={mcpSettings.open}
      />

      <main className="layout">
        <PalettePanel
          workflowName={workflowName}
          currentWorkflowId={currentWorkflowId}
          storedWorkflows={storedWorkflows}
          nodeTypes={nodeTypes}
          nodeTemplates={nodeTemplates}
          onWorkflowNameChange={setWorkflowName}
          onAddNode={addNode}
          onRefreshStored={onRefreshStored}
          onOpenStoredWorkflow={onOpenStoredWorkflow}
        />

        <CanvasPanel editorContainerRef={editorContainerRef} isCanvasEmpty={isCanvasEmpty} />

        <InspectorPanel
          inspector={inspector}
          inspectorEnabled={inspectorEnabled}
          nodeTemplates={nodeTemplates}
          connections={connections}
          validationErrors={validationErrors}
          runData={runData}
          getConnectionKey={getConnectionKey}
          onInspectorFieldChange={updateInspectorField}
          onUpdateNode={onUpdateNode}
          onDeleteNode={removeSelectedNode}
          onDisconnectConnection={disconnectConnection}
        />
      </main>

      <div className={`toast ${toast.visible ? "visible" : ""}`} role="status" aria-live="polite">
        {toast.text}
      </div>

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
