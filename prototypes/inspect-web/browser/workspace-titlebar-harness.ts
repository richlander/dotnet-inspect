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
const params = new URL(location.href).searchParams;
const workspaceMode = params.has("workspace");
const packageMode = params.has("package");
const memberMode = params.has("member");
const subjectPath = workspaceMode
  ? ["Workspace"]
  : packageMode
    ? ["System.Text.Json"]
    : memberMode
      ? ["System.Text.Json", "System.Text.Json.JsonSerializer", "DeserializeSync"]
      : ["System.Text.Json", "System.Text.Json.JsonSerializer"];
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
        scope: workspaceMode
          ? "workspace"
          : packageMode
            ? "package"
            : memberMode
              ? "member"
              : "type",
        strip: workspaceMode
          ? []
          : packageMode
            ? [["overview", "Overview"], ["dependencies", "Dependencies"]]
            : memberMode
              ? [["overview", "Overview"], ["call-graph", "Call graph"]]
              : [["api", "API"], ["metadata", "Metadata"], ["source", "Source"]],
        activeStripId: workspaceMode
          ? null
          : packageMode
            ? "overview"
            : memberMode
              ? "overview"
              : "api",
        stripAttribute: packageMode
          ? "data-package-lens"
          : memberMode
            ? "data-member-section"
            : "data-lens",
        showMemberScope: memberMode,
        escapeHtml,
      }),
    })}
    <header class="subject-zone" aria-label="Inspected subject">
      <div class="subject-path" aria-label="${subjectPath.join(" > ")}" title="${subjectPath.join(" > ")}">
        ${subjectPath.map((segment, index) =>
          `${index === 0 ? "" : '<span class="subject-path-separator" aria-hidden="true">&gt;</span>'}<span class="subject-path-segment${index === 0 ? " root" : ""}${index === subjectPath.length - 1 ? " current" : ""}">${escapeHtml(segment)}</span>`).join("")}
      </div>
      <div class="subject-advertisements"></div>
      <div class="detail-actions"><button id="share">Share</button>${workspaceMode ? "" : '<button id="copy-name">copy name</button>'}</div>
    </header>
    <div class="notice-stack"></div>
    <main class="workspace">
      ${navigationHtml}
      <section class="detail-pane">
        <article class="detail-scroll">
          <h1>${subjectPath.at(-1)}</h1>
          ${packageMode ? `
            <section class="document-section package-coordinate-editor">
              <div class="section-title"><h2>Package coordinate</h2><span>1 target framework</span></div>
              <div class="package-coordinate-fields">
                <label class="version-select"><span>Version</span><select id="package-version"><option>10.0.0</option></select></label>
                <label class="framework-select"><span>Framework</span><select id="framework"><option>net10.0</option></select></label>
              </div>
            </section>` : ""}
        </article>
      </section>
    </main>
  </div>`;
