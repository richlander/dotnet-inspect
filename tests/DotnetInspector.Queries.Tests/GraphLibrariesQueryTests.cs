using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;
using DotnetInspector.Queries;

namespace DotnetInspector.Queries.Tests;

public sealed class GraphLibrariesQueryTests
{
    [Fact]
    public void OperationRoute_ProjectsExecutableClusterSelector()
    {
        IQueryOperationRoute route = GraphLibrariesQuery.OperationRoute;

        Assert.Equal(GraphLibrariesQuery.OperationRouteIdentity, route.Identity);
        Assert.Equal(
            GraphLibrariesQuery.OperationIdentity,
            route.OperationIdentity);
        Assert.Equal(
            GraphLibrariesQuery.OperationSubjectRole,
            route.SubjectRole);
        Assert.Equal(
            GraphLibrariesQuery.OperationResultGrain,
            route.ResultGrain);
        Assert.Equal(GraphLibrariesQuery.RowSets, route.RowSets);
        Assert.Equal(
            GraphLibrariesQuery.OperationProfileIdentity,
            route.ProfileIdentity);
        Assert.Equal("graph-libraries/v1", route.Capabilities.Vocabulary);
        Assert.Empty(route.Capabilities.Orders);
        Assert.Empty(route.Capabilities.Dimensions);
        Assert.Empty(route.Capabilities.Stages);

        QueryOperationTermCapability capability =
            Assert.Single(route.Capabilities.Terms);
        Assert.Equal(
            GraphLibrariesQuery.ClusterTermKey,
            capability.Binding.Key);
        Assert.Equal(
            QueryOperationTermRole.OperationSelector,
            capability.Binding.Role);
        Assert.Equal(
            [PortableQueryOperator.Equal],
            capability.Operators);
        GraphLibrariesQueryRegisteredTerm registered =
            Assert.Single(GraphLibrariesQuery.RegisteredTerms);
        Assert.Equal(capability.Binding.Key, registered.Descriptor.Key);
        Assert.Equal(capability.Operators, registered.Operators);
        Assert.Equal("3", registered.Descriptor.ExampleValue);
    }

    [Fact]
    public void OperationBackedRowSets_InheritTheEffectiveClusterSelector()
    {
        foreach (string rowSet in GraphLibrariesQuery.RowSets)
        {
            IQueryOperationRoute route =
                GraphLibrariesQuery.RouteForRowSet(rowSet);

            Assert.Equal(
                GraphLibrariesQuery.OperationIdentity,
                route.OperationIdentity);
            Assert.Equal([rowSet], route.RowSets);
            Assert.Equal(
                GraphLibrariesQuery.OperationProfileIdentity,
                route.ProfileIdentity);
            GraphLibrariesQueryRegisteredTerm registered =
                Assert.Single(
                    GraphLibrariesQuery.RegisteredTermsForRowSet(rowSet));
            Assert.Equal(
                GraphLibrariesQuery.ClusterTermKey,
                registered.Descriptor.Key);
            Assert.Equal(
                [PortableQueryOperator.Equal],
                registered.Operators);
        }
    }

    [Fact]
    public void QuerySpace_DeclaresEveryOperationRowSetAndSemanticCapability()
    {
        QuerySpaceBinding querySpace = GraphLibrariesQuery.QuerySpace;

        Assert.Equal(
            GraphLibrariesQuery.RowSets,
            querySpace.Descriptor.Operation.RowSets);
        Assert.Equal(5, querySpace.RowScopes.Count);
        Assert.Collection(
            querySpace.RowScopes,
            scope => AssertSelectableScope(
                scope,
                GraphLibrariesQuery.ConsumerUseSitesRowScope,
                GraphLibrariesQuery.ConsumerUseSitesRowSet),
            scope => AssertSelectableScope(
                scope,
                GraphLibrariesQuery.ProviderApiTypesRowScope,
                GraphLibrariesQuery.ProviderApiTypesRowSet),
            scope => AssertSelectableScope(
                scope,
                GraphLibrariesQuery.DirectUseClustersRowScope,
                GraphLibrariesQuery.DirectUseClustersRowSet),
            scope => AssertSelectableScope(
                scope,
                GraphLibrariesQuery.CallSitesRowScope,
                GraphLibrariesQuery.CallSitesRowSet),
            scope =>
            {
                Assert.Same(
                    GraphLibrariesQuery.PublicRootPathsRowScope,
                    scope);
                Assert.Equal(
                    [GraphLibrariesQuery.PublicRootPathsRowSet],
                    scope.Descriptor.RowSets);
                Assert.Empty(scope.Descriptor.Stages);
            });
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            querySpace.Descriptor.Terminals);
    }

    [Fact]
    public void ResolveIntent_ProducesCanonicalOptionalClusterPlan()
    {
        var empty = Assert.IsType<GraphLibrariesQueryPlanResult.Accepted>(
            Resolve(PortableQueryIntent.Empty));
        Assert.Null(empty.Plan.Cluster);
        Assert.Same(PortableQueryIntent.Empty, empty.Plan.Intent);

        PortableQueryIntent expected = GraphLibrariesQuery.CreateIntent(3);
        var selected = Assert.IsType<GraphLibrariesQueryPlanResult.Accepted>(
            Resolve(expected));
        Assert.Equal(3, selected.Plan.Cluster);
        Assert.Equal(
            PortableQueryPayloadCodec.Encode(
                expected,
                TestContext.Current.CancellationToken),
            PortableQueryPayloadCodec.Encode(
                selected.Plan.Intent,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(
        "Cluster",
        PortableQueryOperator.NotEqual,
        "3",
        PortableQueryFailureReason.OperatorNotAdmitted)]
    [InlineData(
        "Cluster",
        PortableQueryOperator.Equal,
        "0",
        PortableQueryFailureReason.ValueRejected)]
    [InlineData(
        "Unknown",
        PortableQueryOperator.Equal,
        "3",
        PortableQueryFailureReason.UnknownKey)]
    public void ResolveIntent_RejectsUnsupportedTermsBeforeCreatingAPlan(
        string key,
        PortableQueryOperator @operator,
        string value,
        PortableQueryFailureReason expectedReason)
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [new(key, @operator, value)],
            [],
            [],
            []);

        var rejected = Assert.IsType<GraphLibrariesQueryPlanResult.Rejected>(
            Resolve(intent));

        Assert.Equal(expectedReason, rejected.Failure.Reason);
    }

    [Fact]
    public void ResolveIntent_RejectsTwoDifferentClusterSelectors()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new(
                    GraphLibrariesQuery.ClusterTermKey,
                    PortableQueryOperator.Equal,
                    "2"),
                new(
                    GraphLibrariesQuery.ClusterTermKey,
                    PortableQueryOperator.Equal,
                    "3"),
            ],
            [],
            [],
            []);

        var rejected = Assert.IsType<GraphLibrariesQueryPlanResult.Rejected>(
            Resolve(intent));

        Assert.Equal(
            PortableQueryFailureReason.TermsIncompatible,
            rejected.Failure.Reason);
    }

    [Fact]
    public void ApplyWithoutClusterPreservesTheCompleteProjection()
    {
        var pair = new AssemblyPairCallUseResult(
            [],
            [],
            [],
            [],
            AssemblyPairCallUseDiagnostics.Empty);
        var projection =
            new AssemblyPairDirectUseClusterProjection(pair, []);
        var accepted = Assert.IsType<GraphLibrariesQueryPlanResult.Accepted>(
            Resolve(PortableQueryIntent.Empty));

        Assert.Same(
            projection,
            GraphLibrariesQuery.Apply(accepted.Plan, projection));
    }

    private static GraphLibrariesQueryPlanResult Resolve(
        PortableQueryIntent intent) =>
        GraphLibrariesQuery.ResolveIntent(
            intent,
            TestContext.Current.CancellationToken);

    private static void AssertSelectableScope(
        QuerySpaceRowScopeBinding actual,
        QuerySpaceRowScopeBinding expected,
        string rowSet)
    {
        Assert.Same(expected, actual);
        Assert.Equal([rowSet], actual.Descriptor.RowSets);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ],
            actual.Descriptor.Stages);
    }
}
