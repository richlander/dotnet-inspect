using DotnetInspector.Fixtures;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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

    [Fact]
    public void UnsafeFixtures_PreserveMemorySafetyBuildAxis()
    {
        Assert.Contains("legacy-memory-safety", FixtureCatalog.DecompilerUnsafeLegacy.Tags);
        Assert.DoesNotContain("updated-memory-safety", FixtureCatalog.DecompilerUnsafeLegacy.Tags);
        Assert.Contains("updated-memory-safety", FixtureCatalog.DecompilerUnsafeNew.Tags);
        Assert.NotEqual(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath(),
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());

        Assert.False(HasAttributeNamed(FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath(), "MemorySafetyRulesAttribute"));
        Assert.True(HasAttributeNamed(FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(), "MemorySafetyRulesAttribute"));
    }

    [Fact]
    public void UnsafeChainFixtures_PreserveCrossAssemblyBoundaries()
    {
        Assert.Contains("updated-memory-safety", FixtureCatalog.DecompilerUnsafeChainA.Tags);
        Assert.Contains("updated-memory-safety", FixtureCatalog.DecompilerUnsafeChainB.Tags);
        Assert.Contains("legacy-memory-safety", FixtureCatalog.DecompilerUnsafeChainC.Tags);
        Assert.Contains("executable", FixtureCatalog.DecompilerUnsafeChainC.Tags);

        AssertAssemblyReferences(
            FixtureCatalog.DecompilerUnsafeChainB,
            "ILInspector.Decompiler.Fixtures.UnsafeChainA");
        AssertAssemblyReferences(
            FixtureCatalog.DecompilerUnsafeChainC,
            "ILInspector.Decompiler.Fixtures.UnsafeChainA",
            "ILInspector.Decompiler.Fixtures.UnsafeChainB");
    }

    static void AssertFixtureFileName(FixtureDefinition fixture, string expectedFileName)
    {
        string path = fixture.AssemblyPath();
        Assert.Equal(expectedFileName, Path.GetFileName(path));
        Assert.Equal(fixture.ProjectName, new DirectoryInfo(path).Parent?.Parent?.Name);
    }

    static void AssertAssemblyReferences(FixtureDefinition fixture, params string[] expectedReferences)
    {
        using var stream = File.OpenRead(fixture.AssemblyPath());
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var references = reader.AssemblyReferences
            .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(expectedReferences, reference =>
            Assert.Contains(reference, references));
    }

    static bool HasAttributeNamed(string assemblyPath, string attributeName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        return reader.GetModuleDefinition()
            .GetCustomAttributes()
            .Select(reader.GetCustomAttribute)
            .Any(attribute => AttributeTypeName(reader, attribute.Constructor) == attributeName);
    }

    static string AttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        var typeHandle = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default,
        };

        return typeHandle.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)typeHandle).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle).Name),
            _ => string.Empty,
        };
    }
}
