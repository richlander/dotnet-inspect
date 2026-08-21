import assert from "node:assert/strict";
import test from "node:test";
import {
  bindDependencyGraphNodes,
  bindGraphBack,
  bindGraphPanZoom,
  bindTypeGraphNodes,
} from "../src/graph-interactions.ts";

class FakeClassList {
  private readonly values = new Set<string>();

  add(...tokens: string[]) {
    for (const token of tokens) this.values.add(token);
  }

  remove(...tokens: string[]) {
    for (const token of tokens) this.values.delete(token);
  }

  contains(token: string) {
    return this.values.has(token);
  }
}

class FakeElement {
  id = "";
  readonly classList = new FakeClassList();
  readonly dataset: Record<string, string | undefined> = {};
  readonly inserted: { textContent: string | null }[] = [];
  readonly ownerDocument = {
    createElementNS: () => ({ textContent: null as string | null }),
  };
  readonly style: Record<string, string> = {};
  firstChild = null;
  hidden = false;
  tabIndex = -1;
  private readonly listeners = new Map<string, EventListener[]>();
  private dataId: string | null = null;

  constructor(options: { dataId?: string; id?: string } = {}) {
    this.dataId = options.dataId ?? null;
    this.id = options.id ?? "";
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, values: Record<string, unknown> = {}) {
    let prevented = false;
    const event = {
      target: this,
      preventDefault: () => prevented = true,
      ...values,
    } as unknown as Event;
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
    return prevented;
  }

  getAttribute(name: string) {
    return name === "data-id" ? this.dataId : null;
  }

  insertBefore(node: { textContent: string | null }) {
    this.inserted.push(node);
    return node;
  }
}

class FakeNodeRoot {
  private readonly nodes: FakeElement[];

  constructor(nodes: FakeElement[]) {
    this.nodes = nodes;
  }

  querySelectorAll(selector: string) {
    return selector === "g.node" ? this.nodes : [];
  }
}

class FakeSvg extends FakeElement {
  readonly attributes = new Map<string, string>();
  readonly viewBox = { baseVal: { width: 100, height: 50 } };
  private readonly nodes: FakeElement[];

  constructor(nodes: FakeElement[]) {
    super();
    this.nodes = nodes;
  }

  getBoundingClientRect() {
    return rect(100, 50);
  }

  querySelectorAll(selector: string) {
    return selector === "g.node" ? this.nodes : [];
  }

  setAttribute(name: string, value: string) {
    this.attributes.set(name, value);
  }
}

class FakeViewport extends FakeElement {
  capturedPointer: number | null = null;
  private readonly svg: FakeSvg;

  constructor(svg: FakeSvg) {
    super();
    this.svg = svg;
  }

  getBoundingClientRect() {
    return rect(200, 100);
  }

  querySelector(selector: string) {
    return selector === "svg" ? this.svg : null;
  }

  releasePointerCapture(pointerId: number) {
    if (this.capturedPointer === pointerId) this.capturedPointer = null;
  }

  setPointerCapture(pointerId: number) {
    this.capturedPointer = pointerId;
  }
}

class FakeContainer {
  private readonly buttons: FakeElement[];

  constructor(buttons: FakeElement[]) {
    this.buttons = buttons;
  }

  querySelectorAll(selector: string) {
    return selector === ".graph-controls button" ? this.buttons : [];
  }
}

function rect(width: number, height: number) {
  return {
    bottom: height,
    height,
    left: 0,
    right: width,
    top: 0,
    width,
    x: 0,
    y: 0,
    toJSON() {},
  };
}

test("graph back binding dispatches only from the rendered control", () => {
  const back = new FakeElement();
  let calls = 0;
  const root = {
    querySelector: (selector: string) =>
      selector === "[data-graph-back]" ? back : null,
  };

  bindGraphBack(
    root as unknown as ParentNode,
    { onBack: () => calls += 1 });

  assert.equal(calls, 0);
  back.dispatch("click");
  assert.equal(calls, 1);
});

test("type and dependency nodes decode stable Mermaid identities", () => {
  const type = new FakeElement({ dataId: "t1" });
  const unavailable = new FakeElement({ id: "flowchart-t2-4" });
  const unknown = new FakeElement({ id: "flowchart-x3-4" });
  const typeCalls: string[] = [];

  bindTypeGraphNodes(
    new FakeNodeRoot([type, unavailable, unknown]) as unknown as ParentNode,
    nodeId => {
      if (nodeId === "t1") {
        return { onSelect: () => typeCalls.push(nodeId) };
      }
      if (nodeId === "t2") {
        return { unavailableLabel: "Hidden.Type — unavailable" };
      }
      return null;
    });

  assert.deepEqual(typeCalls, []);
  assert.equal(type.classList.contains("nav-node"), true);
  assert.equal(type.style.cursor, "pointer");
  type.dispatch("click");
  assert.deepEqual(typeCalls, ["t1"]);
  assert.equal(unavailable.classList.contains("non-nav"), true);
  assert.equal(unavailable.inserted[0]?.textContent, "Hidden.Type — unavailable");
  assert.equal(unknown.classList.contains("nav-node"), false);

  const dependency = new FakeElement({ id: "flowchart-d7-2" });
  const self = new FakeElement({ dataId: "d0" });
  const dependencyCalls: string[] = [];
  bindDependencyGraphNodes(
    new FakeNodeRoot([dependency, self]) as unknown as ParentNode,
    nodeId => nodeId === "d7"
      ? { onSelect: () => dependencyCalls.push(nodeId) }
      : null);

  assert.deepEqual(dependencyCalls, []);
  dependency.dispatch("click");
  self.dispatch("click");
  assert.deepEqual(dependencyCalls, ["d7"]);
});

test("graph pan, zoom, keyboard, controls, and call-node clicks stay coordinated", () => {
  const regular = new FakeElement({ dataId: "n1" });
  const platform = new FakeElement({ id: "flowchart-n2-3" });
  const svg = new FakeSvg([regular, platform]);
  const viewport = new FakeViewport(svg);
  const zoomIn = new FakeElement();
  const zoomOut = new FakeElement();
  const reset = new FakeElement();
  zoomIn.dataset.zoom = "in";
  zoomOut.dataset.zoom = "out";
  reset.dataset.zoom = "reset";
  const container = new FakeContainer([zoomIn, zoomOut, reset]);
  const calls: string[] = [];

  bindGraphPanZoom(
    container as unknown as ParentNode,
    viewport as unknown as HTMLElement,
    {
      resolveCallGraphNode: nodeId => nodeId
        ? {
            onSelect: () => calls.push(nodeId),
            platform: nodeId === "n2",
          }
        : null,
    });

  assert.equal(viewport.tabIndex, 0);
  assert.equal(svg.attributes.get("width"), "100");
  assert.equal(svg.attributes.get("height"), "50");
  assert.equal(svg.style.transform, "translate(50px, 25px) scale(1)");
  assert.equal(regular.classList.contains("nav-node"), true);
  assert.equal(platform.classList.contains("platform-node"), true);
  regular.dispatch("click");
  assert.deepEqual(calls, ["n1"]);

  assert.equal(viewport.dispatch("wheel", {
    clientX: 100,
    clientY: 50,
    deltaY: -100,
  }), true);
  const zoomed = svg.style.transform;
  assert.notEqual(zoomed, "translate(50px, 25px) scale(1)");
  zoomOut.dispatch("click");
  const zoomedOut = svg.style.transform;
  assert.notEqual(zoomedOut, zoomed);
  zoomIn.dispatch("click");
  assert.notEqual(svg.style.transform, zoomedOut);
  reset.dispatch("click");
  const fitted = "translate(50px, 25px) scale(1)";
  assert.equal(svg.style.transform, fitted);
  for (const key of ["+", "=", "-", "_"]) {
    assert.equal(viewport.dispatch("keydown", { key }), true);
    assert.notEqual(svg.style.transform, fitted);
    assert.equal(viewport.dispatch("keydown", { key: "0" }), true);
    assert.equal(svg.style.transform, fitted);
  }
  for (const key of ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"]) {
    assert.equal(viewport.dispatch("keydown", { key }), true);
    assert.notEqual(svg.style.transform, fitted);
    assert.equal(viewport.dispatch("keydown", { key: "0" }), true);
    assert.equal(svg.style.transform, fitted);
  }
  assert.equal(viewport.dispatch("keydown", { key: "x" }), false);

  viewport.dispatch("pointerdown", {
    button: 1,
    clientX: 10,
    clientY: 10,
    pointerId: 6,
  });
  viewport.dispatch("pointermove", {
    clientX: 30,
    clientY: 10,
    pointerId: 6,
  });
  assert.equal(viewport.capturedPointer, null);

  viewport.dispatch("pointerdown", {
    button: 0,
    clientX: 10,
    clientY: 10,
    pointerId: 7,
  });
  viewport.dispatch("pointermove", {
    clientX: 20,
    clientY: 10,
    pointerId: 7,
  });
  assert.equal(viewport.capturedPointer, 7);
  assert.equal(viewport.classList.contains("panning"), true);
  viewport.dispatch("pointerup", { pointerId: 7 });
  assert.equal(viewport.capturedPointer, null);
  assert.equal(viewport.classList.contains("panning"), false);
  platform.dispatch("click");
  assert.deepEqual(calls, ["n1"]);

  viewport.dispatch("pointerdown", {
    button: 0,
    clientX: 10,
    clientY: 10,
    pointerId: 8,
  });
  viewport.dispatch("pointerup", { pointerId: 8 });
  platform.dispatch("click");
  assert.deepEqual(calls, ["n1", "n2"]);
});

test("graph bindings tolerate missing rendered surfaces", () => {
  const root = new FakeNodeRoot([]) as unknown as ParentNode;
  assert.doesNotThrow(() => bindTypeGraphNodes(root, () => null));
  assert.doesNotThrow(() => bindDependencyGraphNodes(root, () => null));
  assert.doesNotThrow(() => bindGraphBack(
    { querySelector: () => null } as unknown as ParentNode,
    { onBack() {} }));
  assert.doesNotThrow(() => bindGraphPanZoom(
    new FakeContainer([]) as unknown as ParentNode,
    {
      querySelector: () => null,
    } as unknown as HTMLElement));
});
