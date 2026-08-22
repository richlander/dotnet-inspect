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
  onGraphTypeSelect: (typeId: string) => void;
  onKindJump: (kind: string) => void;
  onLibraryScopeSelect: (
    library: string | undefined,
    kind: string,
  ) => void;
  onNamespaceJump: (namespace: string) => void;
  onPerformanceMemberSelect: (target: PackagePerformanceTarget) => void;
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
