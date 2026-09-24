import { dotnet } from "./runtime-loader.js";

declare const inertStringBrand: unique symbol;

export type InertString = string & {
  readonly [inertStringBrand]: "InertString";
};

declare const jsonTextBrand: unique symbol;

export type JsonText<T> = string & {
  readonly [jsonTextBrand]: T;
};

export type BrowserAllocationExceptionPathKind = "ThrownValue" | "ExceptionHandler" | number;

export type BrowserAnnotatedSourceCallCycleLimit = "TraversalBoundary" | "IncompleteCorrespondence" | "WitnessBudget" | "PathBudget" | "AnalysisFailure" | number;

export type BrowserAnnotatedSourceCallKind = "Call" | "CallVirtual" | "NewObject" | "LoadFunction" | "LoadVirtualFunction" | "CallIndirect" | number;

export type BrowserAnnotatedSourceCapabilityUnavailableReason = "NotProjected" | "ContextUnavailable" | number;

export type BrowserAnnotatedSourceLocalThrowPathBoundaryKind = "AnalysisIncomplete" | "TraversalBoundary" | "PartialMethodEvidenceScope" | "UnresolvedLocalCalls" | "UnattributedGeneratedBodies" | "DepthLimit" | "NodeBudget" | "EdgeBudget" | "PathBudget" | "IncompleteLocalThrowEvidence" | "IncompleteCorrespondence" | number;

export type BrowserAnnotatedSourceMedium = "CSharp" | "Il" | number;

export type BrowserCalleeEvidenceKind = "ExceptionConstruction" | "Localloc" | "Calli" | number;

export type BrowserCalleeEvidenceState = "Instruction" | "Method" | "InstructionUnavailable" | number;

export type BrowserCostCalleeEvidenceInputKind = "AllocationInLoop" | "Reflection" | "CallInLoop" | "RootReach" | "DirectCallers" | "LoopCalls" | number;

export type BrowserMemberSourcePartKind = "Member" | "XmlDocumentation" | "Attributes" | "Signature" | "Body" | number;

export type BrowserMethodBodyResultKind = "Succeeded" | "Failed" | "Canceled" | number;

export type BrowserSourceComparisonResultKind = "Succeeded" | "TooComplex" | "Failed" | "Canceled" | number;

export type BrowserSourceDiffAnnotationTargetKind = "Change" | "Line" | "Span" | number;

export type BrowserSourceDiffCapacityDimension = "RequestBytes" | "RawBeforeBytes" | "RawAfterBytes" | "RawBeforeLines" | "RawAfterLines" | "Relations" | "CoordinateOccurrences" | "MappedChanges" | "InnerMappings" | "Annotations" | "AnnotationTextBytes" | "AuxiliaryTextBytes" | "EncodedResultBytes" | number;

export type BrowserSourceDiffContentKind = "Unchanged" | "Changed" | number;

export type BrowserSourceDiffLineTerminator = "Unknown" | "Present" | "Absent" | number;

export type BrowserSourceDiffPlacementKind = "Stable" | "Moved" | number;

export type BrowserSourceDiffRelationKind = "Addition" | "Removal" | "Correspondence" | number;

export type BrowserSourceDiffSeverity = "Note" | "Tip" | "Important" | "Warning" | "Caution" | number;

export type BrowserSourceDiffSide = "Before" | "After" | "Both" | number;

export type BrowserSynchronousCompletionKind = "TaskWait" | "TaskResult" | "TaskAwaiterGetResult" | number;

export type BrowserTypeExplorerAccessibility = "Unknown" | "Private" | "PrivateProtected" | "Protected" | "Internal" | "ProtectedInternal" | "Public" | number;

export type BrowserTypeExplorerBodyMode = "Bodies" | "Skeleton" | "SelectedBody" | number;

export type BrowserTypeExplorerOutcomeKind = "Available" | "Incomplete" | "Unavailable" | "Rejected" | number;

export type BrowserTypeExplorerPlacement = "All" | "Instance" | "Static" | number;

export type BrowserTypeSourceCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;

export type BrowserTypeSourceFailureKind = "Expected" | "Unexpected" | number;

export type BrowserTypeSourceResultKind = "Succeeded" | "Failed" | "Canceled" | number;

export type InspectionDiagnosticSeverity = number;

export type JsonValueKind = number;

export type TypeApiDeclarationFailureKind = "ProjectionTruncated" | "ParticipantRejected" | "ParticipantFailed" | "InspectionIncomplete" | "AccessorMetadataUnavailable" | "PrinterNotRendered" | number;

export type TypeApiDeclarationOutcome = "Available" | "NotFound" | "Unavailable" | number;

export type TypeApiDeclarationScope = "ApiVisible" | "All" | number;

export interface BrowserAnnotatedSource {
  readonly document: unknown;
  readonly viewerCatalog: BrowserAnnotatedSourceViewerCatalog;
  readonly provenance: InertString;
  readonly contextLimitation: string | null;
  readonly findingEvidenceDocuments: ReadonlyArray<BrowserAnnotatedSourceFindingEvidenceDocument>;
  readonly findingEvidence: ReadonlyArray<BrowserAnnotatedSourceFindingEvidence>;
  readonly callRelationships: ReadonlyArray<BrowserAnnotatedSourceCallRelationship>;
}

export interface BrowserAnnotatedSourceAllocationExceptionPath {
  readonly factId: number;
  readonly kind: BrowserAllocationExceptionPathKind;
}

export interface BrowserAnnotatedSourceAllocationExceptionPathInspection {
  readonly available: boolean;
  readonly unavailableReason: BrowserAnnotatedSourceCapabilityUnavailableReason | null;
  readonly observations: ReadonlyArray<BrowserAnnotatedSourceAllocationExceptionPath>;
}

export interface BrowserAnnotatedSourceAwaitCompletionPath {
  readonly nodeId: number;
}

export interface BrowserAnnotatedSourceAwaitCompletionPathInspection {
  readonly available: boolean;
  readonly unavailableReason: BrowserAnnotatedSourceCapabilityUnavailableReason | null;
  readonly observations: ReadonlyArray<BrowserAnnotatedSourceAwaitCompletionPath>;
}

export interface BrowserAnnotatedSourceCallCycle {
  readonly findingKey: string;
  readonly ordinal: number;
  readonly edgeRows: ReadonlyArray<number>;
  readonly factIds: ReadonlyArray<number>;
  readonly targets: ReadonlyArray<BrowserCallGraphTarget>;
}

export interface BrowserAnnotatedSourceCallCycleInspection {
  readonly available: boolean;
  readonly unavailableReason: BrowserAnnotatedSourceCapabilityUnavailableReason | null;
  readonly isComplete: boolean;
  readonly limits: ReadonlyArray<BrowserAnnotatedSourceCallCycleLimit>;
  readonly findings: ReadonlyArray<BrowserAnnotatedSourceCallCycle>;
}

export interface BrowserAnnotatedSourceCallRelationship {
  readonly edgeRow: number;
  readonly factId: number;
  readonly moduleVersionId: string;
  readonly callerToken: number;
  readonly ilOffset: number;
  readonly operandToken: number;
  readonly kind: BrowserAnnotatedSourceCallKind;
  readonly inLoop: boolean;
  readonly target: BrowserCallGraphTarget;
}

export interface BrowserAnnotatedSourceCapabilityAvailability {
  readonly available: boolean;
  readonly unavailableReason: BrowserAnnotatedSourceCapabilityUnavailableReason | null;
}

export interface BrowserAnnotatedSourceFindingEvidence {
  readonly factId: number;
  readonly instanceKey: number;
  readonly member: string;
  readonly target: BrowserCallGraphTarget;
  readonly state: BrowserCalleeEvidenceState;
  readonly aggregateInputs: ReadonlyArray<BrowserCostCalleeEvidenceInput>;
  readonly coordinates: ReadonlyArray<BrowserAnnotatedSourceFindingEvidenceCoordinate>;
  readonly documentId: number | null;
  readonly nodeIds: ReadonlyArray<number>;
  readonly unavailableReason: string | null;
}

export interface BrowserAnnotatedSourceFindingEvidenceCoordinate {
  readonly ilOffset: number;
  readonly kind: BrowserCalleeEvidenceKind;
}

export interface BrowserAnnotatedSourceFindingEvidenceDocument {
  readonly id: number;
  readonly document: unknown;
}

export interface BrowserAnnotatedSourceInvocationDestination {
  readonly nodeId: number;
  readonly target: BrowserCallGraphTarget;
}

export interface BrowserAnnotatedSourceLocalThrowPath {
  readonly factIds: ReadonlyArray<number>;
  readonly targets: ReadonlyArray<BrowserCallGraphTarget>;
  readonly terminalThrows: ReadonlyArray<BrowserAnnotatedSourceLocalThrowSite>;
}

export interface BrowserAnnotatedSourceLocalThrowPathBoundary {
  readonly kind: BrowserAnnotatedSourceLocalThrowPathBoundaryKind;
  readonly value: number;
}

export interface BrowserAnnotatedSourceLocalThrowPathInspection {
  readonly available: boolean;
  readonly unavailableReason: BrowserAnnotatedSourceCapabilityUnavailableReason | null;
  readonly isComplete: boolean;
  readonly boundaries: ReadonlyArray<BrowserAnnotatedSourceLocalThrowPathBoundary>;
  readonly limits: BrowserAnnotatedSourceLocalThrowPathLimits | null;
  readonly receipt: BrowserAnnotatedSourceLocalThrowPathReceipt | null;
  readonly paths: ReadonlyArray<BrowserAnnotatedSourceLocalThrowPath>;
}

export interface BrowserAnnotatedSourceLocalThrowPathLimits {
  readonly maximumDepth: number;
  readonly maximumNodes: number;
  readonly maximumEdges: number;
  readonly maximumPaths: number;
}

export interface BrowserAnnotatedSourceLocalThrowPathReceipt {
  readonly destinationSearches: number;
  readonly searchNodes: number;
  readonly searchedEdges: number;
  readonly observedReachablePairs: number;
  readonly returnedPaths: number;
}

export interface BrowserAnnotatedSourceLocalThrowSite {
  readonly exceptionType: string;
  readonly definitionModuleVersionId: string;
  readonly definitionToken: number;
  readonly constructionOffset: number;
  readonly constructorToken: number;
  readonly throwOffset: number;
}

export interface BrowserAnnotatedSourceSynchronousCompletion {
  readonly factId: number;
  readonly kind: BrowserSynchronousCompletionKind;
}

export interface BrowserAnnotatedSourceSynchronousCompletionInspection {
  readonly available: boolean;
  readonly unavailableReason: BrowserAnnotatedSourceCapabilityUnavailableReason | null;
  readonly observations: ReadonlyArray<BrowserAnnotatedSourceSynchronousCompletion>;
}

export interface BrowserAnnotatedSourceViewerCatalog {
  readonly defaultFindingIds: ReadonlyArray<number>;
  readonly supportedMedia: ReadonlyArray<BrowserAnnotatedSourceMedium>;
  readonly invocationLikeNodeKinds: ReadonlyArray<string>;
  readonly invocationDestinations: ReadonlyArray<BrowserAnnotatedSourceInvocationDestination>;
  readonly findingEvidence: BrowserAnnotatedSourceCapabilityAvailability;
  readonly destinations: BrowserAnnotatedSourceCapabilityAvailability;
  readonly callRelationships: BrowserAnnotatedSourceCapabilityAvailability;
  readonly callCycles: BrowserAnnotatedSourceCallCycleInspection;
  readonly synchronousCompletions: BrowserAnnotatedSourceSynchronousCompletionInspection;
  readonly awaitCompletionPaths: BrowserAnnotatedSourceAwaitCompletionPathInspection;
  readonly allocationExceptionPaths: BrowserAnnotatedSourceAllocationExceptionPathInspection;
  readonly localThrowPaths: BrowserAnnotatedSourceLocalThrowPathInspection;
}

export interface BrowserCSharpBodyEvidence {
  readonly isExact: boolean;
  readonly rows: ReadonlyArray<BrowserCSharpBodyRow>;
}

export interface BrowserCSharpBodyOperation {
  readonly kind: string;
  readonly value: string;
}

export interface BrowserCSharpBodyRow {
  readonly assemblyIdentity: string;
  readonly stableMemberKey: string;
  readonly member: string;
  readonly changeId: string;
  readonly message: string;
  readonly hunkId: number;
  readonly kind: string;
  readonly line: number | null;
  readonly sourceCoordinate: string | null;
  readonly fidelity: string;
  readonly text: string;
  readonly oldValue: string | null;
  readonly newValue: string | null;
  readonly oldOperation: BrowserCSharpBodyOperation | null;
  readonly newOperation: BrowserCSharpBodyOperation | null;
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

export interface BrowserCostCalleeEvidenceInput {
  readonly kind: BrowserCostCalleeEvidenceInputKind;
  readonly value: number | null;
}

export interface BrowserIlBodyEvidence {
  readonly outcome: string;
  readonly isExact: boolean;
  readonly isAvailable: boolean;
  readonly failure: string | null;
  readonly rows: ReadonlyArray<BrowserIlBodyRow>;
}

export interface BrowserIlBodyOperand {
  readonly kind: string;
  readonly value: string;
}

export interface BrowserIlBodyOperation {
  readonly offset: number;
  readonly opcodeFamily: string;
  readonly operand: BrowserIlBodyOperand | null;
}

export interface BrowserIlBodyRow {
  readonly hunkId: number;
  readonly kind: string;
  readonly operation: BrowserIlBodyOperation;
  readonly message: string;
}

export interface BrowserMemberFindingCensus {
  readonly factCensusReceipt: string;
  readonly facts: ReadonlyArray<BrowserMemberFindingFact>;
  readonly annotatedSource: BrowserAnnotatedSource;
  readonly sourceFactInstances: ReadonlyArray<BrowserSourceFactInstance>;
}

export interface BrowserMemberFindingFact {
  readonly member: string;
  readonly ilOffset: number | null;
  readonly cSharpLine: number | null;
  readonly anchor: string;
  readonly category: string;
  readonly id: string;
  readonly detail: string | null;
  readonly conditionality: string;
  readonly instanceKey: number | null;
}

export interface BrowserMemberSource {
  readonly source: BrowserSource;
  readonly parts: ReadonlyArray<BrowserMemberSourcePart>;
}

export interface BrowserMemberSourcePart {
  readonly kind: BrowserMemberSourcePartKind;
  readonly spans: ReadonlyArray<BrowserMemberSourceSpan>;
}

export interface BrowserMemberSourceSpan {
  readonly start: number;
  readonly length: number;
  readonly startLine: number;
  readonly endLine: number;
  readonly leadingIndentation: string;
  readonly end: number;
}

export interface BrowserMethodBodyComparison {
  readonly request: BrowserMethodBodyComparisonRequest;
  readonly stage: string;
  readonly outcome: string;
  readonly producers: ReadonlyArray<BrowserMethodBodyProducer>;
  readonly diagnostics: ReadonlyArray<BrowserMethodBodyDiagnostic>;
}

export interface BrowserMethodBodyComparisonRequest {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly moduleVersionId: string;
  readonly before: BrowserMethodBodySelection;
  readonly after: BrowserMethodBodySelection;
}

export interface BrowserMethodBodyComparisonResult {
  readonly version: number;
  readonly kind: BrowserMethodBodyResultKind;
  readonly value: BrowserMethodBodyComparison | null;
  readonly failureKind: BrowserTypeSourceFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
}

export interface BrowserMethodBodyDiagnostic {
  readonly kind: string;
  readonly side: string | null;
  readonly message: string;
  readonly detail: string | null;
  readonly hunkId: number | null;
  readonly subjectToken: number | null;
  readonly mechanism: string | null;
  readonly path: string | null;
}

export interface BrowserMethodBodyEndpoint {
  readonly state: string;
  readonly moduleVersionId: string | null;
  readonly metadataToken: number | null;
  readonly targetState: string | null;
  readonly detail: string | null;
}

export interface BrowserMethodBodyProducer {
  readonly producer: string;
  readonly outcome: string;
  readonly nativeVerdict: string;
  readonly before: BrowserMethodBodyEndpoint;
  readonly after: BrowserMethodBodyEndpoint;
  readonly cSharp: BrowserCSharpBodyEvidence | null;
  readonly il: BrowserIlBodyEvidence | null;
  readonly diagnostics: ReadonlyArray<BrowserMethodBodyDiagnostic>;
}

export interface BrowserMethodBodySelection {
  readonly typeIdentity: string;
  readonly memberName: string;
  readonly selectorKey: string;
  readonly metadataToken: number;
  readonly label: string;
}

export interface BrowserMethodBodyTargets {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly moduleVersionId: string;
  readonly before: BrowserMethodBodySelection;
  readonly methods: ReadonlyArray<BrowserMethodBodySelection>;
}

export interface BrowserMethodBodyTargetsResult {
  readonly version: number;
  readonly kind: BrowserMethodBodyResultKind;
  readonly value: BrowserMethodBodyTargets | null;
  readonly failureKind: BrowserTypeSourceFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
}

export interface BrowserSource {
  readonly provider: string;
  readonly provenance: InertString;
  readonly url: string | null;
  readonly pdbSourceLimitation: string | null;
  readonly text: string;
}

export interface BrowserSourceComparison {
  readonly request: BrowserSourceComparisonRequest;
  readonly status: string;
  readonly isExact: boolean;
  readonly before: BrowserSourceComparisonEndpoint;
  readonly after: BrowserSourceComparisonEndpoint;
  readonly diff: BrowserSourceDiff | null;
  readonly failure: string | null;
}

export interface BrowserSourceComparisonEndpoint {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly assetPath: string;
  readonly moduleVersionId: string | null;
  readonly assemblyIdentity: string;
  readonly memberIdentity: string | null;
  readonly metadataToken: number | null;
  readonly state: string;
  readonly detail: string | null;
  readonly text: string | null;
  readonly browseUrl: string | null;
  readonly repositoryUrl: string | null;
  readonly revision: string | null;
}

export interface BrowserSourceComparisonRequest {
  readonly packageId: string;
  readonly beforeVersion: string;
  readonly afterVersion: string;
  readonly framework: string;
  readonly assembly: string;
  readonly typeIdentity: string;
  readonly memberName: string;
  readonly selectorKey: string;
  readonly metadataToken: number;
}

export interface BrowserSourceComparisonResult {
  readonly version: number;
  readonly kind: BrowserSourceComparisonResultKind;
  readonly value: BrowserSourceComparison | null;
  readonly failureKind: BrowserTypeSourceFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
  readonly capacity: BrowserSourceDiffCapacity | null;
}

export interface BrowserSourceDiff {
  readonly version: number;
  readonly before: BrowserSourceDiffSequence;
  readonly after: BrowserSourceDiffSequence;
  readonly relations: ReadonlyArray<BrowserSourceDiffRelation>;
  readonly statistics: BrowserSourceDiffStatistics;
  readonly changes: ReadonlyArray<BrowserSourceDiffChange>;
}

export interface BrowserSourceDiffAnnotation {
  readonly text: string;
  readonly severity: BrowserSourceDiffSeverity;
  readonly targetKind: BrowserSourceDiffAnnotationTargetKind;
  readonly side: BrowserSourceDiffSide | null;
  readonly line: number | null;
  readonly span: BrowserSourceDiffSpan | null;
}

export interface BrowserSourceDiffCapacity {
  readonly dimension: BrowserSourceDiffCapacityDimension;
  readonly limit: number;
  readonly actual: number;
}

export interface BrowserSourceDiffChange {
  readonly before: BrowserSourceDiffRange;
  readonly after: BrowserSourceDiffRange;
  readonly innerMappings: ReadonlyArray<BrowserSourceDiffInnerMapping>;
  readonly annotations: ReadonlyArray<BrowserSourceDiffAnnotation>;
}

export interface BrowserSourceDiffInnerMapping {
  readonly before: BrowserSourceDiffSpan;
  readonly after: BrowserSourceDiffSpan;
}

export interface BrowserSourceDiffRange {
  readonly start: number;
  readonly count: number;
}

export interface BrowserSourceDiffRelation {
  readonly kind: BrowserSourceDiffRelationKind;
  readonly beforeCoordinates: ReadonlyArray<number>;
  readonly afterCoordinates: ReadonlyArray<number>;
  readonly content: BrowserSourceDiffContentKind | null;
  readonly placement: BrowserSourceDiffPlacementKind | null;
}

export interface BrowserSourceDiffSequence {
  readonly label: string | null;
  readonly lines: ReadonlyArray<string>;
  readonly finalLineTerminator: BrowserSourceDiffLineTerminator;
}

export interface BrowserSourceDiffSpan {
  readonly line: number;
  readonly start: number;
  readonly count: number;
}

export interface BrowserSourceDiffStatistics {
  readonly added: number;
  readonly removed: number;
  readonly changedBefore: number;
  readonly changedAfter: number;
  readonly movedBefore: number;
  readonly movedAfter: number;
}

export interface BrowserSourceFactInstance {
  readonly factId: number;
  readonly instanceKey: number;
}

export interface BrowserTypeExplorerBody {
  readonly bodyId: number;
  readonly role: string;
  readonly range: BrowserTypeExplorerRange;
  readonly destination: BrowserTypeExplorerBodyDestination | null;
}

export interface BrowserTypeExplorerBodyDestination {
  readonly moduleVersionId: string;
  readonly member: BrowserTypeExplorerMemberIdentity;
  readonly metadataToken: number;
}

export interface BrowserTypeExplorerContribution {
  readonly bodyId: number;
  readonly role: string;
  readonly range: BrowserTypeExplorerRange;
}

export interface BrowserTypeExplorerDeclaration {
  readonly declarationId: number;
  readonly identity: BrowserTypeExplorerMemberIdentity;
  readonly declarationToken: number;
  readonly kind: string;
  readonly accessibility: string;
  readonly placement: string;
  readonly origin: string;
  readonly supportsSelectedBody: boolean;
  readonly range: BrowserTypeExplorerRange;
  readonly regions: ReadonlyArray<BrowserTypeExplorerRegion>;
  readonly bodies: ReadonlyArray<BrowserTypeExplorerBody>;
  readonly contributions: ReadonlyArray<BrowserTypeExplorerContribution>;
}

export interface BrowserTypeExplorerDocument {
  readonly typeNamespace: string;
  readonly typeSegments: ReadonlyArray<string>;
  readonly assemblyName: string;
  readonly pdbSupplied: boolean;
  readonly symbolSource: string;
  readonly renderingPolicy: string;
  readonly documentationCapability: string;
  readonly contractRelationshipCapability: string;
  readonly projection: BrowserTypeExplorerProjection | null;
  readonly projectionFailure: BrowserTypeExplorerProjectionFailure | null;
}

export interface BrowserTypeExplorerInspection {
  readonly outcome: BrowserTypeExplorerOutcomeKind;
  readonly reason: string | null;
  readonly bodyProjectionsAttempted: number;
  readonly failedBodyIds: ReadonlyArray<number>;
  readonly document: BrowserTypeExplorerDocument | null;
  readonly share: InspectionShare;
  readonly diagnostics: ReadonlyArray<InspectionDiagnostic>;
}

export interface BrowserTypeExplorerMemberIdentity {
  readonly stableSelector: string;
  readonly canonicalSignature: string;
  readonly fingerprint: string;
  readonly typeFullName: string;
  readonly memberName: string;
}

export interface BrowserTypeExplorerProjection {
  readonly revision: string;
  readonly text: string;
  readonly frameRegions: ReadonlyArray<BrowserTypeExplorerRegion>;
  readonly frameContributions: ReadonlyArray<BrowserTypeExplorerContribution>;
  readonly declarations: ReadonlyArray<BrowserTypeExplorerDeclaration>;
  readonly diagnostics: ReadonlyArray<BrowserTypeExplorerProjectionDiagnostic>;
}

export interface BrowserTypeExplorerProjectionDiagnostic {
  readonly kind: string;
  readonly message: string;
  readonly declarationId: number | null;
  readonly bodyId: number | null;
  readonly contributionRole: string | null;
}

export interface BrowserTypeExplorerProjectionFailure {
  readonly kind: string;
  readonly message: string;
}

export interface BrowserTypeExplorerRange {
  readonly start: number;
  readonly length: number;
}

export interface BrowserTypeExplorerRegion {
  readonly role: string;
  readonly range: BrowserTypeExplorerRange;
}

export interface BrowserTypeExplorerRequest {
  readonly bodyMode: BrowserTypeExplorerBodyMode;
  readonly selectedDeclarationId: number | null;
  readonly documentRevision: string | null;
  readonly placement: BrowserTypeExplorerPlacement;
  readonly accessibilities: ReadonlyArray<BrowserTypeExplorerAccessibility>;
  readonly includeGenerated: boolean;
  readonly includeDocumentation: boolean;
  readonly includeAttributes: boolean;
}

export interface BrowserTypeExplorerResult {
  readonly version: number;
  readonly kind: BrowserTypeSourceResultKind;
  readonly value: BrowserTypeExplorerInspection | null;
  readonly failureKind: BrowserTypeSourceFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
}

export interface BrowserTypeSourceCancellation {
  readonly kind: BrowserTypeSourceCancellationKind;
  readonly reason: string | null;
}

export interface BrowserTypeSourceResult {
  readonly version: number;
  readonly kind: BrowserTypeSourceResultKind;
  readonly value: BrowserTypeCodeView | null;
  readonly failureKind: BrowserTypeSourceFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
}

export interface ExactTypeDefinitionIdentity {
  readonly namespace: string;
  readonly segments: ReadonlyArray<string>;
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

export interface TypeApiDeclarationFailure {
  readonly kind: TypeApiDeclarationFailureKind;
  readonly detail: string;
  readonly operation: string | null;
  readonly subjectToken: number | null;
}

export interface TypeApiDeclarationResult {
  readonly outcome: TypeApiDeclarationOutcome;
  readonly typeIdentity: ExactTypeDefinitionIdentity;
  readonly scope: TypeApiDeclarationScope;
  readonly text: string | null;
  readonly failures: ReadonlyArray<TypeApiDeclarationFailure>;
}

export interface ApiDeclarations {
  readonly kind: "apiDeclarations";
  readonly inspection: InspectionEnvelope<TypeApiDeclarationResult>;
}

export interface Source {
  readonly kind: "source";
  readonly value: BrowserSource;
  readonly share: InspectionShare;
  readonly diagnostics: ReadonlyArray<InspectionDiagnostic>;
}

export type BrowserTypeCodeView = Source | ApiDeclarations;

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
        readonly "Source": {
          readonly "SourceExports": {
            readonly "CancelMemberSourceComparison.271973316": (operationId: string, reason: string) => string;
            readonly "CancelMethodBodyComparison.271973316": (operationId: string, reason: string) => string;
            readonly "CancelSourceQuery.19325221": () => void;
            readonly "CancelTypeExplorerQuery.271973316": (operationId: string, reason: string) => string;
            readonly "CancelTypeSourceQuery.271973316": (operationId: string, reason: string) => string;
            readonly "QueryMemberAnnotatedSource.1135530322": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
            readonly "QueryMemberFindingCensus.1135530322": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
            readonly "QueryMemberSource.641907440": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
            readonly "QueryMemberSourceComparison.451505237": (operationId: string, requestJson: string) => Promise<string>;
            readonly "QueryMethodBodyComparison.451505237": (operationId: string, requestJson: string) => Promise<string>;
            readonly "QueryMethodBodyComparisonTargets.642387634": (operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number) => Promise<string>;
            readonly "QueryTypeExplorer.335255791": (operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string, requestJson: string) => Promise<string>;
            readonly "QueryTypeMemberSource.641907440": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string) => Promise<string>;
            readonly "QueryTypeSource.335255791": (operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string, view: string) => Promise<string>;
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
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "CancelMemberSourceComparison.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelMemberSourceComparison.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "CancelMethodBodyComparison.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelMethodBodyComparison.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "CancelSourceQuery.19325221");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelSourceQuery.19325221\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "CancelTypeExplorerQuery.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelTypeExplorerQuery.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "CancelTypeSourceQuery.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelTypeSourceQuery.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMemberAnnotatedSource.1135530322");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberAnnotatedSource.1135530322\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMemberFindingCensus.1135530322");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberFindingCensus.1135530322\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMemberSource.641907440");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSource.641907440\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMemberSourceComparison.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSourceComparison.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMethodBodyComparison.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMethodBodyComparison.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryMethodBodyComparisonTargets.642387634");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMethodBodyComparisonTargets.642387634\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryTypeExplorer.335255791");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryTypeExplorer.335255791\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryTypeMemberSource.641907440");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryTypeMemberSource.641907440\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Source");
    value = $ownDataProperty(value, "SourceExports");
    value = $ownDataProperty(value, "QueryTypeSource.335255791");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryTypeSource.335255791\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Source");
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

export function cancelMemberSourceComparison(operationId: string, reason: string): BrowserTypeSourceCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelMemberSourceComparison.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeSourceCancellation;
}

export function cancelMethodBodyComparison(operationId: string, reason: string): BrowserTypeSourceCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelMethodBodyComparison.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeSourceCancellation;
}

export function cancelSourceQuery(): void {
  return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelSourceQuery.19325221"]();
}

export function cancelTypeExplorerQuery(operationId: string, reason: string): BrowserTypeSourceCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelTypeExplorerQuery.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeSourceCancellation;
}

export function cancelTypeSourceQuery(operationId: string, reason: string): BrowserTypeSourceCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelTypeSourceQuery.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeSourceCancellation;
}

export async function queryMemberAnnotatedSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserAnnotatedSource> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberAnnotatedSource.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserAnnotatedSource;
}

export async function queryMemberFindingCensus(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserMemberFindingCensus> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberFindingCensus.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberFindingCensus;
}

export async function queryMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserMemberSource> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberSource;
}

export async function queryMemberSourceComparison(operationId: string, requestJson: BrowserSourceComparisonRequest): Promise<JsonText<BrowserSourceComparisonResult>> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberSourceComparison.451505237"](operationId, $serializeJsonInput(requestJson, "DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSourceComparison.451505237", "requestJson"));
  return $result as JsonText<BrowserSourceComparisonResult>;
}

export async function queryMethodBodyComparison(operationId: string, requestJson: BrowserMethodBodyComparisonRequest): Promise<BrowserMethodBodyComparisonResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMethodBodyComparison.451505237"](operationId, $serializeJsonInput(requestJson, "DotnetInspect.Web.Interop.Source.SourceExports.QueryMethodBodyComparison.451505237", "requestJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMethodBodyComparisonResult;
}

export async function queryMethodBodyComparisonTargets(operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserMethodBodyTargetsResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMethodBodyComparisonTargets.642387634"](operationId, packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMethodBodyTargetsResult;
}

export async function queryTypeExplorer(operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string, requestJson: BrowserTypeExplorerRequest): Promise<BrowserTypeExplorerResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryTypeExplorer.335255791"](operationId, packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson, $serializeJsonInput(requestJson, "DotnetInspect.Web.Interop.Source.SourceExports.QueryTypeExplorer.335255791", "requestJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeExplorerResult;
}

export async function queryTypeMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryTypeMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserSource;
}

export async function queryTypeSource(operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string, view: string): Promise<BrowserTypeSourceResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryTypeSource.335255791"](operationId, packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson, view);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserTypeSourceResult;
}

