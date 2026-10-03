using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ILInspector.Analysis;
using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public sealed record LibraryDependencyNamespaceCycleRow(
    int CycleIndex,
    ImmutableArray<string> Namespaces);

public sealed record LibraryDependencyQueryRows(
    ImmutableArray<LibraryDependencyTypeNode> Types,
    ImmutableArray<LibraryDependencyExternalNode> ExternalNodes,
    ImmutableArray<LibraryDependencyTypeEdge> TypeEdges,
    ImmutableArray<LibraryDependencyExternalTypeEdge> ExternalTypeEdges,
    ImmutableArray<LibraryDependencyNamespaceNode> Namespaces,
    ImmutableArray<LibraryDependencyNamespaceEdge> NamespaceEdges,
    ImmutableArray<LibraryDependencyExternalNamespaceEdge>
        ExternalNamespaceEdges,
    ImmutableArray<LibraryDependencyNamespaceCycleRow> Cycles)
{
    public static LibraryDependencyQueryRows Empty { get; } =
        new([], [], [], [], [], [], [], []);
}

public sealed record LibraryDependencySemanticSelectionFailure(
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

public abstract record LibraryDependencyStructureQueryResult
{
    private LibraryDependencyStructureQueryResult()
    {
    }

    public sealed record Available(
        LibraryDependencyStructureDocument Document,
        QuerySpaceRequest Request,
        LibraryDependencyQueryRows Rows,
        int? Count)
        : LibraryDependencyStructureQueryResult;

    public sealed record Unavailable(
        LibraryDependencyStructureResult.Unavailable Outcome)
        : LibraryDependencyStructureQueryResult;

    public sealed record SelectionFailed(
        string Detail,
        RowQueryFailure? ResolutionFailure = null,
        LibraryDependencySemanticSelectionFailure?
            SemanticFailure = null)
        : LibraryDependencyStructureQueryResult;

    public sealed record Failed(Exception Error)
        : LibraryDependencyStructureQueryResult;
}

internal readonly record struct
    LibraryDependencyStructureOperationPredicate;

internal sealed record LibraryDependencyStructureOperationPlan(
    PortableQueryIntent Intent);

internal sealed class LibraryDependencyStructureOperationVocabulary
    : PortableQueryVocabulary<
        LibraryDependencyStructureOperationPredicate,
        LibraryDependencyStructureOperationPlan>
{
    public override string Identity =>
        LibraryDependencyStructureQuery.OperationVocabularyIdentity;

    public override IReadOnlyList<string> RequiredDimensions => [];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<
            LibraryDependencyStructureOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<
            LibraryDependencyStructureOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool AdmitsStageKind(
        RowSelectionStageKind kind) => false;

    public override bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose)
    {
        purpose = default;
        return false;
    }

    public override bool IsOrderable(string key) => false;

    public override bool CollapsesDuplicateBindings => true;

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<
            LibraryDependencyStructureOperationPredicate> first,
        PortableQueryResolvedTerm<
            LibraryDependencyStructureOperationPredicate> second) =>
        true;

    public override LibraryDependencyStructureOperationPlan CreatePlan(
        PortableQueryResolvedIntent<
            LibraryDependencyStructureOperationPredicate> resolved) =>
        new(
            PortableQueryIntent.Create(
                [.. resolved.Terms.Select(term => term.Term)],
                [.. resolved.Bounds],
                [.. resolved.Stages],
                []));
}

public static partial class LibraryDependencyStructureQuery
{
    public const string OperationIdentity =
        "library-dependency-structure";
    public const string OperationRouteIdentity =
        "library-dependency-structure/default";
    public const string OperationSubjectRole = "exact-library";
    public const string OperationResultGrain =
        "library-dependency-structure";
    public const string OperationProfileIdentity = "default";
    public const string OperationVocabularyIdentity =
        "library-dependency-structure/operation/v1";
    public const string QuerySpaceIdentity =
        "library-dependency-structure/query-space/v1";

    public const string TypeNodesRowSet = "dependency-types";
    public const string ExternalNodesRowSet =
        "dependency-external-nodes";
    public const string TypeEdgesRowSet = "dependency-type-edges";
    public const string ExternalTypeEdgesRowSet =
        "dependency-external-type-edges";
    public const string NamespaceNodesRowSet =
        "dependency-namespaces";
    public const string NamespaceEdgesRowSet =
        "dependency-namespace-edges";
    public const string ExternalNamespaceEdgesRowSet =
        "dependency-external-namespace-edges";
    public const string CyclesRowSet = "dependency-namespace-cycles";

    public static IReadOnlyList<string> RowSets =>
        QueryRegistration.RowSets;

    public static InspectionQuery<LibraryDependencyStructureQueryResult>
        Definition => QueryRegistration.Definition;

    public static IQueryOperationRoute OperationRoute =>
        QueryRegistration.Route;

    public static QuerySpaceBinding QuerySpace =>
        QueryRegistration.QuerySpace;

    public static QuerySpaceRequest CreateRequest(
        string rowSet,
        RowSelectionIntent<string> rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return CreateRequest(
            rowSet,
            PortableQueryIntent.Create(
                [],
                [],
                PortableQueryRowSelection.ToStages(rows),
                []),
            terminal);
    }

    public static QuerySpaceRequest CreateRequest(
        string rowSet,
        PortableQueryIntent rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowSet);
        ArgumentNullException.ThrowIfNull(rows);
        QuerySpaceRowScopeBinding scope = RowScope(rowSet);
        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Create([], [], [], []),
            [rowSet],
            [
                new(
                    scope.Descriptor.Identity,
                    rows,
                    [rowSet]),
            ],
            terminal);
    }

    internal static QuerySpaceRowScopeBinding RowScope(string rowSet) =>
        rowSet switch
        {
            TypeNodesRowSet => TypeNodesScope,
            ExternalNodesRowSet => ExternalNodesScope,
            TypeEdgesRowSet => TypeEdgesScope,
            ExternalTypeEdgesRowSet => ExternalTypeEdgesScope,
            NamespaceNodesRowSet => NamespaceNodesScope,
            NamespaceEdgesRowSet => NamespaceEdgesScope,
            ExternalNamespaceEdgesRowSet =>
                ExternalNamespaceEdgesScope,
            CyclesRowSet => CyclesScope,
            _ => throw new ArgumentException(
                $"Unknown Library Dependency Structure row set "
                    + $"'{rowSet}'.",
                nameof(rowSet)),
        };

    public static LibraryDependencyQueryRows CreateRows(
        LibraryDependencyStructureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.Types,
            document.ExternalNodes,
            document.TypeEdges,
            document.ExternalTypeEdges,
            document.Namespaces,
            document.NamespaceEdges,
            document.ExternalNamespaceEdges,
            [
                .. document.Cycles.Select(
                    static (cycle, index) =>
                        new LibraryDependencyNamespaceCycleRow(
                            index,
                            cycle.Namespaces)),
            ]);
    }

    private static class QueryRegistration
    {
        internal static readonly IReadOnlyList<string> RowSets =
        [
            TypeNodesRowSet,
            ExternalNodesRowSet,
            TypeEdgesRowSet,
            ExternalTypeEdgesRowSet,
            NamespaceNodesRowSet,
            NamespaceEdgesRowSet,
            ExternalNamespaceEdgesRowSet,
            CyclesRowSet,
        ];

        private static readonly QueryOperationDefinition<
            LibraryDependencyStructureOperationPredicate,
            LibraryDependencyStructureOperationPlan>
            OperationDefinition =
            QueryOperationDefinition<
                LibraryDependencyStructureOperationPredicate,
                LibraryDependencyStructureOperationPlan>.Create(
                    OperationIdentity,
                    new LibraryDependencyStructureOperationVocabulary(),
                    [OperationSubjectRole],
                    [OperationResultGrain],
                    RowSets,
                    [],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [],
                            []),
                    ]);

        internal static readonly QueryOperationRoute<
            LibraryDependencyStructureOperationPredicate,
            LibraryDependencyStructureOperationPlan> Route =
            QueryOperationRoute<
                LibraryDependencyStructureOperationPredicate,
                LibraryDependencyStructureOperationPlan>.Create(
                    OperationRouteIdentity,
                    OperationDefinition,
                    OperationSubjectRole,
                    OperationResultGrain,
                    RowSets,
                    OperationProfileIdentity,
                    [],
                    []);

        internal static readonly InspectionQuery<
            LibraryDependencyStructureQueryResult> Definition =
            new(
                "Library dependency structure",
                InspectionCost.Unbounded);

        internal static readonly QuerySpaceBinding QuerySpace =
            QuerySpaceBinding.Create(
                QuerySpaceIdentity,
                Route,
                RowScopes,
                [
                    QuerySpaceTerminalRequirement.Rows,
                    QuerySpaceTerminalRequirement.Count,
                ],
                acceptsContinuation: false,
                [
                    new(
                        QuerySpaceTerminalRequirement.Rows,
                        "library-dependency-structure/rows/v1"),
                    new(
                        QuerySpaceTerminalRequirement.Count,
                        "library-dependency-structure/count/v1"),
                ]);
    }
}
