using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public static partial class GraphLibrariesQuery
{
    public const string QuerySpaceIdentity =
        "graph-libraries/query-space/v1";
    public const string ConsumerUseSitesRowScopeIdentity =
        "graph-libraries/consumer-use-sites/v1";
    public const string ProviderApiTypesRowScopeIdentity =
        "graph-libraries/provider-api-types/v1";
    public const string DirectUseClustersRowScopeIdentity =
        "graph-libraries/direct-use-clusters/v1";
    public const string CallSitesRowScopeIdentity =
        "graph-libraries/call-sites/v1";
    public const string PublicRootPathsRowScopeIdentity =
        "graph-libraries/public-root-paths/v1";

    public static QuerySpaceRowScopeBinding<
        AssemblyPairCallUseConsumerUseSite> ConsumerUseSitesRowScope
    { get; } =
        CreateSelectableRowScope<AssemblyPairCallUseConsumerUseSite>(
            ConsumerUseSitesRowScopeIdentity,
            ConsumerUseSitesRowSet);

    public static QuerySpaceRowScopeBinding<
        AssemblyPairCallUseProviderApiType> ProviderApiTypesRowScope
    { get; } =
        CreateSelectableRowScope<AssemblyPairCallUseProviderApiType>(
            ProviderApiTypesRowScopeIdentity,
            ProviderApiTypesRowSet);

    public static QuerySpaceRowScopeBinding<
        AssemblyPairDirectUseCluster> DirectUseClustersRowScope
    { get; } =
        CreateSelectableRowScope<AssemblyPairDirectUseCluster>(
            DirectUseClustersRowScopeIdentity,
            DirectUseClustersRowSet);

    public static QuerySpaceRowScopeBinding<
        AssemblyPairCallUseOccurrence> CallSitesRowScope
    { get; } =
        CreateSelectableRowScope<AssemblyPairCallUseOccurrence>(
            CallSitesRowScopeIdentity,
            CallSitesRowSet);

    public static QuerySpaceRowScopeBinding<
        AssemblyPairClusterRootPathResult> PublicRootPathsRowScope
    { get; } =
        CreateRowScope<AssemblyPairClusterRootPathResult>(
            PublicRootPathsRowScopeIdentity,
            PublicRootPathsRowSet,
            []);

    private static readonly Lazy<QuerySpaceBinding> QuerySpaceValue =
        new(CreateQuerySpace);

    public static QuerySpaceBinding QuerySpace => QuerySpaceValue.Value;

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRoute,
            [
                ConsumerUseSitesRowScope,
                ProviderApiTypesRowScope,
                DirectUseClustersRowScope,
                CallSitesRowScope,
                PublicRootPathsRowScope,
            ],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "graph-libraries/rows/v1"),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    "graph-libraries/count/v1"),
            ]);

    private static QuerySpaceRowScopeBinding<TRow>
        CreateSelectableRowScope<TRow>(
            string identity,
            string rowSet) =>
        CreateRowScope<TRow>(
            identity,
            rowSet,
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ]);

    private static QuerySpaceRowScopeBinding<TRow> CreateRowScope<TRow>(
        string identity,
        string rowSet,
        IReadOnlyList<RowSelectionStageKind> stages) =>
        new(
            new QuerySpaceRowScopeDescriptor(
                identity,
                "graph-libraries/rows/v1",
                [rowSet],
                [],
                [],
                stages),
            RowQueryVocabulary<TRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [],
                []));
}
