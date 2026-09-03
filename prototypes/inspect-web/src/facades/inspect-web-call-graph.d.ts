export interface BrowserCallGraph {
    readonly mermaid: string;
    readonly callers: BrowserCallGraphNode;
    readonly callees: BrowserCallGraphNode;
    readonly scope: BrowserCallGraphScope;
    readonly targets: ReadonlyArray<BrowserCallGraphTarget>;
    readonly diagnostics: BrowserCallGraphDiagnostics;
    readonly noBody: boolean;
}
export interface BrowserCallGraphDiagnostics {
    readonly incompleteNodes: number;
    readonly incompleteEdges: number;
    readonly bindingIdentityConflicts: number;
    readonly hasUnexploredTraversalBoundary: boolean;
    readonly hasAnalysisFailureBoundary: boolean;
    readonly isIncomplete: boolean;
}
export interface BrowserCallGraphNode {
    readonly label: string;
    readonly status: string;
    readonly inLoop: boolean;
    readonly source: string | null;
    readonly children: ReadonlyArray<BrowserCallGraphNode>;
    readonly assembly: string;
    readonly typeFullName: string;
    readonly memberName: string;
}
export interface BrowserCallGraphScope {
    readonly packages: number;
    readonly assemblies: number;
    readonly callerAssemblies: number;
    readonly calleeScope: string;
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
export declare function initializeRuntime(): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function expandPlatformCallGraph(targetFramework: string, platformVersion: string, assembly: string, pack: string, assemblyVersion: string, assemblyCulture: string | null, assemblyPublicKeyToken: string | null, typeFullName: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserCallGraph>;
export declare function queryMemberCallGraph(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, workspaceJson: string): Promise<BrowserCallGraph>;
