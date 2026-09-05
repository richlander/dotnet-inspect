import { packageIdentityKey } from "../src/data.ts";
import { createPackageRemoval } from "../src/package-removal.ts";
import type { PackageControlPackage } from "../src/package-controls.ts";
import { createSavedWorkspaces, type SavedWorkspaceFocus } from "../src/saved-workspaces.ts";
import { bindSavedWorkspaces, restoreSavedWorkspaceFocus } from "../src/saved-workspaces-view.ts";
import {
  bindWorkspaceSubject, captureWorkspaceFocus, renderWorkspaceView, restoreWorkspaceFocus,
} from "../src/workspace-subject.ts";

const root = document.querySelector<HTMLElement>("#app");
const notice = document.querySelector<HTMLElement>("#notice");
if (!root || !notice) throw new Error("Saved Workspace fixture elements are missing.");
const app = root;
const status = notice;
const params = new URL(location.href).searchParams;
const alpha: PackageControlPackage = {
  id: "Alpha", version: "1.2.3", activeFramework: "net10.0", isRuntimePack: false,
};
const beta: PackageControlPackage = {
  id: "Beta", version: "4.5.6", activeFramework: "net9.0", isRuntimePack: false,
};
const packets = new Map([
  ["fixture-alpha-packet", alpha],
  ["fixture-beta-packet", beta],
]);
const state = {
  packages: params.has("empty") ? [] : [alpha],
  package: params.has("empty") ? null : alpha as PackageControlPackage | null,
  recentPackages: [],
};
let readFailure = params.has("read-failure");
const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const saves = createSavedWorkspaces({
  read: () => {
    if (readFailure) throw new Error("Storage unavailable");
    return localStorage.getItem("inspect-saved-workspaces");
  },
  write: value => {
    if (params.has("write-failure")) throw new Error("Quota exceeded");
    localStorage.setItem("inspect-saved-workspaces", value);
  },
  capture: () => {
    if (params.has("projection-failure")) throw new Error("Workspace is not projectable");
    if (!state.package) throw new Error("No package is loaded");
    return state.package.id === "Alpha" ? "fixture-alpha-packet" : "fixture-beta-packet";
  },
  open: entry => {
    const pkg = packets.get(entry.packet);
    if (!pkg) throw new Error("Packet cannot be restored");
    state.packages = [pkg];
    state.package = pkg;
    status.textContent = `Opened ${entry.name}`;
    render();
  },
  render,
});
const removal = createPackageRemoval({
  state,
  persistRecent: () => {},
  activate: pkg => { state.package = pkg; },
  release: () => render(),
});

function render(focus?: SavedWorkspaceFocus) {
  const previous = document.activeElement instanceof HTMLElement
    ? captureWorkspaceFocus(document.activeElement) : null;
  app.innerHTML = renderWorkspaceView({
    packages: state.packages,
    occurrences: state.packages.map(pkg => ({
      action: packageIdentityKey(pkg), package: pkg.id,
      version: pkg.version, framework: pkg.activeFramework,
    })),
    demos: [], demoError: "", loading: false, error: "", escapeHtml,
    savedWorkspaces: {
      state: saves.state, canSave: state.packages.length > 0, canOpen: true,
    },
  });
  bindWorkspaceSubject(app, {
    onSelect: () => {}, onActivate: () => {}, onDemo: () => {}, onRetry: () => {},
    onRemove: key => removal.removeLoaded(key),
  });
  bindSavedWorkspaces(app, saves);
  if (previous) requestAnimationFrame(() => restoreWorkspaceFocus(app, previous));
  if (focus) requestAnimationFrame(() => restoreSavedWorkspaceFocus(app, focus));
}

document.querySelector("#other-workspace")?.addEventListener("click", () => {
  state.packages = [beta];
  state.package = beta;
  render();
});
document.addEventListener("saved-workspace-rerender", () => render());
document.addEventListener("storage-available", () => { readFailure = false; });
render();
