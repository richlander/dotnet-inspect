import {
  packageIdentityKey,
  type DependencyGroupData,
  type LibraryLens,
  type PackageLens,
  type PackageIdentity,
} from "./data.ts";
import type {
  BrowserPackageDependencies,
  BrowserPackageVulnerabilityResult,
} from "./facades/inspect-web-package.d.ts";
import type {
  BrowserLibraryDependencyStructure,
  BrowserPackageIntegrations,
  BrowserLibraryMetrics,
  BrowserPackageOpportunities,
  BrowserPackagePerformance,
  BrowserResourceTriage,
  BrowserPerformanceMember,
} from "./facades/inspect-web-analysis.d.ts";
import type {
  MetadataRootSelection,
  PackageMetadata,
} from "./metadata-viewer.ts";
import type {
  AppMemberSurface,
  AppPackage,
  AppTypeSurface,
} from "./package-acquisition.ts";
import type {
  LibraryMetricsRelationshipState,
} from "./library-metrics.ts";

export type PackagePerformance = BrowserPackagePerformance;
export type PackageResourceTriage = BrowserResourceTriage;
export type PackageLibraryMetrics = BrowserLibraryMetrics;
export type PackageLibraryDependencyStructure =
  BrowserLibraryDependencyStructure;

export interface ResolvedPackagePerformanceMember {
  type: AppTypeSurface;
  member: AppMemberSurface;
}

export function resolvePackagePerformanceMember(
  packageModel: AppPackage,
  performanceMember: Pick<
    BrowserPerformanceMember,
    "assembly" | "typeId" | "stableSelector"
  >,
): ResolvedPackagePerformanceMember | null {
  const type = packageModel.types.find(candidate =>
    candidate.assembly === performanceMember.assembly
    && candidate.definitionId === performanceMember.typeId);
  const member = type?.api.find(candidate =>
    candidate.stableSelector === performanceMember.stableSelector);
  return type && member ? { type, member } : null;
}

function dependencyProjectionError(
  result: BrowserPackageDependencies,
): string {
  const errors: string[] = [];
  if (result.dependencyGroupError) {
    errors.push(result.dependencyGroupError);
  }

  const failureCount = result.declarationFailures.length;
  if (failureCount > 0) {
    errors.push(
      `Dependency declaration projection is incomplete (${failureCount} ${
        failureCount === 1 ? "failure" : "failures"
      }).`,
    );
  }

  return errors.join(" ");
}

export interface PackageInspectionState {
  packages: AppPackage[];
  atPackageRoot: boolean;
  atLibraryRoot: boolean;
  packageLens: PackageLens;
  libraryLens: LibraryLens;
  packageDependencies: BrowserPackageDependencies | null;
  packageDependenciesLoading: boolean;
  packageDependenciesError: string;
  packageDependenciesKey: string;
  packageVulnerabilities: BrowserPackageVulnerabilityResult | null;
  packageVulnerabilitiesLoading: boolean;
  packageVulnerabilitiesError: string;
  packageVulnerabilitiesKey: string;
  workspaceDependencies: Record<string, DependencyGroupData>;
  workspaceDependencyErrors: Record<string, string>;
  workspaceDependencyLoads: Set<string>;
  packageIntegrations: BrowserPackageIntegrations | null;
  packageIntegrationsLoading: boolean;
  packageIntegrationsError: string;
  packageIntegrationsKey: string;
  packageOpportunities: BrowserPackageOpportunities | null;
  packageOpportunitiesLoading: boolean;
  packageOpportunitiesError: string;
  packageOpportunitiesKey: string;
  packagePerformance: PackagePerformance | null;
  packagePerformanceLoading: boolean;
  packagePerformanceError: string;
  packagePerformanceKey: string;
  packageResourceTriage: PackageResourceTriage | null;
  packageResourceTriageLoading: boolean;
  packageResourceTriageError: string;
  packageResourceTriageKey: string;
  packageLibraryMetrics: PackageLibraryMetrics | null;
  packageLibraryMetricsLoading: boolean;
  packageLibraryMetricsError: string;
  packageLibraryMetricsKey: string;
  packageLibraryMetricsRelationshipState:
    LibraryMetricsRelationshipState | null;
  packageLibraryDependencyStructure:
    PackageLibraryDependencyStructure | null;
  packageLibraryDependencyStructureLoading: boolean;
  packageLibraryDependencyStructureError: string;
  packageLibraryDependencyStructureKey: string;
  packageMetadata: PackageMetadata | null;
  packageMetadataLoading: boolean;
  packageMetadataError: string;
  packageMetadataKey: string;
  packageMetadataRoot: MetadataRootSelection;
}

export interface PackageInspectionDependencies {
  state: PackageInspectionState;
  queryDependencies(
    packageModel: PackageIdentity
      & Pick<AppPackage, "assemblyId" | "selectedCompileAssetId">,
  ): Promise<BrowserPackageDependencies>;
  queryVulnerabilities(
    packageModel: AppPackage,
  ): Promise<BrowserPackageVulnerabilityResult>;
  queryPackageIntegrations(
    packageModel: AppPackage,
    library: string,
  ): Promise<BrowserPackageIntegrations>;
  queryPlatformIntegrations(
    framework: string,
    platformVersion: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<BrowserPackageIntegrations>;
  queryPackageOpportunities(
    packageModel: AppPackage,
    library: string,
  ): Promise<BrowserPackageOpportunities>;
  queryPlatformOpportunities(
    framework: string,
    platformVersion: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<BrowserPackageOpportunities>;
  queryPackagePerformance(
    packageModel: AppPackage,
    library: string,
  ): Promise<PackagePerformance>;
  queryPackageResourceTriage(
    packageModel: AppPackage,
    library: string,
  ): Promise<PackageResourceTriage>;
  queryPackageLibraryMetrics(
    packageModel: AppPackage,
    library: string,
  ): Promise<PackageLibraryMetrics>;
  queryPackageLibraryDependencyStructure(
    packageModel: AppPackage,
    library: string,
  ): Promise<PackageLibraryDependencyStructure>;
  queryPlatformPerformance(
    framework: string,
    platformVersion: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<PackagePerformance>;
  queryPlatformResourceTriage(
    framework: string,
    platformVersion: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<PackageResourceTriage>;
  queryPlatformLibraryMetrics(
    framework: string,
    platformVersion: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<PackageLibraryMetrics>;
  queryPlatformLibraryDependencyStructure(
    framework: string,
    platformVersion: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<PackageLibraryDependencyStructure>;
  queryPackageMetadata(
    packageModel: AppPackage,
    library: string,
  ): Promise<PackageMetadata>;
  queryPlatformMetadata(
    framework: string,
    platformVersion: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<PackageMetadata>;
  platformLibraryCoordinates(
    packageModel: AppPackage,
    library: string,
  ): { assemblyFileName: string; pack: string };
  describeError(error: unknown): string;
  refreshPackageStats(): void;
  render(): void;
  renderDependencyGraph(): Promise<void>;
}

export interface PackageInspectionCoordinator {
  invalidatePackageResults(): void;
  loadDependencies(
    packageModel: AppPackage,
    signature: string,
  ): Promise<void>;
  loadVulnerabilities(
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
  loadResourceTriage(
    packageModel: AppPackage,
    signature: string,
    scopedLibrary: string | null,
  ): Promise<void>;
  loadLibraryMetrics(
    packageModel: AppPackage,
    signature: string,
    scopedLibrary: string | null,
  ): Promise<void>;
  loadLibraryDependencyStructure(
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
  let packageResultGeneration = 0;
  let metadataRequestSequence = 0;
  let vulnerabilityRequestSequence = 0;

  const platformCoordinates = (
    packageModel: AppPackage,
    scopedLibrary: string,
  ) => ({
    framework: packageModel.activeFramework,
    platformVersion: packageModel.version,
    ...dependencies.platformLibraryCoordinates(packageModel, scopedLibrary),
  });

  const ensureWorkspaceDependencies = async () => {
    const generation = packageResultGeneration;
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
      if (generation !== packageResultGeneration) return;
      const key = workspaceDependencyKey(packageModel);
      if (!packageIsResident(state.packages, packageModel)) continue;
      state.workspaceDependencyLoads.add(key);
      try {
        const result = await dependencies.queryDependencies(packageModel);
        if (generation !== packageResultGeneration) return;
        if (!packageIsResident(state.packages, packageModel)) continue;
        state.workspaceDependencies[key] = {
          dependencyGroups: result?.dependencyGroups || [],
          dependencyGroupError: result?.dependencyGroupError || "",
        };
        const projectionError = result
          ? dependencyProjectionError(result)
          : "";
        if (projectionError) {
          state.workspaceDependencyErrors[key] = projectionError;
        } else {
          delete state.workspaceDependencyErrors[key];
        }
      } catch (error) {
        if (generation !== packageResultGeneration) return;
        if (!packageIsResident(state.packages, packageModel)) continue;
        state.workspaceDependencies[key] = {
          dependencyGroups: [],
          dependencyGroupError: "",
        };
        state.workspaceDependencyErrors[key] =
          dependencies.describeError(error);
      } finally {
        if (generation === packageResultGeneration) {
          state.workspaceDependencyLoads.delete(key);
        }
      }
    }

    if (generation !== packageResultGeneration) return;
    if (state.atPackageRoot && state.packageLens === "dependencies") {
      dependencies.render();
    }
    dependencies.refreshPackageStats();
  };

  return {
    invalidatePackageResults() {
      packageResultGeneration++;
      state.workspaceDependencyLoads.clear();
      state.packageDependencies = null;
      state.packageDependenciesLoading = false;
      state.packageDependenciesError = "";
      state.packageDependenciesKey = "";
      state.packageVulnerabilities = null;
      state.packageVulnerabilitiesLoading = false;
      state.packageVulnerabilitiesError = "";
      state.packageVulnerabilitiesKey = "";
      state.packageIntegrations = null;
      state.packageIntegrationsLoading = false;
      state.packageIntegrationsError = "";
      state.packageIntegrationsKey = "";
      state.packageOpportunities = null;
      state.packageOpportunitiesLoading = false;
      state.packageOpportunitiesError = "";
      state.packageOpportunitiesKey = "";
      state.packagePerformance = null;
      state.packagePerformanceLoading = false;
      state.packagePerformanceError = "";
      state.packagePerformanceKey = "";
      state.packageResourceTriage = null;
      state.packageResourceTriageLoading = false;
      state.packageResourceTriageError = "";
      state.packageResourceTriageKey = "";
      state.packageLibraryMetrics = null;
      state.packageLibraryMetricsLoading = false;
      state.packageLibraryMetricsError = "";
      state.packageLibraryMetricsKey = "";
      state.packageLibraryMetricsRelationshipState = null;
      state.packageLibraryDependencyStructure = null;
      state.packageLibraryDependencyStructureLoading = false;
      state.packageLibraryDependencyStructureError = "";
      state.packageLibraryDependencyStructureKey = "";
      state.packageMetadata = null;
      state.packageMetadataLoading = false;
      state.packageMetadataError = "";
      state.packageMetadataKey = "";
      state.packageMetadataRoot = "cli";
    },

    async loadDependencies(packageModel, signature) {
      if (state.packageDependenciesKey === signature
        && (state.packageDependencies || state.packageDependenciesError)) {
        dependencies.render();
        return;
      }
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageDependenciesKey === signature
        && generation === packageResultGeneration;
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
        ...(packageModel.selectedCompileAssetId
          ? { selectedCompileAssetId: packageModel.selectedCompileAssetId }
          : {}),
      };
      const workspaceKey = workspaceDependencyKey(packageRequest);
      try {
        const result = await dependencies.queryDependencies(packageRequest);
        if (ownsRequest()) {
          state.packageDependencies = result;
        }
        if (result?.dependencyGroups
          && generation === packageResultGeneration
          && packageIsResident(state.packages, packageRequest)) {
          state.workspaceDependencies[workspaceKey] = {
            dependencyGroups: result.dependencyGroups,
            dependencyGroupError: result.dependencyGroupError || "",
          };
          const projectionError = dependencyProjectionError(result);
          if (projectionError) {
            state.workspaceDependencyErrors[workspaceKey] = projectionError;
          } else {
            delete state.workspaceDependencyErrors[workspaceKey];
          }
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageDependenciesError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageDependenciesLoading = false;
        }
        if (generation === packageResultGeneration) {
          dependencies.refreshPackageStats();
          dependencies.render();
          if (state.atPackageRoot && state.packageLens === "dependencies") {
            await ensureWorkspaceDependencies();
          }
        }
      }
    },

    async loadVulnerabilities(packageModel, signature) {
      if (state.packageVulnerabilitiesKey === signature
        && (state.packageVulnerabilities
          || state.packageVulnerabilitiesError)) {
        dependencies.render();
        return;
      }
      const requestSequence = ++vulnerabilityRequestSequence;
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageVulnerabilitiesKey === signature
        && vulnerabilityRequestSequence === requestSequence
        && generation === packageResultGeneration;
      state.packageVulnerabilitiesKey = signature;
      state.packageVulnerabilities = null;
      state.packageVulnerabilitiesError = "";
      state.packageVulnerabilitiesLoading = true;
      dependencies.render();
      try {
        const result = await dependencies.queryVulnerabilities(packageModel);
        if (ownsRequest()) {
          state.packageVulnerabilities = result;
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageVulnerabilitiesError =
            dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageVulnerabilitiesLoading = false;
          dependencies.render();
        }
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
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageIntegrationsKey === signature
        && generation === packageResultGeneration;
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
              coordinates.platformVersion,
              coordinates.assemblyFileName,
              coordinates.pack)
          : await dependencies.queryPackageIntegrations(
              packageModel,
              scopedLibrary ?? "");
        if (ownsRequest()) {
          state.packageIntegrations = result;
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageIntegrationsError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageIntegrationsLoading = false;
        }
        if (generation === packageResultGeneration) {
          dependencies.render();
        }
      }
    },

    async loadOpportunities(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageOpportunitiesKey === signature
        && (state.packageOpportunities || state.packageOpportunitiesError)) {
        dependencies.render();
        return;
      }
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageOpportunitiesKey === signature
        && generation === packageResultGeneration;
      state.packageOpportunitiesKey = signature;
      state.packageOpportunities = null;
      state.packageOpportunitiesError = "";
      state.packageOpportunitiesLoading = true;
      dependencies.render();
      try {
        const coordinates = packageModel.isRuntimePack
          ? platformCoordinates(packageModel, scopedLibrary ?? "")
          : null;
        const result = coordinates
          ? await dependencies.queryPlatformOpportunities(
              coordinates.framework,
              coordinates.platformVersion,
              coordinates.assemblyFileName,
              coordinates.pack)
          : await dependencies.queryPackageOpportunities(
              packageModel,
              scopedLibrary ?? "");
        if (ownsRequest()) {
          state.packageOpportunities = result;
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageOpportunitiesError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageOpportunitiesLoading = false;
        }
        if (generation === packageResultGeneration) {
          dependencies.render();
        }
      }
    },

    async loadPerformance(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packagePerformanceKey === signature
        && (state.packagePerformance || state.packagePerformanceError)) {
        dependencies.render();
        return;
      }
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packagePerformanceKey === signature
        && generation === packageResultGeneration;
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
              coordinates.platformVersion,
              coordinates.assemblyFileName,
              coordinates.pack)
          : await dependencies.queryPackagePerformance(
              packageModel,
              scopedLibrary ?? "");
        if (ownsRequest()) {
          state.packagePerformance = result;
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packagePerformanceError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packagePerformanceLoading = false;
        }
        if (generation === packageResultGeneration) {
          dependencies.render();
        }
      }
    },

    async loadResourceTriage(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageResourceTriageKey === signature
        && (state.packageResourceTriage || state.packageResourceTriageError)) {
        dependencies.render();
        return;
      }
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageResourceTriageKey === signature
        && generation === packageResultGeneration;
      state.packageResourceTriageKey = signature;
      state.packageResourceTriage = null;
      state.packageResourceTriageError = "";
      state.packageResourceTriageLoading = true;
      dependencies.render();
      try {
        const coordinates = packageModel.isRuntimePack
          ? platformCoordinates(packageModel, scopedLibrary ?? "")
          : null;
        const result = coordinates
          ? await dependencies.queryPlatformResourceTriage(
              coordinates.framework,
              coordinates.platformVersion,
              coordinates.assemblyFileName,
              coordinates.pack)
          : await dependencies.queryPackageResourceTriage(
              packageModel,
              scopedLibrary ?? "");
        if (ownsRequest()) {
          state.packageResourceTriage = result;
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageResourceTriageError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageResourceTriageLoading = false;
        }
        if (generation === packageResultGeneration) {
          dependencies.render();
        }
      }
    },

    async loadLibraryMetrics(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageLibraryMetricsKey === signature
        && (state.packageLibraryMetrics || state.packageLibraryMetricsError)) {
        dependencies.render();
        return;
      }
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageLibraryMetricsKey === signature
        && generation === packageResultGeneration;
      state.packageLibraryMetricsKey = signature;
      state.packageLibraryMetrics = null;
      state.packageLibraryMetricsError = "";
      state.packageLibraryMetricsLoading = true;
      state.packageLibraryMetricsRelationshipState = null;
      dependencies.render();
      try {
        const coordinates = packageModel.isRuntimePack
          ? platformCoordinates(packageModel, scopedLibrary ?? "")
          : null;
        const result = coordinates
          ? await dependencies.queryPlatformLibraryMetrics(
              coordinates.framework,
              coordinates.platformVersion,
              coordinates.assemblyFileName,
              coordinates.pack)
          : await dependencies.queryPackageLibraryMetrics(
              packageModel,
              scopedLibrary ?? "");
        if (ownsRequest()) {
          state.packageLibraryMetrics = result;
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageLibraryMetricsError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageLibraryMetricsLoading = false;
        }
        if (generation === packageResultGeneration) {
          dependencies.render();
        }
      }
    },

    async loadLibraryDependencyStructure(
      packageModel,
      signature,
      scopedLibrary,
    ) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageLibraryDependencyStructureKey === signature
        && (state.packageLibraryDependencyStructure
          || state.packageLibraryDependencyStructureLoading)) {
        dependencies.render();
        return;
      }
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageLibraryDependencyStructureKey === signature
        && generation === packageResultGeneration;
      state.packageLibraryDependencyStructureKey = signature;
      state.packageLibraryDependencyStructure = null;
      state.packageLibraryDependencyStructureError = "";
      state.packageLibraryDependencyStructureLoading = true;
      dependencies.render();
      try {
        const coordinates = packageModel.isRuntimePack
          ? platformCoordinates(packageModel, scopedLibrary ?? "")
          : null;
        const result = coordinates
          ? await dependencies.queryPlatformLibraryDependencyStructure(
              coordinates.framework,
              coordinates.platformVersion,
              coordinates.assemblyFileName,
              coordinates.pack)
          : await dependencies.queryPackageLibraryDependencyStructure(
              packageModel,
              scopedLibrary ?? "");
        if (ownsRequest()) {
          state.packageLibraryDependencyStructure = result;
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageLibraryDependencyStructureError =
            dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageLibraryDependencyStructureLoading = false;
        }
        if (generation === packageResultGeneration) {
          dependencies.render();
        }
      }
    },

    async loadMetadata(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      if (state.packageMetadataKey === signature
        && (state.packageMetadataLoading || state.packageMetadata)) {
        dependencies.render();
        return;
      }
      const requestSequence = ++metadataRequestSequence;
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageMetadataKey === signature
        && metadataRequestSequence === requestSequence
        && generation === packageResultGeneration;
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
              coordinates.platformVersion,
              coordinates.assemblyFileName,
              coordinates.pack)
          : await dependencies.queryPackageMetadata(
              packageModel,
              scopedLibrary ?? "");
        if (ownsRequest()) {
          const completeFailure = (result.assemblies?.length ?? 0) === 0
            ? result.inspectionError
            : null;
          state.packageMetadata = completeFailure ? null : result;
          state.packageMetadataError = completeFailure || "";
        }
      } catch (error) {
        if (ownsRequest()) {
          state.packageMetadataError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.packageMetadataLoading = false;
          dependencies.render();
        }
      }
    },
  };
}
