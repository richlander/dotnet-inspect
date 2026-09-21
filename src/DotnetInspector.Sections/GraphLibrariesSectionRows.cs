using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public sealed record GraphLibrariesSectionRowProjection(
    AssemblyPairCallUseProjection Summaries,
    AssemblyPairDirectUseClusterProjection Clusters,
    IReadOnlyList<AssemblyPairCallUseOccurrence> CallSites);

public abstract class GraphLibrariesSectionRowBinding
{
    private protected GraphLibrariesSectionRowBinding(
        string rowSet,
        QuerySpaceRowScopeBinding queryScope,
        SectionRowSchemaIdentity schema,
        bool requiresDirectUseClusters)
    {
        RowSet = rowSet;
        QueryScope = queryScope;
        Schema = schema;
        RequiresDirectUseClusters = requiresDirectUseClusters;
    }

    public string RowSet { get; }

    public QuerySpaceRowScopeBinding QueryScope { get; }

    public SectionRowSchemaIdentity Schema { get; }

    public bool RequiresDirectUseClusters { get; }

    public abstract QuerySpaceSectionRowResolutionResult<
        GraphLibrariesSectionRowProjection> Resolve(
            GraphLibrariesQueryPlan operation,
            RowSelectionIntent<string> rowSelection,
            QuerySpaceTerminalRequirement terminal,
            GraphLibrariesSectionRowProjection projection);
}

public static class GraphLibrariesSectionRows
{
    public static GraphLibrariesSectionRowBinding ConsumerUseSites
    { get; } =
        Create(
            GraphLibrariesQuery.ConsumerUseSitesRowSet,
            GraphLibrariesQuery.ConsumerUseSitesRowScope,
            requiresDirectUseClusters: false,
            static projection => projection.Summaries.ConsumerUseSites,
            static (projection, rows) => projection with
            {
                Summaries = projection.Summaries with
                {
                    ConsumerUseSites = [.. rows],
                },
            });

    public static GraphLibrariesSectionRowBinding ProviderApiTypes
    { get; } =
        Create(
            GraphLibrariesQuery.ProviderApiTypesRowSet,
            GraphLibrariesQuery.ProviderApiTypesRowScope,
            requiresDirectUseClusters: false,
            static projection => projection.Summaries.ProviderApiTypes,
            static (projection, rows) => projection with
            {
                Summaries = projection.Summaries with
                {
                    ProviderApiTypes = [.. rows],
                },
            });

    public static GraphLibrariesSectionRowBinding DirectUseClusters
    { get; } =
        Create(
            GraphLibrariesQuery.DirectUseClustersRowSet,
            GraphLibrariesQuery.DirectUseClustersRowScope,
            requiresDirectUseClusters: true,
            static projection => projection.Clusters.Clusters,
            static (projection, rows) => projection with
            {
                Clusters = projection.Clusters with
                {
                    Clusters = [.. rows],
                },
            });

    public static GraphLibrariesSectionRowBinding CallSites
    { get; } =
        Create(
            GraphLibrariesQuery.CallSitesRowSet,
            GraphLibrariesQuery.CallSitesRowScope,
            requiresDirectUseClusters: false,
            static projection => projection.CallSites,
            static (projection, rows) => projection with
            {
                CallSites = rows,
            });

    public static IReadOnlyList<GraphLibrariesSectionRowBinding> All
    { get; } =
    [
        ConsumerUseSites,
        ProviderApiTypes,
        DirectUseClusters,
        CallSites,
    ];

    private static GraphLibrariesSectionRowBinding Create<TRow>(
        string rowSet,
        QuerySpaceRowScopeBinding<TRow> queryScope,
        bool requiresDirectUseClusters,
        Func<
            GraphLibrariesSectionRowProjection,
            IReadOnlyList<TRow>> rows,
        Func<
            GraphLibrariesSectionRowProjection,
            IReadOnlyList<TRow>,
            GraphLibrariesSectionRowProjection> resultBinder)
    {
        var schema = SectionRowSchemaIdentity<TRow>.Create();
        return new Binding<TRow>(
            rowSet,
            queryScope,
            schema,
            requiresDirectUseClusters,
            rows,
            resultBinder);
    }

    private sealed class Binding<TRow> :
        GraphLibrariesSectionRowBinding
    {
        private readonly QuerySpaceRowScopeBinding<TRow> _queryScope;
        private readonly SectionRowSchemaIdentity<TRow> _schema;
        private readonly Func<
            GraphLibrariesSectionRowProjection,
            IReadOnlyList<TRow>> _rows;
        private readonly Func<
            GraphLibrariesSectionRowProjection,
            IReadOnlyList<TRow>,
            GraphLibrariesSectionRowProjection> _resultBinder;

        internal Binding(
            string rowSet,
            QuerySpaceRowScopeBinding<TRow> queryScope,
            SectionRowSchemaIdentity<TRow> schema,
            bool requiresDirectUseClusters,
            Func<
                GraphLibrariesSectionRowProjection,
                IReadOnlyList<TRow>> rows,
            Func<
                GraphLibrariesSectionRowProjection,
                IReadOnlyList<TRow>,
                GraphLibrariesSectionRowProjection> resultBinder)
            : base(
                rowSet,
                queryScope,
                schema,
                requiresDirectUseClusters)
        {
            _queryScope = queryScope;
            _schema = schema;
            _rows = rows;
            _resultBinder = resultBinder;
        }

        public override QuerySpaceSectionRowResolutionResult<
            GraphLibrariesSectionRowProjection> Resolve(
                GraphLibrariesQueryPlan operation,
                RowSelectionIntent<string> rowSelection,
                QuerySpaceTerminalRequirement terminal,
                GraphLibrariesSectionRowProjection projection)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(rowSelection);
            ArgumentNullException.ThrowIfNull(projection);

            PortableQueryIntent rowIntent = PortableQueryIntent.Create(
                [],
                [],
                PortableQueryRowSelection.ToStages(rowSelection),
                []);
            QuerySpaceRequest request = QuerySpaceRequest.Create(
                GraphLibrariesQuery.QuerySpace.Descriptor,
                operation.Intent,
                [RowSet],
                [
                    new QuerySpaceRowIntentAssociation(
                        _queryScope.Descriptor.Identity,
                        rowIntent,
                        [RowSet]),
                ],
                terminal);
            return QuerySpaceSectionRowResolver.Resolve<
                GraphLibrariesSectionRowProjection>(
                GraphLibrariesQuery.QuerySpace,
                request,
                [
                    new SectionRowSetDeclaration<
                        string,
                        GraphLibrariesSectionRowProjection,
                        TRow>(
                            RowSet,
                            _schema,
                            _rows(projection),
                            _resultBinder),
                ],
                new SectionQuerySpaceRowScopeBinding<TRow>(
                    _queryScope,
                    _schema));
        }
    }
}
