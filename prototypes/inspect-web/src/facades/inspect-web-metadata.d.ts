export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;
export interface BrowserAssemblyMetadata {
    readonly assembly: string;
    readonly metadataVersion: string;
    readonly metadataVersionTruncated: boolean;
    readonly kind: string;
    readonly isAssembly: boolean;
    readonly metadataSize: number;
    readonly projectedTableTotal: number;
    readonly heaps: ReadonlyArray<BrowserMetadataHeap>;
    readonly tables: ReadonlyArray<BrowserMetadataTable>;
    readonly headers: BrowserMetadataHeaders;
}
export interface BrowserCompileLibraryAvailability {
    readonly status: BrowserCompileLibraryStatus;
    readonly targetFramework: string | null;
    readonly message: string | null;
}
export interface BrowserExceptionSurface {
    readonly type: string;
    readonly description: string;
}
export interface BrowserGraphMemberSurface {
    readonly type: BrowserTypeSurface;
    readonly selectedBody: BrowserMemberBodySelector;
}
export interface BrowserHeapEntry {
    readonly offset: number;
    readonly value: BrowserMetadataCell;
    readonly referenceCount: number;
}
export interface BrowserHeapListing {
    readonly assembly: string;
    readonly heap: string;
    readonly streamName: string;
    readonly coverage: string;
    readonly entries: ReadonlyArray<BrowserHeapEntry>;
    readonly rowsTruncated: boolean;
    readonly entriesTruncated: boolean;
    readonly error: string | null;
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
    readonly returnType: string | null;
    readonly parameters: ReadonlyArray<BrowserParameterSurface>;
    readonly documentationId: string | null;
    readonly summary: string | null;
    readonly returns: string | null;
    readonly exceptions: ReadonlyArray<BrowserExceptionSurface>;
    readonly stableSelector: string;
    readonly anchorDigest: string;
    readonly canonicalSignature: string;
    readonly graphSelectorKey: string;
    readonly bodySelectors: ReadonlyArray<BrowserMemberBodySelector>;
}
export interface BrowserMetadataCell {
    readonly kind: string;
    readonly raw: number | null;
    readonly display: string | null;
    readonly decoded: string | null;
    readonly heap: string | null;
    readonly text: string | null;
    readonly preview: string | null;
    readonly offset: number | null;
    readonly length: number | null;
    readonly truncated: boolean | null;
    readonly targetTable: number | null;
    readonly targetRowId: number | null;
    readonly startRowId: number | null;
    readonly endRowId: number | null;
    readonly count: number | null;
    readonly token: number | null;
    readonly detail: string | null;
}
export interface BrowserMetadataColumn {
    readonly name: string;
    readonly kind: string;
    readonly candidateTargets: ReadonlyArray<number>;
}
export interface BrowserMetadataHeaders {
    readonly machine: string;
    readonly isPE32Plus: boolean;
    readonly subsystem: string;
    readonly corFlags: string | null;
    readonly majorRuntimeVersion: number | null;
    readonly minorRuntimeVersion: number | null;
    readonly entryPointToken: number | null;
    readonly managedNativeHeaderRva: number;
    readonly managedNativeHeaderSize: number;
}
export interface BrowserMetadataHeap {
    readonly name: string;
    readonly sizeInBytes: number;
    readonly maxAddress: number;
    readonly addressing: string;
}
export interface BrowserMetadataRow {
    readonly rowId: number;
    readonly token: number;
    readonly cells: ReadonlyArray<BrowserMetadataCell>;
}
export interface BrowserMetadataTable {
    readonly index: number;
    readonly name: string;
    readonly rowCount: number;
    readonly isProjected: boolean;
}
export interface BrowserMetadataWindow {
    readonly assembly: string;
    readonly index: number;
    readonly name: string;
    readonly rowCount: number;
    readonly startRowId: number;
    readonly columns: ReadonlyArray<BrowserMetadataColumn>;
    readonly rows: ReadonlyArray<BrowserMetadataRow>;
    readonly truncated: boolean;
    readonly error: string | null;
}
export interface BrowserPackageMetadata {
    readonly assemblies: ReadonlyArray<BrowserAssemblyMetadata>;
    readonly inspectionError: string | null;
    readonly compileLibrary: BrowserCompileLibraryAvailability;
}
export interface BrowserParameterSurface {
    readonly name: string;
    readonly type: string;
    readonly modifier: string | null;
    readonly hasDefault: boolean;
    readonly defaultValue: string | null;
    readonly description: string | null;
}
export interface BrowserTypeComposition {
    readonly methods: number;
    readonly properties: number;
    readonly fields: number;
    readonly events: number;
    readonly constructors: number;
    readonly operators: number;
    readonly explicitInterfaceImplementations: number;
    readonly extensionMethods: number;
    readonly static: number;
    readonly unsafe: number;
    readonly async: number;
    readonly virtual: number;
    readonly abstract: number;
    readonly override: number;
    readonly extension: number;
    readonly obsolete: number;
    readonly total: number;
}
export interface BrowserTypeGraphEdge {
    readonly fromId: string;
    readonly toId: string;
    readonly kind: string;
}
export interface BrowserTypeGraphNode {
    readonly id: string;
    readonly displayName: string;
    readonly role: string;
}
export interface BrowserTypeMetadata {
    readonly fullName: string;
    readonly namespace: string | null;
    readonly name: string;
    readonly kind: string;
    readonly modifiers: ReadonlyArray<string>;
    readonly accessibility: string | null;
    readonly assembly: string | null;
    readonly baseType: string | null;
    readonly interfaces: ReadonlyArray<string>;
    readonly derivedTypes: ReadonlyArray<string>;
    readonly typeParameters: ReadonlyArray<BrowserTypeParameter>;
    readonly attributes: ReadonlyArray<string>;
    readonly enumUnderlyingType: string | null;
    readonly composition: BrowserTypeComposition | null;
    readonly graphNodes: ReadonlyArray<BrowserTypeGraphNode>;
    readonly graphEdges: ReadonlyArray<BrowserTypeGraphEdge>;
    readonly inspectionFailures: ReadonlyArray<string>;
}
export interface BrowserTypeParameter {
    readonly name: string;
    readonly variance: string | null;
    readonly constraints: ReadonlyArray<string>;
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
export interface JsExportRuntime {
    readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
    readonly runMain: (mainAssemblyName?: string, args?: string[]) => Promise<number>;
}
export declare function createRuntime(): Promise<JsExportRuntime>;
export declare function initializeRuntime(runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>): Promise<void>;
export declare function runEntryPoint(mainAssemblyName?: string, args?: string[]): Promise<number>;
export declare function queryGraphMemberSurface(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserGraphMemberSurface>;
export declare function queryPackageHeapEntries(packageId: string, version: string, targetFramework: string, assemblyFileName: string, heap: string): Promise<BrowserHeapListing>;
export declare function queryPackageMetadata(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageMetadata>;
export declare function queryPackageMetadataTable(packageId: string, version: string, targetFramework: string, assemblyFileName: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow>;
export declare function queryPlatformHeapEntries(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, heap: string): Promise<BrowserHeapListing>;
export declare function queryPlatformMetadata(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageMetadata>;
export declare function queryPlatformMetadataTable(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string, tableIndex: number, startRowId: number, maxRows: number): Promise<BrowserMetadataWindow>;
export declare function queryTypeProjection(packageId: string, version: string, targetFramework: string, assemblyName: string, typeId: string): Promise<BrowserTypeMetadata>;
