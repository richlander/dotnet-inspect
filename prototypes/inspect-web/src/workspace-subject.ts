import type { PackageControlPackage } from "./package-controls.ts";
import type {
  BrowserWorkspacePackageOccurrence,
} from "./inspect-web-engine.d.ts";

export interface WorkspaceSubjectRenderOptions {
  packageCount: number;
  selected: boolean;
  escapeHtml: (value: unknown) => string;
}

export interface WorkspaceViewRenderOptions {
  occurrences: readonly BrowserWorkspacePackageOccurrence[];
  packages: readonly PackageControlPackage[];
  loading: boolean;
  error: string;
  escapeHtml: (value: unknown) => string;
}

export interface WorkspaceSubjectBindingActions {
  onSelect: () => void;
  onActivate: (action: string) => void;
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
    <header class="browser-head"><span>WORKSPACES</span><small>1</small></header>
    <div class="workspace-list">
      <button class="workspace-card${selected ? " active" : ""}" type="button" data-workspace-default aria-current="${selected ? "true" : "false"}">
        <strong>Default Workspace</strong>
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
    loading,
    error,
    escapeHtml,
  } = options;
  const packageRows = occurrences.map(occurrence => {
    const label = `${occurrence.package} ${occurrence.version} ${occurrence.framework}`;
    return `<li class="workspace-occurrence-row">
      <button class="workspace-occurrence" type="button" data-workspace-activate="${escapeHtml(occurrence.action)}" aria-label="Inspect ${escapeHtml(label)}">
        <span>NuGet package</span>
        <strong>${escapeHtml(occurrence.package)}</strong>
        <small>${escapeHtml(occurrence.version)} · ${escapeHtml(occurrence.framework)}</small>
      </button>
    </li>`;
  }).join("");
  const platformRows = packages.filter(item => item.isRuntimePack).map(item =>
    `<li>
      <span>Platform</span>
      <strong>${escapeHtml(item.id)}</strong>
      <small>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</small>
    </li>`).join("");
  const rows = `${packageRows}${platformRows}`;
  const content = loading
    ? `<p class="workspace-empty">Reading Workspace package occurrences…</p>`
    : error
      ? `<div class="workspace-empty">
          <p>${escapeHtml(error)}</p>
          <button type="button" data-workspace-retry>Retry</button>
        </div>`
      : rows
        ? `<ul class="workspace-detail-list loaded">${rows}</ul>`
        : `<p class="workspace-empty">No packages are loaded in this Workspace.</p>`;
  return `<header class="type-heading workspace-heading">
    <div class="type-badge">W</div>
    <div>
      <div class="type-namespace">Workspace</div>
      <h1>Default Workspace</h1>
      <code class="type-signature">${packages.length} loaded coordinate${packages.length === 1 ? "" : "s"}</code>
    </div>
  </header>
  <div class="workspace-overview">
    <section class="document-section workspace-section">
      <div class="section-title"><h2>Packages</h2><span>${packages.length} coordinate${packages.length === 1 ? "" : "s"}</span></div>
      <p>Choose a package to inspect it. The Workspace keeps every loaded coordinate available.</p>
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
