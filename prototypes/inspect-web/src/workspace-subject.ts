import type { PackageControlPackage } from "./package-controls.ts";
import { packageIdentityKey } from "./data.ts";
import { packageRemoveButton } from "./package-removal.ts";
import type { SavedWorkspaceFocus } from "./saved-workspaces.ts";
import {
  captureSavedWorkspaceFocus,
  renderSavedWorkspaces,
  renderWorkspaceSaveButton,
  restoreSavedWorkspaceFocus,
  type SavedWorkspacesView,
} from "./saved-workspaces-view.ts";
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
  canAddPackage?: boolean;
  savedWorkspaces?: SavedWorkspacesView;
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
  onDemo: (demo: ProductHomeDemoId) => void;
  onRetry: () => void;
  onRemove?: (key: string) => void;
  onAddPackage?: () => void;
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
  | SavedWorkspaceFocus
  | { kind: "workspace" }
  | { kind: "add-package" }
  | { kind: "remove"; key: string; index: number }
  | { kind: "demo"; id: string };

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
  const packageRows = packages.filter(item => !item.isRuntimePack).map(item => {
    const key = packageIdentityKey(item);
    const occurrence = !loading && !error
      ? occurrences.find(candidate => packageIdentityKey({
        id: candidate.package,
        version: candidate.version,
        activeFramework: candidate.framework,
      }) === key)
      : undefined;
    const label = `${item.id} ${item.version} ${item.activeFramework}`;
    return `<li class="workspace-occurrence-row">
      <button class="workspace-occurrence" type="button" ${occurrence ? `data-workspace-activate="${escapeHtml(occurrence.action)}"` : "disabled"} aria-label="Inspect ${escapeHtml(label)}">
        <span>NuGet package</span>
        <strong>${escapeHtml(item.id)}</strong>
        <small>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</small>
      </button>
      ${packageRemoveButton("data-workspace-remove", key, `Remove ${label} from Workspace`, escapeHtml)}
    </li>`;
  }).join("");
  const platformRows = packages.filter(item => item.isRuntimePack).map(item =>
    `<li>
      <span>Platform</span>
      <strong>${escapeHtml(item.id)}</strong>
      <small>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</small>
    </li>`).join("");
  const rows = `${packageRows}${platformRows}`;
  const status = loading
    ? `<p class="workspace-empty">Reading Workspace package occurrences…</p>`
    : error
      ? `<div class="workspace-empty">
          <p>${escapeHtml(error)}</p>
          <button type="button" data-workspace-retry>Retry</button>
        </div>`
      : "";
  const content = status + (rows
    ? `<ul class="workspace-detail-list loaded">${rows}</ul>`
    : `<p class="workspace-empty">No packages are loaded in this Workspace.</p>`);
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
    ${options.savedWorkspaces ? renderWorkspaceSaveButton(options.savedWorkspaces) : ""}
  </header>
  <div class="workspace-overview">
    ${options.savedWorkspaces ? renderSavedWorkspaces(options.savedWorkspaces, escapeHtml) : ""}
    <section class="document-section workspace-section">
      <div class="section-title"><h2>Demos</h2><span>${demos.length} available</span></div>
      <p>Open a product demo to replace this Workspace with its packages and initial view.</p>
      ${demoContent}
    </section>
    <section class="document-section workspace-section">
      <div class="section-title"><h2>Packages</h2><span>${packages.length} coordinate${packages.length === 1 ? "" : "s"}</span>${options.canAddPackage === undefined ? "" : `<button class="workspace-add-package" type="button" data-workspace-add-package${options.canAddPackage ? "" : " disabled"}>Add package</button>`}</div>
      <p>Choose a package to inspect it, or remove it with the adjacent close button.</p>
      ${content}
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
  root.querySelectorAll<HTMLElement>("[data-workspace-demo]").forEach(button =>
    button.addEventListener("click", () => {
      const demo = button.dataset.workspaceDemo;
      if (isProductHomeDemoId(demo)) actions.onDemo(demo);
    }));
  root.querySelector<HTMLElement>("[data-workspace-retry]")
    ?.addEventListener("click", actions.onRetry);
  root.querySelector<HTMLElement>("[data-workspace-add-package]")
    ?.addEventListener("click", () => actions.onAddPackage?.());
  root.querySelectorAll<HTMLElement>("[data-workspace-remove]").forEach(button =>
    button.addEventListener("click", () => {
      const key = button.dataset.workspaceRemove;
      if (key !== undefined) actions.onRemove?.(key);
    }));
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
  const savedFocus = captureSavedWorkspaceFocus(element);
  if (savedFocus) return savedFocus;
  const target = element?.closest<HTMLElement>(
    "[data-workspace-default], [data-workspace-demo], [data-workspace-remove], [data-workspace-add-package]");
  if (!target) return null;
  if (target.hasAttribute("data-workspace-default")) {
    return { kind: "workspace" };
  }
  if (target.hasAttribute("data-workspace-add-package")) {
    return { kind: "add-package" };
  }
  if (target.dataset.workspaceRemove !== undefined) {
    return {
      kind: "remove",
      key: target.dataset.workspaceRemove,
      index: [...target.ownerDocument.querySelectorAll("[data-workspace-remove]")]
        .indexOf(target),
    };
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
    case "remove": {
      const buttons = [...root.querySelectorAll<HTMLElement>("[data-workspace-remove]")];
      element = buttons.find(button => button.dataset.workspaceRemove === target.key)
        ?? buttons[Math.min(target.index, buttons.length - 1)]
        ?? root.querySelector<HTMLElement>("h1");
      if (element?.tagName === "H1") element.tabIndex = -1;
      break;
    }
    case "workspace":
      element = root.querySelector<HTMLElement>("[data-workspace-default]");
      break;
    case "add-package":
      element = root.querySelector<HTMLElement>("[data-workspace-add-package]");
      break;
    case "demo":
      element = [...root.querySelectorAll<HTMLElement>("[data-workspace-demo]")]
        .find(candidate => candidate.dataset.workspaceDemo === target.id)
        ?? null;
      break;
    default:
      return restoreSavedWorkspaceFocus(root, target);
  }
  element?.focus({ preventScroll: true });
  return element !== null;
}
