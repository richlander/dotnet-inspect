using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class DependencyQueryTests
{
    [Fact]
    public void TypeRoute_ProjectsTheExistingRelationshipVocabulary()
    {
        IQueryOperationRoute route =
            DependencyQuery.Route(
                DependencyQueryRouteKind.TypeRelationships);

        Assert.Equal(
            [
                TypeDependencyVocabulary.SourceKey,
                TypeDependencyVocabulary.TargetKey,
                TypeDependencyVocabulary.KindKey,
            ],
            route.Capabilities.Terms.Select(term =>
                term.Binding.Key));
        Assert.Equal(
            [
                TypeDependencyVocabulary.SourceKey,
                TypeDependencyVocabulary.TargetKey,
                TypeDependencyVocabulary.KindKey,
                TypeDependencyVocabulary.TraversalOrderKey,
            ],
            route.Capabilities.Orders.Select(order =>
                order.Binding.Reference));
        Assert.Equal(
            [DependencyQuery.DepthDimension],
            route.Capabilities.Dimensions);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Top,
            ],
            route.Capabilities.Stages);
    }

    [Fact]
    public void TypeRoute_ResolvesAnExecutableRelationshipPlan()
    {
        DependencyQueryPlan plan = Accepted(
            DependencyQuery.ResolveIntent(
                DependencyQueryRouteKind.TypeRelationships,
                PortableQueryIntent.Create(
                    [
                        new(
                            TypeDependencyVocabulary.KindKey,
                            PortableQueryOperator.Equal,
                            "Interface"),
                    ],
                    [new(DependencyQuery.DepthDimension, 2)],
                    [PortableQueryStage.Top(1)],
                    [
                        PortableQueryOrderOperation.Fields(
                            PortableQueryOrderRole.ForStage(0),
                            [
                                new(
                                    TypeDependencyVocabulary.TargetKey,
                                    PortableQueryDirection.Descending),
                            ]),
                    ]),
                TestContext.Current.CancellationToken));
        RowQueryResolutionResult<TypeDependencyRelationship> resolution =
            TypeDependencyVocabulary.Resolve(
                plan.RelationshipRows);

        Assert.True(resolution.IsSuccess);
        Assert.Equal(2, plan.MaximumDepth);
        Assert.Empty(plan.HierarchyRows.Operations);
        var dependency = new TypeDependencyResult(
            "Demo.Consumer",
            [])
        {
            Relationships =
            [
                new(
                    "Demo.Consumer",
                    "Demo.IFirst",
                    TypeDependencyRelationshipKind.Interface,
                    1),
                new(
                    "Demo.Consumer",
                    "Demo.ISecond",
                    TypeDependencyRelationshipKind.Interface,
                    2),
                new(
                    "Demo.Consumer",
                    "Demo.Base",
                    TypeDependencyRelationshipKind.BaseType,
                    3),
            ],
        };
        var selection = TypeDependencySectionExecutor.Select(
            dependency,
            new TypeDependencySectionPlan(
                "Demo.Consumer",
                resolution.Plan!,
                plan.MaximumDepth));

        Assert.True(selection.IsSuccess);
        Assert.Equal(
            "Demo.ISecond",
            Assert.Single(selection.Relationships).TargetTypeName);
    }

    [Fact]
    public void HierarchyRoutes_ResolveEquivalentIntent()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [],
            [new(DependencyQuery.DepthDimension, 2)],
            [PortableQueryStage.Head(3)],
            []);

        DependencyQueryPlan command = Accepted(
            DependencyQuery.ResolveIntent(
                DependencyQueryRouteKind.AssetHierarchy,
                intent,
                TestContext.Current.CancellationToken));
        DependencyQueryPlan section = Accepted(
            DependencyQuery.ResolveIntent(
                DependencyQueryRouteKind.PackageHierarchy,
                intent,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            DependencyQuery.HierarchyProfileIdentity,
            DependencyQuery.Route(
                    DependencyQueryRouteKind.AssetHierarchy)
                .ProfileIdentity);
        Assert.Equal(
            DependencyQuery.HierarchyProfileIdentity,
            DependencyQuery.Route(
                    DependencyQueryRouteKind.PackageHierarchy)
                .ProfileIdentity);
        Assert.Equal(
            command.Intent.Bounds.Select(bound =>
                (bound.Dimension, bound.RequestedMaximum)),
            section.Intent.Bounds.Select(bound =>
                (bound.Dimension, bound.RequestedMaximum)));
        Assert.Equal(
            command.Intent.Stages.Select(stage =>
                (stage.Kind, stage.Count)),
            section.Intent.Stages.Select(stage =>
                (stage.Kind, stage.Count)));
        Assert.Equal(2, command.MaximumDepth);
        Assert.Equal(2, section.MaximumDepth);
        Assert.Single(command.HierarchyRows.Operations);
        Assert.Single(section.HierarchyRows.Operations);
        Assert.Equal(
            RowSelectionStageKind.Head,
            command.HierarchyRows.Operations[0].Kind);
        Assert.Empty(command.RelationshipRows.Predicates);
        Assert.Null(command.RelationshipRows.BaselineOrder);
        Assert.Empty(command.RelationshipRows.Selection.Operations);
    }

    [Fact]
    public void HierarchyRoutes_PreserveOrderedRowStages()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [],
            [],
            [
                PortableQueryStage.Window(2, 4),
                PortableQueryStage.Head(2),
            ],
            []);

        DependencyQueryPlan command = Accepted(
            DependencyQuery.ResolveIntent(
                DependencyQueryRouteKind.AssetHierarchy,
                intent,
                TestContext.Current.CancellationToken));
        DependencyQueryPlan section = Accepted(
            DependencyQuery.ResolveIntent(
                DependencyQueryRouteKind.PackageHierarchy,
                intent,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Head,
            ],
            command.HierarchyRows.Operations.Select(operation =>
                operation.Kind));
        Assert.Equal(
            command.HierarchyRows.Operations.Select(Operation),
            section.HierarchyRows.Operations.Select(Operation));

        static (RowSelectionStageKind Kind, int? Start, int? End, int Count)
            Operation(RowSelectionIntentOperation<string> operation) =>
            operation.Kind switch
            {
                RowSelectionStageKind.Window =>
                    (operation.Kind, operation.Start, operation.End, 0),
                _ => (operation.Kind, null, null, operation.Count),
            };
    }

    [Fact]
    public void TypeRoute_RejectsTraversalAsTopRanking()
    {
        DependencyQueryPlanResult result =
            DependencyQuery.ResolveIntent(
                DependencyQueryRouteKind.TypeRelationships,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Top(1)],
                    [
                        PortableQueryOrderOperation.Named(
                            PortableQueryOrderRole.ForStage(0),
                            TypeDependencyVocabulary.TraversalOrderKey,
                            PortableQueryDirection.Ascending),
                    ]),
                TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<DependencyQueryPlanResult.Rejected>(
                result);
        Assert.Equal(
            PortableQueryFailureReason.OrderNotARanking,
            rejected.Failure.Reason);
    }

    private static DependencyQueryPlan Accepted(
        DependencyQueryPlanResult result) =>
        Assert.IsType<DependencyQueryPlanResult.Accepted>(
            result).Plan;
}
