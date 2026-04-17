import type { NodeTemplatesMap } from "../types/workflow";

/**
 * Что: конфигурация поддерживаемых типов нод.
 * Зачем: единый источник правды для редактора и валидации.
 * Как: задает порты и визуальные метаданные.
 */
export const NODE_TEMPLATES: NodeTemplatesMap = {
  input: { inputs: 0, outputs: 1, label: "Input", description: "Start signal" },
  transform: { inputs: 1, outputs: 1, label: "Transform", description: "Deterministic mapping" },
  log: { inputs: 1, outputs: 1, label: "Log", description: "Write execution log" },
  output: { inputs: 1, outputs: 0, label: "Output", description: "Final result" }
};
