using System.Globalization;

using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public static partial class LibraryDependencyStructureQuery
{
    public const string TypeNodesScopeIdentity =
        "library-dependency-structure/type-nodes/v1";
    public const string ExternalNodesScopeIdentity =
        "library-dependency-structure/external-nodes/v1";
    public const string TypeEdgesScopeIdentity =
        "library-dependency-structure/type-edges/v1";
    public const string ExternalTypeEdgesScopeIdentity =
        "library-dependency-structure/external-type-edges/v1";
    public const string NamespaceNodesScopeIdentity =
        "library-dependency-structure/namespace-nodes/v1";
    public const string NamespaceEdgesScopeIdentity =
        "library-dependency-structure/namespace-edges/v1";
    public const string ExternalNamespaceEdgesScopeIdentity =
        "library-dependency-structure/external-namespace-edges/v1";
    public const string CyclesScopeIdentity =
        "library-dependency-structure/cycles/v1";

    public const string TypeNodeTypeKey = "type";
    public const string TypeNodeNamespaceKey =
        "type-namespace";
    public const string TypeNodeIntraRelationshipCountKey =
        "type-intra-relationship-count";
    public const string ExternalNodeKey = "external";
    public const string ExternalNodeAssemblyNameKey =
        "external-assembly";
    public const string ExternalNodeNamespaceKey =
        "external-namespace";
    public const string ExternalNodeIntrinsicCoreLibraryKey =
        "external-intrinsic-core-library";
    public const string TypeEdgeSourceTypeKey =
        "type-edge-source";
    public const string TypeEdgeTargetTypeKey =
        "type-edge-target";
    public const string TypeEdgeInvocationCountKey =
        "type-edge-invocation-count";
    public const string TypeEdgeFunctionReferenceCountKey =
        "type-edge-function-reference-count";
    public const string TypeEdgeRelationshipCountKey =
        "type-edge-relationship-count";
    public const string ExternalTypeEdgeSourceTypeKey =
        "external-type-edge-source";
    public const string ExternalTypeEdgeExternalKey =
        "external-type-edge-target";
    public const string ExternalTypeEdgeInvocationCountKey =
        "external-type-edge-invocation-count";
    public const string ExternalTypeEdgeFunctionReferenceCountKey =
        "external-type-edge-function-reference-count";
    public const string ExternalTypeEdgeRelationshipCountKey =
        "external-type-edge-relationship-count";
    public const string NamespaceNodeNamespaceKey =
        "namespace";
    public const string NamespaceNodeGlobalNamespaceKey =
        "namespace-global";
    public const string NamespaceNodeTypeCountKey =
        "namespace-type-count";
    public const string NamespaceNodeIntraRelationshipCountKey =
        "namespace-intra-relationship-count";
    public const string NamespaceNodeCycleIndexKey =
        "namespace-cycle-index";
    public const string NamespaceNodeLevelKey =
        "namespace-level";
    public const string NamespaceEdgeSourceNamespaceKey =
        "namespace-edge-source";
    public const string NamespaceEdgeTargetNamespaceKey =
        "namespace-edge-target";
    public const string NamespaceEdgeInvocationCountKey =
        "namespace-edge-invocation-count";
    public const string NamespaceEdgeFunctionReferenceCountKey =
        "namespace-edge-function-reference-count";
    public const string NamespaceEdgeRelationshipCountKey =
        "namespace-edge-relationship-count";
    public const string NamespaceEdgeContributingTypeEdgeCountKey =
        "namespace-edge-contributor-count";
    public const string NamespaceEdgeRemainingContributorCountKey =
        "namespace-edge-remaining-count";
    public const string ExternalNamespaceEdgeSourceNamespaceKey =
        "external-namespace-edge-source";
    public const string ExternalNamespaceEdgeExternalKey =
        "external-namespace-edge-target";
    public const string ExternalNamespaceEdgeInvocationCountKey =
        "external-namespace-edge-invocation-count";
    public const string ExternalNamespaceEdgeFunctionReferenceCountKey =
        "external-namespace-edge-function-reference-count";
    public const string ExternalNamespaceEdgeRelationshipCountKey =
        "external-namespace-edge-relationship-count";
    public const string
        ExternalNamespaceEdgeContributingTypeEdgeCountKey =
        "external-namespace-edge-contributor-count";
    public const string
        ExternalNamespaceEdgeRemainingContributorCountKey =
        "external-namespace-edge-remaining-count";
    public const string CycleIndexKey =
        "cycle-index";
    public const string CycleNamespaceKey =
        "cycle-namespace";
    public const string CycleNamespaceCountKey =
        "cycle-namespace-count";

    public const string TypeNodeIdentityOrder =
        "type-identity";
    public const string TypeNodeVolumeOrder =
        "type-volume";
    public const string ExternalNodeIdentityOrder =
        "external-identity";
    public const string TypeEdgeIdentityOrder =
        "type-edge-identity";
    public const string TypeEdgeRelationshipCountOrder =
        "type-edge-relationship-count-order";
    public const string ExternalTypeEdgeIdentityOrder =
        "external-type-edge-identity";
    public const string ExternalTypeEdgeRelationshipCountOrder =
        "external-type-edge-relationship-count-order";
    public const string NamespaceNodeIdentityOrder =
        "namespace-identity";
    public const string NamespaceNodeTypeCountOrder =
        "namespace-type-count-order";
    public const string NamespaceEdgeIdentityOrder =
        "namespace-edge-identity";
    public const string NamespaceEdgeRelationshipCountOrder =
        "namespace-edge-relationship-count-order";
    public const string ExternalNamespaceEdgeIdentityOrder =
        "external-namespace-edge-identity";
    public const string
        ExternalNamespaceEdgeRelationshipCountOrder =
        "external-namespace-edge-relationship-count-order";
    public const string CycleIdentityOrder =
        "cycle-identity";
    public const string CycleSizeOrder =
        "cycle-namespace-count-order";

    private static IReadOnlyList<RowQueryOperator>
        EqualityOperators() =>
    [
        RowQueryOperator.Equals,
        RowQueryOperator.NotEquals,
    ];

    private static IReadOnlyList<RowQueryOperator>
        NumericOperators() =>
    [
        RowQueryOperator.Equals,
        RowQueryOperator.NotEquals,
        RowQueryOperator.GreaterOrEqual,
        RowQueryOperator.LessOrEqual,
    ];

    private static QuerySpaceRowScopeBinding<LibraryDependencyTypeNode>
        CreateTypeNodesScope() =>
        Scope(
            TypeNodesScopeIdentity,
            TypeNodesRowSet,
            [
                OrdinalKey<LibraryDependencyTypeNode>(
                    TypeNodeTypeKey,
                    static row => row.TypeKey),
                OrdinalKey<LibraryDependencyTypeNode>(
                    TypeNodeNamespaceKey,
                    static row => row.Namespace),
                NumericKey<LibraryDependencyTypeNode>(
                    TypeNodeIntraRelationshipCountKey,
                    static row => row.IntraTypeRelationshipCount),
            ],
            [
                NamedOrder(
                    TypeNodeIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    TypeNodeIdentity()),
                NamedOrder(
                    TypeNodeVolumeOrder,
                    RowQueryOrderPurpose.Ranking,
                    TypeNodeVolume()),
            ],
            TypeNodeIdentityOrder,
            TypeNodeVolumeOrder,
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Top,
            ]);

    private static QuerySpaceRowScopeBinding<
        LibraryDependencyExternalNode> CreateExternalNodesScope() =>
        Scope(
            ExternalNodesScopeIdentity,
            ExternalNodesRowSet,
            [
                OrdinalKey<LibraryDependencyExternalNode>(
                    ExternalNodeKey,
                    static row => row.Key),
                OptionalOrdinalKey<LibraryDependencyExternalNode>(
                    ExternalNodeAssemblyNameKey,
                    static row => row.Assembly?.Name),
                OrdinalKey<LibraryDependencyExternalNode>(
                    ExternalNodeNamespaceKey,
                    static row => row.Namespace),
                BooleanKey<LibraryDependencyExternalNode>(
                    ExternalNodeIntrinsicCoreLibraryKey,
                    static row => row.IsIntrinsicCoreLibrary),
            ],
            [
                NamedOrder(
                    ExternalNodeIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    ExternalNodeIdentity()),
            ],
            ExternalNodeIdentityOrder,
            topOrder: null,
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ]);

    private static QuerySpaceRowScopeBinding<LibraryDependencyTypeEdge>
        CreateTypeEdgesScope() =>
        Scope(
            TypeEdgesScopeIdentity,
            TypeEdgesRowSet,
            EdgeKeys<LibraryDependencyTypeEdge>(
                static row => row.SourceTypeKey,
                static row => row.TargetTypeKey,
                static row => row.Counts,
                TypeEdgeSourceTypeKey,
                TypeEdgeTargetTypeKey,
                TypeEdgeInvocationCountKey,
                TypeEdgeFunctionReferenceCountKey,
                TypeEdgeRelationshipCountKey),
            [
                NamedOrder(
                    TypeEdgeIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    TypeEdgeIdentity()),
                NamedOrder(
                    TypeEdgeRelationshipCountOrder,
                    RowQueryOrderPurpose.Ranking,
                    TypeEdgeVolume()),
            ],
            TypeEdgeIdentityOrder,
            TypeEdgeRelationshipCountOrder,
            FullStages());

    private static QuerySpaceRowScopeBinding<
        LibraryDependencyExternalTypeEdge>
        CreateExternalTypeEdgesScope() =>
        Scope(
            ExternalTypeEdgesScopeIdentity,
            ExternalTypeEdgesRowSet,
            EdgeKeys<LibraryDependencyExternalTypeEdge>(
                static row => row.SourceTypeKey,
                static row => row.ExternalKey,
                static row => row.Counts,
                ExternalTypeEdgeSourceTypeKey,
                ExternalTypeEdgeExternalKey,
                ExternalTypeEdgeInvocationCountKey,
                ExternalTypeEdgeFunctionReferenceCountKey,
                ExternalTypeEdgeRelationshipCountKey),
            [
                NamedOrder(
                    ExternalTypeEdgeIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    ExternalTypeEdgeIdentity()),
                NamedOrder(
                    ExternalTypeEdgeRelationshipCountOrder,
                    RowQueryOrderPurpose.Ranking,
                    ExternalTypeEdgeVolume()),
            ],
            ExternalTypeEdgeIdentityOrder,
            ExternalTypeEdgeRelationshipCountOrder,
            FullStages());

    private static QuerySpaceRowScopeBinding<
        LibraryDependencyNamespaceNode> CreateNamespaceNodesScope() =>
        Scope(
            NamespaceNodesScopeIdentity,
            NamespaceNodesRowSet,
            [
                OrdinalKey<LibraryDependencyNamespaceNode>(
                    NamespaceNodeNamespaceKey,
                    static row => row.Namespace),
                BooleanKey<LibraryDependencyNamespaceNode>(
                    NamespaceNodeGlobalNamespaceKey,
                    static row => row.IsGlobalNamespace),
                NumericKey<LibraryDependencyNamespaceNode>(
                    NamespaceNodeTypeCountKey,
                    static row => row.TypeCount),
                NumericKey<LibraryDependencyNamespaceNode>(
                    NamespaceNodeIntraRelationshipCountKey,
                    static row =>
                        row.IntraNamespaceRelationshipCount),
                OptionalNumericKey<LibraryDependencyNamespaceNode>(
                    NamespaceNodeCycleIndexKey,
                    static row => row.CycleIndex),
                NumericKey<LibraryDependencyNamespaceNode>(
                    NamespaceNodeLevelKey,
                    static row => row.Level),
            ],
            [
                NamedOrder(
                    NamespaceNodeIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    NamespaceNodeIdentity()),
                NamedOrder(
                    NamespaceNodeTypeCountOrder,
                    RowQueryOrderPurpose.Ranking,
                    NamespaceNodeTypeCount()),
            ],
            NamespaceNodeIdentityOrder,
            NamespaceNodeTypeCountOrder,
            FullStages());

    private static QuerySpaceRowScopeBinding<
        LibraryDependencyNamespaceEdge> CreateNamespaceEdgesScope() =>
        Scope(
            NamespaceEdgesScopeIdentity,
            NamespaceEdgesRowSet,
            NamespaceEdgeKeys<LibraryDependencyNamespaceEdge>(
                static row => row.SourceNamespace,
                static row => row.TargetNamespace,
                static row => row.Counts,
                static row => row.ContributingTypeEdgeCount,
                static row => row.RemainingContributorCount,
                NamespaceEdgeSourceNamespaceKey,
                NamespaceEdgeTargetNamespaceKey,
                NamespaceEdgeInvocationCountKey,
                NamespaceEdgeFunctionReferenceCountKey,
                NamespaceEdgeRelationshipCountKey,
                NamespaceEdgeContributingTypeEdgeCountKey,
                NamespaceEdgeRemainingContributorCountKey),
            [
                NamedOrder(
                    NamespaceEdgeIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    NamespaceEdgeIdentity()),
                NamedOrder(
                    NamespaceEdgeRelationshipCountOrder,
                    RowQueryOrderPurpose.Ranking,
                    NamespaceEdgeVolume()),
            ],
            NamespaceEdgeIdentityOrder,
            NamespaceEdgeRelationshipCountOrder,
            FullStages());

    private static QuerySpaceRowScopeBinding<
        LibraryDependencyExternalNamespaceEdge>
        CreateExternalNamespaceEdgesScope() =>
        Scope(
            ExternalNamespaceEdgesScopeIdentity,
            ExternalNamespaceEdgesRowSet,
            NamespaceEdgeKeys<
                LibraryDependencyExternalNamespaceEdge>(
                    static row => row.SourceNamespace,
                    static row => row.ExternalKey,
                    static row => row.Counts,
                    static row => row.ContributingTypeEdgeCount,
                    static row => row.RemainingContributorCount,
                    ExternalNamespaceEdgeSourceNamespaceKey,
                    ExternalNamespaceEdgeExternalKey,
                    ExternalNamespaceEdgeInvocationCountKey,
                    ExternalNamespaceEdgeFunctionReferenceCountKey,
                    ExternalNamespaceEdgeRelationshipCountKey,
                    ExternalNamespaceEdgeContributingTypeEdgeCountKey,
                    ExternalNamespaceEdgeRemainingContributorCountKey),
            [
                NamedOrder(
                    ExternalNamespaceEdgeIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    ExternalNamespaceEdgeIdentity()),
                NamedOrder(
                    ExternalNamespaceEdgeRelationshipCountOrder,
                    RowQueryOrderPurpose.Ranking,
                    ExternalNamespaceEdgeVolume()),
            ],
            ExternalNamespaceEdgeIdentityOrder,
            ExternalNamespaceEdgeRelationshipCountOrder,
            FullStages());

    private static QuerySpaceRowScopeBinding<
        LibraryDependencyNamespaceCycleRow> CreateCyclesScope() =>
        Scope(
            CyclesScopeIdentity,
            CyclesRowSet,
            [
                NumericKey<LibraryDependencyNamespaceCycleRow>(
                    CycleIndexKey,
                    static row => row.CycleIndex),
                MembershipKey<LibraryDependencyNamespaceCycleRow>(
                    CycleNamespaceKey,
                    static row => row.Namespaces),
                NumericKey<LibraryDependencyNamespaceCycleRow>(
                    CycleNamespaceCountKey,
                    static row => row.Namespaces.Length),
            ],
            [
                NamedOrder(
                    CycleIdentityOrder,
                    RowQueryOrderPurpose.Sequence,
                    CycleIdentity()),
                NamedOrder(
                    CycleSizeOrder,
                    RowQueryOrderPurpose.Ranking,
                    CycleSize()),
            ],
            CycleIdentityOrder,
            CycleSizeOrder,
            FullStages());

    public static QuerySpaceRowScopeBinding<LibraryDependencyTypeNode>
        TypeNodesScope => Registration.TypeNodes;

    public static QuerySpaceRowScopeBinding<
        LibraryDependencyExternalNode> ExternalNodesScope =>
        Registration.ExternalNodes;

    public static QuerySpaceRowScopeBinding<LibraryDependencyTypeEdge>
        TypeEdgesScope => Registration.TypeEdges;

    public static QuerySpaceRowScopeBinding<
        LibraryDependencyExternalTypeEdge> ExternalTypeEdgesScope =>
        Registration.ExternalTypeEdges;

    public static QuerySpaceRowScopeBinding<
        LibraryDependencyNamespaceNode> NamespaceNodesScope =>
        Registration.NamespaceNodes;

    public static QuerySpaceRowScopeBinding<
        LibraryDependencyNamespaceEdge> NamespaceEdgesScope =>
        Registration.NamespaceEdges;

    public static QuerySpaceRowScopeBinding<
        LibraryDependencyExternalNamespaceEdge>
        ExternalNamespaceEdgesScope =>
        Registration.ExternalNamespaceEdges;

    public static QuerySpaceRowScopeBinding<
        LibraryDependencyNamespaceCycleRow> CyclesScope =>
        Registration.Cycles;

    private static IReadOnlyList<QuerySpaceRowScopeBinding> RowScopes =>
        Registration.All;

    private static IReadOnlyList<RowSelectionStageKind> FullStages() =>
    [
        RowSelectionStageKind.Head,
        RowSelectionStageKind.Tail,
        RowSelectionStageKind.Window,
        RowSelectionStageKind.Top,
    ];

    private static IComparer<LibraryDependencyTypeNode>
        TypeNodeIdentity() =>
        Comparer<LibraryDependencyTypeNode>.Create(
            static (left, right) => StringComparer.Ordinal.Compare(
                left.TypeKey,
                right.TypeKey));

    private static IComparer<LibraryDependencyTypeNode>
        TypeNodeVolume() =>
        Comparer<LibraryDependencyTypeNode>.Create(
            static (left, right) =>
                CompareRanked(
                    left.IntraTypeRelationshipCount,
                    right.IntraTypeRelationshipCount,
                    left.TypeKey,
                    right.TypeKey));

    private static IComparer<LibraryDependencyExternalNode>
        ExternalNodeIdentity() =>
        Comparer<LibraryDependencyExternalNode>.Create(
            static (left, right) => StringComparer.Ordinal.Compare(
                left.Key,
                right.Key));

    private static IComparer<LibraryDependencyTypeEdge>
        TypeEdgeIdentity() =>
        Comparer<LibraryDependencyTypeEdge>.Create(
            static (left, right) => ComparePair(
                left.SourceTypeKey,
                left.TargetTypeKey,
                right.SourceTypeKey,
                right.TargetTypeKey));

    private static IComparer<LibraryDependencyTypeEdge>
        TypeEdgeVolume() =>
        Comparer<LibraryDependencyTypeEdge>.Create(
            static (left, right) => CompareRankedPair(
                left.Counts.Total,
                right.Counts.Total,
                left.SourceTypeKey,
                left.TargetTypeKey,
                right.SourceTypeKey,
                right.TargetTypeKey));

    private static IComparer<LibraryDependencyExternalTypeEdge>
        ExternalTypeEdgeIdentity() =>
        Comparer<LibraryDependencyExternalTypeEdge>.Create(
            static (left, right) => ComparePair(
                left.SourceTypeKey,
                left.ExternalKey,
                right.SourceTypeKey,
                right.ExternalKey));

    private static IComparer<LibraryDependencyExternalTypeEdge>
        ExternalTypeEdgeVolume() =>
        Comparer<LibraryDependencyExternalTypeEdge>.Create(
            static (left, right) => CompareRankedPair(
                left.Counts.Total,
                right.Counts.Total,
                left.SourceTypeKey,
                left.ExternalKey,
                right.SourceTypeKey,
                right.ExternalKey));

    private static IComparer<LibraryDependencyNamespaceNode>
        NamespaceNodeIdentity() =>
        Comparer<LibraryDependencyNamespaceNode>.Create(
            static (left, right) => StringComparer.Ordinal.Compare(
                left.Namespace,
                right.Namespace));

    private static IComparer<LibraryDependencyNamespaceNode>
        NamespaceNodeTypeCount() =>
        Comparer<LibraryDependencyNamespaceNode>.Create(
            static (left, right) =>
                CompareRanked(
                    left.TypeCount,
                    right.TypeCount,
                    left.Namespace,
                    right.Namespace));

    private static IComparer<LibraryDependencyNamespaceEdge>
        NamespaceEdgeIdentity() =>
        Comparer<LibraryDependencyNamespaceEdge>.Create(
            static (left, right) => ComparePair(
                left.SourceNamespace,
                left.TargetNamespace,
                right.SourceNamespace,
                right.TargetNamespace));

    private static IComparer<LibraryDependencyNamespaceEdge>
        NamespaceEdgeVolume() =>
        Comparer<LibraryDependencyNamespaceEdge>.Create(
            static (left, right) => CompareRankedPair(
                left.Counts.Total,
                right.Counts.Total,
                left.SourceNamespace,
                left.TargetNamespace,
                right.SourceNamespace,
                right.TargetNamespace));

    private static IComparer<
        LibraryDependencyExternalNamespaceEdge>
        ExternalNamespaceEdgeIdentity() =>
        Comparer<
            LibraryDependencyExternalNamespaceEdge>.Create(
                static (left, right) => ComparePair(
                    left.SourceNamespace,
                    left.ExternalKey,
                    right.SourceNamespace,
                    right.ExternalKey));

    private static IComparer<
        LibraryDependencyExternalNamespaceEdge>
        ExternalNamespaceEdgeVolume() =>
        Comparer<
            LibraryDependencyExternalNamespaceEdge>.Create(
                static (left, right) => CompareRankedPair(
                    left.Counts.Total,
                    right.Counts.Total,
                    left.SourceNamespace,
                    left.ExternalKey,
                    right.SourceNamespace,
                    right.ExternalKey));

    private static IComparer<LibraryDependencyNamespaceCycleRow>
        CycleIdentity() =>
        Comparer<LibraryDependencyNamespaceCycleRow>.Create(
            static (left, right) =>
                left.CycleIndex.CompareTo(right.CycleIndex));

    private static IComparer<LibraryDependencyNamespaceCycleRow>
        CycleSize() =>
        Comparer<LibraryDependencyNamespaceCycleRow>.Create(
            static (left, right) =>
            {
                int count = right.Namespaces.Length.CompareTo(
                    left.Namespaces.Length);
                return count != 0
                    ? count
                    : left.CycleIndex.CompareTo(right.CycleIndex);
            });

    private static class Registration
    {
        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyTypeNode> TypeNodes =
            CreateTypeNodesScope();

        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyExternalNode> ExternalNodes =
            CreateExternalNodesScope();

        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyTypeEdge> TypeEdges =
            CreateTypeEdgesScope();

        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyExternalTypeEdge> ExternalTypeEdges =
            CreateExternalTypeEdgesScope();

        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyNamespaceNode> NamespaceNodes =
            CreateNamespaceNodesScope();

        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyNamespaceEdge> NamespaceEdges =
            CreateNamespaceEdgesScope();

        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyExternalNamespaceEdge>
            ExternalNamespaceEdges =
            CreateExternalNamespaceEdgesScope();

        internal static readonly QuerySpaceRowScopeBinding<
            LibraryDependencyNamespaceCycleRow> Cycles =
            CreateCyclesScope();

        internal static readonly IReadOnlyList<
            QuerySpaceRowScopeBinding> All =
        [
            TypeNodes,
            ExternalNodes,
            TypeEdges,
            ExternalTypeEdges,
            NamespaceNodes,
            NamespaceEdges,
            ExternalNamespaceEdges,
            Cycles,
        ];
    }

    private static QuerySpaceRowScopeBinding<TRow> Scope<TRow>(
        string identity,
        string rowSet,
        IReadOnlyList<RowQueryKey<TRow>> keys,
        IReadOnlyList<RowQueryNamedOrder<TRow>> orders,
        string baselineOrder,
        string? topOrder,
        IReadOnlyList<RowSelectionStageKind> stages)
    {
        RowQueryNamedOrder<TRow> baseline =
            orders.Single(order =>
                order.Key == baselineOrder);
        RowQueryNamedOrder<TRow>? top =
            topOrder is null
                ? null
                : orders.Single(order =>
                    order.Key == topOrder);
        return new(
            new QuerySpaceRowScopeDescriptor(
                identity,
                $"{identity}/result",
                [rowSet],
                [
                    .. keys.Select(key => Facet(
                        $"{identity}/{key.Key}",
                        key.Key,
                        key.Operators,
                        supportsOrdering:
                            key.SupportsOrdering)),
                ],
                [
                    .. orders.Select(order => new
                        QuerySpaceRowOrderDescriptor(
                            order.Key,
                            order.Purpose
                                == RowQueryOrderPurpose.Ranking)),
                ],
                stages),
            RowQueryVocabulary<TRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                keys,
                orders,
                defaultBaselineOrder:
                    new(
                        baseline,
                        RowQueryOrderDirection.Ascending),
                defaultTopRanking:
                    top is null
                        ? null
                        : new(
                            top,
                            RowQueryOrderDirection.Ascending)));
    }

    private static IReadOnlyList<RowQueryKey<TRow>> EdgeKeys<TRow>(
        Func<TRow, string> source,
        Func<TRow, string> target,
        Func<TRow, LibraryDependencyCounts> counts,
        string sourceKey,
        string targetKey,
        string invocationCountKey,
        string functionReferenceCountKey,
        string relationshipCountKey) =>
    [
        OrdinalKey(sourceKey, source),
        OrdinalKey(targetKey, target),
        NumericKey<TRow>(
            invocationCountKey,
            row => counts(row).Invocations),
        NumericKey<TRow>(
            functionReferenceCountKey,
            row => counts(row).FunctionReferences),
        NumericKey<TRow>(
            relationshipCountKey,
            row => counts(row).Total),
    ];

    private static IReadOnlyList<RowQueryKey<TRow>>
        NamespaceEdgeKeys<TRow>(
            Func<TRow, string> source,
            Func<TRow, string> target,
            Func<TRow, LibraryDependencyCounts> counts,
            Func<TRow, int> contributorCount,
            Func<TRow, int> remainingCount,
            string sourceKey,
            string targetKey,
            string invocationCountKey,
            string functionReferenceCountKey,
            string relationshipCountKey,
            string contributorCountKey,
            string remainingCountKey) =>
    [
        OrdinalKey(sourceKey, source),
        OrdinalKey(targetKey, target),
        NumericKey<TRow>(
            invocationCountKey,
            row => counts(row).Invocations),
        NumericKey<TRow>(
            functionReferenceCountKey,
            row => counts(row).FunctionReferences),
        NumericKey<TRow>(
            relationshipCountKey,
            row => counts(row).Total),
        NumericKey<TRow>(
            contributorCountKey,
            contributorCount),
        NumericKey<TRow>(
            remainingCountKey,
            remainingCount),
    ];

    private static RowQueryNamedOrder<TRow> NamedOrder<TRow>(
        string reference,
        RowQueryOrderPurpose purpose,
        IComparer<TRow> comparer) =>
        new(
            RowQueryNamedOrderIdentity.Create(),
            reference,
            purpose,
            direction => Directional(comparer, direction));

    private static RowQueryKey<TRow> OrdinalKey<TRow>(
        string key,
        Func<TRow, string> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            EqualityOperators(),
            row => RowQueryValue<string>.Present(accessor(row)),
            BindOrdinalText,
            direction => RowQueryValueOrder.Create(
                (IComparer<string>)StringComparer.Ordinal,
                direction,
                missingLast: false));

    private static RowQueryKey<TRow> OptionalOrdinalKey<TRow>(
        string key,
        Func<TRow, string?> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            EqualityOperators(),
            row => accessor(row) is { } value
                ? RowQueryValue<string>.Present(value)
                : RowQueryValue<string>.Missing,
            BindOrdinalText,
            direction => RowQueryValueOrder.Create(
                (IComparer<string>)StringComparer.Ordinal,
                direction,
                missingLast: true));

    private static RowQueryKey<TRow> MembershipKey<TRow>(
        string key,
        Func<TRow, IEnumerable<string>> accessor)
        where TRow : notnull =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            EqualityOperators(),
            row => RowQueryValue<TRow>.Present(row),
            (operation, token) =>
                operation switch
                {
                    RowQueryOperator.Equals =>
                        row => accessor(row).Contains(
                            token.Text,
                            StringComparer.Ordinal),
                    RowQueryOperator.NotEquals =>
                        row => !accessor(row).Contains(
                            token.Text,
                            StringComparer.Ordinal),
                    _ => null,
                });

    private static RowQueryKey<TRow> NumericKey<TRow>(
        string key,
        Func<TRow, int> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            NumericOperators(),
            row => RowQueryValue<int>.Present(accessor(row)),
            BindInteger,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missingLast: false));

    private static RowQueryKey<TRow> OptionalNumericKey<TRow>(
        string key,
        Func<TRow, int?> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            NumericOperators(),
            row => accessor(row) is int value
                ? RowQueryValue<int>.Present(value)
                : RowQueryValue<int>.Missing,
            BindInteger,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missingLast: true));

    private static RowQueryKey<TRow> BooleanKey<TRow>(
        string key,
        Func<TRow, bool> accessor) =>
        RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            EqualityOperators(),
            row => RowQueryValue<bool>.Present(accessor(row)),
            BindBoolean,
            direction => RowQueryValueOrder.Create(
                Comparer<bool>.Default,
                direction,
                missingLast: false));

    private static Predicate<string>? BindOrdinalText(
        RowQueryOperator operation,
        RowQueryValueToken token) =>
        operation switch
        {
            RowQueryOperator.Equals =>
                value => string.Equals(
                    value,
                    token.Text,
                    StringComparison.Ordinal),
            RowQueryOperator.NotEquals =>
                value => !string.Equals(
                    value,
                    token.Text,
                    StringComparison.Ordinal),
            _ => null,
        };

    private static Predicate<int>? BindInteger(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!int.TryParse(
                token.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int expected)
            || expected < 0)
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            RowQueryOperator.GreaterOrEqual =>
                value => value >= expected,
            RowQueryOperator.LessOrEqual =>
                value => value <= expected,
            _ => null,
        };
    }

    private static Predicate<bool>? BindBoolean(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!bool.TryParse(token.Text, out bool expected))
            return null;
        return operation switch
        {
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            _ => null,
        };
    }

    private static QuerySpaceRowFacetDescriptor Facet(
        string identity,
        string key,
        IReadOnlyList<RowQueryOperator> operators,
        bool supportsOrdering) =>
        new(
            identity,
            key,
            [.. operators.Select(ToPortableOperator)],
            ValueKind(key),
            valueVocabulary: null,
            Label(key),
            [],
            $"Matches the owner-issued {Label(key)}.",
            supportsOrdering);

    private static string ValueKind(string key) =>
        key.EndsWith("-count", StringComparison.Ordinal)
            || key.EndsWith("-index", StringComparison.Ordinal)
            || key.EndsWith("-level", StringComparison.Ordinal)
                ? "non-negative integer"
                : key.EndsWith(
                        "-global",
                        StringComparison.Ordinal)
                    || key.EndsWith(
                        "-intrinsic-core-library",
                        StringComparison.Ordinal)
                    ? "boolean"
                    : "ordinal text";

    private static string Label(string key) =>
        key.Replace('-', ' ');

    private static PortableQueryOperator ToPortableOperator(
        RowQueryOperator operation) =>
        operation switch
        {
            RowQueryOperator.Equals => PortableQueryOperator.Equal,
            RowQueryOperator.NotEquals =>
                PortableQueryOperator.NotEqual,
            RowQueryOperator.GreaterOrEqual =>
                PortableQueryOperator.AtLeast,
            RowQueryOperator.LessOrEqual =>
                PortableQueryOperator.AtMost,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation)),
        };

    private static int CompareRanked(
        int leftCount,
        int rightCount,
        string leftIdentity,
        string rightIdentity)
    {
        int count = rightCount.CompareTo(leftCount);
        return count != 0
            ? count
            : StringComparer.Ordinal.Compare(
                leftIdentity,
                rightIdentity);
    }

    private static int ComparePair(
        string leftFirst,
        string leftSecond,
        string rightFirst,
        string rightSecond)
    {
        int first = StringComparer.Ordinal.Compare(
            leftFirst,
            rightFirst);
        return first != 0
            ? first
            : StringComparer.Ordinal.Compare(
                leftSecond,
                rightSecond);
    }

    private static int CompareRankedPair(
        int leftCount,
        int rightCount,
        string leftFirst,
        string leftSecond,
        string rightFirst,
        string rightSecond)
    {
        int count = rightCount.CompareTo(leftCount);
        return count != 0
            ? count
            : ComparePair(
                leftFirst,
                leftSecond,
                rightFirst,
                rightSecond);
    }

    private static IComparer<T> Directional<T>(
        IComparer<T> ascending,
        RowQueryOrderDirection direction) =>
        direction == RowQueryOrderDirection.Ascending
            ? ascending
            : Comparer<T>.Create(
                (left, right) => ascending.Compare(right, left));
}
