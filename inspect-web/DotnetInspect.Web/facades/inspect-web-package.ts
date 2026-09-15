import { dotnet } from "./runtime-loader.js";

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export type BrowserDependencyCoordinateMatchOutcome = "NoMatch" | "Unique" | "Ambiguous" | number;

export type BrowserDependencyCoordinateProvenance = "NuGetPackage" | "PlatformRuntime" | number;

export type BrowserInspectionShareKind = "Available" | "NonProjectable" | number;

export type BrowserPackageAssemblyAssessmentKind = "NoMatch" | "NotApplicable" | number;

export type BrowserPackageChangesCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;

export type BrowserPackageChangesOperationFailureKind = "Expected" | "Unexpected" | number;

export type BrowserPackageChangesResultKind = "Succeeded" | "Failed" | "Canceled" | number;

export type BrowserPackageQueryCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;

export type BrowserPackageQueryCompletionKind = "Exhausted" | "MatchLimitReached" | "CandidateLimitReached" | "SourcePageLimitReached" | "ClientPageLimitReached" | "Failed" | "ExactPackageComplete" | "ExplicitCandidatesComplete" | number;

export type BrowserPackageQueryEventKind = "Progress" | "Match" | "Failure" | "Completed" | "Assessment" | number;

export type BrowserPackageQueryEvidenceScope = "Package" | "Query" | number;

export type BrowserPackageQueryFacetTier = "Nuspec" | "PackageContent" | "SearchMetadata" | "Assembly" | number;

export type BrowserPackageQueryFailureKind = "Search" | "SearchContract" | "ManifestAcquisition" | "ManifestContract" | "InvalidManifest" | "PackageContentAcquisition" | "PackageContentEvaluation" | "AssemblyAcquisition" | "AssemblyEvaluation" | number;

export type BrowserPackageQueryManifestFailureReason = "MalformedXml" | "UnsupportedDocumentShape" | "IdentityMismatch" | "InvalidDependencyContract" | "ConfiguredLimitExceeded" | "InvalidIdentityContract" | number;

export type BrowserPackageQueryManifestIdentityProvenance = "ExpectedCoordinate" | "SelfAttested" | number;

export type BrowserPackageQueryMatchCreditKind = "Granted" | "NotActive" | number;

export type BrowserPackageQueryOperationFailureKind = "Expected" | "Unexpected" | number;

export type BrowserPackageQueryProgressPhase = "Search" | "Manifest" | "PackageContent" | "Assembly" | number;

export type BrowserPackageQueryResultKind = "Succeeded" | "Failed" | "Canceled" | number;

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

export interface BrowserPackageAssemblyQueryPattern {
  readonly id: string;
  readonly label: string;
  readonly summary: string;
  readonly maximumOperandLength: number;
  readonly maximumPackages: number;
}

export interface BrowserPackageCacheStats {
  readonly packages: number;
  readonly resident: number;
  readonly workspaces: number;
  readonly residentBytes: number;
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
  readonly assemblyReferences: BrowserAssemblyReferenceResult;
  readonly dependencyGroupError: string | null;
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

export interface BrowserPackageQueryCancellation {
  readonly kind: BrowserPackageQueryCancellationKind;
  readonly reason: string | null;
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
  readonly text: string;
  readonly scope: BrowserPackageQueryEvidenceScope;
  readonly summary: BrowserPackageQueryEvidenceSummary | null;
}

export interface BrowserPackageQueryEvidenceSummary {
  readonly count: number;
  readonly preview: ReadonlyArray<string>;
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
  readonly manifestFailureReason: BrowserPackageQueryManifestFailureReason | null;
}

export interface BrowserPackageQueryInspection {
  readonly content: ReadonlyArray<BrowserPackageQueryEvent>;
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
  readonly tier: BrowserPackageQueryFacetTier;
  readonly evidence: ReadonlyArray<BrowserPackageQueryEvidence>;
  readonly totalDownloads: number | null;
  readonly verified: boolean | null;
  readonly producer: string;
  readonly description: string | null;
  readonly rootRequest: string | null;
  readonly owners: ReadonlyArray<string>;
  readonly manifest: BrowserPackageQueryManifest | null;
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

export type BrowserAssemblyReferenceResult = BrowserAssemblyReferenceList | string | null;

type $ManagedExports = {
  readonly "DotnetInspect": {
    readonly "Web": {
      readonly "Interop": {
        readonly "Package": {
          readonly "PackageExports": {
            readonly "ActivateWorkspacePackageOccurrence.976702342": (action: string) => Promise<string>;
            readonly "CancelPackageChanges.271973316": (operationId: string, reason: string) => string;
            readonly "CancelPackageQuery.271973316": (operationId: string, reason: string) => string;
            readonly "ClearWorkspacePackageOccurrences.1731052262": () => Promise<void>;
            readonly "GetPackageDocument.1001223652": (packageId: string, version: string, path: string) => Promise<string>;
            readonly "GetPlatformCatalog.451505237": (targetFramework: string, platformVersion: string) => Promise<string>;
            readonly "GetPlatformVersions.976702342": (targetFramework: string) => Promise<string>;
            readonly "ListPackageAssemblyQueryPatterns.1310674786": () => string;
            readonly "ListPackageQueryFacets.1310674786": () => string;
            readonly "LoadRuntimePack.451505237": (targetFramework: string, platformVersion: string) => Promise<string>;
            readonly "LoadRuntimePackAssembly.1330709314": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, assetFileName: string) => Promise<string>;
            readonly "MatchPackageDependencyCoordinate.1537767637": (packageId: string, declaredRange: string | null, candidatesJson: string) => string;
            readonly "OpenPackageAssemblyQueryResult.976702342": (rootRequest: string) => Promise<string>;
            readonly "PackageCacheStats.1310674786": () => string;
            readonly "PrefetchPlatformPacks.1782598084": (targetFramework: string, platformVersion: string) => Promise<void>;
            readonly "QueryMemberDocumentation.1330709314": (packageId: string, version: string, framework: string, assemblyName: string, documentationId: string) => Promise<string>;
            readonly "QueryPackage.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
            readonly "QueryPackageDependencies.1579276339": (packageId: string, version: string, targetFramework: string, assemblyId: string) => Promise<string>;
            readonly "QueryPackageVersions.451505237": (packageId: string, currentVersion: string) => Promise<string>;
            readonly "QueryWorkspacePackageOccurrences.976702342": (workspaceJson: string) => Promise<string>;
            readonly "RequestPackageQueryMatches.146925470": (operationId: string, additionalMatchCredit: number) => string;
            readonly "ResolvePackageDependencyVersion.451505237": (packageId: string, declaredRange: string | null) => Promise<string>;
            readonly "RunPackageAssemblyQuery.990719355": (operationId: string, patternId: string, operand: string, packageCoordinatesJson: string, targetFramework: string, initialMatchCredit: number, eventSink: unknown) => Promise<string>;
            readonly "RunPackageChanges.1791926993": (operationId: string, requestJson: string, eventSink: unknown) => Promise<string>;
            readonly "RunPackageQuery.52840355": (operationId: string, prefix: string, facetIdsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, initialMatchCredit: number, eventSink: unknown) => Promise<string>;
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
    value = $ownDataProperty(value, "CancelPackageChanges.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.CancelPackageChanges.271973316\u0027 is not callable.");
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
    value = $ownDataProperty(value, "ListPackageAssemblyQueryPatterns.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ListPackageAssemblyQueryPatterns.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "ListPackageQueryFacets.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ListPackageQueryFacets.1310674786\u0027 is not callable.");
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
    value = $ownDataProperty(value, "OpenPackageAssemblyQueryResult.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.OpenPackageAssemblyQueryResult.976702342\u0027 is not callable.");
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
    value = $ownDataProperty(value, "RunPackageAssemblyQuery.990719355");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RunPackageAssemblyQuery.990719355\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "DotnetInspect");
    value = $ownDataProperty(value, "Web");
    value = $ownDataProperty(value, "Interop");
    value = $ownDataProperty(value, "Package");
    value = $ownDataProperty(value, "PackageExports");
    value = $ownDataProperty(value, "RunPackageChanges.1791926993");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RunPackageChanges.1791926993\u0027 is not callable.");
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

export async function activateWorkspacePackageOccurrence(action: string): Promise<BrowserWorkspacePackageOccurrenceActivation> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ActivateWorkspacePackageOccurrence.976702342"](action);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserWorkspacePackageOccurrenceActivation;
}

export function cancelPackageChanges(operationId: string, reason: string): BrowserPackageChangesCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["CancelPackageChanges.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageChangesCancellation;
}

export function cancelPackageQuery(operationId: string, reason: string): BrowserPackageQueryCancellation {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["CancelPackageQuery.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryCancellation;
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

export function listPackageAssemblyQueryPatterns(): ReadonlyArray<BrowserPackageAssemblyQueryPattern> {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ListPackageAssemblyQueryPatterns.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<BrowserPackageAssemblyQueryPattern>;
}

export function listPackageQueryFacets(): BrowserPackageQueryFacetCatalog {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ListPackageQueryFacets.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryFacetCatalog;
}

export async function loadRuntimePack(targetFramework: string, platformVersion: string): Promise<string> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["LoadRuntimePack.451505237"](targetFramework, platformVersion);
}

export async function loadRuntimePackAssembly(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, assetFileName: string): Promise<string> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["LoadRuntimePackAssembly.1330709314"](targetFramework, platformVersion, assemblyFileName, pack, assetFileName);
}

export function matchPackageDependencyCoordinate(packageId: string, declaredRange: string | null, candidatesJson: string): BrowserDependencyCoordinateMatch {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["MatchPackageDependencyCoordinate.1537767637"](packageId, declaredRange, candidatesJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserDependencyCoordinateMatch;
}

export async function openPackageAssemblyQueryResult(rootRequest: string): Promise<BrowserPackageSurface> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["OpenPackageAssemblyQueryResult.976702342"](rootRequest);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageSurface;
}

export function packageCacheStats(): BrowserPackageCacheStats {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["PackageCacheStats.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageCacheStats;
}

export async function prefetchPlatformPacks(targetFramework: string, platformVersion: string): Promise<void> {
  return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["PrefetchPlatformPacks.1782598084"](targetFramework, platformVersion);
}

export async function queryMemberDocumentation(packageId: string, version: string, framework: string, assemblyName: string, documentationId: string): Promise<BrowserMemberDocumentation> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryMemberDocumentation.1330709314"](packageId, version, framework, assemblyName, documentationId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberDocumentation;
}

export async function queryPackage(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageSurface> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackage.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageSurface;
}

export async function queryPackageDependencies(packageId: string, version: string, targetFramework: string, assemblyId: string): Promise<BrowserPackageDependencies> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackageDependencies.1579276339"](packageId, version, targetFramework, assemblyId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageDependencies;
}

export async function queryPackageVersions(packageId: string, currentVersion: string): Promise<BrowserPackageVersions> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackageVersions.451505237"](packageId, currentVersion);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageVersions;
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

export async function runPackageAssemblyQuery(operationId: string, patternId: string, operand: string, packageCoordinatesJson: string, targetFramework: string, initialMatchCredit: number, eventSink: unknown): Promise<BrowserPackageQueryResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageAssemblyQuery.990719355"](operationId, patternId, operand, packageCoordinatesJson, targetFramework, initialMatchCredit, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryResult;
}

export async function runPackageChanges(operationId: string, requestJson: string, eventSink: unknown): Promise<BrowserPackageChangesResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageChanges.1791926993"](operationId, requestJson, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageChangesResult;
}

export async function runPackageQuery(operationId: string, prefix: string, facetIdsJson: string, maximumCandidates: number, maximumMatches: number, includePrerelease: boolean, initialMatchCredit: number, eventSink: unknown): Promise<BrowserPackageQueryResult> {
  const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageQuery.52840355"](operationId, prefix, facetIdsJson, maximumCandidates, maximumMatches, includePrerelease, initialMatchCredit, eventSink);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageQueryResult;
}

export function searchTypes(query: string, candidatesJson: string): ReadonlyArray<BrowserTypeSearchHit> {
  const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["SearchTypes.271973316"](query, candidatesJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as ReadonlyArray<BrowserTypeSearchHit>;
}

