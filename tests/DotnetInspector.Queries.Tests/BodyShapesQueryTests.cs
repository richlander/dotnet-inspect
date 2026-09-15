using DotnetInspector.Fixtures;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Queries.Tests;

public sealed class BodyShapesQueryTests
{
    static string FixturePath => typeof(BodyShapeFixture).Assembly.Location;

    [Fact]
    public void Execute_ReturnsTypedMatchesFromCompiledAssembly()
    {
        using var source = MetadataSource.Open(FixturePath);

        BodyShapesResult result = BodyShapesQuery.Execute(
            source,
            "ObjectCreationExpression");

        var available = Assert.IsType<BodyShapesResult.Available>(result);
        Assert.Contains(
            available.Search.Matches,
            match => match.MethodName == nameof(BodyShapeFixture.PublicCreation));
        Assert.All(
            available.Search.Matches,
            match => Assert.Equal("ObjectCreationExpression", match.Kind));
        Assert.Equal(InspectionCost.Unbounded, BodyShapesQuery.Definition.Cost);
    }

    [Fact]
    public void Execute_EmptyTokenScopeSearchesNoBodies()
    {
        using var source = MetadataSource.Open(FixturePath);

        BodyShapesResult result = BodyShapesQuery.Execute(
            source,
            "ObjectCreationExpression",
            new HashSet<int>());

        var available = Assert.IsType<BodyShapesResult.Available>(result);
        Assert.Empty(available.Search.Matches);
        Assert.Equal(0, available.Search.MethodsInspected);
    }

    [Fact]
    public void Execute_InvalidKindRemainsVisibleAsTypedFailure()
    {
        using var source = MetadataSource.Open(FixturePath);

        BodyShapesResult result = BodyShapesQuery.Execute(source, "NotAKind");

        var failed = Assert.IsType<BodyShapesResult.Failed>(result);
        Assert.IsType<ArgumentException>(failed.Error);
    }
}
