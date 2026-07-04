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
        foreach (var group in Groups())
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
    public void AssemblyNameAxisFixtures_ResolveIntentionalFileNames()
    {
        Assert.EndsWith(Path.Combine("ILInspector.Analysis.ProtobufFixtures", "release", "Google.Protobuf.dll"), FixtureCatalog.AnalysisProtobuf.AssemblyPath());
        Assert.EndsWith(Path.Combine("ILInspector.Analysis.SpoofFixtures", "release", "System.Linq.dll"), FixtureCatalog.AnalysisSpoofSystemLinq.AssemblyPath());
        Assert.EndsWith(Path.Combine("ILInspector.Analysis.SpoofRuntimeFixtures", "release", "System.Runtime.dll"), FixtureCatalog.AnalysisSpoofSystemRuntime.AssemblyPath());
    }

    static IReadOnlyList<FixtureGroup> Groups() =>
    [
        FixtureCatalog.DiffAssemblyFixtures,
        FixtureCatalog.AnalysisFixtures,
        FixtureCatalog.DecompilerFixtures,
        FixtureCatalog.DecompilerLadderFixtures,
        FixtureCatalog.DecompilerUnsafeFixtures,
        FixtureCatalog.RunFasterFixtures,
        FixtureCatalog.ReturnToSenderCandidates,
    ];
}
