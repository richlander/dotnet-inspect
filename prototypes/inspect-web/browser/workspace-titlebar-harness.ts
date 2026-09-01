import {
  packageBarHtml,
  type PackageBarPackage,
} from "../src/package-bar.ts";
import { workbenchShellHtml } from "../src/shell-controls.ts";

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function packageIdentityKey(pkg: PackageBarPackage): string {
  return `${pkg.id}@${pkg.version}::${pkg.activeFramework}`;
}

const requestedCount = Number(
  new URLSearchParams(location.search).get("count") ?? "1");
const packageCount = Number.isInteger(requestedCount) && requestedCount > 0
  ? requestedCount
  : 1;
const packages = Array.from({ length: packageCount }, (_, index) => ({
  id: index === 0
    ? "System.Text.Json"
    : `Microsoft.Extensions.Workspace.${index + 1}`,
  version: "10.0.0",
  workspaceIndex: index + 1,
  activeFramework: "net10.0",
  isRuntimePack: false,
}));
const activePackage = packages[0] ?? null;
const workspaceStripHtml = packageBarHtml(
  { packages, package: activePackage },
  null,
  escapeHtml,
  packageIdentityKey);

const app = document.querySelector<HTMLElement>("#app");
if (!app) throw new Error("The workspace-titlebar harness root is unavailable.");
app.innerHTML = `
  <div class="workbench">
    ${workbenchShellHtml({
      workspaceStripHtml,
      workspaceTitleHtml: `
        <span>package</span>
        <strong>System.Text.Json</strong>`,
      coordinateSelectorsHtml: `
        <label class="version-select">
          <span>version</span>
          <select id="package-version"><option>10.0.0</option></select>
        </label>
        <label class="framework-select">
          <span>framework</span>
          <select id="framework"><option>net10.0</option></select>
        </label>`,
    })}
    <nav class="lensbar" aria-label="Scope and lenses">
      <div class="scope-switch">
        <button>Package</button>
        <button class="active">Types</button>
        <button>Member</button>
      </div>
      <span class="lens-separator"></span>
      <button class="lens active">API</button>
      <button class="lens">Metadata</button>
      <button class="lens">Source</button>
    </nav>
    <main class="workspace">
      <section class="type-browser">
        <header class="browser-head">Target inventory</header>
      </section>
      <section class="detail-pane">
        <header class="detail-head">Target selector</header>
        <article class="detail-scroll">
          <h1>System.Text.Json.JsonSerializer</h1>
        </article>
      </section>
    </main>
  </div>`;
