import { packageIdentityKey } from "./data.ts";
import type { PackageControlPackage } from "./package-controls.ts";
import type {
  BrowserWorkspacePackageOccurrence,
} from "./facades/inspect-web-package.d.ts";
import {
  isProductHomeDemoId,
  type ProductHomeDemoCatalogEntry,
  type ProductHomeDemoId,
} from "./product-home-demos.ts";

export interface WorkspaceSubjectRenderOptions {
  packageCount: number;
  selected: boolean;
  escapeHtml: (value: unknown) => string;
}

export interface WorkspaceViewRenderOptions {
  occurrences: readonly BrowserWorkspacePackageOccurrence[];
  packages: readonly PackageControlPackage[];
  demos: readonly ProductHomeDemoCatalogEntry[];
  demoError: string;
  loading: boolean;
  error: string;
  escapeHtml: (value: unknown) => string;
}

export interface WorkspaceSubjectBindingActions {
  onSelect: () => void;
  onActivate: (action: string) => void;
  onAdd: () => void;
  onRemove: (packageKey: string) => void;
  onClear: () => void;
  onDemo: (demo: ProductHomeDemoId) => void;
  onRetry: () => void;
}

export interface WorkspaceOccurrenceVisibility {
  engineReady: boolean;
  scope: string;
  explorerOpen: boolean;
  creditsOpen: boolean;
  packageQueryOpen: boolean;
  loading: boolean;
  error: string;
  home: boolean;
  hasPackage: boolean;
}

export type WorkspaceFocusTarget =
  | { kind: "workspace" }
  | { kind: "demo"; id: string }
  | { kind: "add" }
  | { kind: "clear" }
  | { kind: "remove"; position: number };

export function workspaceOccurrenceActionsAreVisible(
  state: WorkspaceOccurrenceVisibility,
): boolean {
  return state.engineReady
    && state.scope === "workspace"
    && !state.explorerOpen
    && !state.creditsOpen
    && !state.packageQueryOpen
    && !state.loading
    && !state.error
    && !state.home
    && state.hasPackage;
}

export function renderWorkspaceSubject(
  options: WorkspaceSubjectRenderOptions,
): string {
  const {
    packageCount,
    selected,
    escapeHtml,
  } = options;
  return `<aside class="type-browser workspace-nav">
    <header class="browser-head"><span>WORKSPACE</span></header>
    <div class="workspace-list">
      <button class="workspace-card${selected ? " active" : ""}" type="button" data-workspace-default aria-current="${selected ? "true" : "false"}">
        <strong>Workspace</strong>
        <span>${escapeHtml(packageCount)} loaded coordinate${packageCount === 1 ? "" : "s"}</span>
        <small>Browser session</small>
      </button>
    </div>
  </aside>`;
}

export function renderWorkspaceView(
  options: WorkspaceViewRenderOptions,
): string {
  const {
    occurrences,
    packages,
    demos,
    demoError,
    loading,
    error,
    escapeHtml,
  } = options;
  const packageRows = packages
    .filter(pkg => !pkg.isRuntimePack)
    .map((pkg, position) => {
    const occurrence = occurrences.find(candidate =>
      candidate.package.toLowerCase() === pkg.id.toLowerCase()
      && candidate.version.toLowerCase() === pkg.version.toLowerCase()
      && candidate.framework.toLowerCase()
        === pkg.activeFramework.toLowerCase());
    const inspectAction = occurrence
      ? `<button type="button" data-workspace-activate="${escapeHtml(occurrence.action)}" aria-label="Inspect ${escapeHtml(pkg.id)}">Inspect</button>`
      : `<button type="button" disabled aria-label="Inspect ${escapeHtml(pkg.id)} when its action is ready">Inspect</button>`;
    return `<li class="workspace-occurrence-row">
      <div class="workspace-occurrence">
        <span>NuGet package</span>
        <strong>${escapeHtml(pkg.id)}</strong>
        <small>${escapeHtml(pkg.version)} · ${escapeHtml(pkg.activeFramework)}</small>
      </div>
      ${inspectAction}
      <button type="button" data-workspace-remove="${escapeHtml(packageIdentityKey(pkg))}" data-workspace-remove-position="${position}" aria-label="Remove ${escapeHtml(pkg.id)} from the Workspace">Remove</button>
    </li>`;
  }).join("");
  const platformRows = packages.filter(item => item.isRuntimePack).map(item =>
    `<li>
      <span>Platform</span>
      <strong>${escapeHtml(item.id)}</strong>
      <small>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</small>
    </li>`).join("");
  const rows = `${packageRows}${platformRows}`;
  const packageList = rows
    ? `<ul class="workspace-detail-list loaded">${rows}</ul>`
    : `<p class="workspace-empty">No packages are loaded in this Workspace.</p>`;
  const occurrenceState = !packageRows
    ? ""
    : loading
      ? `<p class="workspace-empty">Reading package activation actions…</p>`
      : error
        ? `<div class="workspace-empty">
            <p>${escapeHtml(error)}</p>
            <button type="button" data-workspace-retry>Retry package actions</button>
          </div>`
        : "";
  const demoRows = demos.map(demo =>
    `<li class="workspace-demo-row">
      <div>
        <strong>${escapeHtml(demo.title)}</strong>
        <small>${escapeHtml(demo.summary)}</small>
      </div>
      <button type="button" data-workspace-demo="${escapeHtml(demo.id)}" aria-label="Open demo ${escapeHtml(demo.title)}">Open demo</button>
    </li>`).join("");
  const demoContent = demoError
    ? `<p class="workspace-empty">${escapeHtml(demoError)}</p>`
    : demoRows
    ? `<ul class="workspace-demo-list">${demoRows}</ul>`
    : `<p class="workspace-empty">No product demos are available.</p>`;
  return `<header class="type-heading workspace-heading">
    <div class="type-badge">W</div>
    <div>
      <div class="type-namespace">Workspace</div>
      <h1>Workspace</h1>
      <code class="type-signature">${packages.length} loaded coordinate${packages.length === 1 ? "" : "s"}</code>
    </div>
    <div class="workspace-editor-actions">
      <button type="button" data-workspace-add>Add package</button>
      <button type="button" data-workspace-clear ${packages.length ? "" : "disabled"}>Clear</button>
    </div>
  </header>
  <div class="workspace-overview">
    <section class="document-section workspace-section">
      <div class="section-title"><h2>Demos</h2><span>${demos.length} available</span></div>
      <p>Open a product demo to replace this Workspace with its packages and initial view.</p>
      ${demoContent}
    </section>
    <section class="document-section workspace-section">
      <div class="section-title"><h2>Packages</h2><span>${packages.length} coordinate${packages.length === 1 ? "" : "s"}</span></div>
      <p>Open replaces the Workspace. Add preserves its exact packages.</p>
      ${packageList}
      ${occurrenceState}
    </section>
  </div>`;
}

export function bindWorkspaceSubject(
  root: ParentNode,
  actions: WorkspaceSubjectBindingActions,
): void {
  root.querySelector<HTMLElement>("[data-workspace-default]")
    ?.addEventListener("click", actions.onSelect);
  root.querySelectorAll<HTMLElement>("[data-workspace-activate]").forEach(button =>
    button.addEventListener("click", () => {
      const action = button.dataset.workspaceActivate;
      if (action !== undefined) actions.onActivate(action);
    }));
  root.querySelector<HTMLElement>("[data-workspace-add]")
    ?.addEventListener("click", actions.onAdd);
  root.querySelectorAll<HTMLElement>("[data-workspace-remove]").forEach(button =>
    button.addEventListener("click", () => {
      const packageKey = button.dataset.workspaceRemove;
      if (packageKey) actions.onRemove(packageKey);
    }));
  root.querySelector<HTMLElement>("[data-workspace-clear]")
    ?.addEventListener("click", actions.onClear);
  root.querySelectorAll<HTMLElement>("[data-workspace-demo]").forEach(button =>
    button.addEventListener("click", () => {
      const demo = button.dataset.workspaceDemo;
      if (isProductHomeDemoId(demo)) actions.onDemo(demo);
    }));
  root.querySelector<HTMLElement>("[data-workspace-retry]")
    ?.addEventListener("click", actions.onRetry);
}

export function focusWorkspace(
  root: ParentNode,
): boolean {
  const button = root.querySelector<HTMLElement>("[data-workspace-default]");
  button?.focus();
  return Boolean(button);
}

export function captureWorkspaceFocus(
  element: HTMLElement | null,
): WorkspaceFocusTarget | null {
  const target = element?.closest<HTMLElement>(
    "[data-workspace-default], [data-workspace-demo], [data-workspace-add], [data-workspace-clear], [data-workspace-remove]");
  if (!target) return null;
  if (target.hasAttribute("data-workspace-default")) {
    return { kind: "workspace" };
  }
  if (target.hasAttribute("data-workspace-add")) {
    return { kind: "add" };
  }
  if (target.hasAttribute("data-workspace-clear")) {
    return { kind: "clear" };
  }
  const position = Number(target.dataset.workspaceRemovePosition);
  if (Number.isInteger(position) && position >= 0) {
    return { kind: "remove", position };
  }
  const demo = target.dataset.workspaceDemo;
  if (demo !== undefined) {
    return { kind: "demo", id: demo };
  }
  return null;
}

export function restoreWorkspaceFocus(
  root: ParentNode,
  target: WorkspaceFocusTarget,
): boolean {
  let element: HTMLElement | null = null;
  switch (target.kind) {
    case "workspace":
      element = root.querySelector<HTMLElement>("[data-workspace-default]");
      break;
    case "demo":
      element = [...root.querySelectorAll<HTMLElement>("[data-workspace-demo]")]
        .find(candidate => candidate.dataset.workspaceDemo === target.id)
        ?? null;
      break;
    case "add":
      element = root.querySelector<HTMLElement>("[data-workspace-add]");
      break;
    case "clear":
      element = root.querySelector<HTMLElement>(
        "[data-workspace-clear]:not(:disabled)");
      break;
    case "remove": {
      const removeButtons = [
        ...root.querySelectorAll<HTMLElement>("[data-workspace-remove]"),
      ];
      element = removeButtons[Math.min(
        target.position,
        removeButtons.length - 1)]
        ?? null;
      element ??= root.querySelector<HTMLElement>("[data-workspace-add]");
      break;
    }
  }
  element?.focus({ preventScroll: true });
  return element !== null;
}
