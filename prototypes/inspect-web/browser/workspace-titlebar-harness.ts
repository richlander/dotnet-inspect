import { renderScopeBar } from "../src/scope-bar.ts";
import { workbenchShellHtml } from "../src/shell-controls.ts";
import { renderWorkspaceSubject } from "../src/workspace-subject.ts";

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const app = document.querySelector<HTMLElement>("#app");
if (!app) throw new Error("The workspace-titlebar harness root is unavailable.");
const workspaceMode = new URL(location.href).searchParams.has("workspace");
const coordinates = [
  {
    id: "System.Text.Json",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
  {
    id: "Microsoft.Extensions.DependencyInjection",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
  {
    id: "Microsoft.Extensions.Http",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
  {
    id: "Microsoft.Extensions.Options",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
];
const activeCoordinate = coordinates[0] ?? null;
const navigationHtml = workspaceMode
  ? renderWorkspaceSubject({
      packages: coordinates,
      activePackage: activeCoordinate,
      escapeHtml,
      packageIdentityKey: item =>
        `${item.id}@${item.version}::${item.activeFramework}`,
    })
  : `<section class="type-browser">
      <header class="browser-head">Target inventory</header>
      <label class="type-search">
        <span>/</span>
        <input aria-label="Filter types" placeholder="Filter types" />
      </label>
      <div class="namespace-picker">
        <select id="namespace-jump" class="scope-select">
          <option>All namespaces · 1</option>
        </select>
      </div>
      <div class="chip-stack">
        <div class="namespace-chips kind-chips">
          <button class="active">all kinds</button>
        </div>
      </div>
      <div class="type-list">
        <button class="namespace-row">System.Text.Json</button>
      </div>
    </section>`;
app.innerHTML = `
  <div class="workbench">
    ${workbenchShellHtml({
      subjectInspectorHtml: renderScopeBar({
        scope: workspaceMode ? "workspace" : "type",
        strip: workspaceMode
          ? []
          : [["api", "API"], ["metadata", "Metadata"], ["source", "Source"]],
        activeStripId: workspaceMode ? null : "api",
        stripAttribute: "data-lens",
        showMemberScope: !workspaceMode,
        coordinateControlsHtml: workspaceMode ? "" : `
          <label class="version-select">
            <span>version</span>
            <select id="package-version"><option>10.0.0</option></select>
          </label>
          <label class="framework-select">
            <span>framework</span>
            <select id="framework"><option>net10.0</option></select>
          </label>`,
        escapeHtml,
      }),
      workspaceTitleHtml: `
        <span>package</span>
        <strong>System.Text.Json</strong>`,
    })}
    <main class="workspace">
      ${navigationHtml}
      <section class="detail-pane">
        <header class="detail-head">
          <div class="subject-identity"><strong>${workspaceMode ? "Workspace" : "System.Text.Json.JsonSerializer"}</strong></div>
          <div class="detail-actions"><button id="share">Share</button>${workspaceMode ? "" : '<button id="copy-name">copy name</button>'}</div>
        </header>
        <article class="detail-scroll">
          <h1>${workspaceMode ? "Workspace" : "System.Text.Json.JsonSerializer"}</h1>
        </article>
      </section>
    </main>
  </div>`;
