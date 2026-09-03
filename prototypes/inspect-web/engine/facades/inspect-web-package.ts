import { dotnet, type RuntimeAPI } from "./_framework/dotnet.js";

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export type BrowserDependencyCoordinateMatchOutcome = "NoMatch" | "Unique" | "Ambiguous" | number;

export type BrowserDependencyCoordinateProvenance = "NuGetPackage" | "PlatformRuntime" | number;

export type BrowserPackageQueryCompletionKind = "Exhausted" | "MatchLimitReached" | "CandidateLimitReached" | "SourcePageLimitReached" | "ClientPageLimitReached" | "Failed" | number;

export type BrowserPackageQueryEventKind = "Match" | "Failure" | "Completed" | number;

export type BrowserPackageQueryFacetTier = "Nuspec" | "PackageContent" | number;

export type BrowserPackageQueryFailureKind = "Search" | "SearchContract" | "ManifestAcquisition" | "ManifestContract" | "InvalidManifest" | "PackageContentAcquisition" | "PackageContentEvaluation" | number;

export interface BrowserAccessibilityDescriptor {
  readonly id: string;
  readonly label: string;
  readonly order: number;
  readonly isDefault: boolean;
  readonly count: number;
}

export interface BrowserAssemblyReference {
  readonly name: string;
  readonly version: string;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
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

export interface BrowserCompileLibraryAvailability {
  readonly status: BrowserCompileLibraryStatus;
  readonly targetFramework: string | null;
  readonly message: string | null;
}

export interface BrowserDependencyCoordinateCandidate {
  readonly key: string;
  readonly provenance: BrowserDependencyCoordinateProvenance;
  readonly packageId: string;
  readonly version: string;
  readonly targetFramework: string;
}

export interface BrowserDependencyCoordinateMatch {
  readonly outcome: BrowserDependencyCoordinateMatchOutcome;
  readonly candidateKey: string | null;
}

export interface BrowserExceptionSurface {
  readonly type: string;
  readonly description: string;
}

export interface BrowserMemberBodySelector {
  readonly token: number;
  readonly memberName: string;
  readonly selectorKey: string;
}

export interface BrowserMemberDocumentation {
  readonly summary: string | null;
  readonly returns: string | null;
  readonly parameters: Readonly<Record<string, string>>;
  readonly exceptions: ReadonlyArray<BrowserExceptionSurface>;
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

export interface BrowserPackageCacheStats {
  readonly packages: number;
  readonly resident: number;
  readonly workspaces: number;
  readonly residentBytes: number;
}

export interface BrowserPackageDependencies {
  readonly package: string;
  readonly version: string;
  readonly activeFramework: string;
  readonly assembly: string | null;
  readonly dependencyGroups: ReadonlyArray<BrowserPackageDependencyGroup>;
  readonly assemblyReferences: ReadonlyArray<BrowserAssemblyReference>;
  readonly dependencyGroupError: string | null;
  readonly assemblyReferenceError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPackageDependency {
  readonly id: string;
  readonly versionRange: string;
}

export interface BrowserPackageDependencyGroup {
  readonly index: number;
  readonly framework: string;
  readonly isActive: boolean;
  readonly dependencies: ReadonlyArray<BrowserPackageDependency>;
}

export interface BrowserPackageDocument {
  readonly kind: string;
  readonly name: string;
  readonly path: string;
  readonly size: number;
}

export interface BrowserPackageDocumentContent {
  readonly kind: string;
  readonly name: string;
  readonly path: string;
  readonly text: string;
}

export interface BrowserPackageIcon {
  readonly mediaType: string;
  readonly base64: string;
}

export interface BrowserPackageQueryCompletion {
  readonly prefix: string;
  readonly producer: string;
  readonly candidateLimit: number;
  readonly matchLimit: number;
  readonly candidates: number;
  readonly matches: number;
  readonly failures: number;
  readonly kind: BrowserPackageQueryCompletionKind;
}

export interface BrowserPackageQueryEvent {
  readonly kind: BrowserPackageQueryEventKind;
  readonly row: BrowserPackageQueryRow | null;
  readonly failure: BrowserPackageQueryFailure | null;
  readonly completion: BrowserPackageQueryCompletion | null;
}

export interface BrowserPackageQueryEvidence {
  readonly id: string;
  readonly text: string;
}

export interface BrowserPackageQueryFacetCatalog {
  readonly facets: ReadonlyArray<BrowserPackageQueryFacetDescriptor>;
}

export interface BrowserPackageQueryFacetDescriptor {
  readonly id: string;
  readonly label: string;
  readonly summary: string;
  readonly weight: number;
  readonly tier: BrowserPackageQueryFacetTier;
  readonly selectionGroupId: string | null;
  readonly displayGroupId: string | null;
  readonly displayGroupLabel: string | null;
}

export interface BrowserPackageQueryFailure {
  readonly packageId: string | null;
  readonly version: string | null;
  readonly producer: string;
  readonly kind: BrowserPackageQueryFailureKind;
  readonly message: string;
}

export interface BrowserPackageQueryRow {
  readonly packageId: string;
  readonly version: string;
  readonly tier: BrowserPackageQueryFacetTier;
  readonly evidence: ReadonlyArray<BrowserPackageQueryEvidence>;
  readonly totalDownloads: number;
  readonly verified: boolean;
  readonly producer: string;
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

export interface BrowserTypeCandidate {
  readonly key: string;
  readonly name: string;
  readonly full: string;
}

export interface BrowserTypeSearchHit {
  readonly key: string;
  readonly kind: string;
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

export interface BrowserWorkspacePackage {
  readonly package: string;
  readonly version: string;
  readonly framework: string;
}

export interface BrowserWorkspacePackageOccurrence {
  readonly action: string;
  readonly package: string;
  readonly version: string;
  readonly framework: string;
}

export interface BrowserWorkspacePackageOccurrenceActivation {
  readonly activated: boolean;
  readonly superseded: boolean;
  readonly package: BrowserPackageSurface | null;
}

export interface BrowserWorkspacePackageOccurrenceView {
  readonly occurrences: ReadonlyArray<BrowserWorkspacePackageOccurrence>;
  readonly superseded: boolean;
}

type $ManagedExports = {
  readonly "PackageExports": {
    readonly "ActivateWorkspacePackageOccurrence.304094707": (action: string) => string;
    readonly "CancelPackageQuery.19325221": () => void;
    readonly "ClearWorkspacePackageOccurrences.19325221": () => void;
    readonly "GetPackageDocument.1001223652": (packageId: string, version: string, path: string) => Promise<string>;
    readonly "ListPackageQueryFacets.1310674786": () => string;
    readonly "LoadRuntimePack.451505237": (targetFramework: string, platformVersion: string) => Promise<string>;
    readonly "LoadRuntimePackAssembly.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "MatchPackageDependencyCoordinate.1537767637": (packageId: string, declaredRange: string | null, candidatesJson: string) => string;
    readonly "PackageCacheStats.1310674786": () => string;
    readonly "QueryMemberDocumentation.1330709314": (packageId: string, version: string, framework: string, assemblyName: string, documentationId: string) => Promise<string>;
    readonly "QueryPackage.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackageDependencies.1579276339": (packageId: string, version: string, targetFramework: string, assemblyId: string) => Promise<string>;
    readonly "QueryPackageVersions.976702342": (packageId: string) => Promise<string>;
    readonly "QueryWorkspacePackageOccurrences.976702342": (workspaceJson: string) => Promise<string>;
    readonly "ResolvePackageDependencyVersion.451505237": (packageId: string, declaredRange: string | null) => Promise<string>;
    readonly "RunPackageQuery.287304775": (prefix: string, facetIdsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, eventSink: unknown) => Promise<string>;
    readonly "SearchTypes.271973316": (query: string, candidatesJson: string) => string;
  };
};

const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
let $runtime: RuntimeAPI | undefined;
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

function $requireRuntime(): RuntimeAPI {
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
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ActivateWorkspacePackageOccurrence.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.ActivateWorkspacePackageOccurrence.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "CancelPackageQuery.19325221");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.CancelPackageQuery.19325221\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ClearWorkspacePackageOccurrences.19325221");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.ClearWorkspacePackageOccurrences.19325221\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "GetPackageDocument.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.GetPackageDocument.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ListPackageQueryFacets.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.ListPackageQueryFacets.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "LoadRuntimePack.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.LoadRuntimePack.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "LoadRuntimePackAssembly.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.LoadRuntimePackAssembly.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "MatchPackageDependencyCoordinate.1537767637");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.MatchPackageDependencyCoordinate.1537767637\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "PackageCacheStats.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.PackageCacheStats.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryMemberDocumentation.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.QueryMemberDocumentation.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackage.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.QueryPackage.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackageDependencies.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.QueryPackageDependencies.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackageVersions.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.QueryPackageVersions.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryWorkspacePackageOccurrences.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.QueryWorkspacePackageOccurrences.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ResolvePackageDependencyVersion.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.ResolvePackageDependencyVersion.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "RunPackageQuery.287304775");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.RunPackageQuery.287304775\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "SearchTypes.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027PackageExports.SearchTypes.271973316\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(): Promise<void> {
  const runtime = await dotnet.create();
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine.PackageExports");
  $validateManagedExports(exports);
  $runtime = runtime;
  $managedExports = exports;
}

export function initializeRuntime(): Promise<void> {
  if ($initialization === undefined) {
    $initialization = Promise.resolve()
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

export function activateWorkspacePackageOccurrence(action: string): BrowserWorkspacePackageOccurrenceActivation {
  const $result = $requireManagedExports()["PackageExports"]["ActivateWorkspacePackageOccurrence.304094707"](action);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspacePackageOccurrenceActivation;
}

export function cancelPackageQuery(): void {
  return $requireManagedExports()["PackageExports"]["CancelPackageQuery.19325221"]();
}

export function clearWorkspacePackageOccurrences(): void {
  return $requireManagedExports()["PackageExports"]["ClearWorkspacePackageOccurrences.19325221"]();
}

export async function getPackageDocument(packageId: string, version: string, path: string): Promise<BrowserPackageDocumentContent> {
  const $result = await $requireManagedExports()["PackageExports"]["GetPackageDocument.1001223652"](packageId, version, path);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageDocumentContent;
}

export function listPackageQueryFacets(): BrowserPackageQueryFacetCatalog {
  const $result = $requireManagedExports()["PackageExports"]["ListPackageQueryFacets.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryFacetCatalog;
}

export async function loadRuntimePack(targetFramework: string, platformVersion: string): Promise<string> {
  return await $requireManagedExports()["PackageExports"]["LoadRuntimePack.451505237"](targetFramework, platformVersion);
}

export async function loadRuntimePackAssembly(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<string> {
  return await $requireManagedExports()["PackageExports"]["LoadRuntimePackAssembly.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}

export function matchPackageDependencyCoordinate(packageId: string, declaredRange: string | null, candidatesJson: string): BrowserDependencyCoordinateMatch {
  const $result = $requireManagedExports()["PackageExports"]["MatchPackageDependencyCoordinate.1537767637"](packageId, declaredRange, candidatesJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserDependencyCoordinateMatch;
}

export function packageCacheStats(): BrowserPackageCacheStats {
  const $result = $requireManagedExports()["PackageExports"]["PackageCacheStats.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageCacheStats;
}

export async function queryMemberDocumentation(packageId: string, version: string, framework: string, assemblyName: string, documentationId: string): Promise<BrowserMemberDocumentation> {
  const $result = await $requireManagedExports()["PackageExports"]["QueryMemberDocumentation.1330709314"](packageId, version, framework, assemblyName, documentationId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberDocumentation;
}

export async function queryPackage(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageSurface> {
  const $result = await $requireManagedExports()["PackageExports"]["QueryPackage.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageSurface;
}

export async function queryPackageDependencies(packageId: string, version: string, targetFramework: string, assemblyId: string): Promise<BrowserPackageDependencies> {
  const $result = await $requireManagedExports()["PackageExports"]["QueryPackageDependencies.1579276339"](packageId, version, targetFramework, assemblyId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageDependencies;
}

export async function queryPackageVersions(packageId: string): Promise<ReadonlyArray<string>> {
  const $result = await $requireManagedExports()["PackageExports"]["QueryPackageVersions.976702342"](packageId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<string>;
}

export async function queryWorkspacePackageOccurrences(workspaceJson: string): Promise<BrowserWorkspacePackageOccurrenceView> {
  const $result = await $requireManagedExports()["PackageExports"]["QueryWorkspacePackageOccurrences.976702342"](workspaceJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspacePackageOccurrenceView;
}

export async function resolvePackageDependencyVersion(packageId: string, declaredRange: string | null): Promise<string> {
  return await $requireManagedExports()["PackageExports"]["ResolvePackageDependencyVersion.451505237"](packageId, declaredRange);
}

export async function runPackageQuery(prefix: string, facetIdsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, eventSink: unknown): Promise<BrowserPackageQueryEvent> {
  const $result = await $requireManagedExports()["PackageExports"]["RunPackageQuery.287304775"](prefix, facetIdsJson, maximumCandidates, maximumMatches, includePrerelease, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryEvent;
}

export function searchTypes(query: string, candidatesJson: string): ReadonlyArray<BrowserTypeSearchHit> {
  const $result = $requireManagedExports()["PackageExports"]["SearchTypes.271973316"](query, candidatesJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<BrowserTypeSearchHit>;
}

