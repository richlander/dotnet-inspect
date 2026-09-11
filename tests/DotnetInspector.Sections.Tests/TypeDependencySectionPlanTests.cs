using DotnetInspector.RowSelection;
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
            RowSelectionIntent<TypeDependencyRowOrder>.Create(
                [
                    RowSelectionIntentOperation<
                        TypeDependencyRowOrder>.Head(2),
                ]));

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
            RowSelectionIntent<TypeDependencyRowOrder>.Create(
                [
                    RowSelectionIntentOperation<
                        TypeDependencyRowOrder>.Window(2, 4),
                ]));

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
    public void Plan_RejectsRankingWithoutAnOwnedOrder()
    {
        Assert.Throws<ArgumentException>(
            () => new TypeDependencySectionPlan(
                "Demo.Consumer",
                RowSelectionIntent<TypeDependencyRowOrder>.Create(
                    [
                        RowSelectionIntentOperation<
                            TypeDependencyRowOrder>.Top(
                                1,
                                TypeDependencyRowOrder.Traversal),
                    ])));
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
}
