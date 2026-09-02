// GENERATED FILE — DO NOT EDIT BY HAND.
//
// Generated from InspectWeb.Engine.dll's [JSExport] surface. Regenerate with:
//   eng/generate-inspect-web-engine-facade.sh
// CI fails if this facade or either compiler-derived artifact drifts.

import { dotnet, type RuntimeAPI } from "./_framework/dotnet.js";

export type BrowserAnnotatedSourceCapabilityUnavailableReason = "NotProjected" | "ContextUnavailable" | number;

export type BrowserAnnotatedSourceMedium = "CSharp" | "Il" | number;

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export type BrowserDependencyCoordinateMatchOutcome = "NoMatch" | "Unique" | "Ambiguous" | number;

export type BrowserDependencyCoordinateProvenance = "NuGetPackage" | "PlatformRuntime" | number;

export type BrowserPackageQueryCompletionKind = "Exhausted" | "MatchLimitReached" | "CandidateLimitReached" | "SourcePageLimitReached" | "ClientPageLimitReached" | "Failed" | number;

export type BrowserPackageQueryEventKind = "Progress" | "Match" | "Failure" | "Completed" | number;

export type BrowserPackageQueryFacetTier = "Nuspec" | "PackageContent" | number;

export type BrowserPackageQueryFailureKind = "Search" | "SearchContract" | "ManifestAcquisition" | "ManifestContract" | "InvalidManifest" | "PackageContentAcquisition" | "PackageContentEvaluation" | number;

export type BrowserPackageQueryProgressPhase = "Search" | "Manifest" | "PackageContent" | number;

export interface BrowserAccessibilityDescriptor {
  readonly id: string;
  readonly label: string;
  readonly order: number;
  readonly isDefault: boolean;
  readonly count: number;
}

export interface BrowserAllocationFact {
  readonly kind: string;
  readonly type: string | null;
  readonly offset: string;
  readonly countedAsHeap: boolean;
  readonly frequency: string;
  readonly multiplicity: string;
  readonly path: string;
  readonly escape: string;
  readonly inLoop: boolean;
  readonly estimatedSizeBytes: number | null;
  readonly detail: string | null;
}

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

export interface BrowserBuildIdentity {
  readonly version: string;
  readonly commit: string | null;
  readonly builtAtUtc: string | null;
  readonly commitUrl: string | null;
}

export interface BrowserCallFact {
  readonly callee: string;
  readonly offset: string;
  readonly opcode: string;
  readonly kind: string;
  readonly multiplicity: string;
  readonly inLoop: boolean;
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

export interface BrowserExceptionRegion {
  readonly region: number;
  readonly clause: string;
  readonly tryRange: string;
  readonly handlerRange: string;
  readonly filterRange: string | null;
  readonly caughtType: string | null;
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

export interface BrowserIntegrationCategory {
  readonly integration: string;
  readonly signals: ReadonlyArray<BrowserIntegrationSignal>;
}

export interface BrowserIntegrationSignal {
  readonly kind: string;
  readonly name: string;
  readonly shape: string;
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

export interface BrowserMemberFacts {
  readonly metadataToken: number;
  readonly signals: BrowserMethodSignals;
  readonly allocations: ReadonlyArray<BrowserAllocationFact>;
  readonly calls: ReadonlyArray<BrowserCallFact>;
  readonly safety: ReadonlyArray<BrowserSafetyFact>;
  readonly exceptionRegions: ReadonlyArray<BrowserExceptionRegion>;
  readonly performanceOpportunities: ReadonlyArray<BrowserPerformanceOpportunity>;
  readonly diagnostics: ReadonlyArray<string>;
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

export interface BrowserMethodSignals {
  readonly allocations: number;
  readonly copies: number;
  readonly unsafe: boolean;
  readonly reflection: number;
  readonly throws: number;
  readonly catches: number;
  readonly finallys: number;
  readonly allocatesInLoop: boolean;
  readonly evidenceOffsets: ReadonlyArray<string>;
  readonly exceptionTypes: ReadonlyArray<string>;
}

export interface BrowserOpportunityCategory {
  readonly integration: string;
  readonly items: ReadonlyArray<BrowserOpportunityItem>;
}

export interface BrowserOpportunityItem {
  readonly api: string;
  readonly integrationType: string;
  readonly lookFor: string;
  readonly sourceDefinitionId: string | null;
  readonly sourceAssembly: string;
  readonly sourceAssemblyVersion: string;
  readonly sourceAssemblyCulture: string | null;
  readonly sourceAssemblyPublicKeyToken: string | null;
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

export interface BrowserPackageIntegrations {
  readonly package: string;
  readonly version: string;
  readonly framework: string;
  readonly categories: ReadonlyArray<BrowserIntegrationCategory>;
  readonly totalSignals: number;
  readonly isComplete: boolean;
  readonly inspectionError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPackageMetadata {
  readonly assemblies: ReadonlyArray<BrowserAssemblyMetadata>;
  readonly inspectionError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPackageOpportunities {
  readonly package: string;
  readonly version: string;
  readonly activeFramework: string;
  readonly categories: ReadonlyArray<BrowserOpportunityCategory>;
  readonly totalOpportunities: number;
  readonly isComplete: boolean;
  readonly inspectionError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPackagePerformance {
  readonly members: ReadonlyArray<BrowserPerformanceMember>;
  readonly inspectionError: string | null;
  readonly nonPublicOpportunities: number;
  readonly totalOpportunities: number;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
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

export interface BrowserPerformanceMember {
  readonly assembly: string;
  readonly typeId: string;
  readonly memberName: string;
  readonly stableSelector: string;
  readonly bodyTokens: ReadonlyArray<number>;
  readonly opportunityCount: number;
  readonly inLoopCount: number;
  readonly shapes: ReadonlyArray<string>;
  readonly confidence: string;
}

export interface BrowserPerformanceOpportunity {
  readonly shape: string;
  readonly evidence: string;
  readonly fix: string;
  readonly confidence: string;
  readonly offset: string | null;
  readonly inLoop: boolean;
  readonly caveat: string | null;
  readonly finding: string | null;
  readonly provenance: string;
}

export interface BrowserSafetyFact {
  readonly kind: string;
  readonly offset: string | null;
  readonly operation: string;
  readonly requirement: string;
  readonly evidence: string;
}

export interface BrowserSource {
  readonly provider: string;
  readonly provenance: string;
  readonly url: string | null;
  readonly pdbSourceLimitation: string | null;
  readonly text: string;
}

export interface BrowserTypeCandidate {
  readonly key: string;
  readonly name: string;
  readonly full: string;
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
  readonly "InspectionEngine": {
    readonly "AsyncLoweringCanary.1684317047": () => Promise<string>;
    readonly "BuildIdentity.1310674786": () => string;
    readonly "CancelPackageQuery.19325221": () => void;
    readonly "CancelSourceQuery.19325221": () => void;
    readonly "ConfigureHost.92020726": (origin: string) => void;
    readonly "DecodeWorkspaceShareState.304094707": (encoded: string) => string;
    readonly "EncodeWorkspaceShareState.304094707": (stateJson: string) => string;
    readonly "ExpandPlatformCallGraph.1136010516": (targetFramework: string, platformVersion: string, assembly: string, pack: string, assemblyVersion: string, assemblyCulture: string | null, assemblyPublicKeyToken: string | null, typeFullName: string, memberName: string, selectorKey: string, metadataToken: number) => Promise<string>;
    readonly "GetPackageDocument.1001223652": (packageId: string, version: string, path: string) => Promise<string>;
    readonly "ListHomeDemos.1310674786": () => string;
    readonly "ListPackageQueryFacets.1310674786": () => string;
    readonly "ListVocabulary.1310674786": () => string;
    readonly "LoadRuntimePack.451505237": (targetFramework: string, platformVersion: string) => Promise<string>;
    readonly "LoadRuntimePackAssembly.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "MatchPackageDependencyCoordinate.1537767637": (packageId: string, declaredRange: string | null, candidatesJson: string) => string;
    readonly "PackageCacheStats.1310674786": () => string;
    readonly "QueryGraphMemberSurface.1542089313": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number) => Promise<string>;
    readonly "QueryMemberAnnotatedSource.1135530322": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
    readonly "QueryMemberCallGraph.1135530322": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, workspaceJson: string) => Promise<string>;
    readonly "QueryMemberDocumentation.1330709314": (packageId: string, version: string, framework: string, assemblyName: string, documentationId: string) => Promise<string>;
    readonly "QueryMemberFacts.581406856": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, implementationBodySelected: boolean) => Promise<string>;
    readonly "QueryMemberSource.641907440": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
    readonly "QueryPackage.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackageDependencies.1579276339": (packageId: string, version: string, targetFramework: string, assemblyId: string) => Promise<string>;
    readonly "QueryPackageHeapEntries.1330709314": (packageId: string, version: string, targetFramework: string, assemblyFileName: string, heap: string) => Promise<string>;
    readonly "QueryPackageIntegrations.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackageMetadata.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackageMetadataTable.1509466830": (packageId: string, version: string, targetFramework: string, assemblyFileName: string, tableIndex: number, startRowId: number, maxRows: number) => Promise<string>;
    readonly "QueryPackageOpportunities.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackagePerformance.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackageVersions.976702342": (packageId: string) => Promise<string>;
    readonly "QueryPlatformHeapEntries.1330709314": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, heap: string) => Promise<string>;
    readonly "QueryPlatformIntegrations.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformMetadata.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformMetadataTable.1509466830": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, tableIndex: number, startRowId: number, maxRows: number) => Promise<string>;
    readonly "QueryPlatformOpportunities.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformPerformance.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryTypeMemberSource.641907440": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
    readonly "QueryTypeProjection.1330709314": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeId: string) => Promise<string>;
    readonly "QueryTypeSource.649160465": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string) => Promise<string>;
    readonly "ResolveHomeDemo.304094707": (scenarioId: string) => string;
    readonly "ResolvePackageDependencyVersion.451505237": (packageId: string, declaredRange: string | null) => Promise<string>;
    readonly "RunHomeDemo.976702342": (scenarioId: string) => Promise<string>;
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
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "AsyncLoweringCanary.1684317047");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.AsyncLoweringCanary.1684317047\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "BuildIdentity.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.BuildIdentity.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "CancelPackageQuery.19325221");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.CancelPackageQuery.19325221\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "CancelSourceQuery.19325221");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.CancelSourceQuery.19325221\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ConfigureHost.92020726");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ConfigureHost.92020726\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "DecodeWorkspaceShareState.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.DecodeWorkspaceShareState.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "EncodeWorkspaceShareState.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.EncodeWorkspaceShareState.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ExpandPlatformCallGraph.1136010516");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ExpandPlatformCallGraph.1136010516\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "GetPackageDocument.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.GetPackageDocument.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ListHomeDemos.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ListHomeDemos.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ListPackageQueryFacets.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ListPackageQueryFacets.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ListVocabulary.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ListVocabulary.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "LoadRuntimePack.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.LoadRuntimePack.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "LoadRuntimePackAssembly.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.LoadRuntimePackAssembly.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "MatchPackageDependencyCoordinate.1537767637");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.MatchPackageDependencyCoordinate.1537767637\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "PackageCacheStats.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.PackageCacheStats.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryGraphMemberSurface.1542089313");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryGraphMemberSurface.1542089313\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryMemberAnnotatedSource.1135530322");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryMemberAnnotatedSource.1135530322\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryMemberCallGraph.1135530322");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryMemberCallGraph.1135530322\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryMemberDocumentation.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryMemberDocumentation.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryMemberFacts.581406856");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryMemberFacts.581406856\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryMemberSource.641907440");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryMemberSource.641907440\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackage.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackage.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackageDependencies.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackageDependencies.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackageHeapEntries.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackageHeapEntries.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackageIntegrations.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackageIntegrations.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackageMetadata.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackageMetadata.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackageMetadataTable.1509466830");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackageMetadataTable.1509466830\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackageOpportunities.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackageOpportunities.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackagePerformance.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackagePerformance.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPackageVersions.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPackageVersions.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPlatformHeapEntries.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPlatformHeapEntries.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPlatformIntegrations.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPlatformIntegrations.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPlatformMetadata.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPlatformMetadata.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPlatformMetadataTable.1509466830");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPlatformMetadataTable.1509466830\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPlatformOpportunities.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPlatformOpportunities.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryPlatformPerformance.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryPlatformPerformance.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryTypeMemberSource.641907440");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryTypeMemberSource.641907440\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryTypeProjection.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryTypeProjection.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "QueryTypeSource.649160465");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.QueryTypeSource.649160465\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ResolveHomeDemo.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ResolveHomeDemo.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ResolvePackageDependencyVersion.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ResolvePackageDependencyVersion.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "RunHomeDemo.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.RunHomeDemo.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "RunPackageQuery.287304775");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.RunPackageQuery.287304775\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "SearchTypes.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.SearchTypes.271973316\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(): Promise<void> {
  const runtime = await dotnet.create();
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine");
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

export async function asyncLoweringCanary(): Promise<string> {
  return await $requireManagedExports()["InspectionEngine"]["AsyncLoweringCanary.1684317047"]();
}

export function buildIdentity(): BrowserBuildIdentity {
  const $result = $requireManagedExports()["InspectionEngine"]["BuildIdentity.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserBuildIdentity;
}

export function cancelPackageQuery(): void {
  return $requireManagedExports()["InspectionEngine"]["CancelPackageQuery.19325221"]();
}

export function cancelSourceQuery(): void {
  return $requireManagedExports()["InspectionEngine"]["CancelSourceQuery.19325221"]();
}

export function configureHost(origin: string): void {
  return $requireManagedExports()["InspectionEngine"]["ConfigureHost.92020726"](origin);
}

export function decodeWorkspaceShareState(encoded: string): BrowserWorkspaceShareDecodeResult {
  const $result = $requireManagedExports()["InspectionEngine"]["DecodeWorkspaceShareState.304094707"](encoded);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareDecodeResult;
}

export function encodeWorkspaceShareState(stateJson: string): BrowserWorkspaceShareEncodeResult {
  const $result = $requireManagedExports()["InspectionEngine"]["EncodeWorkspaceShareState.304094707"](stateJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspaceShareEncodeResult;
}

export async function expandPlatformCallGraph(targetFramework: string, platformVersion: string, assembly: string, pack: string, assemblyVersion: string, assemblyCulture: string | null, assemblyPublicKeyToken: string | null, typeFullName: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserCallGraph> {
  const $result = await $requireManagedExports()["InspectionEngine"]["ExpandPlatformCallGraph.1136010516"](targetFramework, platformVersion, assembly, pack, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, typeFullName, memberName, selectorKey, metadataToken);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserCallGraph;
}

export async function getPackageDocument(packageId: string, version: string, path: string): Promise<BrowserPackageDocumentContent> {
  const $result = await $requireManagedExports()["InspectionEngine"]["GetPackageDocument.1001223652"](packageId, version, path);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageDocumentContent;
}

export function listHomeDemos(): BrowserHomeDemoCatalog {
  const $result = $requireManagedExports()["InspectionEngine"]["ListHomeDemos.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoCatalog;
}

export function listPackageQueryFacets(): BrowserPackageQueryFacetCatalog {
  const $result = $requireManagedExports()["InspectionEngine"]["ListPackageQueryFacets.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryFacetCatalog;
}

export function listVocabulary(): BrowserVocabularyDocument {
  const $result = $requireManagedExports()["InspectionEngine"]["ListVocabulary.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserVocabularyDocument;
}

export async function loadRuntimePack(targetFramework: string, platformVersion: string): Promise<string> {
  return await $requireManagedExports()["InspectionEngine"]["LoadRuntimePack.451505237"](targetFramework, platformVersion);
}

export async function loadRuntimePackAssembly(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<string> {
  return await $requireManagedExports()["InspectionEngine"]["LoadRuntimePackAssembly.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}

export function matchPackageDependencyCoordinate(packageId: string, declaredRange: string | null, candidatesJson: string): BrowserDependencyCoordinateMatch {
  const $result = $requireManagedExports()["InspectionEngine"]["MatchPackageDependencyCoordinate.1537767637"](packageId, declaredRange, candidatesJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserDependencyCoordinateMatch;
}

export function packageCacheStats(): BrowserPackageCacheStats {
  const $result = $requireManagedExports()["InspectionEngine"]["PackageCacheStats.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageCacheStats;
}

export async function queryGraphMemberSurface(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserGraphMemberSurface> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryGraphMemberSurface.1542089313"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserGraphMemberSurface;
}

export async function queryMemberAnnotatedSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserAnnotatedSource> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberAnnotatedSource.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserAnnotatedSource;
}

export async function queryMemberCallGraph(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, workspaceJson: string): Promise<BrowserCallGraph> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberCallGraph.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, workspaceJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserCallGraph;
}

export async function queryMemberDocumentation(packageId: string, version: string, framework: string, assemblyName: string, documentationId: string): Promise<BrowserMemberDocumentation> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberDocumentation.1330709314"](packageId, version, framework, assemblyName, documentationId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberDocumentation;
}

export async function queryMemberFacts(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, implementationBodySelected: boolean): Promise<BrowserMemberFacts> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberFacts.581406856"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, memberSignature, selectorKey, metadataToken, implementationBodySelected);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberFacts;
}

export async function queryMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserSource;
}

export async function queryPackage(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageSurface> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackage.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageSurface;
}

export async function queryPackageDependencies(packageId: string, version: string, targetFramework: string, assemblyId: string): Promise<BrowserPackageDependencies> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageDependencies.1579276339"](packageId, version, targetFramework, assemblyId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageDependencies;
}

export async function queryPackageHeapEntries(packageId: string, version: string, targetFramework: string, assemblyFileName: string, heap: string): Promise<BrowserHeapListing> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageHeapEntries.1330709314"](packageId, version, targetFramework, assemblyFileName, heap);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHeapListing;
}

export async function queryPackageIntegrations(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageIntegrations> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageIntegrations.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageIntegrations;
}

export async function queryPackageMetadata(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageMetadata> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageMetadata.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageMetadata;
}

export async function queryPackageMetadataTable(packageId: string, version: string, targetFramework: string, assemblyFileName: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageMetadataTable.1509466830"](packageId, version, targetFramework, assemblyFileName, tableIndex, startRowId, maxRows);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMetadataWindow;
}

export async function queryPackageOpportunities(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageOpportunities> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageOpportunities.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageOpportunities;
}

export async function queryPackagePerformance(packageId: string, version: string, targetFramework: string): Promise<BrowserPackagePerformance> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackagePerformance.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackagePerformance;
}

export async function queryPackageVersions(packageId: string): Promise<ReadonlyArray<string>> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageVersions.976702342"](packageId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<string>;
}

export async function queryPlatformHeapEntries(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, heap: string): Promise<BrowserHeapListing> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformHeapEntries.1330709314"](targetFramework, platformVersion, assemblyFileName, pack, heap);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHeapListing;
}

export async function queryPlatformIntegrations(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageIntegrations> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformIntegrations.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageIntegrations;
}

export async function queryPlatformMetadata(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageMetadata> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformMetadata.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageMetadata;
}

export async function queryPlatformMetadataTable(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformMetadataTable.1509466830"](targetFramework, platformVersion, assemblyFileName, pack, tableIndex, startRowId, maxRows);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMetadataWindow;
}

export async function queryPlatformOpportunities(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageOpportunities> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformOpportunities.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageOpportunities;
}

export async function queryPlatformPerformance(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<string> {
  return await $requireManagedExports()["InspectionEngine"]["QueryPlatformPerformance.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}

export async function queryTypeMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryTypeMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserSource;
}

export async function queryTypeProjection(packageId: string, version: string, targetFramework: string, assemblyName: string, typeId: string): Promise<BrowserTypeMetadata> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryTypeProjection.1330709314"](packageId, version, targetFramework, assemblyName, typeId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeMetadata;
}

export async function queryTypeSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string): Promise<BrowserSource> {
  const $result = await $requireManagedExports()["InspectionEngine"]["QueryTypeSource.649160465"](packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserSource;
}

export function resolveHomeDemo(scenarioId: string): BrowserHomeDemoResolveResult {
  const $result = $requireManagedExports()["InspectionEngine"]["ResolveHomeDemo.304094707"](scenarioId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoResolveResult;
}

export async function resolvePackageDependencyVersion(packageId: string, declaredRange: string | null): Promise<string> {
  return await $requireManagedExports()["InspectionEngine"]["ResolvePackageDependencyVersion.451505237"](packageId, declaredRange);
}

export async function runHomeDemo(scenarioId: string): Promise<BrowserHomeDemoRunResult> {
  const $result = await $requireManagedExports()["InspectionEngine"]["RunHomeDemo.976702342"](scenarioId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHomeDemoRunResult;
}

export async function runPackageQuery(prefix: string, facetIdsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, eventSink: unknown): Promise<BrowserPackageQueryEvent> {
  const $result = await $requireManagedExports()["InspectionEngine"]["RunPackageQuery.287304775"](prefix, facetIdsJson, maximumCandidates, maximumMatches, includePrerelease, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryEvent;
}

export function searchTypes(query: string, candidatesJson: string): ReadonlyArray<BrowserTypeSearchHit> {
  const $result = $requireManagedExports()["InspectionEngine"]["SearchTypes.271973316"](query, candidatesJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<BrowserTypeSearchHit>;
}

