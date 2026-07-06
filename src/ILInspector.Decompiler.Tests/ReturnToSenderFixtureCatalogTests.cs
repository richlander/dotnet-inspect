using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    [Fact]
    public void ReturnToSenderSourceProbe_IndexesPartialClassOverloadsAcrossSourceFiles()
    {
        var fixture = CompileSourceFixture(
            ("Class1.Part1.cs", """
            namespace SourceProbe;

            public partial class Class1
            {
                public int M() => 1;
            }
            """),
            ("Class1.Part2.cs", """
            namespace SourceProbe;

            public partial class Class1
            {
                public int M(int value) => value;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "M", Overload: 1)],
                fixture.SourcePaths));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
            Assert.Equal("valid_match", result.Reason);
            Assert.Equal("returnvalue;", Normalize(result.ExpectedBody));
            Assert.Equal("returnvalue;", Normalize(result.ActualBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_IndexesBodylessOverloads()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public abstract class Class1
            {
                public abstract int M();

                public int M(int value) => value;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "M", Overload: 1)],
                fixture.SourcePaths));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
            Assert.Equal("valid_match", result.Reason);
            Assert.Equal("returnvalue;", Normalize(result.ExpectedBody));
            Assert.Equal("returnvalue;", Normalize(result.ActualBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_MissingSourcePathIsSourceUnavailable()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public class Class1
            {
                public int M() => 1;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "M", Overload: 0)],
                [Path.Combine(fixture.Directory, "missing.cs")]));

            Assert.Equal(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.CompileBackStatus);
            Assert.Equal("fixture-source-unavailable", result.Reason);
            Assert.Contains("source index could not be built", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    static (string Directory, string AssemblyPath, IReadOnlyList<string> SourcePaths) CompileSourceFixture(
        params (string FileName, string Source)[] sources)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"rts-source-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePaths = new List<string>();
        foreach (var (fileName, source) in sources)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, source);
            sourcePaths.Add(path);
        }

        string assemblyPath = Path.Combine(directory, "SourceProbe.dll");
        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
        var trees = sourcePaths.Select(path =>
            CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                new CSharpParseOptions(LanguageVersion.Preview),
                path));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(assemblyPath),
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable,
                allowUnsafe: true));

        var emit = compilation.Emit(assemblyPath);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return (directory, assemblyPath, sourcePaths);
    }

    static string? Normalize(string? text)
        => text is null
            ? null
            : string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}
