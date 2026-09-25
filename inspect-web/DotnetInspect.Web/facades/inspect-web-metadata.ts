import { dotnet } from "./runtime-loader.js";

declare const inertStringBrand: unique symbol;

export type InertString = string & {
  readonly [inertStringBrand]: "InertString";
};

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export type BrowserLibraryApiDiffCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;

export type BrowserLibraryApiDiffChangeCategory = "Signature" | "Attribute" | number;

export type BrowserLibraryApiDiffChangeClassification = "Additive" | "Breaking" | "PotentiallyBreaking" | number;

export type BrowserLibraryApiDiffChangeKind = "TypeAdded" | "TypeRemoved" | "TypeKindChanged" | "SealedAdded" | "SealedRemoved" | "AbstractAdded" | "AbstractRemoved" | "BaseTypeChanged" | "InterfaceAdded" | "InterfaceRemoved" | "TypeParameterCountChanged" | "TypeParameterVarianceChanged" | "TypeParameterConstraintTightened" | "TypeParameterConstraintLoosened" | "MemberAdded" | "MemberRemoved" | "MemberSignatureChanged" | "VirtualRemoved" | "AbstractMemberAdded" | "EnumValueChanged" | "TypeAttributeAdded" | "TypeAttributeRemoved" | "MemberAttributeAdded" | "MemberAttributeRemoved" | number;

export type BrowserLibraryApiDiffEndpointIssueKind = "Truncated" | "Rejected" | "Failed" | "InspectionFailures" | "DegradedSignatures" | "UnexpectedAssemblyPopulation" | number;

export type BrowserLibraryApiDiffExploreDestinationKind = "member-diff" | number;

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

export interface BrowserLibraryApiDiffChange {
  readonly kind: BrowserLibraryApiDiffChangeKind;
  readonly classification: BrowserLibraryApiDiffChangeClassification;
  readonly category: BrowserLibraryApiDiffChangeCategory;
  readonly message: string;
  readonly oldValue: string | null;
  readonly newValue: string | null;
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

export interface BrowserLibraryApiDiffMatch {
  readonly tier: string;
  readonly confidence: number;
}

export interface BrowserLibraryApiDiffMember {
  readonly documentIdentifier: string;
  readonly pairKind: BrowserLibraryApiDiffMemberPairKind;
  readonly role: BrowserLibraryApiDiffMemberRelationRole;
  readonly before: BrowserLibraryApiDiffMemberIdentity | null;
  readonly after: BrowserLibraryApiDiffMemberIdentity | null;
  readonly changes: ReadonlyArray<BrowserLibraryApiDiffChange>;
  readonly match: BrowserLibraryApiDiffMatch | null;
  readonly explore: BrowserLibraryApiDiffMemberExploreDestination | null;
}

export interface BrowserLibraryApiDiffMemberExploreDestination {
  readonly kind: BrowserLibraryApiDiffExploreDestinationKind;
  readonly target: BrowserLibraryApiDiffMemberExploreEndpoint;
  readonly current: BrowserLibraryApiDiffMemberExploreEndpoint;
}

export interface BrowserLibraryApiDiffMemberExploreEndpoint {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly asset: BrowserLibraryApiDiffCompileAsset;
  readonly assembly: BrowserLibraryApiDiffAssemblyIdentity;
  readonly member: BrowserLibraryApiDiffMemberIdentity | null;
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
  readonly changes: ReadonlyArray<BrowserLibraryApiDiffChange>;
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

type $ManagedExports = {
  readonly "DotnetInspect": {
    readonly "Web": {
      readonly "Interop": {
        readonly "Metadata": {
          readonly "MetadataExports": {
            readonly "CancelLibraryApiDiff.271973316": (operationId: string, reason: string) => string;
            readonly "QueryGraphMemberSurface.1542089313": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number) => Promise<string>;
            readonly "QueryLibraryApiDiff.451505237": (operationId: string, requestJson: string) => Promise<string>;
            readonly "QueryMemberDeclaration.340032695": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, implementationMember: boolean) => Promise<string>;
            readonly "QueryPackageHeapEntries.649160465": (packageId: string, version: string, targetFramework: string, assemblyFileName: string, metadataRoot: string, heap: string) => Promise<string>;
            readonly "QueryPackageMetadata.1579276339": (packageId: string, version: string, targetFramework: string, assemblyFileName: string) => Promise<string>;
            readonly "QueryPackageMetadataTable.1945598111": (packageId: string, version: string, targetFramework: string, assemblyFileName: string, metadataRoot: string, tableIndex: number, startRowId: number, maxRows: number) => Promise<string>;
            readonly "QueryPlatformHeapEntries.649160465": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, metadataRoot: string, heap: string) => Promise<string>;
            readonly "QueryPlatformMemberDeclaration.1542089313": (targetFramework: string, platformVersion: string, assemblyName: string, pack: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number) => Promise<string>;
            readonly "QueryPlatformMetadata.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
            readonly "QueryPlatformMetadataTable.1945598111": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, metadataRoot: string, tableIndex: number, startRowId: number, maxRows: number) => Promise<string>;
            readonly "QueryTypeProjection.1160082336": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeQueryId: string, typeDefinitionId: string, workspaceJson: string) => Promise<string>;
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
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "CancelLibraryApiDiff.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.CancelLibraryApiDiff.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryGraphMemberSurface.1542089313");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryGraphMemberSurface.1542089313\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryLibraryApiDiff.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryLibraryApiDiff.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryMemberDeclaration.340032695");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryMemberDeclaration.340032695\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPackageHeapEntries.649160465");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageHeapEntries.649160465\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPackageMetadata.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadata.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPackageMetadataTable.1945598111");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadataTable.1945598111\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPlatformHeapEntries.649160465");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformHeapEntries.649160465\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPlatformMemberDeclaration.1542089313");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformMemberDeclaration.1542089313\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPlatformMetadata.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformMetadata.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryPlatformMetadataTable.1945598111");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformMetadataTable.1945598111\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Metadata");
    value = $ownDataProperty(value, "MetadataExports");
    value = $ownDataProperty(value, "QueryTypeProjection.1160082336");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryTypeProjection.1160082336\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Metadata");
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

function $serializeJsonInput(
  value: unknown,
  operation: string,
  parameter: string,
): string {
  const json = JSON.stringify(value);
  if (json === undefined) {
    throw new TypeError(
      `${operation} parameter '${parameter}' could not be serialized as JSON.`,
    );
  }
  return json;
}

export function cancelLibraryApiDiff(operationId: string, reason: string): BrowserLibraryApiDiffCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["CancelLibraryApiDiff.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserLibraryApiDiffCancellation;
}

export async function queryGraphMemberSurface(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserGraphMemberSurface> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryGraphMemberSurface.1542089313"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserGraphMemberSurface;
}

export async function queryLibraryApiDiff(operationId: string, requestJson: BrowserLibraryApiDiffRequest): Promise<BrowserLibraryApiDiffResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryLibraryApiDiff.451505237"](operationId, $serializeJsonInput(requestJson, "DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryLibraryApiDiff.451505237", "requestJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserLibraryApiDiffResult;
}

export async function queryMemberDeclaration(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, implementationMember: boolean): Promise<BrowserMemberDeclaration> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryMemberDeclaration.340032695"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, implementationMember);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberDeclaration;
}

export async function queryPackageHeapEntries(packageId: string, version: string, targetFramework: string, assemblyFileName: string, metadataRoot: string, heap: string): Promise<BrowserHeapListing> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPackageHeapEntries.649160465"](packageId, version, targetFramework, assemblyFileName, metadataRoot, heap);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHeapListing;
}

export async function queryPackageMetadata(packageId: string, version: string, targetFramework: string, assemblyFileName: string): Promise<BrowserPackageMetadata> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPackageMetadata.1579276339"](packageId, version, targetFramework, assemblyFileName);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageMetadata;
}

export async function queryPackageMetadataTable(packageId: string, version: string, targetFramework: string, assemblyFileName: string, metadataRoot: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPackageMetadataTable.1945598111"](packageId, version, targetFramework, assemblyFileName, metadataRoot, tableIndex, startRowId, maxRows);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMetadataWindow;
}

export async function queryPlatformHeapEntries(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, metadataRoot: string, heap: string): Promise<BrowserHeapListing> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPlatformHeapEntries.649160465"](targetFramework, platformVersion, assemblyFileName, pack, metadataRoot, heap);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserHeapListing;
}

export async function queryPlatformMemberDeclaration(targetFramework: string, platformVersion: string, assemblyName: string, pack: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserMemberDeclaration> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPlatformMemberDeclaration.1542089313"](targetFramework, platformVersion, assemblyName, pack, typeIdentity, memberName, selectorKey, metadataToken);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberDeclaration;
}

export async function queryPlatformMetadata(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageMetadata> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPlatformMetadata.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageMetadata;
}

export async function queryPlatformMetadataTable(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, metadataRoot: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPlatformMetadataTable.1945598111"](targetFramework, platformVersion, assemblyFileName, pack, metadataRoot, tableIndex, startRowId, maxRows);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMetadataWindow;
}

export async function queryTypeProjection(packageId: string, version: string, targetFramework: string, assemblyName: string, typeQueryId: string, typeDefinitionId: string, workspaceJson: string): Promise<BrowserTypeMetadata> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryTypeProjection.1160082336"](packageId, version, targetFramework, assemblyName, typeQueryId, typeDefinitionId, workspaceJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeMetadata;
}

