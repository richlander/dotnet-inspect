import type { BrowserPackageVersions } from "./facades/inspect-web-package.d.ts";

export interface CatalogPackage {
  id: string;
  version: string;
  isRuntimePack?: boolean;
}

export interface CatalogRequestState {
  packages: CatalogPackage[];
}

export interface CatalogRequestDependencies {
  state: CatalogRequestState;
  queryPackageVersions: (pkg: CatalogPackage) => Promise<BrowserPackageVersions>;
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
