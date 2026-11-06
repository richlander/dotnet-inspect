using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataTypeLookupTests
{
    [Fact]
    public void Query_PrefersFullNameOverEarlierSuffixAndGenericMatches()
    {
        Candidate suffix = Create("Other.N", "Widget");
        Candidate generic = Create("N", "Widget`1");
        Candidate exact = Create("N", "Widget");

        var result = Query([suffix, generic, exact], "n.widget");

        Assert.Same(exact, Assert.Single(result.Matches));
        Assert.Equal("N.Widget", result.MatchedName);
    }

    [Fact]
    public void Query_RetainsEverySuffixCandidateAndItsAssociation()
    {
        Candidate first = Create("First.N", "Widget");
        Candidate second = Create("Second.N", "Widget");

        var result = Query([first, second], "N.Widget");
        var reversed = Query([second, first], "N.Widget");

        Assert.Equal([first, second], result.Matches);
        Assert.Equal([second, first], reversed.Matches);
        Assert.Null(result.MatchedName);
        Assert.Null(reversed.MatchedName);
    }

    [Fact]
    public void Query_RetainsSameNamedDefinitionsFromDifferentSources()
    {
        Candidate first = Create("N", "Widget");
        Candidate second = Create("N", "Widget");
        Assert.NotSame(first, second);

        var result = Query([first, second], "Widget");

        Assert.Same(first, result.Matches[0]);
        Assert.Same(second, result.Matches[1]);
        Assert.Equal("N.Widget", result.MatchedName);
    }

    [Fact]
    public void Query_PrefersNonGenericNameButRetainsUnspecifiedGenericArities()
    {
        Candidate action = Create("System", "Action");
        Candidate actionOfT = Create("System", "Action`1");
        Candidate funcOfT = Create("System", "Func`1");
        Candidate funcOfTwo = Create("System", "Func`2");

        Assert.Same(action, Assert.Single(Query([actionOfT, action], "Action").Matches));
        Assert.Equal(
            [funcOfT, funcOfTwo],
            Query([funcOfT, funcOfTwo], "Func").Matches);
        Assert.Same(
            funcOfTwo,
            Assert.Single(Query([funcOfT, funcOfTwo], "Func<T, TResult>").Matches));
        Assert.Empty(Query([funcOfT, funcOfTwo], "Func<T1, T2, TResult>").Matches);
    }

    [Theory]
    [InlineData("Outer<T>.Inner<U>")]
    [InlineData("Outer`1+Inner`1")]
    public void Query_PreservesEveryNestedSegmentsArity(string query)
    {
        Candidate selected = Create("N", "Outer`1", "Inner`1");
        Candidate otherOuter = Create("N", "Outer`2", "Inner`1");
        Candidate otherInner = Create("N", "Outer`1", "Inner`2");

        Assert.Same(
            selected,
            Assert.Single(Query([otherOuter, otherInner, selected], query).Matches));
    }

    [Fact]
    public void Query_RetainsCaseVariantDefinitions()
    {
        Candidate first = Create("N", "Widget");
        Candidate second = Create("N", "widget");

        Assert.Equal([first, second], Query([first, second], "N.Widget").Matches);
    }

    [Theory]
    [InlineData("N.Widget", "Widget")]
    [InlineData("N.Widget", "n.widget")]
    [InlineData("N.Outer+Inner", "N.Outer.Inner")]
    public void Definition_DoesNotApplyQueryAliases(string definitionId, string queryAlias)
    {
        Candidate candidate = new(
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.ParseSerialized(definitionId)).Name);

        Assert.Same(
            candidate,
            Assert.Single(MetadataTypeLookup.Definition(
                [candidate], static value => value.Definition, definitionId).Matches));
        Assert.Empty(MetadataTypeLookup.Definition(
            [candidate], static value => value.Definition, queryAlias).Matches);
        Assert.Same(candidate, Assert.Single(Query([candidate], queryAlias).Matches));
    }

    [Fact]
    public void Definition_KeepsLiteralDelimitersDistinctFromNestedIdentity()
    {
        Candidate literal = Create("N", "Outer.Inner");
        Candidate nested = Create("N", "Outer", "Inner");

        Assert.Equal(literal.Definition.ToMetadataFullName(), nested.Definition.ToMetadataFullName());
        var serialized = MetadataTypeLookup.Definition(
            new[] { nested, literal }, static value => value.Definition, @"N.Outer\.Inner");
        var structured = MetadataTypeLookup.Definition(
            new[] { nested, literal }, static value => value.Definition, literal.Definition);

        Assert.Same(literal, Assert.Single(serialized.Matches));
        Assert.Same(literal, Assert.Single(structured.Matches));
    }

    [Theory]
    [InlineData("W*dget")]
    [InlineData("Widget?")]
    public void Query_RejectsDiscoveryGlobs(string query) =>
        Assert.Throws<ArgumentException>(() => Query([Create("N", "Widget")], query));

    [Fact]
    public void Query_DoesNotSelectASimilarName()
    {
        var result = Query([Create("N", "Widget")], "Wdiget");

        Assert.Empty(result.Matches);
        Assert.Null(result.MatchedName);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void PinnedCoreLib_SelectionRetainsTheAdmittedPopulation()
    {
        // Microsoft.NETCore.App.Runtime.linux-x64 11.0.0-rc.1.26425.128,
        // already pinned by this suite. Admission stays with the extractor.
        string path = Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", "System.Private.CoreLib.dll");
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        ApiSurface all = ApiSurfaceExtractor.ExtractDeclarations(image, ApiSurfaceExtractionScope.IncludeAll);
        ApiSurface ordinary = ApiSurfaceExtractor.ExtractDeclarations(image, ApiSurfaceExtractionScope.Public);
        const string currencyWrapper = "System.Runtime.InteropServices.CurrencyWrapper";

        var admitted = MetadataTypeLookup.Query(
            all.Types, static type => type.DefinitionName!, "CurrencyWrapper");
        var excluded = MetadataTypeLookup.Query(
            ordinary.Types, static type => type.DefinitionName!, "CurrencyWrapper");

        Assert.Equal(currencyWrapper, Assert.Single(admitted.Matches).FullName);
        Assert.Empty(excluded.Matches);
        Assert.Equal(
            "System.Action",
            Assert.Single(MetadataTypeLookup.Query(
                ordinary.Types, static type => type.DefinitionName!, "Action").Matches).FullName);
        Assert.Equal(
            "System.Action`1",
            Assert.Single(MetadataTypeLookup.Query(
                ordinary.Types, static type => type.DefinitionName!, "Action<T>").Matches).FullName);
    }

    static MetadataTypeLookupResult<Candidate> Query(Candidate[] candidates, string query) =>
        MetadataTypeLookup.Query(candidates, static candidate => candidate.Definition, query);

    static Candidate Create(string @namespace, params string[] segments) =>
        new(Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(@namespace, [.. segments])).Name);

    sealed class Candidate(MetadataTypeDefinitionName definition)
    {
        public MetadataTypeDefinitionName Definition { get; } = definition;
    }
}
