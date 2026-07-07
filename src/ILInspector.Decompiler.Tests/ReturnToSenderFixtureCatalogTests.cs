using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection;

using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    public void DecompilerResult_ExposesEffectiveOptionsAndTasteDecisions()
    {
        using var source = MetadataSource.Open(FixtureCatalog.DiffV1.AssemblyPath());
        var function = IrImporter.Import(source, "DiffFixtureSample.DiffSample", "CallToken", 0);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));

        Assert.True(result.EffectiveOptions.PreferFrameworkTypeImports);
        Assert.False(result.EffectiveOptions.ReadableLocalNames);
        var decision = Assert.Single(result.Decisions, decision =>
            decision.RuleId == "type-name.framework-imported"
            && decision.Category == "taste"
            && decision.Subject == "System.Math");
        Assert.Contains("System.Math", decision.Detail);
        Assert.Contains("Math", result.Output);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ClassifiesKnownTasteDifferenceFromProductDecision()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DiffV1.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "DiffFixtureSample.DiffSample",
                    "CallToken",
                    Overload: 0),
            ]));

        Assert.Equal(ReturnToSenderSourceOutcome.ValidDifferent, result.Outcome);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.CompileBackStatus);
        Assert.Equal("valid_different.known_taste", result.Reason);
        Assert.Contains("System.Math", result.Detail);
        Assert.Equal("returnSystem.Math.Abs(value);", Normalize(result.ExpectedBody));
        Assert.Equal("returnMath.Abs(value);", Normalize(result.ActualBody));
    }

    [Fact]
    public void ReturnToSenderSourceProbe_PreservesUnsafeModifierAfterMemberAttributes()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures",
                    "StackAllocSkipInit",
                    Overload: 0),
            ]));

        Assert.NotEqual(ReturnToSenderSourceOutcome.Invalid, result.Outcome);
        Assert.DoesNotContain("CS1585", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ClassifiesBodylessSourceMembersAsUnsupported()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DecompilerLadderRung9.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "LadderRung9.DynamicConstructTarget",
                    "get_Value",
                    Overload: 0),
            ]));

        Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
        Assert.Equal("valid_match.source_bodyless", result.Reason);
        Assert.EndsWith("LadderRung9.cs", result.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ClassifiesRecordSynthesizedMembersAsUnsupported()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DecompilerLadderRung5.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "LadderRung5.Point",
                    "ToString",
                    Overload: 0),
            ]));

        Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
        Assert.Equal("valid_match.source_bodyless", result.Reason);
        Assert.EndsWith("LadderRung5.cs", result.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_PreservesPrimaryConstructorShellParameters()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DecompilerLadderRung5.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "LadderRung5.PrimaryCounter",
                    "Next",
                    Overload: 0),
            ]));

        Assert.NotEqual(ReturnToSenderSourceOutcome.Invalid, result.Outcome);
        Assert.DoesNotContain("CS0103", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_PreservesExtensionMethodShellParameters()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DecompilerLadderRung2.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "LadderRung2.Program",
                    "Main",
                    Overload: 0),
            ]));

        Assert.NotEqual(ReturnToSenderSourceOutcome.Invalid, result.Outcome);
        Assert.DoesNotContain("CS1061", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ResolvesCs0234FullTypeClosureRoots()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DecompilerLadderRung1.AssemblyPath(),
            [
                new ReturnToSender.RequestedTarget(
                    "LadderRung1.Program",
                    "Greet",
                    Overload: 0),
            ]));

        Assert.NotEqual(ReturnToSenderSourceOutcome.Invalid, result.Outcome);
        Assert.DoesNotContain("CS0234", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ClassifiesKnownTasteDifferenceAcrossMultipleFrameworkImports()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public class Class1
            {
                public int M(int value)
                {
                    System.Console.WriteLine(value);
                    return System.Math.Abs(value);
                }
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "M", Overload: 0)],
                fixture.SourcePaths));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidDifferent, result.Outcome);
            Assert.Equal("valid_different.known_taste", result.Reason);
            Assert.Contains("System.Console", result.Detail);
            Assert.Contains("System.Math", result.Detail);
            Assert.Equal("System.Console.WriteLine(value);returnSystem.Math.Abs(value);", Normalize(result.ExpectedBody));
            Assert.Equal("Console.WriteLine(value);returnMath.Abs(value);", Normalize(result.ActualBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_KnownTasteDoesNotRewriteStringLiterals()
    {
        var decision = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Math",
            "test decision");

        Assert.False(TryKnownTasteDifferenceForTest(
            """var text = "System.Math";""",
            """var text = "Math";""",
            [decision],
            out _));
    }

    [Fact]
    public void ReturnToSenderSourceProbe_KnownTasteDoesNotRewritePrefixCollisions()
    {
        var decision = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Console",
            "test decision");

        Assert.False(TryKnownTasteDifferenceForTest(
            "System.ConsoleColor value;",
            "ConsoleColor value;",
            [decision],
            out _));
    }

    [Fact]
    public void ReturnToSenderSourceProbe_KnownTasteMatchesGenericFrameworkTypes()
    {
        var decision = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Collections.Generic.List`1",
            "test decision")
        {
            OldValue = "System.Collections.Generic.List",
            NewValue = "List",
        };

        Assert.True(TryKnownTasteDifferenceForTest(
            "System.Collections.Generic.List<int> values = null;",
            "List<int> values = null;",
            [decision],
            out _));
    }

    [Fact]
    public void ReturnToSenderSourceProbe_KnownTasteRewritesNestedGenericArguments()
    {
        var task = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Threading.Tasks.Task`1",
            "task decision")
        {
            OldValue = "System.Threading.Tasks.Task",
            NewValue = "Task",
        };
        var list = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Collections.Generic.List`1",
            "list decision")
        {
            OldValue = "System.Collections.Generic.List",
            NewValue = "List",
        };

        Assert.True(TryKnownTasteDifferenceForTest(
            "System.Threading.Tasks.Task<System.Collections.Generic.List<int>> values = null;",
            "Task<List<int>> values = null;",
            [task, list],
            out var detail));
        Assert.Contains("task", detail);
        Assert.Contains("list", detail);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_KnownTastePreservesNestedGenericTypeArguments()
    {
        var list = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Collections.Generic.List`1",
            "list decision")
        {
            OldValue = "System.Collections.Generic.List",
            NewValue = "List",
        };

        Assert.True(TryKnownTasteDifferenceForTest(
            "System.Collections.Generic.List<int>.Enumerator enumerator = default;",
            "List<int>.Enumerator enumerator = default;",
            [list],
            out var detail));
        Assert.Contains("list", detail);
    }

    [Fact]
    public void ReturnToSenderSourceProbe_ClassifiesOpenGenericNestedFrameworkType()
    {
        var fixture = CompileSourceFixture(
            ("Class1.cs", """
            namespace SourceProbe;

            public class Class1
            {
                public System.Type M()
                    => typeof(System.Collections.Generic.List<>.Enumerator);
            }
            """));
        try
        {
            var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                fixture.AssemblyPath,
                [new ReturnToSender.RequestedTarget("SourceProbe.Class1", "M", Overload: 0)],
                fixture.SourcePaths));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidDifferent, result.Outcome);
            Assert.Equal("valid_different.known_taste", result.Reason);
            Assert.Contains("System.Collections.Generic.List", result.Detail);
            Assert.Equal("returntypeof(System.Collections.Generic.List<>.Enumerator);", Normalize(result.ExpectedBody));
            Assert.Equal("returntypeof(List<>.Enumerator);", Normalize(result.ActualBody));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_KnownTasteUsesMostSpecificNestedFrameworkType()
    {
        var environment = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Environment",
            "environment decision")
        {
            OldValue = "System.Environment",
            NewValue = "Environment",
        };
        var specialFolder = new DecompilerDecision(
            "type-name.framework-imported",
            "taste",
            "System.Environment+SpecialFolder",
            "special folder decision")
        {
            OldValue = "System.Environment.SpecialFolder",
            NewValue = "SpecialFolder",
        };

        Assert.True(TryKnownTasteDifferenceForTest(
            "System.Environment.SpecialFolder folder = System.Environment.SpecialFolder.ApplicationData;",
            "SpecialFolder folder = SpecialFolder.ApplicationData;",
            [environment, specialFolder],
            out var detail));
        Assert.Contains("special folder", detail);
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

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesCrossFilePartialIndexerIdentity()
    {
        var definition = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public partial class Class1
            {
                [System.Runtime.CompilerServices.IndexerName("Custom")]
                public partial int this[int index] { get; }
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var implementation = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public partial class Class1
            {
                public partial int this[int index] => index;
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var indexer = Assert.Single(implementation.DescendantNodes().OfType<IndexerDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([definition, implementation]);
        var member = Assert.Single(context.IndexerMembers(indexer, "SourceProbe.Class1"));

        Assert.Equal("get_Custom", member.MetadataName);
        Assert.Equal("return index;", member.Body);
        Assert.Contains("indexer-name-attribute", member.Evidence);
        Assert.Contains("partial-implementation", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesPartialPropertyIdentity()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public partial class Class1
            {
                public partial int P { get; }

                public partial int P => 1;
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var implementation = root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Single(property => property.ExpressionBody is not null);

        var context = CSharpSourceIdentityContext.Create([root]);
        var member = Assert.Single(context.PropertyMembers(implementation));

        Assert.Equal("get_P", member.MetadataName);
        Assert.Equal("return 1;", member.Body);
        Assert.Contains("partial-implementation", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_SkipsExplicitPropertyAndIndexerImplementations()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public interface IValues
            {
                int P { get; }
                int this[int index] { get; }
            }

            public class Class1 : IValues
            {
                int IValues.P => 1;
                int IValues.this[int index] => index;
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var property = Assert.Single(
            root.DescendantNodes().OfType<PropertyDeclarationSyntax>(),
            property => property.ExplicitInterfaceSpecifier is not null);
        var indexer = Assert.Single(
            root.DescendantNodes().OfType<IndexerDeclarationSyntax>(),
            indexer => indexer.ExplicitInterfaceSpecifier is not null);

        var context = CSharpSourceIdentityContext.Create([root]);

        Assert.Empty(context.PropertyMembers(property));
        Assert.Empty(context.IndexerMembers(indexer, "SourceProbe.Class1"));
    }

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesMethodIdentity()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public interface IValue
            {
                int M();
            }

            public partial class Class1 : IValue
            {
                partial void M();
                int IValue.M() => 0;
                public partial int M(int value) => value;
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();

        var context = CSharpSourceIdentityContext.Create([root]);
        Assert.Empty(context.MethodMembers(methods.Single(method =>
            method.Modifiers.Any(SyntaxKind.PartialKeyword)
            && method.Body is null
            && method.ExpressionBody is null)));
        Assert.Empty(context.MethodMembers(methods.Single(method => method.ExplicitInterfaceSpecifier is not null)));
        var member = Assert.Single(context.MethodMembers(methods.Single(method => method.Modifiers.Any(SyntaxKind.PublicKeyword))));

        Assert.Equal("M", member.MetadataName);
        Assert.Equal("return value;", member.Body);
        Assert.Contains("partial-implementation", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesConstructorIdentity()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public class Class1(int value)
            {
                public static int StaticValue;
                public int Value;

                static Class1()
                {
                    StaticValue = 1;
                }

                public Class1() : this(1)
                {
                    Value = value;
                }
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var type = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>());
        var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToArray();

        var context = CSharpSourceIdentityContext.Create([root]);
        var primary = Assert.Single(context.TypeHeaderMembers(type));
        var staticConstructor = Assert.Single(context.ConstructorMembers(constructors.Single(ctor => ctor.Modifiers.Any(SyntaxKind.StaticKeyword))));
        var instanceConstructor = Assert.Single(context.ConstructorMembers(constructors.Single(ctor => !ctor.Modifiers.Any(SyntaxKind.StaticKeyword))));

        Assert.Equal(".ctor", primary.MetadataName);
        Assert.Null(primary.Body);
        Assert.Contains("primary-constructor", primary.Evidence);
        Assert.Equal(".cctor", staticConstructor.MetadataName);
        Assert.Equal("StaticValue = 1;", staticConstructor.Body);
        Assert.Contains("static-constructor", staticConstructor.Evidence);
        Assert.Equal(".ctor", instanceConstructor.MetadataName);
        Assert.Equal("Value = value;", instanceConstructor.Body);
    }

    [Fact]
    public void CSharpSourceIdentityContext_PreservesExplicitRecordMemberBodies()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public record Class1(int Value)
            {
                public override string ToString()
                {
                    return Value.ToString();
                }
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var type = Assert.Single(root.DescendantNodes().OfType<RecordDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([root]);
        var members = context.TypeMembers(type, "SourceProbe.Class1").Where(member => member.MetadataName == "ToString").ToArray();

        var member = Assert.Single(members);
        Assert.Equal("return Value.ToString();", member.Body);
        Assert.DoesNotContain("record-synthesized", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_PreservesExplicitRecordEqualsOverloads()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public record Class1(int Value)
            {
                public bool Equals(int other)
                {
                    return Value == other;
                }
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var type = Assert.Single(root.DescendantNodes().OfType<RecordDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([root]);
        var members = context.TypeMembers(type, "SourceProbe.Class1").Where(member => member.MetadataName == "Equals").ToArray();

        var member = Assert.Single(members);
        Assert.Equal("return Value == other;", member.Body);
        Assert.DoesNotContain("record-synthesized", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesOperatorIdentity()
    {
        var root = CSharpSyntaxTree.ParseText("""
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
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var op = Assert.Single(root.DescendantNodes().OfType<OperatorDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([root]);
        var member = Assert.Single(context.OperatorMembers(op));

        Assert.Equal("op_UnsignedRightShift", member.MetadataName);
        Assert.Equal("return new Class1(value._value >>> shift);", member.Body);
        Assert.Contains("operator", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesCheckedOperatorIdentity()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public readonly struct Class1
            {
                readonly int _value;

                public Class1(int value)
                {
                    _value = value;
                }

                public static Class1 operator checked +(Class1 left, Class1 right)
                    => checked(new Class1(left._value + right._value));
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var op = Assert.Single(root.DescendantNodes().OfType<OperatorDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([root]);
        var member = Assert.Single(context.OperatorMembers(op));

        Assert.Equal("op_CheckedAddition", member.MetadataName);
        Assert.Equal("return checked(new Class1(left._value + right._value));", member.Body);
        Assert.Contains("operator", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesConversionOperatorIdentity()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public readonly struct Class1
            {
                readonly int _value;

                public Class1(int value)
                {
                    _value = value;
                }

                public static implicit operator int(Class1 value) => value._value;
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var conversion = Assert.Single(root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([root]);
        var member = Assert.Single(context.ConversionOperatorMembers(conversion));

        Assert.Equal("op_Implicit", member.MetadataName);
        Assert.Equal("return value._value;", member.Body);
        Assert.Contains("conversion-operator", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_ResolvesCheckedConversionOperatorIdentity()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public readonly struct Class1
            {
                readonly int _value;

                public Class1(int value)
                {
                    _value = value;
                }

                public static explicit operator checked int(Class1 value) => checked(value._value + 1);
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var conversion = Assert.Single(root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([root]);
        var member = Assert.Single(context.ConversionOperatorMembers(conversion));

        Assert.Equal("op_CheckedExplicit", member.MetadataName);
        Assert.Equal("return checked(value._value + 1);", member.Body);
        Assert.Contains("conversion-operator", member.Evidence);
    }

    [Fact]
    public void CSharpSourceIdentityContext_EmitsOrderedTypeMembers()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public class Class1(int value)
            {
                static Class1()
                {
                }

                public Class1() : this(1)
                {
                }

                public int P => value;

                public int M() => value;

                public int this[int index] => index;
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var type = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>());

        var context = CSharpSourceIdentityContext.Create([root]);
        var names = context.TypeMembers(type, "SourceProbe.Class1").Select(member => member.MetadataName).ToArray();

        Assert.Equal([".ctor", ".cctor", ".ctor", "get_P", "M", "get_Item"], names);
    }

    [Fact]
    public void CSharpSourceIdentityContext_UsesMetadataArityForGenericTypes()
    {
        var root = CSharpSyntaxTree.ParseText("""
            namespace SourceProbe;

            public class Class1<T>
            {
                public int M() => 1;
            }
            """, cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var type = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>());

        Assert.Equal("Class1`1", CSharpSourceIdentityContext.TypeMetadataName(type));
    }

    [Fact]
    public void ReturnToSenderSourceProbe_MapsGenericTypeSourceMembers()
    {
        var result = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
            FixtureCatalog.DiffV1.AssemblyPath(),
            [new ReturnToSender.RequestedTarget("DiffFixtureSample.GenericTypeAritySample`1", "M", Overload: 0)]));

        Assert.NotEqual(ReturnToSenderSourceOutcome.SourceUnavailable, result.Outcome);
        Assert.Equal("return1;", Normalize(result.ExpectedBody));
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

    static bool TryKnownTasteDifferenceForTest(
        string expected,
        string actual,
        IReadOnlyList<DecompilerDecision> decisions,
        out string? detail)
    {
        var method = typeof(ReturnToSenderSourceProbe).GetMethod(
            "TryKnownTasteDifference",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        object?[] args = [expected, actual, decisions, null];
        bool result = (bool)method.Invoke(null, args)!;
        detail = (string?)args[3];
        return result;
    }

    static string? Normalize(string? text)
        => text is null
            ? null
            : string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}
