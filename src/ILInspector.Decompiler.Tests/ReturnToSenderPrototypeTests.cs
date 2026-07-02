using ILInspector.DecompilerHarness;
using ILInspector.Decompiler;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
public class ReturnToSenderPrototypeTests
{
    [Fact]
    public void CompileBackFirstPropertyGetter_RoundTripsMinimalClassProperty()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public string Method1 => "Hello World";
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal("Class1", result.Plan.TargetMethod.Type);
            Assert.Equal("get_Method1", result.Plan.TargetMethod.Method);
            Assert.Contains("public class Class1", result.Source);
            Assert.Contains("public string Method1", result.Source);
            Assert.Contains("return \"Hello World\";", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_ExposesTypedModuleAndTypeShellPlan()
    {
        var assemblyPath = CompileFixture("""
            namespace Fixtures;

            public class Class1
            {
                public string Method1 => "Hello World";
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);
            var type = Assert.Single(result.Plan.Types);
            var member = Assert.Single(type.Members);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Equal("Fixtures", type.Namespace);
            Assert.Equal("Class1", type.Name);
            Assert.Equal(CompileBackTypeKind.Class, type.Kind);
            Assert.Equal("Method1", member.Name);
            Assert.Equal(CompileBackMemberKind.PropertyGet, member.Kind);
            Assert.Equal("string", member.Type);
            Assert.Contains("System", result.Plan.Module.Usings);
            Assert.Empty(result.Plan.Module.AssemblyAttributes);
            Assert.Empty(result.Plan.Module.ModuleAttributes);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_UsesDependencyReferencesAndNamespaces()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("External", result.Plan.Module.Usings);
            Assert.Contains("public External.Greeting Method1", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackPropertyGetters_EvaluatesSupportedGetterLadderWithCap()
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
            var results = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2);

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
    public void CompileBackPropertyGetters_AddsSameAssemblyReturnTypeClosureRoot()
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
            var results = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2);

            Assert.Collection(
                results,
                first =>
                {
                    Assert.Equal("get_SameAssemblyType", first.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, first.Status);
                    Assert.Contains(first.Plan.Types, type => type.Name == "Helper");
                    Assert.Contains(first.Plan.TypeRequirements, requirement =>
                        requirement.Type.DisplayName == "Helper"
                        && requirement.SourceFacts.Any(fact => fact.Id == "closure-root"
                            && fact.Producer == "roslyn"
                            && fact.Detail.StartsWith("CS0246", StringComparison.Ordinal)));
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
    public void CompileBackFirstPropertyGetter_EmitsSameAssemblyClosureMemberSurface()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
                .Single(item => item.Plan.TargetMethod.Method == "get_FromHelper");

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Helper"
                && type.SourceFacts.Any(fact => fact.Id == "closure-root" && fact.Producer == "roslyn")
                && type.Members.Any(member => member.Name == "Value" && member.Kind == CompileBackMemberKind.PropertyGet)
                && type.Members.Any(member => member.Name == "Create" && member.Kind == CompileBackMemberKind.Method && member.IsStatic));
            Assert.Contains("public int Value", result.Source);
            Assert.Contains("public static Helper Create()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_EmitsTargetRootSiblingMemberSurface()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types);
            Assert.Contains(type.SourceFacts, fact =>
                fact.Producer == "roslyn"
                && fact.Id == "closure-root"
                && fact.Detail.StartsWith("CS0103", StringComparison.Ordinal));
            Assert.Contains(type.Members, member => member.Name == "GetValue" && member.Kind == CompileBackMemberKind.Method);
            Assert.Contains("public int GetValue()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_GrowsClosureFromNamespaceSegmentDiagnostic()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(result.Plan.Types, type =>
                type.Namespace == "Other.Deep"
                && type.Name == "Helper"
                && type.SourceFacts.Any(fact => fact.Producer == "roslyn" && fact.Id == "closure-root"));
            Assert.Contains("namespace Other.Deep", result.Source);
            Assert.Contains("public class Helper", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_GrowsClosureAcrossMultipleIterations()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
                .Single(item => item.Plan.TargetMethod.Method == "get_FromChain");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "A"
                && type.SourceFacts.Any(fact => fact.Producer == "roslyn" && fact.Id == "closure-root")
                && type.Members.Any(member => member.Name == "Create"));
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "B"
                && type.SourceFacts.Any(fact => fact.Producer == "roslyn" && fact.Id == "closure-root")
                && type.Members.Any(member => member.Name == "Value"));
            Assert.Contains("public static B Create()", result.Source);
            Assert.Contains("public int Value", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_AddsOnlyOneRootWhenSimpleNamesCollide()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Single(result.Plan.Types, type => type.Name == "Helper");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_EscapesKeywordNamespacesInClosureRoots()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackPropertyGetters_UsesAutoPropertyShellForRecordPropertyGetter()
    {
        var assemblyPath = CompileFixture("""
            public sealed record Snapshot(string Assembly, int Count);
            """);
        try
        {
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 8)
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
    public void RunComparison_UsesExactCurrentTargetsPastPerTypeSampleCap()
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
                var exitCode = ReturnToSender.RunComparison([assemblyPath], cap: 10, maxExamples: 10);

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
    public void CompileBackFirstPropertyGetter_PreservesStaticTargetPropertyShape()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public static int StaticValue => 42;
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);
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
    public void CompileBackFirstPropertyGetter_EmitsStructClosureRoot()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackFirstPropertyGetter_EmitsNestedClosureRoot()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(result.Plan.Types, type =>
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
    public void CompileBackFirstPropertyGetter_DoesNotSurfaceOuterMembersForTypeOnlyNestedClosure()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackFirstPropertyGetter_SurfacesReferencedInstanceField()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("public string _value;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_EmitsClosureConstFieldsWithInitializers()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
                .Single(item => item.Plan.TargetMethod.Method == "get_FromHelper");

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("public const int ConstValue = 42;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_EmitsNonFiniteClosureConstFields()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
                .Single(item => item.Plan.TargetMethod.Method == "get_FromHelper");

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("public const float FloatNaN = float.NaN;", result.Source);
            Assert.Contains("public const double DoubleInfinity = double.PositiveInfinity;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_EmitsUnsafePointerTargetAndField()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackTargets_UsesMetadataMethodOverloadIndex()
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
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
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

    [Fact]
    public void CompileBackTargets_RoundTripsConstructorAssigningGetOnlyAutoProperty()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public Class1(string message)
                {
                    Message = message;
                }

                public string Message { get; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", ".ctor", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public string Message { get; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesPrimaryConstructorForFieldInitializerPrologue()
    {
        var assemblyPath = CompileFixture("""
            public class Class1(string message)
            {
                private readonly string _message = message;

                public string Message => _message;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", ".ctor", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public class Class1(string message)", result.Source);
            Assert.Contains("public string _message = message;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackSourceComposer_PrimaryConstructorParametersPrecedeGenericConstraints()
    {
        var method = typeof(CompileBackSourceComposer).GetMethod(
            "AddPrimaryConstructorParameters",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = Assert.IsType<string>(method?.Invoke(
            null,
            ["public class Class1<T> where T : class", "string message"]));

        Assert.Equal("public class Class1<T>(string message) where T : class", result);
    }

    [Fact]
    public void CompileBackTargets_RoundTripsAutoPropertySetter()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Value { get; set; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "set_Value", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public int Value { get; set; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsIndexerSetter()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                private readonly int[] _values;

                public Class1()
                {
                    _values = new int[4];
                }

                public int this[int index]
                {
                    get => _values[index];
                    set => _values[index] = value;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "set_Item", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public int this[int index]", result.Source);
            Assert.Contains("set", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DoesNotDuplicateBodyBackedSetterDuringClosureSurface()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                private int _value;

                public int Value
                {
                    set => _value = value;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "set_Value", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public int Value", result.Source);
            Assert.DoesNotContain("throw null", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_EmitsGetterStubForSetterBodyThatReadsProperty()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    set
                    {
                        if (Value != value)
                            _value = value;
                    }
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "set_Value", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("get", result.Source);
            Assert.Contains("throw null", result.Source);
            Assert.Contains("Value != value", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_SurfacesUnsafeNestedClosureMember()
    {
        var assemblyPath = CompileFixture("""
            public unsafe class Outer
            {
                public class Inner
                {
                    public static int* GetPointer() => null;
                }
            }

            public unsafe class Class1
            {
                public int* Pointer => Outer.Inner.GetPointer();
            }
            """, allowUnsafe: true);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("public static unsafe int* GetPointer()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    static string CompileFixture(
        string source,
        string? directory = null,
        string assemblyName = "fixture",
        IReadOnlyList<MetadataReference>? additionalReferences = null,
        bool allowUnsafe = false)
    {
        directory ??= Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{assemblyName}.dll");
        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Concat(additionalReferences ?? []);
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable,
                allowUnsafe: allowUnsafe));

        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }

    static void DeleteFixture(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(assemblyPath);
        File.Delete(assemblyPath);
        if (directory is not null && Path.GetFileName(directory).StartsWith("return-to-sender-", StringComparison.Ordinal))
            Directory.Delete(directory, recursive: true);
    }
}
