export type BrowserAnnotatedSourceCapabilityUnavailableReason = "NotProjected" | "ContextUnavailable" | number;
export type BrowserAnnotatedSourceMedium = "CSharp" | "Il" | number;
export type BrowserMethodBodyResultKind = "Succeeded" | "Failed" | "Canceled" | number;
export type BrowserSourceComparisonResultKind = "Succeeded" | "Failed" | "Canceled" | number;
export type BrowserTypeSourceCancellationKind = "Requested" | "AlreadyRequested" | "NotActive" | number;
export type BrowserTypeSourceFailureKind = "Expected" | "Unexpected" | number;
export type BrowserTypeSourceResultKind = "Succeeded" | "Failed" | "Canceled" | number;
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
    readonly provenance: string;
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
    readonly lines: ReadonlyArray<BrowserSourceComparisonLine>;
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
    readonly sourceUrl: string | null;
    readonly repositoryUrl: string | null;
    readonly revision: string | null;
}
export interface BrowserSourceComparisonLine {
    readonly kind: string;
    readonly difference: string;
    readonly beforeLine: number | null;
    readonly beforeText: string | null;
    readonly afterLine: number | null;
    readonly afterText: string | null;
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
}
export interface BrowserTypeSourceCancellation {
    readonly kind: BrowserTypeSourceCancellationKind;
    readonly reason: string | null;
}
export interface BrowserTypeSourceResult {
    readonly version: number;
    readonly kind: BrowserTypeSourceResultKind;
    readonly value: BrowserSource | null;
    readonly failureKind: BrowserTypeSourceFailureKind | null;
    readonly error: string | null;
    readonly diagnostic: string | null;
    readonly reason: string | null;
}
export interface JsExportRuntime {
    readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
    readonly runMain: (mainAssemblyName?: string, args?: string[]) => Promise<number>;
}
export declare function createRuntime(): Promise<JsExportRuntime>;
export declare function initializeRuntime(runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function cancelMemberSourceComparison(operationId: string, reason: string): BrowserTypeSourceCancellation;
export declare function cancelMethodBodyComparison(operationId: string, reason: string): BrowserTypeSourceCancellation;
export declare function cancelSourceQuery(): void;
export declare function cancelTypeSourceQuery(operationId: string, reason: string): BrowserTypeSourceCancellation;
export declare function queryMemberAnnotatedSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserAnnotatedSource>;
export declare function queryMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource>;
export declare function queryMemberSourceComparison(operationId: string, requestJson: string): Promise<BrowserSourceComparisonResult>;
export declare function queryMethodBodyComparison(operationId: string, requestJson: string): Promise<BrowserMethodBodyComparisonResult>;
export declare function queryMethodBodyComparisonTargets(operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserMethodBodyTargetsResult>;
export declare function queryTypeMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource>;
export declare function queryTypeSource(operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string): Promise<BrowserTypeSourceResult>;
