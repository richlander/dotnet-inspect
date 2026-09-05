import assert from "node:assert/strict";
import test from "node:test";
import {
  bindGraphBack,
  bindGraphPanZoom,
  bindTypeGraphNodes,
} from "../src/graph-interactions.ts";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import { fakeDom } from "./fake-dom.ts";

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
  readonly attributes = new Map<string, string>();
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
    const event = fakeDom.event({
      target: this,
      preventDefault: () => prevented = true,
      ...values,
    });
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
    return prevented;
  }

  getAttribute(name: string) {
    return name === "data-id"
      ? this.dataId
      : this.attributes.get(name) ?? null;
  }

  setAttribute(name: string, value: string) {
    this.attributes.set(name, value);
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

function graphTransform(element: FakeElement) {
  const match =
    /^translate\(([^p]+)px, ([^p]+)px\) scale\(([^)]+)\)$/
      .exec(element.style.transform ?? "");
  assert.ok(match);
  return {
    scale: Number(match[3]),
    x: Number(match[1]),
    y: Number(match[2]),
  };
}

function dispatchKey(
  keybindings: KeybindingRegistry,
  target: FakeElement,
  values: Record<string, unknown>,
) {
  let prevented = false;
  const event = fakeDom.keyboardEvent({
    altKey: false,
    ctrlKey: false,
    defaultPrevented: false,
    metaKey: false,
    shiftKey: false,
    target,
    composedPath: () => [target],
    preventDefault: () => prevented = true,
    ...values,
  });
  const result = keybindings.dispatch(event);
  return {
    prevented,
    result,
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
    fakeDom.parentNode(root),
    { onBack: () => calls += 1 });

  assert.equal(calls, 0);
  back.dispatch("click");
  assert.equal(calls, 1);
});

test("type nodes decode stable Mermaid identities", () => {
  const type = new FakeElement({ dataId: "t1" });
  const unavailable = new FakeElement({ id: "flowchart-t2-4" });
  const unknown = new FakeElement({ id: "flowchart-x3-4" });
  const typeCalls: string[] = [];

  bindTypeGraphNodes(
    fakeDom.parentNode(new FakeNodeRoot([type, unavailable, unknown])),
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
  assert.equal(unavailable.classList.contains("nav-node"), false);
  assert.equal(unavailable.inserted[0]?.textContent, "Hidden.Type — unavailable");
  assert.equal(unknown.classList.contains("nav-node"), false);
});

test("dependency nodes share keyboard activation and suppress clicks after dragging", () => {
  const dependency = new FakeElement({ id: "flowchart-d7-2" });
  const self = new FakeElement({ dataId: "d0" });
  const direct = new FakeElement({ dataId: "d8" });
  const viewport = new FakeViewport(new FakeSvg([dependency, self, direct]));
  const keybindings = new KeybindingRegistry();
  const dependencyCalls: string[] = [];
  bindGraphPanZoom(
    fakeDom.parentNode(new FakeContainer([])),
    fakeDom.htmlElement(viewport),
    {
      keybindings,
      resolveDependencyGraphNode: nodeId => nodeId === "d0"
        ? null
        : {
            label: `Open ${nodeId}`,
            onSelect: () => dependencyCalls.push(nodeId),
          },
    });

  assert.deepEqual(dependencyCalls, []);
  assert.equal(dependency.classList.contains("nav-node"), true);
  assert.equal(dependency.style.cursor, "pointer");
  assert.equal(dependency.attributes.get("aria-label"), "Open d7");
  assert.equal(dependency.attributes.get("tabindex"), "0");
  assert.equal(dependency.attributes.get("role"), "button");
  assert.equal(self.classList.contains("nav-node"), false);
  dependency.dispatch("click");
  self.dispatch("click");
  assert.deepEqual(dependencyCalls, ["d7"]);
  for (const key of ["Enter", " "]) {
    assert.equal(dispatchKey(keybindings, direct, { key }).prevented, true);
  }
  assert.deepEqual(dependencyCalls, ["d7", "d8", "d8"]);
  viewport.dispatch("pointerdown", {
    button: 0, clientX: 10, clientY: 10, pointerId: 1,
  });
  viewport.dispatch("pointermove", {
    clientX: 30, clientY: 10, pointerId: 1,
  });
  viewport.dispatch("pointerup", { pointerId: 1 });
  dependency.dispatch("click");
  assert.deepEqual(dependencyCalls, ["d7", "d8", "d8"]);
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
  const keybindings = new KeybindingRegistry();

  bindGraphPanZoom(
    fakeDom.parentNode(container),
    fakeDom.htmlElement(viewport),
    {
      keybindings,
      resolveCallGraphNode: nodeId => nodeId
        ? {
            onSelect: () => calls.push(nodeId),
          label: `Open ${nodeId}`,
          platform: nodeId === "n2",
          blocked: nodeId === "n2",
        }
        : null,
    });

  assert.equal(viewport.tabIndex, 0);
  assert.equal(svg.attributes.get("width"), "100");
  assert.equal(svg.attributes.get("height"), "50");
  assert.equal(svg.style.transform, "translate(50px, 25px) scale(1)");
  assert.equal(regular.classList.contains("nav-node"), true);
  assert.equal(regular.classList.contains("platform-node"), false);
  assert.equal(platform.classList.contains("platform-node"), true);
  assert.equal(regular.attributes.get("role"), "button");
  assert.equal(regular.attributes.get("tabindex"), "0");
  assert.equal(regular.attributes.get("aria-label"), "Open n1");
  assert.equal(platform.style.cursor, "not-allowed");
  regular.dispatch("click");
  assert.deepEqual(calls, ["n1"]);
  assert.equal(dispatchKey(
    keybindings,
    platform,
    { key: "Enter" },
  ).prevented, true);
  assert.deepEqual(calls, ["n1", "n2"]);

  assert.equal(viewport.dispatch("wheel", {
    clientX: 100,
    clientY: 50,
    deltaY: -100,
  }), true);
  const zoomed = graphTransform(svg);
  assert.ok(zoomed.scale > 1);
  assert.ok(zoomed.x < 50);
  assert.ok(zoomed.y < 25);
  zoomOut.dispatch("click");
  const zoomedOut = graphTransform(svg);
  assert.ok(zoomedOut.scale < zoomed.scale);
  zoomIn.dispatch("click");
  assert.ok(graphTransform(svg).scale > zoomedOut.scale);
  reset.dispatch("click");
  const fitted = "translate(50px, 25px) scale(1)";
  assert.equal(svg.style.transform, fitted);
  for (const key of ["+", "="]) {
    assert.equal(dispatchKey(keybindings, viewport, { key }).prevented, true);
    assert.ok(graphTransform(svg).scale > 1);
    assert.equal(dispatchKey(
      keybindings,
      viewport,
      { key: "0" },
    ).prevented, true);
    assert.equal(svg.style.transform, fitted);
  }
  for (const key of ["-", "_"]) {
    assert.equal(dispatchKey(keybindings, viewport, { key }).prevented, true);
    assert.ok(graphTransform(svg).scale < 1);
    assert.equal(dispatchKey(
      keybindings,
      viewport,
      { key: "0" },
    ).prevented, true);
    assert.equal(svg.style.transform, fitted);
  }
  const arrowPositions = new Map([
    ["ArrowLeft", { x: 95, y: 25 }],
    ["ArrowRight", { x: 5, y: 25 }],
    ["ArrowUp", { x: 50, y: 70 }],
    ["ArrowDown", { x: 50, y: -20 }],
  ]);
  for (const [key, expected] of arrowPositions) {
    assert.equal(dispatchKey(keybindings, viewport, { key }).prevented, true);
    assert.deepEqual(graphTransform(svg), { ...expected, scale: 1 });
    assert.equal(dispatchKey(
      keybindings,
      viewport,
      { key: "0" },
    ).prevented, true);
  }
  assert.equal(
    dispatchKey(keybindings, viewport, { key: "x" }).result.handled,
    false,
  );
  assert.equal(dispatchKey(keybindings, viewport, {
    key: "ArrowLeft",
    shiftKey: true,
  }).result.handled, false);
  assert.equal(dispatchKey(keybindings, viewport, {
    altKey: true,
    key: "ArrowRight",
  }).result.handled, false);

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
    clientX: 30,
    clientY: 10,
    pointerId: 70,
  });
  assert.equal(viewport.capturedPointer, null);
  assert.equal(svg.style.transform, fitted);
  viewport.dispatch("pointermove", {
    clientX: 20,
    clientY: 10,
    pointerId: 7,
  });
  assert.equal(viewport.capturedPointer, 7);
  assert.equal(viewport.classList.contains("panning"), true);
  assert.deepEqual(graphTransform(svg), { scale: 1, x: 60, y: 25 });
  viewport.dispatch("pointerup", { pointerId: 7 });
  assert.equal(viewport.capturedPointer, null);
  assert.equal(viewport.classList.contains("panning"), false);
  platform.dispatch("click");
  assert.deepEqual(calls, ["n1", "n2"]);

  viewport.dispatch("pointerdown", {
    button: 0,
    clientX: 10,
    clientY: 10,
    pointerId: 8,
  });
  viewport.dispatch("pointermove", {
    clientX: 10,
    clientY: 20,
    pointerId: 8,
  });
  assert.equal(viewport.capturedPointer, 8);
  viewport.dispatch("pointercancel", { pointerId: 8 });
  assert.equal(viewport.capturedPointer, null);
  assert.equal(viewport.classList.contains("panning"), false);
  platform.dispatch("click");
  assert.deepEqual(calls, ["n1", "n2"]);

  viewport.dispatch("pointerdown", {
    button: 0,
    clientX: 10,
    clientY: 10,
    pointerId: 9,
  });
  viewport.dispatch("pointerup", { pointerId: 9 });
  platform.dispatch("click");
  assert.deepEqual(calls, ["n1", "n2", "n2"]);
});

test("graph bindings tolerate missing rendered surfaces", () => {
  const keybindings = new KeybindingRegistry();
  const root = fakeDom.parentNode(new FakeNodeRoot([]));
  assert.doesNotThrow(() => bindTypeGraphNodes(root, () => null));
  assert.doesNotThrow(() => bindGraphBack(
    fakeDom.parentNode({ querySelector: () => null }),
    { onBack() {} }));
  assert.doesNotThrow(() => bindGraphPanZoom(
    fakeDom.parentNode(new FakeContainer([])),
    fakeDom.htmlElement({
      querySelector: () => null,
    }),
    { keybindings }));
});
