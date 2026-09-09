using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;

namespace ILInspector.Decompiler.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FrozenReferenceEnvironmentCollection
{
    public const string Name = "Frozen reference environment";
}

[Collection(FrozenReferenceEnvironmentCollection.Name)]
[Trait("Area", "RoundTrip")]
public sealed class ReturnToSenderFrozenReferenceTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void DiscoveryRolesExcludeTargetOccurrencesAndCoalesceOnlyRepeatedRegistrations()
    {
        using var files = new Files();
        string other = files.Copy(FixtureIds.AnalysisCallerGraphTarget, "other/Target.dll");
        ReturnToSender.WithCompilation(files.Source, operation =>
        {
            Assert.Equal(2, operation.Discovery.Entries.Count(entry => entry.IsTargetInput));
            var entries = operation.Discovery.Entries.Where(entry => !entry.IsTargetInput).ToArray();
            Assert.Equal(2, entries.Length);
            Assert.Same(
                Assert.IsType<AssemblyDependencyAcquisition.Acquired>(entries[0].Acquisition).Assembly,
                Assert.IsType<AssemblyDependencyAcquisition.Acquired>(entries[1].Acquisition).Assembly);
            var candidate = Assert.Single(operation.Inventory.Candidates,
                image => image.Provenance is not AssemblyResolutionProvenance.PlatformAsset);
            var selected = Assert.Single(operation.ReferenceSet.References,
                descriptor => descriptor.Image.Provenance is not AssemblyResolutionProvenance.PlatformAsset);
            Assert.Same(candidate.InventoryId, selected.InventoryId);
            Assert.NotSame(operation.Inventory.Source.InventoryId, candidate.InventoryId);
            Assert.Same(candidate.ArtifactRegistration, candidate.MetadataRegistration.ArtifactRegistration);
            Assert.NotNull(candidate.ContentDigest);
            Assert.Equal(operation.ReferenceSet.References.Length, operation.Closure.References.Length);
            return true;
        }, files.Options with { CorpusAssemblyPaths = [other, other, files.Source] }, cancellationToken: Cancellation);
    }

    [Fact]
    public void DistinctProvenanceRegistrationsRemainAmbiguousEvenForIdenticalBytes()
    {
        using var files = new Files();
        string dependency = files.Copy(FixtureIds.AnalysisCallerGraphTarget, "Dependency.dll");
        var failure = Assert.Throws<ReturnToSender.ReferencePreparationException>(() =>
            ReturnToSender.WithCompilation(files.Source, _ => true,
                files.Options with { CorpusAssemblyPaths = [dependency] }, cancellationToken: Cancellation));
        Assert.Equal(CompileReferenceFailureKind.ReferenceSelectionAmbiguous, failure.Failure.Kind);
        Assert.Equal(2, failure.Failure.Candidates.Length);
        Assert.NotSame(failure.Failure.Candidates[0], failure.Failure.Candidates[1]);
    }

    [Fact]
    public void DifferentContentWithSameFullIdentityDoesNotFirstWin()
    {
        using var files = new Files();
        files.Copy(FixtureIds.DiffV1, "First.dll");
        files.Copy(FixtureIds.DiffV2, "Second.dll");
        var failure = Assert.Throws<ReturnToSender.ReferencePreparationException>(() =>
            ReturnToSender.WithCompilation(files.Source, _ => true, files.Options, cancellationToken: Cancellation));
        Assert.Equal(CompileReferenceFailureKind.ReferenceSelectionAmbiguous, failure.Failure.Kind);
        Assert.Equal(2, failure.Failure.Candidates.Length);
    }

    [Fact]
    public void DifferentVersionsStaySelectedAndCompilerAmbiguityRemainsVisible()
    {
        using var files = new Files();
        files.Copy(FixtureIds.AnalysisCallerGraphTarget, "First.dll");
        files.Copy(FixtureIds.AnalysisCallerGraphTargetV2, "Second.dll");
        ReturnToSender.WithCompilation(files.Source, operation =>
        {
            var versions = operation.ReferenceSet.References.Where(reference =>
                reference.Image.Identity.Name == "ILInspector.Analysis.CallerGraphTarget").ToArray();
            Assert.Equal(2, versions.Length);
            Assert.NotEqual(versions[0].Image.Identity.Version, versions[1].Image.Identity.Version);
            var result = Assert.Single(operation.CompileBackTargets(
                [new("AuthoredRebuildFixtures.ContextSample", "get_Value", 0)],
                applyCompileBackFloor: false));
            Assert.Equal(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.Contains("CS1704", result.Detail);
            return true;
        }, cancellationToken: Cancellation);
    }

    [Theory]
    [InlineData("syntax")]
    [InlineData("structure")]
    [InlineData("missing")]
    public void DiscoveryFailureCannotBecomeASmallerSuccessfulReferenceSet(string kind)
    {
        using var files = new Files();
        File.WriteAllText(Path.ChangeExtension(files.Source, ".deps.json"), kind switch
        {
            "syntax" => "{",
            "structure" => "{}",
            _ => """{"targets":{"net11.0":{"P/1":{"runtime":{"Missing.dll":{"localPath":"Missing.dll"}}}}},"libraries":{}}""",
        });
        var result = ReturnToSender.CompileBackFirstPropertyGetter(files.Source);
        Assert.Equal(FidelityCheck.CompileBackStatus.ContextFail, result.Status);
        Assert.Equal(CompileReferenceFailureKind.ReferenceDiscoveryFailed, result.ReferenceFailure?.Kind);
        Assert.Null(result.FinalRequest);
        Assert.Null(result.CompilationAttempt);
        Assert.False(result.UsedCompileBackFloor);
    }

    [Fact]
    public void DiscoverySnapshotExhaustionRemainsAVisibleFailure()
    {
        using var files = new Files();
        var failure = Assert.Throws<ReturnToSender.ReferencePreparationException>(() =>
            ReturnToSender.WithCompilation(files.Source, _ => true,
                files.Options with { MaxSnapshotImageBytes = 1 }, cancellationToken: Cancellation));
        Assert.Equal(CompileReferenceFailureKind.ReferenceDiscoveryFailed, failure.Failure.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepsLogicalCandidateUsesTheServicesSelectedPhysicalLocation(bool localExists)
    {
        using var files = new Files();
        string package = files.Copy(FixtureIds.AnalysisCallerGraphTargetV2, "packages/p/1/lib/net11.0/P.dll");
        string local = Path.Combine(files.Root, "P.dll");
        if (localExists)
            files.Copy(FixtureIds.AnalysisCallerGraphTarget, "P.dll");
        File.WriteAllText(Path.ChangeExtension(files.Source, ".deps.json"), """
            {"targets":{"net11.0":{"P/1":{"runtime":{"lib/net11.0/P.dll":{"localPath":"P.dll"}}}}},
             "libraries":{"P/1":{"path":"p/1"}}}
            """);
        string? previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", Path.Combine(files.Root, "packages"));
            ReturnToSender.WithCompilation(files.Source, operation =>
            {
                var row = Assert.Single(operation.Discovery.Entries);
                Assert.Equal(localExists ? local : package, row.Dependency.Path);
                var acquired = Assert.IsType<AssemblyDependencyAcquisition.Acquired>(row.Acquisition);
                var selected = Assert.Single(operation.ReferenceSet.References,
                    descriptor => descriptor.Image.Provenance is not AssemblyResolutionProvenance.PlatformAsset);
                Assert.Equal(acquired.Assembly.Identity, selected.Image.Identity);
                Assert.Single(operation.Inventory.Candidates,
                    image => image.Provenance is not AssemblyResolutionProvenance.PlatformAsset);
                var reference = Assert.IsAssignableFrom<PortableExecutableReference>(
                    operation.Closure.References[selected.SelectedOrdinal]);
                var metadata = Assert.IsType<AssemblyMetadata>(reference.GetMetadata());
                Assert.Equal(selected.Image.ModuleVersionId, Assert.Single(metadata.GetModules()).GetModuleVersionId());
                return true;
            }, files.Options with { IncludeSiblingAssemblies = false, IncludeDepsJsonAssets = true }, cancellationToken: Cancellation);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous);
        }
    }

    [Fact]
    public async Task ScopedReplaySurvivesAwaitAndSourceRemovalButOrdinaryReportsAreDetached()
    {
        using var files = new Files();
        var ordinary = ReturnToSender.CompileBackFirstPropertyGetter(files.Source);
        Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, ordinary.Status);
        Assert.Null(ordinary.FinalRequest);
        Assert.NotNull(ordinary.CompilationAttempt);

        await ReturnToSender.WithCompilationAsync(files.Source, async operation =>
        {
            var initial = Assert.Single(operation.CompileBackPropertyGetters(1));
            File.Delete(files.Source);
            await Task.Yield();
            var recorded = new RecordedBuildContext(
                true, ILInspector.Metadata.MetadataFindings.InspectCompilationOptions([],
                    new Inspector.Findings.FindingSubject("test", "test")),
                ILInspector.Metadata.MetadataFindings.InspectCompilationReferences([],
                    new Inspector.Findings.FindingSubject("test", "test")));
            var replay = AuthoredRebuildFidelity.CompileAuthoredBody(
                initial, initial.TargetBody, null, recorded);
            Assert.Equal(AuthoredRebuildOutcome.IlDifferent, replay.Outcome);
            Assert.Same(initial.FinalRequest!.CompilationClosure, operation.Closure);
            Assert.NotNull(replay.AuthoredAttempt);
            return true;
        }, cancellationToken: Cancellation);
    }

    sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"rts-frozen-{Guid.NewGuid():N}");
        public string Source { get; }
        public AssemblyDependencyResolutionOptions Options => new(Source)
        {
            IncludeTrustedPlatformAssemblies = false,
            IncludeAspNetCoreSharedFramework = false,
            IncludeDepsJsonAssets = false,
            IncludeSiblingAssemblies = true,
            IncludeInstalledPlatformFallback = true,
            AllowPlatformAssemblyVersionRollForward = true,
            ExcludeTargetAssembly = true,
            SnapshotAssemblyImages = true,
        };

        public Files()
        {
            Directory.CreateDirectory(Root);
            Source = Path.Combine(Root, "Target.dll");
            File.Copy(FixtureCatalog.DecompilerAuthoredRebuild.AssemblyPath(), Source);
        }

        public string Copy(string fixture, string relativePath)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(FixtureCatalog.AssemblyPath(fixture), path);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
