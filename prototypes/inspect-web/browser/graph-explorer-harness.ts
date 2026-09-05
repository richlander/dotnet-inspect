import mermaid from "mermaid";
import { bindGraphExplore, createGraphExplorer } from "../src/graph-explorer.ts";
import { bindGraphPanZoom } from "../src/graph-interactions.ts";
import { createWorkbenchKeybindings } from "../src/workbench-keybindings.ts";

const app = document.querySelector<HTMLElement>("#app")!;
const explorer = createGraphExplorer(document);
const keybindings = createWorkbenchKeybindings();
keybindings.attach(document);
let key = "member-one";
let state: "ready" | "pending" | "failure" | "no-body" = "ready";
let depth = 0;
let mounts = 0;
let navigations = 0;
let retainedSvg: SVGSVGElement | null = null;

declare global {
  interface Window {
    graphExploreProbe: {
      update: (next: typeof state) => Promise<void>;
      sameSvg: () => boolean;
      rememberSvg: () => void;
      counts: () => { mounts: number; navigations: number };
      navigate: () => Promise<void>;
      replaceModal: () => void;
    };
  }
}

function target() {
  return {
    key,
    title: "Call graph",
    context: "Example.Package@1.0.0 > Example.Long.Namespace.Worker > Process(int)",
    content: document.querySelector<HTMLElement>("[data-call-graph-surface]")!,
    invoker: document.querySelector<HTMLElement>("#explore")!,
  };
}

async function mountGraph() {
  const diagram = document.querySelector<HTMLElement>("#diagram");
  if (!diagram) return;
  mermaid.initialize({
    startOnLoad: false,
    securityLevel: "strict",
    flowchart: { htmlLabels: false },
  });
  const { svg } = await mermaid.render(`browser-graph-${++mounts}`,
    "graph LR\nn0[Process] --> n1[Platform method]\nn0 --> n2[Open member]");
  if (!diagram.isConnected) return;
  diagram.innerHTML = `
    <div class="graph-viewport">${svg}</div>
    <div class="graph-controls">
      <button type="button" data-zoom="in" aria-label="Zoom in">+</button>
      <button type="button" data-zoom="out" aria-label="Zoom out">-</button>
      <button type="button" data-zoom="reset" aria-label="Fit">fit</button>
    </div>`;
  bindGraphPanZoom(diagram, diagram.querySelector<HTMLElement>(".graph-viewport")!, {
    keybindings,
    resolveCallGraphNode: id => id === "n1"
      ? {
          label: "Drill into platform",
          onSelect: () => {
            depth++;
            void render();
          },
        }
      : id === "n2"
        ? { label: "Open member", onSelect: () => { void navigate(); } }
        : null,
  });
}

async function navigate() {
  explorer.close(false);
  navigations++;
  key = "member-two";
  await render();
  document.querySelector<HTMLElement>("h1")!.focus();
}

async function render() {
  explorer.beforeRender(key);
  app.innerHTML = `
    <main>
      <h1 tabindex="-1">Member ${key}</h1>
      <div class="working-surface-actions"><button type="button" id="explore" data-graph-explore${state === "no-body" || state === "failure" ? " disabled" : ""}>Explore</button></div>
      <button type="button" id="background">Background action</button>
      <div data-call-graph-surface>
        ${state === "failure" || state === "no-body"
          ? `<section class="document-section empty-member-section"><h2>${state === "failure" ? "Call graph query failed" : "No call graph"}</h2><p>${state === "failure" ? "Unavailable assembly" : "No IL body"}</p></section>`
          : `<section class="document-section call-graph-section">
              <div class="section-title"><h2>Call graph</h2><span>0 callers · 2 callees</span></div>
              ${depth ? `<div class="graph-breadcrumb"><button type="button" id="graph-back">Back</button><span>Platform depth ${depth}</span></div>` : ""}
              ${state === "pending" ? '<div class="graph-expanding">Scanning callers…</div>' : ""}
              <div class="graph-scope"><strong>Workspace callers</strong><span>2 loaded packages</span><strong>Callees</strong><span>depth 2</span></div>
              <div id="diagram" class="call-graph-diagram"><p>Rendering graph…</p></div>
              <div class="graph-legend"><span>target member</span><span>same type</span><span>external assembly (platform lookup on click)</span></div>
              <details class="graph-mermaid"><summary>Mermaid source</summary><pre><code>graph LR</code></pre></details>
            </section>`}
      </div>
    </main>`;
  bindGraphExplore(document, () => explorer.open(target()));
  document.querySelector("#graph-back")?.addEventListener("click", () => {
    depth--;
    void render();
  });
  explorer.afterRender(target());
  if (state === "ready" || state === "pending") await mountGraph();
}

window.addEventListener("popstate", () => {
  explorer.close(false);
  key = "history-member";
  void render();
});

window.graphExploreProbe = {
  update: async next => {
    state = next;
    await render();
  },
  sameSvg: () => retainedSvg === document.querySelector("#diagram svg"),
  rememberSvg: () => { retainedSvg = document.querySelector("#diagram svg"); },
  counts: () => ({ mounts, navigations }),
  navigate,
  replaceModal: () => {
    explorer.close(false);
    const replacement = document.createElement("dialog");
    replacement.setAttribute("aria-label", "Settings");
    const close = document.createElement("button");
    close.textContent = "Done";
    close.addEventListener("click", () => replacement.remove());
    replacement.append(close);
    document.body.append(replacement);
    replacement.showModal();
  },
};

await render();
