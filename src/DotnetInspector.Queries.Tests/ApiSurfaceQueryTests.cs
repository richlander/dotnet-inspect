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

    [Fact]
    public void Execute_WithSurfaceFactory_UsesResolutionAwareExtraction()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(ApiSurfaceQueryTests).Assembly.Location);
        var expected = new ApiSurface { Name = "resolution-aware" };
        bool called = false;

        var result = ApiSurfaceQuery.Execute(
            new ApiSurfaceQueryContext(
                session,
                IncludeAll: true,
                TypesOnly: false,
                SurfaceFactory: (includeAll, typesOnly) =>
                {
                    Assert.True(includeAll);
                    Assert.False(typesOnly);
                    called = true;
                    return expected;
                }));

        var available = Assert.IsType<ApiSurfaceResult.Available>(result);
        Assert.True(called);
        Assert.Same(expected, available.Surface);
    }
}
