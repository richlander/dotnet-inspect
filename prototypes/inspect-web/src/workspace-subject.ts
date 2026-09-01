import type { PackageControlPackage } from "./package-controls.ts";

export interface WorkspaceSubjectRenderOptions {
  packages: readonly PackageControlPackage[];
  activePackage: PackageControlPackage | null;
  escapeHtml: (value: unknown) => string;
  packageIdentityKey: (pkg: PackageControlPackage) => string;
}

export interface WorkspaceSubjectBindingActions {
  onActivate: (packageKey: string) => void;
  onClose: (packageKey: string) => void;
}

export function renderWorkspaceSubject(
  options: WorkspaceSubjectRenderOptions,
): string {
  const {
    packages,
    activePackage,
    escapeHtml,
    packageIdentityKey,
  } = options;
  const coordinates = packages.filter(item => !item.isRuntimePack);
  const rows = coordinates.map(item => {
    const key = escapeHtml(packageIdentityKey(item));
    const active = Boolean(
      activePackage
      && packageIdentityKey(item) === packageIdentityKey(activePackage));
    return `<div class="workspace-coordinate${active ? " active" : ""}">
      <button type="button" data-workspace-package="${key}">
        <strong>${escapeHtml(item.id)}</strong>
        <span>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</span>
      </button>
      <button type="button" data-workspace-close="${key}" aria-label="Close ${escapeHtml(item.id)}">×</button>
    </div>`;
  }).join("");
  return `<aside class="type-browser workspace-nav">
    <header class="browser-head"><span>WORKSPACE</span><small>${coordinates.length} open</small></header>
    <div class="workspace-coordinate-list">${rows}</div>
  </aside>`;
}

export function bindWorkspaceSubject(
  root: ParentNode,
  actions: WorkspaceSubjectBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-workspace-package]").forEach(button =>
    button.addEventListener("click", () => {
      const key = button.dataset.workspacePackage;
      if (key !== undefined) actions.onActivate(key);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-close]").forEach(button =>
    button.addEventListener("click", () => {
      const key = button.dataset.workspaceClose;
      if (key !== undefined) actions.onClose(key);
    }));
}
