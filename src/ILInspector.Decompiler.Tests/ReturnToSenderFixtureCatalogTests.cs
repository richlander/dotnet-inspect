using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class ReturnToSenderFixtureCatalogTests
{
    [Fact]
    public void ReturnToSenderCandidates_ProvideBuiltAssemblyInputs()
    {
        var paths = FixtureCatalog.ReturnToSenderCandidates.AssemblyPaths();

        Assert.NotEmpty(paths);
        Assert.All(paths, path =>
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            Assert.True(peReader.HasMetadata);
            Assert.NotEmpty(peReader.GetMetadataReader().MethodDefinitions);
        });
    }

    [Fact]
    public void FixtureCatalog_ExposesCheckedInSourcePaths()
    {
        var sourcePaths = FixtureCatalog.DiffV1.SourcePaths();

        Assert.Contains(sourcePaths, path => path.EndsWith("DiffSample.cs", StringComparison.Ordinal));
        Assert.All(sourcePaths, path =>
        {
            Assert.True(File.Exists(path), path);
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", path);
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", path);
        });
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ClassifiesFixtureSourceMatch()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DiffV1.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "DiffFixtureSample.DiffSample",
                    "Stable",
                    Overload: 0),
            ]));

        Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.CompileBackStatus);
        Assert.Equal("valid_match", result.Reason);
        Assert.EndsWith("DiffSample.cs", result.SourcePath, StringComparison.Ordinal);
        Assert.Equal("return42;", Normalize(result.ExpectedBody));
        Assert.Equal("return42;", Normalize(result.ActualBody));
    }

    static string? Normalize(string? text)
        => text is null
            ? null
            : string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}
