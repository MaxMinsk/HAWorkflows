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
    outputPorts: [{
      id: "output_1",
      label: "Data",
      channel: "data",
      description: "Initial run payload passed to the workflow.",
      producesKinds: ["run_input", "workflow_data"]
    }]
  },
  transform: {
    inputs: 1,
    outputs: 1,
    label: "Transform",
    description: "Deterministic mapping",
    pack: "core",
    source: "built_in",
    inputPorts: [{
      id: "input_1",
      label: "Data",
      channel: "data",
      required: true,
      acceptedKinds: ["workflow_data"],
      description: "Payload to mutate with deterministic config."
    }],
    outputPorts: [{
      id: "output_1",
      label: "Data",
      channel: "data",
      description: "Transformed workflow payload.",
      producesKinds: ["workflow_data"]
    }]
  },
  log: {
    inputs: 1,
    outputs: 1,
    label: "Log",
    description: "Write execution log",
    pack: "core",
    source: "built_in",
    inputPorts: [{
      id: "input_1",
      label: "Data",
      channel: "data",
      required: true,
      acceptedKinds: ["workflow_data"],
      description: "Payload that should pass through while this node writes a timeline log entry."
    }],
    outputPorts: [{
      id: "output_1",
      label: "Data",
      channel: "data",
      description: "Same payload after writing a log entry.",
      producesKinds: ["workflow_data"]
    }]
  },
  output: {
    inputs: 1,
    outputs: 0,
    label: "Output",
    description: "Final result",
    pack: "core",
    source: "built_in",
    inputPorts: [{
      id: "input_1",
      label: "Data",
      channel: "data",
      required: true,
      acceptedKinds: ["workflow_data", "agent_result", "evidence_pack"],
      description: "Final payload captured as workflow run output."
    }],
    outputPorts: []
  }
};
