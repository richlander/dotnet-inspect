import { packageIdentityKey, retainWorkspacePackage } from "../src/data.ts";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import type { PackageControlPackage } from "../src/package-controls.ts";
import { createSpotlightPackageSearch } from "../src/spotlight-package-search.ts";
import {
  createSpotlight, type SpotlightPackageResult, type SpotlightResult,
  type SpotlightState,
} from "../src/spotlight.ts";
import {
  bindWorkspaceSubject, captureWorkspaceFocus, renderWorkspaceView, restoreWorkspaceFocus,
} from "../src/workspace-subject.ts";

const root = document.querySelector<HTMLElement>("#app");
const overlayRoot = document.querySelector<HTMLElement>("#overlay");
const notice = document.querySelector<HTMLElement>("#notice");
if (!root || !overlayRoot || !notice) throw new Error("Add-package fixture elements are missing.");
const app = root;
const overlay = overlayRoot;
const status = notice;
const alpha: PackageControlPackage =
  { id: "Alpha", version: "1.2.3", activeFramework: "net10.0", isRuntimePack: false };
const catalog: PackageControlPackage[] = [
  alpha,
  { id: "Beta", version: "4.5.6", activeFramework: "net9.0", isRuntimePack: false },
];
const empty = new URL(location.href).searchParams.has("empty");
const state = {
  packages: empty ? [] : [alpha],
  package: empty ? null : alpha,
};
const search: SpotlightState = {
  spotlightOpen: false, spotlightQuery: "", spotlightIndex: 0,
  spotlightScope: "all", spotlightFocus: "input", spotlightChipIndex: 0,
};
const discoveryState = {
  ...search,
  spotlightPkgHits: [] as { id: string; version: string }[],
  spotlightPkgQuery: "", spotlightPkgLoading: false, spotlightPkgError: "",
};
const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const keybindings = new KeybindingRegistry();
document.addEventListener("keydown", event => keybindings.dispatch(event));
const discovery = createSpotlightPackageSearch({
  state: discoveryState,
  queryPackages: async query => {
    if (query === "fail") throw new Error("NuGet unavailable");
    return catalog.filter(pkg => pkg.id.toLowerCase().includes(query.toLowerCase()))
      .map(pkg => ({ id: pkg.id, version: pkg.version }));
  },
  schedule: (callback, delay) => setTimeout(() => void callback(), delay),
  cancelScheduled: handle => clearTimeout(handle),
  updateResults: () => spotlight.updateResults(),
});
const spotlight = createSpotlight({
  state: search, keybindings, lenses: () => [], escapeHtml,
  highlightRanges: value => escapeHtml(value), kindIcon: () => "T",
  searchResults: (): SpotlightResult[] => [
    ...state.packages.filter(pkg =>
      pkg.id.toLowerCase().includes(search.spotlightQuery.toLowerCase()))
      .map(pkg => ({ kind: "pkg-loaded" as const, pkg, ranges: [] })),
    ...discoveryState.spotlightPkgHits.filter(hit =>
      !state.packages.some(pkg => pkg.id === hit.id))
      .map(hit => ({ kind: "pkg-nuget" as const, hit, ranges: [] })),
    { kind: "package-query", prefix: search.spotlightQuery },
  ],
  pickResult: () => { status.textContent = "Ordinary Search selection"; },
  removeResult: () => { status.textContent = "Ordinary removal"; return true; },
  executeCommand: () => undefined, reportCommandError: error => { throw error; },
  commandContext: () => null,
  schedulePackageFetch: () => {
    discoveryState.spotlightQuery = search.spotlightQuery;
    discoveryState.spotlightScope = search.spotlightScope;
    discovery.schedule();
  },
  resetPackageSearch: () => discovery.reset(),
  packageSearchLoading: () => discoveryState.spotlightPkgLoading,
  packageSearchError: () => discoveryState.spotlightPkgError,
  packageCount: () => state.packages.length,
  activeFramework: () => state.package?.activeFramework ?? "",
  render,
});

// Acquisition/history are exercised by the original-host Node gate. This fixture
// supplies resolved packages to the actual retention and interactive renderers.
function selectPackage(result: SpotlightPackageResult) {
  const id = result.kind === "pkg-loaded" ? result.pkg.id
    : result.kind === "pkg-nuget" ? result.hit.id : result.entry.id;
  const pkg = catalog.find(candidate => candidate.id === id);
  if (!pkg) throw new Error(`Missing fixture package ${id}`);
  spotlight.reset();
  if (!state.packages.some(candidate => packageIdentityKey(candidate) === packageIdentityKey(pkg))) {
    state.packages = retainWorkspacePackage(state.packages, state.package, pkg).packages;
    state.package ??= pkg;
  }
  status.textContent = `Active: ${state.package?.id}`;
  render();
  const heading = app.querySelector<HTMLElement>("h1");
  if (heading) { heading.tabIndex = -1; heading.focus(); }
}

function render() {
  const previous = document.activeElement instanceof HTMLElement
    ? captureWorkspaceFocus(document.activeElement) : null;
  app.innerHTML = renderWorkspaceView({
    canAddPackage: true, packages: state.packages,
    occurrences: state.packages.map(pkg => ({
      action: packageIdentityKey(pkg), package: pkg.id,
      version: pkg.version, framework: pkg.activeFramework,
    })),
    demos: [], demoError: "", loading: false, error: "", escapeHtml,
  });
  bindWorkspaceSubject(app, {
    onSelect: () => {}, onActivate: () => {}, onDemo: () => {}, onRetry: () => {},
    onAddPackage: () => spotlight.openForPackageAddition({
      pickResult: selectPackage,
      focusAfterDismiss: () => {
        restoreWorkspaceFocus(app, { kind: "add-package" });
      },
    }),
  });
  overlay.innerHTML = search.spotlightOpen ? spotlight.modalHtml() : "";
  if (search.spotlightOpen) spotlight.bind(overlay, "modal");
  else if (previous) restoreWorkspaceFocus(app, previous);
}

document.querySelector("#ordinary-search")?.addEventListener("click", () => spotlight.open());
document.addEventListener("workspace-add-rerender", () => render());
render();
