import React from "react";
import type { MutableRefObject } from "react";

/**
 * Что: центральная панель canvas с Drawflow host.
 * Зачем: держать canvas-разметку отдельно от App-контейнера.
 * Как: получает ref контейнера и флаг empty-state.
 */
interface CanvasPanelProps {
  editorContainerRef: MutableRefObject<HTMLDivElement | null>;
  isCanvasEmpty: boolean;
}

export function CanvasPanel({ editorContainerRef, isCanvasEmpty }: CanvasPanelProps) {
  return (
    <section className="canvas-panel" aria-label="Workflow canvas">
      <div ref={editorContainerRef} className="drawflow-host" />
      {isCanvasEmpty && <div className="empty-state">Add nodes from the left panel and connect them on the canvas.</div>}
    </section>
  );
}
