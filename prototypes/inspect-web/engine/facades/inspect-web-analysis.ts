import { dotnet } from "./runtime-loader.js";

export type BrowserCloneCandidateAnalysisBlockerKind = "MetadataReadFailure" | "MethodLimit" | "SeedUnsupported" | "SeedProductionLimit" | "SeedProductionFailure" | "CandidateProductionLimit" | "CandidateProductionFailure" | number;

export type BrowserCloneCandidateBreadth = "Everything" | "Self" | "SelfAndRegisteredEcosystems" | number;

export type BrowserCloneCandidateDiscovery = "SimilarNames" | "All" | number;

export type BrowserCloneCandidateFailureKind = "SeedTypeNotFound" | "SeedTypeAmbiguous" | "SeedMemberNotFound" | "SeedMemberAmbiguous" | "SeedMemberHasNoMethodBody" | "SeedPopulationLimitReached" | "CandidatePopulationLimitReached" | "ParticipantPopulationLimitReached" | "RetrievalWorkLimitReached" | "CandidateLibraryUnavailable" | "SeedLibraryReleased" | "CandidateLibraryReleased" | "MetadataInspectionFailed" | "NameDecodeFailed" | "NameWorkLimitReached" | number;

export type BrowserCloneCandidateOpenFailureKind = "Unreadable" | "InvalidImage" | "ResourceBudget" | "UnsupportedMetadataFormat" | number;

export type BrowserCloneCandidateParticipantMembership = "ContainingLibrary" | "RegisteredEcosystem" | "Available" | number;

export type BrowserCloneCandidatePresentationRejectionKind = "ParticipantCoverageMissing" | "ParticipantModuleInconsistent" | number;

export type BrowserCloneCandidateProvenanceKind = "Package" | "Platform" | "Project" | "Local" | "Designated" | "Embedded" | number;

export type BrowserCloneCandidateResultKind = "Available" | "Rejected" | "Failed" | "Unrepresentable" | number;

export type BrowserCloneCandidateRetrievalDisposition = "Completed" | "Unsupported" | "LimitReached" | "Failed" | number;

export type BrowserCloneCandidateSeedKind = "Library" | "Type" | "Member" | number;

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export type BrowserMetadataRootMalformedReason = "UnmappableMetadataDirectory" | "TruncatedFixedPrefix" | "InvalidSignature" | "InvalidVersionLength" | "TruncatedVersionField" | "MissingVersionTerminator" | number;

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

export interface BrowserCallFact {
  readonly callee: string;
  readonly offset: string;
  readonly opcode: string;
  readonly kind: string;
  readonly multiplicity: string;
  readonly inLoop: boolean;
}

export interface BrowserCloneAssemblyIdentity {
  readonly name: string;
  readonly version: string | null;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
}

export interface BrowserCloneCandidateAnalysisBlocker {
  readonly kind: BrowserCloneCandidateAnalysisBlockerKind;
  readonly detail: string;
}

export interface BrowserCloneCandidateBodySelection {
  readonly memberName: string;
  readonly selectorKey: string;
  readonly metadataToken: number;
}

export interface BrowserCloneCandidateDocument {
  readonly schemaVersion: number;
  readonly seed: BrowserCloneCandidateSeed;
  readonly breadth: BrowserCloneCandidateBreadth;
  readonly discovery: BrowserCloneCandidateDiscovery;
  readonly nameSimilarityThreshold: number;
  readonly limits: BrowserCloneCandidateLimits;
  readonly scopeChangedDuringSearch: boolean;
  readonly coverageIsComplete: boolean;
  readonly rows: ReadonlyArray<BrowserCloneCandidateRow>;
  readonly seeds: ReadonlyArray<BrowserCloneCandidateSeedCoverage>;
  readonly libraries: ReadonlyArray<BrowserCloneCandidateLibraryCoverage>;
  readonly receipt: BrowserCloneCandidateReceipt;
  readonly resultLimitReached: boolean;
  readonly resultLimitOmittedPairs: number;
}

export interface BrowserCloneCandidateFailure {
  readonly kind: BrowserCloneCandidateFailureKind;
  readonly subject: BrowserCloneAssemblyIdentity | null;
  readonly detail: string;
}

export interface BrowserCloneCandidateLibraryCoverage {
  readonly participant: BrowserCloneCandidateParticipant;
  readonly membership: BrowserCloneCandidateParticipantMembership;
  readonly admitted: boolean;
  readonly candidateMethods: number;
  readonly discoveredMethods: number;
  readonly retrievalPairs: number;
  readonly nameComparisonWork: number;
  readonly failures: ReadonlyArray<BrowserCloneCandidateFailure>;
  readonly analysisBlockers: ReadonlyArray<BrowserCloneCandidateAnalysisBlocker>;
  readonly isComplete: boolean;
}

export interface BrowserCloneCandidateLimits {
  readonly maximumResults: number;
  readonly maximumSeedMethods: number;
  readonly maximumCandidateMethods: number;
  readonly maximumParticipants: number;
  readonly maximumRetrievalPairs: number;
  readonly maximumRetrievalChunkMethods: number;
  readonly maximumNameCharacters: number;
  readonly maximumNameComparisonWork: number;
  readonly maximumNameCacheCells: number;
  readonly comparisonLimits: BrowserCloneComparisonLimits | null;
}

export interface BrowserCloneCandidateMethod {
  readonly participant: BrowserCloneCandidateParticipant;
  readonly moduleVersionId: string;
  readonly methodDefinitionToken: number;
  readonly addressDisplay: string;
}

export interface BrowserCloneCandidateNameQualification {
  readonly declaringTypeSimilarity: number;
  readonly memberSimilarity: number;
}

export interface BrowserCloneCandidatePackage {
  readonly packageId: string;
  readonly version: string;
  readonly targetFramework: string;
}

export interface BrowserCloneCandidateParticipant {
  readonly ordinal: number;
  readonly assembly: BrowserCloneAssemblyIdentity;
  readonly provenance: BrowserCloneCandidateProvenance;
  readonly moduleVersionId: string | null;
}

export interface BrowserCloneCandidateProvenance {
  readonly kind: BrowserCloneCandidateProvenanceKind;
  readonly packageId: string | null;
  readonly packageVersion: string | null;
  readonly tfm: string | null;
  readonly rid: string | null;
  readonly framework: string | null;
  readonly frameworkVersion: string | null;
  readonly project: string | null;
  readonly resolverSource: string | null;
  readonly contentRef: string | null;
  readonly digest: string | null;
  readonly declaredName: string | null;
}

export interface BrowserCloneCandidateReceipt {
  readonly seedMethods: number;
  readonly candidateMethods: number;
  readonly discoveredMethods: number;
  readonly admittedLibraries: number;
  readonly excludedLibraries: number;
  readonly nameComparisonWork: number;
  readonly retrievalPairs: number;
  readonly retrievalCalls: number;
  readonly rankedPairs: number;
  readonly suppressedPairs: number;
  readonly returnedPairs: number;
  readonly resultLimitReached: boolean;
}

export interface BrowserCloneCandidateRequest {
  readonly schemaVersion: number;
  readonly packages: ReadonlyArray<BrowserCloneCandidatePackage>;
  readonly selectedPackageIndex: number;
  readonly assembly: string;
  readonly seed: BrowserCloneCandidateSeedRequest;
  readonly breadth: BrowserCloneCandidateBreadth;
  readonly discovery: BrowserCloneCandidateDiscovery;
}

export interface BrowserCloneCandidateResult {
  readonly schemaVersion: number;
  readonly request: BrowserCloneCandidateRequest;
  readonly kind: BrowserCloneCandidateResultKind;
  readonly document: BrowserCloneCandidateDocument | null;
  readonly seedLibrary: BrowserCloneAssemblyIdentity | null;
  readonly openFailureKind: BrowserCloneCandidateOpenFailureKind | null;
  readonly failure: BrowserCloneCandidateFailure | null;
  readonly presentationRejectionKind: BrowserCloneCandidatePresentationRejectionKind | null;
  readonly subject: BrowserCloneAssemblyIdentity | null;
  readonly detail: string | null;
  readonly metadataRootReason: BrowserMetadataRootMalformedReason | null;
}

export interface BrowserCloneCandidateRow {
  readonly rank: number;
  readonly left: BrowserCloneCandidateMethod;
  readonly right: BrowserCloneCandidateMethod;
  readonly similarity: BrowserCloneCandidateSimilarity;
  readonly nameQualification: BrowserCloneCandidateNameQualification | null;
}

export interface BrowserCloneCandidateSeed {
  readonly kind: BrowserCloneCandidateSeedKind;
  readonly type: BrowserMetadataTypeDefinitionName | null;
  readonly member: BrowserCloneMemberAnchor | null;
}

export interface BrowserCloneCandidateSeedCoverage {
  readonly seed: BrowserCloneCandidateMethod;
  readonly disposition: BrowserCloneCandidateRetrievalDisposition;
  readonly rankedPairs: number;
  readonly suppressedPairs: number;
  readonly blockers: ReadonlyArray<BrowserCloneCandidateAnalysisBlocker>;
  readonly failures: ReadonlyArray<BrowserCloneCandidateFailure>;
  readonly isComplete: boolean;
}

export interface BrowserCloneCandidateSeedRequest {
  readonly kind: BrowserCloneCandidateSeedKind;
  readonly typeDefinitionId: string | null;
  readonly member: BrowserCloneMemberAnchor | null;
  readonly body: BrowserCloneCandidateBodySelection | null;
}

export interface BrowserCloneCandidateSimilarity {
  readonly score: number;
  readonly operationScore: number;
  readonly positionScore: number;
  readonly blockScore: number;
  readonly edgeScore: number;
  readonly localScore: number;
  readonly seedInstructions: number;
  readonly candidateInstructions: number;
  readonly seedBlocks: number;
  readonly candidateBlocks: number;
  readonly seedEdges: number;
  readonly candidateEdges: number;
  readonly seedLocals: number;
  readonly candidateLocals: number;
}

export interface BrowserCloneComparisonLimits {
  readonly maximumInstructions: number;
  readonly maximumBlocks: number;
  readonly maximumEdges: number;
  readonly maximumLocals: number;
  readonly maximumVerificationSteps: number;
  readonly maximumBodyBytes: number;
  readonly maximumNearAlignmentIndexSteps: number;
  readonly maximumNearAlignmentCandidates: number;
  readonly maximumNearAlignmentVerificationSteps: number;
  readonly maximumNearAlignmentAlternatives: number;
  readonly maximumNearBlockElements: number;
}

export interface BrowserCloneMemberAnchor {
  readonly stableSelector: string;
  readonly canonicalSignature: string;
  readonly fingerprint: string;
  readonly typeFullName: string;
  readonly memberName: string;
}

export interface BrowserCompileLibraryAvailability {
  readonly status: BrowserCompileLibraryStatus;
  readonly targetFramework: string | null;
  readonly message: string | null;
}

export interface BrowserExceptionRegion {
  readonly region: number;
  readonly clause: string;
  readonly tryRange: string;
  readonly handlerRange: string;
  readonly filterRange: string | null;
  readonly caughtType: string | null;
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

export interface BrowserMetadataTypeDefinitionName {
  readonly namespace: string;
  readonly segments: ReadonlyArray<string>;
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

type $ManagedExports = {
  readonly "AnalysisExports": {
    readonly "QueryCloneCandidates.976702342": (requestJson: string) => Promise<string>;
    readonly "QueryMemberFacts.581406856": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, implementationBodySelected: boolean) => Promise<string>;
    readonly "QueryPackageIntegrations.1579276339": (packageId: string, version: string, targetFramework: string, assemblyName: string) => Promise<string>;
    readonly "QueryPackageOpportunities.1579276339": (packageId: string, version: string, targetFramework: string, assemblyName: string) => Promise<string>;
    readonly "QueryPackagePerformance.1579276339": (packageId: string, version: string, targetFramework: string, assemblyName: string) => Promise<string>;
    readonly "QueryPlatformIntegrations.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformOpportunities.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformPerformance.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
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
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryCloneCandidates.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryCloneCandidates.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryMemberFacts.581406856");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryMemberFacts.581406856\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPackageIntegrations.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPackageIntegrations.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPackageOpportunities.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPackageOpportunities.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPackagePerformance.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPackagePerformance.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPlatformIntegrations.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPlatformIntegrations.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPlatformOpportunities.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPlatformOpportunities.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPlatformPerformance.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPlatformPerformance.1579276339\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine.AnalysisExports");
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

export async function queryCloneCandidates(requestJson: string): Promise<BrowserCloneCandidateResult> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryCloneCandidates.976702342"](requestJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserCloneCandidateResult;
}

export async function queryMemberFacts(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, implementationBodySelected: boolean): Promise<BrowserMemberFacts> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryMemberFacts.581406856"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, memberSignature, selectorKey, metadataToken, implementationBodySelected);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberFacts;
}

export async function queryPackageIntegrations(packageId: string, version: string, targetFramework: string, assemblyName: string): Promise<BrowserPackageIntegrations> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackageIntegrations.1579276339"](packageId, version, targetFramework, assemblyName);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageIntegrations;
}

export async function queryPackageOpportunities(packageId: string, version: string, targetFramework: string, assemblyName: string): Promise<BrowserPackageOpportunities> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackageOpportunities.1579276339"](packageId, version, targetFramework, assemblyName);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageOpportunities;
}

export async function queryPackagePerformance(packageId: string, version: string, targetFramework: string, assemblyName: string): Promise<BrowserPackagePerformance> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackagePerformance.1579276339"](packageId, version, targetFramework, assemblyName);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackagePerformance;
}

export async function queryPlatformIntegrations(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageIntegrations> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPlatformIntegrations.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageIntegrations;
}

export async function queryPlatformOpportunities(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageOpportunities> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPlatformOpportunities.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageOpportunities;
}

export async function queryPlatformPerformance(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<string> {
  return await $requireManagedExports()["AnalysisExports"]["QueryPlatformPerformance.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}

