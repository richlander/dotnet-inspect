import mermaid from "mermaid";
import { bindGraphExplore, createGraphExplorer } from "../src/graph-explorer.ts";
import { bindGraphPanZoom } from "../src/graph-interactions.ts";
import { buildDependencyGraphMermaid, resolveMermaidCssVariables } from "../src/graph-mermaid.ts";
import { bindPackageView } from "../src/package-view.ts";
import { createWorkbenchKeybindings } from "../src/workbench-keybindings.ts";

const app = document.querySelector<HTMLElement>("#app")!;
const explorer = createGraphExplorer(document);
const keybindings = createWorkbenchKeybindings();
keybindings.attach(document);
const pkg = { id: "Example.Package", version: "1.0.0", activeFramework: "net10.0" };
const loaded = { ...pkg, id: "Loaded.Dependency" };
const groups = [
  {
    index: 0, framework: "net10.0", isActive: true,
    dependencies: ["Loaded.Dependency", "New.Dependency", "Failed.Dependency"]
      .map(id => ({ id, versionRange: "1.0.0" })),
  },
  { index: 1, framework: "net11.0", dependencies: [] },
  {
    index: 2, framework: "netstandard2.0",
    dependencies: Array.from({ length: 85 }, (_, index) => ({
      id: `Dependency.${index}`, versionRange: "1.0.0",
    })),
  },
];
let groupIndex = 0;
let state: "ready" | "query-error" | "render-error" | "no-groups" = "ready";
let notices = false;
let notice = "";
let mounts = 0;
let navigations = 0;
let retainedSvg: SVGSVGElement | null = null;
let hold: Promise<void> | null = null;
let releasePending: (() => void) | null = null;
let pendingRender = Promise.resolve();

declare global {
  interface Window {
    dependencyExploreProbe: {
      update: (next: typeof state) => Promise<void>;
      startPending: () => void;
      finishPending: () => Promise<void>;
      showNotices: () => Promise<void>;
      changeOwner: () => Promise<void>;
      rememberSvg: () => void;
      sameSvg: () => boolean;
      counts: () => { mounts: number; navigations: number };
    };
  }
}

function key() {
  return `dependencies:${pkg.id}@${pkg.version}/${pkg.activeFramework}`;
}

function target() {
  return {
    key: key(),
    title: "Dependency graph",
    context: `${pkg.id}@${pkg.version} · ${pkg.activeFramework}`,
    content: document.querySelector<HTMLElement>("[data-dependency-graph-surface]")!,
    invoker: document.querySelector<HTMLElement>("#explore")!,
  };
}

async function mountGraph() {
  const diagram = document.querySelector<HTMLElement>("#dependency-graph-diagram");
  if (!diagram) return;
  if (state === "render-error") {
    diagram.innerHTML = '<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>Fixture diagram failure</p></div>';
    return;
  }
  const graph = buildDependencyGraphMermaid({
    package: pkg,
    packages: [pkg, loaded],
    packageDependencies: { dependencyGroups: groups },
    dependenciesGroupIndex: groupIndex,
    workspaceDependencies: {},
  }, (packages, id) => packages.find(candidate => candidate.id === id) ?? null);
  if (!graph) {
    diagram.innerHTML = "<p>No connected packages for this framework.</p>";
    return;
  }
  const pending = hold;
  mermaid.initialize({
    startOnLoad: false, securityLevel: "strict", flowchart: { htmlLabels: false },
  });
  const style = getComputedStyle(document.documentElement);
  const definition = resolveMermaidCssVariables(graph.definition, name => style.getPropertyValue(name));
  const { svg } = await mermaid.render(`dependency-browser-${++mounts}`, definition);
  await pending;
  if (document.querySelector("#dependency-graph-diagram") !== diagram) return;
  diagram.innerHTML = `
    <div class="dependency-graph-stage">
      <div class="graph-viewport">${svg}</div>
      <div class="graph-controls">
        <button type="button" data-zoom="in" aria-label="Zoom in">+</button>
        <button type="button" data-zoom="out" aria-label="Zoom out">-</button>
        <button type="button" data-zoom="reset" aria-label="Fit">fit</button>
      </div>
    </div>
    ${graph.truncated ? `<div class="graph-drill-error graph-diagnostics" role="status">Dependency graph truncated at ${graph.nodeLimit} nodes.</div>` : ""}`;
  bindGraphPanZoom(diagram, diagram.querySelector<HTMLElement>(".graph-viewport")!, {
    keybindings,
    resolveDependencyGraphNode: nodeId => {
      const info = graph.nodeInfoById.get(nodeId);
      if (!info?.id || info.kind === "self") return null;
      const id = info.id;
      return {
        label: `${info.kind === "open" ? "Open" : "Load"} ${id}`,
        onSelect: () => { void navigate(id); },
      };
    },
  });
}

async function navigate(id: string) {
  explorer.close(false);
  navigations++;
  if (id === "Failed.Dependency") {
    notice = "Could not load Failed.Dependency: fixture acquisition failure.";
    await render();
    document.querySelector<HTMLElement>("#explore")!.focus();
  } else {
    pkg.id = id;
    await render();
    document.querySelector<HTMLElement>("h1")!.focus();
  }
}

function patchGroup() {
  const selected = groups[groupIndex];
  if (!selected) throw new Error(`Unknown fixture dependency group: ${groupIndex}`);
  document.querySelectorAll<HTMLElement>("[data-dep-group]").forEach(button => {
    const active = Number(button.dataset.depGroup) === groupIndex;
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", String(active));
  });
  document.querySelector<HTMLElement>("#dep-list-section")!.textContent =
    `${selected.framework}: ${selected.dependencies.length} packages`;
  return mountGraph();
}

async function render() {
  explorer.beforeRender(key());
  app.innerHTML = `
    <main>
      <h1 tabindex="-1">Dependencies: ${pkg.id}</h1>
      <div class="working-surface-actions"><button type="button" id="explore" data-graph-explore${state === "query-error" || state === "no-groups" ? " disabled" : ""}>Explore</button></div>
      <button type="button" id="coordinates">Package coordinate controls</button>
      ${notice ? `<p role="status">${notice}</p>` : ""}
      <div class="package-dependencies-scroll">
        <div data-dependency-graph-surface>
          ${state === "query-error" || state === "no-groups"
            ? `<section class="document-section empty-document"><h2>${state === "query-error" ? "Dependency query failed" : "No package dependencies"}</h2><p>${state === "query-error" ? "Fixture query failure" : "Self-contained package"}</p></section>`
            : `
              ${notices ? '<section class="document-section empty-document"><h2>No exact dependency group</h2><p>The package has no manifest group matching the active coordinate.</p></section>' : ""}
              <section class="document-section dependency-group-selector">
                <div class="section-title"><h2>Target frameworks</h2><span>one framework at a time</span></div>
                <div class="type-chip-list" id="dep-tfm-chips">${groups.map(group => `<button type="button" class="type-chip" data-dep-group="${group.index}">${group.framework}</button>`).join("")}</div>
              </section>
              <section class="document-section dependency-graph-section">
                <div class="section-title"><h2>Dependency graph</h2><span>callers above · dependencies below · click a package to open</span></div>
                ${notices ? '<div class="graph-drill-error" role="status">Some workspace manifests could not be read.</div>' : ""}
                <div id="dependency-graph-diagram" class="call-graph-diagram"><p>Rendering graph...</p></div>
              </section>`}
        </div>
        <section class="document-section" id="dep-list-section"></section>
        <section class="document-section" id="assembly-references">Assembly references</section>
      </div>
    </main>`;
  bindGraphExplore(document, () => explorer.open(target()));
  bindPackageView(document, {
    onDependencyGroupSelect: index => { groupIndex = index; void patchGroup(); },
    onDependencyOpen: id => { void navigate(id); },
    onDependencyLoad: id => { void navigate(id); },
    onGraphTypeSelect() {},
    onKindJump() {},
    onLibraryScopeSelect() {},
    onNamespaceJump() {},
    onPerformanceMemberSelect() {},
  });
  explorer.afterRender(target());
  await patchGroup();
}

window.dependencyExploreProbe = {
  update: async next => { state = next; await render(); },
  startPending: () => {
    hold = new Promise<void>(resolve => { releasePending = resolve; });
    pendingRender = render();
  },
  finishPending: async () => { releasePending?.(); hold = null; await pendingRender; },
  showNotices: async () => { notices = true; await render(); },
  changeOwner: async () => { pkg.activeFramework = "net11.0"; await render(); },
  rememberSvg: () => { retainedSvg = document.querySelector("#dependency-graph-diagram svg"); },
  sameSvg: () => retainedSvg === document.querySelector("#dependency-graph-diagram svg"),
  counts: () => ({ mounts, navigations }),
};

await render();
