export interface DotnetRelease {
  major: number;
  tfm: string;
  version: string;
}

export interface CatalogPackage {
  id: string;
  isRuntimePack?: boolean;
}

export interface CatalogRequestState {
  package: CatalogPackage | null;
  packages: CatalogPackage[];
  dotnetReleases: DotnetRelease[] | null;
  dotnetReleasesLoading: boolean;
  packageVersions: Record<string, string[]>;
  packageVersionsLoading: Record<string, boolean>;
}

export interface CatalogRequestDependencies {
  state: CatalogRequestState;
  queryDotnetReleases: () => Promise<readonly DotnetRelease[]>;
  queryPackageVersions: (packageId: string) => Promise<readonly string[]>;
  updatePlatformVersionSelect: () => void;
  updatePackageVersionSelect: (packageId: string) => void;
}

export function resetCatalogRequestLoading(state: CatalogRequestState) {
  state.dotnetReleasesLoading = false;
  state.packageVersionsLoading = {};
}

export function compareVersionsDesc(a: string, b: string) {
  const parse = (value: string): Array<number | string> =>
    value.split(/[.\-+]/).map(part =>
      /^\d+$/.test(part) ? Number(part) : part);
  const pa = parse(a);
  const pb = parse(b);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const x = pa[i];
    const y = pb[i];
    if (x === y) continue;
    if (x === undefined) return 1;
    if (y === undefined) return -1;
    if (typeof x === "number" && typeof y === "number") return y - x;
    return String(y).localeCompare(String(x));
  }
  return 0;
}

export function createCatalogRequests(
  dependencies: CatalogRequestDependencies,
) {
  const { state } = dependencies;
  const packageIsResident = (packageId: string) =>
    state.packages.some(item => item.id.toLowerCase() === packageId);

  return {
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
      const packageId = pkg.id.toLowerCase();
      if (state.packageVersions[packageId]
        || state.packageVersionsLoading[packageId]) return;
      state.packageVersionsLoading[packageId] = true;
      try {
        const versions = [...await dependencies.queryPackageVersions(packageId)]
          .sort(compareVersionsDesc);
        if (packageIsResident(packageId)) {
          state.packageVersions[packageId] = versions;
          dependencies.updatePackageVersionSelect(packageId);
        }
      } catch {
        // Keep the selector on its current version when the index query fails.
      } finally {
        if (packageIsResident(packageId)) {
          state.packageVersionsLoading[packageId] = false;
        } else {
          delete state.packageVersionsLoading[packageId];
        }
      }
    },
  };
}
