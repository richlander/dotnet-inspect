import { renderScopeBar } from "../src/scope-bar.ts";
import { workbenchShellHtml } from "../src/shell-controls.ts";

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const app = document.querySelector<HTMLElement>("#app");
if (!app) throw new Error("The workspace-titlebar harness root is unavailable.");
app.innerHTML = `
  <div class="workbench">
    ${workbenchShellHtml({
      subjectInspectorHtml: renderScopeBar({
        scope: "type",
        strip: [["api", "API"], ["metadata", "Metadata"], ["source", "Source"]],
        activeStripId: "api",
        stripAttribute: "data-lens",
        showMemberScope: true,
        coordinateControlsHtml: `
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
      <section class="type-browser">
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
      </section>
      <section class="detail-pane">
        <header class="detail-head">
          <div class="subject-identity"><strong>System.Text.Json.JsonSerializer</strong></div>
          <div class="detail-actions"><button id="share">Share</button><button id="copy-name">copy name</button></div>
        </header>
        <article class="detail-scroll">
          <h1>System.Text.Json.JsonSerializer</h1>
        </article>
      </section>
    </main>
  </div>`;
