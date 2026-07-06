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
        Assert.Contains(FixtureBoundary.SidecarAsset, fixture.Boundaries);
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

        AssertBoundary(FixtureCatalog.AnalysisProtobuf, FixtureBoundary.AssemblyName);
        AssertBoundary(FixtureCatalog.AnalysisSpoofSystemLinq, FixtureBoundary.AssemblyName);
        AssertBoundary(FixtureCatalog.AnalysisSpoofSystemRuntime, FixtureBoundary.AssemblyName);
    }

    [Fact]
    public void BoundaryMetadata_CoversIntentionalSeparateFixtureProjects()
    {
        var consolidatedProjects = new HashSet<string>(StringComparer.Ordinal)
        {
            "ILInspector.Analysis.Fixtures",
            "ILInspector.Decompiler.Fixtures.Ladder",
        };

        var singleFixtureProjects = FixtureCatalog.All
            .GroupBy(fixture => fixture.ProjectName, StringComparer.Ordinal)
            .Where(group => group.Count() == 1 && !consolidatedProjects.Contains(group.Key))
            .Select(group => group.Single());

        Assert.All(singleFixtureProjects, fixture =>
            Assert.NotEmpty(fixture.Boundaries));
    }

    [Theory]
    [InlineData(FixtureIds.DiffV1, FixtureBoundary.VersionPair)]
    [InlineData(FixtureIds.DiffV2, FixtureBoundary.VersionPair)]
    [InlineData(FixtureIds.DiffAsmCaller, FixtureBoundary.AssemblyIdentity)]
    [InlineData(FixtureIds.AnalysisCallerGraphCaller, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.AnalysisCallerGraphCallerTwin, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.AnalysisCallerGraphLookalikeCaller, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.AnalysisCallerGraphTarget, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.AnalysisCrossAsmCollision, FixtureBoundary.ExternAlias)]
    [InlineData(FixtureIds.AnalysisCrossAsmShape, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.AnalysisExceptionBase, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.AnalysisFacade, FixtureBoundary.TargetFramework)]
    [InlineData(FixtureIds.AnalysisLookalike, FixtureBoundary.AssemblyIdentity)]
    [InlineData(FixtureIds.AnalysisRender, FixtureBoundary.FrameworkReference)]
    [InlineData(FixtureIds.DecompilerCheckedArithmetic, FixtureBoundary.CompilerLowering)]
    [InlineData(FixtureIds.DecompilerClassicAsync, FixtureBoundary.CompilerLowering)]
    [InlineData(FixtureIds.DecompilerUnsafeLegacy, FixtureBoundary.ModuleAttribute)]
    [InlineData(FixtureIds.DecompilerUnsafeNew, FixtureBoundary.ModuleAttribute)]
    [InlineData(FixtureIds.DecompilerUnsafeChainA, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.DecompilerUnsafeChainB, FixtureBoundary.CrossAssemblyBoundary)]
    [InlineData(FixtureIds.DecompilerUnsafeChainC, FixtureBoundary.OutputKind)]
    [InlineData(FixtureIds.RunFasterAllocation, FixtureBoundary.SidecarAsset)]
    public void BoundaryMetadata_DocumentsSemanticAxes(string fixtureId, FixtureBoundary boundary)
    {
        AssertBoundary(FixtureCatalog.Get(fixtureId), boundary);
    }

    [Fact]
    public void ConsolidatedFixtureBuckets_ShareAssemblyWithoutProjectBoundaryAxes()
    {
        Assert.Equal(FixtureCatalog.AnalysisCrossAsmShape.ProjectName, FixtureCatalog.AnalysisExceptionBase.ProjectName);
        Assert.Equal(FixtureCatalog.AnalysisCrossAsmShape.AssemblyPath(), FixtureCatalog.AnalysisExceptionBase.AssemblyPath());

        Assert.DoesNotContain(FixtureBoundary.AssemblyName, FixtureCatalog.AnalysisCrossAsmShape.Boundaries);
        Assert.DoesNotContain(FixtureBoundary.AssemblyName, FixtureCatalog.AnalysisExceptionBase.Boundaries);
        Assert.DoesNotContain(FixtureBoundary.TargetFramework, FixtureCatalog.AnalysisCrossAsmShape.Boundaries);
        Assert.DoesNotContain(FixtureBoundary.TargetFramework, FixtureCatalog.AnalysisExceptionBase.Boundaries);
    }

    [Fact]
    public void UnsafeFixtures_PreserveMemorySafetyBuildAxis()
    {
        Assert.Contains("legacy-memory-safety", FixtureCatalog.DecompilerUnsafeLegacy.Tags);
        Assert.DoesNotContain("updated-memory-safety", FixtureCatalog.DecompilerUnsafeLegacy.Tags);
        Assert.Contains("updated-memory-safety", FixtureCatalog.DecompilerUnsafeNew.Tags);
        AssertBoundary(FixtureCatalog.DecompilerUnsafeLegacy, FixtureBoundary.ModuleAttribute);
        AssertBoundary(FixtureCatalog.DecompilerUnsafeNew, FixtureBoundary.ModuleAttribute);
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
        AssertBoundary(FixtureCatalog.DecompilerUnsafeChainA, FixtureBoundary.CrossAssemblyBoundary);
        AssertBoundary(FixtureCatalog.DecompilerUnsafeChainB, FixtureBoundary.CrossAssemblyBoundary);
        AssertBoundary(FixtureCatalog.DecompilerUnsafeChainC, FixtureBoundary.CrossAssemblyBoundary);
        AssertBoundary(FixtureCatalog.DecompilerUnsafeChainC, FixtureBoundary.OutputKind);

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

    static void AssertBoundary(FixtureDefinition fixture, FixtureBoundary boundary)
        => Assert.Contains(boundary, fixture.Boundaries);

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
