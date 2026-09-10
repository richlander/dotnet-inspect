import mermaid from "mermaid";
import { bindGraphExplore, createGraphExplorer } from "../src/graph-explorer.ts";
import { bindGraphPanZoom, graphControlsHtml } from "../src/graph-interactions.ts";
import { callGraphLegendHtml } from "../src/graph-legends.ts";
import {
  resolveMermaidCssVariables,
  styleCallGraphMermaid,
} from "../src/graph-mermaid.ts";
import { createWorkbenchKeybindings } from "../src/workbench-keybindings.ts";

const app = document.querySelector<HTMLElement>("#app")!;
const explorer = createGraphExplorer(document);
const keybindings = createWorkbenchKeybindings();
keybindings.attach(document);
const graphTargets = [
  {
    id: "n0", assembly: "Example", assemblyVersion: "1.0.0.0",
    typeDefinitionId: "Example.Worker", kind: "focus",
  },
  {
    id: "n1", assembly: "System.Private.CoreLib", assemblyVersion: "11.0.0.0",
    typeDefinitionId: "System.Console", kind: "external",
  },
  {
    id: "n2", assembly: "Example", assemblyVersion: "1.0.0.0",
    typeDefinitionId: "Example.Worker", kind: "normal",
  },
] as const;
let key = "member-one";
let state: "ready" | "pending" | "failure" | "no-body" = "ready";
let depth = 0;
let mounts = 0;
let navigations = 0;
let retainedSvg: SVGSVGElement | null = null;
const longHeader = new URLSearchParams(location.search).get("header") === "long";

const subject = longHeader
  ? "System.Threading.Tasks.ValueTask<System.Collections.Immutable.ImmutableArray<Example.Result>> ProcessAsync<TRequest, TResponse>(TRequest request, System.Threading.CancellationToken cancellationToken)"
  : "Process(int)";
const context = longHeader
  ? "Example.Package.Experimental.Extensions@12.0.0-preview.7.26381.103 · Example.Long.Namespace.Containing.Multiple.Nested.Types.Worker<TRequest, TResponse>"
  : "Example.Package@1.0.0 · Example.Long.Namespace.Worker";

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
    kind: "Call graph",
    subject,
    context,
    summary: "0 callers · 2 callees",
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
  const style = getComputedStyle(document.documentElement);
  const definition = resolveMermaidCssVariables(
    styleCallGraphMermaid(
      `graph LR
        n0[Process]:::focus --> n1[Platform method]:::external
        n0 --> n2[Open member]:::normal`,
      graphTargets),
    name => style.getPropertyValue(name));
  const { svg } = await mermaid.render(`browser-graph-${++mounts}`, definition);
  if (!diagram.isConnected) return;
  diagram.innerHTML = `
    <div class="graph-viewport">${svg}</div>
    ${graphControlsHtml()}`;
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
              ${callGraphLegendHtml()}
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
