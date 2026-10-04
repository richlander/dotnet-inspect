using System.Collections.Immutable;

using CSharpText;

using DotnetInspector.Queries;

using ILInspector.Metadata;
using ILInspector.Research;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryNameFamilyQueryTests
{
    [Fact]
    public void QuerySpace_DeclaresPopulationRowsCountAndRowScopes()
    {
        QuerySpaceBinding querySpace = LibraryNameFamilyQuery.QuerySpace;

        Assert.Equal(
            [
                LibraryNameFamilyQuery.FamilyRowsRowSet,
                LibraryNameFamilyQuery.TypeRowsRowSet,
            ],
            querySpace.Descriptor.Operation.RowSets);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            querySpace.Descriptor.Terminals);
        Assert.Equal(2, querySpace.RowScopes.Count);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Top,
            ],
            LibraryNameFamilyQuery.FamilyRowsScope
                .Descriptor.Stages);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ],
            LibraryNameFamilyQuery.TypeRowsScope
                .Descriptor.Stages);
        Assert.Contains(
            LibraryNameFamilyQuery.FamilyRowsScope.Descriptor.Facets,
            facet => facet.Key == LibraryNameFamilyQuery.FamilyKindKey);
        Assert.Contains(
            LibraryNameFamilyQuery.TypeRowsScope.Descriptor.Facets,
            facet =>
                facet.Key
                    == LibraryNameFamilyQuery.TypeDefinitionTokenKey);
    }

    [Theory]
    [InlineData("all", LibraryNameFamilyPopulationKind.AllTypes)]
    [InlineData(
        "ordinary",
        LibraryNameFamilyPopulationKind.OrdinaryEvidenceOnly)]
    [InlineData(
        "generated",
        LibraryNameFamilyPopulationKind.GeneratedEvidenceOnly)]
    [InlineData("mixed", LibraryNameFamilyPopulationKind.MixedEvidence)]
    [InlineData("unknown", LibraryNameFamilyPopulationKind.Unknown)]
    public void ResolveIntent_BindsExactPopulation(
        string token,
        LibraryNameFamilyPopulationKind expected)
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new(
                    LibraryNameFamilyQuery.PopulationTermKey,
                    PortableQueryOperator.Equal,
                    token),
            ],
            [],
            [],
            []);

        var accepted =
            Assert.IsType<LibraryNameFamilyQueryPlanResult.Accepted>(
                LibraryNameFamilyQuery.ResolveIntent(
                    intent,
                    TestContext.Current.CancellationToken));

        Assert.Equal(expected, accepted.Plan.Population);
    }

    [Fact]
    public void TypeFamilyKindNotEquals_RejectsRowsWithEitherMatchingFamily()
    {
        LibraryNameFamilyMethodology methodology = Methodology();
        LibraryNameFamilyIdentity one = new(
            methodology,
            LibraryNameFamilyKind.OneWordSuffix,
            ["Validator"],
            separator: null);
        LibraryNameFamilyIdentity two = new(
            methodology,
            LibraryNameFamilyKind.TwoWordSuffix,
            ["Customer", "Validator"],
            separator: string.Empty);
        LibraryNameFamilyTypeRow both = TypeRow(
            1,
            "CustomerValidator",
            one,
            two);
        LibraryNameFamilyTypeRow onlyTwo = TypeRow(
            2,
            "OrderValidator",
            oneWord: null,
            two);
        LibraryNameFamilyTypeRow neither = TypeRow(
            3,
            "ZZQ",
            oneWord: null,
            twoWord: null);
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new(
                    LibraryNameFamilyQuery.TypeFamilyKindKey,
                    PortableQueryOperator.NotEqual,
                    "one-word"),
            ],
            [],
            [],
            []);
        RowQueryResolutionResult<LibraryNameFamilyTypeRow> resolution =
            LibraryNameFamilyQuery.TypeRowsScope.Resolve(intent);

        Assert.True(resolution.IsSuccess);
        RowSelectionResult<LibraryNameFamilyTypeRow> selected =
            RowQueryExecutor.Apply(
                [both, onlyTwo, neither],
                resolution.Plan!);

        Assert.True(selected.IsSuccess);
        Assert.Equal([onlyTwo, neither], selected.Values);
    }

    private static LibraryNameFamilyMethodology Methodology() =>
        new(
            "test",
            new IdentifierWordOracleReceipt(
                "grammar",
                "vocabulary",
                "digest",
                "source",
                "review",
                EntryCount: 1));

    private static LibraryNameFamilyTypeRow TypeRow(
        int row,
        string name,
        LibraryNameFamilyIdentity? oneWord,
        LibraryNameFamilyIdentity? twoWord)
    {
        var created =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Example",
                    ImmutableArray.Create(name)));
        return new(
            MetadataTypeDefinitionAddress.FromToken(
                Guid.Parse(
                    "00112233-4455-6677-8899-aabbccddeeff"),
                0x02000000 + row),
            created.Name,
            name,
            name,
            [],
            oneWord,
            twoWord,
            OneWordResidual: null,
            TwoWordResidual: null,
            IsDefinitionPublic: true,
            IsPublicSurface: true,
            AssemblyTypeDefinitionKind.Class,
            SourceEvidence: null);
    }
}
