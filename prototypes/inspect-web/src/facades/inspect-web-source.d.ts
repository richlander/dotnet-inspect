export type BrowserAnnotatedSourceCapabilityUnavailableReason = "NotProjected" | "ContextUnavailable" | number;
export type BrowserAnnotatedSourceMedium = "CSharp" | "Il" | number;
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
export declare function initializeRuntime(): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function cancelSourceQuery(): void;
export declare function queryMemberAnnotatedSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserAnnotatedSource>;
export declare function queryMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource>;
export declare function queryTypeMemberSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number, styleOptionsJson: string): Promise<BrowserSource>;
export declare function queryTypeSource(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, styleOptionsJson: string): Promise<BrowserSource>;
