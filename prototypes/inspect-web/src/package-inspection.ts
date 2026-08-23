import {
  assertNever,
  packageIdentityKey,
  type AsyncResource,
  type DependencyGroupData,
  type PackageLens,
  type PackageIdentity,
} from "./data.ts";
import type {
  BrowserPackageDependencies,
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
} from "./inspect-web-engine.d.ts";
import type { PackageMetadata } from "./metadata-viewer.ts";
import type { AppPackage } from "./package-acquisition.ts";

export interface PackagePerformanceMember {
  assembly: string;
  typeId: string;
  memberName: string;
  metadataToken: number;
  opportunityCount: number;
  inLoopCount: number;
  shapes: string[];
  confidence: string;
}

export interface PackagePerformance {
  members: PackagePerformanceMember[];
  inspectionError?: string;
  nonPublicOpportunities: number;
  totalOpportunities: number;
}

export interface PackageInspectionState {
  packages: AppPackage[];
  atPackageRoot: boolean;
  packageLens: PackageLens;
  packageDependencies: AsyncResource<BrowserPackageDependencies>;
  workspaceDependencies: Record<string, DependencyGroupData>;
  workspaceDependencyErrors: Record<string, string>;
  workspaceDependencyLoads: Set<string>;
  packageIntegrations: AsyncResource<BrowserPackageIntegrations>;
  packageOpportunities: AsyncResource<BrowserPackageOpportunities>;
  packagePerformance: AsyncResource<PackagePerformance>;
  packageMetadata: AsyncResource<PackageMetadata>;
}

// The metadata renderer predates `AsyncResource` and still takes four flattened
// options. Project them here, from the union, in one exhaustive switch: the renderer's
// signature is a rendering concern, but deciding *which* of those four combinations the
// state means is a state concern, and it belongs with the state.
export function packageMetadataView(
  resource: AsyncResource<PackageMetadata>,
  signature: string,
): {
  fresh: boolean;
  loading: boolean;
  error: string;
  metadata: PackageMetadata | null;
} {
  if (resource.status === "idle" || resource.key !== signature) {
    return { fresh: false, loading: false, error: "", metadata: null };
  }
  switch (resource.status) {
    case "loading":
      return { fresh: true, loading: true, error: "", metadata: null };
    case "failed":
      return { fresh: true, loading: false, error: resource.error, metadata: null };
    case "ready":
      return { fresh: true, loading: false, error: "", metadata: resource.data };
    default:
      return assertNever(resource, "package metadata resource status");
  }
}

export interface PackageInspectionDependencies {
  state: PackageInspectionState;
  queryDependencies(
    packageModel: PackageIdentity & { assemblyId: string },
  ): Promise<BrowserPackageDependencies>;
  queryPackageIntegrations(
    packageModel: AppPackage,
  ): Promise<BrowserPackageIntegrations>;
  queryPlatformIntegrations(
    framework: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<BrowserPackageIntegrations>;
  queryPackageOpportunities(
    packageModel: AppPackage,
  ): Promise<BrowserPackageOpportunities>;
  queryPlatformOpportunities(
    framework: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<BrowserPackageOpportunities>;
  queryPackagePerformance(
    packageModel: AppPackage,
  ): Promise<PackagePerformance>;
  queryPlatformPerformance(
    framework: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<PackagePerformance>;
  queryPackageMetadata(packageModel: AppPackage): Promise<PackageMetadata>;
  queryPlatformMetadata(
    framework: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<PackageMetadata>;
  platformPackForAssembly(assemblyName: string): string;
  describeError(error: unknown): string;
  refreshPackageStats(): void;
  render(): void;
  renderDependencyGraph(): Promise<void>;
}

export interface PackageInspectionCoordinator {
  loadDependencies(
    packageModel: AppPackage,
    signature: string,
  ): Promise<void>;
  ensureWorkspaceDependencies(): Promise<void>;
  loadIntegrations(
    packageModel: AppPackage,
    signature: string,
    scopedLibrary: string | null,
  ): Promise<void>;
  loadOpportunities(
    packageModel: AppPackage,
    signature: string,
    scopedLibrary: string | null,
  ): Promise<void>;
  loadPerformance(
    packageModel: AppPackage,
    signature: string,
    scopedLibrary: string | null,
  ): Promise<void>;
  loadMetadata(
    packageModel: AppPackage,
    signature: string,
    scopedLibrary: string | null,
  ): Promise<void>;
}

export function workspaceDependencyKey(packageModel: PackageIdentity): string {
  return [
    packageModel.id.toLowerCase(),
    packageModel.version.toLowerCase(),
    packageModel.activeFramework.toLowerCase(),
  ].join("@");
}

function packageIsResident(
  packages: readonly AppPackage[],
  packageModel: PackageIdentity,
): boolean {
  const key = packageIdentityKey(packageModel);
  return packages.some(candidate => packageIdentityKey(candidate) === key);
}

export function createPackageInspectionCoordinator(
  dependencies: PackageInspectionDependencies,
): PackageInspectionCoordinator {
  const { state } = dependencies;

  const platformCoordinates = (
    packageModel: AppPackage,
    scopedLibrary: string,
  ) => ({
    framework: packageModel.activeFramework,
    assemblyFileName: `${scopedLibrary}.dll`,
    pack: dependencies.platformPackForAssembly(scopedLibrary),
  });

  const ensureWorkspaceDependencies = async () => {
    const missing = state.packages.filter(packageModel =>
      !packageModel.isRuntimePack
      && !Object.hasOwn(
        state.workspaceDependencies,
        workspaceDependencyKey(packageModel))
      && !state.workspaceDependencyLoads.has(
        workspaceDependencyKey(packageModel)));
    if (!missing.length) {
      await dependencies.renderDependencyGraph();
      return;
    }
    for (const packageModel of missing) {
      const key = workspaceDependencyKey(packageModel);
      if (!packageIsResident(state.packages, packageModel)) continue;
      state.workspaceDependencyLoads.add(key);
      try {
        const result = await dependencies.queryDependencies(packageModel);
        if (!packageIsResident(state.packages, packageModel)) continue;
        state.workspaceDependencies[key] = {
          dependencyGroups: result?.dependencyGroups || [],
          dependencyGroupError: result?.dependencyGroupError || "",
        };
        if (result?.dependencyGroupError) {
          state.workspaceDependencyErrors[key] =
            result.dependencyGroupError;
        } else {
          delete state.workspaceDependencyErrors[key];
        }
      } catch (error) {
        if (!packageIsResident(state.packages, packageModel)) continue;
        state.workspaceDependencies[key] = {
          dependencyGroups: [],
          dependencyGroupError: "",
        };
        state.workspaceDependencyErrors[key] =
          dependencies.describeError(error);
      } finally {
        state.workspaceDependencyLoads.delete(key);
      }
    }

    if (state.atPackageRoot && state.packageLens === "dependencies") {
      dependencies.render();
    }
    dependencies.refreshPackageStats();
  };

  // These loaders are `async`, so returning early resolves the caller's await. When a
  // same-key request was already in flight the old guard did exactly that: it re-rendered
  // and returned, telling the second caller the load had finished while the first request
  // was still running. "Reuse" has to mean the second caller joins the first, so the
  // original request's promise is handed back instead.
  //
  // A settled resource with the same key is a different case and still returns
  // immediately: that work really is done, and re-querying it is the duplication this
  // guard exists to avoid.
  const inFlight = new Map<
    string,
    { resource: AsyncResource<unknown>; promise: Promise<void>; settle: () => void }>();

  // Join on the identity of the pending resource, not on its key. A request that has
  // lost ownership -- because a newer request or a scope change overwrote the slot --
  // will discard its own result when it lands, so joining it would leave the caller
  // awaiting a request that publishes nothing. Two requests for one scope can carry the
  // same key while only one of them still owns the state, so key equality cannot tell
  // them apart and object identity can.
  // Identity alone is not enough: it says the in-flight request still owns the state, not
  // that it is the request this caller wants. Restacking onto the opportunities slice
  // showed why -- its A->B->A tests deadlocked here, because a caller for a *different*
  // scope matched on identity, joined the running request, and returned without ever
  // starting its own. The key comparison is what makes this "the same request", and the
  // identity comparison is what makes it "still the live one".
  function joinInFlight(
    lens: string,
    live: AsyncResource<unknown>,
    signature: string,
  ): Promise<void> | null {
    const active = inFlight.get(lens);
    if (!active || active.resource !== live) return null;
    if (live.status !== "loading" || live.key !== signature) return null;
    dependencies.render();
    return active.promise;
  }

  // The body runs inside this function's `try`, so settling is not something a caller can
  // forget or be skipped past. Round 2 review (Claude Opus 5, GPT-5.6 Sol) found the
  // previous shape -- `beginInFlight` returning a `settle` the caller invoked from its own
  // `finally` -- deadlocked the lens: `dependencies.render()` sat between the registration
  // and the `try`, so a throwing render skipped `settle` while `state[lens]` was still the
  // exact pending object the join requires. The next same-key caller joined a promise
  // nobody would ever resolve. `loadDependencies` had a second instance of the same bug,
  // settling after an awaited call inside its own `finally`.
  //
  // Taking the body removes the window rather than narrowing it: there is no way to
  // register an entry without this `finally`. `test/in-flight-settlement.test.ts` drives
  // every lens with a render callback that throws on the nth call, for every n, and proves
  // a later same-key caller still settles.
  function runInFlight(
    lens: string,
    resource: AsyncResource<unknown>,
    body: () => Promise<void>,
  ): Promise<void> {
    let settle: () => void = () => {};
    const promise = new Promise<void>(resolve => {
      settle = resolve;
    });
    const entry = { resource, promise, settle };
    inFlight.set(lens, entry);
    return (async () => {
      try {
        await body();
      } finally {
        if (inFlight.get(lens) === entry) inFlight.delete(lens);
        entry.settle();
      }
    })();
  }

  return {
    async loadDependencies(packageModel, signature) {
      const joined = joinInFlight("packageDependencies", state.packageDependencies, signature);
      if (joined) return joined;
      if (state.packageDependencies.status !== "idle"
        && state.packageDependencies.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageDependencies = pending;
      return runInFlight("packageDependencies", pending, async () => {
        dependencies.render();
        const packageRequest = {
          id: packageModel.id,
          version: packageModel.version,
          activeFramework: packageModel.activeFramework,
          assemblyId: packageModel.assemblyId,
        };
        const workspaceKey = workspaceDependencyKey(packageRequest);
        try {
        const result = await dependencies.queryDependencies(packageRequest);
        if (state.packageDependencies === pending) {
          state.packageDependencies = {
            status: "ready",
            key: signature,
            data: result,
          };
        }
        if (result?.dependencyGroups
          && packageIsResident(state.packages, packageRequest)) {
          state.workspaceDependencies[workspaceKey] = {
            dependencyGroups: result.dependencyGroups,
            dependencyGroupError: result.dependencyGroupError || "",
          };
          if (result.dependencyGroupError) {
            state.workspaceDependencyErrors[workspaceKey] =
              result.dependencyGroupError;
          } else {
            delete state.workspaceDependencyErrors[workspaceKey];
          }
        }
      } catch (error) {
        if (state.packageDependencies === pending) {
          state.packageDependencies = {
            status: "failed",
            key: signature,
            error: dependencies.describeError(error),
          };
        }
        } finally {
          dependencies.refreshPackageStats();
          dependencies.render();
          await ensureWorkspaceDependencies();
        }
      });
    },

    ensureWorkspaceDependencies,

    async loadIntegrations(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      const joined = joinInFlight("packageIntegrations", state.packageIntegrations, signature);
      if (joined) return joined;
      if (state.packageIntegrations.status !== "idle"
        && state.packageIntegrations.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageIntegrations = pending;
      return runInFlight("packageIntegrations", pending, async () => {
        dependencies.render();
          try {
          const coordinates = packageModel.isRuntimePack
            ? platformCoordinates(packageModel, scopedLibrary ?? "")
            : null;
          const result = coordinates
            ? await dependencies.queryPlatformIntegrations(
                coordinates.framework,
                coordinates.assemblyFileName,
                coordinates.pack)
            : await dependencies.queryPackageIntegrations(packageModel);
          if (state.packageIntegrations === pending) {
            state.packageIntegrations = {
              status: "ready",
              key: signature,
              data: result,
            };
          }
        } catch (error) {
          if (state.packageIntegrations === pending) {
            state.packageIntegrations = {
              status: "failed",
              key: signature,
              error: dependencies.describeError(error),
            };
          }
        } finally {
          dependencies.render();
        }
      });
    },

    async loadOpportunities(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      const joined = joinInFlight("packageOpportunities", state.packageOpportunities, signature);
      if (joined) return joined;
      if (state.packageOpportunities.status !== "idle"
        && state.packageOpportunities.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageOpportunities = pending;
      return runInFlight("packageOpportunities", pending, async () => {
        dependencies.render();
        try {
          const coordinates = packageModel.isRuntimePack
            ? platformCoordinates(packageModel, scopedLibrary ?? "")
            : null;
          const result = coordinates
            ? await dependencies.queryPlatformOpportunities(
                coordinates.framework,
                coordinates.assemblyFileName,
                coordinates.pack)
            : await dependencies.queryPackageOpportunities(packageModel);
          if (state.packageOpportunities === pending) {
            state.packageOpportunities = {
              status: "ready",
              key: signature,
              data: result,
            };
          }
        } catch (error) {
          if (state.packageOpportunities === pending) {
            state.packageOpportunities = {
              status: "failed",
              key: signature,
              error: dependencies.describeError(error),
            };
          }
        } finally {
          dependencies.render();
        }
      });
    },

    async loadPerformance(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      const joined = joinInFlight("packagePerformance", state.packagePerformance, signature);
      if (joined) return joined;
      if (state.packagePerformance.status !== "idle"
        && state.packagePerformance.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packagePerformance = pending;
      return runInFlight("packagePerformance", pending, async () => {
        dependencies.render();
          try {
          const coordinates = packageModel.isRuntimePack
            ? platformCoordinates(packageModel, scopedLibrary ?? "")
            : null;
          const result = coordinates
            ? await dependencies.queryPlatformPerformance(
                coordinates.framework,
                coordinates.assemblyFileName,
                coordinates.pack)
            : await dependencies.queryPackagePerformance(packageModel);
          if (state.packagePerformance === pending) {
            state.packagePerformance = {
              status: "ready",
              key: signature,
              data: result,
            };
          }
        } catch (error) {
          if (state.packagePerformance === pending) {
            state.packagePerformance = {
              status: "failed",
              key: signature,
              error: dependencies.describeError(error),
            };
          }
        } finally {
          dependencies.render();
        }
      });
    },

    async loadMetadata(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      const joined = joinInFlight("packageMetadata", state.packageMetadata, signature);
      if (joined) return joined;
      if (state.packageMetadata.status !== "idle"
        && state.packageMetadata.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageMetadata = pending;
      return runInFlight("packageMetadata", pending, async () => {
        dependencies.render();
          try {
          const coordinates = packageModel.isRuntimePack
            ? platformCoordinates(packageModel, scopedLibrary ?? "")
            : null;
          const result = coordinates
            ? await dependencies.queryPlatformMetadata(
                coordinates.framework,
                coordinates.assemblyFileName,
                coordinates.pack)
            : await dependencies.queryPackageMetadata(packageModel);
          if (state.packageMetadata === pending) {
            state.packageMetadata = {
              status: "ready",
              key: signature,
              data: result,
            };
          }
        } catch (error) {
          if (state.packageMetadata === pending) {
            state.packageMetadata = {
              status: "failed",
              key: signature,
              error: dependencies.describeError(error),
            };
          }
        } finally {
          dependencies.render();
        }
      });
    },
  };
}
