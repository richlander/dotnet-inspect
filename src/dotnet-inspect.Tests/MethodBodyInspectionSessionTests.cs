using DotnetInspector.Inspectors;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests the command-policy and cross-assembly composition owned by
/// <see cref="MethodBodyInspectionSession"/>. Neutral index queries are tested by Analysis.
/// </summary>
public class MethodBodyInspectionSessionTests
{
    static string ProductPath => typeof(MethodBodyInspectionSession).Assembly.Location;
    static string TestPath => typeof(MethodBodyInspectionSessionTests).Assembly.Location;

    static int CalledToken(Analysis.LibraryBodyIndex index)
    {
        var methodTokens = index.Methods.Select(m => m.MetadataToken).ToHashSet();
        return index.DirectCalls
            .Select(call => call.CalleeDefinitionToken)
            .First(token => methodTokens.Contains(token));
    }

    [Fact]
    public void Open_ExposesConfiguredNeutralIndex()
    {
        var session = MethodBodyInspectionSession.Open(
            ProductPath,
            includeAllocations: false,
            includeOpportunities: false);

        Assert.NotEmpty(session.BodyIndex.Methods);
        Assert.Empty(session.BodyIndex.GetAllocationOccurrences());
        Assert.Empty(session.BodyIndex.OptimizationOpportunities);
    }

    [Fact]
    public void Open_HonorsBodyScope()
    {
        var fullIndex = Analysis.LibraryBodyIndex.Open(ProductPath);
        var token = fullIndex.GetDirectCallsByCaller().First(entry => entry.Value.Length > 0).Key;

        var scopedIndex = MethodBodyInspectionSession.Open(
            ProductPath,
            includeAllocations: false,
            includeOpportunities: false,
            bodyScope: new HashSet<int> { token }).BodyIndex;

        Assert.NotEmpty(scopedIndex.DirectCalls);
        Assert.All(scopedIndex.DirectCalls, call => Assert.Equal(token, call.Caller.MetadataToken));
    }

    [Fact]
    public void SourceName_MatchesAssemblyFileName()
    {
        var expected = Path.GetFileNameWithoutExtension(ProductPath);
        Assert.Equal(expected, MethodBodyInspectionSession.Open(ProductPath).SourceName);
    }

    [Fact]
    public void CallerEdges_MatchSameAssemblyCalls_AndAttributeSource()
    {
        var index = Analysis.LibraryBodyIndex.Open(ProductPath);
        var targetToken = CalledToken(index);
        var identity = index.Methods.First(method => method.MetadataToken == targetToken);
        var pattern = Analysis.MemberPattern.Method(identity.DeclaringType, identity.Name, identity.ParameterTypes);
        var expected = index.DirectCalls
            .Where(call => call.CalleeDefinitionToken == targetToken || pattern.Matches(call.Callee))
            .ToList();

        var session = MethodBodyInspectionSession.Open(ProductPath);
        var actual = session.CallerEdges(targetToken);

        Assert.NotEmpty(actual);
        Assert.Equal(expected.Count, actual.Length);
        Assert.All(actual, edge => Assert.Equal(session.SourceName, edge.Source));
        Assert.Equal(
            expected.Select(call => (call.Caller.MetadataToken, call.ILOffset)).OrderBy(value => value),
            actual.Select(edge => (edge.Call.Caller.MetadataToken, edge.Call.ILOffset)).OrderBy(value => value));
    }

    [Fact]
    public void CallerEdges_IncludeAndAttributeCallerScopeAssemblies()
    {
        var target = MethodBodyInspectionSession.Open(ProductPath);
        var openMethod = target.BodyIndex.Methods.Single(method =>
            method.DeclaringType.Name == nameof(MethodBodyInspectionSession)
            && method.Name == nameof(MethodBodyInspectionSession.Open));
        var scope = MethodBodyInspectionSession.Open(TestPath);

        var actual = target.CallerEdges(openMethod.MetadataToken, [scope]);

        Assert.Contains(actual, edge => edge.Source == scope.SourceName);
    }

    [Fact]
    public void CallerEdges_UnknownToken_ReturnsEmpty()
        => Assert.Empty(MethodBodyInspectionSession.Open(ProductPath).CallerEdges(targetToken: 0));

    [Fact]
    public void CallerTree_SessionScopes_MatchesNeutralIndexComposition()
    {
        var index = Analysis.LibraryBodyIndex.Open(ProductPath);
        var token = CalledToken(index);

        var expected = index.BuildCallerTree(token, [Analysis.LibraryBodyIndex.Open(TestPath)]);
        var actual = MethodBodyInspectionSession.Open(ProductPath)
            .CallerTree(token, [MethodBodyInspectionSession.Open(TestPath)]);

        Assert.Equal(expected.Children.Count(), actual.Children.Count());
    }
}
