import { dotnet } from "./runtime-loader.js";

export type BrowserLibraryInspectionShareKind = "Available" | "NonProjectable" | number;

export type BrowserUploadedLibraryFailureKind = "InvalidDeclaredName" | "EmptyImage" | "ResourceBudget" | "DescriptorUnavailable" | "NotAssembly" | "InvalidImage" | "UnsupportedMetadataFormat" | "InspectionFailed" | "ProjectionTruncated" | number;

export type BrowserUploadedLibraryInspectionOutcome = "Available" | "Rejected" | number;

export interface BrowserEmbeddedLibraryProvenance {
  readonly contentRef: string;
  readonly digest: string;
  readonly declaredName: string;
}

export interface BrowserLibraryAccessibilityDescriptor {
  readonly id: string;
  readonly label: string;
  readonly order: number;
  readonly isDefault: boolean;
  readonly count: number;
}

export interface BrowserLibraryAssemblyReference {
  readonly name: string;
  readonly version: string;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
}

export interface BrowserLibraryAssemblySurface {
  readonly id: string;
  readonly name: string;
  readonly version: string;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
  readonly asset: string;
  readonly publicTypes: number;
  readonly publicMembers: number;
  readonly platformPack: string | null;
}

export interface BrowserLibraryExceptionSurface {
  readonly type: string;
  readonly description: string;
}

export interface BrowserLibraryInspectionDiagnostic {
  readonly code: string;
  readonly severity: string;
  readonly summary: string;
  readonly correspondence: string | null;
}

export interface BrowserLibraryInspectionFailure {
  readonly operation: string;
  readonly subjectToken: number;
  readonly mechanism: string;
  readonly kind: string;
  readonly detail: string;
  readonly subjectAssembly: BrowserLibraryAssemblyReference | null;
  readonly dependencyAssembly: BrowserLibraryAssemblyReference | null;
}

export interface BrowserLibraryInspectionShare {
  readonly kind: BrowserLibraryInspectionShareKind;
  readonly fullUrl: string | null;
  readonly packet: string | null;
  readonly path: string | null;
  readonly reason: string | null;
}

export interface BrowserLibraryMemberBodySelector {
  readonly token: number;
  readonly memberName: string;
  readonly selectorKey: string;
}

export interface BrowserLibraryMemberSurface {
  readonly name: string;
  readonly kind: string;
  readonly signature: string;
  readonly accessibility: string;
  readonly isStatic: boolean;
  readonly isUnsafe: boolean;
  readonly isVirtual: boolean;
  readonly isAbstract: boolean;
  readonly isOverride: boolean;
  readonly isExtension: boolean;
  readonly isObsolete: boolean;
  readonly genericArity: number;
  readonly metadataToken: number | null;
  readonly declarationMetadataToken: number | null;
  readonly returnType: string | null;
  readonly parameters: ReadonlyArray<BrowserLibraryParameterSurface>;
  readonly documentationId: string | null;
  readonly summary: string | null;
  readonly returns: string | null;
  readonly exceptions: ReadonlyArray<BrowserLibraryExceptionSurface>;
  readonly stableSelector: string;
  readonly anchorDigest: string;
  readonly canonicalSignature: string;
  readonly anchorTypeFullName: string;
  readonly graphSelectorKey: string;
  readonly bodySelectors: ReadonlyArray<BrowserLibraryMemberBodySelector>;
}

export interface BrowserLibraryParameterSurface {
  readonly name: string;
  readonly type: string;
  readonly modifier: string | null;
  readonly hasDefault: boolean;
  readonly defaultValue: string | null;
  readonly description: string | null;
}

export interface BrowserLibraryTypeSurface {
  readonly id: string;
  readonly definitionId: string;
  readonly queryId: string;
  readonly metadataId: string;
  readonly name: string;
  readonly displayName: string;
  readonly namespace: string;
  readonly kind: string;
  readonly accessibility: string;
  readonly accessibilityId: string;
  readonly assembly: string;
  readonly assemblyId: string;
  readonly assemblyName: string;
  readonly members: number;
  readonly signature: string;
  readonly api: ReadonlyArray<BrowserLibraryMemberSurface>;
  readonly platformPack: string | null;
}

export interface BrowserUploadedLibraryFailure {
  readonly kind: BrowserUploadedLibraryFailureKind;
  readonly detail: string;
}

export interface BrowserUploadedLibraryInspection {
  readonly content: BrowserUploadedLibraryResult;
  readonly share: BrowserLibraryInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserLibraryInspectionDiagnostic>;
}

export interface BrowserUploadedLibraryResult {
  readonly outcome: BrowserUploadedLibraryInspectionOutcome;
  readonly declaredName: string;
  readonly digest: string;
  readonly byteLength: number;
  readonly provenance: BrowserEmbeddedLibraryProvenance | null;
  readonly assembly: BrowserLibraryAssemblyReference | null;
  readonly surface: BrowserUploadedLibrarySurface | null;
  readonly inspectionFailures: ReadonlyArray<BrowserLibraryInspectionFailure>;
  readonly failure: BrowserUploadedLibraryFailure | null;
  readonly isComplete: boolean;
}

export interface BrowserUploadedLibrarySurface {
  readonly assemblies: ReadonlyArray<BrowserLibraryAssemblySurface>;
  readonly types: ReadonlyArray<BrowserLibraryTypeSurface>;
  readonly accessibility: ReadonlyArray<BrowserLibraryAccessibilityDescriptor>;
  readonly totalMembers: number;
  readonly inspectionErrors: ReadonlyArray<string>;
  readonly inspectionError: string | null;
  readonly isTruncated: boolean;
}

type $ManagedExports = {
  readonly "DotnetInspect": {
    readonly "Web": {
      readonly "Interop": {
        readonly "Library": {
          readonly "LibraryExports": {
            readonly "OpenUploadedLibrary.833021020": (declaredName: string, content: number[]) => Promise<string>;
          };
        };
      };
    };
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
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Library");
    value = $ownDataProperty(value, "LibraryExports");
    value = $ownDataProperty(value, "OpenUploadedLibrary.833021020");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Library.LibraryExports.OpenUploadedLibrary.833021020\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Library");
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

export async function openUploadedLibrary(declaredName: string, content: number[]): Promise<BrowserUploadedLibraryInspection> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Library"]["LibraryExports"]["OpenUploadedLibrary.833021020"](declaredName, content);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserUploadedLibraryInspection;
}

