export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;
export type JsonValueKind = number;
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
    readonly focusKind: string;
    readonly focusId: string;
    readonly focusVersion: string;
    readonly focusFramework: string;
    readonly focusAssembly: string | null;
    readonly typeId: string;
    readonly section: string;
    readonly memberName: string | null;
    readonly memberKind: string | null;
    readonly memberAnchorDigest: string | null;
    readonly memberSection: string | null;
    readonly platformContextId: string | null;
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
    readonly declarationMetadataToken: number | null;
    readonly returnType: string | null;
    readonly parameters: ReadonlyArray<BrowserParameterSurface>;
    readonly documentationId: string | null;
    readonly summary: string | null;
    readonly returns: string | null;
    readonly exceptions: ReadonlyArray<BrowserExceptionSurface>;
    readonly stableSelector: string;
    readonly anchorDigest: string;
    readonly canonicalSignature: string;
    readonly anchorTypeFullName: string;
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
export interface BrowserRetainedNavigationAction {
    readonly session: string;
    readonly generation: string;
    readonly id: string;
    readonly source: string;
    readonly kind: string;
}
export interface BrowserRetainedNavigationAuthority {
    readonly session: string;
    readonly revision: string;
    readonly intent: string;
    readonly epoch: string;
}
export interface BrowserRetainedNavigationCoordinateOutcome {
    readonly disposition: string;
    readonly detail: string;
    readonly libraryPairing: string | null;
    readonly typeCorrespondence: string | null;
    readonly memberCorrespondence: string | null;
}
export interface BrowserRetainedNavigationDiagnostic {
    readonly kind: string;
    readonly library: string;
    readonly message: string;
}
export interface BrowserRetainedNavigationFacet {
    readonly id: string;
    readonly kind: string;
    readonly title: string;
    readonly summary: string;
    readonly order: number;
    readonly role: string | null;
}
export interface BrowserRetainedNavigationLens {
    readonly id: string;
    readonly subject: BrowserRetainedNavigationSubject;
    readonly facet: string;
}
export interface BrowserRetainedNavigationLensDescriptor {
    readonly facet: BrowserRetainedNavigationFacet;
    readonly state: string;
    readonly isCurrent: boolean;
    readonly target: BrowserRetainedNavigationLens | null;
    readonly unavailability: string | null;
    readonly message: string | null;
    readonly action: BrowserRetainedNavigationAction | null;
}
export interface BrowserRetainedNavigationLensOutcome {
    readonly kind: string;
    readonly basis: string;
    readonly subject: BrowserRetainedNavigationSubject;
    readonly effectiveLens: BrowserRetainedNavigationLens | null;
    readonly request: BrowserRetainedNavigationLens | null;
    readonly preferredRole: string | null;
    readonly policyFailure: string | null;
    readonly resolution: BrowserRetainedNavigationResolution | null;
    readonly suspension: BrowserRetainedNavigationRealization | null;
}
export interface BrowserRetainedNavigationLibraryDescriptor {
    readonly navigation: BrowserRetainedNavigationSubjectDescriptor;
    readonly assetId: string | null;
    readonly isAggregate: boolean;
    readonly isPrimary: boolean;
}
export interface BrowserRetainedNavigationMemberDescriptor {
    readonly navigation: BrowserRetainedNavigationSubjectDescriptor;
    readonly library: string;
    readonly containingType: string;
    readonly declaringType: string;
    readonly accessibility: string | null;
    readonly memberKind: string;
    readonly signature: string | null;
    readonly descendantLenses: ReadonlyArray<BrowserRetainedNavigationLensDescriptor>;
}
export interface BrowserRetainedNavigationOutcome {
    readonly kind: string;
    readonly rejection: string | null;
    readonly failureSource: string | null;
    readonly message: string | null;
    readonly request: BrowserRetainedNavigationRequest | null;
    readonly resolution: BrowserRetainedNavigationResolution | null;
    readonly scope: BrowserRetainedNavigationScopeOutcome | null;
    readonly diagnostics: ReadonlyArray<BrowserRetainedNavigationDiagnostic>;
    readonly coordinateRetention: BrowserRetainedNavigationCoordinateOutcome | null;
}
export interface BrowserRetainedNavigationPackageDescriptor {
    readonly order: number;
    readonly subject: BrowserRetainedNavigationSubject;
    readonly packageId: string;
    readonly version: string;
    readonly framework: string | null;
    readonly runtimeIdentifier: string | null;
    readonly realization: string;
    readonly realizationFailure: string | null;
    readonly state: string;
    readonly isCurrent: boolean;
    readonly action: BrowserRetainedNavigationAction | null;
}
export interface BrowserRetainedNavigationRealization {
    readonly kind: string;
    readonly failure: string | null;
}
export interface BrowserRetainedNavigationRequest {
    readonly source: BrowserRetainedNavigationSubject;
    readonly destination: BrowserRetainedNavigationSubject;
    readonly lens: BrowserRetainedNavigationLens | null;
}
export interface BrowserRetainedNavigationResolution {
    readonly kind: string;
    readonly descriptor: BrowserRetainedNavigationFacet | null;
    readonly unavailability: string | null;
    readonly message: string | null;
}
export interface BrowserRetainedNavigationResult {
    readonly operation: string;
    readonly request: string;
    readonly snapshot: BrowserRetainedNavigationSnapshot;
    readonly outcome: BrowserRetainedNavigationOutcome;
    readonly synchronization: string;
    readonly authority: BrowserRetainedNavigationAuthority | null;
}
export interface BrowserRetainedNavigationScopeOutcome {
    readonly kind: string;
    readonly operation: string;
    readonly rejection: string | null;
    readonly failure: string | null;
}
export interface BrowserRetainedNavigationScopeStatus {
    readonly kind: string;
    readonly runtimeFailure: string | null;
}
export interface BrowserRetainedNavigationSnapshot {
    readonly generation: string;
    readonly scope: BrowserRetainedNavigationScopeStatus;
    readonly workspace: BrowserRetainedNavigationSubject;
    readonly activePackage: string | null;
    readonly activeSubject: BrowserRetainedNavigationSubject;
    readonly typeInventoryLibraryContext: BrowserRetainedNavigationSubject | null;
    readonly packages: ReadonlyArray<BrowserRetainedNavigationPackageDescriptor>;
    readonly hierarchy: ReadonlyArray<BrowserRetainedNavigationSubjectDescriptor>;
    readonly libraries: ReadonlyArray<BrowserRetainedNavigationLibraryDescriptor>;
    readonly types: ReadonlyArray<BrowserRetainedNavigationTypeDescriptor>;
    readonly members: ReadonlyArray<BrowserRetainedNavigationMemberDescriptor>;
    readonly lenses: ReadonlyArray<BrowserRetainedNavigationLensDescriptor>;
    readonly lensOutcome: BrowserRetainedNavigationLensOutcome;
    readonly diagnostics: ReadonlyArray<BrowserRetainedNavigationDiagnostic>;
}
export interface BrowserRetainedNavigationSubject {
    readonly id: string;
    readonly kind: string;
    readonly label: string;
    readonly summary: string | null;
    readonly parent: string | null;
}
export interface BrowserRetainedNavigationSubjectDescriptor {
    readonly kind: string;
    readonly label: string;
    readonly subject: BrowserRetainedNavigationSubject | null;
    readonly state: string;
    readonly isActive: boolean;
    readonly isRetained: boolean;
    readonly action: BrowserRetainedNavigationAction | null;
}
export interface BrowserRetainedNavigationTypeDescriptor {
    readonly navigation: BrowserRetainedNavigationSubjectDescriptor;
    readonly library: string;
    readonly accessibility: string | null;
    readonly typeKind: string;
    readonly descendantLenses: ReadonlyArray<BrowserRetainedNavigationLensDescriptor>;
}
export interface BrowserRetainedWorkspaceActivationFailure {
    readonly kind: string;
    readonly message: string;
}
export interface BrowserRetainedWorkspaceActivationResult {
    readonly status: string;
    readonly installation: BrowserRetainedWorkspaceInstallation | null;
    readonly failure: BrowserRetainedWorkspaceActivationFailure | null;
}
export interface BrowserRetainedWorkspaceCleanup {
    readonly message: string;
}
export interface BrowserRetainedWorkspaceDeactivationResult {
    readonly status: string;
    readonly settlement: BrowserRetainedWorkspaceSettlement | null;
    readonly message: string | null;
}
export interface BrowserRetainedWorkspaceInstallation {
    readonly retainedDefinitionId: string;
    readonly label: string;
    readonly canonicalLocation: string;
    readonly canonicalPacket: string;
    readonly realizationId: string;
    readonly publicationOrdinal: number;
    readonly navigation: BrowserRetainedNavigationResult;
    readonly packages: ReadonlyArray<BrowserRetainedWorkspacePackage>;
    readonly predecessor: BrowserRetainedWorkspacePredecessor | null;
    readonly cleanup: BrowserRetainedWorkspaceCleanup | null;
}
export interface BrowserRetainedWorkspacePackage {
    readonly navigationId: string;
    readonly consumerPackageSubjectId: string;
    readonly surface: BrowserPackageSurface;
}
export interface BrowserRetainedWorkspacePredecessor {
    readonly settlementId: string;
    readonly reason: string;
}
export interface BrowserRetainedWorkspaceSettlement {
    readonly succeeded: boolean;
    readonly reason: string;
    readonly failure: string | null;
}
export interface BrowserRetainedWorkspaceSettlementResult {
    readonly status: string;
    readonly settlement: BrowserRetainedWorkspaceSettlement | null;
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
export interface JsExportRuntime {
    readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
    readonly runMain: (mainAssemblyName?: string, args?: string[]) => Promise<number>;
}
export declare function createRuntime(): Promise<JsExportRuntime>;
export declare function initializeRuntime(runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function abandonRetainedWorkspaceNavigation(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): string;
export declare function acknowledgeRetainedWorkspaceNavigation(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): string;
export declare function activateRetainedWorkspaceDefinition(retainedDefinitionId: string, label: string, canonicalLocation: string, canonicalPacket: string): Promise<BrowserRetainedWorkspaceActivationResult>;
export declare function canonicalizeWorkspaceSharePacket(encoded: string): BrowserWorkspaceShareEncodeResult;
export declare function deactivateRetainedWorkspaceDefinition(retainedDefinitionId: string): Promise<BrowserRetainedWorkspaceDeactivationResult>;
export declare function decodeWorkspaceShareState(encoded: string): BrowserWorkspaceShareDecodeResult;
export declare function encodeWorkspaceShareState(stateJson: BrowserWorkspaceShareState): BrowserWorkspaceShareEncodeResult;
export declare function listHomeDemos(): BrowserHomeDemoCatalog;
export declare function listVocabulary(): BrowserVocabularyDocument;
export declare function observeRetainedWorkspaceSettlement(settlementId: string): Promise<BrowserRetainedWorkspaceSettlementResult>;
export declare function recordRetainedWorkspaceNavigationInstallation(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): string;
export declare function resolveHomeDemo(scenarioId: string): BrowserHomeDemoResolveResult;
export declare function runHomeDemo(scenarioId: string): Promise<BrowserHomeDemoRunResult>;
export declare function validateRetainedWorkspaceNavigationAuthority(realizationId: string, publicationOrdinal: number, session: string, revision: string, intent: string, epoch: string): boolean;
