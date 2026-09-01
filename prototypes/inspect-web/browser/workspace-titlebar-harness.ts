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
  ? [{ kind: "workspace", label: "Workspace", copyable: false }]
  : packageMode
    ? [{ kind: "package", label: "System.Text.Json", copyable: true }]
    : memberMode
      ? [
          { kind: "package", label: "System.Text.Json", copyable: true },
          { kind: "type", label: "System.Text.Json.JsonSerializer", copyable: true },
          { kind: "member", label: "DeserializeSync", copyable: true },
        ]
      : [
          { kind: "package", label: "System.Text.Json", copyable: true },
          { kind: "type", label: "System.Text.Json.JsonSerializer", copyable: true },
        ];
const subjectPathLabel = subjectPath.map(segment => segment.label).join(" > ");
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
      <span class="subject-icon" aria-hidden="true">${workspaceMode ? "W" : "⬡"}</span>
      <div class="subject-path" aria-label="${subjectPathLabel}" title="${subjectPathLabel}">
        ${subjectPath.map((segment, index) => {
          const className = `subject-path-segment${index === 0 ? " root" : ""}${index === subjectPath.length - 1 ? " current" : ""}`;
          const content = segment.copyable
            ? `<button type="button" class="${className}" data-subject-copy="${index}" title="Copy ${escapeHtml(segment.label)}" aria-label="Copy ${segment.kind} name ${escapeHtml(segment.label)}">${escapeHtml(segment.label)}</button>`
            : `<span class="${className}">${escapeHtml(segment.label)}</span>`;
          return `${index === 0 ? "" : '<span class="subject-path-separator" aria-hidden="true">&gt;</span>'}${content}`;
        }).join("")}
      </div>
      <div class="subject-advertisements"></div>
      <div class="subject-navigation">
        <div class="nav-history">
          <button id="nav-back" disabled aria-label="Back">←</button>
          <button id="nav-forward" disabled aria-label="Forward">→</button>
        </div>
        <button id="open-search" class="subject-search" type="button" aria-haspopup="dialog">
          <span class="subject-search-glyph" aria-hidden="true">⌕</span>
          <span class="subject-search-label">Search types, members, packages</span>
          <kbd>Ctrl P</kbd>
        </button>
      </div>
      <div class="detail-actions"><button id="share">Share</button></div>
    </header>
    <div class="notice-stack"></div>
    <main class="workspace">
      ${navigationHtml}
      <section class="detail-pane">
        <article class="detail-scroll">
          <h1>${subjectPath.at(-1)?.label}</h1>
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

document.querySelectorAll<HTMLElement>("[data-subject-copy]").forEach(button =>
  button.addEventListener("click", () => {
    const index = Number(button.dataset.subjectCopy);
    document.body.dataset.copiedSubject = subjectPath[index]?.label ?? "";
  }));
