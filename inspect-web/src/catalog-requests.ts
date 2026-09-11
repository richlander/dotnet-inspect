import type { BrowserPackageVersions } from "./facades/inspect-web-package.d.ts";

export interface DotnetRelease {
  major: number;
  tfm: string;
  version: string;
}

export interface CatalogPackage {
  id: string;
  version: string;
  isRuntimePack?: boolean;
}

export interface CatalogRequestState {
  package: CatalogPackage | null;
  packages: CatalogPackage[];
  dotnetReleases: DotnetRelease[] | null;
  dotnetReleasesLoading: boolean;
}

export interface CatalogRequestDependencies {
  state: CatalogRequestState;
  queryDotnetReleases: () => Promise<readonly DotnetRelease[]>;
  queryPackageVersions: (pkg: CatalogPackage) => Promise<BrowserPackageVersions>;
  updatePlatformVersionSelect: () => void;
  updatePackageVersionSelect: (pkg: CatalogPackage) => void;
}

export type PackageVersionState =
  | { status: "idle" | "loading" }
  | { status: "available"; inventory: BrowserPackageVersions }
  | { status: "failed"; message: string };

export function createCatalogRequests(
  dependencies: CatalogRequestDependencies,
) {
  const { state } = dependencies;
  const inventories = new WeakMap<CatalogPackage, PackageVersionState>();

  return {
    packageVersions(pkg: CatalogPackage): PackageVersionState {
      return inventories.get(pkg) ?? { status: "idle" };
    },

    forgetPackage(pkg: CatalogPackage) {
      inventories.delete(pkg);
    },

    copyPackage(from: CatalogPackage, to: CatalogPackage) {
      const entry = inventories.get(from);
      if (entry && entry.status !== "loading") inventories.set(to, entry);
    },

    async ensureDotnetReleases() {
      if (state.dotnetReleases || state.dotnetReleasesLoading) return;
      state.dotnetReleasesLoading = true;
      try {
        state.dotnetReleases = [...await dependencies.queryDotnetReleases()];
        if (state.package?.isRuntimePack) {
          dependencies.updatePlatformVersionSelect();
        }
      } catch {
        // Keep the selector on its current version when the remote index fails.
      } finally {
        state.dotnetReleasesLoading = false;
      }
    },

    async ensurePackageVersions(pkg: CatalogPackage | null) {
      if (!pkg || pkg.isRuntimePack) return;
      if (!state.packages.includes(pkg) || inventories.has(pkg)) return;
      const pending: PackageVersionState = { status: "loading" };
      inventories.set(pkg, pending);
      const isCurrent = () =>
        state.packages.includes(pkg) && inventories.get(pkg) === pending;
      let next: PackageVersionState;
      try {
        const inventory = await dependencies.queryPackageVersions(pkg);
        next = { status: "available", inventory };
      } catch (error: unknown) {
        next = {
          status: "failed",
          message: error instanceof Error ? error.message : String(error),
        };
      }
      if (isCurrent()) {
        inventories.set(pkg, next);
        dependencies.updatePackageVersionSelect(pkg);
      }
    },
  };
}
