import { dotnet } from "./runtime-loader.js";

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export interface BrowserAssemblyMetadata {
  readonly assembly: string;
  readonly metadataVersion: string;
  readonly metadataVersionTruncated: boolean;
  readonly kind: string;
  readonly isAssembly: boolean;
  readonly metadataSize: number;
  readonly projectedTableTotal: number;
  readonly heaps: ReadonlyArray<BrowserMetadataHeap>;
  readonly tables: ReadonlyArray<BrowserMetadataTable>;
  readonly headers: BrowserMetadataHeaders;
}

export interface BrowserCompileLibraryAvailability {
  readonly status: BrowserCompileLibraryStatus;
  readonly targetFramework: string | null;
  readonly message: string | null;
}

export interface BrowserExceptionSurface {
  readonly type: string;
  readonly description: string;
}

export interface BrowserGraphMemberSurface {
  readonly type: BrowserTypeSurface;
  readonly selectedBody: BrowserMemberBodySelector;
}

export interface BrowserHeapEntry {
  readonly offset: number;
  readonly value: BrowserMetadataCell;
  readonly referenceCount: number;
}

export interface BrowserHeapListing {
  readonly assembly: string;
  readonly heap: string;
  readonly streamName: string;
  readonly coverage: string;
  readonly entries: ReadonlyArray<BrowserHeapEntry>;
  readonly rowsTruncated: boolean;
  readonly entriesTruncated: boolean;
  readonly error: string | null;
}

export interface BrowserMemberBodySelector {
  readonly token: number;
  readonly memberName: string;
  readonly selectorKey: string;
}

export interface BrowserMemberSurface {
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
  readonly returnType: string | null;
  readonly parameters: ReadonlyArray<BrowserParameterSurface>;
  readonly documentationId: string | null;
  readonly summary: string | null;
  readonly returns: string | null;
  readonly exceptions: ReadonlyArray<BrowserExceptionSurface>;
  readonly stableSelector: string;
  readonly anchorDigest: string;
  readonly canonicalSignature: string;
  readonly graphSelectorKey: string;
  readonly bodySelectors: ReadonlyArray<BrowserMemberBodySelector>;
}

export interface BrowserMetadataCell {
  readonly kind: string;
  readonly raw: number | null;
  readonly display: string | null;
  readonly decoded: string | null;
  readonly heap: string | null;
  readonly text: string | null;
  readonly preview: string | null;
  readonly offset: number | null;
  readonly length: number | null;
  readonly truncated: boolean | null;
  readonly targetTable: number | null;
  readonly targetRowId: number | null;
  readonly startRowId: number | null;
  readonly endRowId: number | null;
  readonly count: number | null;
  readonly token: number | null;
  readonly detail: string | null;
}

export interface BrowserMetadataColumn {
  readonly name: string;
  readonly kind: string;
  readonly candidateTargets: ReadonlyArray<number>;
}

export interface BrowserMetadataHeaders {
  readonly machine: string;
  readonly isPE32Plus: boolean;
  readonly subsystem: string;
  readonly corFlags: string | null;
  readonly majorRuntimeVersion: number | null;
  readonly minorRuntimeVersion: number | null;
  readonly entryPointToken: number | null;
  readonly managedNativeHeaderRva: number;
  readonly managedNativeHeaderSize: number;
}

export interface BrowserMetadataHeap {
  readonly name: string;
  readonly sizeInBytes: number;
  readonly maxAddress: number;
  readonly addressing: string;
}

export interface BrowserMetadataRow {
  readonly rowId: number;
  readonly token: number;
  readonly cells: ReadonlyArray<BrowserMetadataCell>;
}

export interface BrowserMetadataTable {
  readonly index: number;
  readonly name: string;
  readonly rowCount: number;
  readonly isProjected: boolean;
}

export interface BrowserMetadataWindow {
  readonly assembly: string;
  readonly index: number;
  readonly name: string;
  readonly rowCount: number;
  readonly startRowId: number;
  readonly columns: ReadonlyArray<BrowserMetadataColumn>;
  readonly rows: ReadonlyArray<BrowserMetadataRow>;
  readonly truncated: boolean;
  readonly error: string | null;
}

export interface BrowserPackageMetadata {
  readonly assemblies: ReadonlyArray<BrowserAssemblyMetadata>;
  readonly inspectionError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserParameterSurface {
  readonly name: string;
  readonly type: string;
  readonly modifier: string | null;
  readonly hasDefault: boolean;
  readonly defaultValue: string | null;
  readonly description: string | null;
}

export interface BrowserTypeComposition {
  readonly methods: number;
  readonly properties: number;
  readonly fields: number;
  readonly events: number;
  readonly constructors: number;
  readonly operators: number;
  readonly explicitInterfaceImplementations: number;
  readonly extensionMethods: number;
  readonly static: number;
  readonly unsafe: number;
  readonly async: number;
  readonly virtual: number;
  readonly abstract: number;
  readonly override: number;
  readonly extension: number;
  readonly obsolete: number;
  readonly total: number;
}

export interface BrowserTypeGraphEdge {
  readonly fromId: string;
  readonly toId: string;
  readonly kind: string;
}

export interface BrowserTypeGraphNode {
  readonly id: string;
  readonly displayName: string;
  readonly role: string;
}

export interface BrowserTypeMetadata {
  readonly fullName: string;
  readonly namespace: string | null;
  readonly name: string;
  readonly kind: string;
  readonly modifiers: ReadonlyArray<string>;
  readonly accessibility: string | null;
  readonly assembly: string | null;
  readonly baseType: string | null;
  readonly interfaces: ReadonlyArray<string>;
  readonly derivedTypes: ReadonlyArray<string>;
  readonly typeParameters: ReadonlyArray<BrowserTypeParameter>;
  readonly attributes: ReadonlyArray<string>;
  readonly enumUnderlyingType: string | null;
  readonly composition: BrowserTypeComposition | null;
  readonly graphNodes: ReadonlyArray<BrowserTypeGraphNode>;
  readonly graphEdges: ReadonlyArray<BrowserTypeGraphEdge>;
  readonly inspectionFailures: ReadonlyArray<string>;
}

export interface BrowserTypeParameter {
  readonly name: string;
  readonly variance: string | null;
  readonly constraints: ReadonlyArray<string>;
}

export interface BrowserTypeSurface {
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
  readonly api: ReadonlyArray<BrowserMemberSurface>;
  readonly platformPack: string | null;
}

type $ManagedExports = {
  readonly "MetadataExports": {
    readonly "QueryGraphMemberSurface.1542089313": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number) => Promise<string>;
    readonly "QueryPackageHeapEntries.1330709314": (packageId: string, version: string, targetFramework: string, assemblyFileName: string, heap: string) => Promise<string>;
    readonly "QueryPackageMetadata.1579276339": (packageId: string, version: string, targetFramework: string, assemblyFileName: string) => Promise<string>;
    readonly "QueryPackageMetadataTable.1509466830": (packageId: string, version: string, targetFramework: string, assemblyFileName: string, tableIndex: number, startRowId: number, maxRows: number) => Promise<string>;
    readonly "QueryPlatformHeapEntries.1330709314": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, heap: string) => Promise<string>;
    readonly "QueryPlatformMetadata.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformMetadataTable.1509466830": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, tableIndex: number, startRowId: number, maxRows: number) => Promise<string>;
    readonly "QueryTypeProjection.1330709314": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeId: string) => Promise<string>;
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
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryGraphMemberSurface.1542089313");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryGraphMemberSurface.1542089313\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPackageHeapEntries.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryPackageHeapEntries.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPackageMetadata.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryPackageMetadata.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPackageMetadataTable.1509466830");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryPackageMetadataTable.1509466830\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPlatformHeapEntries.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryPlatformHeapEntries.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPlatformMetadata.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryPlatformMetadata.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPlatformMetadataTable.1509466830");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryPlatformMetadataTable.1509466830\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryTypeProjection.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MetadataExports.QueryTypeProjection.1330709314\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine.MetadataExports");
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

export async function queryGraphMemberSurface(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserGraphMemberSurface> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryGraphMemberSurface.1542089313"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserGraphMemberSurface;
}

export async function queryPackageHeapEntries(packageId: string, version: string, targetFramework: string, assemblyFileName: string, heap: string): Promise<BrowserHeapListing> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryPackageHeapEntries.1330709314"](packageId, version, targetFramework, assemblyFileName, heap);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHeapListing;
}

export async function queryPackageMetadata(packageId: string, version: string, targetFramework: string, assemblyFileName: string): Promise<BrowserPackageMetadata> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryPackageMetadata.1579276339"](packageId, version, targetFramework, assemblyFileName);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageMetadata;
}

export async function queryPackageMetadataTable(packageId: string, version: string, targetFramework: string, assemblyFileName: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryPackageMetadataTable.1509466830"](packageId, version, targetFramework, assemblyFileName, tableIndex, startRowId, maxRows);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMetadataWindow;
}

export async function queryPlatformHeapEntries(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, heap: string): Promise<BrowserHeapListing> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryPlatformHeapEntries.1330709314"](targetFramework, platformVersion, assemblyFileName, pack, heap);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHeapListing;
}

export async function queryPlatformMetadata(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageMetadata> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryPlatformMetadata.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageMetadata;
}

export async function queryPlatformMetadataTable(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryPlatformMetadataTable.1509466830"](targetFramework, platformVersion, assemblyFileName, pack, tableIndex, startRowId, maxRows);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMetadataWindow;
}

export async function queryTypeProjection(packageId: string, version: string, targetFramework: string, assemblyName: string, typeId: string): Promise<BrowserTypeMetadata> {
  const $result = await $requireManagedExports()["MetadataExports"]["QueryTypeProjection.1330709314"](packageId, version, targetFramework, assemblyName, typeId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeMetadata;
}

