using QuerySpace.Rows;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeDependencySectionPlanTests
{
    [Fact]
    public void Select_AppliesTypedRelationshipRowsInTraversalOrder()
    {
        TypeDependencyResult dependency = Dependency();
        var plan = new TypeDependencySectionPlan(
            "Demo.Consumer",
            Resolve(
                RowQueryIntent.Create(
                    [],
                    baselineOrder: null,
                    RowSelectionIntent<RowQueryOrderIntent>.Create(
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Head(2),
                        ]))));

        TypeDependencyRowSelectionResult result =
            TypeDependencySectionExecutor.Select(
                dependency,
                plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Demo.Base", "Demo.IFirst"],
            result.Relationships.Select(
                static relationship =>
                    relationship.TargetTypeName));
    }

    [Fact]
    public void Select_PreservesTypedStrictWindowFailure()
    {
        TypeDependencyResult dependency = Dependency();
        var plan = new TypeDependencySectionPlan(
            "Demo.Consumer",
            Resolve(
                RowQueryIntent.Create(
                    [],
                    baselineOrder: null,
                    RowSelectionIntent<RowQueryOrderIntent>.Create(
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Window(2, 4),
                        ]))));

        TypeDependencyRowSelectionResult result =
            TypeDependencySectionExecutor.Select(
                dependency,
                plan);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Relationships);
        Assert.Equal(
            TypeDependencyRowSet.Relationships,
            result.Failure!.Identity);
        Assert.Equal(1, result.Failure.Failure.StageNumber);
        Assert.Equal(4, result.Failure.Failure.RequiredPosition);
        Assert.Equal(3, result.Failure.Failure.AvailableCount);
    }

    [Fact]
    public void Resolve_RejectsTraversalAsTopRanking()
    {
        RowQueryResolutionResult<TypeDependencyRelationship> result =
            TypeDependencyVocabulary.Resolve(
                RowQueryIntent.Create(
                    [],
                    baselineOrder: null,
                    RowSelectionIntent<RowQueryOrderIntent>.Create(
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    1,
                                    RowQueryOrderIntent.Named(
                                        "Traversal",
                                        RowQueryOrderDirection.Ascending)),
                        ])));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            RowQueryFailureReason.NamedOrderIsNotRanking,
            result.Failure!.Reason);
        Assert.Equal(1, result.Failure.SemanticStageNumber);
    }

    [Fact]
    public void Select_AppliesTypedPredicatesBeforeStableFieldOrder()
    {
        TypeDependencyResult dependency = Dependency();
        var plan = new TypeDependencySectionPlan(
            "Demo.Consumer",
            Resolve(
                RowQueryIntent.Create(
                    [
                        new RowQueryPredicateIntent(
                            "Kind",
                            RowQueryOperator.Equals,
                            new RowQueryValueToken("Interface")),
                    ],
                    RowQueryOrderIntent.Keys(
                        [
                            new RowQueryOrderTermIntent(
                                "Target",
                                RowQueryOrderDirection.Descending),
                        ]),
                    RowSelectionIntent<RowQueryOrderIntent>.Create(
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Head(1),
                        ]))));

        TypeDependencyRowSelectionResult result =
            TypeDependencySectionExecutor.Select(
                dependency,
                plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Demo.ISecond",
            Assert.Single(result.Relationships).TargetTypeName);
    }

    [Fact]
    public void Select_MatchesSourceAndTargetGlobsCaseInsensitively()
    {
        TypeDependencyResult dependency = Dependency();
        var plan = new TypeDependencySectionPlan(
            "Demo.Consumer",
            Resolve(
                RowQueryIntent.Create(
                    [
                        new RowQueryPredicateIntent(
                            "Source",
                            RowQueryOperator.Equals,
                            new RowQueryValueToken("demo.*")),
                        new RowQueryPredicateIntent(
                            "Target",
                            RowQueryOperator.Equals,
                            new RowQueryValueToken("*ifirst")),
                    ],
                    baselineOrder: null,
                    RowSelectionIntent<RowQueryOrderIntent>.Empty)));

        TypeDependencyRowSelectionResult result =
            TypeDependencySectionExecutor.Select(
                dependency,
                plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Demo.IFirst",
            Assert.Single(result.Relationships).TargetTypeName);
    }

    [Fact]
    public void Select_FieldOrderPreservesTraversalWithinEqualKeys()
    {
        TypeDependencyResult dependency = Dependency();
        var plan = new TypeDependencySectionPlan(
            "Demo.Consumer",
            Resolve(
                RowQueryIntent.Create(
                    [],
                    RowQueryOrderIntent.Keys(
                        [
                            new RowQueryOrderTermIntent(
                                "Kind",
                                RowQueryOrderDirection.Descending),
                        ]),
                    RowSelectionIntent<RowQueryOrderIntent>.Empty)));

        TypeDependencyRowSelectionResult result =
            TypeDependencySectionExecutor.Select(
                dependency,
                plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Demo.IFirst", "Demo.ISecond", "Demo.Base"],
            result.Relationships.Select(
                static relationship =>
                    relationship.TargetTypeName));
    }

    [Fact]
    public void Select_SupportsExplicitFieldRankingForTop()
    {
        TypeDependencyResult dependency = Dependency();
        var plan = new TypeDependencySectionPlan(
            "Demo.Consumer",
            Resolve(
                RowQueryIntent.Create(
                    [],
                    baselineOrder: null,
                    RowSelectionIntent<RowQueryOrderIntent>.Create(
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    1,
                                    RowQueryOrderIntent.Keys(
                                        [
                                            new RowQueryOrderTermIntent(
                                                "Target",
                                                RowQueryOrderDirection.Descending),
                                        ])),
                        ]))));

        TypeDependencyRowSelectionResult result =
            TypeDependencySectionExecutor.Select(
                dependency,
                plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Demo.ISecond",
            Assert.Single(result.Relationships).TargetTypeName);
    }

    [Fact]
    public void Resolve_RejectsInvalidKindValue()
    {
        RowQueryResolutionResult<TypeDependencyRelationship> result =
            TypeDependencyVocabulary.Resolve(
                RowQueryIntent.Create(
                    [
                        new RowQueryPredicateIntent(
                            "Kind",
                            RowQueryOperator.Equals,
                            new RowQueryValueToken("event")),
                    ],
                    baselineOrder: null,
                    RowSelectionIntent<RowQueryOrderIntent>.Empty));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            RowQueryFailureReason.InvalidValue,
            result.Failure!.Reason);
        Assert.Equal(
            RowQueryOperationKind.Predicate,
            result.Failure.OperationKind);
    }

    [Fact]
    public void Plan_RetainsMaximumTraversalDepth()
    {
        TypeDependencySectionPlan plan =
            TypeDependencySectionPlan.All(
                "Demo.Consumer",
                maximumDepth: 2);

        Assert.Equal(2, plan.MaximumDepth);
    }

    private static TypeDependencyResult Dependency() =>
        new(
            "Demo.Consumer",
            [])
        {
            Relationships =
            [
                new(
                    "Demo.Consumer",
                    "Demo.IFirst",
                    TypeDependencyRelationshipKind.Interface,
                    2),
                new(
                    "Demo.Consumer",
                    "Demo.Base",
                    TypeDependencyRelationshipKind.BaseType,
                    1),
                new(
                    "Demo.Consumer",
                    "Demo.ISecond",
                    TypeDependencyRelationshipKind.Interface,
                    3),
            ],
        };

    private static ResolvedRowQueryPlan<TypeDependencyRelationship>
        Resolve(RowQueryIntent intent)
    {
        RowQueryResolutionResult<TypeDependencyRelationship> result =
            TypeDependencyVocabulary.Resolve(intent);
        return result.Plan
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected Type Dependency query to resolve: "
                    + $"{result.Failure!.OperationKind}/"
                    + $"{result.Failure.Reason}.");
    }
}
