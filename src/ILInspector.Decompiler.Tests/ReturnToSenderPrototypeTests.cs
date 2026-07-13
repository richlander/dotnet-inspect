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

            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
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
    public void CompileBackFirstPropertyGetter_FallsBackToCompileBackFloorForAttributeShellStall()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public abstract class BaseMarkerAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MarkerAttribute : BaseMarkerAttribute
            {
                public bool Flag => true;
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.True(result.UsedCompileBackFloor, result.Detail);
            Assert.NotNull(result.CompileBackFloor);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.CompileBackFloor.Status);
            Assert.Contains("compile-back-floor", result.Detail);
            Assert.Contains("CS0641", result.Detail);
            Assert.Contains("return true;", result.TargetBody);
            Assert.Contains("return true;", result.Source);
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
            using System;

            public abstract class BaseMarkerAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MarkerAttribute : BaseMarkerAttribute
            {
                public bool Flag => true;
            }
            """);
        try
        {
            var target = new ReturnToSender.RequestedTarget("MarkerAttribute", "get_Flag", 0);
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
    public void CompileBackTargets_DropsInitializerWhenChainedConstructorIsOverloadAmbiguous()
    {
        // Issue #2678 guard: the printer can render chain arguments without a
        // disambiguating cast (a `box` to `object` prints as its inner value), and
        // C# overload resolution — including the target constructor — can then
        // re-bind the call. A boxed argument is never faithful, and here three
        // same-arity constructors (`C(object)`, `C(int)`, the target `C(string)`)
        // share the callee's arity, so unique-arity binding does not hold either.
        // The initializer must be stripped rather than emit a wrong-binding chain.
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

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenChainBindsToTargetConstructor()
    {
        // Issue #2678 guard: overload resolution also considers the target
        // constructor itself. The printer drops the `(object)` cast from
        // `: this((object)text)`, printing `: this(text)`; with the target
        // `C(string)` in the shell, `text` (a string) binds back to the target
        // constructor — `C(string)` calling itself (CS0516). The argument is not
        // faithful (its printed string type differs from the `object` parameter)
        // and the callee shares the target's arity, so the initializer must be
        // stripped.
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

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
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
    public void CompileBackTargets_DropsInitializerWhenCrossArityParamsSiblingCanBind()
    {
        // Issue #2678 guard (Gemini R5): unique *declared* arity is not enough — a
        // cross-arity `params` (or optional) sibling can absorb an N-argument call
        // and, for a lossy argument the printer cannot type precisely, offer a
        // better conversion that steals the bind. Here `C()` chains
        // `: this((object)null)`; the printer drops the `(object)` cast, printing
        // `: this(null)`. `C(object)` is the only one-parameter constructor, but
        // `C(string, params int[])` is also applicable to a one-argument call and
        // `null` binds to `string` (more derived than `object`) via params
        // expansion — the wrong overload. The initializer must be stripped.
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

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenChainArgumentIsAmbiguousLambda()
    {
        // Issue #2678 guard (Gemini R6): a lambda argument prints typeless
        // (`() => ...`) and relies on C# target-typing, so its IR result type
        // matching the parameter does NOT prove an unambiguous bind. Here `C()`
        // chains `: this((Func<int>)(() => 1))`; the shell also contains
        // `C(Expression<Func<int>>)` (pulled in by the body). Both constructors
        // accept `() => 1`, so the printed `: this(() => 1)` is ambiguous (CS0121).
        // The lambda must not be treated as faithful; with a same-arity sibling in
        // the shell, unique-arity does not hold either, so the initializer is
        // stripped and the body (which references the sibling through a typed local,
        // so it stays unambiguous) still compiles.
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

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesConstructorChainWhenArgumentIsArrayCovariantToInterface()
    {
        // Issue #2726: the `(Version, string, string)` ctor chains to
        // `(Version, IEnumerable<string>, string)` passing a `string[]` argument.
        // `string[]` is implicitly convertible to `IEnumerable<string>` by array
        // covariance but is NOT structurally identical to it, so the faithful
        // check (identity only) declines the chain; the two ctors share arity, so
        // the unique-arity check declines too. `string[]` cannot bind to the
        // sibling's second `string` parameter, so the chain binds unambiguously to
        // `(Version, IEnumerable<string>, string)` and must be preserved.
        var assemblyPath = CompileFixture("""
            using System;
            using System.Collections.Generic;

            public class Ver
            {
                public Ver(Version version, string release, string metadata)
                    : this(version, ParseLabels(release), metadata)
                {
                }

                public Ver(Version version, IEnumerable<string> releaseLabels, string metadata)
                {
                    Labels = releaseLabels;
                    Metadata = metadata;
                }

                public IEnumerable<string> Labels { get; }

                public string Metadata { get; }

                private static string[] ParseLabels(string release) => release.Split('.');
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Ver", ".ctor", 0,
                    "(corelib:System.Version, corelib:System.String, corelib:System.String) -> corelib:System.Void")]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenArrayCovariantArgumentAlsoBindsCovariantSibling()
    {
        // Issue #2726 guard: `string[]` is array-covariant to BOTH the chained-to
        // `IReadOnlyCollection<string>` parameter and the same-arity sibling's
        // `IEnumerable<string>` parameter, so the chained-to constructor is not the
        // unique applicable candidate at the array position. The `new C(...)` in the
        // body forces the sibling into the shell (defeating the unique-arity path),
        // so the assignability gate must decide — and must strip rather than emit a
        // chain that could bind to the sibling.
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;

            public class C
            {
                public C(string tag)
                    : this(Parse(tag), tag.Length)
                {
                    _ = new C((IEnumerable<string>)null, 0);
                }

                public C(IReadOnlyCollection<string> labels, int count)
                {
                    Count = count;
                }

                public C(IEnumerable<string> labels, int count)
                {
                    Count = count;
                }

                public int Count { get; }

                private static string[] Parse(string tag) => tag.Split('.');
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenArrayCovariantArgumentAlsoBindsObjectSibling()
    {
        // Issue #2726 guard: `string[]` converts to `object`, so a same-arity
        // sibling taking `object` at the array position cannot be excluded. The
        // covariance model over-approximates convertibility (so exclusion is only
        // ever a proof of non-convertibility); an unprovable competitor strips. The
        // `new C(...)` forces the `object` sibling into the shell so the
        // assignability gate is the decider.
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;

            public class C
            {
                public C(string tag)
                    : this(Parse(tag), tag.Length)
                {
                    _ = new C((object)null, 0);
                }

                public C(IEnumerable<string> labels, int count)
                {
                    Count = count;
                }

                public C(object marker, int count)
                {
                    Count = count;
                }

                public int Count { get; }

                private static string[] Parse(string tag) => tag.Split('.');
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenArrayCovariantTargetHasParamsConstructor()
    {
        // Issue #2726 guard: a `params` constructor absorbs calls of other arities,
        // so a `params string[]` sibling is applicable to the two-argument chain.
        // At the params slot `string[]` binds as the whole array, so the covariance
        // model cannot exclude it (exclusion requires ruling out both element and
        // whole-array binding), and the chain must strip. The `new C("a", "b")`
        // forces the `params` sibling into the shell so the unique-arity path does
        // not decide.
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;

            public class C
            {
                public C(string tag)
                    : this(Parse(tag), tag.Length)
                {
                    _ = new C("a", "b");
                }

                public C(IEnumerable<string> labels, int count)
                {
                    Count = count;
                }

                public C(params string[] parts)
                {
                    Count = parts.Length;
                }

                public int Count { get; }

                private static string[] Parse(string tag) => tag.Split('.');
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenArrayCovariantArgumentAlsoBindsSpanSibling()
    {
        // Issue #2726 guard (adversarial, from PR #2730 review): a library can use
        // [OverloadResolutionPriority(-1)] to keep an IEnumerable<string> overload
        // selected for a string[] argument while ALSO offering a first-class
        // ReadOnlySpan<string> overload. The original chain then binds to the
        // IEnumerable ctor (so the covariance emit path is eligible), but `string[]`
        // implicitly converts to ReadOnlySpan<string> too, and RTS does not re-emit
        // the priority attribute, so a recompiled `: this(...)` would prefer the span
        // ctor. `ArrayCanConvertTo` must NOT prove the span competitor
        // non-convertible; the chain must strip. The `new C(...)` forces the span
        // sibling into the shell so the assignability gate is the decider.
        var assemblyPath = CompileFixture("""
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            public class C
            {
                public C(string tag)
                    : this(Parse(tag), tag.Length)
                {
                    _ = new C(default(ReadOnlySpan<string>), 0);
                }

                public C(IEnumerable<string> labels, int count)
                {
                    Count = count;
                }

                [OverloadResolutionPriority(-1)]
                public C(ReadOnlySpan<string> labels, int count)
                {
                    Count = count;
                }

                public int Count { get; }

                private static string[] Parse(string tag) => tag.Split('.');
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenArrayCovariantArgumentBindsFacadeAssemblyUserConversion()
    {
        // Issue #2726 guard (adversarial, from PR #2730 round-3 review): when the
        // inspected assembly's OWN simple name is a corelib facade (System.Runtime,
        // mscorlib, netstandard, ...), TypeRefDecoder canonicalizes its own
        // TypeDefinitions' assembly to "corelib". Assembly-identity exclusion alone
        // would then wrongly rule out `System.MySink` — a type this assembly DEFINES
        // and which declares an implicit array operator — as if it were corelib.
        // The gate instead never excludes a competitor whose receiving parameter is
        // an inspected TypeDefinition, so the unsound chain strips. Recompile status
        // is environment-dependent for a facade-named shell (see the corelib-facade
        // interface-seed test), so only the strip invariant is asserted.
        var assemblyPath = CompileFixture(
            """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            namespace System
            {
                public class MySink
                {
                    public static implicit operator MySink(string[] value) => new MySink();
                }
            }

            public class C
            {
                public C(string tag)
                    : this(Parse(tag), tag.Length)
                {
                    _ = (System.MySink)Parse(tag);
                    _ = new C(default(System.MySink), 0);
                }

                public C(IEnumerable<string> labels, int count)
                {
                    Count = count;
                }

                [OverloadResolutionPriority(-1)]
                public C(System.MySink sink, int count)
                {
                    Count = count;
                }

                public int Count { get; }

                private static string[] Parse(string tag) => tag.Split('.');
            }
            """,
            assemblyName: "System.Runtime");
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenArrayCovariantArgumentBindsNestedFacadeAssemblyUserConversion()
    {
        // Issue #2726 guard (adversarial, from PR #2730 round-4 review, found
        // independently by both reviewers): a NESTED inspected type has an empty
        // metadata namespace and a simple metadata name, but the decoder shapes its
        // TypeRef with the outermost namespace and a `Declaring+Nested` name
        // (System.Outer+MySink). A flat `namespace + "." + name` inspected-name key
        // would miss it, so in a facade-simple-named assembly the nested user type
        // (canonicalized to corelib) would be wrongly excluded and the chain emitted.
        // InspectedDefinitionKey walks the declaring chain to match the decoder, so
        // the nested competitor stays in play and the chain strips.
        var assemblyPath = CompileFixture(
            """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            namespace System
            {
                public class Outer
                {
                    public class MySink
                    {
                        public static implicit operator MySink(string[] value) => new MySink();
                    }
                }
            }

            public class C
            {
                public C(string tag)
                    : this(Parse(tag), tag.Length)
                {
                    _ = (System.Outer.MySink)Parse(tag);
                    _ = new C(default(System.Outer.MySink), 0);
                }

                public C(IEnumerable<string> labels, int count)
                {
                    Count = count;
                }

                [OverloadResolutionPriority(-1)]
                public C(System.Outer.MySink sink, int count)
                {
                    Count = count;
                }

                public int Count { get; }

                private static string[] Parse(string tag) => tag.Split('.');
            }
            """,
            assemblyName: "System.Runtime");
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            Assert.DoesNotContain(": this(", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_DropsInitializerWhenArrayCovariantArgumentBindsSystemNamespaceUserConversion()
    {
        // Issue #2726 guard (adversarial, from PR #2730 round-2 review): a user type
        // declared in a `System` namespace is NOT corelib and may declare an
        // implicit conversion operator from an array. Excluding a competitor by
        // namespace string would be unsound; corelib membership is decided by
        // assembly identity (TypeRef.Assembly == "corelib"), so `System.MySink`
        // (defined in the target assembly) stays in play and the chain strips. The
        // body forces both the conversion operator and the competitor ctor into the
        // reconstructed shell; [OverloadResolutionPriority(-1)] keeps the original
        // chain bound to the IEnumerable ctor.
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            namespace System
            {
                public class MySink
                {
                    public static implicit operator MySink(string[] value) => new MySink();
                }
            }

            public class C
            {
                public C(string tag)
                    : this(Parse(tag), tag.Length)
                {
                    _ = (System.MySink)Parse(tag);
                    _ = new C(default(System.MySink), 0);
                }

                public C(IEnumerable<string> labels, int count)
                {
                    Count = count;
                }

                [OverloadResolutionPriority(-1)]
                public C(System.MySink sink, int count)
                {
                    Count = count;
                }

                public int Count { get; }

                private static string[] Parse(string tag) => tag.Split('.');
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("C", ".ctor", 0,
                    "(corelib:System.String) -> corelib:System.Void")]));

            // The load-bearing invariant is that the gate STRIPS the unsound chain.
            // This fixture is deliberately pathological (a user type in `namespace
            // System` plus an implicit array operator), which RTS cannot round-trip
            // as a whole shell, so the overall status is not asserted; emitting the
            // initializer would fail or bind wrong, so stripping is the sound choice.
            Assert.DoesNotContain(": this(", result.Source);
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

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
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
                result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
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
                result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
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
                    Assert.Contains("public T Comparable<T>(T value) where T : System.IComparable<T>", result.Source);
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
                    Assert.Contains("public System.Decimal DecimalDefault(System.Decimal value = 1.25m)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("public int EnumDefault(Choice choice = (Choice)1)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)", result.Source);
                    Assert.Contains("System.DateTime when", result.Source);
                    Assert.DoesNotContain("System.DateTime when =", result.Source);
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
                    Assert.Contains("System.Diagnostics.CodeAnalysis.NotNull", result.Source);
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
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeParamIndex = 1)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeConst = 4)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 4)", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 0)", result.Source);
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
                    Assert.Contains("[System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when", result.Source);
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
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public int Method1()", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Obsolete(\"use Method1\")] public int ObsoleteMethod()", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public string Text", result.Source);
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
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public class Class1", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public class Class1", result.Source);
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
                    Assert.Contains("protected virtual System.Type EqualityContract", toString.Source);
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
            Assert.Contains("PrintMembers(System.Text.StringBuilder", result.Source);
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
                        getHashCode.Status == FidelityCheck.CompileBackStatus.Exact,
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
                        typedEquals.Status == FidelityCheck.CompileBackStatus.Exact,
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
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
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
                        getHashCode.Status == FidelityCheck.CompileBackStatus.Exact,
                        $"{getHashCode.Status}: {getHashCode.Detail}{Environment.NewLine}{getHashCode.Source}");
                    Assert.Contains("public string Name;", getHashCode.Source);
                    Assert.Contains("public string Value;", getHashCode.Source);
                    Assert.DoesNotContain("public string Name { get; }", getHashCode.Source);
                },
                typedEquals =>
                {
                    Assert.True(
                        typedEquals.Status == FidelityCheck.CompileBackStatus.Exact,
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
    public void ReturnToSenderTypePlanner_NestedGenericTypeParametersSkipDeclaringTypeParameters()
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
            var method = typeof(CompileBackSourceComposer).GetMethod(
                "TypeParameters",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(MetadataReader), typeof(TypeDefinition)]);

            var typeParameters = Assert.IsAssignableFrom<IReadOnlyList<CompileBackTypeParameter>>(
                method?.Invoke(null, [reader, nested]));
            var typeParameter = Assert.Single(typeParameters);
            Assert.Equal("U", typeParameter.Name);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void ReturnToSenderTypePlanner_TypeParametersPreserveDelegateVariance()
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
            var method = typeof(CompileBackSourceComposer).GetMethod(
                "TypeParameters",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(MetadataReader), typeof(TypeDefinition)]);

            var typeParameters = Assert.IsAssignableFrom<IReadOnlyList<CompileBackTypeParameter>>(
                method?.Invoke(null, [reader, type]));
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
