export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;
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
export interface JsExportRuntime {
    readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
    readonly runMain: (mainAssemblyName?: string, args?: string[]) => Promise<number>;
}
export declare function createRuntime(): Promise<JsExportRuntime>;
export declare function initializeRuntime(runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function queryMemberFacts(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, implementationBodySelected: boolean): Promise<BrowserMemberFacts>;
export declare function queryPackageIntegrations(packageId: string, version: string, targetFramework: string, assemblyName: string): Promise<BrowserPackageIntegrations>;
export declare function queryPackageOpportunities(packageId: string, version: string, targetFramework: string, assemblyName: string): Promise<BrowserPackageOpportunities>;
export declare function queryPackagePerformance(packageId: string, version: string, targetFramework: string, assemblyName: string): Promise<BrowserPackagePerformance>;
export declare function queryPlatformIntegrations(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageIntegrations>;
export declare function queryPlatformOpportunities(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageOpportunities>;
export declare function queryPlatformPerformance(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<string>;
