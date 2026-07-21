using ILInspector.DecompilerHarness;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Collection(ConsoleMutatorCollection.Name)]
[Trait("Area", "RoundTrip")]
public class ReturnToSenderPrototypeTests
{
    [Fact]
    public void CompileBackTargets_SynthesizesParameterlessConstructorForNestedDerivedType()
    {
        // Issue #2527 guard (Gemini review of #2732): nested types are emitted from
        // their enclosing requirement and are absent from the top-level requirement
        // map, so the synthetic-parameterless-base-constructor scan must also walk
        // nested types. Here `Outer.Nested : Base` is emitted even though it is never
        // consumed; `Base` (reconstructed with only a parameterized constructor) must
        // still receive a synthetic parameterless constructor so Nested's implicit
        // `: base()` binds natively, without relying on the compile-back floor.
        var assemblyPath = CompileFixture("""
            using System;

            public class Base
            {
                public Base(int seed)
                {
                    Seed = seed;
                }

                public int Seed { get; }
            }

            public class Outer
            {
                public Outer()
                {
                }

                public class Nested : Base
                {
                    public Nested() : base(1)
                    {
                    }
                }
            }

            public static class Use
            {
                public static void Run()
                {
                    Console.WriteLine(new Base(1));
                    Console.WriteLine(new Outer());
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Use", "Run", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DoesNotReconstructGenericBaseClass()
    {
        // Issue #2527 guard (Gemini review of #2732): a closed generic base
        // instantiation (`Derived : Base<int>`) is a TypeSpecification, which the
        // flat shell cannot carry and cannot own a synthetic constructor for. It must
        // be dropped rather than emitted, so the derived stub does not fail on an
        // implicit `: base()` with no parameterless target.
        var assemblyPath = CompileFixture("""
            using System;

            public class Base<T>
            {
                public Base(int seed)
                {
                    Seed = seed;
                }

                public int Seed { get; }
            }

            public class Derived : Base<int>
            {
                public Derived() : base(1)
                {
                }
            }

            public static class Use
            {
                public static void Run()
                {
                    Console.WriteLine(new Base<int>(1));
                    Console.WriteLine(new Derived());
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Use", "Run", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.DoesNotContain(": Base", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

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

            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Equal("Class1", result.Plan.TargetMethod.Type);
            Assert.Equal("get_Method1", result.Plan.TargetMethod.Method);
            Assert.Contains("public class Class1", result.Source);
            Assert.Contains("public string Method1", result.Source);
            Assert.Contains("return \"Hello World\";", result.Source);
            Assert.NotNull(result.FidelityDiff);
            Assert.True(result.FidelityDiff.IsExact);

            var compileBack = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                row => row.Type == "Class1" && row.Method == "get_Method1");
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, compileBack.Status);
            Assert.NotNull(compileBack.FidelityDiff);
            Assert.True(compileBack.FidelityDiff.IsExact);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_RoundTripsInheritedExplicitInterfaceProperty()
    {
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitPropertyFixture : IDerived
            {
                int IBase.Value => 42;

                void IBase.Touch()
                {
                }
            }

            public interface IDerived : IBase
            {
            }

            public interface IBase
            {
                int Value { get; }

                void Touch();
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("int IBase.Value", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IBase_Value", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_RoundTripsExplicitInterfaceIndexer()
    {
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitIndexerFixture : IValues
            {
                int IValues.this[int index] => index;
            }

            public interface IValues
            {
                int this[int index] { get; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitIndexerFixture",
                    "IValues.get_Item",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("int IValues.this[int index]", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("public int IValues.this", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_KeepsStaticOnExplicitInterfaceProperty()
    {
        // #2875: a C# 11 static-abstract interface member implemented explicitly must keep
        // `static` (while omitting the access modifier). Dropping `static` reconstructs an
        // instance member and fails the interface contract (CS0106/CS0539).
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitStaticFixture : ICounter
            {
                static int ICounter.Count => 7;
            }

            public interface ICounter
            {
                static abstract int Count { get; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitStaticFixture",
                    "ICounter.get_Count",
                    0)]));

            Assert.Contains("static int ICounter.Count", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("public int ICounter.Count", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("public static int ICounter.Count", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Theory]
    [InlineData("IBaseEvents.add_Changed")]
    [InlineData("IBaseEvents.remove_Changed")]
    public void CompileBackEventAccessor_RoundTripsExplicitInterfaceEvent(string accessorName)
    {
        var assemblyPath = CompileFixture("""
            using System;

            public sealed class ExplicitEventFixture : IDerivedEvents
            {
                event Action IBaseEvents.Changed
                {
                    add
                    {
                        Console.WriteLine(value);
                    }
                    remove
                    {
                        Console.WriteLine(value);
                    }
                }
            }

            public interface IDerivedEvents : IBaseEvents
            {
            }

            public interface IBaseEvents
            {
                event Action Changed;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitEventFixture",
                    accessorName,
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("event Action IBaseEvents.Changed", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("public event Action IBaseEvents.Changed", result.Source, StringComparison.Ordinal);
            Assert.Contains("add", result.Source, StringComparison.Ordinal);
            Assert.Contains("remove", result.Source, StringComparison.Ordinal);
            Assert.Contains("Console.WriteLine(value);", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Theory]
    [InlineData("IBaseEvents.add_Changed", "Console.WriteLine(\"adding\");", "Console.WriteLine(\"removing\");")]
    [InlineData("IBaseEvents.remove_Changed", "Console.WriteLine(\"removing\");", "Console.WriteLine(\"adding\");")]
    public void CompileBackEventAccessor_RaisesSiblingAccessorBodyInsteadOfThrowStub(
        string accessorName,
        string expectedTargetBody,
        string expectedSiblingBody)
    {
        // Issue #2913: both explicit-interface event accessors have real IL
        // bodies here (distinguishable add/remove literals). Targeting either
        // one must raise BOTH bodies in a single reconstruction rather than
        // rendering the non-targeted accessor as an honest `throw null;` stub,
        // and each accessor's compile-back verdict must be tracked
        // independently via Result.SiblingAccessor.
        var assemblyPath = CompileFixture("""
            using System;

            public sealed class ExplicitEventFixture : IDerivedEvents
            {
                event Action IBaseEvents.Changed
                {
                    add
                    {
                        Console.WriteLine("adding");
                    }
                    remove
                    {
                        Console.WriteLine("removing");
                    }
                }
            }

            public interface IDerivedEvents : IBaseEvents
            {
            }

            public interface IBaseEvents
            {
                event Action Changed;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitEventFixture",
                    accessorName,
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains(expectedTargetBody, result.Source, StringComparison.Ordinal);
            Assert.Contains(expectedSiblingBody, result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("throw null;", result.Source, StringComparison.Ordinal);

            Assert.NotNull(result.SiblingAccessor);
            Assert.True(
                result.SiblingAccessor!.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.SiblingAccessor.Status}: {result.SiblingAccessor.MethodName}");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackEventAccessor_KeepsStaticOnExplicitInterfaceEvent()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public sealed class ExplicitStaticEventFixture : IStaticEvents
            {
                static event Action IStaticEvents.Changed
                {
                    add
                    {
                        Console.WriteLine(value);
                    }
                    remove
                    {
                        Console.WriteLine(value);
                    }
                }
            }

            public interface IStaticEvents
            {
                static abstract event Action Changed;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitStaticEventFixture",
                    "IStaticEvents.add_Changed",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("static event Action IStaticEvents.Changed", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("public static event Action IStaticEvents.Changed", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackEventAccessor_PreservesOrdinaryFieldLikeEventHandling()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public sealed class OrdinaryEventFixture
            {
                public event Action Changed;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "OrdinaryEventFixture",
                    "add_Changed",
                    0)]));

            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("public void add_Changed(Action value)", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("event Action Changed", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_PreservesRequiredImplicitInterfaceProperty()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Consumer
            {
                public int Read => ((IBase)new ImplicitPropertyFixture()).Value;
            }

            public sealed class ImplicitPropertyFixture : IDerived
            {
                public int Value => 42;

                public void Touch()
                {
                }
            }

            public interface IDerived : IBase
            {
            }

            public interface IBase
            {
                int Value { get; }

                void Touch();
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("public int Value", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("public void Touch", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_ProjectsPropertiesFromRequiredInterfaceSurface()
    {
        var assemblyPath = CompileFixture("""
            public sealed class InheritedTypeParameter : IGenericTypeParameter, IGenericParameter
            {
                private readonly IGenericTypeParameter _parentParameter;

                public InheritedTypeParameter(IGenericTypeParameter parentParameter)
                {
                    _parentParameter = parentParameter;
                }

                public bool MustBeReferenceType => _parentParameter.MustBeReferenceType;

                public ITypeDefinition DefiningType => _parentParameter.DefiningType;
            }

            public interface IGenericTypeParameter : IGenericParameter
            {
                ITypeDefinition DefiningType { get; }
            }

            public interface IGenericParameter
            {
                bool MustBeReferenceType { get; }
            }

            public interface ITypeDefinition
            {
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "InheritedTypeParameter",
                    "get_MustBeReferenceType",
                    0)]));

            Assert.DoesNotContain("CS0535", result.Detail ?? "", StringComparison.Ordinal);
            var targetType = Assert.Single(
                result.Plan.Types,
                type => type.Name == "InheritedTypeParameter");
            Assert.Contains(targetType.Members, member =>
                member.Name == "DefiningType"
                && member.SourceFacts.Any(fact =>
                    fact.Id == "required-interface-property"));
            Assert.Equal(1, targetType.Members.Count(member => member.Name == "MustBeReferenceType"));
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
    public void CompileBackFirstPropertyGetter_DeduplicatesSystemUsing_WhenBodyAlreadyReferencesSystem()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackFirstPropertyGetter_FallsBackToCompileBackFloorForAttributeShellStall()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CorpusParity_DoesNotApplyCompileBackFloorToRtsFailure()
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
            var floored = Assert.Single(ReturnToSender.CompileBackTargets(assemblyPath, [target]));
            Assert.True(floored.UsedCompileBackFloor, floored.Detail);
            var reference = Assert.IsType<FidelityCheck.CompileBackResult>(floored.CompileBackFloor);

            var native = Assert.Single(ReturnToSender.CompileBackTargets(
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
    public void CorpusFidelity_EvaluatesCompileBackSelectedTargetsThroughReturnToSender()
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
            var results = CorpusSensor.EvaluateReturnToSenderForTesting(assemblyPath, cap: 10);
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
    public void CompileBackFirstPropertyGetter_DeduplicatesRepeatedPreciseClosureMembers()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
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
    public void CompileBackFirstPropertyGetter_UsesTypedClosureFieldForStaticMemberAccess()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackFirstPropertyGetter_UsesTypedTargetFieldForUnqualifiedFieldAccess()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackFirstPropertyGetter_DoesNotEmitGeneratedBackingFieldRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Value { get; } = 42;
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackTargets_PreservesThisConstructorChainOpcodes()
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
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
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

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenChainedConstructorUnreconstructable()
    {
        // Issue #2678 guard: when the chained-to constructor has an unsupported
        // signature (a function pointer), the planner drops it from the shell. A
        // same-arity sibling ctor pulled in by another dependency must NOT be
        // mistaken for the chained-to ctor: emitting `: this(args)` would bind to
        // the wrong overload and fail with CS1503. The initializer must be
        // stripped, falling back to an (empty) body that still compiles.
        var assemblyPath = CompileFixture(
            """
            public unsafe class Chained
            {
                public Chained(int value) : this((delegate*<void>)value)
                {
                    _ = new Chained(true);
                }

                public Chained(delegate*<void> callback)
                {
                }

                public Chained(bool flag)
                {
                }
            }
            """,
            allowUnsafe: true);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Chained", ".ctor", 0,
                    "(corelib:System.Int32) -> corelib:System.Void")]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ValueBoxChainArgumentIsVisibleOpcodeDiffNotFalseExact()
    {
        // Issue #2726 / adversarial review: a value-type box in a chain argument
        // (`: this((object)1)` — `ldc.i4.1; box int32; call C::.ctor(object)`) is
        // NOT the oracle blind spot. If the printer drops the boxing cast and
        // prints `: this(1)`, the shell binds `C(int)` and the recompiled body
        // OMITS the `box` opcode — a difference the opcode-name comparison already
        // sees, so it surfaces as an honest OpcodeDiff, never a false Exact. (The
        // real blind spot is a REFERENCE upcast/`null`, which emits no distinguishing
        // opcode; the product printer now spells those at their parameter type — see
        // the SelfRecursive/CrossArity/ReviewerFixture canaries below.)
        var assemblyPath = CompileFixture("""
            public class C
            {
                public C(object x)
                {
                }

                public C(int x)
                {
                }

                public C(string z) : this((object)1)
                {
                    _ = new C(2);
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 2)]));

            Assert.Contains(": this(", result.Source);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesReferenceUpcastChainArgument()
    {
        // Issue #2726 / adversarial review (GPT-5.5 + Gemini 3.1 Pro): a reference
        // upcast chain argument (`: this((object)text)`, `text` a `string`) emits
        // NO IL conversion opcode, so a wrong rebind is invisible to the oracle's
        // opcode-name comparison — the false-Exact blind spot. The product printer
        // now spells the argument at its parameter type (`: this((object)text)`),
        // so the shell rebinds to the original `C(object)` instead of the target
        // `C(string)` calling itself (CS0516). RTS stays a C#-free orchestrator; the
        // fidelity knowledge lives in the printer and the recompile round-trips
        // Exact.
        var assemblyPath = CompileFixture("""
            public class C
            {
                public C(object value)
                {
                }

                public C(string text) : this((object)text)
                {
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 1)]));

            Assert.Contains(": this((object)", result.Source);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesConstructorChainWhenArityIsUnique()
    {
        // Issue #2678: a chain whose arguments the printer cannot type precisely (a
        // bare `null`) is still safe to emit when exactly one constructor in the
        // shell has the chain's argument count — a normal-form exact-arity match
        // has no competing overload, so `: this(1, 2, null)` binds unambiguously to
        // the sole three-argument constructor. The initializer must be preserved so
        // the constructor round-trips Exact.
        var assemblyPath = CompileFixture("""
            public class C
            {
                public C(int a, int b, string s)
                {
                    S = s;
                }

                public C(string z) : this(1, 2, null)
                {
                }

                public string S { get; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 1)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesConstructorChainWhenArgumentIsAssignableNotIdentity()
    {
        // Issue #2726: a chain argument whose printed type is assignable but not
        // identity-equal to the chained-to parameter (`string[]` -> covariant
        // `IEnumerable<string>`) binds unambiguously in the shell even though a
        // same-arity sibling exists — no other one-parameter constructor accepts a
        // `string[]`. The old RTS gate modelled C# overload resolution and, seeing
        // a non-faithful argument sharing arity with a sibling, stripped the
        // initializer (OpcodeDiff). RTS no longer predicts binding: it emits the
        // product's chain and lets the Roslyn oracle judge, so this now round-trips
        // Exact. Mirrors the NuGet.Versioning SemanticVersion chains this issue
        // targeted (`this(version, ParseReleaseLabels(label), metadata)`).
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;
            public class V
            {
                public V(string label) : this(Parse(label))
                {
                }

                public V(IEnumerable<string> labels)
                {
                    Labels = labels;
                }

                public IEnumerable<string> Labels { get; }

                private static string[] Parse(string s) => new[] { s };
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("V", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesNullLiteralChainArgumentAgainstCrossAritySibling()
    {
        // Issue #2726 / adversarial review: a type-less `null` chain argument
        // re-resolves against every same- and cross-arity sibling in the shell. Here
        // `C()` chains `: this((object)null)` while `C(string, params int[])` can
        // absorb a single `null` and offers a better conversion (string is more
        // derived than object). A bare `: this(null)` would silently rebind to the
        // params sibling — invisible to the opcode-name oracle. The product printer
        // now spells `: this((object)null)`, pinning the original `C(object)` bind,
        // so the recompile round-trips Exact.
        var assemblyPath = CompileFixture("""
            public class C
            {
                public C(object x)
                {
                }

                public C(string x, params int[] y)
                {
                }

                public C() : this((object)null)
                {
                    _ = new C("hello");
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 2)]));

            Assert.Contains(": this((object)null)", result.Source);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ReviewerNullRebindFixtureRoundTripsExact()
    {
        // Regression canary for the exact fixture two adversarial reviewers (GPT-5.5
        // and Gemini 3.1 Pro) used to prove the pre-fix false Exact: `: this((object)
        // null)` rendered as `: this(null)` rebound `null` to the more-derived
        // `C(string)` while the opcode-name sequence (`ldnull call`) stayed
        // identical, so the oracle reported a false Exact. The product printer now
        // spells the parameter type, so the shell binds the original `C(object)` and
        // the round-trip is a TRUE Exact.
        var assemblyPath = CompileFixture("""
            public class C
            {
                public C(object x)
                {
                }

                public C(string s)
                {
                }

                public C() : this((object)null)
                {
                    _ = new C("body");
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 2)]));

            Assert.Contains(": this((object)null)", result.Source);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_LambdaTargetTypedChainArgumentRemainsHonestNonExact()
    {
        // Issue #2726: a lambda argument prints typeless (`() => ...`) and relies on
        // C# target-typing, which the chain-argument parameter-type cast does not
        // reconstruct (the argument's IR type already equals the parameter, so no
        // reference-upcast cast applies). Here `C()` chains `: this((Func<int>)(() =>
        // 1))` while the shell also contains `C(Expression<Func<int>>)` (pulled in by
        // the body). Both constructors accept `() => 1`, so the printed
        // `: this(() => 1)` is ambiguous (CS0121). This is a distinct, lower-risk
        // fidelity gap (lambda cast preservation, not reference rebinding) and it
        // surfaces as an honest non-Exact — never a false Exact.
        var assemblyPath = CompileFixture("""
            using System;
            using System.Linq.Expressions;
            public class C
            {
                public C(Func<int> x)
                {
                }

                public C(Expression<Func<int>> y)
                {
                }

                public C() : this((Func<int>)(() => 1))
                {
                    Expression<Func<int>> e = () => 2;
                    _ = new C(e);
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 2)]));

            Assert.Contains(": this(", result.Source);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ReconstructsBaseClassForCovariantArgument()
    {
        // Issue #2527: RTS minimal shells used to drop base classes entirely
        // (BaseTypeSignature emitted only System.Attribute). A body that relies on
        // an implicit derived->base conversion — passing a `Dog` where an `Animal`
        // is expected — then failed to compile (CS1503) because the shell declared
        // `Dog` with no base. Reconstructing the real base class restores the
        // covariant conversion so the method round-trips.
        var assemblyPath = CompileFixture("""
            public class Animal
            {
                public Animal(string name)
                {
                    Name = name;
                }

                public string Name { get; }
            }

            public class Dog : Animal
            {
                public Dog(string name) : base(name)
                {
                }
            }

            public static class Shelter
            {
                public static string Describe(Dog dog) => Name(dog);

                private static string Name(Animal animal) => animal.Name;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Shelter", "Describe", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.Contains("class Dog : Animal", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_SynthesizesParameterlessConstructorForReconstructedBase()
    {
        // Issue #2527: once base classes are reconstructed, a derived stub emits an
        // implicit `: base()`. When the base shell carries only a parameterized
        // constructor (its base(...) chain is left empty in the flat shell), that
        // implicit call has nothing to bind to (CS7036/CS1729). The planner
        // synthesizes an accessible parameterless constructor on the reconstructed
        // base so base-class reconstruction never breaks the derived shell. Here the
        // body constructs a `Base` directly (so `Base(int)` is reconstructed with no
        // parameterless sibling) and a `Widget : Base` (whose stub needs `: base()`).
        var assemblyPath = CompileFixture("""
            public class Base
            {
                public Base(int seed)
                {
                    Seed = seed;
                }

                public int Seed { get; }
            }

            public class Widget : Base
            {
                public Widget(int seed) : base(seed)
                {
                }
            }

            public static class Factory
            {
                public static Widget Create()
                {
                    Base b = new Base(1);
                    _ = b.Seed;
                    return new Widget(21);
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Factory", "Create", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.Contains("class Widget : Base", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DoesNotReconstructExternalBaseClass()
    {
        // Issue #2527 guard (GPT-5.5 review of #2732): base-class reconstruction must
        // stay same-assembly. An external (referenced-assembly) base whose only
        // constructor is parameterized cannot receive a synthesized parameterless
        // constructor (the shell does not own it), so reconstructing `Derived : Base`
        // would make the derived stub's implicit `: base()` fail with CS7036 where the
        // baseline dropped the base and compiled. The shell must not declare the
        // external base.
        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var dependencyPath = CompileFixture("""
            namespace External;

            public class Base
            {
                public Base(int seed)
                {
                    Seed = seed;
                }

                public int Seed { get; }
            }
            """, directory, "ExternalLib");
        var assemblyPath = CompileFixture("""
            using External;

            public class Derived : Base
            {
                public Derived(int seed) : base(seed)
                {
                }
            }

            public static class Factory
            {
                public static Derived Make(Derived value) => value;
            }
            """, directory, "Fixture", [MetadataReference.CreateFromFile(dependencyPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Factory", "Make", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.DoesNotContain(": Base", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesTargetBackingFieldWriteForConstructorAssignment()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public Class1(string message)
                {
                    Method1 = message;
                }

                public string Method1 { get; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", ".ctor", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(type.Members, member =>
                member.Name == "Method1"
                && member.Kind == CompileBackMemberKind.PropertyGet
                && member.SourceFacts.Any(fact => fact.Id == "target-backing-field-write" && fact.Detail == "<Method1>k__BackingField"));
            Assert.Contains("public string Method1 { get; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DoesNotDuplicatePrimaryConstructorAutoPropertyInitializer()
    {
        var assemblyPath = CompileFixture("""
            public class Class1(int value)
            {
                public int Value { get; } = value;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", ".ctor", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.OperandDiff, result.Status);
            Assert.NotNull(result.FidelityDiff);
            Assert.False(result.FidelityDiff.IsExact);
            Assert.DoesNotContain("already contains a definition", result.Detail ?? "", StringComparison.Ordinal);
            Assert.Equal(1, Assert.Single(result.Plan.Types).Members.Count(member => member.Name == "Value"));
            Assert.Contains("public int Value", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesTargetBackingFieldWriteForStaticConstructorAssignment()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                static Class1()
                {
                    Value = 42;
                }

                public static int Value { get; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", ".cctor", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(type.Members, member =>
                member.Name == "Value"
                && member.IsStatic
                && member.SourceFacts.Any(fact => fact.Id == "target-backing-field-write" && fact.Detail == "<Value>k__BackingField"));
            Assert.Contains("public static int Value { get; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void IrImporter_ClassifiesConstructorMethodKinds()
    {
        // Typed constructor evidence (migration 3): the importer decodes the
        // reserved metadata method name into IrFunction.MethodKind so compile-back
        // composition routes it instead of re-matching ".ctor"/".cctor" strings.
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                static Class1() { Value = 42; }
                public Class1(int other) { Other = other; }
                public void M() { }
                public static int Value { get; }
                public int Other { get; }
            }
            """);
        try
        {
            using var source = MetadataSource.Open(assemblyPath);
            Assert.Equal(IrMethodKind.StaticConstructor, IrImporter.Import(source, "Class1", ".cctor", publicOnly: false)!.MethodKind);
            Assert.Equal(IrMethodKind.Constructor, IrImporter.Import(source, "Class1", ".ctor", publicOnly: false)!.MethodKind);
            Assert.Equal(IrMethodKind.Method, IrImporter.Import(source, "Class1", "M", publicOnly: false)!.MethodKind);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_SeedsTargetInterfaceRoot()
    {
        var assemblyPath = CompileFixture("""
            public interface IValue
            {
                int GetValue();
            }

            public class Class1 : IValue
            {
                public int GetValue() => 42;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "GetValue", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "IValue"
                && type.SourceFacts.Any(fact => fact.Producer == "metadata" && fact.Id == "target-interface"));
            Assert.DoesNotContain(result.Plan.Types.SelectMany(type => type.SourceFacts), fact =>
                fact.Producer == "roslyn" && fact.Id == "closure-root");
            Assert.Contains("public interface IValue", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_SeedsTargetInterfaceRootWhenTargetAssemblyNameIsCorelibFacade()
    {
        // A target assembly whose own name is a canonicalized corelib facade
        // (System.Runtime, mscorlib, ...) must still resolve its own interface
        // definitions: TypeRefDecoder canonicalizes the assembly name, so the
        // same-assembly gate has to canonicalize too. Assert on the seeded plan
        // facts (not recompile status) so this holds independent of the compile
        // environment.
        var assemblyPath = CompileFixture(
            """
            public interface IValue
            {
                int GetValue();
            }

            public class Class1 : IValue
            {
                public int GetValue() => 42;
            }
            """,
            assemblyName: "System.Runtime");
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "GetValue", 0)]));

            Assert.Contains(result.Plan.Types, type =>
                type.Name == "IValue"
                && type.SourceFacts.Any(fact => fact.Producer == "metadata" && fact.Id == "target-interface"));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesTypedObjectInitializerPropertyRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public int Value { get; set; }
            }

            public class Class1
            {
                public Helper Method1(int value) => new Helper { Value = value };
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Method1", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var helper = Assert.Single(result.Plan.Types, type => type.Name == "Helper");
            Assert.DoesNotContain(helper.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(helper.Members, member =>
                member.Name == "Value"
                && member.Kind == CompileBackMemberKind.PropertyGet
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-property" && fact.Detail == "set_Value"));
            Assert.Contains("public int Value { get; set; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesTypedTargetObjectInitializerPropertyRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Value { get; set; }

                public Class1 Method1(int value) => new Class1 { Value = value };
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Method1", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(type.Members, member =>
                member.Name == "Value"
                && member.Kind == CompileBackMemberKind.PropertyGet
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-property" && fact.Detail == "set_Value"));
            Assert.Contains("public int Value { get; set; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_KeepsTypeResolvedCs0117PropertyFallback()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public static int StaticValue => 42;

                public int FromStatic => Class1.StaticValue;
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 3)
                .Single(item => item.Plan.TargetMethod.Method == "get_FromStatic");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var type = Assert.Single(result.Plan.Types);
            Assert.Contains(type.SourceFacts, fact =>
                fact.Producer == "roslyn"
                && fact.Id == "closure-member"
                && fact.Detail.StartsWith("CS0117", StringComparison.Ordinal));
            Assert.Contains(type.Members, member => member.Name == "StaticValue" && member.Kind == CompileBackMemberKind.PropertyGet);
            Assert.Contains("public static int StaticValue", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_KeepsTypeResolvedCs1061PropertyFallback()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Other => 42;

                public int FromOther(Class1 self) => self.Other;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "FromOther", 0)]));

            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            var type = Assert.Single(result.Plan.Types);
            Assert.Contains(type.SourceFacts, fact =>
                fact.Producer == "roslyn"
                && fact.Id == "closure-member"
                && fact.Detail.StartsWith("CS1061", StringComparison.Ordinal));
            Assert.Contains(type.Members, member => member.Name == "Other" && member.Kind == CompileBackMemberKind.PropertyGet);
            Assert.Contains("public int Other", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_EmitsClosureConstructorRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
                public Helper(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }

            public class Class1
            {
                public int FromHelper => new Helper(42).Value;
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
                .Single(item => item.Plan.TargetMethod.Method == "get_FromHelper");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(result.Plan.Types, type =>
                type.Name == "Helper"
                && type.Members.Any(member => member.Name == ".ctor"
                    && member.Parameters.Single().Type.DisplayName == "int"
                    && member.SourceFacts.Any(fact => fact.Id == "typed-closure-constructor" && fact.Detail == ".ctor"))
                && type.Members.Any(member => member.Name == "Value"));
            Assert.Contains("public Helper(int value)", result.Source);
            Assert.Contains("public int Value", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_SelectsTypedOverloadByParameterTypes()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
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
    public void CompileBackFirstPropertyGetter_EmitsGenericClosureMemberSurface()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
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
    public void CompileBackFirstPropertyGetter_UsesPreservedDeconstructMemberEvidence()
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
            var result = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2)
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
    public void CompileBackFirstPropertyGetter_EmitsNestedClosureMemberRequirement()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackFirstPropertyGetter_EmitsTargetNestedMemberRequirement()
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
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public void CompileBackTargets_EmitsOuterRequirementForNestedMethodTarget()
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
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
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
    public void CompileBackTargets_EmitsNestedRequirementForTopLevelMethodTarget()
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
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
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
    public void CompileBackFirstPropertyGetter_DoesNotEmitUnreferencedClosureConstFields()
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
            Assert.DoesNotContain("ConstValue", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_DoesNotEmitUnreferencedNonFiniteClosureConstFields()
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
            Assert.DoesNotContain("FloatNaN", result.Source);
            Assert.DoesNotContain("DoubleInfinity", result.Source);
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
    public void CompileBackTargets_ResolvesBySignatureOverridingOrdinal()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Pick(int value) => value + 1;

                public int Pick(string value) => value.Length;
            }
            """);
        try
        {
            // Ordinal 0 is Pick(int) in count-all metadata order; the signature must win
            // and select Pick(string) instead, proving identity no longer depends on the
            // ordinal position of same-name members.
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Pick", Overload: 0, Signature: "`0(string)")]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal(1, result.Plan.TargetMethod.Overload);
            Assert.Contains("public int Pick(string value)", result.Source);
            Assert.DoesNotContain("public int Pick(int value)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_MatchesSourceBySignatureWhenDeclarationOrderDiffers()
    {
        // The compiled assembly declares Pick(int) before Pick(string); the source slice
        // reverses that order. Ordinal correlation would pair Pick(int)'s decompiled body
        // with Pick(string)'s source (a false ValidDifferent); the normalized signature
        // pairs them correctly.
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Pick(int value) => value + 1;

                public int Pick(string value) => value.Length;
            }
            """);
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"rts-signature-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "ReversedOrder.cs");
        File.WriteAllText(sourcePath, """
            public class Class1
            {
                public int Pick(string value) => value.Length;

                public int Pick(int value) => value + 1;
            }
            """);
        try
        {
            var withSignature = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Pick", Overload: 0, Signature: "`0(int)")],
                [sourcePath]));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, withSignature.Outcome);

            var ordinalOnly = Assert.Single(ReturnToSenderSourceProbe.EvaluateTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Pick", Overload: 0)],
                [sourcePath]));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidDifferent, ordinalOnly.Outcome);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            try
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void DiscoverTargets_DropsSignatureWhenNormalizationIsAmbiguous()
    {
        // A user type named Nullable<T> normalizes to the same `int?` token as
        // System.Nullable<int> in metadata. The round-trip guard must drop that
        // ambiguous signature so correlation cannot mis-select the sibling overload.
        var assemblyPath = CompileFixture("""
            namespace Sample { public readonly struct Nullable<T> { } }

            public class Class1
            {
                public int Pick(Sample.Nullable<int> value) => 1;

                public int Pick(int? value) => 2;
            }
            """);
        try
        {
            var pickTargets = ReturnToSenderSourceProbe.DiscoverTargets(assemblyPath, int.MaxValue)
                .Where(target => target.Target is { Type: "Class1", Method: "Pick" })
                .ToArray();

            Assert.Equal(2, pickTargets.Length);
            Assert.All(pickTargets, target => Assert.Null(target.Target.Signature));

            // With the signature dropped, correlation falls back to the ordinal and each
            // overload still pairs with its own source body (no false ValidDifferent).
            var results = ReturnToSenderSourceProbe.EvaluateTargets(
                assemblyPath,
                pickTargets.Select(target => target.Target).ToArray(),
                [WriteTempSource(
                    "NullableCollision.cs",
                    """
                    namespace Sample { public readonly struct Nullable<T> { } }

                    public class Class1
                    {
                        public int Pick(Sample.Nullable<int> value) => 1;

                        public int Pick(int? value) => 2;
                    }
                    """,
                    out var sourceDirectory)]);

            try
            {
                Assert.All(results, result => Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome));
            }
            finally
            {
                TryDeleteDirectory(sourceDirectory);
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ReturnToSenderSourceProbe_MatchesMixedRankArrayOverloadsBySignature()
    {
        // int[][,] and int[,][] must not cross-match: source lists ranks outer-to-inner
        // while metadata builds them inner-to-outer. With the source slice in reversed
        // declaration order, only a rank-consistent signature pairs each overload with
        // its own body.
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Rank(int[][,] value) => value.Length + 1;

                public int Rank(int[,][] value) => value.Length + 2;
            }
            """);
        var reversedSource = WriteTempSource(
            "MixedRankArrays.cs",
            """
            public class Class1
            {
                public int Rank(int[,][] value) => value.Length + 2;

                public int Rank(int[][,] value) => value.Length + 1;
            }
            """,
            out var sourceDirectory);
        try
        {
            var targets = ReturnToSenderSourceProbe.DiscoverTargets(assemblyPath, int.MaxValue)
                .Where(target => target.Target is { Type: "Class1", Method: "Rank" })
                .Select(target => target.Target)
                .ToArray();

            Assert.Equal(2, targets.Length);
            Assert.All(targets, target => Assert.NotNull(target.Signature));

            var results = ReturnToSenderSourceProbe.EvaluateTargets(assemblyPath, targets, [reversedSource]);

            Assert.All(results, result => Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void TryIsolateRecompileFailure_ClassifiesBodyDefectWhenAuthoredBodyCompiles()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("BodyDefect.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            var result = TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;");

            Assert.NotNull(result);
            Assert.Equal(ReturnToSender.FaultIsolationKind.BodyDefect, result.Kind);
            Assert.Equal(sourcePath, result.SourcePath);
            Assert.Contains("authored body compiled", result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void TryIsolateRecompileFailure_ClassifiesShellOrClosureDefectWhenAuthoredBodyAlsoFails()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource(
            "ShellOrClosureDefect.cs",
            """
            public class Class1
            {
                public int M() { return Missing.Symbol; }
            }
            """,
            out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            var result = TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return AlsoMissing.Symbol;");

            Assert.NotNull(result);
            Assert.Equal(ReturnToSender.FaultIsolationKind.ShellOrClosureDefect, result.Kind);
            Assert.Equal(sourcePath, result.SourcePath);
            Assert.Contains("CS0103", result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    static string WriteTempSource(string fileName, string source, out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), $"rts-signature-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, source);
        return path;
    }

    static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    static ReturnToSender.FaultIsolationResult? TryIsolateRecompileFailureForMethod(
        string assemblyPath,
        string sourcePath,
        string rejectedTargetBody)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);

        var (typeHandle, methodHandle) = FindMethod(reader, "Class1", "M");
        var function = IrImporter.Import(source, "Class1", "M", 0)
            ?? throw new InvalidOperationException("Could not import Class1::M.");
        var request = new MethodArtifactRequest(
            AssemblyPath: assemblyPath,
            Reader: reader,
            Function: function,
            TargetType: typeHandle,
            TargetMethod: methodHandle,
            TargetBody: new ProductTargetBody(rejectedTargetBody, []),
            FullType: "Class1",
            MethodName: "M",
            Overload: 0,
            SignatureText: "",
            ClosureRoots: new HashSet<TypeDefinitionHandle> { typeHandle },
            ClosureFacts: new Dictionary<TypeDefinitionHandle, List<CompileBackFact>>());
        var sourceIndex = ReturnToSenderSourceIndex.TryCreate([sourcePath]);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable,
            allowUnsafe: true);

        return ReturnToSender.TryIsolateRecompileFailure(
            request,
            sourceIndex,
            parseOptions,
            compileOptions,
            RoslynTestReferences.TrustedPlatform.ToArray());
    }

    static (TypeDefinitionHandle Type, MethodDefinitionHandle Method) FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (!string.Equals(reader.GetFullTypeName(type), typeName, StringComparison.Ordinal))
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (string.Equals(reader.GetString(method.Name), methodName, StringComparison.Ordinal))
                    return (typeHandle, methodHandle);
            }
        }

        throw new InvalidOperationException($"Could not find {typeName}::{methodName}.");
    }

    [Fact]
    public void CompileBackTargets_EmitsNestedTargetMemberRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public class Inner
                {
                    public int FromSibling() => GetValue();
                    public int GetValue() => 42;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1.Inner", "FromSibling", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var inner = Assert.Single(result.Plan.Types, type => type.Name == "Inner");
            Assert.Contains(inner.Members, member => member.Name == "GetValue"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "GetValue"));
            Assert.DoesNotContain(inner.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains("public int GetValue()", result.Source);
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
    public void CSharpTypePrinter_PrimaryConstructorParametersPrecedeGenericConstraints()
    {
        var type = new ApiType
        {
            Name = "Class1`1",
            MetadataName = "Class1`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T", Constraints = ["class"] }],
        };
        var result = new CSharpTypePrinter().Print(new CSharpTypePrintRequest(
            type,
            primaryConstructorParameters:
            [
                new ApiParameter { Type = "string", Name = "message" }
            ]));

        Assert.Contains("public class Class1<T>(string message) where T : class", Assert.Single(result.Units).Source);
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
    public void CompileBackTargets_RendersRefReadonlyReturnShell()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public static ref readonly int SelectReadonlyRef(bool useLeft, in int left, in int right)
                {
                    if (useLeft)
                    {
                        return ref left;
                    }

                    return ref right;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "SelectReadonlyRef", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public static ref readonly int SelectReadonlyRef(bool useLeft, in int left, in int right)", result.Source);
            Assert.DoesNotContain("ref @readonly", result.Source);
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
    public void CompileBackTargets_OverloadedIndexerSetterComparesSingleShellAccessor()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                private readonly int[] _values;
                private readonly string[] _names;

                public Class1()
                {
                    _values = new int[4];
                    _names = new string[4];
                }

                public int this[int index]
                {
                    get => _values[index];
                    set => _values[index] = value;
                }

                public string this[string key]
                {
                    get => _names[key.Length];
                    set => _names[key.Length] = value;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "set_Item", 1)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal(1, result.Plan.TargetMethod.Overload);
            Assert.Contains("public string this[string key]", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsGenericMethodSignatures()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public T Echo<T>(T value) => value;

                public T Choose<T>(T left, T right) where T : class => left;

                public T Create<T>() where T : new() => new T();

                public T DefaultValue<T>() where T : struct => default;

                public T Comparable<T>(T value) where T : System.IComparable<T> => value;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", "Echo", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Choose", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Create", 0),
                    new ReturnToSender.RequestedTarget("Class1", "DefaultValue", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Comparable", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public T Echo<T>(T value)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public T Choose<T>(T left, T right) where T : class", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public T Create<T>() where T : new()", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public T DefaultValue<T>() where T : struct", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public T Comparable<T>(T value) where T : IComparable<T>", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsParameterModifierSignatures()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public void Increment(ref int value)
                {
                    value++;
                }

                public void Set(out int value)
                {
                    value = 42;
                }

                public int Read(in int value) => value;

                public int Sum(params int[] values)
                {
                    var sum = 0;
                    foreach (var value in values)
                        sum += value;
                    return sum;
                }
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", "Increment", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Set", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Read", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Sum", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public void Increment(ref int value)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public void Set(out int value)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public int Read(in int value)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public int Sum(params int[] values)", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsDefaultParameterSignatures()
    {
        var assemblyPath = CompileFixture("""
            public enum Choice
            {
                None = 0,
                One = 1,
            }

            public class Class1
            {
                public int Add(int value = 42, bool enabled = true)
                    => enabled ? value + 1 : value;

                public string Format(string text = "hello", object value = null)
                    => value is null ? text : text + value.ToString();

                public decimal DecimalDefault(decimal value = 1.25m)
                    => value;

                public int EnumDefault(Choice choice = Choice.One)
                    => (int)choice;

                public long DateTimeDefault(
                    [System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when)
                    => when.Ticks;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", "Add", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Format", 0),
                    new ReturnToSender.RequestedTarget("Class1", "DecimalDefault", 0),
                    new ReturnToSender.RequestedTarget("Class1", "EnumDefault", 0),
                    new ReturnToSender.RequestedTarget("Class1", "DateTimeDefault", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public int Add(int value = 42, bool enabled = true)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public string Format(string text = \"hello\", object value = null)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public decimal DecimalDefault(decimal value = 1.25m)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public int EnumDefault(Choice choice = (Choice)1)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("DateTimeConstant(637000000000000000L)", result.Source);
                    Assert.Contains("DateTime when", result.Source);
                    Assert.DoesNotContain("DateTime when =", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsParameterAttributes()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Length([System.Diagnostics.CodeAnalysis.NotNull] string value)
                    => value.Length;

                public int Copy([System.Runtime.InteropServices.Out] int value)
                    => value;

                public int Update([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] ref int value)
                {
                    value++;
                    return value;
                }
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", "Length", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Copy", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Update", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[NotNull] string value", result.Source);
                    Assert.Contains("int Length(", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("int Copy(", result.Source);
                    Assert.DoesNotContain("out int value", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("ref int value", result.Source);
                    Assert.DoesNotContain("out int value", result.Source);
                    Assert.DoesNotContain("in int value", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsParameterMarshalling()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int I4([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] int value)
                    => value;

                public int LpStr([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string value)
                    => value.Length;

                public int Bool([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool value)
                    => value ? 1 : 0;

                public int Sum(
                    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeParamIndex = 1)] int[] values,
                    int count)
                {
                    var sum = 0;
                    for (var i = 0; i < count; i++)
                        sum += values[i];
                    return sum;
                }

                public int FirstFour(
                    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeConst = 4)] int[] values)
                    => values[0] + values[1] + values[2] + values[3];

                public int PlainArray(
                    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] int[] values)
                    => values.Length;

                public int FixedArray(
                    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 4)] int[] values)
                    => values[0] + values[1] + values[2] + values[3];

                public int ZeroSizedArray(
                    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 0)] int[] values)
                    => values.Length;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", "I4", 0),
                    new ReturnToSender.RequestedTarget("Class1", "LpStr", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Bool", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Sum", 0),
                    new ReturnToSender.RequestedTarget("Class1", "FirstFour", 0),
                    new ReturnToSender.RequestedTarget("Class1", "PlainArray", 0),
                    new ReturnToSender.RequestedTarget("Class1", "FixedArray", 0),
                    new ReturnToSender.RequestedTarget("Class1", "ZeroSizedArray", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)", result.Source);
                    Assert.DoesNotContain("System.Runtime.InteropServices.MarshalAs(", result.Source);
                    Assert.Contains("using System.Runtime.InteropServices;", result.Source);
                    Assert.DoesNotContain("using System.Runtime.InteropServices.UnmanagedType;", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeParamIndex = 1)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeConst = 4)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 4)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 0)", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsReturnParameterMetadata()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
                public int I4()
                    => 42;

                [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
                public string Text()
                    => "hello";

                [return: System.Diagnostics.CodeAnalysis.NotNull]
                public string NotNullText()
                    => "hello";

                [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
                public int FallbackSignature(
                    [System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when)
                    => when.Year;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", "I4", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Text", 0),
                    new ReturnToSender.RequestedTarget("Class1", "NotNullText", 0),
                    new ReturnToSender.RequestedTarget("Class1", "FallbackSignature", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[return: System.Diagnostics.CodeAnalysis.NotNull]", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]", result.Source);
                    Assert.Contains("[Optional, DateTimeConstant(637000000000000000L)] DateTime when", result.Source);
                    Assert.DoesNotContain("public [return:", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsPropertyReturnMetadata()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public string Text
                {
                    [return: System.Diagnostics.CodeAnalysis.NotNull]
                    get => "hello";
                }

                public int Number
                {
                    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
                    get => 42;
                }

                public string this[int index]
                {
                    [return: System.Diagnostics.CodeAnalysis.NotNull]
                    get => "hello";
                }
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", "get_Text", 0),
                    new ReturnToSender.RequestedTarget("Class1", "get_Number", 0),
                    new ReturnToSender.RequestedTarget("Class1", "get_Item", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[return: System.Diagnostics.CodeAnalysis.NotNull] get", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] get", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("this[int index]", result.Source);
                    Assert.Contains("[return: System.Diagnostics.CodeAnalysis.NotNull] get", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsMemberAttributes()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public Class1()
                {
                }

                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public int Method1()
                    => 42;

                [System.Obsolete("use Method1")]
                public int ObsoleteMethod()
                    => 43;

                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public string Text
                {
                    get => "hello";
                }

                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public int Value
                {
                    get;
                    set;
                }
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", ".ctor", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Method1", 0),
                    new ReturnToSender.RequestedTarget("Class1", "ObsoleteMethod", 0),
                    new ReturnToSender.RequestedTarget("Class1", "get_Text", 0),
                    new ReturnToSender.RequestedTarget("Class1", "set_Value", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.DoesNotContain("ExcludeFromCodeCoverage", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\n    public int Method1()", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Obsolete(\"use Method1\")]\n    public int ObsoleteMethod()", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\n    public string Text", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.DoesNotContain("ExcludeFromCodeCoverage", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsTypeAttributes()
    {
        var assemblyPath = CompileFixture("""
            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public class Class1
            {
                public int Method1()
                    => 42;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", ".ctor", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Method1", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic class Class1", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic class Class1", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesAbstractClosureTypeAndMethod()
    {
        var assemblyPath = CompileFixture("""
            public abstract class Node
            {
                private readonly System.Collections.Generic.List<Node> _children = new();

                public Node Parent { get; set; }

                public int ChildIndex { get; set; }

                public abstract string Describe();

                public void CheckInvariant()
                {
                    for (int i = 0; i < _children.Count; i++)
                    {
                        var child = _children[i];
                        if (child.Parent != this)
                            throw new System.InvalidOperationException($"child {child.Describe()} of {Describe()}");
                        if (child.ChildIndex != i)
                            throw new System.InvalidOperationException($"child {child.Describe()} slot {child.ChildIndex} expected {i}");
                    }
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Node", "CheckInvariant", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public abstract class Node", result.Source);
            Assert.Contains("public abstract string Describe();", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DoesNotMarkFinalNewSlotStructMethodVirtual()
    {
        var assemblyPath = CompileFixture("""
            public interface IThing
            {
                int GetValue();
            }

            public struct Thing : IThing
            {
                public int GetValue() => 42;
            }

            public class Class1
            {
                public int UseThing()
                {
                    var thing = new Thing();
                    return thing.GetValue();
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "UseThing", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public struct Thing", result.Source);
            Assert.Contains("public int GetValue()", result.Source);
            Assert.DoesNotContain("virtual int GetValue", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DoesNotMarkUserBaseOverrideWithoutBaseShell()
    {
        var assemblyPath = CompileFixture("""
            public abstract class BaseNode
            {
                public abstract string Describe();
            }

            public class DerivedNode : BaseNode
            {
                public override string Describe() => "derived";
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("DerivedNode", "Describe", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public string Describe()", result.Source);
            Assert.DoesNotContain("override string Describe", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_EmitsEqualityOperatorPairSibling()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name);
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "op_Equality", 0),
                    new ReturnToSender.RequestedTarget("Row", "op_Inequality", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("operator ==(", result.Source);
                    Assert.Contains("operator !=(", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("operator ==(", result.Source);
                    Assert.Contains("operator !=(", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesRecordGeneratedVirtualHelperShells()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value);
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "ToString", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 0),
                ]);

            Assert.Collection(
                results,
                toString =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, toString.Status);
                    Assert.Contains("protected virtual bool PrintMembers", toString.Source);
                    Assert.Contains("protected virtual Type EqualityContract", toString.Source);
                },
                equalsObject =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, equalsObject.Status);
                    Assert.Contains("public virtual bool Equals(Row other)", equalsObject.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RecordSurfaceHelperKeepsGenericDependency()
    {
        // A record with a custom ToString that calls a generic same-type helper: the record
        // surface path reconstructs the faithful member surface, but the metadata surface
        // enumeration skips generic methods — so the IR-gathered generic dependency must still
        // be carried (via AddRequiredMembers) or compile-back regresses to a non-Exact floor.
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value)
            {
                public override string ToString() => Render<int>();

                private string Render<T>() => Name + Value;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "ToString", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("Render", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RecordSurfaceHelperKeepsGenericSameNameOverload()
    {
        // A user generic `PrintMembers<T>` overload called by a custom ToString must survive
        // the record-surface stub removal, and the synthesized `PrintMembers(StringBuilder)`
        // it also calls must still be present: the removal must not leave the synthesized shape
        // unre-emitted when a same-name overload shadows it in the name-based surface dedup.
        var assemblyPath = CompileFixture("""
            using System.Text;
            public record Row(string Name)
            {
                public override string ToString()
                {
                    var b = new StringBuilder();
                    PrintMembers<int>(7);
                    PrintMembers(b);
                    return b.ToString();
                }

                public bool PrintMembers<T>(int x) => x == 7;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "ToString", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("PrintMembers<T>", result.Source);
            Assert.Contains("PrintMembers(StringBuilder", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesFieldShellForRecordGeneratedFieldReadHelpers()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value);
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "GetHashCode", 0),
                    new ReturnToSender.RequestedTarget("Row", "ToString", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 1),
                ]);

            Assert.Collection(
                results,
                getHashCode =>
                {
                    Assert.True(
                        getHashCode.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{getHashCode.Status}: {getHashCode.Detail}{Environment.NewLine}{getHashCode.Source}");
                    Assert.Contains("public string Name;", getHashCode.Source);
                    Assert.Contains("public string Value;", getHashCode.Source);
                    Assert.DoesNotContain("public string Name { get; }", getHashCode.Source);
                },
                toString =>
                {
                    Assert.True(
                        toString.Status == FidelityCheck.CompileBackStatus.Exact,
                        $"{toString.Status}: {toString.Detail}{Environment.NewLine}{toString.Source}");
                    Assert.Contains("public string Name { get; set; }", toString.Source);
                    Assert.DoesNotContain("public string Name;", toString.Source);
                },
                typedEquals =>
                {
                    Assert.True(
                        typedEquals.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{typedEquals.Status}: {typedEquals.Detail}{Environment.NewLine}{typedEquals.Source}");
                    Assert.Contains("public virtual bool Equals(Row other)", typedEquals.Source);
                    Assert.Contains("public string Name;", typedEquals.Source);
                    Assert.Contains("public string Value;", typedEquals.Source);
                    Assert.DoesNotContain("public string Name { get; }", typedEquals.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesRecordEqualityContractShellForFieldReadHelpers()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value);
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "GetHashCode", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 1),
                ]);

            Assert.Collection(
                results,
                getHashCode => AssertRecordEqualityContractRequirement(getHashCode),
                typedEquals => AssertRecordEqualityContractRequirement(typedEquals));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }

        static void AssertRecordEqualityContractRequirement(ReturnToSender.Result result)
        {
            Assert.Equal(FidelityCheck.CompileBackStatus.OperandDiff, result.Status);
            Assert.NotNull(result.FidelityDiff);
            Assert.False(result.FidelityDiff.IsExact);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(type.Members, member =>
                member.Name == "EqualityContract"
                && member.SourceFacts.Any(fact => fact.Id == "record-equality-contract" && fact.Detail == "get_EqualityContract"));
            Assert.Contains("EqualityContract", result.Source);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesIncrementConsumedOperatorEvidence()
    {
        var assemblyPath = CompileFixture("""
            public struct Counter
            {
                public int Value;

                public Counter(int value)
                {
                    Value = value;
                }

                public static Counter operator ++(Counter value) => new Counter(value.Value + 1);
            }

            public class Class1
            {
                public Counter Method1(Counter counter)
                {
                    counter++;
                    return counter;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Method1", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var counter = Assert.Single(result.Plan.Types, type => type.Name == "Counter");
            Assert.Contains(counter.Members, member =>
                member.Name == "op_Increment"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "op_Increment"));
            Assert.Contains("operator ++", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesCheckedBinaryOperatorSibling()
    {
        var assemblyPath = CompileFixture("""
            public struct CustomNumber
            {
                public int Value;

                public CustomNumber(int value)
                {
                    Value = value;
                }

                public static CustomNumber operator +(CustomNumber left, CustomNumber right) => new CustomNumber(left.Value + right.Value);
                public static CustomNumber operator checked +(CustomNumber left, CustomNumber right) => new CustomNumber(checked(left.Value + right.Value));
            }

            public class Class1
            {
                public CustomNumber Method1(CustomNumber left, CustomNumber right) => checked(left + right);
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Method1", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var number = Assert.Single(result.Plan.Types, type => type.Name == "CustomNumber");
            Assert.Contains(number.Members, member =>
                member.Name == "op_CheckedAddition"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "op_CheckedAddition"));
            Assert.Contains(number.Members, member =>
                member.Name == "op_Addition"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "op_Addition"));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_UsesFieldShellForRecordStructGeneratedFieldReadHelpers()
    {
        var assemblyPath = CompileFixture("""
            public record struct Row(string Name, string Value);
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "GetHashCode", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 1),
                ]);

            Assert.Collection(
                results,
                getHashCode =>
                {
                    Assert.True(
                        getHashCode.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{getHashCode.Status}: {getHashCode.Detail}{Environment.NewLine}{getHashCode.Source}");
                    Assert.Contains("public string Name;", getHashCode.Source);
                    Assert.Contains("public string Value;", getHashCode.Source);
                    Assert.DoesNotContain("public string Name { get; }", getHashCode.Source);
                },
                typedEquals =>
                {
                    Assert.True(
                        typedEquals.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{typedEquals.Status}: {typedEquals.Detail}{Environment.NewLine}{typedEquals.Source}");
                    Assert.Contains("public bool Equals(Row other)", typedEquals.Source);
                    Assert.Contains("public string Name;", typedEquals.Source);
                    Assert.Contains("public string Value;", typedEquals.Source);
                    Assert.DoesNotContain("public string Name { get; }", typedEquals.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackSelfType_DistinguishesNestedTypeFromNamespacePeer()
    {
        var assemblyPath = CompileFixture("""
            namespace N;

            public class Container
            {
                public record Row(string Name);
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            using var source = MetadataSource.Open(assemblyPath);
            var reader = pe.GetMetadataReader();
            var nestedHandle = reader.TypeDefinitions
                .Single(handle => TypeResolver.GetFullName(reader, reader.GetTypeDefinition(handle)) == "N.Container.Row");
            var nestedType = reader.GetTypeDefinition(nestedHandle);
            var nestedIdentity = CompileBackTypeIdentity.FromDefinition(reader, nestedType);
            var namespacePeerIdentity = new CompileBackTypeIdentity(
                "N.Container",
                "Row",
                "Row",
                "N.Container.Row",
                "N.Container.Row");
            var function = IrImporter.Import(source, "N.Container.Row", "GetHashCode", publicOnly: false);
            Assert.NotNull(function);
            IrPasses.Run(function, IrPasses.Default, new PassContext(new Stepper(enabled: false)));
            var load = Assert.Single(function.Descendants.OfType<LoadField>(), field => field.Field.BackingPropertyName == "Name");
            var helper = typeof(CompileBackSourceComposer).GetMethod("IsSelfType", BindingFlags.Static | BindingFlags.NonPublic)!;

            Assert.True((bool)helper.Invoke(null, [load.Field.DeclaringType, nestedIdentity])!);
            Assert.False((bool)helper.Invoke(null, [load.Field.DeclaringType, namespacePeerIdentity])!);

            var globalNestedIdentity = new CompileBackTypeIdentity("", "B", "B", "A.B", "A.B");
            var dottedTopLevelType = TypeRef.Definition("fixture", "", "A.B");
            Assert.False((bool)helper.Invoke(null, [dottedTopLevelType, globalNestedIdentity])!);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesGenericRecordTypedEqualsShell()
    {
        var assemblyPath = CompileFixture("""
            public record Row<T>(T Value);
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row`1", "Equals", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public virtual bool Equals(Row<T> other)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RendersAbstractClosurePropertiesWithoutBodies()
    {
        var assemblyPath = CompileFixture("""
            public abstract class Row
            {
                public abstract string Name { get; set; }
                public void SetName(string value) => Name = value;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "SetName", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public abstract string Name { get; set; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackSelfTypeSignature_IncludesDeclaringGenericParameters()
    {
        var assemblyPath = CompileFixture("""
            public class Container<T>
            {
                public record Row<U>(T Outer, U Value);
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var typeHandle = reader.TypeDefinitions
                .Single(handle => TypeResolver.GetFullName(reader, reader.GetTypeDefinition(handle)) == "Container`1.Row`1");
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var identity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            var helper = typeof(CompileBackSourceComposer).GetMethod(
                "SelfTypeSignature",
                BindingFlags.Static | BindingFlags.NonPublic);

            var selfType = Assert.IsType<string>(helper!.Invoke(null, [reader, typeDef, identity]));

            Assert.Equal("Container<T>.Row<U>", selfType);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_EmitsSignatureMatchedEqualityOperatorPairSibling()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(Row left, Row right) => true;
                public static bool operator !=(Row left, Row right) => false;
                public static bool operator ==(Row left, string right) => right == "x";
                public static bool operator !=(Row left, string right) => !(left == right);
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 1)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("operator ==(Row left, string right)", result.Source);
            Assert.Contains("operator !=(Row left, string right)", result.Source);
            Assert.DoesNotContain("operator !=(Row left, Row right)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_EmitsNoBodyEqualityOperatorPairSibling()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(Row left, Row right) => true;
                [System.Runtime.InteropServices.DllImport("native")]
                public static extern bool operator !=(Row left, Row right);
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("operator ==(Row left, Row right)", result.Source);
            Assert.Contains("operator !=(Row left, Row right)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesGenericEqualityOperatorReferenceComparison()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row<T>
            {
                public static bool operator ==(Row<T> left, Row<T> right) => (object)left == (object)right;
                public static bool operator !=(Row<T> left, Row<T> right) => (object)left != (object)right;
                public override bool Equals(object obj) => obj is Row<T>;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row`1", "op_Equality", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("return (object)left == (object)right;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesInParameterEqualityOperatorReferenceComparison()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(in Row left, in Row right) => (object)left == (object)right;
                public static bool operator !=(in Row left, in Row right) => (object)left != (object)right;
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("return (object)(left) == (object)(right);", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesSpilledLocalEqualityOperatorReferenceComparison()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(Row left, Row right)
                {
                    Row V_0 = right;
                    Row S_256 = left;
                    System.Console.WriteLine(S_256 is null);
                    return (object)S_256 == (object)V_0;
                }

                public static bool operator !=(Row left, Row right) => (object)left != (object)right;
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("return (object)S_256 == (object)V_0;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsGenericTypeTargets()
    {
        var assemblyPath = CompileFixture("""
            public class Box<T>
            {
                private T _value;

                public Box(T value)
                {
                    _value = value;
                }

                public T Value
                {
                    get => _value;
                    set => _value = value;
                }

                public T Echo(T value) => value;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Box`1", ".ctor", 0),
                    new ReturnToSender.RequestedTarget("Box`1", "get_Value", 0),
                    new ReturnToSender.RequestedTarget("Box`1", "set_Value", 0),
                    new ReturnToSender.RequestedTarget("Box`1", "Echo", 0),
                ]);

            Assert.Collection(
                results,
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
            Assert.All(results, result => Assert.Contains("public class Box<T>", result.Source));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void GetTypeParameters_NestedGenericType_SkipsDeclaringTypeParameters()
    {
        var assemblyPath = CompileFixture("""
            public class Outer<T>
            {
                public class Inner<U>
                {
                }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var nested = reader.TypeDefinitions
                .Select(handle => reader.GetTypeDefinition(handle))
                .Single(type => reader.GetString(type.Name).StartsWith("Inner", StringComparison.Ordinal));

            var typeParameters = MetadataDeclarationQuery.GetTypeParameters(reader, nested);
            var typeParameter = Assert.Single(typeParameters);
            Assert.Equal("U", typeParameter.Name);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void GetTypeParameters_PreservesDelegateVariance()
    {
        var assemblyPath = CompileFixture("""
            public delegate void Handler<in T>(T value);
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var type = reader.TypeDefinitions
                .Select(handle => reader.GetTypeDefinition(handle))
                .Single(type => reader.GetString(type.Name).StartsWith("Handler", StringComparison.Ordinal));

            var typeParameters = MetadataDeclarationQuery.GetTypeParameters(reader, type);
            var typeParameter = Assert.Single(typeParameters);
            Assert.Equal("T", typeParameter.Name);
            Assert.Equal("in", typeParameter.Variance);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsStructPropertyTargets()
    {
        var assemblyPath = CompileFixture("""
            public struct Counter
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    set => _value = value;
                }

                public int Add(int value) => _value + value;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Counter", "get_Value", 0),
                    new ReturnToSender.RequestedTarget("Counter", "set_Value", 0),
                    new ReturnToSender.RequestedTarget("Counter", "Add", 0),
                ]);

            Assert.Collection(
                results,
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
            Assert.All(results, result => Assert.Contains("public struct Counter", result.Source));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_SurfacesStructNonAutoPropertyWithoutBackingField()
    {
        var assemblyPath = CompileFixture("""
            public struct Counter
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    set => _value = value;
                }
            }

            public class Class1
            {
                public int Read(Counter counter) => counter.Value;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Read", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public int Value", result.Source);
            Assert.Contains("throw null", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsStaticClassTargets()
    {
        var assemblyPath = CompileFixture("""
            public static class Class1
            {
                private static int s_value = 42;

                public static int Value => s_value;

                public static int Method1(int value) => value + s_value;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Class1", ".cctor", 0),
                    new ReturnToSender.RequestedTarget("Class1", "get_Value", 0),
                    new ReturnToSender.RequestedTarget("Class1", "Method1", 0),
                ]);

            Assert.Collection(
                results,
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
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

    [Fact]
    public void CompileBackTargets_DoesNotDuplicateSelfRecursiveTargetMethodDuringClosureSurface()
    {
        // Guard for the rts-parity burndown row TypeResolver::GetTypeNameFromReference:
        // a target method that calls itself recursively must not also be reconstructed
        // as a hollow `throw null` closure stub (the self-reference resolves to the
        // target method's own handle). Emitting both the body-backed method and a
        // same-signature stub produced CS0111 (duplicate member) and forced the
        // compile-back floor. A referenced sibling (Combine) must still be stubbed.
        var assemblyPath = CompileFixture("""
            public static class Class1
            {
                public static string Describe(int depth, string name)
                {
                    if (depth > 0)
                        return Describe(depth - 1, name);
                    return Combine(name, name);
                }

                public static string Combine(string left, string right) => left + right;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Describe", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            // The recursive target keeps exactly one declaration (body-backed, not a
            // hollow stub); the referenced sibling is the only `throw null` member.
            Assert.Equal(1, result.Source.Split("string Describe(").Length - 1);
            Assert.DoesNotContain("string Describe(int depth, string name) { throw null; }", result.Source);
            Assert.Contains("public static string Combine(string left, string right) { throw null; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PopulatesEnumMembersWhenTargetReferencesThemByName()
    {
        // A target method that returns a nested enum and references several of its
        // members by name forces the enum to be reconstructed as a closure supporting
        // type. A member-less `enum { }` shell cannot bind those references (CS0117)
        // and drops the row to the compile-back floor; the reconstructed enum surface
        // must carry its named members with their constant values.
        var assemblyPath = CompileFixture("""
            public class Host
            {
                public enum Kind { Unknown, First, Second }

                public static Kind Classify(int value)
                {
                    if (value == 1)
                        return Kind.First;
                    if (value == 2)
                        return Kind.Second;
                    return Kind.Unknown;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Host", "Classify", 0)]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.NotNull(result.Source);
            Assert.Contains("enum Kind", result.Source);
            Assert.Contains("Unknown = 0", result.Source);
            Assert.Contains("First = 1", result.Source);
            Assert.Contains("Second = 2", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ReconstructsNonIntEnumUnderlyingTypeForMemberValues()
    {
        // A reconstructed enum that names members whose constant values do not fit
        // `int` (long/ulong/uint, negative, or byte-backed) must reproduce the enum's
        // underlying type. Otherwise the shell defaults to `int` and the emitted
        // members fail to bind (CS0266), dropping the row to the compile-back floor.
        var assemblyPath = CompileFixture("""
            public class Host
            {
                public enum ELong : long { A = 0, B = 2147483648L, C = -1L }
                public enum EULong : ulong { None = 0, All = 18446744073709551615UL }
                public enum EUInt : uint { Z = 0, Top = 2147483648 }
                public enum EByte : byte { Lo = 0, Hi = 255 }

                public static ELong GetL(long v) => v == 0 ? ELong.A : (v == 1 ? ELong.B : ELong.C);
                public static EULong GetUL(int v) => v == 0 ? EULong.None : EULong.All;
                public static EUInt GetUI(int v) => v == 0 ? EUInt.Z : EUInt.Top;
                public static EByte GetB(int v) => v == 0 ? EByte.Lo : EByte.Hi;
            }
            """);
        try
        {
            foreach (var method in new[] { "GetL", "GetUL", "GetUI", "GetB" })
            {
                var result = Assert.Single(ReturnToSender.CompileBackTargets(
                    assemblyPath,
                    [new ReturnToSender.RequestedTarget("Host", method, 0)]));

                Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
                Assert.False(result.UsedCompileBackFloor, $"{method}: {result.Detail}");
            }
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
        var references = RoslynTestReferences.TrustedPlatform
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
