import { renderContentNavigationCloseButton } from "./content-frame.ts";

// DOM bindings for package-level navigation surfaces. The application root owns
// package, filter, graph, and inspection state transitions behind these callbacks.

export interface PackageDependencyBindingActions {
  onDependencyLoad: (id: string, version: string) => void;
  onDependencyOpen: (packageKey: string) => void;
}

export interface PackagePerformanceTarget {
  stableSelector: string;
  assembly: string;
  typeId: string;
}

export interface PackageViewBindingActions
  extends PackageDependencyBindingActions {
  onDependencyGroupSelect: (index: number) => void;
  onPruningEvaluate: () => void;
  onPruningFamilySelect: (family: string) => void;
  onGraphTypeSelect: (typeId: string) => void;
  onKindJump: (kind: string) => void;
  onLibraryScopeSelect: (
    library: string | undefined,
    kind: string,
  ) => void;
  onNamespaceJump: (namespace: string) => void;
  onPerformanceMemberSelect: (target: PackagePerformanceTarget) => void;
}

export interface PackageNavOptions {
  frameworks: readonly string[];
  activeFramework: string;
  escapeHtml: (value: unknown) => string;
}

export function renderPackageNav(options: PackageNavOptions): string {
  const { frameworks, activeFramework, escapeHtml } = options;
  return `
    <aside id="content-navigation-pane" class="type-browser package-framework-nav" aria-label="Frameworks">
      <div class="browser-head">
        <div>
          <span class="pane-label">TARGET FRAMEWORKS</span>
          <span class="result-count">${frameworks.length}</span>
        </div>
        ${renderContentNavigationCloseButton()}
      </div>
      <div class="type-list package-framework-list" role="group" aria-label="Target framework navigation" tabindex="-1" data-nav-scope="frameworks" data-nav-selection="${activeFramework ? `framework:${escapeHtml(activeFramework)}` : ""}">
        ${frameworks.map(framework => {
          const selected = framework === activeFramework;
          return `<button type="button" class="type-row package-framework-row ${selected ? "selected" : ""}" data-package-framework="${escapeHtml(framework)}"${selected ? ' aria-current="page"' : ""} title="Use ${escapeHtml(framework)}">
            <span class="kind-icon">T</span>
            <span class="type-name">${escapeHtml(framework)}</span>
            <small>${selected ? "current" : "available"}</small>
          </button>`;
        }).join("") || '<div class="empty-list">No target frameworks are available for this package version.</div>'}
      </div>
      <footer class="pane-footer"><span>choose a TFM</span><span>↵ load</span></footer>
    </aside>`;
}

export function bindPackageDependencyList(
  root: ParentNode,
  actions: PackageDependencyBindingActions,
) {
  root.querySelectorAll<HTMLElement>("[data-dep-open]").forEach(button =>
    button.onclick = () => {
      const key = button.dataset.depOpen;
      if (key) actions.onDependencyOpen(key);
    });
  root.querySelectorAll<HTMLElement>("[data-dep-load]").forEach(button =>
    button.onclick = () => {
      const id = button.dataset.depLoad;
      if (id) {
        actions.onDependencyLoad(id, button.dataset.depVersion || "");
      }
    });
}

export function bindPackageView(
  root: ParentNode,
  actions: PackageViewBindingActions,
) {
  root.querySelectorAll<HTMLElement>("[data-dep-group]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onDependencyGroupSelect(Number(button.dataset.depGroup))));
  root.querySelectorAll<HTMLSelectElement>("[data-pruning-family]").forEach(select =>
    select.addEventListener(
      "change",
      () => actions.onPruningFamilySelect(select.value)));
  root.querySelectorAll<HTMLElement>("[data-pruning-evaluate]").forEach(button =>
    button.addEventListener("click", actions.onPruningEvaluate));
  bindPackageDependencyList(root, actions);
  root.querySelectorAll<HTMLElement>("[data-kind-jump]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onKindJump(button.dataset.kindJump ?? "")));
  root.querySelectorAll<HTMLElement>("[data-namespace-jump]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onNamespaceJump(button.dataset.namespaceJump ?? "")));
  root.querySelectorAll<HTMLElement>("[data-lib-scope]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onLibraryScopeSelect(
        button.dataset.libScope,
        button.dataset.libKind || "")));
  root.querySelectorAll<HTMLElement>("[data-graph-type]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onGraphTypeSelect(button.dataset.graphType ?? "")));
  root.querySelectorAll<HTMLElement>("[data-perf-selector]").forEach(button =>
    button.addEventListener("click", () => actions.onPerformanceMemberSelect({
      stableSelector: button.dataset.perfSelector ?? "",
      assembly: button.dataset.perfAssembly ?? "",
      typeId: button.dataset.perfType ?? "",
    })));
}
