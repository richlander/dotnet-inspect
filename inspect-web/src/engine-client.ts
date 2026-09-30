import type {
  BrowserSourceComparisonResult,
} from "./source-diff-transport.ts";

type HostFacade = typeof import("./facades/inspect-web-host.d.ts");
type PackageFacade = typeof import("./facades/inspect-web-package.d.ts");
type LibraryFacade = typeof import("./facades/inspect-web-library.d.ts");
type MetadataFacade = typeof import("./facades/inspect-web-metadata.d.ts");
type AnalysisFacade = typeof import("./facades/inspect-web-analysis.d.ts");
type SourceFacade = typeof import("./facades/inspect-web-source.d.ts");
type CallGraphFacade = typeof import("./facades/inspect-web-call-graph.d.ts");
type CatalogFacade = typeof import("./facades/inspect-web-catalog.d.ts");

type AsyncFacade<
  TFacade,
  TOperation extends keyof TFacade,
> = {
  readonly [TOperationName in TOperation]:
    TFacade[TOperationName] extends (...args: infer TArguments) => infer TResult
      ? (...args: TArguments) => Promise<Awaited<TResult>>
      : never;
};

type PackageOperations =
  | "activatePlatformForwarder"
  | "activateWorkspacePackageOccurrence"
  | "classifyPackageGraphIdentities"
  | "clearWorkspacePackageOccurrences"
  | "closePlatformForwarderView"
  | "getPlatformCatalog"
  | "getPlatformVersions"
  | "getPackageDocument"
  | "listPackageActivityEcosystems"
  | "listPackageQueryCatalog"
  | "loadRuntimePack"
  | "loadRuntimePackAssembly"
  | "matchPackageDependencyCoordinate"
  | "openPlatformForwarderView"
  | "packageCacheStats"
  | "prefetchPlatformPacks"
  | "queryLibraries"
  | "queryLibraryApi"
  | "queryMemberDocumentation"
  | "queryPlatformMemberDocumentation"
  | "queryPackage"
  | "queryPackageRoot"
  | "queryPackageDependencies"
  | "queryPackagePruning"
  | "queryPackageVersions"
  | "queryWorkspacePackageOccurrences"
  | "resolvePackageDependencyVersion"
  | "runPackageActivity"
  | "runPackageQuery"
  | "searchCapabilities"
  | "searchTypes";

type LibraryOperations = "inspectLibrary" | "openUploadedLibrary";

type MetadataOperations =
  | "cancelLibraryApiDiff"
  | "findTypes"
  | "queryLibraryApiDiff"
  | "queryMemberDeclaration"
  | "queryMemberGroupDocument"
  | "queryTypeMemberPopulation"
  | "queryPlatformMemberDeclaration"
  | "queryPlatformMemberGroupDocument"
  | "queryPlatformTypeMemberPopulation"
  | "queryGraphMemberSurface"
  | "queryPackageHeapEntries"
  | "queryPackageMetadata"
  | "queryPackageMetadataTable"
  | "queryPlatformHeapEntries"
  | "queryPlatformMetadata"
  | "queryPlatformMetadataTable"
  | "queryTypeProjection";

type AnalysisOperations =
  | "queryCloneCandidates"
  | "queryMemberFacts"
  | "queryPackageTypeImplementationHeat"
  | "queryPackageIntegrations"
  | "queryPackageOpportunities"
  | "queryPackagePerformance"
  | "queryPackageLibraryMetrics"
  | "queryPlatformTypeImplementationHeat"
  | "queryPlatformLibraryMetrics"
  | "queryPlatformIntegrations"
  | "queryPlatformOpportunities"
  | "queryPlatformPerformance";

type SourceOperations =
  | "cancelMemberSourceComparison"
  | "cancelMethodBodyComparison"
  | "cancelSourceQuery"
  | "queryMemberFindingCensus"
  | "queryMemberSource"
  | "queryPlatformMemberSource"
  | "queryPlatformTypeSource"
  | "queryMethodBodyComparison"
  | "queryMethodBodyComparisonTargets"
  | "queryRetainedMethodBodyComparison"
  | "queryRetainedMethodBodyComparisonTargets"
  | "queryTypeExplorer"
  | "queryTypeMemberSource"
  | "queryTypeSource";

type CallGraphOperations =
  | "expandPlatformCallGraph"
  | "queryMemberCallGraph";

type CatalogOperations =
  | "admitRetainedWorkspacePackage"
  | "admitRetainedWorkspacePlatform"
  | "abandonRetainedWorkspaceNavigation"
  | "acknowledgeRetainedWorkspaceNavigation"
  | "activateRetainedWorkspaceDefinition"
  | "activateRetainedWorkspaceDefinitionWithCredentials"
  | "activateSpotlightDestination"
  | "cancelRetainedWorkspaceActivation"
  | "captureCompleteWorkspaceShareState"
  | "canonicalizeWorkspaceSharePacket"
  | "commitRetainedWorkspaceActivation"
  | "completeRetainedWorkspaceActivation"
  | "completeRetainedWorkspaceDeactivation"
  | "deactivateRetainedWorkspaceDefinition"
  | "describeWorkspacePackageSources"
  | "decodeWorkspaceShareState"
  | "encodeWorkspaceShareState"
  | "inspectVocabulary"
  | "listHomeDemos"
  | "observeRetainedWorkspaceSettlement"
  | "preparePackageQueryWorkspaceDefinition"
  | "prepareRetainedWorkspaceDefinition"
  | "prepareRetainedWorkspaceDefinitionWithCredentials"
  | "recordRetainedWorkspaceNavigationPosting"
  | "resolveHomeDemo"
  | "runHomeDemo"
  | "validateRetainedWorkspaceNavigationAuthority";

export interface EngineClient {
  readonly host: AsyncFacade<HostFacade, "buildIdentity">;
  readonly package: AsyncFacade<PackageFacade, PackageOperations> & {
    cancelPackageActivity(
      ...args: Parameters<PackageFacade["cancelPackageActivity"]>
    ): void;
    cancelPackageQuery(
      ...args: Parameters<PackageFacade["cancelPackageQuery"]>
    ): void;
    requestPackageQueryMatches(
      ...args: Parameters<PackageFacade["requestPackageQueryMatches"]>
    ): Promise<ReturnType<PackageFacade["requestPackageQueryMatches"]>>;
  };
  readonly library: AsyncFacade<LibraryFacade, LibraryOperations>;
  readonly metadata: AsyncFacade<MetadataFacade, MetadataOperations> & {
    readonly queryUploadedLibraryMemberGroupDocument: (
      libraryIdentity: string,
      typeIdentity: string,
      memberName: string,
    ) => Promise<Awaited<ReturnType<
      MetadataFacade["queryUploadedLibraryMemberGroupDocument"]
    >>>;
    readonly queryUploadedLibraryTypeMemberPopulation: (
      libraryIdentity: string,
      typeIdentity: string,
      spelling: string,
      accessibility: string,
    ) => Promise<Awaited<ReturnType<
      MetadataFacade["queryUploadedLibraryTypeMemberPopulation"]
    >>>;
  };
  readonly analysis: AsyncFacade<AnalysisFacade, AnalysisOperations>;
  readonly source: AsyncFacade<SourceFacade, SourceOperations> & {
    queryMemberSourceComparison(
      ...args: Parameters<SourceFacade["queryMemberSourceComparison"]>
    ): Promise<BrowserSourceComparisonResult>;
    cancelTypeExplorerQuery(
      ...args: Parameters<SourceFacade["cancelTypeExplorerQuery"]>
    ): void;
    cancelTypeSourceQuery(
      ...args: Parameters<SourceFacade["cancelTypeSourceQuery"]>
    ): void;
  };
  readonly callGraph: AsyncFacade<CallGraphFacade, CallGraphOperations>;
  readonly catalog: AsyncFacade<CatalogFacade, CatalogOperations>;
  /** Outstanding ordinary-Worker requests; background work waits for idle. */
  readonly activity: {
    outstanding(): number;
    whenIdle(): Promise<void>;
  };
}
