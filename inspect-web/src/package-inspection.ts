import {
  packageIdentityKey,
  type DependencyGroup,
  type DependencyGroupData,
  type LibraryLens,
  type PackageLens,
  type PackageIdentity,
} from "./data.ts";
import type {
  BrowserPackageDependencyInspection,
  BrowserPackageVulnerabilityResult,
  InspectionEnvelope,
  LibraryReferenceInspectionOutcome,
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
import { createAppTypeSurface } from "./package-acquisition.ts";
import type {
  LibraryMetricsRelationshipState,
} from "./library-metrics.ts";

export type PackagePerformance = BrowserPackagePerformance;
export type PackageResourceTriage = BrowserResourceTriage;
export type PackageLibraryMetrics = BrowserLibraryMetrics;
export type PackageLibraryDependencyStructure =
  BrowserLibraryDependencyStructure;

export type PackageInspectionRequestState<TResult> =
  | { readonly status: "idle" }
  | {
    readonly status: "loading";
    readonly request: { readonly key: string };
  }
  | {
    readonly status: "ready";
    readonly key: string;
    readonly result: TResult;
  }
  | {
    readonly status: "failed";
    readonly key: string;
    readonly error: string;
  };

export function packageInspectionRequestStateKey<TResult>(
  state: PackageInspectionRequestState<TResult>,
): string | null {
  if (state.status === "idle") return null;
  return state.status === "loading" ? state.request.key : state.key;
}

export function normalizePackageInspectionRequestSnapshot<TResult>(
  state: PackageInspectionRequestState<TResult> | null | undefined,
): PackageInspectionRequestState<TResult> {
  return !state || state.status === "loading" ? { status: "idle" } : state;
}

export type PackageDependenciesState =
  PackageInspectionRequestState<BrowserPackageDependencyInspection>;
export type LibraryReferenceInspectionEnvelope =
  InspectionEnvelope<LibraryReferenceInspectionOutcome | null>;
export type LibraryReferenceInspectionState =
  PackageInspectionRequestState<LibraryReferenceInspectionEnvelope>;
export type PackageVulnerabilityState =
  PackageInspectionRequestState<BrowserPackageVulnerabilityResult>;
export type PackageIntegrationsState =
  PackageInspectionRequestState<BrowserPackageIntegrations>;
export type PackageOpportunitiesState =
  PackageInspectionRequestState<BrowserPackageOpportunities>;
export type PackageMetadataState =
  PackageInspectionRequestState<PackageMetadata>;

export interface ResolvedPackagePerformanceMember {
  type: AppTypeSurface;
  member: AppMemberSurface;
}

export function resolvePackagePerformanceType(
  packageModel: AppPackage,
  performanceMember: Pick<
    BrowserPerformanceMember,
    "assembly" | "typeId"
  >,
): AppTypeSurface | null {
  return packageModel.types.find(candidate =>
    candidate.assembly === performanceMember.assembly
    && candidate.definitionId === performanceMember.typeId) ?? null;
}

export function projectPackagePerformanceType(
  performanceMember: BrowserPerformanceMember,
): AppTypeSurface {
  return createAppTypeSurface({
    ...performanceMember.declaringType,
    api: [],
  });
}

export function resolvePackagePerformanceMember(
  packageModel: AppPackage,
  performanceMember: Pick<
    BrowserPerformanceMember,
    "assembly" | "typeId" | "stableSelector"
  >,
): ResolvedPackagePerformanceMember | null {
  const type = resolvePackagePerformanceType(packageModel, performanceMember);
  const member = type?.api.find(candidate =>
    candidate.stableSelector === performanceMember.stableSelector);
  return type && member ? { type, member } : null;
}

export interface PackageDependencyGroupProjection extends DependencyGroup {
  readonly dependencies: readonly {
    readonly id: string;
    readonly versionRange: string;
  }[];
}

export interface PackageDependencyProjection extends DependencyGroupData {
  readonly dependencyGroups: readonly PackageDependencyGroupProjection[];
  readonly dependencyGroupError: string;
  readonly platformReferences: readonly {
    readonly family: string;
    readonly label: string;
  }[];
  readonly defaultPlatformReference: {
    readonly family: string;
    readonly label: string;
  } | null;
  readonly platformReferenceError: string | null;
  readonly inspectionError: string;
}

function projectPackageDependencyFramework(framework: string): string | null {
  if (framework.trim().length === 0) return "any";
  if (framework.length > 128
    || !/^[A-Za-z0-9.\-+_,= ]+$/.test(framework)) {
    return null;
  }
  return framework;
}

export function projectPackageDependencyInspection(
  result: BrowserPackageDependencyInspection | null | undefined,
): PackageDependencyProjection {
  const inspection = result?.inspection;
  const content = inspection?.content;
  const platform = {
    platformReferences: result?.platformReferences ?? [],
    defaultPlatformReference: result?.defaultPlatformReference ?? null,
    platformReferenceError: result?.platformReferenceError ?? null,
  };
  if (!content) {
    return {
      ...platform,
      dependencyGroups: [],
      dependencyGroupError: "",
      inspectionError:
        "Package dependency inspection returned no shared content.",
    };
  }
  if (content.kind === "no-manifest") {
    return {
      ...platform,
      dependencyGroups: [],
      dependencyGroupError: "",
      inspectionError:
        "The package contains no root manifest; dependencies are unavailable.",
    };
  }
  if (content.kind === "failed") {
    const diagnostic = inspection.diagnostics
      .map(candidate => candidate.summary)
      .find(summary => summary.trim());
    return {
      ...platform,
      dependencyGroups: [],
      dependencyGroupError: "",
      inspectionError: diagnostic
        ?? "The package dependency manifest could not be inspected.",
    };
  }

  const selectedGroupIndex = content.dependencies.selectedGroupIndex;
  const selectedGroup = content.dependencies.selectedGroup;
  const frameworks = content.dependencies.groups.map(group =>
    projectPackageDependencyFramework(group.targetFramework));
  if (frameworks.some(framework => framework === null)) {
    return {
      ...platform,
      dependencyGroups: [],
      dependencyGroupError: "",
      inspectionError:
        "A dependency group framework identifier cannot be represented safely.",
    };
  }
  const dependencyGroups = content.dependencies.groups.map((group, index) => ({
    index,
    framework: frameworks[index] ?? "any",
    isActive: index === selectedGroupIndex,
    isComplete: true,
    dependencies: (index === selectedGroupIndex && selectedGroup
      ? selectedGroup
      : group).dependencies.map(dependency => ({
        id: dependency.id,
        versionRange: dependency.versionRange,
      })),
  }));
  return {
    ...platform,
    dependencyGroups,
    dependencyGroupError:
      dependencyGroups.length > 0 && selectedGroupIndex === null
        ? "The manifest declares no dependency group for the active target framework."
        : "",
    inspectionError: "",
  };
}

function dependencyProjectionError(
  result: PackageDependencyProjection,
): string {
  const errors: string[] = [];
  if (result.inspectionError) {
    errors.push(result.inspectionError);
  }
  if (result.dependencyGroupError) {
    errors.push(result.dependencyGroupError);
  }

  return errors.join(" ");
}

export interface PackageInspectionState {
  packages: AppPackage[];
  atPackageRoot: boolean;
  atLibraryRoot: boolean;
  packageLens: PackageLens;
  libraryLens: LibraryLens;
  packageDependencies: PackageDependenciesState;
  libraryReferences: LibraryReferenceInspectionState;
  packageVulnerabilities: PackageVulnerabilityState;
  workspaceDependencies: Record<string, DependencyGroupData>;
  workspaceDependencyErrors: Record<string, string>;
  workspaceDependencyLoads: Set<string>;
  packageIntegrations: PackageIntegrationsState;
  packageOpportunities: PackageOpportunitiesState;
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
  packageMetadata: PackageMetadataState;
  packageMetadataRoot: MetadataRootSelection;
}

export interface PackageInspectionDependencies {
  state: PackageInspectionState;
  queryDependencies(
    packageModel: PackageIdentity & Pick<AppPackage, "runtimeIdentifier">,
  ): Promise<BrowserPackageDependencyInspection>;
  queryLibraryReferences(
    packageModel: PackageIdentity
      & Pick<AppPackage, "assemblyId" | "selectedCompileAssetId">,
  ): Promise<LibraryReferenceInspectionEnvelope>;
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
  loadLibraryReferences(
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
        const projection = projectPackageDependencyInspection(result);
        if (generation !== packageResultGeneration) return;
        if (!packageIsResident(state.packages, packageModel)) continue;
        state.workspaceDependencies[key] = projection;
        const projectionError = dependencyProjectionError(projection);
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
      state.packageDependencies = { status: "idle" };
      state.libraryReferences = { status: "idle" };
      state.packageVulnerabilities = { status: "idle" };
      state.packageIntegrations = { status: "idle" };
      state.packageOpportunities = { status: "idle" };
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
      state.packageMetadata = { status: "idle" };
      state.packageMetadataRoot = "cli";
    },

    async loadDependencies(packageModel, signature) {
      const current = state.packageDependencies;
      if (current.status !== "idle"
        && packageInspectionRequestStateKey(current) === signature) {
        dependencies.render();
        return;
      }
      const request = { key: signature };
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageDependencies.status === "loading"
        && state.packageDependencies.request === request
        && generation === packageResultGeneration;
      state.packageDependencies = { status: "loading", request };
      dependencies.render();
      const packageRequest = {
        id: packageModel.id,
        version: packageModel.version,
        activeFramework: packageModel.activeFramework,
        runtimeIdentifier: packageModel.runtimeIdentifier ?? null,
      };
      const workspaceKey = workspaceDependencyKey(packageRequest);
      try {
        const result = await dependencies.queryDependencies(packageRequest);
        if (ownsRequest()) {
          state.packageDependencies = {
            status: "ready",
            key: signature,
            result,
          };
        }
        const projection = projectPackageDependencyInspection(result);
        if (generation === packageResultGeneration
          && packageIsResident(state.packages, packageRequest)) {
          state.workspaceDependencies[workspaceKey] = projection;
          const projectionError = dependencyProjectionError(projection);
          if (projectionError) {
            state.workspaceDependencyErrors[workspaceKey] = projectionError;
          } else {
            delete state.workspaceDependencyErrors[workspaceKey];
          }
        }
      } catch (error) {
        if (ownsRequest()) {
          const diagnostic = dependencies.describeError(error);
          state.packageDependencies = {
            status: "failed",
            key: signature,
            error: diagnostic.trim()
              ? diagnostic
              : "Dependency query failed.",
          };
        }
      } finally {
        if (generation === packageResultGeneration) {
          dependencies.refreshPackageStats();
          dependencies.render();
          if (state.atPackageRoot && state.packageLens === "dependencies") {
            await ensureWorkspaceDependencies();
          }
        }
      }
    },

    async loadLibraryReferences(packageModel, signature) {
      const current = state.libraryReferences;
      if (current.status !== "idle"
        && packageInspectionRequestStateKey(current) === signature) {
        dependencies.render();
        return;
      }
      const request = { key: signature };
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.libraryReferences.status === "loading"
        && state.libraryReferences.request === request
        && generation === packageResultGeneration;
      state.libraryReferences = { status: "loading", request };
      dependencies.render();
      const packageRequest = {
        id: packageModel.id,
        version: packageModel.version,
        activeFramework: packageModel.activeFramework,
        runtimeIdentifier: packageModel.runtimeIdentifier ?? null,
        assemblyId: packageModel.assemblyId,
        ...(packageModel.selectedCompileAssetId
          ? { selectedCompileAssetId: packageModel.selectedCompileAssetId }
          : {}),
      };
      try {
        const result =
          await dependencies.queryLibraryReferences(packageRequest);
        if (ownsRequest()) {
          state.libraryReferences = {
            status: "ready",
            key: signature,
            result,
          };
        }
      } catch (error) {
        if (ownsRequest()) {
          const diagnostic = dependencies.describeError(error);
          state.libraryReferences = {
            status: "failed",
            key: signature,
            error: diagnostic.trim()
              ? diagnostic
              : "Reference query failed.",
          };
        }
      } finally {
        if (generation === packageResultGeneration) {
          dependencies.refreshPackageStats();
          dependencies.render();
        }
      }
    },

    async loadVulnerabilities(packageModel, signature) {
      const current = state.packageVulnerabilities;
      if ((current.status === "ready" || current.status === "failed")
        && current.key === signature) {
        dependencies.render();
        return;
      }
      const request = { key: signature };
      const ownsRequest = () =>
        state.packageVulnerabilities.status === "loading"
        && state.packageVulnerabilities.request === request;
      state.packageVulnerabilities = { status: "loading", request };
      dependencies.render();
      try {
        const result = await dependencies.queryVulnerabilities(packageModel);
        if (!ownsRequest()) return;
        state.packageVulnerabilities = {
          status: "ready",
          key: signature,
          result,
        };
      } catch (error) {
        if (!ownsRequest()) return;
        state.packageVulnerabilities = {
          status: "failed",
          key: signature,
          error: dependencies.describeError(error),
        };
      }
      dependencies.render();
    },

    ensureWorkspaceDependencies,

    async loadIntegrations(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      const current = state.packageIntegrations;
      if ((current.status === "ready" || current.status === "failed")
        && current.key === signature) {
        dependencies.render();
        return;
      }
      const request = { key: signature };
      const ownsRequest = () =>
        state.packageIntegrations.status === "loading"
        && state.packageIntegrations.request === request;
      state.packageIntegrations = { status: "loading", request };
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
        if (!ownsRequest()) return;
        state.packageIntegrations = {
          status: "ready",
          key: signature,
          result,
        };
      } catch (error) {
        if (!ownsRequest()) return;
        state.packageIntegrations = {
          status: "failed",
          key: signature,
          error: dependencies.describeError(error),
        };
      }
      dependencies.render();
    },

    async loadOpportunities(packageModel, signature, scopedLibrary) {
      if (packageModel.isRuntimePack && !scopedLibrary) return;
      const current = state.packageOpportunities;
      if ((current.status === "ready" || current.status === "failed")
        && current.key === signature) {
        dependencies.render();
        return;
      }
      const request = { key: signature };
      const ownsRequest = () =>
        state.packageOpportunities.status === "loading"
        && state.packageOpportunities.request === request;
      state.packageOpportunities = { status: "loading", request };
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
        if (!ownsRequest()) return;
        state.packageOpportunities = {
          status: "ready",
          key: signature,
          result,
        };
      } catch (error) {
        if (!ownsRequest()) return;
        const diagnostic = dependencies.describeError(error);
        state.packageOpportunities = {
          status: "failed",
          key: signature,
          error: diagnostic.trim()
            ? diagnostic
            : "Opportunity inspection failed.",
        };
      }
      dependencies.render();
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
      const current = state.packageMetadata;
      if ((current.status === "loading"
          && current.request.key === signature)
        || (current.status === "ready" && current.key === signature)) {
        dependencies.render();
        return;
      }
      const request = { key: signature };
      const generation = packageResultGeneration;
      const ownsRequest = () =>
        state.packageMetadata.status === "loading"
        && state.packageMetadata.request === request
        && generation === packageResultGeneration;
      state.packageMetadata = { status: "loading", request };
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
        if (!ownsRequest()) return;
        const completeFailure = (result.assemblies?.length ?? 0) === 0
          ? result.inspectionError
          : null;
        state.packageMetadata = completeFailure
          ? {
            status: "failed",
            key: signature,
            error: completeFailure.trim()
              ? completeFailure
              : "Metadata inspection failed.",
          }
          : { status: "ready", key: signature, result };
      } catch (error) {
        if (!ownsRequest()) return;
        const diagnostic = dependencies.describeError(error);
        state.packageMetadata = {
          status: "failed",
          key: signature,
          error: diagnostic.trim()
            ? diagnostic
            : "Metadata inspection failed.",
        };
      }
      dependencies.render();
    },
  };
}
