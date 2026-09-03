export interface BrowserBuildIdentity {
    readonly version: string;
    readonly commit: string | null;
    readonly builtAtUtc: string | null;
    readonly commitUrl: string | null;
}
export declare function initializeRuntime(): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function asyncLoweringCanary(): Promise<string>;
export declare function buildIdentity(): BrowserBuildIdentity;
export declare function configureHost(origin: string): void;
