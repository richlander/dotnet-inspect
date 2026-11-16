using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

public sealed class FindingDiffAnyApiExactnessPrerequisiteTests
{
    [Fact]
    public void AccessorHiddenStatus_AgreesWithTheCompleteApiDiff()
    {
        using var before = new PEReader(File.OpenRead(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath()));
        const string name = "MemberFoldUncertainGetterStatus";
        FindingDiffAnyTypeMembers raw = new FindingDiffAnyTypePairing(before, after).Members("FindingDiffAnyFixture." + name);
        ApiSurface Surface(PEReader image) => ApiSurfaceExtractor.ExtractDeclarations(
            image, ApiSurfaceExtractionScope.ApiAxis,
            type => image.GetMetadataReader().StringComparer.Equals(image.GetMetadataReader().GetTypeDefinition(type).Name, name));
        ApiFindingComparison complete = MetadataFindings.CompareApi(Surface(before), Surface(after),
            new FindingSubject("probe", "API exactness"), new ApiDiffOptions(ApiDiffScope.All));
        FindingDiffAnyState expected = complete.ApiDiff.IsEmpty
            ? FindingDiffAnyState.Unchanged
            : FindingDiffAnyState.Changed;
        Assert.Equal(expected, raw.Api);
        FindingDiffAnyResult library = FindingDiffAny.Compare(before, after,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expected, Assert.Single(library.Types, type => type.FullName == "FindingDiffAnyFixture." + name).Api);
        Assert.True(library.Receipt.ApiSurfacesBuilt > 0);
        FindingDiffAnyTypeInventory inventory = new FindingDiffAnyTypePairing(before, after).Inventory("FindingDiffAnyFixture." + name);
        Assert.NotEmpty(inventory.Facts);
        Assert.Equal(FindingDiffAnyState.Unchanged, inventory.Api);
        Assert.All(raw.Members, member => Assert.Equal(FindingDiffAnyState.Unchanged, member.Api));
        Assert.Equal(FindingDiffAnyState.Unchanged, raw.ResidualApi);
        Assert.Null(FindingDiffAny.Explain(before, after, "FindingDiffAnyFixture." + name).Api);
    }
}
