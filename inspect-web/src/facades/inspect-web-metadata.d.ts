declare const inertStringBrand: unique symbol;
export type InertString = string & {
    readonly [inertStringBrand]: "InertString";
};
export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;
export type BrowserLibraryApiDiffCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;
export type BrowserLibraryApiDiffEndpointIssueKind = "Truncated" | "Rejected" | "Failed" | "InspectionFailures" | "DegradedSignatures" | "UnexpectedAssemblyPopulation" | number;
export type BrowserLibraryApiDiffFailureKind = "Expected" | "Unexpected" | number;
export type BrowserLibraryApiDiffInspectionFailureMechanism = "Metadata" | "Relationship" | "Signature" | "TypeSpecification" | number;
export type BrowserLibraryApiDiffMemberPairKind = "Changed" | "Added" | "Removed" | number;
export type BrowserLibraryApiDiffMemberRelationRole = "Before" | "After" | "Both" | number;
export type BrowserLibraryApiDiffMetadataRootMalformedReason = "UnmappableMetadataDirectory" | "TruncatedFixedPrefix" | "InvalidSignature" | "InvalidVersionLength" | "TruncatedVersionField" | "MissingVersionTerminator" | number;
export type BrowserLibraryApiDiffOpenFailureKind = "Unreadable" | "InvalidImage" | "ResourceBudget" | "UnsupportedMetadataFormat" | number;
export type BrowserLibraryApiDiffProjectionLimit = "Participants" | "Types" | "Members" | "InspectionFailures" | "TypeForwarders" | "MetadataRows" | "RetainedTextCharacters" | number;
export type BrowserLibraryApiDiffRejectionKind = "LogicalLibraryMismatch" | "FindingComparisonFailed" | "CompatibilityInspectionFailed" | "MissingExactTypeIdentity" | "MissingMemberAnchor" | "DuplicateExactTypeIdentity" | "UnassociatedStructuredSubject" | "ContradictoryOccupiedSideTopology" | "ChangedTypeCountLimitExceeded" | "TypeTextLimitExceeded" | "CollectionEntryLimitExceeded" | "SerializedResultLimitExceeded" | number;
export type BrowserLibraryApiDiffResultKind = "Succeeded" | "Unavailable" | "Rejected" | "Failed" | "Canceled" | number;
export type BrowserLibraryApiDiffSurfaceScope = "Public" | "IncludeAll" | "PublicWithNonPublicTypes" | number;
export type BrowserLibraryApiDiffTypeState = "Diff" | "Addition" | "Deletion" | number;
export type BrowserLibraryApiDiffUnavailableKind = "TargetIncomplete" | "CurrentIncomplete" | "BothIncomplete" | number;
export type CandidateOpenFailureKind = number;
export type ExactTypeInspectionFailureKind = number;
export type ExactTypeInspectionOutcome = number;
export type InspectionDiagnosticSeverity = number;
export type JsonValueKind = number;
export type MetadataRootMalformedReason = number;
export type MetadataTypeNameFailureMechanism = number;
export type TypeDependencyRejectionKind = number;
export type TypeDependencyRelationshipKind = number;
export type TypeDependencyRowSet = number;
export interface AssemblyContextSubject {
    readonly identity: AssemblyReferenceIdentity;
    readonly provenance: AssemblyResolutionProvenance;
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
export interface BrowserLibraryApiDiffMember {
    readonly documentIdentifier: string;
    readonly pairKind: BrowserLibraryApiDiffMemberPairKind;
    readonly role: BrowserLibraryApiDiffMemberRelationRole;
    readonly before: BrowserLibraryApiDiffMemberIdentity | null;
    readonly after: BrowserLibraryApiDiffMemberIdentity | null;
}
export interface BrowserLibraryApiDiffMemberIdentity {
    readonly declaringTypeIdentifier: string;
    readonly stableSelector: string;
    readonly canonicalSignature: string;
    readonly fingerprint: string;
    readonly typeFullName: string;
    readonly memberName: string;
    readonly display: string;
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
    readonly inspection: InspectionEnvelope<unknown> | null;
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
    readonly members: ReadonlyArray<BrowserLibraryApiDiffMember>;
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
    readonly exactTypeInspection: InspectionEnvelope<ExactTypeInspectionResult>;
    readonly derivedTypes: ReadonlyArray<string>;
    readonly graphNodes: ReadonlyArray<BrowserTypeGraphNode>;
    readonly graphEdges: ReadonlyArray<BrowserTypeGraphEdge>;
    readonly typeDependencyInspection: InspectionEnvelope<TypeDependencySectionResult>;
    readonly inspectionFailures: ReadonlyArray<string>;
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
export interface CandidateOpenFailure {
    readonly kind: CandidateOpenFailureKind;
    readonly detail: string;
    readonly metadataRootReason: MetadataRootMalformedReason | null;
}
export interface ExactTypeApi {
    readonly fullName: string;
    readonly namespace: string | null;
    readonly name: string;
    readonly definitionIdentity: ExactTypeDefinitionIdentity;
    readonly introducedTypeParameterCounts: ReadonlyArray<number>;
    readonly kind: string;
    readonly accessibility: string | null;
    readonly attributes: ReadonlyArray<string>;
    readonly isSealed: boolean;
    readonly isAbstract: boolean;
    readonly isStatic: boolean;
    readonly isByRefLike: boolean;
    readonly isReadOnly: boolean;
    readonly baseType: string | null;
    readonly interfaces: ReadonlyArray<string>;
    readonly derivedTypes: ReadonlyArray<string>;
    readonly typeParameters: ReadonlyArray<ExactTypeParameter>;
    readonly members: ReadonlyArray<ExactTypeMember>;
    readonly enumUnderlyingType: string | null;
    readonly isForwarded: boolean;
}
export interface ExactTypeApiInspectionFailure {
    readonly operation: string;
    readonly subjectToken: number;
    readonly mechanism: MetadataTypeNameFailureMechanism;
    readonly kind: string;
    readonly detail: string;
    readonly subjectAssembly: AssemblyReferenceIdentity | null;
    readonly dependencyAssembly: AssemblyReferenceIdentity | null;
}
export interface ExactTypeAssemblyIdentity {
    readonly identity: AssemblyReferenceIdentity;
    readonly moduleVersionId: string;
}
export interface ExactTypeDefinitionIdentity {
    readonly namespace: string;
    readonly segments: ReadonlyArray<string>;
}
export interface ExactTypeForwardingHop {
    readonly source: AssemblyReferenceIdentity;
    readonly target: AssemblyReferenceIdentity;
}
export interface ExactTypeInspectionFailure {
    readonly kind: ExactTypeInspectionFailureKind;
    readonly detail: string;
    readonly assembly: AssemblyReferenceIdentity | null;
}
export interface ExactTypeInspectionResult {
    readonly outcome: ExactTypeInspectionOutcome;
    readonly requestedType: string;
    readonly matchedType: string | null;
    readonly type: ExactTypeApi | null;
    readonly requestedAssembly: ExactTypeAssemblyIdentity | null;
    readonly supplierAssembly: ExactTypeAssemblyIdentity | null;
    readonly forwardingHops: ReadonlyArray<ExactTypeForwardingHop>;
    readonly suggestions: ReadonlyArray<string>;
    readonly inspectionFailures: ReadonlyArray<ExactTypeApiInspectionFailure>;
    readonly failures: ReadonlyArray<ExactTypeInspectionFailure>;
    readonly isAvailable: boolean;
    readonly isComplete: boolean;
}
export interface ExactTypeMember {
    readonly name: string;
    readonly kind: string;
    readonly signature: string | null;
}
export interface ExactTypeParameter {
    readonly name: string;
    readonly variance: string | null;
    readonly constraints: ReadonlyArray<string>;
}
export interface InspectionDiagnostic {
    readonly code: string;
    readonly severity: InspectionDiagnosticSeverity;
    readonly summary: InertString;
    readonly correspondence: InertString | null;
}
export interface InspectionEnvelope<T0> {
    readonly content: T0;
    readonly share: InspectionShare;
    readonly diagnostics: ReadonlyArray<InspectionDiagnostic>;
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
export interface Completed {
    readonly kind: "completed";
    readonly subject: AssemblyContextSubject;
}
export interface Rejected {
    readonly kind: "rejected";
    readonly subject: AssemblyContextSubject;
    readonly failure: CandidateOpenFailure;
}
export type AssemblyContextTypeDependencyEntry = Completed | Rejected;
export interface DesignatedAsset {
    readonly kind: "designated";
    readonly resolverSource: string;
}
export interface EmbeddedAsset {
    readonly kind: "embedded";
    readonly contentRef: string;
    readonly digest: string;
    readonly declaredName: string;
}
export interface LocalAsset {
    readonly kind: "local";
    readonly resolverSource: string;
}
export interface PackageAsset {
    readonly kind: "package";
    readonly packageId: string;
    readonly packageVersion: string;
    readonly tfm: string | null;
    readonly rid: string | null;
    readonly assetPath: string | null;
}
export interface PlatformAsset {
    readonly kind: "platform";
    readonly framework: string;
    readonly frameworkVersion: string | null;
    readonly resolverSource: string;
}
export interface ProjectAsset {
    readonly kind: "project";
    readonly project: string;
    readonly tfm: string | null;
    readonly rid: string | null;
}
export type AssemblyResolutionProvenance = PackageAsset | PlatformAsset | ProjectAsset | LocalAsset | EmbeddedAsset | DesignatedAsset;
export interface Available {
    readonly kind: "available";
    readonly fullUrl: string;
    readonly packet: string;
}
export interface NonProjectable {
    readonly kind: "nonProjectable";
    readonly fullUrl: string | null;
    readonly packet: string | null;
    readonly path: string;
    readonly reason: InertString;
}
export type InspectionShare = Available | NonProjectable;
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
export declare function queryTypeProjection(packageId: string, version: string, targetFramework: string, assemblyName: string, typeQueryId: string, typeDefinitionId: string, workspaceJson: string): Promise<BrowserTypeMetadata>;
export {};
