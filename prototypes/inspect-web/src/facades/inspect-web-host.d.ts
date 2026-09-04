export interface BrowserBuildIdentity {
    readonly version: string;
    readonly commit: string | null;
    readonly builtAtUtc: string | null;
    readonly commitUrl: string | null;
}
export interface JsExportRuntime {
    readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
    readonly runMain: (mainAssemblyName?: string, args?: string[]) => Promise<number>;
}
export declare function createRuntime(): Promise<JsExportRuntime>;
export declare function initializeRuntime(runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function asyncLoweringCanary(): Promise<string>;
export declare function buildIdentity(): BrowserBuildIdentity;
export declare function configureHost(origin: string): void;
