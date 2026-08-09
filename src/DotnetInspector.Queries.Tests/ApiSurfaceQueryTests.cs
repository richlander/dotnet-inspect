using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class ApiSurfaceQueryTests
{
    [Fact]
    public void RegistryRun_ExtractsApiSurfaceFromOpenSession()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(ApiSurfaceQueryTests).Assembly.Location);
        var registry = new InspectionQueryRegistry<ApiSurfaceQueryContext>()
            .Add(ApiSurfaceQuery.Definition, ApiSurfaceQuery.Execute);

        var results = registry.Run(
            [ApiSurfaceQuery.Definition],
            new ApiSurfaceQueryContext(session, IncludeAll: true));

        var available = Assert.IsType<ApiSurfaceResult.Available>(
            results.Get(ApiSurfaceQuery.Definition));
        Assert.Contains(
            available.Surface.Types,
            type => type.FullName == typeof(ApiSurfaceQueryTests).FullName);
    }

    [Fact]
    public void Execute_WithDisposedSession_ReturnsTypedFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(ApiSurfaceQueryTests).Assembly.Location);
        session.Dispose();

        var result = ApiSurfaceQuery.Execute(
            new ApiSurfaceQueryContext(session, IncludeAll: true));

        var failed = Assert.IsType<ApiSurfaceResult.Failed>(result);
        Assert.NotNull(failed.Error);
    }
}
