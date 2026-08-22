import {
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

  // The completion of the opportunity request that owns the live `loading` resource. A
  // duplicate caller has to await this rather than returning: a `loading` resource means the
  // work is still running, and returning told the caller it had finished. Joining is keyed
  // on the *identity* of that resource as well as the signature -- the signature alone could
  // join a request that was already abandoned and will publish nothing.
  let opportunitiesInFlight: {
    readonly resource: AsyncResource<BrowserPackageOpportunities>;
    readonly completion: Promise<void>;
  } | null = null;

  // Query failures are published as resource state. Rendering can still reject, so the
  // in-flight record observes completion even if the starting caller's render throws.
  const publishOpportunities = async (
    packageModel: AppPackage,
    signature: string,
    scopedLibrary: string | null | undefined,
    pending: AsyncResource<BrowserPackageOpportunities>,
  ): Promise<void> => {
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
        state.packageOpportunities = { status: "ready", key: signature, data: result };
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
  };

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

  return {
    async loadDependencies(packageModel, signature) {
      if (state.packageDependencies.status !== "idle"
        && state.packageDependencies.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageDependencies = pending;
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
    },

    ensureWorkspaceDependencies,

    async loadIntegrations(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageIntegrations.status !== "idle"
        && state.packageIntegrations.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageIntegrations = pending;
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
    },

    async loadOpportunities(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      const current = state.packageOpportunities;
      if (current.status !== "idle" && current.key === signature) {
        // A settled resource is the finished answer, so returning is correct. A `loading`
        // one is not finished, and returning reported a completion that had not happened.
        const joined = current.status === "loading"
          && opportunitiesInFlight?.resource === current
          ? opportunitiesInFlight.completion
          : null;
        dependencies.render();
        if (joined) await joined;
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageOpportunities = pending;
      const completion = publishOpportunities(
        packageModel, signature, scopedLibrary, pending);
      opportunitiesInFlight = { resource: pending, completion };
      void completion.then(() => {
        if (opportunitiesInFlight?.resource === pending) opportunitiesInFlight = null;
      }, () => {
        if (opportunitiesInFlight?.resource === pending) opportunitiesInFlight = null;
      });
      dependencies.render();
      await completion;
    },

    async loadPerformance(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packagePerformance.status !== "idle"
        && state.packagePerformance.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packagePerformance = pending;
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
    },

    async loadMetadata(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageMetadata.status !== "idle"
        && state.packageMetadata.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageMetadata = pending;
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
    },
  };
}
