import type { KeybindingRegistry } from "./keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "./workbench-keybindings.ts";

export interface PackageBarPackage {
  id: string;
  version: string;
  workspaceIndex?: number;
  activeFramework: string;
  isRuntimePack: boolean;
}

export interface PackageBarState {
  packages: readonly PackageBarPackage[];
  package: PackageBarPackage | null;
}

export interface ParsedPackageQuery {
  packageId: string;
  version: string;
  explicitVersion: boolean;
}

interface PackageBarOptions {
  keybindings: KeybindingRegistry;
  state: PackageBarState;
  escapeHtml: (value: unknown) => string;
  packageIdentityKey: (pkg: PackageBarPackage) => string;
  runtimePackPackage: () => PackageBarPackage | null;
  selectPackageTab: (pkg: PackageBarPackage) => void;
  closePackageTab: (packageKey: string) => void;
  openRuntimePack: () => void;
  selectFramework: (framework: string) => void;
  selectVersion: (version: string) => void;
}

export interface PackageSelectionActions {
  onFrameworkSelect: (framework: string) => void;
  onVersionSelect: (version: string) => void;
}

export function bindPackageSelections(
  root: ParentNode,
  actions: PackageSelectionActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-framework-chip]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onFrameworkSelect(button.dataset.frameworkChip ?? "")));
  const framework = root.querySelector<HTMLSelectElement>("#framework");
  framework?.addEventListener(
    "change",
    () => actions.onFrameworkSelect(framework.value));
  const version = root.querySelector<HTMLSelectElement>("#package-version");
  version?.addEventListener(
    "change",
    () => actions.onVersionSelect(version.value));
}

export function packageIdentityEquals(
  left: PackageBarPackage | null,
  right: PackageBarPackage | null,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): boolean {
  return Boolean(left && right && packageIdentityKey(left) === packageIdentityKey(right));
}

export function assignPackageWorkspaceIndex(
  packageModel: PackageBarPackage,
  packages: readonly PackageBarPackage[],
  replacedPackage: PackageBarPackage | null,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): number {
  if (packageModel.isRuntimePack) {
    packageModel.workspaceIndex = 0;
    return 0;
  }

  const existing = packages.find(item =>
    packageIdentityKey(item) === packageIdentityKey(packageModel));
  const retained = replacedPackage?.workspaceIndex ?? existing?.workspaceIndex;
  if (retained !== undefined && retained > 0) {
    packageModel.workspaceIndex = retained;
    return retained;
  }

  const used = new Set(packages.flatMap(item =>
    item.workspaceIndex !== undefined && item.workspaceIndex > 0
      ? [item.workspaceIndex]
      : []));
  let candidate = 1;
  while (used.has(candidate)) candidate++;
  packageModel.workspaceIndex = candidate;
  return candidate;
}

// Platform is the fixed, non-closable subject at index 0. It abstracts the .NET runtime
// packs behind one surface: when a pack is resident it activates it; otherwise it loads lazily.
export function platformTabHtml(
  runtimePack: PackageBarPackage | null,
  activePackage: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  const active = Boolean(
    runtimePack && activePackage && activePackage.id === runtimePack.id);
  const attr = runtimePack
    ? `data-package-key="${escapeHtml(packageIdentityKey(runtimePack))}"`
    : `data-platform-open="1"`;
  return `<button class="workspace-window platform${active ? " active" : ""}" ${attr} role="tab" aria-selected="${active}" title="0:Platform · .NET runtime libraries">
      <span class="workspace-index">0:</span>
      <span class="workspace-label">Platform</span>
      ${active ? '<span class="workspace-active-marker" aria-hidden="true">*</span>' : ""}
    </button>`;
}

export function packageTabHtml(
  item: PackageBarPackage,
  activePackage: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  const active = packageIdentityEquals(item, activePackage, packageIdentityKey);
  const key = escapeHtml(packageIdentityKey(item));
  const workspaceIndex = item.workspaceIndex ?? 1;
  return `
            <div class="workspace-window${active ? " active" : ""}" data-package-key="${key}" role="tab" aria-selected="${active}" tabindex="0" title="${workspaceIndex}:${escapeHtml(item.id)} · ${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}">
              <span class="workspace-index">${workspaceIndex}:</span>
              <span class="workspace-label">${escapeHtml(item.id)}</span>
              ${active ? '<span class="workspace-active-marker" aria-hidden="true">*</span>' : ""}
              ${active
                ? `<button class="workspace-close" data-package-close="${key}" type="button" aria-label="Close ${escapeHtml(item.id)}">×</button>`
                : ""}
            </div>`;
}

export function packageTabsHtml(
  state: PackageBarState,
  runtimePack: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  const usedIndexes = new Set(state.packages.flatMap(item =>
    item.workspaceIndex !== undefined && item.workspaceIndex > 0
      ? [item.workspaceIndex]
      : []));
  let fallbackIndex = 1;
  const tabs = state.packages
    .filter(item => !item.isRuntimePack)
    .map(item => {
      while (usedIndexes.has(fallbackIndex)) fallbackIndex++;
      const rendered = item.workspaceIndex === undefined
        ? { ...item, workspaceIndex: fallbackIndex++ }
        : item;
      return packageTabHtml(
        rendered,
        state.package,
        escapeHtml,
        packageIdentityKey);
    })
    .join("");
  return `${platformTabHtml(runtimePack, state.package, escapeHtml, packageIdentityKey)}${tabs}`;
}

export function packageBarHtml(
  state: PackageBarState,
  runtimePack: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  return `
        <div class="workspace-strip" role="tablist" aria-label="Open workspaces">
          ${packageTabsHtml(state, runtimePack, escapeHtml, packageIdentityKey)}
        </div>`;
}

// Only an empty query or "package@" with nothing after the "@" is rejected, matching the
// inline handler's original bounds exactly. A leading "@" (no package id, e.g. "@1.0.0")
// is preserved as-is rather than treated specially: separator > 0 is false, so the whole
// trimmed string — "@" included — becomes the package id, same as the handler this
// replaces. That is an existing quirk of the original code, not a rejection case.
export function parsePackageQuery(value: string): ParsedPackageQuery | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const separator = trimmed.lastIndexOf("@");
  if (separator === trimmed.length - 1) return null;

  const packageId = separator > 0 ? trimmed.slice(0, separator) : trimmed;
  const version = separator > 0 ? trimmed.slice(separator + 1) : "latest";
  return { packageId, version, explicitVersion: separator > 0 };
}

export function findPackageTabForQuery(
  state: PackageBarState,
  query: ParsedPackageQuery,
): PackageBarPackage | null {
  const idMatches = state.packages.filter(item =>
    !item.isRuntimePack
    && item.id.toLowerCase() === query.packageId.toLowerCase());
  const matches = query.explicitVersion
    ? idMatches.filter(item =>
      item.version.toLowerCase() === query.version.toLowerCase())
    : idMatches;

  if (state.package && matches.includes(state.package))
    return state.package;
  // Prefer the most recently retained matching tab when another package is active.
  return matches.at(-1) ?? null;
}

export function createPackageBar(options: PackageBarOptions) {
  const {
    keybindings,
    state,
    escapeHtml,
    packageIdentityKey,
    runtimePackPackage,
    selectPackageTab,
    closePackageTab,
    openRuntimePack,
    selectFramework,
    selectVersion,
  } = options;

  function html(): string {
    return packageBarHtml(state, runtimePackPackage(), escapeHtml, packageIdentityKey);
  }

  function bind(root: ParentNode): void {
    bindPackageSelections(root, {
      onFrameworkSelect: selectFramework,
      onVersionSelect: selectVersion,
    });
    root.querySelectorAll<HTMLElement>("[data-package-key]").forEach(tab => {
      const activate = () => {
        const target = state.packages.find(item => packageIdentityKey(item) === tab.dataset.packageKey);
        if (target) selectPackageTab(target);
      };
      tab.addEventListener("click", event => {
        if (event.target instanceof Element && event.target.closest("[data-package-close]")) return;
        activate();
      });
      keybindings.register({
        id: "workspace.activate",
        key: ["Enter", " "],
        allowExtraModifiers: true,
        priority: WORKBENCH_KEYBINDING_PRIORITY.element,
        run: () => {
          activate();
          return true;
        },
      }, tab);
    });

    root.querySelectorAll<HTMLButtonElement>("[data-package-close]").forEach(button =>
      button.addEventListener("click", event => {
        event.stopPropagation();
        const key = button.dataset.packageClose;
        if (key !== undefined) closePackageTab(key);
      }));

    root.querySelector<HTMLElement>("[data-platform-open]")?.addEventListener("click", () => openRuntimePack());

    // Keep the active indexed subject visible and map vertical wheel motion to the
    // horizontal strip when the subject count exceeds its allocation.
    const tabStrip = root.querySelector<HTMLElement>(".workspace-strip");
    if (tabStrip) {
      requestAnimationFrame(() =>
        tabStrip.querySelector(".workspace-window.active")?.scrollIntoView({ block: "nearest", inline: "nearest" }));
      tabStrip.addEventListener("wheel", event => {
        if (event.deltaY === 0) return;
        event.preventDefault();
        tabStrip.scrollLeft += event.deltaY;
      }, { passive: false });
    }
  }

  return {
    bind,
    html,
  };
}
