import { useEffect, type MutableRefObject } from "react";
import type Drawflow from "drawflow";
import type { ClipboardNode } from "../../../shared/types/workflow";

/**
 * Что: hotkeys для canvas-редактора.
 * Зачем: централизовать keyboard UX (save/copy/paste/delete).
 * Как: подписка на window keydown, guarded по focus в input/textarea.
 */
interface UseDrawflowKeyboardShortcutsProps {
  editorRef: MutableRefObject<Drawflow | null>;
  editorContainerRef: MutableRefObject<HTMLDivElement | null>;
  selectedNodeIdRef: MutableRefObject<number | null>;
  clipboardNodeRef: MutableRefObject<ClipboardNode | null>;
  onSaveRequestedRef: MutableRefObject<(() => Promise<void>) | (() => void)>;
  onToast: (message: string) => void;
  addNode: (type: string, x?: number, y?: number) => void;
  removeNode: (nodeId: number | null) => void;
  syncInspector: (nodeId: number | null) => void;
  syncNodeMarkup: (nodeId: number, name: string) => void;
}

export function useDrawflowKeyboardShortcuts({
  editorRef,
  editorContainerRef,
  selectedNodeIdRef,
  clipboardNodeRef,
  onSaveRequestedRef,
  onToast,
  addNode,
  removeNode,
  syncInspector,
  syncNodeMarkup
}: UseDrawflowKeyboardShortcutsProps) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      const activeTag = document.activeElement ? document.activeElement.tagName : "";
      const isEditingInput = activeTag === "INPUT" || activeTag === "TEXTAREA";

      if (event.key === "Delete" && !isEditingInput) {
        removeNode(selectedNodeIdRef.current);
        return;
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        onSaveRequestedRef.current?.();
        return;
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "c" && !isEditingInput) {
        if (selectedNodeIdRef.current === null || !editorRef.current) {
          return;
        }

        const node = editorRef.current.getNodeFromId(selectedNodeIdRef.current);
        if (!node) {
          return;
        }

        clipboardNodeRef.current = {
          type: node.data?.type ?? node.name ?? "transform",
          name: node.data?.name ?? "Node",
          config: node.data?.config ?? {}
        };
        onToast("Node copied");
        return;
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "v" && !isEditingInput) {
        if (!clipboardNodeRef.current) {
          return;
        }

        const rect = editorContainerRef.current?.getBoundingClientRect();
        addNode(
          clipboardNodeRef.current.type,
          Math.round((rect?.width ?? 640) / 2 + 40),
          Math.round((rect?.height ?? 380) / 2 + 40)
        );

        if (selectedNodeIdRef.current !== null && editorRef.current) {
          const node = editorRef.current.getNodeFromId(selectedNodeIdRef.current);
          if (node) {
            const data = {
              ...node.data,
              name: `${clipboardNodeRef.current.name} Copy`,
              config: clipboardNodeRef.current.config
            };
            editorRef.current.updateNodeDataFromId(selectedNodeIdRef.current, data);
            syncNodeMarkup(selectedNodeIdRef.current, data.name);
            syncInspector(selectedNodeIdRef.current);
          }
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [
    addNode,
    clipboardNodeRef,
    editorContainerRef,
    editorRef,
    onSaveRequestedRef,
    onToast,
    removeNode,
    selectedNodeIdRef,
    syncInspector,
    syncNodeMarkup
  ]);
}
