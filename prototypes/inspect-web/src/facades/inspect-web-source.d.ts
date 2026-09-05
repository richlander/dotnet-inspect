export type BrowserAnnotatedSourceCapabilityUnavailableReason = "NotProjected" | "ContextUnavailable" | number;
export type BrowserAnnotatedSourceMedium = "CSharp" | "Il" | number;
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
export interface BrowserSource {
    readonly provider: string;
    readonly provenance: string;
    readonly url: string | null;
    readonly pdbSourceLimitation: string | null;
    readonly text: string;
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
export declare function cancelSourceQuery(): void;
export declare function cancelTypeSourceQuery(operationId: string, reason: string): BrowserTypeSourceCancellation;
export declare function queryMemberAnnotatedSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserAnnotatedSource>;
export declare function queryMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource>;
export declare function queryTypeMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource>;
export declare function queryTypeSource(operationId: string, packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string): Promise<BrowserTypeSourceResult>;
