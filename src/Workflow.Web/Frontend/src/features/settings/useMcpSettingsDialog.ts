import { useCallback, useState } from "react";
import { getErrorMessage } from "../../shared/lib/errorMessage";
import type {
  McpSettingsDocument,
  TestMcpProfileResponse,
  WorkflowApiClient
} from "../../shared/types/workflow";

/**
 * Что: orchestration состояния MCP Settings dialog.
 * Зачем: App/Toolbar не должны знать детали загрузки, сохранения и test/list-tools smoke.
 * Как: хранит JSON editor text, выбранный profile и вызывает backend settings API.
 */
export function useMcpSettingsDialog(apiClient: WorkflowApiClient) {
  const [isOpen, setIsOpen] = useState(false);
  const [isBusy, setIsBusy] = useState(false);
  const [configPath, setConfigPath] = useState("");
  const [exists, setExists] = useState(false);
  const [settingsText, setSettingsText] = useState("");
  const [selectedProfile, setSelectedProfile] = useState("mock");
  const [error, setError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<TestMcpProfileResponse | null>(null);

  const open = useCallback(async () => {
    setIsOpen(true);
    setIsBusy(true);
    setError(null);
    setTestResult(null);

    try {
      const response = await apiClient.getMcpSettings();
      setConfigPath(response.configPath);
      setExists(response.exists);
      setSettingsText(JSON.stringify(response.settings, null, 2));
      setSelectedProfile(response.settings.defaultProfile || "mock");
    } catch (loadError) {
      setError(getErrorMessage(loadError, "Failed to load MCP settings"));
    } finally {
      setIsBusy(false);
    }
  }, [apiClient]);

  const close = useCallback(() => {
    setIsOpen(false);
  }, []);

  const save = useCallback(async () => {
    const parsedSettings = parseSettings(settingsText);
    if (!parsedSettings.ok) {
      setError(parsedSettings.error);
      return;
    }

    setIsBusy(true);
    setError(null);
    setTestResult(null);
    try {
      const response = await apiClient.saveMcpSettings(parsedSettings.settings);
      setConfigPath(response.configPath);
      setExists(response.exists);
      setSettingsText(JSON.stringify(response.settings, null, 2));
      setSelectedProfile(response.settings.defaultProfile || selectedProfile || "mock");
    } catch (saveError) {
      setError(getErrorMessage(saveError, "Failed to save MCP settings"));
    } finally {
      setIsBusy(false);
    }
  }, [apiClient, selectedProfile, settingsText]);

  const test = useCallback(async () => {
    const profile = selectedProfile.trim();
    if (!profile) {
      setError("Server profile is required.");
      return;
    }

    setIsBusy(true);
    setError(null);
    setTestResult(null);
    try {
      const response = await apiClient.testMcpProfile({
        serverProfile: profile,
        timeoutSeconds: 30
      });
      setTestResult(response);
    } catch (testError) {
      setError(getErrorMessage(testError, "MCP profile test failed"));
    } finally {
      setIsBusy(false);
    }
  }, [apiClient, selectedProfile]);

  return {
    isOpen,
    isBusy,
    configPath,
    exists,
    settingsText,
    selectedProfile,
    error,
    testResult,
    open,
    close,
    save,
    test,
    setSettingsText,
    setSelectedProfile
  };
}

type ParseSettingsResult =
  | { ok: true; settings: McpSettingsDocument }
  | { ok: false; error: string };

function parseSettings(settingsText: string): ParseSettingsResult {
  try {
    const parsed = JSON.parse(settingsText);
    if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") {
      return { ok: false, error: "MCP settings must be a JSON object." };
    }

    if (!("profiles" in parsed) || !parsed.profiles || typeof parsed.profiles !== "object") {
      return { ok: false, error: "MCP settings must include a profiles object." };
    }

    return {
      ok: true,
      settings: parsed as McpSettingsDocument
    };
  } catch (error) {
    return {
      ok: false,
      error: getErrorMessage(error, "MCP settings JSON is invalid")
    };
  }
}
