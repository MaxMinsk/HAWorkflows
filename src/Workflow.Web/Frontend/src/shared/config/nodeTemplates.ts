import type { NodeTemplatesMap } from "../types/workflow";

/**
 * Что: конфигурация поддерживаемых типов нод.
 * Зачем: единый источник правды для редактора и валидации.
 * Как: задает порты и визуальные метаданные.
 */
export const NODE_TEMPLATES: NodeTemplatesMap = {
  input: {
    inputs: 0,
    outputs: 1,
    label: "Input",
    description: "Start signal",
    pack: "core",
    source: "built_in",
    inputPorts: [],
    outputPorts: [{ id: "output_1", label: "output 1", channel: "data" }]
  },
  transform: {
    inputs: 1,
    outputs: 1,
    label: "Transform",
    description: "Deterministic mapping",
    pack: "core",
    source: "built_in",
    inputPorts: [{ id: "input_1", label: "input 1", channel: "data" }],
    outputPorts: [{ id: "output_1", label: "output 1", channel: "data" }]
  },
  log: {
    inputs: 1,
    outputs: 1,
    label: "Log",
    description: "Write execution log",
    pack: "core",
    source: "built_in",
    inputPorts: [{ id: "input_1", label: "input 1", channel: "data" }],
    outputPorts: [{ id: "output_1", label: "output 1", channel: "data" }]
  },
  output: {
    inputs: 1,
    outputs: 0,
    label: "Output",
    description: "Final result",
    pack: "core",
    source: "built_in",
    inputPorts: [{ id: "input_1", label: "input 1", channel: "data" }],
    outputPorts: []
  }
};
