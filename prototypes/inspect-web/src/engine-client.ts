type HostFacade = typeof import("./facades/inspect-web-host.d.ts");
type PackageFacade = typeof import("./facades/inspect-web-package.d.ts");
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
  | "activateWorkspacePackageOccurrence"
  | "clearWorkspacePackageOccurrences"
  | "getPackageDocument"
  | "listGalleryDiscoveryCatalog"
  | "listPackageAssemblyQueryPatterns"
  | "listPackageQueryFacets"
  | "loadRuntimePack"
  | "loadRuntimePackAssembly"
  | "matchPackageDependencyCoordinate"
  | "openPackageAssemblyQueryResult"
  | "packageCacheStats"
  | "queryMemberDocumentation"
  | "queryPackage"
  | "queryPackageDependencies"
  | "queryPackageVersions"
  | "queryWorkspacePackageOccurrences"
  | "resolvePackageDependencyVersion"
  | "runPackageAssemblyQuery"
  | "runPackageQuery"
  | "searchTypes";

type MetadataOperations =
  | "queryGraphMemberSurface"
  | "queryPackageHeapEntries"
  | "queryPackageMetadata"
  | "queryPackageMetadataTable"
  | "queryPlatformHeapEntries"
  | "queryPlatformMetadata"
  | "queryPlatformMetadataTable"
  | "queryTypeProjection";

type AnalysisOperations =
  | "queryMemberFacts"
  | "queryPackageIntegrations"
  | "queryPackageOpportunities"
  | "queryPackagePerformance"
  | "queryPlatformIntegrations"
  | "queryPlatformOpportunities"
  | "queryPlatformPerformance";

type SourceOperations =
  | "cancelMemberSourceComparison"
  | "cancelMethodBodyComparison"
  | "cancelSourceQuery"
  | "queryMemberFindingCensus"
  | "queryMemberSource"
  | "queryMemberSourceComparison"
  | "queryMethodBodyComparison"
  | "queryMethodBodyComparisonTargets"
  | "queryTypeMemberSource"
  | "queryTypeSource";

type CallGraphOperations =
  | "expandPlatformCallGraph"
  | "queryMemberCallGraph";

type CatalogOperations =
  | "decodeWorkspaceShareState"
  | "encodeWorkspaceShareState"
  | "listHomeDemos"
  | "listVocabulary"
  | "resolveHomeDemo"
  | "runHomeDemo";

export interface EngineClient {
  readonly host: AsyncFacade<HostFacade, "buildIdentity">;
  readonly package: AsyncFacade<PackageFacade, PackageOperations> & {
    cancelPackageQuery(
      ...args: Parameters<PackageFacade["cancelPackageQuery"]>
    ): void;
    requestPackageQueryMatches(
      ...args: Parameters<PackageFacade["requestPackageQueryMatches"]>
    ): Promise<ReturnType<PackageFacade["requestPackageQueryMatches"]>>;
  };
  readonly metadata: AsyncFacade<MetadataFacade, MetadataOperations>;
  readonly analysis: AsyncFacade<AnalysisFacade, AnalysisOperations>;
  readonly source: AsyncFacade<SourceFacade, SourceOperations> & {
    cancelTypeSourceQuery(
      ...args: Parameters<SourceFacade["cancelTypeSourceQuery"]>
    ): void;
  };
  readonly callGraph: AsyncFacade<CallGraphFacade, CallGraphOperations>;
  readonly catalog: AsyncFacade<CatalogFacade, CatalogOperations>;
}
