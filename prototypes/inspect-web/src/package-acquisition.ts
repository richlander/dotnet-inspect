import {
  graphMemberTargetWithSelectedBody,
  mergeInspectionErrorEntries,
  retainGraphOnlyBodyTarget,
  renderInspectionErrors,
} from "./data.ts";
import type {
  BrowserAccessibilityDescriptor,
  BrowserAssemblySurface,
  BrowserExceptionSurface,
  BrowserMemberBodySelector,
  BrowserMemberSurface,
  BrowserPackageDocument,
  BrowserPackageSurface,
  BrowserParameterSurface,
  BrowserTypeSurface,
} from "./inspect-web-engine.d.ts";
import type { BodyTarget } from "./member-filtering.ts";

export interface AppParameterSurface
  extends Omit<BrowserParameterSurface, "description"> {
  description: string | null;
}

export interface AppMemberSurface
  extends Omit<
    BrowserMemberSurface,
    "parameters" | "summary" | "returns" | "exceptions"
  > {
  parameters: AppParameterSurface[];
  summary: string | null;
  returns: string | null;
  exceptions: BrowserExceptionSurface[];
  documentationLoaded?: boolean;
  graphOnly?: boolean;
  graphTarget?: BodyTarget;
  implementationBody?: BrowserMemberBodySelector;
}

export interface AppTypeSurface extends Omit<BrowserTypeSurface, "api"> {
  api: AppMemberSurface[];
}

export interface AppPackage {
  id: string;
  version: string;
  workspaceIndex?: number;
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
  types: AppTypeSurface[];
  accessibility: BrowserAccessibilityDescriptor[];
  totalTypes: number;
  totalMembers: number;
  documents: BrowserPackageDocument[];
  inspectionErrors?: string[];
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

export function createAppMemberSurface(
  surface: BrowserMemberSurface,
): AppMemberSurface {
  return {
    ...surface,
    parameters: surface.parameters.map(parameter => ({ ...parameter })),
    exceptions: [...surface.exceptions],
  };
}

export function retainGraphOnlyImplementationBody<
  TTarget extends BodyTarget,
>(
  overload: AppMemberSurface | null | undefined,
  target: TTarget | null | undefined,
): TTarget | null {
  if (!overload?.graphOnly) return target ?? null;
  if (!target) {
    delete overload.implementationBody;
    return null;
  }
  const selectedBody = overload.bodySelectors.find(body =>
    body.memberName === target.memberName
    && body.selectorKey === target.selectorKey);
  if (!selectedBody) {
    delete overload.implementationBody;
    retainGraphOnlyBodyTarget(overload, target);
    return target;
  }

  overload.implementationBody = selectedBody;
  const canonicalTarget =
    graphMemberTargetWithSelectedBody(target, selectedBody);
  retainGraphOnlyBodyTarget(overload, canonicalTarget);
  return canonicalTarget;
}

export function graphOnlyImplementationBody(
  overload: AppMemberSurface | null | undefined,
): BrowserMemberBodySelector | undefined {
  return overload?.graphOnly
    ? overload.implementationBody
    : undefined;
}

function packageTypes(result: BrowserPackageSurface): AppTypeSurface[] {
  return (result.types ?? []).map(type => ({
    ...type,
    api: (type.api ?? []).map(createAppMemberSurface),
  }));
}

function surfaceInspectionErrors(result: BrowserPackageSurface): string[] {
  return result.inspectionErrors?.length
    ? [...result.inspectionErrors]
    : mergeInspectionErrorEntries([], result.inspectionError
      ? [result.inspectionError]
      : []);
}

// The two validations are separate because they belong on different paths, and round 6
// review (GPT-5.6 Sol) showed what collapsing them costs.
//
// A *blank* identity is never legitimate for a surface whose compile library is selected.
// Root-only package surfaces declare typed compile-library unavailability and bypass this
// helper. An *unmatched* selected identity is legitimate -- `InspectionEngine.cs` permits
// an empty `assemblies` list whenever extraction truncates and then falls back to the
// selected asset id, which matches no descriptor. The producer commits each descriptor and
// its types atomically, but that truncated result still carries a visible inspection notice
// worth merging into a compatible resident.
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
  if (result.compileLibrary.status !== "Selected") {
    throw new Error(
      result.compileLibrary.message
      || `The package Root has no selected compile library (${result.compileLibrary.status}).`);
  }
  const assembly = defaultAssembly(
    result,
    "The package query did not return its selected assembly descriptor.");
  const inspectionErrors = surfaceInspectionErrors(result);
  return {
    id: result.package,
    version: result.version,
    frameworks: [...(result.frameworks ?? [])],
    activeFramework: result.activeFramework,
    assembly: assembly.name,
    assemblyId: assembly.id,
    assemblyAsset: assembly.asset,
    source: { kind: "nuget.org" },
    assemblies: [...(result.assemblies ?? [])],
    types: packageTypes(result),
    accessibility: [...(result.accessibility ?? [])],
    totalTypes: (result.assemblies ?? [])
      .reduce((count, candidate) => count + (candidate.publicTypes ?? 0), 0),
    totalMembers: result.totalMembers,
    documents: [...(result.documents ?? [])],
    inspectionErrors,
    inspectionError: renderInspectionErrors(inspectionErrors),
    isRuntimePack: false,
  };
}

export function createRuntimePackageModel(
  result: BrowserPackageSurface,
): AppPackage {
  const assembly = defaultAssembly(
    result,
    "The platform query did not return its selected assembly descriptor.");
  return createRuntimePackageModelForAssembly(result, assembly);
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
  return createRuntimePackageModelForAssembly(result, assembly);
}

function createRuntimePackageModelForAssembly(
  result: BrowserPackageSurface,
  assembly: BrowserAssemblySurface,
): AppPackage {
  const types = packageTypes(result);
  const inspectionErrors = surfaceInspectionErrors(result);
  return {
    id: result.package,
    version: result.version,
    frameworks: [...(result.frameworks ?? [])],
    activeFramework: result.activeFramework,
    assembly: assembly.name,
    assemblyId: assembly.id,
    assemblyAsset: assembly.asset,
    source: { kind: "platform" },
    assemblies: [...(result.assemblies ?? [])],
    types,
    accessibility: [...(result.accessibility ?? [])],
    totalTypes: types.length,
    totalMembers: result.totalMembers,
    documents: [...(result.documents ?? [])],
    inspectionErrors,
    inspectionError: renderInspectionErrors(inspectionErrors),
    isRuntimePack: true,
  };
}

export function mergeRuntimePackageSurface(
  existing: AppPackage,
  result: BrowserPackageSurface,
): AppPackage {
  const defaultAssemblyId = requireAssemblyIdentity(
    result,
    "The platform query did not return its selected assembly identity.");
  const hasProjectedContent =
    (result.assemblies?.length ?? 0) > 0
    || (result.types?.length ?? 0) > 0;
  if (hasProjectedContent && !selectedAssembly(result)) {
    throw new Error(
      `The platform query returned no descriptor for ${defaultAssemblyId}.`);
  }

  const newTypes = packageTypes(result);
  const seenTypes = new Set(existing.types.map(type => type.id));
  const acceptedTypes: AppTypeSurface[] = [];
  for (const type of newTypes) {
    if (seenTypes.has(type.id)) continue;
    seenTypes.add(type.id);
    existing.types.push(type);
    acceptedTypes.push(type);
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
    const key = assemblyKey(assembly);
    if (seenAssemblies.has(key)) continue;
    seenAssemblies.add(key);
    existing.assemblies.push(assembly);
  }

  // Preserve authoritative surface aggregates for a wholly new result. A partial or
  // repeated merge must instead count only rows accepted above.
  const acceptedAllTypes =
    newTypes.length > 0 && acceptedTypes.length === newTypes.length;
  const acceptedAccessibilityCounts = new Map<string, number>();
  if (acceptedAllTypes) {
    for (const descriptor of result.accessibility ?? []) {
      acceptedAccessibilityCounts.set(descriptor.id, descriptor.count);
    }
  } else {
    for (const type of acceptedTypes) {
      acceptedAccessibilityCounts.set(
        type.accessibilityId,
        (acceptedAccessibilityCounts.get(type.accessibilityId) ?? 0) + 1);
    }
  }
  const descriptors = new Map(
    existing.accessibility.map(descriptor => [descriptor.id, descriptor]));
  for (const descriptor of result.accessibility ?? []) {
    const acceptedCount = acceptedAccessibilityCounts.get(descriptor.id) ?? 0;
    if (acceptedCount === 0) continue;
    const current = descriptors.get(descriptor.id);
    descriptors.set(descriptor.id, current
      ? { ...current, count: current.count + acceptedCount }
      : { ...descriptor, count: acceptedCount });
  }
  existing.accessibility = [...descriptors.values()]
    .sort((left, right) => left.order - right.order);
  existing.totalTypes = existing.types.length;
  const defaultAccessibilityIds = new Set(
    (result.accessibility ?? [])
      .filter(descriptor => descriptor.isDefault)
      .map(descriptor => descriptor.id));
  const acceptedMembers = acceptedAllTypes
    ? (result.totalMembers || 0)
    : acceptedTypes
      .filter(type => defaultAccessibilityIds.has(type.accessibilityId))
      .reduce((total, type) => total + type.members, 0);
  existing.totalMembers = (existing.totalMembers || 0) + acceptedMembers;
  existing.inspectionErrors = mergeInspectionErrorEntries(
    existing.inspectionErrors
      ?? (existing.inspectionError ? [existing.inspectionError] : []),
    surfaceInspectionErrors(result));
  existing.inspectionError = renderInspectionErrors(existing.inspectionErrors);
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
  loadRuntimePack(framework: string, platformVersion: string): Promise<string>;
  loadRuntimePackAssembly(
    framework: string,
    platformVersion: string,
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
    platformVersion?: string,
  ): Promise<RuntimeAcquisitionResult>;
  loadRuntimePackAssembly(
    framework: string,
    assemblyFileName: string,
    pack: string,
    isCurrent?: () => boolean,
    platformVersion?: string,
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

    async loadRuntimePack(
      framework,
      isCurrent = () => true,
      platformVersion = "",
    ) {
      return enqueueRuntimeRequest(async () => {
        if (!isCurrent()) return { packageModel: null, error: null };
        const requestedFramework = framework || "";
        const requestedVersion =
          platformVersion.toLowerCase() === "latest"
            ? ""
            : platformVersion;
        const existing = dependencies.runtimePackage();
        if (existing
          && runtimePackIsResident(existing)
          && (!requestedFramework
            || existing.activeFramework.toLowerCase()
              === requestedFramework.toLowerCase())
          && (!requestedVersion
            || existing.version.toLowerCase()
              === requestedVersion.toLowerCase())) {
          return { packageModel: existing, error: null };
        }

        return runRuntimeOperation(async () => {
          const result = dependencies.parseRuntimeSurface(
            await dependencies.loadRuntimePack(
              requestedFramework,
              requestedVersion));
          if (!isCurrent()) return null;
          dependencies.refreshPackageStats();
          const current = dependencies.runtimePackage();
          if (current
            && (!requestedFramework
              || current.activeFramework.toLowerCase()
                === requestedFramework.toLowerCase())
            && (!requestedVersion
              || current.version.toLowerCase()
                === requestedVersion.toLowerCase())) {
            const merged = mergeRuntimePackageSurface(current, result);
            const primary = selectedAssembly(result);
            if (primary) {
              promoteRuntimePackagePrimary(
                merged,
                createRuntimePackageModelForAssembly(result, primary));
            }
            return merged;
          }
          const packageModel = createRuntimePackageModel(result);
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
      platformVersion = "",
    ) {
      return enqueueRuntimeRequest(async () => {
        if (!isCurrent()) return { packageModel: null, error: null };
        const requestedFramework = framework || "";
        const requestedVersion =
          platformVersion.toLowerCase() === "latest"
            ? ""
            : platformVersion;
        const requestedAssembly = assemblyFileName
          .replace(/\.dll$/i, "");
        const resident = dependencies.runtimePackage();
        if (resident
          && (!requestedFramework
            || resident.activeFramework.toLowerCase()
              === requestedFramework.toLowerCase())
          && (!requestedVersion
            || resident.version.toLowerCase()
              === requestedVersion.toLowerCase())) {
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
              requestedVersion,
              assemblyFileName,
              pack || ""));
          if (!isCurrent()) return null;
          dependencies.refreshPackageStats();
          const existing = dependencies.runtimePackage();
          if (existing
            && (!requestedFramework
              || existing.activeFramework.toLowerCase()
                === requestedFramework.toLowerCase())
            && (!requestedVersion
              || existing.version.toLowerCase()
                === requestedVersion.toLowerCase())) {
            const merged = mergeRuntimePackageSurface(existing, result);
            const primary = selectedAssembly(result);
            // Promotion builds a package model, so it needs a descriptor. A truncated
            // surface has none, and round 5 established that such a surface must still
            // merge rather than fail the whole load -- so skip the promotion instead of
            // letting the model construction throw.
            if (primary?.name.toLowerCase()
                === DEFAULT_RUNTIME_ASSEMBLY.toLowerCase()) {
              promoteRuntimePackagePrimary(
                merged,
                createRuntimePackageModelForAssembly(result, primary));
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
