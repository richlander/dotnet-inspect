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

function packageTypes(result: BrowserPackageSurface): BrowserTypeSurface[] {
  return (result.types ?? []).map(type => ({
    ...type,
    api: type.api ?? [],
  }));
}

function defaultAssembly(
  result: BrowserPackageSurface,
  failureMessage: string,
): BrowserAssemblySurface {
  const assembly = (result.assemblies ?? [])
    .find(candidate => candidate.id === result.defaultAssemblyId);
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
): AppPackage {
  return createRuntimePackageModelForAssembly(
    result,
    result.assemblies[0],
    result.defaultAssemblyId);
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

  const seenAssemblies = new Set(existing.assemblies.map(assembly => assembly.name));
  for (const assembly of result.assemblies ?? []) {
    if (!seenAssemblies.has(assembly.name)) existing.assemblies.push(assembly);
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
  return existing;
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
  let runtimeOperation: Promise<AppPackage | null> | null = null;

  const waitForRuntimeOperation = async () => {
    for (;;) {
      const pending = runtimeOperation;
      if (!pending) return;
      try {
        await pending;
      } catch {
        // The operation owner reports its failure before the next request starts.
      }
    }
  };

  const runRuntimeOperation = async (
    operation: () => Promise<AppPackage | null>,
  ): Promise<RuntimeAcquisitionResult> => {
    dependencies.beginRuntimeLoad();
    const pending = operation();
    runtimeOperation = pending;
    try {
      return {
        packageModel: await pending,
        error: null,
      };
    } catch (error) {
      dependencies.failRuntimeLoad(error);
      return {
        packageModel: null,
        error,
      };
    } finally {
      if (runtimeOperation === pending) runtimeOperation = null;
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
      if (runtimeOperation) await waitForRuntimeOperation();
      if (!isCurrent()) return { packageModel: null, error: null };
      const requestedFramework = framework || "";
      const existing = dependencies.runtimePackage();
      if (existing
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
        dependencies.retainPackage(packageModel, existing);
        return packageModel;
      });
    },

    async loadRuntimePackAssembly(
      framework,
      assemblyFileName,
      pack,
      isCurrent = () => true,
    ) {
      if (runtimeOperation) await waitForRuntimeOperation();
      if (!isCurrent()) return { packageModel: null, error: null };
      const requestedFramework = framework || "";
      const resident = dependencies.runtimePackage();
      if (resident
        && (!requestedFramework
          || resident.activeFramework.toLowerCase()
            === requestedFramework.toLowerCase())
        && resident.assemblies.some(assembly =>
          assembly.name.toLowerCase()
            === assemblyFileName.toLowerCase())) {
        return { packageModel: resident, error: null };
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
        if (existing
          && (!requestedFramework
            || existing.activeFramework.toLowerCase()
              === requestedFramework.toLowerCase())) {
          return mergeRuntimePackageSurface(existing, result);
        }
        const packageModel = createRuntimeAssemblyPackageModel(result);
        dependencies.retainPackage(packageModel, existing);
        return packageModel;
      });
    },
  };
}
