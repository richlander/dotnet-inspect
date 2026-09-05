import mermaid from "mermaid";
import { bindGraphExplore, createGraphExplorer } from "../src/graph-explorer.ts";
import { bindGraphPanZoom } from "../src/graph-interactions.ts";
import { buildTypeGraphMermaid, resolveMermaidCssVariables } from "../src/graph-mermaid.ts";
import {
  renderTypeMetadata, typeMetadataSignature, type TypeSummary,
} from "../src/type-panel.ts";
import { createWorkbenchKeybindings } from "../src/workbench-keybindings.ts";

const app = document.querySelector<HTMLElement>("#app")!;
app.style.height = "100dvh";
app.style.display = "grid";
app.style.gridTemplateRows = "40px minmax(0, 1fr)";
const explorer = createGraphExplorer(document);
const keybindings = createWorkbenchKeybindings();
keybindings.attach(document);
const pkg = { id: "Example.Package", version: "1.0.0", activeFramework: "net11.0" };
const item: TypeSummary = {
  id: "Example.Type", name: "Type", namespace: "Example", kind: "class",
  signature: "public class Type", members: 1, assembly: "Example.dll",
};
let state: "ready" | "partial" | "loading" | "error" | "empty" | "stale" = "ready";
let mounts = 0;
let navigations = 0;
let retainedSvg: SVGSVGElement | null = null;
let hold: Promise<void> | null = null;
let releasePending: (() => void) | null = null;
let pendingRender = Promise.resolve();

declare global {
  interface Window {
    typeExploreProbe: {
      update: (next: typeof state) => Promise<void>;
      startPending: () => void;
      finishPending: () => Promise<void>;
      changeOwner: () => Promise<void>;
      rememberSvg: () => void;
      sameSvg: () => boolean;
      counts: () => { mounts: number; navigations: number };
    };
  }
}

function metadata() {
  return {
    graphNodes: state === "empty" ? [{ id: item.id, displayName: item.id, role: "self" }] : [
      { id: "External.Base", displayName: "External.Base", role: "base" },
      { id: "Example.Type", displayName: "Example.Type", role: item.id === "Example.Type" ? "self" : "base" },
      { id: "Example.Derived", displayName: "Example.Derived", role: item.id === "Example.Derived" ? "self" : "derived" },
    ],
    graphEdges: [
      { fromId: "External.Base", toId: "Example.Type" },
      { fromId: "Example.Type", toId: "Example.Derived" },
    ],
    inspectionFailures: state === "partial" ? ["Fixture relationship could not be projected."] : [],
    derivedTypes: ["Example.Derived"],
    attributes: ["Example.Attribute"],
  };
}

function key() {
  return state === "empty" ? null : JSON.stringify(["type", typeMetadataSignature(item, pkg)]);
}

function target() {
  const owner = key();
  const content = document.querySelector<HTMLElement>("[data-type-graph-surface]");
  const invoker = document.querySelector<HTMLElement>("#explore");
  return owner && content && invoker
    ? { key: owner, title: "Type relationships", context: `${pkg.id} > ${item.id}`, content, invoker }
    : null;
}

function focusHeading() {
  const heading = document.querySelector<HTMLElement>("#metadata-surface-title")!;
  heading.tabIndex = -1;
  heading.focus({ preventScroll: true });
}

async function mountGraph() {
  const diagram = document.querySelector<HTMLElement>("#type-graph-diagram");
  if (!diagram) return;
  const meta = metadata();
  const definition = buildTypeGraphMermaid(meta);
  if (!definition) throw new Error("Fixture graph region has no graph.");
  const pending = hold;
  mermaid.initialize({
    startOnLoad: false, securityLevel: "strict", flowchart: { htmlLabels: false },
  });
  const style = getComputedStyle(document.documentElement);
  const resolved = resolveMermaidCssVariables(definition, name => style.getPropertyValue(name));
  const { svg } = await mermaid.render(`type-browser-${++mounts}`, resolved);
  await pending;
  if (document.querySelector("#type-graph-diagram") !== diagram) return;
  diagram.innerHTML = `
    <div class="graph-viewport">${svg}</div>
    <div class="graph-controls">
      <button type="button" data-zoom="in" aria-label="Zoom in">+</button>
      <button type="button" data-zoom="out" aria-label="Zoom out">-</button>
      <button type="button" data-zoom="reset" aria-label="Fit">fit</button>
    </div>`;
  const nodes = new Map(meta.graphNodes.map((node, index) => [`t${index}`, node]));
  bindGraphPanZoom(diagram, diagram.querySelector<HTMLElement>(".graph-viewport")!, {
    keybindings,
    resolveTypeGraphNode: id => {
      const node = nodes.get(id);
      if (!node) return null;
      if (node.id === "External.Base") {
        return { unavailableLabel: "External.Base - not in the browsable public surface" };
      }
      return {
        label: `Open ${node.displayName}`,
        onSelect: () => {
          explorer.close(false);
          navigations++;
          item.id = node.id;
          void render().then(focusHeading);
        },
      };
    },
  });
}

function escapeHtml(value: unknown) {
  return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

async function render() {
  const wasOpen = explorer.isOpen;
  explorer.beforeRender(key());
  const closed = wasOpen && !explorer.isOpen;
  const available = state === "ready" || state === "partial";
  app.innerHTML = `
    <div class="working-surface-actions"><button type="button" id="explore" data-graph-explore${available ? "" : " disabled"}>Explore</button></div>
    ${renderTypeMetadata({
      item, packageContext: pkg,
      metadataState: {
        typeMetadataKey: state === "stale" ? "old-coordinate" : typeMetadataSignature(item, pkg),
        typeMetadataLoading: state === "loading",
        typeMetadataError: state === "error" ? "Fixture projection failure." : null,
        typeMetadata: state === "loading" || state === "error" ? null : metadata(),
      },
      memberCompositionHtml: "",
      escapeHtml,
      relatedTypeChip: name => `<span class="type-chip">${escapeHtml(name)}</span>`,
      factRows: rows => `<div class="fact-rows">${rows.map(([label, value]) =>
        `<div><span>${escapeHtml(label)}</span><span>${escapeHtml(value)}</span></div>`).join("")}</div>`,
    })}`;
  bindGraphExplore(document, () => {
    const next = target();
    if (!next) throw new Error("Fixture Explore control has no target.");
    explorer.open(next);
  });
  explorer.afterRender(target());
  if (closed) focusHeading();
  await mountGraph();
}

window.typeExploreProbe = {
  update: async next => { state = next; await render(); },
  startPending: () => {
    hold = new Promise<void>(resolve => { releasePending = resolve; });
    pendingRender = render();
  },
  finishPending: async () => { releasePending?.(); hold = null; await pendingRender; },
  changeOwner: async () => { pkg.activeFramework = "net10.0"; await render(); },
  rememberSvg: () => { retainedSvg = document.querySelector("#type-graph-diagram svg"); },
  sameSvg: () => retainedSvg === document.querySelector("#type-graph-diagram svg"),
  counts: () => ({ mounts, navigations }),
};

await render();
