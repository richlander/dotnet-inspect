import { dotnet } from "./runtime-loader.js";

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export type BrowserDependencyCoordinateMatchOutcome = "NoMatch" | "Unique" | "Ambiguous" | number;

export type BrowserDependencyCoordinateProvenance = "NuGetPackage" | "PlatformRuntime" | number;

export type BrowserExactLibraryApiAssetKind = number;

export type BrowserExactLibraryApiInspectionFailureKind = number;

export type BrowserExactLibraryApiInspectionOutcome = number;

export type BrowserExactLibraryApiProjectionLimit = number;

export type BrowserInspectionShareKind = "Available" | "NonProjectable" | number;

export type BrowserPackageAssemblyAssessmentKind = "NoMatch" | "NotApplicable" | number;

export type BrowserPackageAssemblyNotApplicableReason = "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "NoImplementationCounterpart" | number;

export type BrowserPackageAssemblySemanticCandidateOutcomeKind = "Matched" | "NoMatch" | "NotApplicable" | "Failure" | "NotEvaluated" | number;

export type BrowserPackageAssemblySemanticFailureKind = "Acquisition" | "Evaluation" | number;

export type BrowserPackageAssemblySemanticNonEvaluationKind = "OperationDeadline" | number;

export type BrowserPackageAssemblySemanticPopulationCompletionKind = "ExactPackageComplete" | "PrefixExhausted" | "CandidateLimitReached" | "SourcePageLimitReached" | "ClientPageLimitReached" | "SourceFailed" | number;

export type BrowserPackageChangesCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;

export type BrowserPackageChangesOperationFailureKind = "Expected" | "Unexpected" | number;

export type BrowserPackageChangesResultKind = "Succeeded" | "Failed" | "Canceled" | number;

export type BrowserPackageDependencyDeclarationFailureKind = "ConflictingPackageDeclaration" | "InvalidPackageDeclaration" | "RestoredProject" | "AuthoredProject" | "AuthoredProjectUnresolvedSyntax" | number;

export type BrowserPackageGraphIdentityRole = "Inspected" | "SamePrefix" | "External" | number;

export type BrowserPackagePruningCompletion = "Complete" | "Partial" | "Failed" | "NotApplicable" | number;

export type BrowserPackagePruningDisposition = "PlatformDelegation" | "PackageRetained" | "CandidateUnavailable" | "NotEvaluated" | number;

export type BrowserPackageQueryAcquisitionTier = "Nuspec" | "PackageContent" | "SearchMetadata" | "Assembly" | number;

export type BrowserPackageQueryCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;

export type BrowserPackageQueryCompletionKind = "Exhausted" | "MatchLimitReached" | "CandidateLimitReached" | "SourcePageLimitReached" | "ClientPageLimitReached" | "Failed" | "ExactPackageComplete" | "ExplicitCandidatesComplete" | number;

export type BrowserPackageQueryEventKind = "Progress" | "Match" | "Failure" | "Completed" | "Assessment" | number;

export type BrowserPackageQueryEvidenceScope = "Package" | "Query" | number;

export type BrowserPackageQueryExecutionClass = "SearchMetadata" | "Nuspec" | "NuspecExpensive" | "PackageContent" | "Metadata" | "MetadataExpensive" | number;

export type BrowserPackageQueryFailureKind = "Search" | "SearchContract" | "ManifestAcquisition" | "ManifestContract" | "InvalidManifest" | "PackageContentAcquisition" | "PackageContentEvaluation" | "DependencyTraversal" | "AssemblyAcquisition" | "AssemblyEvaluation" | number;

export type BrowserPackageQueryManifestFailureReason = "MalformedXml" | "UnsupportedDocumentShape" | "IdentityMismatch" | "InvalidDependencyContract" | "ConfiguredLimitExceeded" | "InvalidIdentityContract" | number;

export type BrowserPackageQueryManifestIdentityProvenance = "ExpectedCoordinate" | "SelfAttested" | number;

export type BrowserPackageQueryMatchCreditKind = "Granted" | "NotActive" | number;

export type BrowserPackageQueryOperationFailureKind = "Expected" | "Unexpected" | number;

export type BrowserPackageQueryProgressPhase = "Search" | "Manifest" | "PackageContent" | "DependencyTraversal" | "Assembly" | number;

export type BrowserPackageQueryResultKind = "Succeeded" | "Failed" | "Canceled" | number;

export type BrowserPackageVersionSettlementOutcomeKind = "Settled" | "NotSettled" | number;

export type CompiledDocumentationIncompleteReason = "Deadline" | "ContributionLimit" | "CompanionSelectionPartial" | "CompiledXmlByteLimit" | number;

export type CompiledDocumentationRequestRejectionKind = "LibraryReferenceMismatch" | "ApiContentMismatch" | "LeaseReferenceMismatch" | number;

export type CompiledDocumentationSourceEvidenceKind = "Candidate" | "Absent" | "Partial" | "Unavailable" | number;

export type CompiledDocumentationSourceKind = "Package" | "Platform" | "DirectLibrary" | "SourceHouse" | number;

export type CompiledDocumentationSourceRejectionKind = "SubjectMismatch" | "LibraryMismatch" | "ApiContentMismatch" | "CompanionMismatch" | number;

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

export interface BrowserAssemblyReferenceList {
  readonly references: ReadonlyArray<BrowserAssemblyReference>;
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

export interface BrowserExactLibraryApiAssemblyIdentity {
  readonly identity: BrowserExactLibraryApiAssemblyReferenceIdentity;
  readonly moduleVersionId: string;
}

export interface BrowserExactLibraryApiAssemblyReferenceIdentity {
  readonly name: string;
  readonly version: string | null;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
}

export interface BrowserExactLibraryApiAsset {
  readonly id: string;
  readonly path: string;
  readonly assemblyName: string;
  readonly targetFramework: string;
  readonly kind: BrowserExactLibraryApiAssetKind;
}

export interface BrowserExactLibraryApiFacet {
  readonly id: string;
  readonly singularLabel: string;
  readonly pluralLabel: string;
  readonly weight: number;
  readonly count: number;
  readonly isDefault: boolean;
}

export interface BrowserExactLibraryApiInspection {
  readonly content: BrowserExactLibraryApiInspectionResult;
  readonly share: BrowserInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserInspectionDiagnostic>;
}

export interface BrowserExactLibraryApiInspectionFailure {
  readonly kind: BrowserExactLibraryApiInspectionFailureKind;
  readonly detail: string;
  readonly subjectAssembly: BrowserExactLibraryApiAssemblyReferenceIdentity | null;
}

export interface BrowserExactLibraryApiInspectionResult {
  readonly outcome: BrowserExactLibraryApiInspectionOutcome;
  readonly packageId: string;
  readonly packageVersion: string;
  readonly requestedTargetFramework: string;
  readonly requestedLibrary: string;
  readonly source: BrowserExactLibraryApiSourceCoordinate | null;
  readonly asset: BrowserExactLibraryApiAsset | null;
  readonly assembly: BrowserExactLibraryApiAssemblyIdentity | null;
  readonly inventory: BrowserExactLibraryApiInventory | null;
  readonly truncation: BrowserExactLibraryApiProjectionTruncation | null;
  readonly failures: ReadonlyArray<BrowserExactLibraryApiInspectionFailure>;
  readonly isComplete: boolean;
  readonly isAvailable: boolean;
}

export interface BrowserExactLibraryApiInventory {
  readonly publicTypeCount: number;
  readonly publicMemberCount: number;
  readonly publicMethodCount: number;
  readonly publicPropertyCount: number;
  readonly typeKinds: ReadonlyArray<BrowserExactLibraryApiFacet>;
  readonly namespaces: ReadonlyArray<BrowserExactLibraryApiNamespace>;
}

export interface BrowserExactLibraryApiNamespace {
  readonly name: string;
  readonly count: number;
}

export interface BrowserExactLibraryApiProjectionTruncation {
  readonly limit: BrowserExactLibraryApiProjectionLimit;
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

export interface BrowserExactLibraryApiSourceCoordinate {
  readonly packageId: string;
  readonly packageVersion: string;
  readonly producer: string;
  readonly framework: string | null;
}

export interface BrowserExceptionSurface {
  readonly type: string;
  readonly description: string;
}

export interface BrowserInspectionDiagnostic {
  readonly code: string;
  readonly severity: string;
  readonly summary: string;
  readonly correspondence: string | null;
}

export interface BrowserInspectionShare {
  readonly kind: BrowserInspectionShareKind;
  readonly fullUrl: string | null;
  readonly packet: string | null;
  readonly path: string | null;
  readonly reason: string | null;
}

export interface BrowserLibraryQueryDocument {
  readonly results: ReadonlyArray<BrowserLibraryQueryRow>;
  readonly failures: ReadonlyArray<BrowserLibraryQueryFailure>;
  readonly summary: BrowserLibraryQuerySummary;
}

export interface BrowserLibraryQueryFailure {
  readonly assetId: string | null;
  readonly library: string | null;
  readonly path: string | null;
  readonly source: string | null;
  readonly kind: string;
  readonly message: string;
}

export interface BrowserLibraryQueryInspection {
  readonly content: BrowserLibraryQueryDocument;
  readonly share: BrowserInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserInspectionDiagnostic>;
}

export interface BrowserLibraryQueryRow {
  readonly assetId: string;
  readonly library: string;
  readonly path: string;
  readonly source: string;
  readonly version: string | null;
  readonly sourceKind: string;
  readonly targetFramework: string | null;
  readonly matchedReferences: ReadonlyArray<string>;
}

export interface BrowserLibraryQuerySummary {
  readonly populationCandidates: number;
  readonly candidateLimit: number;
  readonly candidates: number;
  readonly matches: number;
  readonly failures: number;
  readonly incompleteReasons: string;
  readonly isComplete: boolean;
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

export interface BrowserPackageAssemblyAssessment {
  readonly packageId: string;
  readonly version: string;
  readonly disposition: BrowserPackageAssemblyAssessmentKind;
  readonly message: string;
  readonly assetPath: string | null;
  readonly rootRequest: string;
}

export interface BrowserPackageAssemblySemanticCandidateOutcome {
  readonly kind: BrowserPackageAssemblySemanticCandidateOutcomeKind;
  readonly candidateOrdinal: number;
  readonly packageId: string;
  readonly version: string;
  readonly producer: string;
  readonly result: BrowserPackageAssemblySemanticResult | null;
  readonly selectedAsset: BrowserPackageAssemblySemanticSelectedAsset | null;
  readonly rootRequest: string | null;
  readonly notApplicableReason: BrowserPackageAssemblyNotApplicableReason | null;
  readonly failureKind: BrowserPackageAssemblySemanticFailureKind | null;
  readonly failureStage: string | null;
  readonly nonEvaluationKind: BrowserPackageAssemblySemanticNonEvaluationKind | null;
  readonly timeoutKind: string | null;
  readonly timeoutSeconds: number | null;
  readonly message: string | null;
}

export interface BrowserPackageAssemblySemanticCompletion {
  readonly population: BrowserPackageAssemblySemanticPopulationCompletionKind;
  readonly isRequestedPopulationComplete: boolean;
  readonly allCandidatesHaveTerminalOutcomes: boolean;
  readonly hasFailures: boolean;
  readonly isSemanticEvaluationComplete: boolean;
  readonly isOperationDeadlineExpired: boolean;
}

export interface BrowserPackageAssemblySemanticDocument {
  readonly population: BrowserPackageAssemblySemanticPopulation;
  readonly results: ReadonlyArray<BrowserPackageAssemblySemanticResult>;
  readonly candidateOutcomes: ReadonlyArray<BrowserPackageAssemblySemanticCandidateOutcome>;
  readonly candidateCount: number;
  readonly evaluatedCandidateCount: number;
  readonly notEvaluatedCount: number;
  readonly matchedPackageCount: number;
  readonly occurrenceCount: number;
  readonly semanticMissCount: number;
  readonly notApplicableCount: number;
  readonly failureCount: number;
  readonly completion: BrowserPackageAssemblySemanticCompletion;
}

export interface BrowserPackageAssemblySemanticOccurrence {
  readonly moduleVersionId: string;
  readonly methodDefinitionToken: number;
  readonly ilOffset: number;
  readonly userStringToken: number;
  readonly literalCharacterCount: number;
  readonly literalText: string;
}

export interface BrowserPackageAssemblySemanticPopulation {
  readonly requestedCandidates: number;
  readonly candidates: number;
  readonly completion: BrowserPackageAssemblySemanticPopulationCompletionKind;
  readonly isRequestedPopulationComplete: boolean;
  readonly failures: ReadonlyArray<BrowserPackageAssemblySemanticPopulationFailure>;
}

export interface BrowserPackageAssemblySemanticPopulationFailure {
  readonly candidateOrdinal: number | null;
  readonly packageId: string | null;
  readonly version: string | null;
  readonly authority: string;
  readonly kind: string;
  readonly message: string;
  readonly timeoutKind: string | null;
  readonly timeoutSeconds: number | null;
}

export interface BrowserPackageAssemblySemanticResult {
  readonly candidateOrdinal: number;
  readonly packageId: string;
  readonly version: string;
  readonly producer: string;
  readonly selectedAsset: BrowserPackageAssemblySemanticSelectedAsset;
  readonly occurrences: ReadonlyArray<BrowserPackageAssemblySemanticOccurrence>;
}

export interface BrowserPackageAssemblySemanticSelectedAsset {
  readonly path: string;
  readonly assemblyName: string;
  readonly targetFramework: string;
  readonly sequence: string;
  readonly ordinal: number;
  readonly unevaluatedSiblings: number;
  readonly rootRequest: string;
}

export interface BrowserPackageCacheStats {
  readonly packages: number;
  readonly resident: number;
  readonly maxPackageEntries: number;
  readonly workspaces: number;
  readonly maxWorkspaces: number;
  readonly maxWorkspaceAssembliesPerRole: number;
  readonly residentBytes: number;
  readonly maxResidentBytes: number;
  readonly maxWorkspaceRetainedImageBytes: number;
}

export interface BrowserPackageChangesAdvisoryAcquisition {
  readonly packageProducerKey: string;
  readonly advisoryProducer: string;
  readonly observedAt: string;
  readonly apiRequests: number;
  readonly responseBytes: number;
  readonly complete: boolean;
  readonly failures: ReadonlyArray<string>;
  readonly packages: ReadonlyArray<BrowserPackageChangesAdvisoryPackage>;
}

export interface BrowserPackageChangesAdvisoryEvidence {
  readonly availability: string;
  readonly advisories: ReadonlyArray<BrowserPackageChangesAdvisoryReference>;
}

export interface BrowserPackageChangesAdvisoryPackage {
  readonly packageId: string;
  readonly version: string;
  readonly currentAdvisoryContext: BrowserPackageChangesAdvisoryEvidence;
  readonly fixedVersionEvidence: BrowserPackageChangesAdvisoryEvidence;
}

export interface BrowserPackageChangesAdvisoryReference {
  readonly ghsaId: string;
  readonly cveId: string | null;
  readonly severity: string;
  readonly advisoryUrl: string;
  readonly publishedAt: string;
  readonly updatedAt: string;
}

export interface BrowserPackageChangesCancellation {
  readonly kind: BrowserPackageChangesCancellationKind;
  readonly reason: string | null;
}

export interface BrowserPackageChangesCatalogActivity {
  readonly packageId: string;
  readonly version: string;
  readonly normalizedPackageId: string;
  readonly normalizedVersion: string;
  readonly leafUrl: string;
  readonly commitId: string;
  readonly commitTimestamp: string;
  readonly catalogKind: string;
  readonly activity: string;
}

export interface BrowserPackageChangesDocument {
  readonly schemaVersion: number;
  readonly request: BrowserPackageChangesResolvedRequest;
  readonly source: BrowserPackageChangesSource;
  readonly progress: ReadonlyArray<BrowserPackageChangesProgress>;
  readonly rows: ReadonlyArray<BrowserPackageChangesRow>;
  readonly failures: ReadonlyArray<BrowserPackageChangesFailure>;
  readonly summary: BrowserPackageChangesSummary;
}

export interface BrowserPackageChangesFailure {
  readonly provider: string;
  readonly catalogFailure: BrowserPackageChangesPackageSourceFailure | null;
  readonly advisoryFailure: string | null;
  readonly packageReceiptFailure: BrowserPackageChangesReceiptFailure | null;
}

export interface BrowserPackageChangesInspection {
  readonly content: BrowserPackageChangesDocument;
  readonly share: BrowserInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserInspectionDiagnostic>;
}

export interface BrowserPackageChangesPackageReceipt {
  readonly receivedAt: string;
  readonly basis: string;
}

export interface BrowserPackageChangesPackageScope {
  readonly kind: string;
  readonly selectionId: string | null;
  readonly prefix: string | null;
  readonly packageIds: ReadonlyArray<string>;
}

export interface BrowserPackageChangesPackageSetCatalog {
  readonly version: number;
  readonly packageSets: ReadonlyArray<BrowserPackageChangesPackageSetDescriptor>;
}

export interface BrowserPackageChangesPackageSetDescriptor {
  readonly id: string;
  readonly title: string;
  readonly summary: string;
  readonly order: number;
}

export interface BrowserPackageChangesPackageSourceFailure {
  readonly capability: number;
  readonly packageId: string | null;
  readonly version: string | null;
  readonly kind: string;
  readonly detail: string;
}

export interface BrowserPackageChangesProgress {
  readonly phase: string;
  readonly completed: number;
  readonly total: number | null;
  readonly capturedHorizon: string | null;
  readonly catalogPagesAcquired: number;
  readonly catalogHttpAttempts: number;
  readonly catalogDecodedBytes: number;
}

export interface BrowserPackageChangesReceiptFailure {
  readonly catalogActivity: BrowserPackageChangesCatalogActivity;
  readonly failure: BrowserPackageChangesPackageSourceFailure;
}

export interface BrowserPackageChangesRequest {
  readonly packageSetId: string;
  readonly fromExclusive: string | null;
  readonly throughInclusive: string | null;
  readonly securityOnly: boolean;
  readonly maximumRows: number;
}

export interface BrowserPackageChangesResolvedRequest {
  readonly referenceTime: string;
  readonly fromExclusive: string;
  readonly throughInclusive: string;
  readonly usedDefaultInterval: boolean;
  readonly packageScope: BrowserPackageChangesPackageScope;
  readonly securitySelection: string;
  readonly maximumRows: number;
  readonly maximumCandidateEvents: number;
  readonly maximumReceiptRequests: number;
}

export interface BrowserPackageChangesResult {
  readonly version: number;
  readonly kind: BrowserPackageChangesResultKind;
  readonly inspection: BrowserPackageChangesInspection | null;
  readonly failureKind: BrowserPackageChangesOperationFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
}

export interface BrowserPackageChangesRow {
  readonly catalogActivity: BrowserPackageChangesCatalogActivity;
  readonly currentAdvisoryContext: BrowserPackageChangesAdvisoryEvidence;
  readonly fixedVersionEvidence: BrowserPackageChangesAdvisoryEvidence;
  readonly packageReceipt: BrowserPackageChangesPackageReceipt | null;
  readonly securityReleaseStatus: string;
  readonly securityRelease: BrowserPackageChangesSecurityRelease | null;
  readonly isSecurityRelevant: boolean;
}

export interface BrowserPackageChangesSecurityRelease {
  readonly receipt: BrowserPackageChangesPackageReceipt;
  readonly advisories: ReadonlyArray<BrowserPackageChangesAdvisoryReference>;
}

export interface BrowserPackageChangesSource {
  readonly producerKey: string;
  readonly producer: string;
  readonly transportKind: string;
}

export interface BrowserPackageChangesSummary {
  readonly capturedHorizon: string | null;
  readonly catalogCompletion: string | null;
  readonly catalogFailure: BrowserPackageChangesPackageSourceFailure | null;
  readonly catalogPagesAcquired: number;
  readonly catalogHttpAttempts: number;
  readonly catalogDecodedBytes: number;
  readonly catalogInWindowEventCount: number;
  readonly matchingEventCount: number;
  readonly retainedEventCount: number;
  readonly candidateLimitReached: boolean;
  readonly advisoryEvidence: BrowserPackageChangesAdvisoryAcquisition;
  readonly receiptCandidates: number;
  readonly receiptRequests: number;
  readonly receiptSuccesses: number;
  readonly receiptFailures: ReadonlyArray<BrowserPackageChangesReceiptFailure>;
  readonly receiptLimitReached: boolean;
  readonly currentContextUnevaluableRows: number;
  readonly securityReleaseUnevaluableRows: number;
  readonly eligibleRowCount: number;
  readonly returnedRowCount: number;
  readonly resultLimitReached: boolean;
  readonly completion: string;
}

export interface BrowserPackageDependencies {
  readonly package: string;
  readonly version: string;
  readonly activeFramework: string;
  readonly assembly: string | null;
  readonly dependencyGroups: ReadonlyArray<BrowserPackageDependencyGroup>;
  readonly declarationFailures: ReadonlyArray<BrowserPackageDependencyDeclarationFailure>;
  readonly assemblyReferences: BrowserAssemblyReferenceResult;
  readonly dependencyGroupError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPackageDependency {
  readonly id: string;
  readonly versionRange: string;
}

export interface BrowserPackageDependencyDeclarationFailure {
  readonly kind: BrowserPackageDependencyDeclarationFailureKind;
  readonly framework: string | null;
  readonly package: string | null;
  readonly sourceOccurrenceCount: number | null;
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

export interface BrowserPackageInfoMeasurementInspection {
  readonly content: BrowserPackageInfoMeasurements;
  readonly share: BrowserInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserInspectionDiagnostic>;
}

export interface BrowserPackageInfoMeasurements {
  readonly status: string;
  readonly packageId: string;
  readonly packageVersion: string;
  readonly compressedPackageBytes: number | null;
  readonly selectedTargetFramework: string | null;
  readonly availableTargetFrameworks: ReadonlyArray<string> | null;
  readonly selectedTargetFrameworkFolders: ReadonlyArray<string> | null;
  readonly selectedLibraryPayloadBytes: number | null;
  readonly selectedLibraryCount: number | null;
  readonly detail: string | null;
  readonly unavailableReason: string | null;
  readonly hasSelectedSlice: boolean;
}

export interface BrowserPackageLoadResult {
  readonly versionSettlement: BrowserPackageVersionSettlementInspection;
  readonly packageInfo: BrowserPackageInfoMeasurementInspection | null;
  readonly surface: BrowserPackageSurface | null;
}

export interface BrowserPackagePruningRequest {
  readonly schemaVersion: number;
  readonly family: string;
  readonly targetFramework: string;
  readonly platformVersion: string;
  readonly supplies: ReadonlyArray<BrowserPackagePruningSupply>;
}

export interface BrowserPackagePruningResult {
  readonly schemaVersion: number;
  readonly package: string;
  readonly version: string;
  readonly targetFramework: string;
  readonly selectedFramework: string | null;
  readonly family: string;
  readonly platformVersion: string;
  readonly completion: BrowserPackagePruningCompletion;
  readonly rows: ReadonlyArray<BrowserPackagePruningRow>;
  readonly declarationFailures: ReadonlyArray<BrowserPackageDependencyDeclarationFailure>;
  readonly summary: BrowserPackagePruningSummary;
  readonly message: string | null;
}

export interface BrowserPackagePruningRow {
  readonly package: string;
  readonly requestedRange: string;
  readonly candidateVersion: string | null;
  readonly platformSuppliedVersion: string | null;
  readonly disposition: BrowserPackagePruningDisposition;
  readonly reason: string;
}

export interface BrowserPackagePruningSummary {
  readonly declarations: number;
  readonly evaluated: number;
  readonly delegated: number;
  readonly retained: number;
  readonly notEvaluated: number;
  readonly failed: number;
  readonly declarationFailures: number;
}

export interface BrowserPackagePruningSupply {
  readonly pack: string;
  readonly family: string;
  readonly package: string;
  readonly version: string;
}

export interface BrowserPackageQueryAnswer {
  readonly id: string;
  readonly value: string;
  readonly term: BrowserPackageQueryTerm | null;
}

export interface BrowserPackageQueryCancellation {
  readonly kind: BrowserPackageQueryCancellationKind;
  readonly reason: string | null;
}

export interface BrowserPackageQueryCatalog {
  readonly presets: ReadonlyArray<BrowserPackageQueryPresetDescriptor>;
  readonly terms: ReadonlyArray<BrowserPackageQueryTermDescriptor>;
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
  readonly semanticMisses: number | null;
  readonly notApplicable: number | null;
  readonly scope: string | null;
  readonly occurrences: number | null;
  readonly notEvaluated: number | null;
}

export interface BrowserPackageQueryDeclaredDependency {
  readonly id: string;
  readonly versionRange: string;
}

export interface BrowserPackageQueryDeclaredDependencyGroup {
  readonly targetFramework: string;
  readonly dependencies: ReadonlyArray<BrowserPackageQueryDeclaredDependency>;
  readonly isImplicitManifestGroup: boolean;
}

export interface BrowserPackageQueryDocument {
  readonly results: ReadonlyArray<BrowserPackageQueryRow>;
  readonly hasPackages: boolean;
  readonly failures: ReadonlyArray<BrowserPackageQueryFailure>;
  readonly completion: BrowserPackageQueryCompletion;
  readonly assemblySemantic: BrowserPackageAssemblySemanticDocument | null;
}

export interface BrowserPackageQueryEvent {
  readonly kind: BrowserPackageQueryEventKind;
  readonly row: BrowserPackageQueryRow | null;
  readonly failure: BrowserPackageQueryFailure | null;
  readonly completion: BrowserPackageQueryCompletion | null;
  readonly progress: BrowserPackageQueryProgress | null;
  readonly assessment: BrowserPackageAssemblyAssessment | null;
}

export interface BrowserPackageQueryEvidence {
  readonly id: string;
  readonly scope: BrowserPackageQueryEvidenceScope;
  readonly summary: BrowserPackageQueryEvidenceSummary | null;
  readonly properties: ReadonlyArray<BrowserPackageQueryEvidenceProperty>;
  readonly number: number | null;
  readonly term: BrowserPackageQueryTerm | null;
}

export interface BrowserPackageQueryEvidenceProperty {
  readonly name: string;
  readonly value: string;
}

export interface BrowserPackageQueryEvidenceSummary {
  readonly count: number;
  readonly preview: ReadonlyArray<string>;
}

export interface BrowserPackageQueryFailure {
  readonly packageId: string | null;
  readonly version: string | null;
  readonly producer: string;
  readonly kind: BrowserPackageQueryFailureKind;
  readonly message: string;
  readonly manifestFailureReason: BrowserPackageQueryManifestFailureReason | null;
}

export interface BrowserPackageQueryInspection {
  readonly content: BrowserPackageQueryDocument;
  readonly share: BrowserInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserInspectionDiagnostic>;
}

export interface BrowserPackageQueryManifest {
  readonly packageId: string;
  readonly version: string;
  readonly manifestVersion: string;
  readonly description: string | null;
  readonly authors: string | null;
  readonly repository: string | null;
  readonly repositoryType: string | null;
  readonly repositoryCommit: string | null;
  readonly license: string | null;
  readonly licenseUrl: string | null;
  readonly packageTypes: ReadonlyArray<string>;
  readonly isToolPackage: boolean;
  readonly readmeFile: string | null;
  readonly dependencyGroups: ReadonlyArray<BrowserPackageQueryDeclaredDependencyGroup>;
  readonly iconFile: string | null;
  readonly iconUrl: string | null;
  readonly identityProvenance: BrowserPackageQueryManifestIdentityProvenance;
}

export interface BrowserPackageQueryMatchCreditResponse {
  readonly kind: BrowserPackageQueryMatchCreditKind;
  readonly additionalMatchCredit: number | null;
}

export interface BrowserPackageQueryPresetDescriptor {
  readonly key: string;
  readonly operator: string;
  readonly value: string;
  readonly label: string;
  readonly summary: string;
  readonly weight: number;
  readonly tier: BrowserPackageQueryAcquisitionTier;
  readonly executionClass: BrowserPackageQueryExecutionClass;
  readonly selectionGroupId: string | null;
  readonly combinesWithinSelectionGroup: boolean;
  readonly replacementGroupId: string | null;
  readonly displayGroupId: string | null;
  readonly displayGroupLabel: string | null;
}

export interface BrowserPackageQueryProgress {
  readonly phase: BrowserPackageQueryProgressPhase;
  readonly completed: number;
  readonly limit: number;
}

export interface BrowserPackageQueryResult {
  readonly version: number;
  readonly kind: BrowserPackageQueryResultKind;
  readonly value: BrowserPackageQueryEvent | null;
  readonly inspection: BrowserPackageQueryInspection | null;
  readonly failureKind: BrowserPackageQueryOperationFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly reason: string | null;
}

export interface BrowserPackageQueryRow {
  readonly packageId: string;
  readonly version: string;
  readonly tier: BrowserPackageQueryAcquisitionTier;
  readonly answers: ReadonlyArray<BrowserPackageQueryAnswer>;
  readonly evidence: ReadonlyArray<BrowserPackageQueryEvidence>;
  readonly totalDownloads: number | null;
  readonly verified: boolean | null;
  readonly producer: string;
  readonly description: string | null;
  readonly rootRequest: string | null;
  readonly owners: ReadonlyArray<string>;
  readonly manifest: BrowserPackageQueryManifest | null;
}

export interface BrowserPackageQueryTerm {
  readonly key: string;
  readonly operator: string;
  readonly value: string;
}

export interface BrowserPackageQueryTermDescriptor {
  readonly key: string;
  readonly label: string;
  readonly summary: string;
  readonly weight: number;
  readonly tier: BrowserPackageQueryAcquisitionTier;
  readonly executionClass: BrowserPackageQueryExecutionClass;
  readonly operators: ReadonlyArray<string>;
  readonly valueKind: string;
  readonly example: string;
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

export interface BrowserPackageVersionSettlementAuthorityFailure {
  readonly authority: string;
  readonly kind: string;
  readonly message: string;
  readonly timeoutKind: string | null;
}

export interface BrowserPackageVersionSettlementCoordinate {
  readonly packageId: string;
  readonly version: string;
}

export interface BrowserPackageVersionSettlementFailure {
  readonly request: BrowserPackageVersionSettlementRequest;
  readonly kind: string;
  readonly reason: string;
  readonly operationTimedOut: boolean;
  readonly authorityFailures: ReadonlyArray<BrowserPackageVersionSettlementAuthorityFailure>;
}

export interface BrowserPackageVersionSettlementInspection {
  readonly content: BrowserPackageVersionSettlementOutcome;
  readonly share: BrowserInspectionShare;
  readonly diagnostics: ReadonlyArray<BrowserInspectionDiagnostic>;
}

export interface BrowserPackageVersionSettlementListing {
  readonly version: string;
  readonly listed: boolean;
}

export interface BrowserPackageVersionSettlementOutcome {
  readonly kind: BrowserPackageVersionSettlementOutcomeKind;
  readonly result: BrowserPackageVersionSettlementResult | null;
  readonly failure: BrowserPackageVersionSettlementFailure | null;
}

export interface BrowserPackageVersionSettlementRequest {
  readonly packageId: string;
  readonly version: string | null;
}

export interface BrowserPackageVersionSettlementResult {
  readonly request: BrowserPackageVersionSettlementRequest;
  readonly coordinate: BrowserPackageVersionSettlementCoordinate;
  readonly includePrerelease: boolean;
  readonly freshness: string | null;
  readonly listings: ReadonlyArray<BrowserPackageVersionSettlementListing>;
  readonly sourceListings: ReadonlyArray<BrowserPackageVersionSettlementSourceListing>;
}

export interface BrowserPackageVersionSettlementSourceListing {
  readonly version: string;
  readonly feed: string;
  readonly listed: boolean;
}

export interface BrowserPackageVersions {
  readonly versions: ReadonlyArray<string>;
  readonly currentVersionInsertionIndex: number;
  readonly previousVersion?: string;
  readonly previousVersionUnavailableReason?: string;
}

export interface BrowserParameterSurface {
  readonly name: string;
  readonly type: string;
  readonly modifier: string | null;
  readonly hasDefault: boolean;
  readonly defaultValue: string | null;
  readonly description: string | null;
}

export interface BrowserPlatformCatalog {
  readonly tfm: string;
  readonly version: string;
  readonly rows: ReadonlyArray<BrowserPlatformLibrary>;
}

export interface BrowserPlatformLibrary {
  readonly tfm: string;
  readonly pack: string;
  readonly assembly: string;
  readonly file: string;
  readonly kind: string;
  readonly forwardsTo: string | null;
  readonly version: string;
  readonly publicTypes: number;
  readonly inReferencePack: boolean;
  readonly hasImplementation: boolean;
  readonly packVersion: string;
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

export interface CompiledDocumentationAssemblyIdentity {
  readonly name?: string;
  readonly version?: string;
  readonly culture?: string;
  readonly publicKeyToken?: string;
}

export interface CompiledDocumentationEntry {
  readonly summary?: string;
  readonly remarks?: string;
  readonly returns?: string;
  readonly parameters: ReadonlyArray<CompiledDocumentationParameter>;
  readonly exceptions: ReadonlyArray<CompiledDocumentationException>;
  readonly samples: ReadonlyArray<CompiledDocumentationSample>;
}

export interface CompiledDocumentationException {
  readonly reference?: string;
  readonly description?: string;
}

export interface CompiledDocumentationParameter {
  readonly name?: string;
  readonly description?: string;
}

export interface CompiledDocumentationSample {
  readonly code?: string;
  readonly title?: string;
  readonly region?: string;
}

export interface CompiledDocumentationSource {
  readonly kind: CompiledDocumentationSourceKind;
  readonly name?: string;
  readonly precedence?: number;
}

export interface CompiledDocumentationSourceEvidence {
  readonly source: CompiledDocumentationSource;
  readonly kind: CompiledDocumentationSourceEvidenceKind;
}

export interface CompiledDocumentationSourceRejection {
  readonly source: CompiledDocumentationSource;
  readonly reason: CompiledDocumentationSourceRejectionKind;
}

export interface CompiledDocumentationSubject {
  readonly assembly: CompiledDocumentationAssemblyIdentity;
  readonly documentationId?: string;
}

export interface Absent {
  readonly kind: "absent";
  readonly subject: CompiledDocumentationSubject;
  readonly sources: ReadonlyArray<CompiledDocumentationSourceEvidence>;
  readonly sourcesTruncated?: boolean;
}

export interface Ambiguous {
  readonly kind: "ambiguous";
  readonly subject: CompiledDocumentationSubject;
  readonly candidates: ReadonlyArray<CompiledDocumentationSource>;
  readonly candidatesTruncated?: boolean;
}

export interface Available {
  readonly kind: "available";
  readonly subject: CompiledDocumentationSubject;
  readonly source: CompiledDocumentationSource;
  readonly documentation: CompiledDocumentationEntry;
}

export interface ContentAccessFailed {
  readonly kind: "contentAccessFailed";
  readonly subject: CompiledDocumentationSubject;
  readonly source: CompiledDocumentationSource;
}

export interface ContributionsRejected {
  readonly kind: "contributionsRejected";
  readonly subject: CompiledDocumentationSubject;
  readonly rejections: ReadonlyArray<CompiledDocumentationSourceRejection>;
  readonly rejectionsTruncated?: boolean;
}

export interface Incomplete {
  readonly kind: "incomplete";
  readonly subject: CompiledDocumentationSubject;
  readonly reason: CompiledDocumentationIncompleteReason;
  readonly sources: ReadonlyArray<CompiledDocumentationSourceEvidence>;
  readonly sourcesTruncated?: boolean;
}

export interface MalformedOrUnreadableDocument {
  readonly kind: "malformedOrUnreadableDocument";
  readonly subject: CompiledDocumentationSubject;
  readonly source: CompiledDocumentationSource;
}

export interface RequestRejected {
  readonly kind: "requestRejected";
  readonly subject: CompiledDocumentationSubject;
  readonly reason: CompiledDocumentationRequestRejectionKind;
}

export interface Unavailable {
  readonly kind: "unavailable";
  readonly subject: CompiledDocumentationSubject;
  readonly sources: ReadonlyArray<CompiledDocumentationSourceEvidence>;
  readonly sourcesTruncated?: boolean;
}

export type CompiledDocumentationOutcome = Available | Absent | Unavailable | Ambiguous | ContributionsRejected | MalformedOrUnreadableDocument | Incomplete | RequestRejected | ContentAccessFailed;

export type BrowserAssemblyReferenceResult = BrowserAssemblyReferenceList | string | null;

type $ManagedExports = {
  readonly "DotnetInspect": {
    readonly "Web": {
      readonly "Interop": {
        readonly "Package": {
          readonly "PackageExports": {
            readonly "ActivateWorkspacePackageOccurrence.976702342": (action: string) => Promise<string>;
            readonly "CancelPackageActivity.271973316": (operationId: string, reason: string) => string;
            readonly "CancelPackageQuery.271973316": (operationId: string, reason: string) => string;
            readonly "ClassifyPackageGraphIdentities.271973316": (inspectedPackageId: string, packageIdsJson: string) => string;
            readonly "ClearWorkspacePackageOccurrences.1731052262": () => Promise<void>;
            readonly "GetPackageDocument.1001223652": (packageId: string, version: string, path: string) => Promise<string>;
            readonly "GetPlatformCatalog.451505237": (targetFramework: string, platformVersion: string) => Promise<string>;
            readonly "GetPlatformVersions.976702342": (targetFramework: string) => Promise<string>;
            readonly "ListPackageActivityPackageSets.1310674786": () => string;
            readonly "ListPackageQueryCatalog.1310674786": () => string;
            readonly "LoadRuntimePack.451505237": (targetFramework: string, platformVersion: string) => Promise<string>;
            readonly "LoadRuntimePackAssembly.1330709314": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, assetFileName: string) => Promise<string>;
            readonly "MatchPackageDependencyCoordinate.1537767637": (packageId: string, declaredRange: string | null, candidatesJson: string) => string;
            readonly "PackageCacheStats.1310674786": () => string;
            readonly "PrefetchPlatformPacks.1782598084": (targetFramework: string, platformVersion: string) => Promise<void>;
            readonly "QueryLibraries.1330709314": (packageId: string, version: string, targetFramework: string, admittedAssetIdsJson: string, requiredReferencesJson: string) => Promise<string>;
            readonly "QueryLibraryApi.1579276339": (packageId: string, version: string, targetFramework: string, assemblyId: string) => Promise<string>;
            readonly "QueryMemberDocumentation.1330709314": (packageId: string, version: string, framework: string, assemblyName: string, documentationId: string) => Promise<string>;
            readonly "QueryPackage.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
            readonly "QueryPackageDependencies.1579276339": (packageId: string, version: string, targetFramework: string, assemblyId: string) => Promise<string>;
            readonly "QueryPackagePruning.1579276339": (packageId: string, version: string, targetFramework: string, requestJson: string) => Promise<string>;
            readonly "QueryPackageRoot.976702342": (rootRequest: string) => Promise<string>;
            readonly "QueryPackageVersions.451505237": (packageId: string, currentVersion: string) => Promise<string>;
            readonly "QueryPlatformMemberDocumentation.1330709314": (framework: string, platformVersion: string, assemblyName: string, platformPack: string, documentationId: string) => Promise<string>;
            readonly "QueryWorkspacePackageOccurrences.976702342": (workspaceJson: string) => Promise<string>;
            readonly "RequestPackageQueryMatches.146925470": (operationId: string, additionalMatchCredit: number) => string;
            readonly "ResolvePackageDependencyVersion.451505237": (packageId: string, declaredRange: string | null) => Promise<string>;
            readonly "RunPackageActivity.1791926993": (operationId: string, requestJson: string, eventSink: unknown) => Promise<string>;
            readonly "RunPackageAssemblySemanticQuery.1998922553": (operationId: string, packageInput: string, literal: string, targetFramework: string, maximumCandidates: number, includePrerelease: boolean, initialMatchCredit: number, eventSink: unknown) => Promise<string>;
            readonly "RunPackageQuery.52840355": (operationId: string, prefix: string, termsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, initialMatchCredit: number, eventSink: unknown) => Promise<string>;
            readonly "SearchTypes.271973316": (query: string, candidatesJson: string) => string;
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
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ActivateWorkspacePackageOccurrence.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ActivateWorkspacePackageOccurrence.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "CancelPackageActivity.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.CancelPackageActivity.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "CancelPackageQuery.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.CancelPackageQuery.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ClassifyPackageGraphIdentities.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ClassifyPackageGraphIdentities.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ClearWorkspacePackageOccurrences.1731052262");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ClearWorkspacePackageOccurrences.1731052262\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "GetPackageDocument.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.GetPackageDocument.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "GetPlatformCatalog.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.GetPlatformCatalog.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "GetPlatformVersions.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.GetPlatformVersions.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ListPackageActivityPackageSets.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ListPackageActivityPackageSets.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ListPackageQueryCatalog.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ListPackageQueryCatalog.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "LoadRuntimePack.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePack.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "LoadRuntimePackAssembly.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePackAssembly.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "MatchPackageDependencyCoordinate.1537767637");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.MatchPackageDependencyCoordinate.1537767637\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "PackageCacheStats.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.PackageCacheStats.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "PrefetchPlatformPacks.1782598084");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.PrefetchPlatformPacks.1782598084\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryLibraries.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryLibraries.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryLibraryApi.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryLibraryApi.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryMemberDocumentation.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryMemberDocumentation.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackage.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackage.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackageDependencies.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageDependencies.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackagePruning.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackagePruning.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackageRoot.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageRoot.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPackageVersions.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageVersions.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryPlatformMemberDocumentation.1330709314");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPlatformMemberDocumentation.1330709314\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "QueryWorkspacePackageOccurrences.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryWorkspacePackageOccurrences.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "RequestPackageQueryMatches.146925470");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RequestPackageQueryMatches.146925470\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ResolvePackageDependencyVersion.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ResolvePackageDependencyVersion.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "RunPackageActivity.1791926993");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RunPackageActivity.1791926993\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "RunPackageAssemblySemanticQuery.1998922553");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RunPackageAssemblySemanticQuery.1998922553\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "RunPackageQuery.52840355");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RunPackageQuery.52840355\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "SearchTypes.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.SearchTypes.271973316\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Package");
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

export async function activateWorkspacePackageOccurrence(action: string): Promise<BrowserWorkspacePackageOccurrenceActivation> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ActivateWorkspacePackageOccurrence.976702342"](action);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspacePackageOccurrenceActivation;
}

export function cancelPackageActivity(operationId: string, reason: string): BrowserPackageChangesCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["CancelPackageActivity.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageChangesCancellation;
}

export function cancelPackageQuery(operationId: string, reason: string): BrowserPackageQueryCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["CancelPackageQuery.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryCancellation;
}

export function classifyPackageGraphIdentities(inspectedPackageId: string, packageIdsJson: ReadonlyArray<string>): ReadonlyArray<BrowserPackageGraphIdentityRole> {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ClassifyPackageGraphIdentities.271973316"](inspectedPackageId, $serializeJsonInput(packageIdsJson, "DotnetInspect.Web.Interop.Package.PackageExports.ClassifyPackageGraphIdentities.271973316", "packageIdsJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<BrowserPackageGraphIdentityRole>;
}

export async function clearWorkspacePackageOccurrences(): Promise<void> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ClearWorkspacePackageOccurrences.1731052262"]();
}

export async function getPackageDocument(packageId: string, version: string, path: string): Promise<BrowserPackageDocumentContent> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["GetPackageDocument.1001223652"](packageId, version, path);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageDocumentContent;
}

export async function getPlatformCatalog(targetFramework: string, platformVersion: string): Promise<BrowserPlatformCatalog> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["GetPlatformCatalog.451505237"](targetFramework, platformVersion);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPlatformCatalog;
}

export async function getPlatformVersions(targetFramework: string): Promise<ReadonlyArray<string>> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["GetPlatformVersions.976702342"](targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<string>;
}

export function listPackageActivityPackageSets(): BrowserPackageChangesPackageSetCatalog {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ListPackageActivityPackageSets.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageChangesPackageSetCatalog;
}

export function listPackageQueryCatalog(): BrowserPackageQueryCatalog {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ListPackageQueryCatalog.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryCatalog;
}

export async function loadRuntimePack(targetFramework: string, platformVersion: string): Promise<string> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["LoadRuntimePack.451505237"](targetFramework, platformVersion);
}

export async function loadRuntimePackAssembly(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, assetFileName: string): Promise<string> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["LoadRuntimePackAssembly.1330709314"](targetFramework, platformVersion, assemblyFileName, pack, assetFileName);
}

export function matchPackageDependencyCoordinate(packageId: string, declaredRange: string | null, candidatesJson: ReadonlyArray<BrowserDependencyCoordinateCandidate>): BrowserDependencyCoordinateMatch {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["MatchPackageDependencyCoordinate.1537767637"](packageId, declaredRange, $serializeJsonInput(candidatesJson, "DotnetInspect.Web.Interop.Package.PackageExports.MatchPackageDependencyCoordinate.1537767637", "candidatesJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserDependencyCoordinateMatch;
}

export function packageCacheStats(): BrowserPackageCacheStats {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["PackageCacheStats.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageCacheStats;
}

export async function prefetchPlatformPacks(targetFramework: string, platformVersion: string): Promise<void> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["PrefetchPlatformPacks.1782598084"](targetFramework, platformVersion);
}

export async function queryLibraries(packageId: string, version: string, targetFramework: string, admittedAssetIdsJson: string, requiredReferencesJson: string): Promise<BrowserLibraryQueryInspection> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryLibraries.1330709314"](packageId, version, targetFramework, admittedAssetIdsJson, requiredReferencesJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserLibraryQueryInspection;
}

export async function queryLibraryApi(packageId: string, version: string, targetFramework: string, assemblyId: string): Promise<BrowserExactLibraryApiInspection> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryLibraryApi.1579276339"](packageId, version, targetFramework, assemblyId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserExactLibraryApiInspection;
}

export async function queryMemberDocumentation(packageId: string, version: string, framework: string, assemblyName: string, documentationId: string): Promise<CompiledDocumentationOutcome> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryMemberDocumentation.1330709314"](packageId, version, framework, assemblyName, documentationId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as CompiledDocumentationOutcome;
}

export async function queryPackage(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageLoadResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackage.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageLoadResult;
}

export async function queryPackageDependencies(packageId: string, version: string, targetFramework: string, assemblyId: string): Promise<BrowserPackageDependencies> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackageDependencies.1579276339"](packageId, version, targetFramework, assemblyId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageDependencies;
}

export async function queryPackagePruning(packageId: string, version: string, targetFramework: string, requestJson: string): Promise<BrowserPackagePruningResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackagePruning.1579276339"](packageId, version, targetFramework, requestJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackagePruningResult;
}

export async function queryPackageRoot(rootRequest: string): Promise<BrowserPackageSurface> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackageRoot.976702342"](rootRequest);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageSurface;
}

export async function queryPackageVersions(packageId: string, currentVersion: string): Promise<BrowserPackageVersions> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackageVersions.451505237"](packageId, currentVersion);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageVersions;
}

export async function queryPlatformMemberDocumentation(framework: string, platformVersion: string, assemblyName: string, platformPack: string, documentationId: string): Promise<CompiledDocumentationOutcome> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPlatformMemberDocumentation.1330709314"](framework, platformVersion, assemblyName, platformPack, documentationId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as CompiledDocumentationOutcome;
}

export async function queryWorkspacePackageOccurrences(workspaceJson: string): Promise<BrowserWorkspacePackageOccurrenceView> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryWorkspacePackageOccurrences.976702342"](workspaceJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspacePackageOccurrenceView;
}

export function requestPackageQueryMatches(operationId: string, additionalMatchCredit: number): BrowserPackageQueryMatchCreditResponse {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RequestPackageQueryMatches.146925470"](operationId, additionalMatchCredit);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryMatchCreditResponse;
}

export async function resolvePackageDependencyVersion(packageId: string, declaredRange: string | null): Promise<string> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ResolvePackageDependencyVersion.451505237"](packageId, declaredRange);
}

export async function runPackageActivity(operationId: string, requestJson: string, eventSink: unknown): Promise<BrowserPackageChangesResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageActivity.1791926993"](operationId, requestJson, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageChangesResult;
}

export async function runPackageAssemblySemanticQuery(operationId: string, packageInput: string, literal: string, targetFramework: string, maximumCandidates: number, includePrerelease: boolean, initialMatchCredit: number, eventSink: unknown): Promise<BrowserPackageQueryResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageAssemblySemanticQuery.1998922553"](operationId, packageInput, literal, targetFramework, maximumCandidates, includePrerelease, initialMatchCredit, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryResult;
}

export async function runPackageQuery(operationId: string, prefix: string, termsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, initialMatchCredit: number, eventSink: unknown): Promise<BrowserPackageQueryResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageQuery.52840355"](operationId, prefix, termsJson, maximumCandidates, maximumMatches, includePrerelease, initialMatchCredit, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryResult;
}

export function searchTypes(query: string, candidatesJson: ReadonlyArray<BrowserTypeCandidate>): ReadonlyArray<BrowserTypeSearchHit> {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["SearchTypes.271973316"](query, $serializeJsonInput(candidatesJson, "DotnetInspect.Web.Interop.Package.PackageExports.SearchTypes.271973316", "candidatesJson"));
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<BrowserTypeSearchHit>;
}

