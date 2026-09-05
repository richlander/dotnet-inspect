import { dotnet } from "./runtime-loader.js";

export type BrowserAnnotatedSourceCapabilityUnavailableReason = "NotProjected" | "ContextUnavailable" | number;

export type BrowserAnnotatedSourceMedium = "CSharp" | "Il" | number;

export interface BrowserAnnotatedSource {
  readonly document: unknown;
  readonly viewerCatalog: BrowserAnnotatedSourceViewerCatalog;
  readonly provenance: string;
  readonly contextLimitation: string | null;
}

export interface BrowserAnnotatedSourceCapabilityAvailability {
  readonly available: boolean;
  readonly unavailableReason: BrowserAnnotatedSourceCapabilityUnavailableReason | null;
}

export interface BrowserAnnotatedSourceInvocationDestination {
  readonly nodeId: number;
  readonly target: BrowserCallGraphTarget;
}

export interface BrowserAnnotatedSourceViewerCatalog {
  readonly defaultFindingIds: ReadonlyArray<number>;
  readonly supportedMedia: ReadonlyArray<BrowserAnnotatedSourceMedium>;
  readonly invocationLikeNodeKinds: ReadonlyArray<string>;
  readonly invocationDestinations: ReadonlyArray<BrowserAnnotatedSourceInvocationDestination>;
  readonly findingEvidence: BrowserAnnotatedSourceCapabilityAvailability;
  readonly destinations: BrowserAnnotatedSourceCapabilityAvailability;
}

export interface BrowserCallGraphTarget {
  readonly id: string;
  readonly assembly: string;
  readonly assemblyVersion: string | null;
  readonly assemblyCulture: string | null;
  readonly assemblyPublicKeyToken: string | null;
  readonly typeFullName: string;
  readonly typeMetadataId: string | null;
  readonly typeDefinitionId: string | null;
  readonly memberName: string;
  readonly parameterTypes: ReadonlyArray<string>;
  readonly returnType: string;
  readonly genericArity: number;
  readonly metadataToken: number | null;
  readonly selectorKey: string;
  readonly kind: string;
  readonly platformPack: string | null;
  readonly surfaceAssemblyId: string | null;
}

export interface BrowserSource {
  readonly provider: string;
  readonly provenance: string;
  readonly url: string | null;
  readonly pdbSourceLimitation: string | null;
  readonly text: string;
}

type $ManagedExports = {
  readonly "SourceExports": {
    readonly "CancelSourceQuery.19325221": () => void;
    readonly "QueryMemberAnnotatedSource.1135530322": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
    readonly "QueryMemberSource.641907440": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
    readonly "QueryTypeMemberSource.641907440": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
    readonly "QueryTypeSource.649160465": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string) => Promise<string>;
  };
};

export interface JsExportRuntime {
  readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
  readonly runMain: (
    mainAssemblyName?: string,
    args?: string[],
  ) => Promise<number>;
}

const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
let $runtime: JsExportRuntime | undefined;
let $managedExports: $ManagedExports | undefined;
let $initialization: Promise<void> | undefined;
let $initializationFailure: { readonly error: unknown } | undefined;

function $ownDataProperty(value: unknown, key: string): unknown {
  if (value === null || (typeof value !== "object" && typeof value !== "function")) {
    throw new Error(`Managed export path '${key}' has a non-object parent.`);
  }
  const descriptor = Object.getOwnPropertyDescriptor(value, key);
  if (descriptor === undefined || !("value" in descriptor)) {
    throw new Error(`Managed export path '${key}' is not an own data property.`);
  }
  return descriptor.value;
}

function $requireRuntime(): JsExportRuntime {
  if ($initializationFailure !== undefined) throw $initializationFailure.error;
  if ($runtime === undefined) {
    throw $notInitializedError;
  }
  return $runtime;
}

function $requireManagedExports(): $ManagedExports {
  if ($initializationFailure !== undefined) throw $initializationFailure.error;
  if ($managedExports === undefined) {
    throw $notInitializedError;
  }
  return $managedExports;
}

function $validateManagedExports(exports: unknown): asserts exports is $ManagedExports {
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "CancelSourceQuery.19325221");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027SourceExports.CancelSourceQuery.19325221\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMemberAnnotatedSource.1135530322");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027SourceExports.QueryMemberAnnotatedSource.1135530322\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMemberSource.641907440");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027SourceExports.QueryMemberSource.641907440\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryTypeMemberSource.641907440");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027SourceExports.QueryTypeMemberSource.641907440\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryTypeSource.649160465");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027SourceExports.QueryTypeSource.649160465\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine.SourceExports");
  $validateManagedExports(exports);
  $runtime = runtime;
  $managedExports = exports;
}

export function createRuntime(): Promise<JsExportRuntime> {
  return dotnet.create();
}

export function initializeRuntime(
  runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>,
): Promise<void> {
  if ($initialization === undefined) {
    $initialization = Promise.resolve()
      .then(() => runtime === undefined ? createRuntime() : runtime)
      .then($initializeRuntimeCore)
      .catch((error: unknown) => {
        $initializationFailure = { error };
        throw error;
      });
  }
  return $initialization;
}

export function runEntryPoint(
  mainAssemblyName?: string,
  args?: string[],
): Promise<number> {
  return $requireRuntime().runMain(mainAssemblyName, args);
}

export function cancelSourceQuery(): void {
  return $requireManagedExports()["SourceExports"]["CancelSourceQuery.19325221"]();
}

export async function queryMemberAnnotatedSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserAnnotatedSource> {
  const $result = await $requireManagedExports()["SourceExports"]["QueryMemberAnnotatedSource.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserAnnotatedSource;
}

export async function queryMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource> {
  const $result = await $requireManagedExports()["SourceExports"]["QueryMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserSource;
}

export async function queryTypeMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource> {
  const $result = await $requireManagedExports()["SourceExports"]["QueryTypeMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserSource;
}

export async function queryTypeSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string): Promise<BrowserSource> {
  const $result = await $requireManagedExports()["SourceExports"]["QueryTypeSource.649160465"](packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserSource;
}

