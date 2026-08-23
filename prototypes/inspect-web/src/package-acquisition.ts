import { mergeInspectionErrors } from "./data.ts";
import type {
  BrowserAccessibilityDescriptor,
  BrowserAssemblySurface,
  BrowserPackageDocument,
  BrowserPackageSurface,
  BrowserTypeSurface,
} from "./inspect-web-engine.d.ts";

export interface AppPackage {
  id: string;
  version: string;
  frameworks: string[];
  activeFramework: string;
  assembly: string;
  assemblyId: string;
  assemblyAsset: string;
  source:
    | { kind: "file" }
    | { kind: "nuget.org" }
    | { kind: "feed"; host: string }
    | { kind: "platform" }
    | { kind: "unknown" };
  assemblies: BrowserAssemblySurface[];
  types: BrowserTypeSurface[];
  accessibility: BrowserAccessibilityDescriptor[];
  totalTypes: number;
  totalMembers: number;
  documents: BrowserPackageDocument[];
  inspectionError?: string;
  isRuntimePack: boolean;
}

const DEFAULT_RUNTIME_ASSEMBLY = "System.Private.CoreLib";

export function runtimeAssemblyIsResident(
  packageModel: Pick<AppPackage, "assemblies"> | null | undefined,
  assemblyName: string,
  platformPack: string,
): boolean {
  const normalized = assemblyName.replace(/\.dll$/i, "").toLowerCase();
  return packageModel?.assemblies.some(assembly =>
    assembly.name.toLowerCase() === normalized
    && assembly.platformPack === platformPack) ?? false;
}

export function runtimePackIsResident(
  packageModel: Pick<AppPackage, "assemblies"> | null | undefined,
): boolean {
  return packageModel?.assemblies.some(assembly =>
    assembly.name.toLowerCase()
      === DEFAULT_RUNTIME_ASSEMBLY.toLowerCase()) ?? false;
}

function packageTypes(result: BrowserPackageSurface): BrowserTypeSurface[] {
  return (result.types ?? []).map(type => ({
    ...type,
    api: type.api ?? [],
  }));
}

// The two validations are separate because they belong on different paths, and round 6
// review (GPT-5.6 Sol) showed what collapsing them costs.
//
// A *blank* identity is never legitimate: `defaultAssemblyId` is declared non-optional,
// and a surface without one produces a package model with a blank identity whatever is
// done with it. An *unmatched* identity is legitimate -- `InspectionEngine.cs` permits an
// empty `assemblies` list whenever extraction truncates and then falls back to
// `coordinate.DefaultAsset.Id`, an id matching no descriptor -- and those surfaces still
// carry types worth merging.
//
// Round 5 moved the whole check after the merge branch to stop rejecting that truncated
// surface, which also stopped rejecting blank identities on the resident-merge path.
// Splitting the checks keeps both properties: identity is required everywhere, a matching
// descriptor only where one is actually read.
function requireAssemblyIdentity(
  result: BrowserPackageSurface,
  failureMessage: string,
): string {
  const defaultAssemblyId = result.defaultAssemblyId;
  if (typeof defaultAssemblyId !== "string"
    || defaultAssemblyId.trim().length === 0) {
    throw new Error(failureMessage);
  }
  return defaultAssemblyId;
}

// The descriptor the surface's declared default names, or null when none matches.
function selectedAssembly(
  result: BrowserPackageSurface,
): BrowserAssemblySurface | null {
  const defaultAssemblyId = result.defaultAssemblyId;
  if (typeof defaultAssemblyId !== "string"
    || defaultAssemblyId.trim().length === 0) {
    return null;
  }
  return (result.assemblies ?? [])
    .find(candidate => candidate.id === defaultAssemblyId) ?? null;
}

function defaultAssembly(
  result: BrowserPackageSurface,
  failureMessage: string,
): BrowserAssemblySurface {
  requireAssemblyIdentity(result, failureMessage);
  const assembly = selectedAssembly(result);
  if (!assembly) throw new Error(failureMessage);
  return assembly;
}

export function createNuGetPackageModel(
  result: BrowserPackageSurface,
): AppPackage {
  const assembly = defaultAssembly(
    result,
    "The package query did not return its selected assembly descriptor.");
  return {
    id: result.package,
    version: result.version,
    frameworks: result.frameworks ?? [],
    activeFramework: result.activeFramework,
    assembly: assembly.name,
    assemblyId: assembly.id,
    assemblyAsset: assembly.asset,
    source: { kind: "nuget.org" },
    assemblies: result.assemblies ?? [],
    types: packageTypes(result),
    accessibility: result.accessibility ?? [],
    totalTypes: (result.assemblies ?? [])
      .reduce((count, candidate) => count + (candidate.publicTypes ?? 0), 0),
    totalMembers: result.totalMembers,
    documents: result.documents ?? [],
    inspectionError: result.inspectionError || "",
    isRuntimePack: false,
  };
}

export function createRuntimePackageModel(
  result: BrowserPackageSurface,
): AppPackage {
  const assembly = defaultAssembly(
    result,
    "The platform query did not return its selected assembly descriptor.");
  return createRuntimePackageModelForAssembly(result, assembly, assembly.id);
}

function createRuntimeAssemblyPackageModel(
  result: BrowserPackageSurface,
  requestedAssembly: string,
): AppPackage {
  // `requestedAssembly` and the no-descriptor failure come from main (#4405); selecting
  // the descriptor the surface's *declared default* names comes from this slice.
  // Projecting `assemblies[0]` while reporting `defaultAssemblyId` as the identity builds
  // a model that names one assembly and identifies another. The two agree for every
  // surface the engine emits today -- a runtime-pack assembly load returns the one
  // assembly it was asked for -- which is why nothing caught it.
  const assembly = selectedAssembly(result);
  if (!assembly) {
    throw new Error(
      result.inspectionError
      || `The platform query returned no descriptor for ${requestedAssembly}.`);
  }
  return createRuntimePackageModelForAssembly(result, assembly, assembly.id);
}

function createRuntimePackageModelForAssembly(
  result: BrowserPackageSurface,
  assembly: BrowserAssemblySurface,
  assemblyId: string,
): AppPackage {
  const types = packageTypes(result);
  return {
    id: result.package,
    version: result.version,
    frameworks: result.frameworks ?? [],
    activeFramework: result.activeFramework,
    assembly: assembly.name,
    assemblyId,
    assemblyAsset: assembly.asset,
    source: { kind: "platform" },
    assemblies: result.assemblies ?? [],
    types,
    accessibility: result.accessibility ?? [],
    totalTypes: types.length,
    totalMembers: result.totalMembers,
    documents: result.documents ?? [],
    inspectionError: result.inspectionError || "",
    isRuntimePack: true,
  };
}

export function mergeRuntimePackageSurface(
  existing: AppPackage,
  result: BrowserPackageSurface,
): AppPackage {
  const newTypes = packageTypes(result);
  const seenTypes = new Set(existing.types.map(type => type.id));
  for (const type of newTypes) {
    if (!seenTypes.has(type.id)) existing.types.push(type);
  }

  const assemblyKey = (assembly: BrowserAssemblySurface) => [
    assembly.name.toLowerCase(),
    assembly.version ?? "",
    (assembly.culture ?? "").toLowerCase() === "neutral"
      ? ""
      : (assembly.culture ?? "").toLowerCase(),
    (assembly.publicKeyToken ?? "").toLowerCase(),
    assembly.platformPack ?? "",
  ].join("\0");
  const seenAssemblies = new Set(existing.assemblies.map(assemblyKey));
  for (const assembly of result.assemblies ?? []) {
    if (!seenAssemblies.has(assemblyKey(assembly))) {
      existing.assemblies.push(assembly);
    }
  }

  const descriptors = new Map(
    existing.accessibility.map(descriptor => [descriptor.id, descriptor]));
  for (const descriptor of result.accessibility ?? []) {
    const current = descriptors.get(descriptor.id);
    descriptors.set(descriptor.id, current
      ? { ...current, count: current.count + descriptor.count }
      : descriptor);
  }
  existing.accessibility = [...descriptors.values()]
    .sort((left, right) => left.order - right.order);
  existing.totalTypes = existing.types.length;
  existing.totalMembers = (existing.totalMembers || 0) + (result.totalMembers || 0);
  existing.inspectionError = mergeInspectionErrors(
    existing.inspectionError,
    result.inspectionError);
  return existing;
}

function promoteRuntimePackagePrimary(
  existing: AppPackage,
  primary: AppPackage,
) {
  existing.version = primary.version;
  existing.frameworks = primary.frameworks;
  existing.activeFramework = primary.activeFramework;
  existing.assembly = primary.assembly;
  existing.assemblyId = primary.assemblyId;
  existing.assemblyAsset = primary.assemblyAsset;
}

export interface PackageAcquisitionDependencies {
  queryPackage(
    packageId: string,
    version: string,
    framework: string,
  ): Promise<BrowserPackageSurface>;
  loadRuntimePack(framework: string): Promise<string>;
  loadRuntimePackAssembly(
    framework: string,
    assemblyFileName: string,
    pack: string,
  ): Promise<string>;
  parseRuntimeSurface(json: string): BrowserPackageSurface;
  runtimePackage(): AppPackage | null;
  retainPackage(packageModel: AppPackage, replacedPackage?: AppPackage | null): void;
  recordRecentPackage(id: string, version: string, framework: string): void;
  refreshPackageStats(): void;
  beginRuntimeLoad(): void;
  failRuntimeLoad(error: unknown): void;
  endRuntimeLoad(): void;
}

export interface NuGetPackageRequest {
  packageId: string;
  version: string;
  framework: string;
  replacePackage?: AppPackage | null;
  isCurrent?: () => boolean;
}

export interface RuntimeAcquisitionResult {
  packageModel: AppPackage | null;
  error: unknown;
}

export interface PackageAcquisition {
  loadPackage(request: NuGetPackageRequest): Promise<AppPackage | null>;
  loadRuntimePack(
    framework: string,
    isCurrent?: () => boolean,
  ): Promise<RuntimeAcquisitionResult>;
  loadRuntimePackAssembly(
    framework: string,
    assemblyFileName: string,
    pack: string,
    isCurrent?: () => boolean,
  ): Promise<RuntimeAcquisitionResult>;
}

export function createPackageAcquisition(
  dependencies: PackageAcquisitionDependencies,
): PackageAcquisition {
  let runtimeTail: Promise<void> | null = null;

  const enqueueRuntimeRequest = async (
    operation: () => Promise<RuntimeAcquisitionResult>,
  ): Promise<RuntimeAcquisitionResult> => {
    const predecessor = runtimeTail;
    let release!: () => void;
    const slot = new Promise<void>(resolve => {
      release = resolve;
    });
    runtimeTail = slot;
    if (predecessor) await predecessor;
    try {
      return await operation();
    } finally {
      release();
      if (runtimeTail === slot) runtimeTail = null;
    }
  };

  const runRuntimeOperation = async (
    operation: () => Promise<AppPackage | null>,
    isCurrent: () => boolean,
  ): Promise<RuntimeAcquisitionResult> => {
    dependencies.beginRuntimeLoad();
    try {
      return {
        packageModel: await operation(),
        error: null,
      };
    } catch (error) {
      if (isCurrent()) dependencies.failRuntimeLoad(error);
      return {
        packageModel: null,
        error,
      };
    } finally {
      dependencies.endRuntimeLoad();
    }
  };

  return {
    async loadPackage(request) {
      const result = await dependencies.queryPackage(
        request.packageId,
        request.version,
        request.framework);
      if (request.isCurrent && !request.isCurrent()) return null;
      dependencies.refreshPackageStats();
      const packageModel = createNuGetPackageModel(result);
      dependencies.retainPackage(packageModel, request.replacePackage);
      dependencies.recordRecentPackage(
        packageModel.id,
        packageModel.version,
        packageModel.activeFramework);
      return packageModel;
    },

    async loadRuntimePack(framework, isCurrent = () => true) {
      return enqueueRuntimeRequest(async () => {
        if (!isCurrent()) return { packageModel: null, error: null };
        const requestedFramework = framework || "";
        const existing = dependencies.runtimePackage();
        if (existing
          && runtimePackIsResident(existing)
          && (!requestedFramework
            || existing.activeFramework.toLowerCase()
              === requestedFramework.toLowerCase())) {
          return { packageModel: existing, error: null };
        }

        return runRuntimeOperation(async () => {
          const result = dependencies.parseRuntimeSurface(
            await dependencies.loadRuntimePack(requestedFramework));
          if (!isCurrent()) return null;
          dependencies.refreshPackageStats();
          const packageModel = createRuntimePackageModel(result);
          const current = dependencies.runtimePackage();
          if (current
            && (!requestedFramework
              || current.activeFramework.toLowerCase()
                === requestedFramework.toLowerCase())) {
            mergeRuntimePackageSurface(current, result);
            promoteRuntimePackagePrimary(current, packageModel);
            return current;
          }
          dependencies.retainPackage(packageModel, existing);
          return packageModel;
        }, isCurrent);
      });
    },

    async loadRuntimePackAssembly(
      framework,
      assemblyFileName,
      pack,
      isCurrent = () => true,
    ) {
      return enqueueRuntimeRequest(async () => {
        if (!isCurrent()) return { packageModel: null, error: null };
        const requestedFramework = framework || "";
        const requestedAssembly = assemblyFileName
          .replace(/\.dll$/i, "");
        const resident = dependencies.runtimePackage();
        if (resident
          && (!requestedFramework
            || resident.activeFramework.toLowerCase()
              === requestedFramework.toLowerCase())) {
          if (runtimeAssemblyIsResident(
            resident,
            requestedAssembly,
            pack)) {
            return { packageModel: resident, error: null };
          }
        }

        return runRuntimeOperation(async () => {
          const result = dependencies.parseRuntimeSurface(
            await dependencies.loadRuntimePackAssembly(
              requestedFramework,
              assemblyFileName,
              pack || ""));
          if (!isCurrent()) return null;
          dependencies.refreshPackageStats();
          const existing = dependencies.runtimePackage();
          // Identity is required on every path, including the merge. Round 6 review
          // (GPT-5.6 Sol) showed that returning through the merge without it let a
          // surface with an absent, empty, or whitespace `defaultAssemblyId` succeed --
          // and mutate the resident package -- whenever a same-framework runtime package
          // happened to be resident. A matching descriptor is still only required below,
          // where one is actually read.
          requireAssemblyIdentity(
            result,
            "The platform assembly query did not return its selected assembly identity.");
          if (existing
            && (!requestedFramework
              || existing.activeFramework.toLowerCase()
                === requestedFramework.toLowerCase())) {
            const merged = mergeRuntimePackageSurface(existing, result);
            const primary = result.assemblies?.[0];
            // Promotion builds a package model, so it needs a descriptor. A truncated
            // surface has none, and round 5 established that such a surface must still
            // merge rather than fail the whole load -- so skip the promotion instead of
            // letting the model construction throw.
            if (selectedAssembly(result)
              && primary?.name.toLowerCase()
                === DEFAULT_RUNTIME_ASSEMBLY.toLowerCase()) {
              promoteRuntimePackagePrimary(
                merged,
                createRuntimeAssemblyPackageModel(
                  result,
                  requestedAssembly));
            }
            return merged;
          }
          const packageModel = createRuntimeAssemblyPackageModel(
            result,
            requestedAssembly);
          dependencies.retainPackage(packageModel, existing);
          return packageModel;
        }, isCurrent);
      });
    },
  };
}
