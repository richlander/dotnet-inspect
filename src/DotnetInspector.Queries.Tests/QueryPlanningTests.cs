namespace DotnetInspector.Queries.Tests;

public class QueryPlanningTests
{
    [Fact]
    public async Task Plan_ExecutesDependenciesFirstAndOnce()
    {
        var shared = Definition("shared");
        var left = Definition("left");
        var right = Definition("right");
        var root = Definition("root");
        var catalog = new QueryCatalogBuilder<List<string>>()
            .Add(root, left, right)
            .Add(right, shared)
            .Add(shared)
            .Add(left, shared)
            .Build();

        var plan = catalog.Plan(root, left);
        List<string> execution = [];
        var results = await plan.ExecuteAsync(
            execution,
            QueryExecutionPolicy.NetworkFree,
            TestContext.Current.CancellationToken);

        Assert.Equal(["shared", "left", "right", "root"], execution);
        Assert.Equal(
            ["shared", "left", "right", "root"],
            plan.Queries.Select(static query => query.Name));
        Assert.Equal("root", results.RequireValue(root));
    }

    [Fact]
    public void Plan_RejectsDependencyCycle()
    {
        var first = Definition("first");
        var second = Definition("second");
        var catalog = new QueryCatalogBuilder<List<string>>()
            .Add(first, second)
            .Add(second, first)
            .Build();

        var exception = Assert.Throws<QueryPlanException>(() => catalog.Plan(first));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsUnregisteredQuery()
    {
        var registered = Definition("registered");
        var unregistered = Definition("unregistered");
        var catalog = new QueryCatalogBuilder<List<string>>()
            .Add(registered)
            .Build();

        var exception = Assert.Throws<QueryPlanException>(
            () => catalog.Plan(unregistered));

        Assert.Contains("not registered", exception.Message);
    }

    [Fact]
    public async Task Execute_PreflightsCostBeforeRunningQueries()
    {
        var query = Definition("moderated", QueryCost.Moderated);
        var plan = new QueryCatalogBuilder<List<string>>()
            .Add(query)
            .Build()
            .Plan(query);
        List<string> execution = [];

        await Assert.ThrowsAsync<QueryPolicyException>(
            () => plan.ExecuteAsync(
                execution,
                QueryExecutionPolicy.NetworkFree,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(execution);
    }

    [Fact]
    public async Task Execute_PreflightsCapabilitiesBeforeRunningQueries()
    {
        var query = Definition(
            "network",
            QueryCost.NetworkFree,
            QueryCapabilities.Network);
        var plan = new QueryCatalogBuilder<List<string>>()
            .Add(query)
            .Build()
            .Plan(query);
        List<string> execution = [];

        await Assert.ThrowsAsync<QueryPolicyException>(
            () => plan.ExecuteAsync(
                execution,
                QueryExecutionPolicy.NetworkFree,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(execution);
    }

    [Fact]
    public async Task Execute_PreservesTypedFailure()
    {
        QueryDefinition<List<string>, string> query = new(
            "failure",
            QueryCost.NetworkFree,
            QueryCapabilities.None,
            static (_, _, _) => ValueTask.FromResult(
                QueryResult<string>.Failed(
                    new QueryFailure("fixture", "expected failure"))));
        var plan = new QueryCatalogBuilder<List<string>>()
            .Add(query)
            .Build()
            .Plan(query);

        var results = await plan.ExecuteAsync(
            [],
            QueryExecutionPolicy.NetworkFree,
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<QueryResult<string>.Failure>(results.Get(query));
        Assert.Equal("fixture", failure.Error.Code);
        var exception = Assert.Throws<QueryFailedException>(
            () =>
            {
                results.RequireValue(query);
            });
        Assert.Equal(failure.Error, exception.Failure);
    }

    private static QueryDefinition<List<string>, string> Definition(
        string name,
        QueryCost cost = QueryCost.NetworkFree,
        QueryCapabilities capabilities = QueryCapabilities.None)
        => new(
            name,
            cost,
            capabilities,
            (execution, _, _) =>
            {
                execution.Add(name);
                return ValueTask.FromResult(
                    QueryResult<string>.Succeeded(name));
            });
}
