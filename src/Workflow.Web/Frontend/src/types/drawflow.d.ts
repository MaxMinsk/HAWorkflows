declare module "drawflow" {
  export default class Drawflow {
    constructor(container: HTMLElement);
    reroute: boolean;
    start(): void;
    on(event: string, handler: (...args: any[]) => void): void;
    export(): any;
    import(payload: any): void;
    clear(): void;
    addNode(
      name: string,
      inputs: number,
      outputs: number,
      posX: number,
      posY: number,
      className: string,
      data: any,
      html: string,
      typenode: boolean
    ): number;
    selectNodeId(nodeId: string): void;
    removeNodeId(nodeId: string): void;
    removeSingleConnection(
      outputId: number,
      inputId: number,
      outputClass: string,
      inputClass: string
    ): void;
    addConnection(
      outputId: number,
      inputId: number,
      outputClass: string,
      inputClass: string
    ): void;
    getNodeFromId(nodeId: number): any;
    updateNodeDataFromId(nodeId: number, data: any): void;
  }
}
