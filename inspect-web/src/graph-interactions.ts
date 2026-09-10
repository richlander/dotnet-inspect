import type { KeybindingRegistry } from "./keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "./workbench-keybindings.ts";

export interface GraphBackBindingActions {
  onBack: () => void;
}

export interface GraphNodeBinding {
  onSelect: () => void;
  label: string;
  platform?: boolean;
  blocked?: boolean;
}

export interface GraphPanZoomBindingOptions {
  keybindings: KeybindingRegistry;
  resolveCallGraphNode?: (
    nodeId: string,
  ) => GraphNodeBinding | null;
  resolveDependencyGraphNode?: (
    nodeId: string,
  ) => GraphNodeBinding | null;
  resolveTypeGraphNode?: (
    nodeId: string,
  ) => GraphNodeBinding | { unavailableLabel: string } | null;
}

export function bindGraphBack(
  root: ParentNode,
  actions: GraphBackBindingActions,
) {
  root.querySelector("[data-graph-back]")
    ?.addEventListener("click", actions.onBack);
}

function mermaidNodeId(node: Element, prefix: string): string {
  const dataId = node.getAttribute("data-id");
  const idMatch =
    node.id.match(new RegExp(`(?:^|flowchart-)(${prefix}\\d+)(?:-|$)`));
  return dataId || idMatch?.[1] || "";
}

export function bindGraphPanZoom(
  container: ParentNode,
  viewport: HTMLElement,
  options: GraphPanZoomBindingOptions,
) {
  const svg = viewport.querySelector<SVGSVGElement>("svg");
  if (!svg) return;
  const renderedSvg = svg;

  const box = svg.viewBox?.baseVal;
  const naturalWidth =
    box && box.width ? box.width : svg.getBoundingClientRect().width;
  const naturalHeight =
    box && box.height ? box.height : svg.getBoundingClientRect().height;
  svg.setAttribute("width", String(naturalWidth));
  svg.setAttribute("height", String(naturalHeight));

  const minScale = 0.2;
  const maxScale = 8;
  const view = { scale: 1, x: 0, y: 0 };
  const clampScale = (value: number) =>
    Math.min(maxScale, Math.max(minScale, value));

  function apply() {
    renderedSvg.style.transform =
      `translate(${view.x}px, ${view.y}px) scale(${view.scale})`;
  }

  function fit() {
    const rect = viewport.getBoundingClientRect();
    if (!naturalWidth || !naturalHeight || !rect.width) return;
    const fitScale =
      Math.min(rect.width / naturalWidth, rect.height / naturalHeight) * 0.92;
    view.scale = clampScale(Math.min(fitScale, 1));
    view.x = (rect.width - naturalWidth * view.scale) / 2;
    view.y = (rect.height - naturalHeight * view.scale) / 2;
    apply();
  }

  function zoomAt(px: number, py: number, factor: number) {
    const next = clampScale(view.scale * factor);
    const ratio = next / view.scale;
    view.x = px - (px - view.x) * ratio;
    view.y = py - (py - view.y) * ratio;
    view.scale = next;
    apply();
  }

  viewport.addEventListener("wheel", event => {
    event.preventDefault();
    const rect = viewport.getBoundingClientRect();
    zoomAt(
      event.clientX - rect.left,
      event.clientY - rect.top,
      Math.exp(-event.deltaY * 0.0015));
  }, { passive: false });

  let pointerId: number | null = null;
  let moved = false;
  let capturing = false;
  const panThreshold = 5;
  const start = { x: 0, y: 0, vx: 0, vy: 0 };
  viewport.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    pointerId = event.pointerId;
    moved = false;
    capturing = false;
    start.x = event.clientX;
    start.y = event.clientY;
    start.vx = view.x;
    start.vy = view.y;
  });
  viewport.addEventListener("pointermove", event => {
    if (pointerId !== event.pointerId) return;
    const dx = event.clientX - start.x;
    const dy = event.clientY - start.y;
    if (!capturing) {
      if (Math.abs(dx) + Math.abs(dy) <= panThreshold) return;
      capturing = true;
      moved = true;
      viewport.setPointerCapture(pointerId);
      viewport.classList.add("panning");
    }
    view.x = start.vx + dx;
    view.y = start.vy + dy;
    apply();
  });
  function endPan(event: PointerEvent) {
    if (pointerId !== event.pointerId) return;
    if (capturing) {
      viewport.releasePointerCapture(pointerId);
      viewport.classList.remove("panning");
    }
    capturing = false;
    pointerId = null;
  }
  viewport.addEventListener("pointerup", endPan);
  viewport.addEventListener("pointercancel", endPan);

  container.querySelectorAll<HTMLElement>(".graph-controls button")
    .forEach(button => {
      button.addEventListener("click", () => {
        const rect = viewport.getBoundingClientRect();
        const mode = button.dataset.zoom;
        if (mode === "in")
          zoomAt(rect.width / 2, rect.height / 2, 1.25);
        else if (mode === "out")
          zoomAt(rect.width / 2, rect.height / 2, 0.8);
        else
          fit();
      });
    });

  viewport.tabIndex = 0;
  const handlePanZoomKey = (event: KeyboardEvent): boolean => {
    const rect = viewport.getBoundingClientRect();
    const step = 45;
    if (event.key === "+" || event.key === "=")
      zoomAt(rect.width / 2, rect.height / 2, 1.25);
    else if (event.key === "-" || event.key === "_")
      zoomAt(rect.width / 2, rect.height / 2, 0.8);
    else if (event.key === "0")
      fit();
    else if (event.key === "ArrowLeft" && !event.altKey && !event.shiftKey) {
      // Alt/Shift+ArrowLeft is the global back gesture; leave it unclaimed so
      // panning doesn't swallow document-level history navigation.
      view.x += step;
      apply();
    } else if (event.key === "ArrowRight" && !event.altKey && !event.shiftKey) {
      view.x -= step;
      apply();
    } else if (event.key === "ArrowUp") {
      view.y += step;
      apply();
    } else if (event.key === "ArrowDown") {
      view.y -= step;
      apply();
    } else {
      return false;
    }
    return true;
  };
  options.keybindings.register({
    id: "graph.zoom",
    key: ["+", "=", "-", "_", "0"],
    allowExtraModifiers: true,
    priority: WORKBENCH_KEYBINDING_PRIORITY.element,
    run: handlePanZoomKey,
  }, viewport);
  options.keybindings.register({
    id: "graph.pan-horizontal",
    key: ["ArrowLeft", "ArrowRight"],
    allowExtraModifiers: true,
    priority: WORKBENCH_KEYBINDING_PRIORITY.element,
    when: event => !event.altKey && !event.shiftKey,
    run: handlePanZoomKey,
  }, viewport);
  options.keybindings.register({
    id: "graph.pan-vertical",
    key: ["ArrowUp", "ArrowDown"],
    allowExtraModifiers: true,
    priority: WORKBENCH_KEYBINDING_PRIORITY.element,
    run: handlePanZoomKey,
  }, viewport);

  const resolveNode = options.resolveCallGraphNode
    ?? options.resolveDependencyGraphNode
    ?? options.resolveTypeGraphNode;
  if (resolveNode) {
    const prefix = options.resolveCallGraphNode ? "n"
      : options.resolveDependencyGraphNode ? "d" : "t";
    svg.querySelectorAll<SVGGElement>("g.node").forEach(node => {
      const binding = resolveNode(mermaidNodeId(node, prefix));
      if (!binding) return;
      if ("unavailableLabel" in binding) {
        node.classList.add("non-nav");
        node.setAttribute("role", "img");
        node.setAttribute("aria-label", binding.unavailableLabel);
        const title =
          node.ownerDocument.createElementNS("http://www.w3.org/2000/svg", "title");
        title.textContent = binding.unavailableLabel;
        node.insertBefore(title, node.firstChild);
        return;
      }
      node.classList.add("nav-node");
      if (binding.platform) node.classList.add("platform-node");
      node.style.cursor = binding.blocked ? "not-allowed" : "pointer";
      node.setAttribute("tabindex", "0");
      node.setAttribute("role", "button");
      node.setAttribute("aria-label", binding.label);
      node.addEventListener("click", () => {
        if (!moved) binding.onSelect();
      });
      options.keybindings.register({
        id: options.resolveCallGraphNode
          ? "call-graph-node.activate"
          : options.resolveDependencyGraphNode
            ? "dependency-graph-node.activate"
            : "type-graph-node.activate",
        key: ["Enter", " "],
        allowExtraModifiers: true,
        priority: WORKBENCH_KEYBINDING_PRIORITY.element,
        run: () => {
          binding.onSelect();
          return true;
        },
      }, node);
    });
  }

  fit();
}
