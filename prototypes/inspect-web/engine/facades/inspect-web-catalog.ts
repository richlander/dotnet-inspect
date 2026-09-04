import { dotnet } from "./_framework/dotnet.js";

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export interface BrowserAccessibilityDescriptor {
  readonly id: string;
  readonly label: string;
  readonly order: number;
  readonly isDefault: boolean;
  readonly count: number;
}

export interface BrowserAssemblySurface {
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

export interface BrowserCallGraph {
  readonly mermaid: string;
  readonly callers: BrowserCallGraphNode;
  readonly callees: BrowserCallGraphNode;
  readonly scope: BrowserCallGraphScope;
  readonly targets: ReadonlyArray<BrowserCallGraphTarget>;
  readonly diagnostics: BrowserCallGraphDiagnostics;
  readonly noBody: boolean;
}

export interface BrowserCallGraphDiagnostics {
  readonly incompleteNodes: number;
  readonly incompleteEdges: number;
  readonly bindingIdentityConflicts: number;
  readonly hasUnexploredTraversalBoundary: boolean;
  readonly hasAnalysisFailureBoundary: boolean;
  readonly isIncomplete: boolean;
}

export interface BrowserCallGraphNode {
  readonly label: string;
  readonly status: string;
  readonly inLoop: boolean;
  readonly source: string | null;
  readonly children: ReadonlyArray<BrowserCallGraphNode>;
  readonly assembly: string;
  readonly typeFullName: string;
  readonly memberName: string;
}

export interface BrowserCallGraphScope {
  readonly packages: number;
  readonly assemblies: number;
  readonly callerAssemblies: number;
  readonly calleeScope: string;
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

export interface BrowserCompileLibraryAvailability {
  readonly status: BrowserCompileLibraryStatus;
  readonly targetFramework: string | null;
  readonly message: string | null;
}

export interface BrowserExceptionSurface {
  readonly type: string;
  readonly description: string;
}

export interface BrowserHomeDemoCatalog {
  readonly demos: ReadonlyArray<BrowserHomeDemoCatalogEntry>;
}

export interface BrowserHomeDemoCatalogEntry {
  readonly id: string;
  readonly title: string;
  readonly summary: string;
}

export interface BrowserHomeDemoMember {
  readonly kind: string;
  readonly id: string;
  readonly version: string | null;
  readonly framework: string | null;
  readonly assembly: string | null;
}

export interface BrowserHomeDemoNavigationTab {
  readonly id: string;
  readonly member: BrowserHomeDemoMember;
}

export interface BrowserHomeDemoResolveResult {
  readonly found: boolean;
  readonly demo: BrowserHomeDemoResolved | null;
}

export interface BrowserHomeDemoResolved {
  readonly id: string;
  readonly title: string;
  readonly summary: string;
  readonly workspaceMembers: ReadonlyArray<BrowserHomeDemoMember>;
  readonly tabs: ReadonlyArray<BrowserHomeDemoNavigationTab>;
  readonly focusTabIndex: number;
  readonly view: BrowserHomeDemoView;
}

export interface BrowserHomeDemoRunActivation {
  readonly focusPackage: string;
  readonly focusVersion: string;
  readonly focusFramework: string;
  readonly typeId: string;
  readonly section: string;
  readonly memberName: string | null;
  readonly memberKind: string | null;
  readonly memberAnchorDigest: string | null;
  readonly memberSection: string | null;
}

export interface BrowserHomeDemoRunResult {
  readonly found: boolean;
  readonly packages: ReadonlyArray<BrowserPackageSurface>;
  readonly activation: BrowserHomeDemoRunActivation | null;
  readonly callGraph: BrowserCallGraph | null;
}

export interface BrowserHomeDemoView {
  readonly library: string | null;
  readonly type: string | null;
  readonly memberAnchor: string | null;
  readonly memberKey: string | null;
  readonly section: string | null;
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

export interface BrowserPackageDocument {
  readonly kind: string;
  readonly name: string;
  readonly path: string;
  readonly size: number;
}

export interface BrowserPackageIcon {
  readonly mediaType: string;
  readonly base64: string;
}

export interface BrowserPackageSurface {
  readonly package: string;
  readonly version: string;
  readonly frameworks: ReadonlyArray<string>;
  readonly activeFramework: string;
  readonly icon: BrowserPackageIcon | null;
  readonly defaultAssemblyId: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
  readonly assemblies: ReadonlyArray<BrowserAssemblySurface>;
  readonly types: ReadonlyArray<BrowserTypeSurface>;
  readonly accessibility: ReadonlyArray<BrowserAccessibilityDescriptor>;
  readonly totalMembers: number;
  readonly documents: ReadonlyArray<BrowserPackageDocument>;
  readonly inspectionErrors: ReadonlyArray<string>;
  readonly inspectionError: string | null;
}

export interface BrowserParameterSurface {
  readonly name: string;
  readonly type: string;
  readonly modifier: string | null;
  readonly hasDefault: boolean;
  readonly defaultValue: string | null;
  readonly description: string | null;
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

export interface BrowserVocabularyDocument {
  readonly schema_version: number;
  readonly sections: ReadonlyArray<BrowserVocabularySection>;
}

export interface BrowserVocabularyField {
  readonly id: string;
  readonly label: string;
  readonly summary: string;
  readonly type: string;
  readonly operators: ReadonlyArray<string>;
}

export interface BrowserVocabularySection {
  readonly id: string;
  readonly name: string;
  readonly summary: string;
  readonly categories: ReadonlyArray<string>;
  readonly accepted_by: ReadonlyArray<string>;
  readonly fields: ReadonlyArray<BrowserVocabularyField>;
  readonly values: ReadonlyArray<unknown>;
}

export interface BrowserWorkspaceShareContext {
  readonly id: string;
  readonly tabIds: ReadonlyArray<string>;
}

export interface BrowserWorkspaceShareDecodeResult {
  readonly succeeded: boolean;
  readonly state: BrowserWorkspaceShareState | null;
  readonly failure: BrowserWorkspaceShareFailure | null;
}

export interface BrowserWorkspaceShareEncodeResult {
  readonly succeeded: boolean;
  readonly packet: string | null;
  readonly failure: BrowserWorkspaceShareFailure | null;
}

export interface BrowserWorkspaceShareFailure {
  readonly kind: string;
  readonly path: string;
  readonly message: string;
}

export interface BrowserWorkspaceShareState {
  readonly tabs: ReadonlyArray<BrowserWorkspaceShareTab>;
  readonly contexts: ReadonlyArray<BrowserWorkspaceShareContext>;
  readonly activeTabId: string;
  readonly selectedContextId: string;
  readonly view: BrowserWorkspaceShareView;
}

export interface BrowserWorkspaceShareTab {
  readonly id: string;
  readonly kind: string;
  readonly source: string;
  readonly version: string | null;
  readonly framework: string | null;
  readonly runtimeIdentifier: string | null;
}

export interface BrowserWorkspaceShareView {
  readonly lens: string | null;
  readonly type: string | null;
  readonly memberAnchor: string | null;
  readonly memberSignature: string | null;
  readonly section: string | null;
  readonly libraries: ReadonlyArray<string>;
}

type $ManagedExports = {
  readonly "CatalogExports": {
    readonly "DecodeWorkspaceShareState.304094707": (encoded: string) => string;
    readonly "EncodeWorkspaceShareState.304094707": (stateJson: string) => string;
    readonly "ListHomeDemos.1310674786": () => string;
    readonly "ListVocabulary.1310674786": () => string;
    readonly "ResolveHomeDemo.304094707": (scenarioId: string) => string;
    readonly "RunHomeDemo.976702342": (scenarioId: string) => Promise<string>;
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
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "DecodeWorkspaceShareState.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CatalogExports.DecodeWorkspaceShareState.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "EncodeWorkspaceShareState.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CatalogExports.EncodeWorkspaceShareState.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ListHomeDemos.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CatalogExports.ListHomeDemos.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ListVocabulary.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CatalogExports.ListVocabulary.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "ResolveHomeDemo.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CatalogExports.ResolveHomeDemo.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "CatalogExports");
    value = $ownDataProperty(value, "RunHomeDemo.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CatalogExports.RunHomeDemo.976702342\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine.CatalogExports");
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

export function decodeWorkspaceShareState(encoded: string): BrowserWorkspaceShareDecodeResult {
  const $result = $requireManagedExports()["CatalogExports"]["DecodeWorkspaceShareState.304094707"](encoded);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareDecodeResult;
}

export function encodeWorkspaceShareState(stateJson: string): BrowserWorkspaceShareEncodeResult {
  const $result = $requireManagedExports()["CatalogExports"]["EncodeWorkspaceShareState.304094707"](stateJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareEncodeResult;
}

export function listHomeDemos(): BrowserHomeDemoCatalog {
  const $result = $requireManagedExports()["CatalogExports"]["ListHomeDemos.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoCatalog;
}

export function listVocabulary(): BrowserVocabularyDocument {
  const $result = $requireManagedExports()["CatalogExports"]["ListVocabulary.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserVocabularyDocument;
}

export function resolveHomeDemo(scenarioId: string): BrowserHomeDemoResolveResult {
  const $result = $requireManagedExports()["CatalogExports"]["ResolveHomeDemo.304094707"](scenarioId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoResolveResult;
}

export async function runHomeDemo(scenarioId: string): Promise<BrowserHomeDemoRunResult> {
  const $result = await $requireManagedExports()["CatalogExports"]["RunHomeDemo.976702342"](scenarioId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoRunResult;
}

