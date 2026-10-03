using DotnetInspector.Queries;
using DotnetInspector.Sections;

using ILInspector.Analysis;
using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;

namespace DotnetInspector.ResearchSections;

public static class LibraryDependencyStructureInspection
{
    public static InspectionEnvelope<LibraryDependencyStructureDocument>
        Envelope(
            LibraryDependencyStructureQueryResult.Available available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return new(
            available.Document,
            new InspectionShare.NonProjectable(
                "library-dependency-structure/share",
                "Inspect Web cannot yet restore an exact Library "
                    + "dependency-structure inspection."),
            []);
    }

    public static LibraryDependencyStructureQueryResult Execute(
        LibraryBodyAnalysisExecution analysis,
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return Project(
                LibraryDependencyStructure.Execute(analysis),
                request);
        }
        catch (Exception error)
        {
            return new LibraryDependencyStructureQueryResult.Failed(
                error);
        }
    }

    public static LibraryDependencyStructureQueryResult Execute(
        LibraryCallGraphAnalysisResult callGraph,
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(callGraph);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return Project(
                LibraryDependencyStructure.Execute(callGraph),
                request);
        }
        catch (Exception error)
        {
            return new LibraryDependencyStructureQueryResult.Failed(
                error);
        }
    }

    public static LibraryDependencyStructureQueryResult Select(
        LibraryDependencyStructureDocument document,
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            if (request.ParticipatingRowSets.Count != 1)
            {
                return new LibraryDependencyStructureQueryResult
                    .SelectionFailed(
                        "Library Dependency Structure selection requires "
                            + "exactly one participating row set.");
            }

            string rowSet = request.ParticipatingRowSets[0];
            Binding? binding = Bindings.SingleOrDefault(
                candidate => string.Equals(
                    candidate.RowSet,
                    rowSet,
                    StringComparison.Ordinal));
            if (binding is null)
            {
                return new LibraryDependencyStructureQueryResult
                    .SelectionFailed(
                        $"Unknown Library Dependency Structure row set "
                            + $"'{rowSet}'.");
            }

            var projection = new Projection(
                document,
                LibraryDependencyStructureQuery.CreateRows(document));
            QuerySpaceSectionRowResolutionResult<Projection>
                resolution = binding.Resolve(request, projection);
            if (!resolution.IsSuccess)
            {
                return new LibraryDependencyStructureQueryResult
                    .SelectionFailed(
                        "The Library Dependency Structure row query "
                            + "could not be resolved.",
                        resolution.Failure!.RowQueryFailure);
            }

            if (request.Terminal
                == QuerySpaceTerminalRequirement.Count)
            {
                SectionCountOutcome<string, string> count =
                    QuerySpaceSectionRowExecutor.ApplyCount<
                        Projection,
                        string>(resolution.Request!);
                return count switch
                {
                    SectionCountOutcome<string, string>.Completed
                        completed =>
                        new LibraryDependencyStructureQueryResult
                            .Available(
                                document,
                                request,
                                LibraryDependencyQueryRows.Empty,
                                completed.Counts.Single().Value),
                    SectionCountOutcome<string, string>.Semantic
                        semantic =>
                        SelectionFailure(
                            "The Library Dependency Structure Count "
                                + "selection exceeded the available rows.",
                            semantic.StageNumber,
                            semantic.RequiredPosition,
                            semantic.AvailableCount),
                    _ => throw new InvalidOperationException(
                        "Library Dependency Structure Count did not "
                            + "produce an exact completed or semantic "
                            + "outcome."),
                };
            }

            SectionRowsOutcome<string, Projection> rows =
                QuerySpaceSectionRowExecutor.ApplyRows(
                    resolution.Request!);
            if (!rows.IsSuccess)
            {
                return SelectionFailure(
                    "The Library Dependency Structure row selection "
                        + "exceeded the available rows.",
                    rows.Failure!.Failure.StageNumber,
                    rows.Failure.Failure.RequiredPosition,
                    rows.Failure.Failure.AvailableCount);
            }

            Projection selected = rows.Rebind(projection);
            return new LibraryDependencyStructureQueryResult.Available(
                document,
                request,
                selected.Rows,
                Count: null);
        }
        catch (Exception error)
        {
            return new LibraryDependencyStructureQueryResult.Failed(
                error);
        }
    }

    private static LibraryDependencyStructureQueryResult.SelectionFailed
        SelectionFailure(
            string detail,
            int stageNumber,
            int requiredPosition,
            int availableCount) =>
        new(
            detail,
            SemanticFailure:
                new(
                    stageNumber,
                    requiredPosition,
                    availableCount));

    private static LibraryDependencyStructureQueryResult Project(
        LibraryDependencyStructureResult result,
        QuerySpaceRequest request) =>
        result switch
        {
            LibraryDependencyStructureResult.Available available =>
                Select(available.Document, request),
            LibraryDependencyStructureResult.Unavailable unavailable =>
                new LibraryDependencyStructureQueryResult.Unavailable(
                    unavailable),
            var unknown => throw new InvalidOperationException(
                "Unknown Library Dependency Structure result "
                    + $"'{unknown.GetType().Name}'."),
        };

    private sealed record Projection(
        LibraryDependencyStructureDocument Document,
        LibraryDependencyQueryRows Rows);

    private static IReadOnlyList<Binding> Bindings { get; } =
    [
        Create(
            LibraryDependencyStructureQuery.TypeNodesRowSet,
            LibraryDependencyStructureQuery.TypeNodesScope,
            static projection => projection.Rows.Types,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        Types = [.. rows],
                    },
                }),
        Create(
            LibraryDependencyStructureQuery.ExternalNodesRowSet,
            LibraryDependencyStructureQuery.ExternalNodesScope,
            static projection => projection.Rows.ExternalNodes,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        ExternalNodes = [.. rows],
                    },
                }),
        Create(
            LibraryDependencyStructureQuery.TypeEdgesRowSet,
            LibraryDependencyStructureQuery.TypeEdgesScope,
            static projection => projection.Rows.TypeEdges,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        TypeEdges = [.. rows],
                    },
                }),
        Create(
            LibraryDependencyStructureQuery.ExternalTypeEdgesRowSet,
            LibraryDependencyStructureQuery.ExternalTypeEdgesScope,
            static projection => projection.Rows.ExternalTypeEdges,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        ExternalTypeEdges = [.. rows],
                    },
                }),
        Create(
            LibraryDependencyStructureQuery.NamespaceNodesRowSet,
            LibraryDependencyStructureQuery.NamespaceNodesScope,
            static projection => projection.Rows.Namespaces,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        Namespaces = [.. rows],
                    },
                }),
        Create(
            LibraryDependencyStructureQuery.NamespaceEdgesRowSet,
            LibraryDependencyStructureQuery.NamespaceEdgesScope,
            static projection => projection.Rows.NamespaceEdges,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        NamespaceEdges = [.. rows],
                    },
                }),
        Create(
            LibraryDependencyStructureQuery
                .ExternalNamespaceEdgesRowSet,
            LibraryDependencyStructureQuery
                .ExternalNamespaceEdgesScope,
            static projection =>
                projection.Rows.ExternalNamespaceEdges,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        ExternalNamespaceEdges = [.. rows],
                    },
                }),
        Create(
            LibraryDependencyStructureQuery.CyclesRowSet,
            LibraryDependencyStructureQuery.CyclesScope,
            static projection => projection.Rows.Cycles,
            static (projection, rows) =>
                projection with
                {
                    Rows = LibraryDependencyQueryRows.Empty with
                    {
                        Cycles = [.. rows],
                    },
                }),
    ];

    private static Binding Create<TRow>(
        string rowSet,
        QuerySpaceRowScopeBinding<TRow> queryScope,
        Func<Projection, IReadOnlyList<TRow>> rows,
        Func<
            Projection,
            IReadOnlyList<TRow>,
            Projection> resultBinder) =>
        new TypedBinding<TRow>(
            rowSet,
            queryScope,
            rows,
            resultBinder);

    private abstract class Binding
    {
        private protected Binding(string rowSet) =>
            RowSet = rowSet;

        internal string RowSet { get; }

        internal abstract QuerySpaceSectionRowResolutionResult<
            Projection> Resolve(
                QuerySpaceRequest request,
                Projection projection);
    }

    private sealed class TypedBinding<TRow> : Binding
    {
        private readonly QuerySpaceRowScopeBinding<TRow> _queryScope;
        private readonly SectionRowSchemaIdentity<TRow> _schema =
            SectionRowSchemaIdentity<TRow>.Create();
        private readonly Func<
            Projection,
            IReadOnlyList<TRow>> _rows;
        private readonly Func<
            Projection,
            IReadOnlyList<TRow>,
            Projection> _resultBinder;

        internal TypedBinding(
            string rowSet,
            QuerySpaceRowScopeBinding<TRow> queryScope,
            Func<Projection, IReadOnlyList<TRow>> rows,
            Func<
                Projection,
                IReadOnlyList<TRow>,
                Projection> resultBinder)
            : base(rowSet)
        {
            _queryScope = queryScope;
            _rows = rows;
            _resultBinder = resultBinder;
        }

        internal override QuerySpaceSectionRowResolutionResult<
            Projection> Resolve(
                QuerySpaceRequest request,
                Projection projection)
        {
            var declaration =
                new SectionRowSetDeclaration<
                    string,
                    Projection,
                    TRow>(
                        RowSet,
                        _schema,
                        _rows(projection),
                        _resultBinder);
            return QuerySpaceSectionRowResolver.Resolve<Projection>(
                LibraryDependencyStructureQuery.QuerySpace,
                request,
                [declaration],
                new SectionQuerySpaceRowScopeBinding<TRow>(
                    _queryScope,
                    _schema));
        }
    }
}
