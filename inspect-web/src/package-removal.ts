import {
  packageIdentityKey,
  removeWorkspacePackage,
  type RemoveWorkspacePackageInput,
} from "./data.ts";

export interface RecentPackageEntry {
  id: string;
  version: string;
  framework: string;
}

export interface PackageRemovalState<T> {
  packages: T[];
  package: T | null;
  recentPackages: RecentPackageEntry[];
}

export function packageRemoveButton(
  attribute: string,
  identity: string,
  label: string,
  escapeHtml: (value: unknown) => string,
): string {
  return `<button type="button" class="package-row-remove" ${attribute}="${escapeHtml(identity)}" aria-label="${escapeHtml(label)}" title="${escapeHtml(label)}"><span aria-hidden="true">&times;</span></button>`;
}

export function createPackageRemoval<T extends RemoveWorkspacePackageInput>(
  options: {
    state: PackageRemovalState<T>;
    persistRecent: (entries: readonly RecentPackageEntry[]) => void;
    activate: (next: T | null) => void;
    release: (removed: T) => void;
  },
) {
  const { state } = options;

  function forgetRecent(id: string): void {
    const entries = state.recentPackages.filter(
      entry => entry.id.toLowerCase() !== id.toLowerCase());
    // Persist first: a failed write must not look like a successful deletion.
    options.persistRecent(entries);
    state.recentPackages = entries;
  }

  return {
    forgetRecent,
    removeLoaded(key: string): void {
      const removed = removeWorkspacePackage(state.packages, state.package, key);
      if (!removed.closed) {
        throw new Error("That package is no longer removable from this Workspace.");
      }
      forgetRecent(removed.closed.id);
      const activeChanged =
        packageIdentityKey(state.package) !== packageIdentityKey(removed.active);
      state.packages = removed.packages;
      if (activeChanged) options.activate(removed.active);
      options.release(removed.closed);
    },
  };
}
