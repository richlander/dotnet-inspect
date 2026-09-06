export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;
export type BrowserDependencyCoordinateMatchOutcome = "NoMatch" | "Unique" | "Ambiguous" | number;
export type BrowserDependencyCoordinateProvenance = "NuGetPackage" | "PlatformRuntime" | number;
export type BrowserPackageQueryCompletionKind = "Exhausted" | "MatchLimitReached" | "CandidateLimitReached" | "SourcePageLimitReached" | "ClientPageLimitReached" | "Failed" | "GalleryResponseComplete" | number;
export type BrowserPackageQueryEventKind = "Progress" | "Match" | "Failure" | "Completed" | number;
export type BrowserPackageQueryFacetTier = "Nuspec" | "PackageContent" | "SearchMetadata" | number;
export type BrowserPackageQueryFailureKind = "Search" | "SearchContract" | "ManifestAcquisition" | "ManifestContract" | "InvalidManifest" | "PackageContentAcquisition" | "PackageContentEvaluation" | number;
export type BrowserPackageQueryProgressPhase = "Search" | "Manifest" | "PackageContent" | number;
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
export interface BrowserGalleryDiscoveryCatalog {
    readonly packageType: BrowserGalleryPackageTypeFacet;
    readonly orders: ReadonlyArray<BrowserGalleryDiscoveryOrder>;
}
export interface BrowserGalleryDiscoveryOrder {
    readonly id: string;
    readonly label: string;
    readonly summary: string;
}
export interface BrowserGalleryPackageTypeFacet {
    readonly id: string;
    readonly label: string;
    readonly summary: string;
    readonly suggestions: ReadonlyArray<BrowserGalleryPackageTypeSuggestion>;
}
export interface BrowserGalleryPackageTypeSuggestion {
    readonly value: string;
    readonly label: string;
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
    readonly sourceCandidates: number | null;
    readonly estimatedTotalHits: number | null;
}
export interface BrowserPackageQueryEvent {
    readonly kind: BrowserPackageQueryEventKind;
    readonly row: BrowserPackageQueryRow | null;
    readonly failure: BrowserPackageQueryFailure | null;
    readonly completion: BrowserPackageQueryCompletion | null;
    readonly progress: BrowserPackageQueryProgress | null;
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
    readonly combinesWithinSelectionGroup: boolean;
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
export interface BrowserPackageQueryProgress {
    readonly phase: BrowserPackageQueryProgressPhase;
    readonly completed: number;
    readonly limit: number;
}
export interface BrowserPackageQueryRow {
    readonly packageId: string;
    readonly version: string;
    readonly tier: BrowserPackageQueryFacetTier;
    readonly evidence: ReadonlyArray<BrowserPackageQueryEvidence>;
    readonly totalDownloads: number | null;
    readonly verified: boolean | null;
    readonly producer: string;
    readonly description: string | null;
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
export interface BrowserPackageVersions {
    readonly versions: ReadonlyArray<string>;
    readonly currentVersionInsertionIndex: number;
    readonly previousVersion: string | null;
    readonly previousVersionUnavailableReason: string | null;
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
export interface JsExportRuntime {
    readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
    readonly runMain: (mainAssemblyName?: string, args?: string[]) => Promise<number>;
}
export declare function createRuntime(): Promise<JsExportRuntime>;
export declare function initializeRuntime(runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function activateWorkspacePackageOccurrence(action: string): Promise<BrowserWorkspacePackageOccurrenceActivation>;
export declare function cancelPackageQuery(): void;
export declare function clearWorkspacePackageOccurrences(): void;
export declare function getPackageDocument(packageId: string, version: string, path: string): Promise<BrowserPackageDocumentContent>;
export declare function listGalleryDiscoveryCatalog(): BrowserGalleryDiscoveryCatalog;
export declare function listPackageQueryFacets(): BrowserPackageQueryFacetCatalog;
export declare function loadRuntimePack(targetFramework: string, platformVersion: string): Promise<string>;
export declare function loadRuntimePackAssembly(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<string>;
export declare function matchPackageDependencyCoordinate(packageId: string, declaredRange: string | null, candidatesJson: string): BrowserDependencyCoordinateMatch;
export declare function packageCacheStats(): BrowserPackageCacheStats;
export declare function queryMemberDocumentation(packageId: string, version: string, framework: string, assemblyName: string, documentationId: string): Promise<BrowserMemberDocumentation>;
export declare function queryPackage(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageSurface>;
export declare function queryPackageDependencies(packageId: string, version: string, targetFramework: string, assemblyId: string): Promise<BrowserPackageDependencies>;
export declare function queryPackageVersions(packageId: string, currentVersion: string): Promise<BrowserPackageVersions>;
export declare function queryWorkspacePackageOccurrences(workspaceJson: string): Promise<BrowserWorkspacePackageOccurrenceView>;
export declare function requestPackageQueryMatches(additionalMatchCredit: number): boolean;
export declare function resolvePackageDependencyVersion(packageId: string, declaredRange: string | null): Promise<string>;
export declare function runPackageQuery(prefix: string, facetIdsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, initialMatchCredit: number, eventSink: unknown, packageType: string | null, sourceOrderId: string | null): Promise<BrowserPackageQueryEvent>;
export declare function searchTypes(query: string, candidatesJson: string): ReadonlyArray<BrowserTypeSearchHit>;
