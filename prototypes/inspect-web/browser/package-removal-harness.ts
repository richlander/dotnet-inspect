import { packageIdentityKey } from "../src/data.ts";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import { createPackageRemoval } from "../src/package-removal.ts";
import type { PackageControlPackage } from "../src/package-controls.ts";
import { createSpotlight, type SpotlightResult, type SpotlightState } from "../src/spotlight.ts";
import {
  bindWorkspaceSubject, captureWorkspaceFocus, renderWorkspaceView,
  restoreWorkspaceFocus,
} from "../src/workspace-subject.ts";

const root = document.querySelector<HTMLElement>("#app");
const notice = document.querySelector<HTMLElement>("#notice");
if (!root || !notice) throw new Error("Package removal harness elements are missing.");
const app = root;
const status = notice;
const params = new URL(location.href).searchParams;
const workspace = params.has("workspace");
const modal = params.has("modal");
const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const initialPackages = [
  { id: "Newtonsoft.Json", version: "13.0.4", activeFramework: "net10.0", isRuntimePack: false },
  { id: "System.Text.Json", version: "10.0.0", activeFramework: "net10.0", isRuntimePack: false },
];
const recentIds = [...initialPackages.map(pkg => pkg.id), "Microsoft.Extensions.Http"];
if (!localStorage.getItem("removal-harness-initialized")) {
  localStorage.setItem("inspect-recent-packages", JSON.stringify(recentIds));
  localStorage.setItem("removal-harness-initialized", "true");
}
const stored: unknown = JSON.parse(localStorage.getItem("inspect-recent-packages") ?? "[]");
if (!Array.isArray(stored) || !stored.every(item => typeof item === "string")) {
  throw new Error("Invalid harness history");
}
const packages: PackageControlPackage[] = params.has("cold") ? [] : initialPackages;
const state = {
  packages, package: packages[0] ?? null,
  recentPackages: stored.map((id: string) => ({ id, version: "10.0.0", framework: "net10.0" })),
};
const search: SpotlightState = {
  spotlightOpen: modal, spotlightQuery: "", spotlightIndex: 0,
  spotlightScope: "all", spotlightFocus: "input", spotlightChipIndex: 0,
};
const keys = new KeybindingRegistry();
document.addEventListener("keydown", event => keys.dispatch(event));
const removal = createPackageRemoval({
  state,
  persistRecent: entries => {
    if (params.has("storage-failure")) throw new Error("Storage is unavailable");
    localStorage.setItem("inspect-recent-packages", JSON.stringify(entries.map(entry => entry.id)));
  },
  activate: next => { state.package = next; },
  release: () => render(),
});
const spotlight = createSpotlight({
  state: search, keybindings: keys, lenses: () => [],
  escapeHtml, highlightRanges: value => escapeHtml(value), kindIcon: () => "T",
  searchResults: (): SpotlightResult[] => {
    const query = search.spotlightQuery.toLowerCase();
    const loaded = state.packages.filter(pkg => pkg.id.toLowerCase().includes(query));
    const recent = state.recentPackages.filter(entry =>
      entry.id.toLowerCase().includes(query)
      && !state.packages.some(pkg => pkg.id === entry.id));
    return [
      ...loaded.map(pkg => ({ kind: "pkg-loaded" as const, pkg, ranges: [] })),
      ...recent.map(entry => ({ kind: "pkg-recent" as const, entry, ranges: [] })),
      ...(query ? [{ kind: "pkg-nuget" as const, hit: { id: "System.Text.Json" }, ranges: [] }] : []),
    ];
  },
  pickResult: () => { status.textContent = "Activated"; },
  removeResult: result => {
    try {
      if (result.kind === "pkg-recent") removal.forgetRecent(result.entry.id);
      else removal.removeLoaded(packageIdentityKey({
        ...result.pkg, activeFramework: result.pkg.activeFramework ?? "",
      }));
      return true;
    } catch (error) {
      status.textContent = String(error);
      return false;
    }
  },
  executeCommand: () => undefined, reportCommandError: error => { throw error; },
  commandContext: () => null, schedulePackageFetch: () => {},
  resetPackageSearch: () => {}, packageSearchLoading: () => false,
  packageCount: () => state.packages.length, activeFramework: () => "net10.0",
  render,
});

function render() {
  const focused = document.activeElement instanceof HTMLElement
    ? captureWorkspaceFocus(document.activeElement) : null;
  if (workspace) {
    app.innerHTML = renderWorkspaceView({
      packages: state.packages,
      occurrences: state.packages.map(pkg => ({
        action: packageIdentityKey(pkg), package: pkg.id,
        version: pkg.version, framework: pkg.activeFramework,
      })),
      demos: [], demoError: "", loading: params.has("loading"),
      error: params.has("failed-query") ? "Occurrence query unavailable" : "",
      escapeHtml,
    });
    bindWorkspaceSubject(app, {
      onSelect: () => {}, onActivate: () => { status.textContent = "Activated"; },
      onDemo: () => {}, onRetry: () => {},
      onRemove: key => {
        try { removal.removeLoaded(key); }
        catch (error) { status.textContent = String(error); }
      },
    });
    if (focused) restoreWorkspaceFocus(app, focused);
  } else {
    app.innerHTML = modal ? spotlight.modalHtml() : spotlight.inlineHtml(false);
    spotlight.bind(app, modal ? "modal" : "inline");
  }
}
render();
