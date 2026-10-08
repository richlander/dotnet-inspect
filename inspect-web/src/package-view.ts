import type { PackageVersionState } from "./catalog-requests.ts";
import { renderContentNavigationCloseButton } from "./content-frame.ts";

// DOM bindings for package-level navigation surfaces. The application root owns
// package, filter, graph, and inspection state transitions behind these callbacks.

export interface PackageDependencyBindingActions {
  onDependencyLoad: (id: string, version: string) => void;
  onDependencyOpen: (packageKey: string) => void;
}

export interface PackagePerformanceTarget {
  resourceMethodToken?: number;
  stableSelector: string;
  assembly: string;
  typeId: string;
}

export interface RelatedTypeNavigationTarget {
  typeId: string;
  packageId?: string;
  version?: string;
  framework?: string;
  asset?: string;
}

export interface PackageViewBindingActions
  extends PackageDependencyBindingActions {
  onPackageChildLibrarySelect: (assetId: string) => void;
  onLibraryReferenceSelect: (
    packageKey: string,
    libraryId: string,
  ) => void;
  onRuntimeIdentifierPackageLoad: (
    packageId: string,
    packageVersion: string,
  ) => void;
  onDependencyGroupSelect: (index: number) => void;
  onGraphTypeSelect: (target: RelatedTypeNavigationTarget) => void;
  onKindJump: (kind: string) => void;
  onLibraryScopeSelect: (
    library: string | undefined,
    kind: string,
  ) => void;
  onNamespaceJump: (namespace: string) => void;
  onPerformanceMemberSelect: (target: PackagePerformanceTarget) => void;
}

export function packageNavigationVersions(activeVersion: string, entry: PackageVersionState): string[] {
  const versions = entry.status === "available" ? [...entry.inventory.versions] : [activeVersion];
  if (!versions.some(version => version.toLowerCase() === activeVersion.toLowerCase())) {
    versions.splice(entry.status === "available" ? entry.inventory.currentVersionInsertionIndex : 0, 0, activeVersion);
  }
  return versions;
}

export interface PackageNavOptions {
  frameworks: readonly string[];
  activeFramework: string;
  versions: readonly string[];
  activeVersion: string;
  unlistedVersions?: readonly string[];
  statusHtml?: string;
  escapeHtml: (value: unknown) => string;
}

export function renderPackageNav(options: PackageNavOptions): string {
  const { versions, activeVersion, statusHtml = "", escapeHtml } = options;
  const unlisted = new Set(options.unlistedVersions?.map(version => version.toLowerCase()));
  return `<aside id="content-navigation-pane" class="type-browser package-version-nav" aria-label="Frameworks &amp; versions">
    ${renderPackageFrameworks(options.frameworks, options.activeFramework, escapeHtml, renderContentNavigationCloseButton())}
    <section class="package-navigation-controls" aria-label="Version filters">
      <div class="section-title"><h2>Versions</h2><span>${versions.length}</span></div>
      <label>Filter versions<input id="package-version-filter" type="search" placeholder="Find a version"></label>
      <div class="package-version-inclusion" role="group" aria-label="Include versions">
        <span>Include:</span>
        <label><input id="package-version-prerelease" type="checkbox"> Prerelease</label>
        <label><input id="package-version-unlisted" type="checkbox"> Unlisted</label>
      </div>
      ${statusHtml}
    </section>
    <div id="package-version-list" class="type-list" role="group" aria-label="Package version navigation" tabindex="-1" data-nav-scope="versions" data-nav-selection="version:${escapeHtml(activeVersion)}">
      ${versions.map(version => `<button type="button" class="type-row ${version.toLowerCase() === activeVersion.toLowerCase() ? "selected" : ""}" data-package-version="${escapeHtml(version)}"${unlisted.has(version.toLowerCase()) ? ' data-package-unlisted="true"' : ""}${version.toLowerCase() === activeVersion.toLowerCase() ? ' aria-current="page"' : ""} title="Use ${escapeHtml(version)}"><span class="kind-icon">V</span><span class="type-name">${escapeHtml(version)}</span></button>`).join("")}
    </div>
  </aside>`;
}

export function renderPackageFrameworks(
  frameworks: readonly string[], activeFramework: string,
  escapeHtml: (value: unknown) => string,
  navigationCloseHtml = "",
): string {
  return `<section class="document-section package-frameworks"><div class="section-title"><h2>Target frameworks</h2><div class="browser-head-actions"><span>${frameworks.length}</span>${navigationCloseHtml}</div></div>
    <div role="group" aria-label="Target frameworks" tabindex="-1" data-nav-scope="frameworks">${frameworks.map(framework => `<button type="button" class="type-row ${framework === activeFramework ? "selected" : ""}" data-package-framework="${escapeHtml(framework)}"${framework === activeFramework ? ' aria-current="page"' : ""}><span class="kind-icon">T</span><span class="type-name">${escapeHtml(framework)}</span></button>`).join("") || '<p class="empty-list">No target frameworks are available for this package version.</p>'}</div></section>`;
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
  root.querySelectorAll<HTMLElement>("[data-package-child-library]").forEach(
    button => button.addEventListener(
      "click",
      () => actions.onPackageChildLibrarySelect(
        button.dataset.packageChildLibrary ?? "")));
  root.querySelectorAll<HTMLElement>("[data-library-reference-package]").forEach(
    button => button.addEventListener(
      "click",
      () => actions.onLibraryReferenceSelect(
        button.dataset.libraryReferencePackage ?? "",
        button.dataset.libraryReferenceLibrary ?? "")));
  root.querySelectorAll<HTMLElement>("[data-package-child-package]").forEach(
    button => button.addEventListener(
      "click",
      () => actions.onRuntimeIdentifierPackageLoad(
        button.dataset.packageChildPackage ?? "",
        button.dataset.packageChildVersion ?? "")));
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
      () => actions.onGraphTypeSelect({
        typeId: button.dataset.graphType ?? "",
        ...(button.dataset.graphPackage
          ? { packageId: button.dataset.graphPackage }
          : {}),
        ...(button.dataset.graphVersion
          ? { version: button.dataset.graphVersion }
          : {}),
        ...(button.dataset.graphFramework
          ? { framework: button.dataset.graphFramework }
          : {}),
        ...(button.dataset.graphAsset
          ? { asset: button.dataset.graphAsset }
          : {}),
      })));
  root.querySelectorAll<HTMLElement>("[data-perf-selector]").forEach(button =>
    button.addEventListener("click", () => actions.onPerformanceMemberSelect({
      stableSelector: button.dataset.perfSelector ?? "",
      assembly: button.dataset.perfAssembly ?? "",
      typeId: button.dataset.perfType ?? "",
      ...(button.dataset.resourceToken ? { resourceMethodToken: Number(button.dataset.resourceToken) } : {}),
    })));
}
