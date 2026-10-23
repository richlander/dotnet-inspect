using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public sealed class EcosystemFindIndexCliTests
{
    [Fact]
    public void EmbeddedArtifactsAreValidDeterministicAndContainKnownAnswers()
    {
        EcosystemFindIndexArtifact aspire = Load(EcosystemPackIds.Aspire);
        EcosystemFindIndexArtifact extensions =
            Load(EcosystemPackIds.MicrosoftExtensions);

        AssertKnownMembers(
            aspire,
            "AddRedis",
            "AddPostgres",
            "AddRedisClient");
        AssertKnownMembers(extensions, "AddHttpClient");
        Assert.True(
            EcosystemFindIndexJson.Serialize(aspire)
                .SequenceEqual(EmbeddedBytes(EcosystemPackIds.Aspire)));
        Assert.True(
            EcosystemFindIndexJson.Serialize(extensions)
                .SequenceEqual(
                    EmbeddedBytes(EcosystemPackIds.MicrosoftExtensions)));
    }

    [Fact]
    public void ExactIndexedDuplicateCollapsesButSameSpellingFromAnotherSourceRemains()
    {
        EcosystemFindIndexArtifact artifact = Load(EcosystemPackIds.Aspire);
        EcosystemFindIndexSymbol symbol = artifact.Symbols.First(candidate =>
            candidate.Kind is EcosystemFindIndexSymbolKind.Type);
        EcosystemFindIndexSourceCoordinate source =
            artifact.Sources[symbol.SourceIndex];
        EcosystemFindIndexSourceCoordinate otherSource =
            artifact.Sources.First(candidate =>
                !ExactSource(candidate).Equals(ExactSource(source)));
        ExactLibrarySourceCoordinate exact = ExactSource(source);
        MetadataTypeDefinitionName type = symbol.Type!;
        var duplicate = new TypeFindResult
        {
            Pattern = type.Segments[^1],
            Match = TypeFindMatchKind.Direct,
            Type = type.Segments[^1],
            Namespace = type.Namespace,
            FullName = type.ToMetadataFullName(),
            ExactSource = exact,
            ExactType = type,
        };
        TypeFindResult crossSource = duplicate with
        {
            ExactSource = ExactSource(otherSource),
        };
        var block = new EcosystemFindBlock<FindCommand.LayeredSearchBlock>(
            new([duplicate, crossSource], []),
            RowCount: 2);

        EcosystemFindBlock<FindCommand.LayeredSearchBlock> filtered =
            FindCommand.SuppressIndexedDuplicates(
                new FindOptions { Tsv = true },
                block,
                [new(exact, type)],
                []);

        Assert.Equal(1, filtered.RowCount);
        Assert.Same(crossSource, Assert.Single(filtered.Content.Types));
    }

    [Fact]
    public void ExactMemberDuplicateCollapsesButSameSpellingFromAnotherSourceRemains()
    {
        EcosystemFindIndexArtifact artifact = Load(EcosystemPackIds.Aspire);
        EcosystemFindIndexSymbol symbol = artifact.Symbols.First(candidate =>
            candidate.Kind is EcosystemFindIndexSymbolKind.Member);
        EcosystemFindIndexSourceCoordinate source =
            artifact.Sources[symbol.SourceIndex];
        EcosystemFindIndexSourceCoordinate otherSource =
            artifact.Sources.First(candidate =>
                !ExactSource(candidate).Equals(ExactSource(source)));
        ExactLibrarySourceCoordinate exact = ExactSource(source);
        var duplicate = new MemberFindResult
        {
            Pattern = symbol.Member!.Member.MemberName,
            Member = symbol.Member.Member.MemberName,
            ExactSource = exact,
            ExactMember = symbol.Member.Member,
        };
        MemberFindResult crossSource = duplicate with
        {
            ExactSource = ExactSource(otherSource),
        };
        var block = new EcosystemFindBlock<FindCommand.LayeredSearchBlock>(
            new([], [duplicate, crossSource], AcceptedMemberCount: 2),
            RowCount: 2);

        EcosystemFindBlock<FindCommand.LayeredSearchBlock> filtered =
            FindCommand.SuppressIndexedDuplicates(
                new FindOptions { Members = true, Tsv = true },
                block,
                [],
                [new(exact, symbol.Member.Member)]);

        Assert.Equal(1, filtered.RowCount);
        Assert.Equal(1, filtered.Content.AcceptedMemberCount);
        Assert.Same(crossSource, Assert.Single(filtered.Content.Members));
    }

    [Fact]
    public void ExactIdentityFieldsAreNotSerialized()
    {
        EcosystemFindIndexArtifact artifact = Load(EcosystemPackIds.Aspire);
        EcosystemFindIndexSymbol symbol = artifact.Symbols.First(candidate =>
            candidate.Kind is EcosystemFindIndexSymbolKind.Type);
        var row = new TypeFindResult
        {
            Pattern = "Thing",
            Match = TypeFindMatchKind.Direct,
            ExactSource = ExactSource(
                artifact.Sources[symbol.SourceIndex]),
            ExactType = symbol.Type,
        };

        string json = JsonSerializer.Serialize(row);

        Assert.DoesNotContain("exactSource", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exactType", json, StringComparison.OrdinalIgnoreCase);
    }

    private static EcosystemFindIndexArtifact Load(EcosystemPackId ecosystem) =>
        Assert.IsType<EcosystemFindIndexResourceLoad.Available>(
            EcosystemFindIndexResources.Load(ecosystem)).Artifact;

    private static byte[] EmbeddedBytes(EcosystemPackId ecosystem) =>
        EcosystemFindIndexResources.ReadBytes(ecosystem);

    private static void AssertKnownMembers(
        EcosystemFindIndexArtifact artifact,
        params string[] expected)
    {
        HashSet<string> members =
        [
            .. artifact.Symbols
                .Where(symbol =>
                    symbol.Kind is EcosystemFindIndexSymbolKind.Member)
                .Select(symbol => symbol.Member!.Member.MemberName),
        ];
        Assert.All(expected, name => Assert.Contains(name, members));
    }

    private static ExactLibrarySourceCoordinate ExactSource(
        EcosystemFindIndexSourceCoordinate source) =>
        new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create(
                source.Package.PackageId,
                source.Package.Version!),
            new ManagedMetadataIdentity.Assembly(source.Assembly));
}
