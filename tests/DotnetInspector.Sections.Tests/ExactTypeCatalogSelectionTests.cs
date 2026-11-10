using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class ExactTypeCatalogSelectionTests
{
    [Fact]
    public void FullNameWinsAcrossCatalogSources()
    {
        ApiType exact = Type("Example", "Root", "first.dll");
        ApiType suffix = Type("Other.Example", "Root", "second.dll");
        var result = Select([suffix, exact], "Example.Root");
        Assert.Equal(ExactTypeInspectionOutcome.Available, result.Outcome);
        Assert.Same(exact, result.Type);
        Assert.Equal("first.dll", result.Type!.SourceAssemblyPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoincidentDefinitionNamesInDifferentSourcesRemainAmbiguous(bool reverse)
    {
        ApiType first = Type("Example", "Root", "first.dll");
        ApiType second = Type("Example", "Root", "second.dll");
        var result = Select(reverse ? [second, first] : [first, second], "Root");
        Assert.Equal(ExactTypeInspectionOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.Type);
        Assert.Equal(2, result.Suggestions.Length);
    }

    [Fact]
    public void AmbiguousPrefixStopsBeforeAShorterPrefixCanSucceed()
    {
        var result = Select([
            Type("First.Namespace", "Root"), Type("Second.Namespace", "Root"), Type("", "Namespace")],
            "Namespace.Root.Method", implied: true);
        Assert.Equal(ExactTypeInspectionOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.Type);
        Assert.Null(result.ImpliedMember);
    }

    [Theory]
    [InlineData("Root..ctor:1", ".ctor:1")]
    [InlineData("Root..ctor~abcdef", ".ctor~abcdef")]
    [InlineData("Root..cctor~abcdef", ".cctor~abcdef")]
    [InlineData("Root.explicit:System.IDisposable.Dispose:1", "explicit:System.IDisposable.Dispose:1")]
    [InlineData("Root.Convert<T>", "Convert<T>")]
    [InlineData("Root.Conver*", "Conver*")]
    public void PrefixSearchRetainsCompleteMemberRemainder(string query, string expected)
    {
        var result = Select([Type("Example", "Root")], query, implied: true);
        Assert.Equal(ExactTypeInspectionOutcome.Available, result.Outcome);
        Assert.Equal(expected, result.ImpliedMember);
        Assert.Equal(query, result.RequestedType);
    }

    [Fact]
    public void DefinitionModeUsesOrdinalIdentityWithoutMemberPeeling()
    {
        ApiType type = Type("Example", "Root");
        var catalog = new ApiSurface { Types = [type] };
        var result = ExactTypeInspectionOperation.SelectCatalog(catalog,
            "example.Root", allowImpliedMember: true,
            ExactTypeSelectionKind.DefinitionIdentity);
        Assert.Equal(ExactTypeInspectionOutcome.NotFound, result.Content.Outcome);
        Assert.Null(result.Content.ImpliedMember);
        Assert.Empty(result.Content.Suggestions);
        Assert.Same(type, ExactTypeInspectionOperation.SelectCatalog(catalog,
            "Example.Root", selectionKind: ExactTypeSelectionKind.DefinitionIdentity).Content.Type);
    }

    [Fact]
    public void MissingDefinitionIdentityIsUnavailable()
    {
        var result = Select([new ApiType { Namespace = "Example", Name = "Root" }], "Root");
        Assert.Equal(ExactTypeInspectionOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Type);
    }

    [Fact]
    public void ForwardingFailureStopsPrefixSearchAndSurvivesEnvelope()
    {
        ApiType type = Type("Example", "Root");
        var failure = new ApiSurfaceInspectionFailure("resolve forwarded type", 0,
            MetadataTypeNameFailureMechanism.Metadata, "UnboundBinding", "Missing supplier")
        {
            AffectedTypeDefinitions = [type.DefinitionName!],
        };
        var catalog = new ApiSurface { InspectionFailures = [failure] };
        var result = ExactTypeInspectionOperation.SelectCatalog(catalog,
            "Root.Method", allowImpliedMember: true);
        Assert.Equal(ExactTypeInspectionOutcome.Unavailable, result.Content.Outcome);
        Assert.Same(failure, Assert.Single(result.Content.Failures));
        Assert.Null(result.Content.ImpliedMember);
        Assert.IsType<InspectionShare.NonProjectable>(result.Share);
        Assert.Equal("exact-type.unavailable", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void UnrelatedTypedFailureDoesNotHideAnAvailableType()
    {
        ApiType type = Type("Example", "Root");
        var catalog = new ApiSurface
        {
            Types = [type],
            InspectionFailures = [new("read type", 0,
                MetadataTypeNameFailureMechanism.Metadata, "Malformed", "Other Type")
            {
                OwningTypeDefinition = Type("Other", "Missing").DefinitionName,
            }],
        };
        var result = ExactTypeInspectionOperation.SelectCatalog(catalog, "Root");
        Assert.Same(type, result.Content.Type);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CatalogConsumesExtractorAdmissionWithoutReclassifyingAttributes()
    {
        string assembly = typeof(string).Assembly.Location;
        ApiSurface all = AssemblyReader.ExtractApiSurface(assembly, includeAll: true)!;
        ApiType witness = Assert.Single(all.Types, type =>
            type.DefinitionName?.ToEscapedFullName() == "System.Runtime.InteropServices.CurrencyWrapper");
        var result = ExactTypeInspectionOperation.SelectCatalog(all, "CurrencyWrapper");
        Assert.Same(witness, result.Content.Type);
        ApiSurface ordinary = AssemblyReader.ExtractApiSurface(assembly, includeAll: false)!;
        Assert.Equal(ordinary.Types.Any(type => type.DefinitionName!.Equals(witness.DefinitionName)),
            ExactTypeInspectionOperation.SelectCatalog(ordinary, "CurrencyWrapper").Content.Type is not null);
    }

    static ExactTypeCatalogSelection Select(List<ApiType> types, string query, bool implied = false)
        => ExactTypeInspectionOperation.SelectCatalog(new ApiSurface { Types = types },
            query, allowImpliedMember: implied).Content;

    static ApiType Type(string ns, string name, string? source = null) => new()
    {
        Namespace = ns,
        Name = name,
        SourceAssemblyPath = source,
        DefinitionName = ((MetadataTypeDefinitionNameResult.Valid)
            MetadataTypeDefinitionName.Create(ns, [name])).Name,
    };
}
