import type { PackageControlPackage } from "./package-controls.ts";
import type { PlatformNavigationState } from "./platform-subject.ts";
import type {
  NavigationPackagePresentationItem,
} from "./navigation-descriptor-presentation.ts";
import {
  packageIdentityKey,
  workspacePackageRemovalKey,
} from "./data.ts";
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
  type ProductHomeDemoId,
} from "./product-home-demos.ts";

export interface WorkspaceSubjectRenderOptions {
  workspaces: readonly WorkspaceSubjectItem[];
  escapeHtml: (value: unknown) => string;
}

export interface WorkspaceSubjectItem {
  id: string;
  label: string;
  packageCount: number;
  active: boolean;
  status?:
    | "Active"
    | "Activate"
    | "Activating"
    | "Activation failed"
    | "Closing";
  deletionDisabled?: boolean;
}

export interface WorkspaceViewRenderOptions {
  canAddPackage?: boolean;
  savedWorkspaces?: SavedWorkspacesView;
  occurrences: readonly BrowserWorkspacePackageOccurrence[];
  navigationPackages?: readonly NavigationPackagePresentationItem[];
  packages: readonly PackageControlPackage[];
  platform?: PlatformNavigationState | null;
  frameworkLibraries?: readonly {
    name: string;
    assembly: string;
    pack: string;
    version: string;
    framework: string;
    source: ".NET" | "ASP.NET Core";
  }[];
  loading: boolean;
  error: string;
  escapeHtml: (value: unknown) => string;
}

export interface WorkspaceSubjectBindingActions {
  onSelect: (workspaceId: string) => void;
  onActivateWorkspace: (workspaceId: string) => void;
  onDeleteWorkspace: (workspaceId: string) => void;
  onActivate: (action: string) => void;
  onProductNavigationAction?: (token: string) => void;
  onDemo: (demo: ProductHomeDemoId) => void;
  onRetry: () => void;
  onRemove?: (key: string) => void;
  onAddPackage?: () => void;
  onPlatform?: () => void;
  onFrameworkLibrary?: (
    assembly: string,
    pack: string,
    framework: string,
    version: string,
  ) => void;
}

export interface WorkspaceOccurrenceVisibility {
  engineReady: boolean;
  scope: string;
  explorerOpen: boolean;
  creditsOpen: boolean;
  packageQueryOpen: boolean;
  packageActivityOpen: boolean;
  loading: boolean;
  error: string;
  home: boolean;
  hasPackage: boolean;
}

export type WorkspaceFocusTarget =
  | SavedWorkspaceFocus
  | { kind: "workspace"; id: string }
  | { kind: "delete-workspace"; id: string; index: number }
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
    && !state.packageActivityOpen
    && !state.loading
    && !state.error
    && !state.home
    && state.hasPackage;
}

export function renderWorkspaceSubject(
  options: WorkspaceSubjectRenderOptions,
): string {
  const { workspaces, escapeHtml } = options;
  const rows = workspaces.map(workspace => {
    const count = workspace.packageCount;
    const status = workspace.status
      ?? (workspace.active ? "Active" : "Activate");
    const action = workspace.active
      ? `data-workspace-select="${escapeHtml(workspace.id)}"`
      : `data-workspace-switch="${escapeHtml(workspace.id)}"`;
    return `<div class="workspace-row">
      <button class="workspace-card${workspace.active ? " active" : ""}" type="button" ${action} aria-current="${workspace.active ? "true" : "false"}"${status === "Activating" || status === "Closing" ? " disabled" : ""}>
        <strong>${escapeHtml(workspace.label)}</strong>
        <span>${escapeHtml(count)} loaded coordinate${count === 1 ? "" : "s"}</span>
        <small>${status}</small>
      </button>
      ${packageRemoveButton(
        "data-workspace-delete",
        workspace.id,
        `Delete ${workspace.label}`,
        escapeHtml,
        workspace.deletionDisabled)}
    </div>`;
  }).join("");
  return `<aside class="type-browser workspace-nav">
    <header class="browser-head"><span>WORKSPACES</span></header>
    <div class="workspace-list">
      ${rows || '<p class="workspace-empty">No live Workspaces.</p>'}
    </div>
  </aside>`;
}

export function renderWorkspaceView(
  options: WorkspaceViewRenderOptions,
): string {
  const {
    occurrences,
    navigationPackages,
    packages,
    loading,
    error,
    escapeHtml,
  } = options;
  const packageRows = navigationPackages
    ? navigationPackages.map(item => {
      const framework = item.framework
        ?? item.summary.selectedCompileFramework
        ?? "";
      const key = workspacePackageRemovalKey({
        id: item.package,
        version: item.version,
        activeFramework: framework,
        runtimeIdentifier: item.runtimeIdentifier,
      });
      const runtimeIdentifier = item.runtimeIdentifier
        ? ` · ${item.runtimeIdentifier}`
        : "";
      const status = item.subject.state.toLowerCase() === "available"
        ? ""
        : ` · ${item.subject.state}`;
      const evidence = item.subject.evidence ?? item.realizationFailure;
      const label =
        `${item.package} ${item.version} ${framework}${runtimeIdentifier}`;
      const accessibleLabel = `Inspect ${label}`
        + (item.subject.current ? ". Current" : "")
        + (status ? `. ${item.subject.state}` : "")
        + (evidence ? `. ${evidence}` : "");
      return `<li class="workspace-occurrence-row" data-navigation-order="${item.order}">
        <button class="workspace-occurrence${item.subject.current ? " active" : ""}" type="button" ${item.subject.action ? `data-product-navigation-action="${escapeHtml(item.subject.action)}"` : item.subject.current ? "" : "disabled"} data-product-navigation-id="${escapeHtml(item.subject.identity ?? "")}" data-navigation-state="${escapeHtml(item.subject.state)}"${item.subject.current ? ' aria-current="page"' : ""} aria-label="${escapeHtml(accessibleLabel)}">
          <span>NuGet package</span>
          <strong>${escapeHtml(item.package)}</strong>
          <small>${escapeHtml(item.version)} · ${escapeHtml(framework)}${escapeHtml(runtimeIdentifier)}${item.subject.current ? " · Current" : ""}${escapeHtml(status)}${evidence ? ` · ${escapeHtml(evidence)}` : ""}</small>
        </button>
        ${packageRemoveButton("data-workspace-remove", key, `Remove ${label} from Workspace`, escapeHtml)}
      </li>`;
    }).join("")
    : packages.filter(item => !item.isRuntimePack).map(item => {
    const key = workspacePackageRemovalKey(item);
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
  const platform = options.platform;
  const platformRows = platform ? `<li class="workspace-occurrence-row">
    <button class="workspace-occurrence" type="button" data-workspace-platform aria-label="Inspect Platform ${escapeHtml(platform.tfm)} ${escapeHtml(platform.version)}">
      <span>Platform</span><strong>.NET Platform</strong>
      <small>${escapeHtml(platform.version)} · ${escapeHtml(platform.tfm)}</small>
    </button></li>` : "";
  const frameworkLibraryRows = (options.frameworkLibraries ?? []).map(library =>
    `<li class="workspace-occurrence-row">
      <button class="workspace-occurrence" type="button"
        data-workspace-framework-library="${escapeHtml(library.assembly)}"
        data-workspace-framework-pack="${escapeHtml(library.pack)}"
        data-workspace-framework="${escapeHtml(library.framework)}"
        data-workspace-framework-version="${escapeHtml(library.version)}"
        aria-label="Inspect ${escapeHtml(library.name)}">
        <span>${escapeHtml(library.source)} Library</span>
        <strong>${escapeHtml(library.name)}</strong>
        <small>${escapeHtml(library.version)} · ${escapeHtml(library.framework)}</small>
      </button>
    </li>`).join("");
  const rows = packageRows;
  const packageCount = navigationPackages?.length
    ?? packages.filter(item => !item.isRuntimePack).length;
  const coordinateCount = packageCount + (platform ? 1 : 0)
    + (options.frameworkLibraries?.length ?? 0);
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
  return `<header class="type-heading workspace-heading">
    <div class="type-badge">W</div>
    <div>
      <div class="type-namespace">Workspace</div>
      <h1>Workspace</h1>
      <code class="type-signature">${coordinateCount} loaded coordinate${coordinateCount === 1 ? "" : "s"}</code>
    </div>
    ${options.savedWorkspaces ? renderWorkspaceSaveButton(options.savedWorkspaces) : ""}
  </header>
  <div class="workspace-overview">
    ${options.savedWorkspaces ? renderSavedWorkspaces(options.savedWorkspaces, escapeHtml) : ""}
    <section class="document-section workspace-section">
      <div class="section-title"><h2>Packages</h2><span>${packageCount} coordinate${packageCount === 1 ? "" : "s"}</span>${options.canAddPackage === undefined ? "" : `<button class="workspace-add-package" type="button" data-workspace-add-package${options.canAddPackage ? "" : " disabled"}>Add package</button>`}</div>
      <p>Choose a package to inspect it, or remove it with the adjacent close button.</p>
      ${content}
    </section>
    ${frameworkLibraryRows ? `<section class="document-section workspace-section"><div class="section-title"><h2>Libraries</h2></div><ul class="workspace-detail-list loaded">${frameworkLibraryRows}</ul></section>` : ""}
    ${platformRows ? `<section class="document-section workspace-section"><div class="section-title"><h2>Platform</h2></div><ul class="workspace-detail-list loaded">${platformRows}</ul></section>` : ""}
  </div>`;
}

export function bindWorkspaceSubject(
  root: ParentNode,
  actions: WorkspaceSubjectBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-workspace-select]").forEach(button =>
    button.addEventListener("click", () => {
      const workspaceId = button.dataset.workspaceSelect;
      if (workspaceId !== undefined) actions.onSelect(workspaceId);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-switch]").forEach(button =>
    button.addEventListener("click", () => {
      const workspaceId = button.dataset.workspaceSwitch;
      if (workspaceId !== undefined) actions.onActivateWorkspace(workspaceId);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-delete]").forEach(button =>
    button.addEventListener("click", () => {
      const workspaceId = button.dataset.workspaceDelete;
      if (workspaceId !== undefined) actions.onDeleteWorkspace(workspaceId);
    }));
  root.querySelectorAll<HTMLElement>("[data-workspace-activate]").forEach(button =>
    button.addEventListener("click", () => {
      const action = button.dataset.workspaceActivate;
      if (action !== undefined) actions.onActivate(action);
    }));
  root.querySelectorAll<HTMLElement>(
    "[data-product-navigation-action]",
  ).forEach(button => button.addEventListener("click", () => {
    const token = button.dataset.productNavigationAction;
    if (token !== undefined) actions.onProductNavigationAction?.(token);
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
  root.querySelector<HTMLElement>("[data-workspace-platform]")
    ?.addEventListener("click", () => actions.onPlatform?.());
  const frameworkLibrary =
    root.querySelector<HTMLElement>("[data-workspace-framework-library]");
  frameworkLibrary?.addEventListener("click", () => {
    const assembly = frameworkLibrary.dataset.workspaceFrameworkLibrary;
    const pack = frameworkLibrary.dataset.workspaceFrameworkPack;
    const framework = frameworkLibrary.dataset.workspaceFramework;
    const version = frameworkLibrary.dataset.workspaceFrameworkVersion;
    if (assembly !== undefined && pack !== undefined
      && framework !== undefined && version !== undefined) {
      actions.onFrameworkLibrary?.(assembly, pack, framework, version);
    }
  });
  root.querySelectorAll<HTMLElement>("[data-workspace-remove]").forEach(button =>
    button.addEventListener("click", () => {
      const key = button.dataset.workspaceRemove;
      if (key !== undefined) actions.onRemove?.(key);
    }));
}

export function focusWorkspace(
  root: ParentNode,
): boolean {
  const button = root.querySelector<HTMLElement>("[data-workspace-select]")
    ?? root.querySelector<HTMLElement>("[data-workspace-switch]");
  button?.focus();
  return Boolean(button);
}

export function captureWorkspaceFocus(
  element: HTMLElement | null,
): WorkspaceFocusTarget | null {
  const savedFocus = captureSavedWorkspaceFocus(element);
  if (savedFocus) return savedFocus;
  const target = element?.closest<HTMLElement>(
    "[data-workspace-select], [data-workspace-switch], [data-workspace-delete], [data-workspace-demo], [data-workspace-remove], [data-workspace-add-package]");
  if (!target) return null;
  const workspaceId =
    target.dataset.workspaceSelect ?? target.dataset.workspaceSwitch;
  if (workspaceId !== undefined) {
    return { kind: "workspace", id: workspaceId };
  }
  if (target.dataset.workspaceDelete !== undefined) {
    return {
      kind: "delete-workspace",
      id: target.dataset.workspaceDelete,
      index: [...target.ownerDocument.querySelectorAll("[data-workspace-delete]")]
        .indexOf(target),
    };
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
      element = [...root.querySelectorAll<HTMLElement>(
        "[data-workspace-select], [data-workspace-switch]")]
        .find(candidate =>
          (candidate.dataset.workspaceSelect
            ?? candidate.dataset.workspaceSwitch) === target.id)
        ?? null;
      break;
    case "delete-workspace": {
      const buttons =
        [...root.querySelectorAll<HTMLElement>("[data-workspace-delete]")];
      element = buttons.find(button => button.dataset.workspaceDelete === target.id)
        ?? buttons[Math.min(target.index, buttons.length - 1)]
        ?? root.querySelector<HTMLElement>(
          "[data-workspace-select], [data-workspace-switch], h1");
      if (element?.tagName === "H1") element.tabIndex = -1;
      break;
    }
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
