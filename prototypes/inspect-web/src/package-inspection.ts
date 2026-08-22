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
  packageDependencies: BrowserPackageDependencies | null;
  packageDependenciesLoading: boolean;
  packageDependenciesError: string;
  packageDependenciesKey: string;
  workspaceDependencies: Record<string, DependencyGroupData>;
  workspaceDependencyErrors: Record<string, string>;
  workspaceDependencyLoads: Set<string>;
  packageIntegrations: BrowserPackageIntegrations | null;
  packageIntegrationsLoading: boolean;
  packageIntegrationsError: string;
  packageIntegrationsKey: string;
  packageOpportunities: AsyncResource<BrowserPackageOpportunities>;
  packagePerformance: PackagePerformance | null;
  packagePerformanceLoading: boolean;
  packagePerformanceError: string;
  packagePerformanceKey: string;
  packageMetadata: PackageMetadata | null;
  packageMetadataLoading: boolean;
  packageMetadataError: string;
  packageMetadataKey: string;
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

  return {
    async loadDependencies(packageModel, signature) {
      if (state.packageDependenciesKey === signature
        && (state.packageDependencies || state.packageDependenciesError)) {
        dependencies.render();
        return;
      }
      state.packageDependenciesKey = signature;
      state.packageDependencies = null;
      state.packageDependenciesError = "";
      state.packageDependenciesLoading = true;
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
        if (state.packageDependenciesKey === signature) {
          state.packageDependencies = result;
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
        if (state.packageDependenciesKey === signature) {
          state.packageDependenciesError = dependencies.describeError(error);
        }
      } finally {
        if (state.packageDependenciesKey === signature) {
          state.packageDependenciesLoading = false;
        }
        dependencies.refreshPackageStats();
        dependencies.render();
        await ensureWorkspaceDependencies();
      }
    },

    ensureWorkspaceDependencies,

    async loadIntegrations(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageIntegrationsKey === signature
        && (state.packageIntegrations || state.packageIntegrationsError)) {
        dependencies.render();
        return;
      }
      state.packageIntegrationsKey = signature;
      state.packageIntegrations = null;
      state.packageIntegrationsError = "";
      state.packageIntegrationsLoading = true;
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
        if (state.packageIntegrationsKey === signature) {
          state.packageIntegrations = result;
        }
      } catch (error) {
        if (state.packageIntegrationsKey === signature) {
          state.packageIntegrationsError = dependencies.describeError(error);
        }
      } finally {
        if (state.packageIntegrationsKey === signature) {
          state.packageIntegrationsLoading = false;
        }
        dependencies.render();
      }
    },

    async loadOpportunities(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageOpportunities.status !== "idle"
        && state.packageOpportunities.key === signature) {
        dependencies.render();
        return;
      }
      const pending = { status: "loading", key: signature } as const;
      state.packageOpportunities = pending;
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
    },

    async loadPerformance(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packagePerformanceKey === signature
        && (state.packagePerformance || state.packagePerformanceError)) {
        dependencies.render();
        return;
      }
      state.packagePerformanceKey = signature;
      state.packagePerformance = null;
      state.packagePerformanceError = "";
      state.packagePerformanceLoading = true;
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
        if (state.packagePerformanceKey === signature) {
          state.packagePerformance = result;
        }
      } catch (error) {
        if (state.packagePerformanceKey === signature) {
          state.packagePerformanceError = dependencies.describeError(error);
        }
      } finally {
        if (state.packagePerformanceKey === signature) {
          state.packagePerformanceLoading = false;
        }
        dependencies.render();
      }
    },

    async loadMetadata(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageMetadataKey === signature
        && (state.packageMetadata || state.packageMetadataError)) {
        dependencies.render();
        return;
      }
      state.packageMetadataKey = signature;
      state.packageMetadata = null;
      state.packageMetadataError = "";
      state.packageMetadataLoading = true;
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
        if (state.packageMetadataKey === signature) {
          state.packageMetadata = result;
        }
      } catch (error) {
        if (state.packageMetadataKey === signature) {
          state.packageMetadataError = dependencies.describeError(error);
        }
      } finally {
        if (state.packageMetadataKey === signature) {
          state.packageMetadataLoading = false;
        }
        dependencies.render();
      }
    },
  };
}
