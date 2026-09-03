import type { PackageControlPackage } from "./package-controls.ts";

export interface WorkspaceSummary<TPackage extends PackageControlPackage> {
  id: string;
  name: string;
  isDefault: boolean;
  packages: readonly TPackage[];
  activePackageKey: string | null;
}

export interface WorkspaceSubjectRenderOptions<
  TPackage extends PackageControlPackage,
> {
  workspaces: readonly WorkspaceSummary<TPackage>[];
  selectedWorkspaceId: string;
  maximumWorkspaces: number;
  escapeHtml: (value: unknown) => string;
}

export interface WorkspaceViewRenderOptions<
  TPackage extends PackageControlPackage,
> {
  workspace: WorkspaceSummary<TPackage>;
  escapeHtml: (value: unknown) => string;
  packageIdentityKey: (pkg: TPackage) => string;
}

export interface WorkspaceSubjectBindingActions {
  onSelect: (workspaceId: string) => void;
  onCreate: () => void;
  onRemove: (workspaceId: string) => void;
  onClosePackage: (packageKey: string) => void;
}

function packageSummary(
  packages: readonly PackageControlPackage[],
): string {
  if (packages.length === 0) return "No packages loaded";
  const first = packages[0]!;
  if (packages.length === 1) return first.id;
  return `${first.id} + ${packages.length - 1}`;
}

export function renderWorkspaceSubject<
  TPackage extends PackageControlPackage,
>(
  options: WorkspaceSubjectRenderOptions<TPackage>,
): string {
  const {
    workspaces,
    selectedWorkspaceId,
    maximumWorkspaces,
    escapeHtml,
  } = options;
  const rows = workspaces.map(workspace => {
    const active = workspace.id === selectedWorkspaceId;
    return `<button class="workspace-card${active ? " active" : ""}" type="button" data-workspace="${escapeHtml(workspace.id)}" aria-current="${active ? "true" : "false"}">
      <strong>${escapeHtml(workspace.name)}</strong>
      <span>${escapeHtml(packageSummary(workspace.packages))}</span>
      <small>${workspace.packages.length} coordinate${workspace.packages.length === 1 ? "" : "s"}</small>
    </button>`;
  }).join("");
  const createDisabled = workspaces.length >= maximumWorkspaces;
  return `<aside class="type-browser workspace-nav">
    <header class="browser-head">
      <span>WORKSPACES</span>
      <small>${workspaces.length}</small>
    </header>
    <div class="workspace-list">${rows}</div>
    <footer class="workspace-nav-actions">
      <button type="button" data-workspace-create${createDisabled ? " disabled" : ""}>New workspace</button>
    </footer>
  </aside>`;
}

export function renderWorkspaceView<
  TPackage extends PackageControlPackage,
>(
  options: WorkspaceViewRenderOptions<TPackage>,
): string {
  const {
    workspace,
    escapeHtml,
    packageIdentityKey,
  } = options;
  const loadedCoordinates = workspace.packages.map(item => {
    const key = packageIdentityKey(item);
    const active = workspace.activePackageKey === key;
    const label = `${item.id} ${item.version} ${item.activeFramework}`;
    return `<li class="${active ? "active" : ""}">
      <span>${item.isRuntimePack ? "Platform" : "NuGet package"}</span>
      <strong>${escapeHtml(item.id)}</strong>
      <small>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</small>
      ${item.isRuntimePack
        ? ""
        : `<button type="button" data-workspace-package-close="${escapeHtml(key)}" aria-label="Close ${escapeHtml(label)}">Close</button>`}
    </li>`;
  }).join("");
  const packageCount = workspace.packages.length;
  const summary = packageCount === 0
    ? "This workspace is ready for packages. Search for a package to begin."
    : `${packageCount} loaded coordinate${packageCount === 1 ? "" : "s"} available for analysis.`;
  return `<header class="type-heading workspace-heading">
    <div class="type-badge">W</div>
    <div>
      <div class="type-namespace">Inspection workspace</div>
      <h1>${escapeHtml(workspace.name)}</h1>
      <code class="type-signature">${escapeHtml(packageSummary(workspace.packages))}</code>
    </div>
  </header>
  <div class="workspace-overview">
    <div class="workspace-introduction">
      <p>${escapeHtml(summary)}</p>
      ${workspace.isDefault
        ? ""
        : `<button type="button" data-workspace-remove="${escapeHtml(workspace.id)}">Remove workspace</button>`}
    </div>
    <section class="document-section workspace-section">
      <div class="section-title"><h2>Packages</h2><span>${packageCount} coordinate${packageCount === 1 ? "" : "s"}</span></div>
      ${packageCount === 0
        ? '<p class="workspace-empty">No packages are loaded in this workspace.</p>'
        : `<ul class="workspace-detail-list loaded">${loadedCoordinates}</ul>`}
    </section>
  </div>`;
}

export function bindWorkspaceSubject(
  root: ParentNode,
  actions: WorkspaceSubjectBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-workspace]").forEach(button =>
    button.addEventListener("click", () => {
      const id = button.dataset.workspace;
      if (id !== undefined) actions.onSelect(id);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-create]").forEach(button =>
    button.addEventListener("click", actions.onCreate));
  root.querySelectorAll<HTMLElement>("[data-workspace-remove]").forEach(button =>
    button.addEventListener("click", () => {
      const id = button.dataset.workspaceRemove;
      if (id !== undefined) actions.onRemove(id);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-package-close]").forEach(
    button => button.addEventListener("click", () => {
      const key = button.dataset.workspacePackageClose;
      if (key !== undefined) actions.onClosePackage(key);
    }));
}

export function focusWorkspace(
  root: ParentNode,
  workspaceId: string,
): boolean {
  for (const button of root.querySelectorAll<HTMLElement>("[data-workspace]")) {
    if (button.dataset.workspace !== workspaceId) continue;
    button.focus();
    return true;
  }
  return false;
}
