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
    public async Task CompileBackFirstPropertyGetter_SelectsTypedOverloadByParameterTypes()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public int Pick(int value) => value + 1;
                public int Pick(string value) => value.Length;
            }

            public class Class1
            {
                public int FromHelper => new Helper().Pick("hello");
            }
            """);
        try
        {
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2))
                .Single(item => item.Plan.TargetMethod.Method == "get_FromHelper");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Helper"
                && type.Members.Any(member => member.Name == "Pick"
                    && member.Parameters is [{ Type.DisplayName: "string" }]
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "Pick"))
                && !type.Members.Any(member => member.Name == "Pick"
                    && member.Parameters is [{ Type.DisplayName: "int" }]));
            Assert.Contains("public int Pick(string value)", result.Source);
            Assert.DoesNotContain("public int Pick(int value)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsGenericClosureMemberSurface()
    {
        var assemblyPath = CompileFixture("""
            public class Helper<T>
            {
                public int Value => 42;
                public static Helper<T> Create() => new Helper<T>();
            }

            public class Class1
            {
                public int FromGeneric => Helper<int>.Create().Value;
            }
            """);
        try
        {
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2))
                .Single(item => item.Plan.TargetMethod.Method == "get_FromGeneric");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.PrintRequests, request =>
                request.Type.Name == "Helper`1"
                && request.Type.TypeParameters.Single().Name == "T");
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Helper"
                && !type.SourceFacts.Any(fact => fact.Id == "closure-member" && fact.Producer == "roslyn")
                && type.Members.Any(member => member.Name == "Value"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-property" && fact.Detail == "get_Value"))
                && type.Members.Any(member => member.Name == "Create"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "Create")));
            var evidence = ReturnToSenderClosureEvidenceBuilder.FromPlan(result.Plan);
            Assert.Equal(0, evidence.RoslynRecoveredMemberSurfaces);
            Assert.Contains("public class Helper<T>", result.Source);
            Assert.Contains("public static Helper<T> Create()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_UsesPreservedDeconstructMemberEvidence()
    {
        var assemblyPath = CompileFixture("""
            public class Pair
            {
                public void Deconstruct(out int left, out int right)
                {
                    left = 1;
                    right = 2;
                }
            }

            public class Class1
            {
                public int Sum
                {
                    get
                    {
                        var pair = new Pair();
                        var (left, right) = pair;
                        return left + right;
                    }
                }
            }
            """);
        try
        {
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2))
                .Single(item => item.Plan.TargetMethod.Method == "get_Sum");

            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Pair"
                && !type.SourceFacts.Any(fact => fact.Id == "closure-member" && fact.Producer == "roslyn")
                && type.Members.Any(member => member.Name == "Deconstruct"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "Deconstruct")));
            var evidence = ReturnToSenderClosureEvidenceBuilder.FromPlan(result.Plan);
            Assert.Equal(0, evidence.RoslynRecoveredMemberSurfaces);
            Assert.Contains("public void Deconstruct", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsTargetRootSiblingMemberSurface()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int FromSibling => GetValue();
                public int GetValue() => 42;
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(type.Members, member => member.Name == "GetValue"
                && member.Kind == CompileBackMemberKind.Method
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "GetValue"));
            Assert.Contains("public int GetValue()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_GrowsClosureFromNamespaceSegmentDiagnostic()
    {
        var assemblyPath = CompileFixture("""
            namespace Target
            {
                public class Class1
                {
                    public Other.Deep.Helper FromNamespace => new Other.Deep.Helper();
                }
            }

            namespace Other.Deep
            {
                public class Helper
                {
                }
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(result.Plan.Types, type =>
                type.Namespace == "Other.Deep"
                && type.Name == "Helper"
                && type.SourceFacts.Any(fact => fact.Producer == "metadata" && fact.Id == "body-type"));
            Assert.Contains("namespace Other.Deep", result.Source);
            Assert.Contains("public class Helper", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_GrowsClosureAcrossMultipleIterations()
    {
        var assemblyPath = CompileFixture("""
            public class A
            {
                public static B Create() => new B();
            }

            public class B
            {
                public int Value => 42;
            }

            public class Class1
            {
                public int FromChain => A.Create().Value;
            }
            """);
        try
        {
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2))
                .Single(item => item.Plan.TargetMethod.Method == "get_FromChain");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "A"
                && type.SourceFacts.Any(fact => fact.Producer == "metadata" && fact.Id == "body-type")
                && !type.SourceFacts.Any(fact => fact.Id == "closure-member" && fact.Producer == "roslyn")
                && type.Members.Any(member => member.Name == "Create"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "Create")));
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "B"
                && type.SourceFacts.Any(fact => fact.Producer == "metadata" && fact.Id == "body-type")
                && !type.SourceFacts.Any(fact => fact.Id == "closure-member" && fact.Producer == "roslyn")
                && type.Members.Any(member => member.Name == "Value"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-property" && fact.Detail == "get_Value")));
            var evidence = ReturnToSenderClosureEvidenceBuilder.FromPlan(result.Plan);
            Assert.Equal(0, evidence.RoslynRecoveredMemberSurfaces);
            Assert.Contains("public static B Create()", result.Source);
            Assert.Contains("public int Value", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_AddsOnlyOneRootWhenSimpleNamesCollide()
    {
        var assemblyPath = CompileFixture("""
            namespace Target
            {
                using A;

                public class Class1
                {
                    public int FromAmbiguous => Helper.Value;
                }
            }

            namespace A
            {
                public class Helper
                {
                    public static int Value => 42;
                }
            }

            namespace B
            {
                public class Helper
                {
                    public static int Value => 13;
                }
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Single(result.Plan.Types, type => type.Name == "Helper");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EscapesKeywordNamespacesInClosureRoots()
    {
        var assemblyPath = CompileFixture("""
            namespace My.@event
            {
                public interface IValue
                {
                    int Value { get; }
                }

                public class Helper : IValue
                {
                    public int Value => 42;
                    public static Helper Create() => new Helper();
                }
            }

            namespace Target
            {
                public class Class1
                {
                    public int FromKeywordNamespace => My.@event.Helper.Create().Value;
                }
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("using My.@event;", result.Source);
            Assert.Contains("namespace My.@event", result.Source);
            Assert.DoesNotContain("using My.event;", result.Source);
            Assert.DoesNotContain("namespace My.event", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackPropertyGetters_UsesAutoPropertyShellForRecordPropertyGetter()
    {
        var assemblyPath = CompileFixture("""
            public sealed record Snapshot(string Assembly, int Count);
            """);
        try
        {
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 8))
                .Single(item => item.Plan.TargetMethod.Method == "get_Assembly");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var member = Assert.Single(Assert.Single(result.Plan.Types).Members, member => member.Name == "Assembly");
            Assert.Equal(CompileBackStubBodyKind.AutoProperty, member.StubBody);
            Assert.Contains("public string Assembly { get; }", result.Source);
            Assert.DoesNotContain("return this.Assembly;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task RunComparison_UsesExactCurrentTargetsPastPerTypeSampleCap()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int P0 => 0;
                public int P1 => 1;
                public int P2 => 2;
                public int P3 => 3;
                public int P4 => 4;
                public int P5 => 5;
                public int P6 => 6;
                public int P7 => 7;
                public int P8 => 8;
                public int P9 => 9;
            }
            """);
        try
        {
            var oldOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                var exitCode = await ReturnToSender.RunComparison([assemblyPath], cap: 10, maxExamples: 10);

                Assert.Equal(0, exitCode);
            }
            finally
            {
                Console.SetOut(oldOut);
            }

            var output = writer.ToString();
            Assert.Contains("RETURNTOSENDER A/B over 10 property getters", output);
            Assert.Contains("  Same          : 10", output);
            Assert.Contains("  CurrentMissing: 0", output);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_PreservesStaticTargetPropertyShape()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public static int StaticValue => 42;
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);
            var member = Assert.Single(Assert.Single(result.Plan.Types).Members);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.True(member.IsStatic);
            Assert.Contains("public static int StaticValue", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsStructClosureRoot()
    {
        var assemblyPath = CompileFixture("""
            public struct StructHelper
            {
            }

            public class Class1
            {
                public StructHelper FromStruct => default;
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, type => type.Name == "StructHelper" && type.Kind == CompileBackTypeKind.Struct);
            Assert.Contains("public struct StructHelper", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsNestedClosureRoot()
    {
        var assemblyPath = CompileFixture("""
            public class Outer
            {
                public class Inner
                {
                }
            }

            public class Class1
            {
                public Outer.Inner FromNested => new Outer.Inner();
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(result.Plan.PrintRequests, type =>
                type.Name == "Outer"
                && type.NestedTypes.Any(nested => nested.Name == "Inner"));
            Assert.Contains("public class Outer", result.Source);
            Assert.Contains("public class Inner", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsNestedClosureMemberRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Outer
            {
                internal static int Leak() => 13;

                public class Inner
                {
                    public static int GetValue() => 42;
                }
            }

            public class Class1
            {
                public int FromNested => Outer.Inner.GetValue();
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.PrintRequests, type =>
                type.Name == "Outer"
                && !type.Members.Any(member => member.Name == "Leak")
                && type.NestedTypes.Any(nested =>
                    nested.Name == "Inner"
                    && nested.Members.Any(member => member.Name == "GetValue")));
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Inner"
                && type.Members.Any(member => member.Name == "GetValue"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "GetValue")));
            Assert.Contains("public static int GetValue()", result.Source);
            Assert.DoesNotContain("Leak", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsTargetNestedMemberRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public class Inner
                {
                    public static int GetValue() => 42;
                }

                public int FromNested => Inner.GetValue();
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types, type => type.Name == "Class1");
            Assert.Contains(result.Plan.Types, nested =>
                nested.Name == "Inner"
                && nested.Members.Any(member => member.Name == "GetValue"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "GetValue")));
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains("public static int GetValue()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_EmitsOuterRequirementForNestedMethodTarget()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int GetOuterValue() => 42;

                public class Inner
                {
                    public int FromOuter() => new Class1().GetOuterValue();
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1.Inner", "FromOuter", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var root = Assert.Single(result.Plan.Types, type => type.Name == "Class1");
            Assert.Contains(root.Members, member => member.Name == "GetOuterValue"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "GetOuterValue"));
            var inner = Assert.Single(result.Plan.Types, type => type.Name == "Inner");
            Assert.Contains(inner.Members, member => member.Name == "FromOuter"
                && member.SourceFacts.Any(fact => fact.Id == "target-method"));
            Assert.Contains("public int GetOuterValue()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_EmitsNestedRequirementForTopLevelMethodTarget()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public class Inner
                {
                    public static int GetValue() => 42;
                }

                public int FromNested() => Inner.GetValue();
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "FromNested", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, nested =>
                nested.Name == "Inner"
                && nested.Members.Any(member => member.Name == "GetValue"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "GetValue")));
            Assert.Contains("public static int GetValue()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_DoesNotSurfaceOuterMembersForTypeOnlyNestedClosure()
    {
        var assemblyPath = CompileFixture("""
            internal class Hidden
            {
            }

            public class Outer
            {
                public class Inner
                {
                    internal static Hidden LeakNested() => new Hidden();
                }

                internal static Hidden Leak() => new Hidden();
            }

            public class Class1
            {
                public Outer.Inner FromNested => new Outer.Inner();
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("public class Inner", result.Source);
            Assert.DoesNotContain("Leak", result.Source);
            Assert.DoesNotContain("LeakNested", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_SurfacesReferencedInstanceField()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                private readonly string _value = "Hello";

                public string Value => _value;
            }
            """);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("public readonly string _value;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_DoesNotEmitUnreferencedClosureConstFields()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public const int ConstValue = 42;
                public int Value => ConstValue;
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
            Assert.DoesNotContain("ConstValue", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_DoesNotEmitUnreferencedNonFiniteClosureConstFields()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public const float FloatNaN = float.NaN;
                public const double DoubleInfinity = double.PositiveInfinity;
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
            Assert.DoesNotContain("FloatNaN", result.Source);
            Assert.DoesNotContain("DoubleInfinity", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackFirstPropertyGetter_EmitsUnsafePointerTargetAndField()
    {
        var assemblyPath = CompileFixture("""
            public unsafe class Class1
            {
                private int* _pointer;

                public int* Pointer => _pointer;
            }
            """, allowUnsafe: true);
        try
        {
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("public unsafe int* Pointer", result.Source);
            Assert.Contains("public unsafe int* _pointer;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_UsesMetadataMethodOverloadIndex()
    {
        var assemblyPath = CompileFixture("""
            public abstract class Class1
            {
                public abstract int Method1();

                public int Method1(int value) => value + 1;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Method1", 1)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal(1, result.Plan.TargetMethod.Overload);
            Assert.Contains("public int Method1(int value)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }
}
