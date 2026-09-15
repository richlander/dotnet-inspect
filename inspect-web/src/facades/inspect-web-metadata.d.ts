export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;
export type BrowserExactTypeDiagnosticSeverity = "Information" | "Warning" | "Error" | number;
export type BrowserExactTypeFailureKind = "InvalidRequest" | "DefinitionMismatch" | "ContextUnavailable" | "ContextLoadFailed" | "PopulationUnavailable" | "DeclarationInventoryIncomplete" | "TypeResolutionRejected" | "TypeResolutionUnavailable" | "TypeResolutionAmbiguous" | "ApiSurfaceRejected" | "ApiSurfaceFailed" | "ApiSurfaceIncomplete" | "AsyncClassificationUnavailable" | "ResolvedTypeMissing" | number;
export type BrowserExactTypeInspectionFailureMechanism = "Metadata" | "Relationship" | "Signature" | "TypeSpecification" | number;
export type BrowserExactTypeLibrarySourceKind = "Package" | "Platform" | "Project" | "Local" | number;
export type BrowserExactTypeOutcome = "Available" | "NotFound" | "Ambiguous" | "Incomplete" | "Rejected" | number;
export type BrowserExactTypeRealizedSourceKind = "Package" | "Platform" | number;
export type BrowserExactTypeResolutionScope = "Any" | "Platform" | number;
export type BrowserExactTypeShareKind = "Available" | "NonProjectable" | number;
export type BrowserExactTypeSurfaceScope = "Public" | "IncludeAll" | "PublicWithNonPublicTypes" | number;
export type BrowserLibraryApiDiffCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;
export type BrowserLibraryApiDiffEndpointIssueKind = "Truncated" | "Rejected" | "Failed" | "InspectionFailures" | "DegradedSignatures" | "UnexpectedAssemblyPopulation" | number;
export type BrowserLibraryApiDiffFailureKind = "Expected" | "Unexpected" | number;
export type BrowserLibraryApiDiffInspectionFailureMechanism = "Metadata" | "Relationship" | "Signature" | "TypeSpecification" | number;
export type BrowserLibraryApiDiffMetadataRootMalformedReason = "UnmappableMetadataDirectory" | "TruncatedFixedPrefix" | "InvalidSignature" | "InvalidVersionLength" | "TruncatedVersionField" | "MissingVersionTerminator" | number;
export type BrowserLibraryApiDiffOpenFailureKind = "Unreadable" | "InvalidImage" | "ResourceBudget" | "UnsupportedMetadataFormat" | number;
export type BrowserLibraryApiDiffProjectionLimit = "Participants" | "Types" | "Members" | "InspectionFailures" | "TypeForwarders" | "MetadataRows" | "RetainedTextCharacters" | number;
export type BrowserLibraryApiDiffRejectionKind = "LogicalLibraryMismatch" | "FindingComparisonFailed" | "CompatibilityInspectionFailed" | "MissingExactTypeIdentity" | "MissingMemberAnchor" | "DuplicateExactTypeIdentity" | "UnassociatedStructuredSubject" | "ContradictoryOccupiedSideTopology" | "ChangedTypeCountLimitExceeded" | "TypeTextLimitExceeded" | "CollectionEntryLimitExceeded" | "SerializedResultLimitExceeded" | number;
export type BrowserLibraryApiDiffResultKind = "Succeeded" | "Unavailable" | "Rejected" | "Failed" | "Canceled" | number;
export type BrowserLibraryApiDiffSurfaceScope = "Public" | "IncludeAll" | "PublicWithNonPublicTypes" | number;
export type BrowserLibraryApiDiffTypeState = "Diff" | "Addition" | "Deletion" | number;
export type BrowserLibraryApiDiffUnavailableKind = "TargetIncomplete" | "CurrentIncomplete" | "BothIncomplete" | number;
export type InspectionDiagnosticSeverity = number;
export type MetadataRootMalformedReason = number;
export type TypeDependencyRejectionKind = number;
export type TypeDependencyRelationshipKind = number;
export type TypeDependencyRowSet = number;
export interface ArtifactAcquisitionRegistration {
    readonly generation: ArtifactGenerationIdentity;
    readonly artifact: ArtifactIdentity;
    readonly provenance: IArtifactProvenance;
}
export interface ArtifactGenerationIdentity {
}
export interface ArtifactIdentity {
    readonly generation: ArtifactGenerationIdentity;
    readonly ordinal: number;
}
export interface AssemblyAcquisitionRegistration {
    readonly artifactRegistration: ArtifactAcquisitionRegistration | null;
    readonly moduleVersionId: string | null;
}
export interface AssemblyContextSubject {
    readonly registration: AssemblyAcquisitionRegistration;
    readonly identity: AssemblyReferenceIdentity;
    readonly provenance: AssemblyResolutionProvenance;
}
export interface AssemblyContextTypeDependencyEntry {
    readonly subject: AssemblyContextSubject;
}
export interface AssemblyContextTypeDependencyResult {
    readonly dependency: TypeDependencyResult;
    readonly participants: ReadonlyArray<AssemblyContextTypeDependencyEntry>;
    readonly hasSurvivingParticipant: boolean;
    readonly isComplete: boolean;
}
export interface AssemblyReferenceIdentity {
    readonly name: string;
    readonly version: string | null;
    readonly culture: string | null;
    readonly publicKeyToken: string | null;
}
export interface AssemblyResolutionProvenance {
}
export interface BrowserAssemblyMetadata {
    readonly assembly: string;
    readonly metadataRoots: ReadonlyArray<BrowserMetadataImage>;
    readonly cliMetadataError: string | null;
    readonly manifestMetadataError: string | null;
    readonly readyToRun: BrowserReadyToRunImage | null;
    readonly readyToRunError: string | null;
}
export interface BrowserCompileLibraryAvailability {
    readonly status: BrowserCompileLibraryStatus;
    readonly targetFramework: string | null;
    readonly message: string | null;
}
export interface BrowserExactTypeAssemblyIdentity {
    readonly name: string;
    readonly version: string | null;
    readonly culture: string | null;
    readonly publicKeyToken: string | null;
}
export interface BrowserExactTypeAvailable {
    readonly candidate: BrowserExactTypeCandidate;
    readonly isContextUnique: boolean;
    readonly type: BrowserExactTypeSelectedType;
    readonly memberFacts: ReadonlyArray<BrowserExactTypeMemberFacts>;
    readonly memberKindFacets: ReadonlyArray<BrowserExactTypeFacet>;
    readonly inspectionFailures: ReadonlyArray<BrowserExactTypeInspectionFailure>;
}
export interface BrowserExactTypeCandidate {
    readonly definition: BrowserExactTypeName;
    readonly address: BrowserExactTypeDefinitionAddress;
    readonly declaration: BrowserExactTypeLibrarySource;
    readonly supplier: BrowserExactTypeLibrarySource;
    readonly supplierSource: BrowserExactTypeRealizedSource;
    readonly supplierAssembly: BrowserExactTypeAssemblyIdentity;
    readonly forwardingHops: ReadonlyArray<BrowserExactTypeForwardingHop>;
    readonly declarationAssetId: string;
    readonly supplierAssetId: string;
}
export interface BrowserExactTypeDefinitionAddress {
    readonly moduleVersionId: string;
    readonly metadataToken: number;
}
export interface BrowserExactTypeDiagnostic {
    readonly code: string;
    readonly severity: BrowserExactTypeDiagnosticSeverity;
    readonly summary: string;
    readonly correspondence: string | null;
}
export interface BrowserExactTypeFacet {
    readonly id: string;
    readonly singularLabel: string;
    readonly pluralLabel: string;
    readonly weight: number;
    readonly count: number;
    readonly isDefault: boolean;
}
export interface BrowserExactTypeFailure {
    readonly kind: BrowserExactTypeFailureKind;
    readonly assembly: BrowserExactTypeAssemblyIdentity | null;
    readonly contextLoadFailure: string | null;
    readonly candidateOpenFailure: string | null;
    readonly populationFailure: string | null;
    readonly surfaceLimit: string | null;
}
export interface BrowserExactTypeForwardingHop {
    readonly sourceAssembly: BrowserExactTypeAssemblyIdentity;
    readonly targetAssembly: BrowserExactTypeAssemblyIdentity;
    readonly scope: BrowserExactTypeResolutionScope;
}
export interface BrowserExactTypeInspectionContent {
    readonly kind: BrowserExactTypeOutcome;
    readonly isComplete: boolean;
    readonly request: BrowserExactTypeRequest;
    readonly available: BrowserExactTypeAvailable | null;
    readonly suggestions: ReadonlyArray<BrowserExactTypeName>;
    readonly candidates: ReadonlyArray<BrowserExactTypeCandidate>;
    readonly failures: ReadonlyArray<BrowserExactTypeFailure>;
}
export interface BrowserExactTypeInspectionEnvelope {
    readonly content: BrowserExactTypeInspectionContent;
    readonly share: BrowserExactTypeShare;
    readonly diagnostics: ReadonlyArray<BrowserExactTypeDiagnostic>;
}
export interface BrowserExactTypeInspectionFailure {
    readonly operation: string;
    readonly subjectToken: number;
    readonly mechanism: BrowserExactTypeInspectionFailureMechanism;
    readonly kind: string;
    readonly detail: string;
    readonly subjectAssembly: BrowserExactTypeAssemblyIdentity | null;
    readonly dependencyAssembly: BrowserExactTypeAssemblyIdentity | null;
    readonly owningTypeToken: number | null;
    readonly owningTypeDefinition: BrowserExactTypeName | null;
    readonly affectedTypeDefinitions: ReadonlyArray<BrowserExactTypeName>;
}
export interface BrowserExactTypeLibrarySource {
    readonly kind: BrowserExactTypeLibrarySourceKind;
    readonly library: BrowserExactTypeAssemblyIdentity;
    readonly packageId: string | null;
    readonly version: string | null;
    readonly platformFamily: string | null;
}
export interface BrowserExactTypeMemberFacts {
    readonly metadataToken: number | null;
    readonly declarationMetadataToken: number | null;
    readonly isAsync: boolean;
    readonly hasMethodBody: boolean | null;
    readonly attributes: ReadonlyArray<string>;
}
export interface BrowserExactTypeName {
    readonly namespace: string;
    readonly segments: ReadonlyArray<string>;
}
export interface BrowserExactTypeRealizedSource {
    readonly kind: BrowserExactTypeRealizedSourceKind;
    readonly packageId: string | null;
    readonly version: string | null;
    readonly producer: string | null;
    readonly framework: string | null;
    readonly runtimeIdentifier: string | null;
    readonly platformFamily: string | null;
    readonly assembly: string | null;
}
export interface BrowserExactTypeRequest {
    readonly contextIndex: number;
    readonly typeSelector: string;
    readonly scope: BrowserExactTypeSurfaceScope;
    readonly surfaceLimits: BrowserExactTypeSurfaceLimits;
    readonly assemblyName: string | null;
    readonly library: BrowserExactTypeAssemblyIdentity | null;
    readonly compileAssetId: string | null;
}
export interface BrowserExactTypeSelectedType {
    readonly surface: BrowserTypeSurface;
    readonly baseType: string | null;
    readonly interfaces: ReadonlyArray<string>;
    readonly derivedTypes: ReadonlyArray<string>;
    readonly typeParameters: ReadonlyArray<BrowserTypeParameter>;
    readonly attributes: ReadonlyArray<string>;
    readonly enumUnderlyingType: string | null;
    readonly composition: BrowserTypeComposition | null;
    readonly isForwarded: boolean;
}
export interface BrowserExactTypeShare {
    readonly kind: BrowserExactTypeShareKind;
    readonly fullUrl: string | null;
    readonly packet: string | null;
    readonly path: string | null;
    readonly reason: string | null;
}
export interface BrowserExactTypeSurfaceLimits {
    readonly maxParticipants: number;
    readonly maxTypes: number;
    readonly maxMembers: number;
    readonly maxInspectionFailures: number;
    readonly maxTypeForwarders: number;
    readonly maxMetadataRows: number;
    readonly maxRetainedTextCharacters: number;
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
export interface BrowserLibraryApiDiffAggregate {
    readonly changedTypeCount: number;
    readonly addedTypeCount: number;
    readonly removedTypeCount: number;
    readonly changedMemberCount: number;
    readonly breakingCount: number;
    readonly additiveCount: number;
    readonly potentiallyBreakingCount: number;
}
export interface BrowserLibraryApiDiffAssemblyIdentity {
    readonly name: string;
    readonly version: string | null;
    readonly culture: string | null;
    readonly publicKeyToken: string | null;
}
export interface BrowserLibraryApiDiffCancellation {
    readonly kind: BrowserLibraryApiDiffCancellationKind;
    readonly reason: string | null;
}
export interface BrowserLibraryApiDiffCompileAsset {
    readonly id: string;
    readonly path: string;
    readonly assemblyName: string;
}
export interface BrowserLibraryApiDiffEndpoint {
    readonly packageId: string;
    readonly version: string;
    readonly framework: string;
    readonly asset: BrowserLibraryApiDiffCompileAsset;
    readonly assembly: BrowserLibraryApiDiffAssemblyIdentity;
    readonly scope: BrowserLibraryApiDiffSurfaceScope;
    readonly isComplete: boolean;
    readonly issues: ReadonlyArray<BrowserLibraryApiDiffEndpointIssue>;
}
export interface BrowserLibraryApiDiffEndpointIssue {
    readonly kind: BrowserLibraryApiDiffEndpointIssueKind;
    readonly truncation: BrowserLibraryApiDiffProjectionTruncation | null;
    readonly openFailureKind: BrowserLibraryApiDiffOpenFailureKind | null;
    readonly detail: string | null;
    readonly metadataRootReason: BrowserLibraryApiDiffMetadataRootMalformedReason | null;
    readonly count: number | null;
    readonly inspectionFailures: ReadonlyArray<BrowserLibraryApiDiffInspectionFailure> | null;
}
export interface BrowserLibraryApiDiffInspectionFailure {
    readonly operation: string;
    readonly subjectToken: number;
    readonly mechanism: BrowserLibraryApiDiffInspectionFailureMechanism;
    readonly kind: string;
    readonly detail: string;
    readonly subjectAssembly: BrowserLibraryApiDiffAssemblyIdentity | null;
    readonly dependencyAssembly: BrowserLibraryApiDiffAssemblyIdentity | null;
}
export interface BrowserLibraryApiDiffProjectionTruncation {
    readonly limit: BrowserLibraryApiDiffProjectionLimit;
    readonly bound: number;
    readonly projectedParticipants: number;
    readonly omittedParticipants: number;
    readonly projectedTypes: number;
    readonly projectedMembers: number;
    readonly projectedInspectionFailures: number;
    readonly projectedTypeForwarders: number;
    readonly inspectedMetadataRows: number;
    readonly projectedRetainedTextCharacters: number;
}
export interface BrowserLibraryApiDiffRejected {
    readonly kind: BrowserLibraryApiDiffRejectionKind;
    readonly target: BrowserLibraryApiDiffEndpoint | null;
    readonly current: BrowserLibraryApiDiffEndpoint | null;
    readonly bound: number | null;
    readonly observed: number | null;
}
export interface BrowserLibraryApiDiffRequest {
    readonly schemaVersion: number;
    readonly packageId: string;
    readonly currentVersion: string;
    readonly targetVersion: string;
    readonly targetFramework: string;
    readonly compileAssetId: string;
}
export interface BrowserLibraryApiDiffResult {
    readonly schemaVersion: number;
    readonly request: BrowserLibraryApiDiffRequest | null;
    readonly kind: BrowserLibraryApiDiffResultKind;
    readonly value: BrowserLibraryApiDiffSucceeded | null;
    readonly unavailable: BrowserLibraryApiDiffUnavailable | null;
    readonly rejected: BrowserLibraryApiDiffRejected | null;
    readonly failureKind: BrowserLibraryApiDiffFailureKind | null;
    readonly error: string | null;
    readonly diagnostic: string | null;
    readonly reason: string | null;
}
export interface BrowserLibraryApiDiffSucceeded {
    readonly libraryIdentifier: string;
    readonly libraryDisplay: string;
    readonly target: BrowserLibraryApiDiffEndpoint;
    readonly current: BrowserLibraryApiDiffEndpoint;
    readonly aggregate: BrowserLibraryApiDiffAggregate;
    readonly types: ReadonlyArray<BrowserLibraryApiDiffType>;
}
export interface BrowserLibraryApiDiffType {
    readonly documentIdentifier: string;
    readonly display: string;
    readonly state: BrowserLibraryApiDiffTypeState;
    readonly typeDefinitionChanged: boolean | null;
    readonly changedMemberCount: number;
    readonly breakingCount: number;
    readonly additiveCount: number;
    readonly potentiallyBreakingCount: number;
    readonly before: BrowserLibraryApiDiffTypeIdentity | null;
    readonly after: BrowserLibraryApiDiffTypeIdentity | null;
}
export interface BrowserLibraryApiDiffTypeIdentity {
    readonly identifier: string;
    readonly namespace: string;
    readonly segments: ReadonlyArray<string>;
    readonly display: string;
}
export interface BrowserLibraryApiDiffUnavailable {
    readonly kind: BrowserLibraryApiDiffUnavailableKind;
    readonly target: BrowserLibraryApiDiffEndpoint;
    readonly current: BrowserLibraryApiDiffEndpoint;
}
export interface BrowserMemberBodySelector {
    readonly token: number;
    readonly memberName: string;
    readonly selectorKey: string;
}
export interface BrowserMemberDeclaration {
    readonly text: string | null;
    readonly unavailable: string | null;
    readonly compatibility: boolean;
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
export interface BrowserMetadataImage {
    readonly requestedRoot: string;
    readonly canonicalRoot: string | null;
    readonly rootRelativeVirtualAddress: number | null;
    readonly rootSize: number | null;
    readonly aliasesCliMetadata: boolean;
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
export interface BrowserReadyToRunImage {
    readonly role: string;
    readonly advertisements: string;
    readonly majorVersion: number;
    readonly minorVersion: number;
    readonly flagsValue: number;
    readonly flags: string;
    readonly headerRelativeVirtualAddress: number;
    readonly headerSize: number;
    readonly managedNativeHeaderRelativeVirtualAddress: number | null;
    readonly managedNativeHeaderSize: number | null;
    readonly exportHeaderRelativeVirtualAddress: number | null;
    readonly manifestMetadata: BrowserReadyToRunManifest | null;
    readonly sections: ReadonlyArray<BrowserReadyToRunSection>;
}
export interface BrowserReadyToRunManifest {
    readonly relativeVirtualAddress: number;
    readonly size: number;
    readonly aliasesCliMetadata: boolean;
}
export interface BrowserReadyToRunSection {
    readonly type: string;
    readonly typeValue: number;
    readonly relativeVirtualAddress: number;
    readonly size: number;
    readonly aliasesCliMetadata: boolean;
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
    readonly exactTypeInspection: BrowserExactTypeInspectionEnvelope;
    readonly typeDependencyInspection: InspectionEnvelope<TypeDependencySectionResult>;
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
export interface IArtifactProvenance {
}
export interface InspectionDiagnostic {
    readonly code: string;
    readonly severity: InspectionDiagnosticSeverity;
    readonly summary: string;
    readonly correspondence: string | null;
}
export interface InspectionEnvelope<T0> {
    readonly content: T0;
    readonly share: InspectionShare;
    readonly diagnostics: ReadonlyArray<InspectionDiagnostic>;
}
export interface InspectionShare {
    readonly fullUrl: string | null;
    readonly packet: string | null;
}
export interface RowWindowFailure {
    readonly stageNumber: number;
    readonly requiredPosition: number;
    readonly availableCount: number;
}
export interface RowsCohortSemanticFailure<T0> {
    readonly identity: T0;
    readonly failure: RowWindowFailure;
}
export interface TypeDependencyDepthBoundary {
    readonly typeName: string;
    readonly maximumDepth: number;
}
export interface TypeDependencyNode {
    readonly typeName: string;
    readonly children: ReadonlyArray<TypeDependencyNode>;
}
export interface TypeDependencyRejection {
    readonly assemblyPath: string;
    readonly kind: TypeDependencyRejectionKind;
    readonly metadataRootReason: MetadataRootMalformedReason | null;
}
export interface TypeDependencyRelationship {
    readonly sourceTypeName: string;
    readonly targetTypeName: string;
    readonly kind: TypeDependencyRelationshipKind;
    readonly ordinal: number;
}
export interface TypeDependencyResult {
    readonly matchedType: string | null;
    readonly tree: ReadonlyArray<TypeDependencyNode>;
    readonly found: boolean;
    readonly relationships: ReadonlyArray<TypeDependencyRelationship>;
    readonly depthBoundaries: ReadonlyArray<TypeDependencyDepthBoundary>;
    readonly rejections: ReadonlyArray<TypeDependencyRejection>;
}
export interface TypeDependencyRowSelectionResult {
    readonly isSuccess: boolean;
    readonly relationships: ReadonlyArray<TypeDependencyRelationship>;
    readonly failure: RowsCohortSemanticFailure<TypeDependencyRowSet> | null;
}
export interface TypeDependencySectionResult {
    readonly queryResult: AssemblyContextTypeDependencyResult;
    readonly rowSelection: TypeDependencyRowSelectionResult;
}
export interface JsExportRuntime {
    readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
    readonly runMain: (mainAssemblyName?: string, args?: string[]) => Promise<number>;
}
export declare function createRuntime(): Promise<JsExportRuntime>;
export declare function initializeRuntime(runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function cancelLibraryApiDiff(operationId: string, reason: string): BrowserLibraryApiDiffCancellation;
export declare function queryGraphMemberSurface(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserGraphMemberSurface>;
export declare function queryLibraryApiDiff(operationId: string, requestJson: string): Promise<BrowserLibraryApiDiffResult>;
export declare function queryMemberDeclaration(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, implementationMember: boolean): Promise<BrowserMemberDeclaration>;
export declare function queryPackageHeapEntries(packageId: string, version: string, targetFramework: string, assemblyFileName: string, metadataRoot: string, heap: string): Promise<BrowserHeapListing>;
export declare function queryPackageMetadata(packageId: string, version: string, targetFramework: string, assemblyFileName: string): Promise<BrowserPackageMetadata>;
export declare function queryPackageMetadataTable(packageId: string, version: string, targetFramework: string, assemblyFileName: string, metadataRoot: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow>;
export declare function queryPlatformHeapEntries(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, metadataRoot: string, heap: string): Promise<BrowserHeapListing>;
export declare function queryPlatformMemberDeclaration(targetFramework: string, platformVersion: string, assemblyName: string, pack: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserMemberDeclaration>;
export declare function queryPlatformMetadata(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageMetadata>;
export declare function queryPlatformMetadataTable(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, metadataRoot: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow>;
export declare function queryTypeProjection(packageId: string, version: string, targetFramework: string, assemblyName: string, typeId: string, workspaceJson: string): Promise<BrowserTypeMetadata>;
