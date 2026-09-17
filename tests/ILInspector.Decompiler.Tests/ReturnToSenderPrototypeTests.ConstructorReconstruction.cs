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
    public async Task CompileBackTargets_DropsInitializerWhenChainedConstructorUnreconstructable()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ValueBoxChainArgumentIsVisibleOpcodeDiffNotFalseExact()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_PreservesReferenceUpcastChainArgument()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_PreservesConstructorChainWhenArityIsUnique()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_PreservesConstructorChainWhenArgumentIsAssignableNotIdentity()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_PreservesNullLiteralChainArgumentAgainstCrossAritySibling()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ReviewerNullRebindFixtureRoundTripsExact()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_LambdaTargetTypedChainArgumentRemainsHonestNonExact()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ReconstructsBaseClassForCovariantArgument()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_SynthesizesParameterlessConstructorForReconstructedBase()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_DoesNotReconstructExternalBaseClass()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_UsesTargetBackingFieldWriteForConstructorAssignment()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_DoesNotDuplicatePrimaryConstructorAutoPropertyInitializer()
    {
        var assemblyPath = CompileFixture("""
            public class Class1(int value)
            {
                public int Value { get; } = value;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_UsesTargetBackingFieldWriteForStaticConstructorAssignment()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_SeedsTargetInterfaceRoot()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_SeedsTargetInterfaceRootWhenTargetAssemblyNameIsCorelibFacade()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_UsesTypedObjectInitializerPropertyRequirement()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_UsesTypedTargetObjectInitializerPropertyRequirement()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackFirstPropertyGetter_KeepsTypeResolvedCs0117PropertyFallback()
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
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 3))
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
    public async Task CompileBackTargets_KeepsTypeResolvedCs1061PropertyFallback()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackFirstPropertyGetter_EmitsClosureConstructorRequirement()
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
            var result = (await ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2))
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
}
