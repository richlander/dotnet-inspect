using DotnetInspector.Fixtures;

namespace DotnetInspector.Fixtures.Tests;

public class FixtureCatalogTests
{
    [Fact]
    public void All_FixtureIdsAreUnique()
    {
        var duplicate = FixtureCatalog.All
            .GroupBy(fixture => fixture.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        Assert.Null(duplicate);
    }

    [Fact]
    public void All_FixturesResolveToBuiltAssemblies()
    {
        foreach (var fixture in FixtureCatalog.All)
        {
            string path = fixture.AssemblyPath();
            Assert.True(File.Exists(path), $"Expected fixture {fixture.Id} at {path}");
        }
    }

    [Fact]
    public void Groups_ReferenceRegisteredFixtures()
    {
        var all = FixtureCatalog.All.ToHashSet();
        foreach (var group in FixtureCatalog.Groups)
        {
            Assert.NotEmpty(group.Fixtures);
            Assert.All(group.Fixtures, fixture =>
                Assert.Contains(fixture, all));
        }
    }

    [Fact]
    public void ReturnToSenderCandidates_AreRegisteredAndTagged()
    {
        var all = FixtureCatalog.All.ToHashSet();
        Assert.All(FixtureCatalog.ReturnToSenderCandidates.Fixtures, fixture =>
        {
            Assert.Contains(fixture, all);
            Assert.Contains("rts-candidate", fixture.Tags);
        });
    }

    [Fact]
    public void Group_UnknownIdFailsClearly()
    {
        var error = Assert.Throws<ArgumentException>(() => FixtureCatalog.Group("missing"));

        Assert.Contains("Unknown fixture group id 'missing'", error.Message);
    }

    [Fact]
    public void SelectByTag_UnknownTagFailsClearly()
    {
        var error = Assert.Throws<ArgumentException>(() => FixtureCatalog.SelectByTag("missing"));

        Assert.Contains("Unknown fixture tag 'missing'", error.Message);
    }

    [Fact]
    public void SelectByTag_RtsCandidateMatchesReturnToSenderGroup()
    {
        Assert.Equal(
            FixtureCatalog.ReturnToSenderCandidates.Fixtures.Select(fixture => fixture.Id).Order(StringComparer.Ordinal),
            FixtureCatalog.SelectByTag("rts-candidate").Select(fixture => fixture.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ReturnToSenderCandidates_ResolveAssemblyPaths()
    {
        Assert.All(FixtureCatalog.ReturnToSenderCandidates.AssemblyPaths(), path =>
            Assert.True(File.Exists(path), $"Expected RTS candidate fixture at {path}"));
    }

    [Fact]
    public void SidecarAssets_ResolveTraceCoupledRunFasterAsset()
    {
        var fixture = FixtureCatalog.RunFasterAllocation;

        Assert.Contains("trace-coupled", fixture.Tags);
        var asset = Assert.Single(fixture.Assets);
        Assert.Equal("fixture.nettrace", asset.Name);
        Assert.EndsWith(Path.Combine("Fixtures", "RunFaster.AllocationFixture", "fixture.nettrace"), fixture.AssetPath(asset.Name));
    }

    [Fact]
    public void SidecarAssets_UnknownAssetFailsClearly()
    {
        var error = Assert.Throws<ArgumentException>(() => FixtureCatalog.RunFasterAllocation.AssetPath("missing"));

        Assert.Contains("Fixture 'runfaster.allocation' has no asset named 'missing'", error.Message);
    }

    [Fact]
    public void AssemblyNameAxisFixtures_ResolveIntentionalFileNames()
    {
        AssertFixtureFileName(FixtureCatalog.AnalysisProtobuf, "Google.Protobuf.dll");
        AssertFixtureFileName(FixtureCatalog.AnalysisSpoofSystemLinq, "System.Linq.dll");
        AssertFixtureFileName(FixtureCatalog.AnalysisSpoofSystemRuntime, "System.Runtime.dll");
    }

    static void AssertFixtureFileName(FixtureDefinition fixture, string expectedFileName)
    {
        string path = fixture.AssemblyPath();
        Assert.Equal(expectedFileName, Path.GetFileName(path));
        Assert.Equal(fixture.ProjectName, new DirectoryInfo(path).Parent?.Parent?.Name);
    }
}
