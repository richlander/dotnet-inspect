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

    [Fact]
    public void ReturnToSenderSourceProbe_DoesNotIndexErasedPartialMethodDefinition()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public partial class Class1
            {
                partial void M();

                public int M(int value) => value;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "M", Overload: 0)],
                fixture.SourcePaths));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
            Assert.Equal("returnvalue;", Normalize(result.ExpectedBody));
            Assert.Equal("returnvalue;", Normalize(result.ActualBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_DoesNotIndexExplicitInterfaceImplementationUnderPublicName()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public interface IValue
            {
                int M();
            }

            public class Class1 : IValue
            {
                int IValue.M() => 0;

                public int M() => 1;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "M", Overload: 0)],
                fixture.SourcePaths));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
            Assert.Equal("return1;", Normalize(result.ExpectedBody));
            Assert.Equal("return1;", Normalize(result.ActualBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_IndexesIndexerGetter()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public class Class1
            {
                public int this[int index] => index;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "get_Item", Overload: 0)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("returnindex;", Normalize(result.ExpectedBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_IndexesUnsignedRightShiftOperator()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public readonly struct Class1
            {
                readonly int _value;

                public Class1(int value)
                {
                    _value = value;
                }

                public static Class1 operator >>>(Class1 value, int shift)
                    => new Class1(value._value >>> shift);
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "op_UnsignedRightShift", Overload: 0)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("returnnewClass1(value._value>>>shift);", Normalize(result.ExpectedBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_SplitsStaticAndInstanceConstructorSlots()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public class Class1
            {
                public static int StaticValue;
                public int Value;

                static Class1()
                {
                    StaticValue = 1;
                }

                public Class1()
                {
                    Value = 2;
                }
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", ".ctor", Overload: 0)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("Value=2;", Normalize(result.ExpectedBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ConsumesPrimaryConstructorSlot()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public class Class1(int value)
            {
                public Class1() : this(1)
                {
                }

                public int Value() => value;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", ".ctor", Overload: 1)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("", Normalize(result.ExpectedBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ConsumesStructPrimaryConstructorSlot()
    {
        var fixture = CompileSourceFixture(
            ("Struct1.cs", """
            namespace SourceProbe;

            public struct Struct1(int value)
            {
                public Struct1() : this(1)
                {
                }

                public int Value() => value;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Struct1", ".ctor", Overload: 1)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("", Normalize(result.ExpectedBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_DoesNotIndexErasedPartialPropertyDefinition()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public partial class Class1
            {
                public partial int P { get; }

                public partial int P => 1;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "get_P", Overload: 0)],
                fixture.SourcePaths));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
            Assert.Equal("return1;", Normalize(result.ExpectedBody));
            Assert.Equal("return1;", Normalize(result.ActualBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_UsesIndexerNameAttribute()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public class Class1
            {
                [System.Runtime.CompilerServices.IndexerName("Custom")]
                public int this[int index] => index;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "get_Custom", Overload: 0)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("returnindex;", Normalize(result.ExpectedBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_UsesIndexerNameFromPartialDefinition()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public partial class Class1
            {
                [System.Runtime.CompilerServices.IndexerName("Custom")]
                public partial int this[int index] { get; }

                public partial int this[int index] => index;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "get_Custom", Overload: 0)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("returnindex;", Normalize(result.ExpectedBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_UsesIndexerNameFromCrossFilePartialDefinition()
    {
        var fixture = CompileSourceFixture(
            ("Class1.Part1.cs", """
            namespace SourceProbe;

            public partial class Class1
            {
                [System.Runtime.CompilerServices.IndexerName("Custom")]
                public partial int this[int index] { get; }
            }
            """),
            ("Class1.Part2.cs", """
            namespace SourceProbe;

            public partial class Class1
            {
                public partial int this[int index] => index;
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "get_Custom", Overload: 0)],
                fixture.SourcePaths));

            Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
            Assert.Equal("returnindex;", Normalize(result.ExpectedBody));
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
        var references = RoslynTestReferences.TrustedPlatform.AsEnumerable();
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
