using ILInspector.DecompilerHarness;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Instructions;
using DotnetInspector.RoundTripCompilation;
using DotnetInspector.Services;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

public partial class ReturnToSenderPrototypeTests
{
    [Fact]
    public async Task CompileBackFirstPropertyGetter_DeduplicatesSystemUsing_WhenBodyAlreadyReferencesSystem()
    {
        // Issue #2848: the module Usings list unconditionally prepended "System" to
        // MemberBodyFacts.ReferencedNamespaces(function). A body that already
        // references a System-namespace type (Guid, here) produced two "System"
        // entries in the generated using list.
        var assemblyPath = CompileFixture("""
            namespace Fixtures;

            public class Class1
            {
                public string Method1 => System.Guid.NewGuid().ToString();
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Equal(1, result.Plan.Module.Usings.Count(name => name == "System"));
            Assert.DoesNotContain("using System;\r\nusing System;", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("using System;\nusing System;", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_FallsBackToCompileBackFloorForAttributeShellStall()
    {
        // Issue #2527: base-class reconstruction restores same-assembly base classes,
        // so the old dropped-base attribute stall no longer occurs. A concrete shell
        // that inherits an abstract member it does not itself consume still cannot
        // satisfy that obligation (CS0534) — the growth loop does not synthesize
        // abstract/interface member implementations. The shell stalls with a complete
        // payload; the compile-back floor (which compiles the decompiled member
        // against the full original assembly) rescues it.
        var assemblyPath = CompileFixture("""
            public abstract class Shape
            {
                protected abstract int Corners();
            }

            public sealed class Triangle : Shape
            {
                protected override int Corners() => 3;

                public int First => Corners();
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(result.UsedCompileBackFloor, result.Detail);
            Assert.NotNull(result.CompileBackFloor);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                result.Detail);
            Assert.Equal(result.CompileBackFloor.Status, result.Status);
            Assert.Contains("compile-back-floor", result.Detail);
            Assert.Contains("CS0534", result.Detail);
            Assert.Contains("Corners", result.TargetBody);
            Assert.Contains("Corners", result.Source);
            Assert.NotNull(result.MemberAnchor);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CorpusParity_DoesNotApplyCompileBackFloorToRtsFailure()
    {
        var assemblyPath = CompileFixture("""
            public abstract class Shape
            {
                protected abstract int Corners();
            }

            public sealed class Triangle : Shape
            {
                protected override int Corners() => 3;

                public int First => Corners();
            }
            """);
        try
        {
            var target = new ReturnToSender.RequestedTarget("Triangle", "get_First", 0);
            var floored = Assert.Single(await ReturnToSender.CompileBackTargets(assemblyPath, [target]));
            Assert.True(floored.UsedCompileBackFloor, floored.Detail);
            var reference = Assert.IsType<FidelityCheck.CompileBackResult>(floored.CompileBackFloor);

            var native = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [target],
                applyCompileBackFloor: false));
            Assert.False(native.UsedCompileBackFloor);
            Assert.True(
                native.Status is FidelityCheck.CompileBackStatus.RecompileFail
                    or FidelityCheck.CompileBackStatus.ContextFail,
                $"{native.Status}: {native.Detail}");

            var aligned = CorpusSensor.AlignReturnToSenderResultsForTesting([reference], [native]);
            var parity = CorpusSensor.SummarizeReturnToSenderParityForTesting([reference], aligned);

            Assert.Equal(0, parity.RescuedMethods);
            Assert.Equal(0, parity.SameMethods);
            Assert.Equal(1, parity.WorseMethods);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_UsesDependencyReferencesAndNamespaces()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var dependencyPath = CompileFixture("""
            namespace External;

            public class Greeting
            {
                public static Greeting Create() => new Greeting();
            }
            """, directory, "ExternalLib");
        var assemblyPath = CompileFixture("""
            using External;

            public class Class1
            {
                public Greeting Method1 => Greeting.Create();
            }
            """, directory, "Fixture", [MetadataReference.CreateFromFile(dependencyPath)]);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("External", result.Plan.Module.Usings);
            Assert.Contains("public Greeting Method1", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackPropertyGetters_EvaluatesSupportedGetterLadderWithCap()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public string Method1 => "Hello World";
                public int Count => 42;
                public string this[int index] => index.ToString();
            }

            public readonly struct SkippedStruct
            {
                public int Value => 1;
            }
            """);
        try
        {
            var results = await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2);

            Assert.Collection(
                results,
                first =>
                {
                    Assert.Equal("get_Method1", first.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, first.Status);
                },
                second =>
                {
                    Assert.Equal("get_Count", second.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, second.Status);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CorpusFidelity_EvaluatesCompileBackSelectedTargetsThroughReturnToSender()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Value => 42;
                public int Transform(int value) => value + 1;
                public int Transform(string value) => value.Length;
            }
            """);
        try
        {
            var results = await CorpusSensor.EvaluateReturnToSenderForTesting(assemblyPath, cap: 10);
            var getter = Assert.Single(results, result => result.Method == "get_Value");
            var overloads = results
                .Where(result => result.Method == "Transform")
                .OrderBy(result => result.Overload)
                .ToArray();

            Assert.Equal("Class1", getter.Type);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, getter.Status);
            Assert.StartsWith("return-to-sender", getter.CaptureDetail);
            Assert.Equal(2, overloads.Length);
            Assert.Equal(new[] { 0, 1 }, overloads.Select(result => result.Overload));
            Assert.Equal(2, overloads.Select(result => result.Signature).Distinct().Count());
            Assert.All(overloads, result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackPropertyGetters_AddsSameAssemblyReturnTypeClosureRoot()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
            }

            public class Class1
            {
                public Helper SameAssemblyType => new Helper();
                public string Method1 => "Hello World";
            }
            """);
        try
        {
            var results = await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2);

            Assert.Collection(
                results,
                first =>
                {
                    Assert.Equal("get_SameAssemblyType", first.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, first.Status);
                    Assert.Contains(first.Plan.Types, type => type.Name == "Helper");
                    Assert.Contains(first.Plan.Types, requirement =>
                        requirement.Type.DisplayName == "Helper"
                        && requirement.SourceFacts.Any(fact => fact.Id == "body-type"
                            && fact.Producer == "metadata"
                            && fact.Detail == "Helper"));
                    var evidence = ReturnToSenderClosureEvidenceBuilder.FromPlan(first.Plan);
                    Assert.Equal(2, evidence.RequiredTypes);
                    Assert.Equal(0, evidence.RoslynRecoveredTypes);
                    Assert.Contains(evidence.Requirements, requirement =>
                        requirement.Type == "Helper"
                        && !requirement.RoslynRecovered
                        && requirement.Facts.Contains("metadata/body-type: Helper"));
                },
                second =>
                {
                    Assert.Equal("get_Method1", second.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, second.Status);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsSameAssemblyClosureMemberSurface()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public int Value => 42;
                public static Helper Create() => new Helper();
            }

            public class Class1
            {
                public int FromHelper => Helper.Create().Value;
            }
            """);
        try
        {
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2))
                .Single(item => item.Plan.TargetMethod.Method == "get_FromHelper");

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Helper"
                && type.SourceFacts.Any(fact => fact.Id == "body-type" && fact.Producer == "metadata")
                && !type.SourceFacts.Any(fact => fact.Id == "closure-member" && fact.Producer == "roslyn")
                && type.Members.Any(member => member.Name == "Value" && member.Kind == CompileBackMemberKind.PropertyGet
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-property" && fact.Detail == "get_Value"))
                && type.Members.Any(member => member.Name == "Create" && member.Kind == CompileBackMemberKind.Method && member.IsStatic
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "Create")));
            var evidence = ReturnToSenderClosureEvidenceBuilder.FromPlan(result.Plan);
            Assert.Equal(0, evidence.RoslynRecoveredMemberSurfaces);
            Assert.Contains("public int Value", result.Source);
            Assert.Contains("public static Helper Create()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_DeduplicatesRepeatedPreciseClosureMembers()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public int Value => 42;
                public static Helper Create() => new Helper();
            }

            public class Class1
            {
                public int FromHelper => Helper.Create().Value + Helper.Create().Value;
            }
            """);
        try
        {
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2))
                .Single(item => item.Plan.TargetMethod.Method == "get_FromHelper");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var helper = Assert.Single(result.Plan.Types, type => type.Name == "Helper");
            Assert.Equal(1, helper.Members.Count(member => member.Name == "Value" && member.Kind == CompileBackMemberKind.PropertyGet));
            Assert.Equal(1, helper.Members.Count(member => member.Name == "Create" && member.Kind == CompileBackMemberKind.Method));
            Assert.DoesNotContain("already contains a definition", result.Detail ?? "");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_UsesTypedClosureFieldForStaticMemberAccess()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public static int Value = 42;
            }

            public class Class1
            {
                public int FromHelper => Helper.Value;
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Helper"
                && !type.SourceFacts.Any(fact => fact.Producer == "roslyn" && fact.Id == "closure-member")
                && type.Members.Any(member => member.Name == "Value"
                    && member.Kind == CompileBackMemberKind.Field
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-field" && fact.Detail == "Value")));
            Assert.Contains("public static int Value;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_UsesTypedTargetFieldForUnqualifiedFieldAccess()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                private readonly int _value = 42;

                public int Value => _value;
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(type.Members, member =>
                member.Name == "_value"
                && member.Kind == CompileBackMemberKind.Field
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-field" && fact.Detail == "_value"));
            Assert.Contains("public int _value;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_DoesNotEmitGeneratedBackingFieldRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Value { get; } = 42;
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.Members, member => member.Name.Contains('<', StringComparison.Ordinal));
            Assert.DoesNotContain(result.Source, "<Value>", StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesThisConstructorChainOpcodes()
    {
        // Issue #2678: RTS used to reconstruct target constructors with empty
        // bodies, dropping the `: this(...)` chain call. The recompiled ctor then
        // emitted `ldarg call ret` instead of the original `ldarg ldarg call call
        // ret`, producing an OpcodeDiff. The chain must be preserved so the ctor
        // round-trips Exact.
        var assemblyPath = CompileFixture("""
            public class Versioned
            {
                public Versioned(string text) : this(Parse(text))
                {
                }

                public Versioned(int value)
                {
                    Value = value;
                }

                public int Value { get; }

                private static int Parse(string text) => text.Length;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Versioned", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }
}
