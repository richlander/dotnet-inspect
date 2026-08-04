using ILInspector.DecompilerHarness;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Instructions;
using DotnetInspector.RoundTripCompilation;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Collection(ConsoleMutatorCollection.Name)]
[Trait("Area", "RoundTrip")]
public class ReturnToSenderPrototypeTests
{
    [Fact]
    public void CompileBackTargets_AllFullReconstructsUnrelatedExplicitInterfaceEventAccessors()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public interface IEvents
            {
                event Action Changed;
            }

            public sealed class EventSource : IEvents
            {
                private Action? _changed;

                event Action IEvents.Changed
                {
                    add { _changed += value; }
                    remove { _changed -= value; }
                }
            }

            public static class Target
            {
                public static int Run() => 42;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Target", "Run", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
            Assert.True(
                result.FullBodies.Count != 0,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            var adder = Assert.Single(result.FullBodies, body => body.Member == "EventSource.add_IEvents.Changed");
            var remover = Assert.Single(result.FullBodies, body => body.Member == "EventSource.remove_IEvents.Changed");
            Assert.Equal(MemberBodyProductionStatus.Complete, adder.Status);
            Assert.Equal(MemberBodyProductionStatus.Complete, remover.Status);
            Assert.Contains("event Action IEvents.Changed", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("public event Action IEvents.Changed", result.Source, StringComparison.Ordinal);
            Assert.Contains("Delegate.Combine", result.Source, StringComparison.Ordinal);
            Assert.Contains("Delegate.Remove", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.NotNull(result.DonorPe);
            Assert.NotNull(result.Comparison);
            Assert.Equal(RoundTripComparisonStatus.Completed, result.Comparison.Status);
            Assert.Contains(result.Comparison.Members, member =>
                member.Target.Method == adder.Method
                && member.CSharpStatus != RoundTripEvidenceStatus.Unavailable
                && member.IlStatus != IlBodyDiffOutcome.Unavailable);
            Assert.Contains(result.Comparison.Members, member =>
                member.Target.Method == remover.Method
                && member.CSharpStatus != RoundTripEvidenceStatus.Unavailable
                && member.IlStatus != IlBodyDiffOutcome.Unavailable);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_AllFullReconstructsUnrelatedEventAccessors()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public static class Target
            {
                public static int Run() => 1;
            }

            public static class Unrelated
            {
                private static Action? _changed;

                public static event Action Changed
                {
                    add { _changed += value; }
                    remove { _changed -= value; }
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Target", "Run", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
            var adder = Assert.Single(result.FullBodies, body => body.Member == "Unrelated.add_Changed");
            var remover = Assert.Single(result.FullBodies, body => body.Member == "Unrelated.remove_Changed");
            Assert.Equal(MemberBodyProductionStatus.Complete, adder.Status);
            Assert.Equal(MemberBodyProductionStatus.Complete, remover.Status);
            Assert.Contains("Delegate.Combine", result.Source, StringComparison.Ordinal);
            Assert.Contains("Delegate.Remove", result.Source, StringComparison.Ordinal);
            Assert.NotNull(result.Comparison);
            Assert.Contains(result.Comparison.Members, member =>
                member.Target.Method == adder.Method && member.CSharpStatus != RoundTripEvidenceStatus.Unavailable);
            Assert.Contains(result.Comparison.Members, member =>
                member.Target.Method == remover.Method && member.CSharpStatus != RoundTripEvidenceStatus.Unavailable);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullRejectsMultiplePrimaryTargets()
    {
        var exception = Assert.Throws<NotSupportedException>(() => ReturnToSender.CompileBackTargets(
            typeof(ReturnToSenderPrototypeTests).Assembly.Location,
            [
                new ReturnToSender.RequestedTarget("One", "M", 0),
                new ReturnToSender.RequestedTarget("Two", "M", 0),
            ],
            RoundTripScope.All,
            RoundTripBodyPolicy.Full));

        Assert.Contains("exactly one primary target", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileBackTargets_AllFullReconstructsEveryConcreteMethodBody()
    {
        var assemblyPath = CompileFixture("""
            public static class Target
            {
                public static int Run() => Unrelated.Value();
            }

            public static class Unrelated
            {
                public static int Value() => 42;
                public static int Twice(int value) => value * 2;
                public static int Doubled => Value() * 2;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Target", "Run", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Equal(RoundTripScope.All, result.Scope);
            Assert.Equal(RoundTripBodyPolicy.Full, result.BodyPolicy);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
            Assert.Collection(
                result.FullBodies.OrderBy(body => body.Member, StringComparer.Ordinal),
                body =>
                {
                    Assert.Equal("Target.Run", body.Member);
                    Assert.Equal(MemberBodyProductionStatus.Complete, body.Status);
                },
                body =>
                {
                    Assert.Equal("Unrelated.Twice", body.Member);
                    Assert.Equal(MemberBodyProductionStatus.Complete, body.Status);
                },
                body =>
                {
                    Assert.Equal("Unrelated.Value", body.Member);
                    Assert.Equal(MemberBodyProductionStatus.Complete, body.Status);
                },
                body =>
                {
                    Assert.Equal("Unrelated.get_Doubled", body.Member);
                    Assert.Equal(MemberBodyProductionStatus.Complete, body.Status);
                });
            Assert.Contains("return Unrelated.Value();", result.Source, StringComparison.Ordinal);
            Assert.Contains("return 42;", result.Source, StringComparison.Ordinal);
            Assert.Contains("return value * 2;", result.Source, StringComparison.Ordinal);
            Assert.Contains("return Value() * 2;", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("throw null", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.NotNull(result.DonorPe);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, result.Status);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.ContextFail, result.Status);
            Assert.NotNull(result.Comparison);
            Assert.Equal(RoundTripComparisonStatus.Completed, result.Comparison.Status);
            Assert.Equal(4, result.Comparison.Members.Length);
            Assert.All(result.Comparison.Members, comparison =>
            {
                Assert.Equal(RoundTripEvidenceStatus.Exact, comparison.CSharpStatus);
                Assert.NotEqual(IlBodyDiffOutcome.Unavailable, comparison.IlStatus);
            });
            Assert.Equal(2, result.Comparison.Members.Count(comparison => comparison.IlStatus == IlBodyDiffOutcome.Exact));
            Assert.Equal(2, result.Comparison.Members.Count(comparison => comparison.IlStatus == IlBodyDiffOutcome.OperandDiff));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_AllFullReportsConcreteDeclarationItCannotRepresent()
    {
        var assemblyPath = CompileFixture("""
            public static class Target
            {
                public static int Run() => 1;
            }

            public static class Unrelated
            {
                public static T Echo<T>(T value) => value;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Target", "Run", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.False(result.BodyComplete);
            var failure = Assert.Single(
                result.FullBodies,
                body => body.Member == "Unrelated.Echo");
            Assert.Equal(MemberBodyProductionStatus.Failed, failure.Status);
            Assert.Contains("not represented", failure.Failure, StringComparison.Ordinal);
            Assert.Contains(
                result.Plan.Diagnostics,
                diagnostic => diagnostic.Reason == "declaration-not-represented"
                              && diagnostic.Detail == "Unrelated.Echo");
            Assert.DoesNotContain("Echo", result.Source, StringComparison.Ordinal);
            Assert.NotNull(result.Comparison);
            Assert.Equal(RoundTripComparisonStatus.Completed, result.Comparison.Status);
            Assert.Equal(2, result.Comparison.Members.Length);
            var unavailable = Assert.Single(
                result.Comparison.Members,
                member => member.Target.Method == failure.Method);
            Assert.Equal(RoundTripEvidenceStatus.Unavailable, unavailable.CSharpStatus);
            Assert.Equal(IlBodyDiffOutcome.Unavailable, unavailable.IlStatus);
            Assert.Contains("not represented", unavailable.CSharpFailure, StringComparison.Ordinal);
            Assert.Contains("not represented", unavailable.IlFailure, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullPreservesConcreteSiblingWhenTargetIsPropertyAccessor()
    {
        // Issue #3000: when the target is a property accessor, the sibling accessor's produced
        // full body was silently dropped (kept a `throw null;` stub) while still reported Complete.
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                private int _v;
                public int Value
                {
                    get => _v;
                    set { _v = value; }
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "get_Value", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            var getter = Assert.Single(result.FullBodies, body => body.Member == "Holder.get_Value");
            var setter = Assert.Single(result.FullBodies, body => body.Member == "Holder.set_Value");
            Assert.Equal(MemberBodyProductionStatus.Complete, getter.Status);
            Assert.Equal(MemberBodyProductionStatus.Complete, setter.Status);

            // Evidence reports the sibling Complete, so the emitted sibling body must be the produced
            // body, not a `throw null;` stub, and the target accessor body must be preserved.
            Assert.Contains("_v = value;", result.Source, StringComparison.Ordinal);
            Assert.Contains("return _v;", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("throw null", result.Source, StringComparison.Ordinal);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));

            Assert.NotNull(result.Comparison);
            Assert.Equal(RoundTripComparisonStatus.Completed, result.Comparison.Status);
            var setterComparison = Assert.Single(
                result.Comparison.Members,
                member => member.Target.Method == setter.Method);
            Assert.Equal(RoundTripEvidenceStatus.Exact, setterComparison.CSharpStatus);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullPreservesAutoPropertyWhenTargetIsAutoAccessor()
    {
        // Issue #3000 regression guard: when the target is an auto-property accessor, the property
        // has no explicit accessor body to preserve. The target-aware branch must leave the base
        // auto-property skeleton intact rather than replacing it with empty accessor bodies (which
        // deletes the accessors -> `int Value {  }`, CS0548, forcing a floor fallback).
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                public int Value { get; } = 42;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "get_Value", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            var getter = Assert.Single(result.FullBodies, body => body.Member == "Holder.get_Value");
            Assert.Equal(MemberBodyProductionStatus.Complete, getter.Status);
            Assert.Contains("Value { get; }", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Value {  }", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullPreservesReadWriteAutoPropertyWhenTargetIsGetter()
    {
        // Issue #3000: a read-write auto-property targeted at its getter was rendered get-only
        // (`{ get; }`), silently dropping the setter while still recording set_Value Complete.
        // The getter compose path must select AutoPropertyGetSet when a setter exists so the
        // preserved skeleton keeps both accessors.
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                public int Value { get; set; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "get_Value", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.get_Value").Status);
            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.set_Value").Status);
            Assert.Contains("Value { get; set; }", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullPreservesInitAccessorWhenTargetIsGetter()
    {
        // Issue #3000: a get/init auto-property targeted at its getter was rendered get-only
        // (`{ get; }`), silently dropping the init setter while still recording set_Value Complete.
        // The getter compose path must render a get/init auto-property under Full so the
        // compiler-synthesized init accessor faithfully reproduces the original setter and stays
        // represented (not flipped to a public `set`, which would lose the init-only shape).
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                public int Value { get; init; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "get_Value", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.get_Value").Status);
            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.set_Value").Status);
            Assert.Contains("Value { get; init; }", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Value { get; set; }", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullPreservesInitAccessorWhenTargetIsSetter()
    {
        // Issue #3000: targeting the init setter itself must render a get/init auto-property, not
        // `{ get; set; }`. Flipping init to a public set loses the init-only shape and produces a
        // setter whose IL diverges from the original init accessor.
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                public int Value { get; init; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "set_Value", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.set_Value").Status);
            Assert.Contains("Value { get; init; }", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Value { get; set; }", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullPreservesExplicitInitAccessorWhenTargetIsGetter()
    {
        // Issue #3000: a non-auto (explicit-body) get/init property targeted at its getter was
        // rendered with a public `set` accessor, silently downgrading the init-only property
        // (dropping the required modreq(IsExternalInit)) while still reporting set_Value Complete.
        // The getter compose path must route the sibling init setter through the init-aware stub
        // kind so the accessor is spelled `init`, preserving the init-only shape.
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                private int _value;
                public int Value
                {
                    get => _value;
                    init => _value = value;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "get_Value", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.get_Value").Status);
            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.set_Value").Status);
            Assert.Contains("init", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("set", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullAutoInitPropertySiblingStaysSkeletonNotRecursive()
    {
        // Issue #3000: under Full, a non-target auto init-property sibling was enriched by
        // decompiling its compiler-synthesized accessors, which read/write the unspeakable
        // backing field. The decompiler renders that as the property itself, producing recursive
        // `get { return this.Value; }` / `init { this.Value = value; }` that compiles but is
        // semantically wrong while the accessors were still reported Complete. The auto-property
        // skeleton must be preserved so the compiler re-synthesizes faithful accessors.
        var assemblyPath = CompileFixture("""
            public readonly struct Holder
            {
                public int Value { get; init; }
                public int M() => 1;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "M", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Contains("public int Value { get; init; }", result.Source);
            Assert.DoesNotContain("k__BackingField", result.Source);
            Assert.DoesNotContain("return this.Value", result.Source);
            Assert.DoesNotContain("this.Value = value", result.Source);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullAutoSetPropertySiblingStaysSkeletonNotRecursive()
    {
        // Issue #3000: the same recursion downgrade affected plain `{ get; set; }` auto-property
        // siblings under Full (this class of bug predates the init work); the skeleton must be
        // preserved so the accessor bodies are not the recursive `return this.Value` shape.
        var assemblyPath = CompileFixture("""
            public struct Holder
            {
                public int Value { get; set; }
                public int M() => 1;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "M", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Contains("public int Value { get; set; }", result.Source);
            Assert.DoesNotContain("k__BackingField", result.Source);
            Assert.DoesNotContain("return this.Value", result.Source);
            Assert.DoesNotContain("this.Value = value", result.Source);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullSuppressesStrayAutoPropertyBackingField()
    {
        // Issue #3036: when a type is pulled onto the RTS Full member surface and one of its
        // members is a compiler-synthesized auto-property, the reconstruction preserved the
        // auto-property skeleton but *also* emitted the raw `<Value>k__BackingField` (sanitized to
        // `__Value_k__BackingField`) as a separate stray field. The compiler re-synthesizes the
        // backing field for the auto-property, so the raw field must be suppressed to avoid a
        // duplicate.
        var assemblyPath = CompileFixture("""
            public struct Holder
            {
                public int Value { get; init; }
                public int M() => 1;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "M", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Contains("public int Value { get; init; }", result.Source);
            Assert.DoesNotContain("k__BackingField", result.Source);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.Members, member => member.Name.Contains("k__BackingField", StringComparison.Ordinal));
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullPreservesExplicitInitAccessorWhenTargetIsSetter()
    {
        // Issue #3000: targeting a non-auto (explicit-body) init setter itself must render an
        // `init` accessor, not a public `set`. Flipping init to set loses the init-only shape.
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                private int _value;
                public int Value
                {
                    get => _value;
                    init => _value = value;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "set_Value", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Equal(MemberBodyProductionStatus.Complete, Assert.Single(result.FullBodies, body => body.Member == "Holder.set_Value").Status);
            Assert.Contains("init", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("set", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullMemberSurfacePreservesSiblingInitAccessor()
    {
        // Issue #3000: targeting a plain method under Full adds the whole declaring type to the
        // member surface, so its sibling explicit-body init property flows through the surface
        // stub-selection path. That path hardcoded `set` (ThrowGetSet / AutoPropertyGetSet),
        // silently downgrading the init-only setter to a public `set` while Enrich filled the real
        // body and reported it Complete. The surface path must route init setters through the
        // init-aware stub kind so the accessor is spelled `init`.
        var assemblyPath = CompileFixture("""
            public class Holder
            {
                private int _value;
                public int Value
                {
                    get => _value;
                    init => _value = value;
                }

                public int M() => 1;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "M", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.Contains("init", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("set", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RecordSurfacePreservesSiblingInitAccessor()
    {
        // Issue #3000: targeting a record's compiler ToString pulls the whole record onto the
        // member surface, so a sibling auto init-property flows through the surface stub path.
        // Before the fix that path emitted `{ get; set; }`, silently dropping the init-only shape
        // while reporting BodyComplete. The surface path must spell the auto init accessor `init`.
        var assemblyPath = CompileFixture("""
            public record Holder(int A)
            {
                public int Value { get; init; }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "ToString", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Selected));

            Assert.Contains("init", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_InterfaceSurfacePreservesInitAccessor()
    {
        // Issue #3000: pulling an interface dependency onto the surface routed its `init` property
        // through the no-body (interface) stub branch, which emitted `{ get; set; }` and stripped
        // the init-only shape. The no-body branch must also honor init and render `{ get; init; }`.
        var assemblyPath = CompileFixture("""
            public interface IHolder
            {
                int Value { get; init; }
            }
            public class Holder : IHolder
            {
                public int Value { get; init; }
                public void Method(IHolder holder)
                {
                    var x = holder.Value;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "Method", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Selected));

            Assert.Contains("init", result.Source, StringComparison.Ordinal);
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullReconstructsPlainEventAccessorTarget()
    {
        // Issue #3007 (follow-up to #3000/#3008): a plain (non-explicit-interface) event accessor
        // target under Full policy reconstructs a coherent single `event { add remove }` carrying
        // both real accessor bodies, rather than a standalone accessor method that collides
        // (CS0082) with the re-declared event's compiler-synthesized accessor. Routing the plain
        // accessor through ComposeEventAccessor with the full member surface folds the sibling
        // accessor into the event and represents the constructor, so every concrete declaration is
        // accounted for and BodyComplete is honestly true.
        var assemblyPath = CompileFixture("""
            using System;

            public class Holder
            {
                private Action? _changed;
                public event Action Changed
                {
                    add { _changed += value; }
                    remove { _changed -= value; }
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Holder", "add_Changed", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.True(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
            Assert.False(result.UsedCompileBackFloor, result.Detail);

            var adder = Assert.Single(result.FullBodies, body => body.Member == "Holder.add_Changed");
            var remover = Assert.Single(result.FullBodies, body => body.Member == "Holder.remove_Changed");
            Assert.Equal(MemberBodyProductionStatus.Complete, adder.Status);
            Assert.Equal(MemberBodyProductionStatus.Complete, remover.Status);
            // The parameterless constructor is a concrete declaration on the target type; the full
            // member surface represents it so it is not flagged unrepresented (which would drop
            // BodyComplete back to the honest-floor state that preceded issue #3007).
            Assert.Contains(
                result.FullBodies,
                body => body.Member == "Holder..ctor" && body.Status == MemberBodyProductionStatus.Complete);

            // A single coherent event declaration with both real bodies and no standalone accessor
            // method (the standalone method + re-declared event is exactly the CS0082 shape #3007
            // eliminates).
            Assert.Contains("public event Action Changed", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("void add_Changed", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("void remove_Changed", result.Source, StringComparison.Ordinal);
            Assert.Contains("Delegate.Combine", result.Source, StringComparison.Ordinal);
            Assert.Contains("Delegate.Remove", result.Source, StringComparison.Ordinal);

            Assert.NotNull(result.DonorPe);
            Assert.NotNull(result.Comparison);
            Assert.Equal(RoundTripComparisonStatus.Completed, result.Comparison.Status);
            Assert.Contains(result.Comparison.Members, member =>
                member.Target.Method == adder.Method
                && member.CSharpStatus != RoundTripEvidenceStatus.Unavailable
                && member.IlStatus != IlBodyDiffOutcome.Unavailable);
            Assert.Contains(result.Comparison.Members, member =>
                member.Target.Method == remover.Method
                && member.CSharpStatus != RoundTripEvidenceStatus.Unavailable
                && member.IlStatus != IlBodyDiffOutcome.Unavailable);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_AllSeedsEverySupportedTopLevelRoot()
    {
        var assemblyPath = CompileFixture("""
            public static class Target
            {
                public static int Run() => 1;
            }

            public static class Unrelated
            {
                public static int Value() => 2;
            }

            public delegate void UnsupportedDelegate();
            """);
        try
        {
            var target = new ReturnToSender.RequestedTarget("Target", "Run", 0);
            var pair = ReturnToSender.CompileBackScopes(assemblyPath, target);
            var cluster = pair.Cluster;
            var all = pair.All;

            Assert.DoesNotContain("class Unrelated", cluster.Source);
            Assert.Contains("class Unrelated", all.Source);
            Assert.True(all.Plan.Types.Count > cluster.Plan.Types.Count);
            Assert.Equal(RoundTripScope.Cluster, cluster.Scope);
            Assert.Equal(RoundTripScope.All, all.Scope);
            Assert.True(cluster.DeclarationComplete);
            Assert.False(all.DeclarationComplete);
            Assert.Contains("UnsupportedDelegate", all.UnsupportedDeclarations);
            Assert.False(cluster.UsedCompileBackFloor, cluster.Detail);
            Assert.False(all.UsedCompileBackFloor, all.Detail);
            Assert.NotNull(cluster.Compilation);
            Assert.NotNull(all.Compilation);
            Assert.NotNull(cluster.DonorPe);
            Assert.NotNull(all.DonorPe);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, all.Status);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.ContextFail, all.Status);
            Assert.Equal(RoundTripScopeComparisonStatus.Completed, pair.Comparison.Status);
            var comparison = Assert.Single(pair.Comparison.Members);
            Assert.Equal(RoundTripEvidenceStatus.Exact, comparison.CSharpStatus);
            Assert.Equal(IlBodyDiffOutcome.Exact, comparison.IlStatus);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

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
    public void CompileBackTargets_RoundTripsExplicitInterfaceMethod()
    {
        // #3112: a class method whose metadata name is an explicit-interface spelling
        // (`IBase.Touch`) must reconstruct as an explicit-interface implementation with the
        // interface declaring the member, not a plain `IBase_Touch` method (which recompiles
        // under the wrong name and fails the fidelity lookup as ContextFail/method-not-found).
        var assemblyPath = CompileFixture("""
            using System;
            public sealed class ExplicitMethodFixture : IBase
            {
                void IBase.Touch()
                {
                    Console.WriteLine("touched");
                }
            }

            public interface IBase
            {
                void Touch();
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitMethodFixture",
                    "IBase.Touch",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("void IBase.Touch()", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IBase_Touch", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsNamespacedExplicitInterfaceMethodWithParameters()
    {
        // The corpus family (#3112) is dominated by namespaced interfaces (System.IConvertible,
        // System.Collections.IEnumerable, ...) with real parameters and return values. Reconstruct
        // the qualified interface spelling and round-trip the body exactly.
        var assemblyPath = CompileFixture("""
            namespace Sample
            {
                public sealed class ExplicitComputeFixture : IComputer
                {
                    int IComputer.Compute(int left, int right)
                    {
                        return left + right;
                    }
                }

                public interface IComputer
                {
                    int Compute(int left, int right);
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "Sample.ExplicitComputeFixture",
                    "Sample.IComputer.Compute",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("int Sample.IComputer.Compute(int left, int right)", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IComputer_Compute", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsExternalSingleMemberExplicitInterfaceMethod()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Seq : System.Collections.IEnumerable
            {
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    throw null;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "Seq",
                    "System.Collections.IEnumerable.GetEnumerator",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.Source.Contains("class Seq : System.Collections.IEnumerable", StringComparison.Ordinal)
                || result.Source.Contains("class Seq : IEnumerable", StringComparison.Ordinal),
                result.Source);
            Assert.True(
                result.Source.Contains(
                    "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()",
                    StringComparison.Ordinal)
                || result.Source.Contains(
                    "IEnumerator System.Collections.IEnumerable.GetEnumerator()",
                    StringComparison.Ordinal)
                || result.Source.Contains(
                    "IEnumerator IEnumerable.GetEnumerator()",
                    StringComparison.Ordinal),
                result.Source);
            Assert.DoesNotContain("System_Collections_IEnumerable_GetEnumerator", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    // Close-negative for the shadow-decline (#3112 review): a compiler-authored sibling whose
    // simple name matches a *non-leading* segment of the interface spelling must NOT trigger a
    // decline. Only the FIRST segment (`System`) can be shadowed into a compile error, because
    // the explicit-member qualifier is always emitted fully qualified
    // (`System.Collections.IEnumerable.GetEnumerator`) and the collision-aware using-collapser
    // only shortens the base-list entry to the bare `IEnumerable` when nothing collides:
    //  - `N.Collections` (middle segment): collapser shortens to `class Seq : IEnumerable`; the
    //    middle `Collections` never leads, so it compiles.
    //  - `N.IEnumerable` (final type name): collapser detects the collision and KEEPS the base
    //    list fully qualified (`class Seq : System.Collections.IEnumerable`, leading `System`),
    //    so it still compiles.
    // Under RoundTripScope.All the sibling is reconstructed alongside the real explicit impl and
    // the whole shape must round-trip Exact, not fall back to the sanitized
    // `System_Collections_IEnumerable_GetEnumerator` floor.
    [Theory]
    [InlineData("Collections")]
    [InlineData("IEnumerable")]
    public void CompileBackTargets_ExternalExplicitInterfaceKeepsExactWhenClosureSiblingMatchesNonLeadingSegment(string siblingName)
    {
        var assemblyPath = CompileFixture($$"""
            namespace N;
            public sealed class {{siblingName}} { }
            public sealed class Seq : System.Collections.IEnumerable
            {
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    throw null;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "N.Seq",
                    "System.Collections.IEnumerable.GetEnumerator",
                    0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains(
                "System.Collections.IEnumerable.GetEnumerator()",
                result.Source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "System_Collections_IEnumerable_GetEnumerator",
                result.Source,
                StringComparison.Ordinal);
            Assert.Contains($"class {siblingName}", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    // Regression for #3112 review: a hand-authored IL assembly can carry a *clean*
    // explicit-interface metadata name (`System.Collections.IEnumerable.GetEnumerator`)
    // while also declaring a sibling type `N.System` that shadows the `System` namespace
    // root of that spelling. No conformant C# compiler can emit this shape — a shadowing
    // sibling forces `global::` into the explicit override's metadata name, which the gate
    // declines — but IL is not bound by that rule. Under RoundTripScope.All the sibling is
    // reconstructed into namespace N, so the unrooted `System.Collections.IEnumerable`
    // spelling binds to the sibling (CS0426). The gate must decline to the sanitized shape
    // rather than introduce that new RecompileFail. Under RoundTripScope.Cluster the sibling
    // is not reconstructed, so engagement must be preserved (round-trips Exact): the decline
    // is scope-aware, not a blanket stand-down.
    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenClosureSiblingShadowsSpelling()
    {
        var ilasm = TryLocateIlasm();
        if (ilasm is null)
        {
            Assert.Skip("ilasm not available; skipping hand-authored IL shadow regression.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var assemblyPath = AssembleIlFixture(ilasm, ShadowingSiblingIl, directory, "shadowrepro");
        try
        {
            var target = new ReturnToSender.RequestedTarget(
                "N.Seq",
                "System.Collections.IEnumerable.GetEnumerator",
                0);

            // Cluster does not reconstruct the shadowing sibling N.System, so the external
            // explicit-interface reconstruction engages and round-trips Exact.
            var cluster = Assert.Single(
                ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.Cluster));
            Assert.True(
                cluster.Status == FidelityCheck.CompileBackStatus.Exact,
                $"cluster {cluster.Status}: {cluster.Detail}");

            // All reconstructs N.System, which shadows the `System` root of the spelling.
            // The gate must decline to the sanitized shape (the pre-#3112 ContextFail floor)
            // rather than emit a new RecompileFail (CS0426). Strictly better or identical,
            // never worse.
            var all = Assert.Single(
                ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
            Assert.True(
                all.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"all {all.Status}: {all.Detail}");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // Regression for #3112 review (escaped-identifier shadow): a hand-authored external
    // interface can live in a namespace whose segment is a C# keyword (`class`), so its raw
    // metadata full name is `class.IProbe` but its C# display name is `@class.IProbe`. A
    // sibling type `N.class` (raw) is emitted as `class @class` and shadows the `@class` root
    // of the spelling under RoundTripScope.All (CS0426). The shadow check must compare the
    // leading segment against the raw metadata name (`class`), not the escaped display name
    // (`@class`) — otherwise the collision is missed and a new RecompileFail escapes. The gate
    // must decline to the sanitized ContextFail floor. Uses two IL assemblies (an external
    // contract plus the target) resolved as siblings.
    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenKeywordNamespaceSiblingShadowsSpelling()
    {
        var ilasm = TryLocateIlasm();
        if (ilasm is null)
        {
            Assert.Skip("ilasm not available; skipping hand-authored IL keyword-shadow regression.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        AssembleIlFixture(ilasm, KeywordContractsIl, directory, "KeywordContracts");
        var assemblyPath = AssembleIlFixture(ilasm, KeywordShadowFixtureIl, directory, "keywordfixture");
        try
        {
            var target = new ReturnToSender.RequestedTarget("N.Seq", "class.IProbe.M", 0);

            // All reconstructs the sibling N.class (emitted `class @class`), which shadows the
            // escaped `@class` root of the spelling. The gate must decline to the sanitized
            // shape rather than emit a new RecompileFail (CS0426). The raw-metadata-name
            // comparison is what catches this; the escaped display name would miss it.
            var all = Assert.Single(
                ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
            Assert.True(
                all.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"all {all.Status}: {all.Detail}{Environment.NewLine}{all.Source}");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // Regression for #3112 review (unspeakable member name): a hand-authored external interface
    // can have a legal type name (`Good.IProbe`) but a method whose metadata name is
    // compiler-unspeakable (`<Bad>`). The explicit-member spelling emits
    // Identifier(declarationName), which sanitizes `<Bad>` lossily to `__Bad_`; the interface
    // still declares `<Bad>`, so `Good.IProbe.__Bad_()` binds to no interface member
    // (CS0539 = RecompileFail). The gate must decline to the sanitized ContextFail floor.
    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenMemberNameIsUnrepresentable()
    {
        var ilasm = TryLocateIlasm();
        if (ilasm is null)
        {
            Assert.Skip("ilasm not available; skipping hand-authored IL unspeakable-member regression.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        AssembleIlFixture(ilasm, UnspeakableMemberContractsIl, directory, "BadMethodContracts");
        var assemblyPath = AssembleIlFixture(ilasm, UnspeakableMemberFixtureIl, directory, "badmethodfixture");
        try
        {
            var target = new ReturnToSender.RequestedTarget("N.Seq", "Good.IProbe.<Bad>", 0);

            // All would reconstruct `Good.IProbe.__Bad_()` from the lossily-sanitized member
            // name; the interface declares `<Bad>`, so it binds to nothing (CS0539). The gate
            // must decline rather than emit a new RecompileFail. The member-name guard is what
            // catches this; the raw member name is not identifier-like.
            var all = Assert.Single(
                ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
            Assert.True(
                all.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"all {all.Status}: {all.Detail}{Environment.NewLine}{all.Source}");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // Regression for #3112 review (format-character member name): a hand-authored external
    // interface method name can carry a Unicode format character (U+200C) that is
    // identifier-like yet does NOT round-trip — Roslyn strips format characters when binding,
    // so the emitted `Good.IProbe.M\u200C()` binds to `Good.IProbe.M`, which the interface
    // (declaring the raw `M\u200C`) does not contain (CS0539 = RecompileFail). The member-name
    // round-trip guard must reject format characters, not merely check identifier-likeness.
    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenMemberNameHasFormatCharacter()
    {
        var ilasm = TryLocateIlasm();
        if (ilasm is null)
        {
            Assert.Skip("ilasm not available; skipping hand-authored IL format-character member regression.");
            return;
        }

        const string zwnj = "\u200C";
        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        AssembleIlFixture(ilasm, CfMemberContractsIl.Replace("%ZWNJ%", zwnj), directory, "CfContracts");
        var assemblyPath = AssembleIlFixture(
            ilasm, CfMemberFixtureIl.Replace("%ZWNJ%", zwnj), directory, "cffixture");
        try
        {
            var target = new ReturnToSender.RequestedTarget("N.Seq", $"Good.IProbe.M{zwnj}", 0);

            var all = Assert.Single(
                ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
            Assert.True(
                all.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"all {all.Status}: {all.Detail}{Environment.NewLine}{all.Source}");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // Regression for #3112 review (format-character namespace): the same format-character
    // hazard applies to the interface TYPE name. A namespace segment `G\u200Cood` is
    // identifier-like but does not round-trip (Roslyn strips U+200C, so the emitted
    // `G\u200Cood.IProbe` binds to `Good.IProbe`, which does not exist — CS0246). The
    // interface-name representability guard must reject format characters per segment.
    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenNamespaceHasFormatCharacter()
    {
        var ilasm = TryLocateIlasm();
        if (ilasm is null)
        {
            Assert.Skip("ilasm not available; skipping hand-authored IL format-character namespace regression.");
            return;
        }

        const string zwnj = "\u200C";
        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        AssembleIlFixture(ilasm, CfNamespaceContractsIl.Replace("%ZWNJ%", zwnj), directory, "CfNsContracts");
        var assemblyPath = AssembleIlFixture(
            ilasm, CfNamespaceFixtureIl.Replace("%ZWNJ%", zwnj), directory, "cfnsfixture");
        try
        {
            var target = new ReturnToSender.RequestedTarget("N.Seq", $"G{zwnj}ood.IProbe.M", 0);

            var all = Assert.Single(
                ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
            Assert.True(
                all.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"all {all.Status}: {all.Detail}{Environment.NewLine}{all.Source}");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // Regression for #3112 review (decomposed / non-NFC member name): the round-trip guard must
    // NOT over-decline. Roslyn strips format (Cf) characters when binding identifiers but does
    // NOT apply Unicode normalization, so a decomposed member name `e` + U+0301 (which is NOT in
    // NFC — its composed form is U+00E9) is emitted and bound verbatim and round-trips exactly.
    // A prior guard that additionally required NFC declined this compiler-producible shape to the
    // sanitized ContextFail floor, regressing a real Exact. The gate must engage and round-trip
    // Exact, not decline.
    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceKeepsExactWhenMemberNameIsDecomposed()
    {
        var ilasm = TryLocateIlasm();
        if (ilasm is null)
        {
            Assert.Skip("ilasm not available; skipping hand-authored IL decomposed-identifier member regression.");
            return;
        }

        const string comb = "\u0301";
        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        AssembleIlFixture(ilasm, NfcMemberContractsIl.Replace("%COMB%", comb), directory, "NfcContracts");
        var assemblyPath = AssembleIlFixture(
            ilasm, NfcMemberFixtureIl.Replace("%COMB%", comb), directory, "nfcfixture");
        try
        {
            var target = new ReturnToSender.RequestedTarget("N.Seq", $"Good.IProbe.e{comb}", 0);

            var all = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath, [target], RoundTripScope.All, RoundTripBodyPolicy.Full));
            Assert.True(
                all.Status == FidelityCheck.CompileBackStatus.Exact,
                $"all {all.Status}: {all.Detail}{Environment.NewLine}{all.Source}");
            Assert.False(all.UsedCompileBackFloor, all.Detail);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // Regression for #3112 review (unrepresentable interface name): a hand-authored external
    // interface can live in a namespace whose segment is a compiler-unspeakable name (`<Bad>`)
    // — legal in metadata but not a legal C# identifier. Clean() sanitizes it lossily to a
    // DIFFERENT identifier (`__Bad_`), so the reconstruction would emit `using __Bad_;` /
    // `__Bad_.IProbe.M()` referencing a type that does not exist (CS0246 = RecompileFail). The
    // gate must recognize the name cannot round-trip and decline to the sanitized ContextFail
    // floor. Uses two IL assemblies (an external contract plus the target) resolved as
    // siblings.
    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenNameIsUnrepresentable()
    {
        var ilasm = TryLocateIlasm();
        if (ilasm is null)
        {
            Assert.Skip("ilasm not available; skipping hand-authored IL unrepresentable-name regression.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        AssembleIlFixture(ilasm, UnrepresentableContractsIl, directory, "GeneratedContracts");
        var assemblyPath = AssembleIlFixture(ilasm, UnrepresentableFixtureIl, directory, "badfixture");
        try
        {
            var target = new ReturnToSender.RequestedTarget("N.Seq", "<Bad>.IProbe.M", 0);

            // All would reconstruct the interface spelling from the lossily-sanitized display
            // name (`__Bad_.IProbe`), which names no type. The gate must decline rather than
            // emit a new RecompileFail (CS0246). The name-representability guard is what
            // catches this; the raw metadata name is not identifier-like.
            var all = Assert.Single(
                ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
            Assert.True(
                all.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"all {all.Status}: {all.Detail}{Environment.NewLine}{all.Source}");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsExternalMultiMemberExplicitInterfaceMethod()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public sealed class Convertible : IConvertible
            {
                TypeCode IConvertible.GetTypeCode() => TypeCode.Empty;
                bool IConvertible.ToBoolean(IFormatProvider provider) => false;
                byte IConvertible.ToByte(IFormatProvider provider) => 0;
                char IConvertible.ToChar(IFormatProvider provider) => '\0';
                DateTime IConvertible.ToDateTime(IFormatProvider provider) => default;
                decimal IConvertible.ToDecimal(IFormatProvider provider) => 0m;
                double IConvertible.ToDouble(IFormatProvider provider) => 0d;
                short IConvertible.ToInt16(IFormatProvider provider) => 0;
                int IConvertible.ToInt32(IFormatProvider provider) => 0;
                long IConvertible.ToInt64(IFormatProvider provider) => 0L;
                sbyte IConvertible.ToSByte(IFormatProvider provider) => 0;
                float IConvertible.ToSingle(IFormatProvider provider) => 0f;
                string IConvertible.ToString(IFormatProvider provider) => "";
                object IConvertible.ToType(Type conversionType, IFormatProvider provider) => this;
                ushort IConvertible.ToUInt16(IFormatProvider provider) => 0;
                uint IConvertible.ToUInt32(IFormatProvider provider) => 0U;
                ulong IConvertible.ToUInt64(IFormatProvider provider) => 0UL;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "Convertible",
                    "System.IConvertible.ToBoolean",
                    0)]));

            // #3112 Increment 2: a multi-member external interface engages by reconstructing the
            // target member with its real body and synthesizing `throw null` explicit-interface
            // stubs for every OTHER required member, so the full surface satisfies CS0535 and the
            // fidelity lookup finds the correctly-named explicit member (Exact, not the sanitized
            // `System_IConvertible_ToBoolean` ContextFail floor).
            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.Source.Contains(": System.IConvertible", StringComparison.Ordinal)
                || result.Source.Contains(": IConvertible", StringComparison.Ordinal),
                result.Source);
            // The target member reconstructs as a real explicit implementation.
            Assert.Contains("IConvertible.ToBoolean(", result.Source, StringComparison.Ordinal);
            // A non-target member is synthesized as a `throw null` explicit-interface stub so the
            // interface's full required surface is satisfied.
            Assert.Contains("IConvertible.GetTypeCode(", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("System_IConvertible_ToBoolean", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_MultiMemberExternalExplicitInterfaceWithUnspellableSiblingFallsBackWithoutRecompileFail()
    {
        // #3112 Increment 2 whole-surface atomicity: engaging a multi-member external interface
        // names it in the base list, which forces the reconstructed type to implement EVERY
        // required member (CS0535). The target member (`Target`) is perfectly representable, but
        // a SIBLING member (`Sibling(ref int)`) carries by-ref detail SignatureDecoder cannot
        // spell unambiguously. Synthesizing a stub for it (or omitting it) would leave the
        // surface unsatisfied or drifted (CS0535/CS0539 = RecompileFail). The gate must decline
        // the WHOLE interface when ANY member is unspellable and keep the sanitized ContextFail
        // floor, even though the requested target member itself is fine.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var contractsPath = CompileFixture(
            "namespace RtsMulti { public interface IProbe { void Target(); void Sibling(ref int value); } }",
            directory: fixtureDir,
            assemblyName: "RtsMultiContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class MultiImpl : RtsMulti.IProbe
            {
                void RtsMulti.IProbe.Target() { }
                void RtsMulti.IProbe.Sibling(ref int value) => value = 0;
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(contractsPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "MultiImpl",
                    "RtsMulti.IProbe.Target",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsMulti_IProbe_Target", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsMulti.IProbe.Target", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_MultiMemberExternalExplicitInterfaceWithOverloadedSiblingsRoundTrips()
    {
        // #3112 Increment 2 overload robustness: a real corpus interface such as
        // System.ComponentModel.ICustomTypeDescriptor carries same-name overloads
        // (`GetProperties()` / `GetProperties(Attribute[])`). Every non-target member is
        // synthesized as a `throw null` explicit-interface stub, and two overloads share one
        // explicit member name (`RtsOv.IProbe.Overloaded`) while differing only by signature.
        // The reconstructed members must NOT be deduplicated by name (that would drop one
        // overload, leaving the interface surface unsatisfied, CS0535). Both stubs must emit as
        // distinct explicit implementations so the type compiles and the target reconstructs
        // Exact.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var contractsPath = CompileFixture(
            "namespace RtsOv { public interface IProbe { void Target(); int Overloaded(); int Overloaded(string label); } }",
            directory: fixtureDir,
            assemblyName: "RtsOvContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class OvImpl : RtsOv.IProbe
            {
                void RtsOv.IProbe.Target() { }
                int RtsOv.IProbe.Overloaded() => 0;
                int RtsOv.IProbe.Overloaded(string label) => 0;
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(contractsPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "OvImpl",
                    "RtsOv.IProbe.Target",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.True(
                result.Source.Contains(": RtsOv.IProbe", StringComparison.Ordinal)
                || result.Source.Contains(": IProbe", StringComparison.Ordinal),
                result.Source);
            // Both overloads must be present as distinct explicit-interface stubs.
            Assert.Contains("RtsOv.IProbe.Overloaded()", result.Source, StringComparison.Ordinal);
            Assert.Contains("RtsOv.IProbe.Overloaded(string", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsOv_IProbe_Target", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_InheritedBaseInterfaceMemberFallsBackWithoutRecompileFail()
    {
        // #3112 Increment 2 base-interface atomicity: an external interface that INHERITS from
        // another interface flattens the base's members into the required surface, but the
        // synthesized stubs record no declaring-interface identity and are all qualified with
        // the ROOT interface. An inherited member emitted as `void IRoot.Member()` is CS0539
        // (not a member of IRoot) and leaves the base member unimplemented (CS0535 =
        // RecompileFail). The gate must decline the WHOLE surface to the sanitized ContextFail
        // floor whenever a base interface contributes a required member.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var contractsPath = CompileFixture(
            "namespace RtsInh { public interface IBase { void Sibling(); } public interface IDerived : IBase { void Target(); } }",
            directory: fixtureDir,
            assemblyName: "RtsInhContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class InhImpl : RtsInh.IDerived
            {
                void RtsInh.IDerived.Target() { }
                void RtsInh.IBase.Sibling() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(contractsPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("InhImpl", "RtsInh.IDerived.Target", 0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsInh_IDerived_Target", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsInh.IDerived.Target", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_NestedExternalMultiMemberInterfaceFallsBackWithoutRecompileFail()
    {
        // #3112 Increment 2: a nested external interface (`Outer.IProbe`) cannot be named in the
        // reconstructed base list — its metadata separator (`Outer+IProbe`) is not bindable C#
        // and its TypeReference resolves through the enclosing type rather than an assembly
        // reference. The engagement must decline to the sanitized ContextFail floor rather than
        // emit an unspellable `Outer+IProbe` qualifier (CS1001/CS0246 = RecompileFail).
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var contractsPath = CompileFixture(
            "namespace RtsNest { public class Outer { public interface IProbe { void Target(); void Sibling(); } } }",
            directory: fixtureDir,
            assemblyName: "RtsNestContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class NestImpl : RtsNest.Outer.IProbe
            {
                void RtsNest.Outer.IProbe.Target() { }
                void RtsNest.Outer.IProbe.Sibling() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(contractsPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("NestImpl", "RtsNest.Outer.IProbe.Target", 0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsNest_Outer_IProbe_Target", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_GenericExternalExplicitInterfaceFallsBackWithoutRecompileFail()
    {
        var assemblyPath = CompileFixture("""
            public sealed class IntSeq : System.Collections.Generic.IEnumerable<int>
            {
                System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<int>.GetEnumerator()
                {
                    throw null;
                }

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    throw null;
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "IntSeq",
                    "System.Collections.Generic.IEnumerable<System.Int32>.GetEnumerator",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("System_Collections_Generic_IEnumerable_System_Int32__GetEnumerator", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Collections.Generic.IEnumerable<System.Int32>.GetEnumerator", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithSignatureDriftFallsBackWithoutRecompileFail()
    {
        // Regression guard: the external explicit-interface gate must compare full
        // signatures, not just name + generic arity. The target is compiled against a
        // reference interface method `int M(int)`, but the copy of that interface resolved
        // at reconstruction time (the sibling on disk) declares `int M(string)`. A
        // name+arity-only gate would engage and emit `int RtsDrift.IProbe.M(int)`, which
        // binds to no member of the resolved interface (CS0539 = RecompileFail). The gate
        // must decline and keep the sanitized ContextFail floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referenceDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        // Reference build the target compiles against: M takes an int.
        var referencePath = CompileFixture(
            "namespace RtsDrift { public interface IProbe { int M(int value); } }",
            directory: referenceDir,
            assemblyName: "RtsDriftContracts");
        // Drifted build placed next to the target: same type, but M takes a string.
        CompileFixture(
            "namespace RtsDrift { public interface IProbe { int M(string value); } }",
            directory: fixtureDir,
            assemblyName: "RtsDriftContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class DriftImpl : RtsDrift.IProbe
            {
                int RtsDrift.IProbe.M(int value) => value;
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(referencePath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "DriftImpl",
                    "RtsDrift.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsDrift_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsDrift.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            if (Directory.Exists(referenceDir))
                Directory.Delete(referenceDir, recursive: true);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithAmbiguousDefinitionFallsBackWithoutRecompileFail()
    {
        // Regression guard: the reconstructed base list names the interface by display name
        // only, with no extern alias, so it must be defined by exactly one assembly across
        // the recompile closure. Here two sibling assemblies both define `RtsDup.IShape`.
        // Engaging would emit `: RtsDup.IShape`, which the recompile cannot disambiguate
        // (CS0433 = RecompileFail). The gate must decline and keep the sanitized floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        const string interfaceSource =
            "namespace RtsDup { public interface IShape { int M(int value); } }";
        var primaryPath = CompileFixture(
            interfaceSource,
            directory: fixtureDir,
            assemblyName: "RtsDupPrimary");
        CompileFixture(
            interfaceSource,
            directory: fixtureDir,
            assemblyName: "RtsDupSecondary");
        var assemblyPath = CompileFixture(
            """
            public sealed class DupImpl : RtsDup.IShape
            {
                int RtsDup.IShape.M(int value) => value;
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(primaryPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "DupImpl",
                    "RtsDup.IShape.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsDup_IShape_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsDup.IShape.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithByRefKindDriftFallsBackWithoutRecompileFail()
    {
        // Regression guard: a decoded-signature string cannot distinguish by-ref kinds.
        // SignatureDecoder renders `ref T`, `out T`, and `in T` identically as "ref T", so a
        // name + arity + decoded-string gate would treat `void M(ref int)` and
        // `void M(out int)` as equal. The target here is compiled against a reference
        // interface method `void M(ref int)`, but the copy resolved at reconstruction time
        // (the sibling on disk) declares `void M(out int)`. Engaging would emit
        // `void RtsRef.IProbe.M(ref int)`, which binds to no member of the resolved
        // interface (CS0539 = RecompileFail). The gate must decline any signature carrying
        // by-ref detail and keep the sanitized ContextFail floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referenceDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        // Reference build the target compiles against: M takes a ref int.
        var referencePath = CompileFixture(
            "namespace RtsRef { public interface IProbe { void M(ref int value); } }",
            directory: referenceDir,
            assemblyName: "RtsRefContracts");
        // Drifted build placed next to the target: same type, but M takes an out int.
        CompileFixture(
            "namespace RtsRef { public interface IProbe { void M(out int value); } }",
            directory: fixtureDir,
            assemblyName: "RtsRefContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class RefImpl : RtsRef.IProbe
            {
                void RtsRef.IProbe.M(ref int value) => value = 0;
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(referencePath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "RefImpl",
                    "RtsRef.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsRef_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsRef.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            if (Directory.Exists(referenceDir))
                Directory.Delete(referenceDir, recursive: true);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithVarArgsFallsBackWithoutRecompileFail()
    {
        // Regression guard: the decoded return/parameter strings do not carry a method's
        // calling convention, so a VarArgs (`__arglist`) interface method is spelled
        // identically to a fixed-arity one. C# cannot express `__arglist` in a reconstructed
        // explicit interface member, so engaging would emit `void RtsVar.IProbe.M()` (the
        // `__arglist` dropped), which binds to no member of the resolved interface
        // (CS0539 = RecompileFail). The gate must decline any non-default calling convention
        // and keep the sanitized ContextFail floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var contractsPath = CompileFixture(
            "namespace RtsVar { public interface IProbe { void M(__arglist); } }",
            directory: fixtureDir,
            assemblyName: "RtsVarContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class VarImpl : RtsVar.IProbe
            {
                void RtsVar.IProbe.M(__arglist) { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(contractsPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "VarImpl",
                    "RtsVar.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsVar_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsVar.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithInternalInterfaceFallsBackWithoutRecompileFail()
    {
        // Regression guard: the reconstructed assembly ("return-to-sender-source-oracle")
        // references the interface's defining assembly but is not granted InternalsVisibleTo,
        // so it cannot name an internal interface even though the target implements it via IVT
        // to its own name. Engaging would emit `: RtsInt.IProbe` against a type the recompile
        // cannot see (CS0122 = RecompileFail). The gate must decline any non-publicly-accessible
        // interface and keep the sanitized ContextFail floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var contractsPath = CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: InternalsVisibleTo("fixture")]
            namespace RtsInt { internal interface IProbe { void M(); } }
            """,
            directory: fixtureDir,
            assemblyName: "RtsIntContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class IntImpl : RtsInt.IProbe
            {
                void RtsInt.IProbe.M() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(contractsPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "IntImpl",
                    "RtsInt.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsInt_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsInt.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithInternalMemberFallsBackWithoutRecompileFail()
    {
        // Regression guard: a PUBLIC interface may still declare a NON-public member (C# 8+
        // allows explicit accessibility on interface members). The interface type passes the
        // IsPubliclyAccessible gate, but the reconstructed assembly
        // ("return-to-sender-source-oracle") is not granted InternalsVisibleTo, so it cannot
        // name the internal member even though the target implements it via IVT to its own name.
        // Engaging would emit `void RtsVis.IProbe.M()` against a member the recompile cannot see
        // (CS0122 = RecompileFail). The gate must decline any non-public required method and keep
        // the sanitized ContextFail floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var contractsPath = CompileFixture(
            """
            using System.Runtime.CompilerServices;
            [assembly: InternalsVisibleTo("fixture")]
            namespace RtsVis { public interface IProbe { internal abstract void M(); } }
            """,
            directory: fixtureDir,
            assemblyName: "RtsVisContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class VisImpl : RtsVis.IProbe
            {
                void RtsVis.IProbe.M() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(contractsPath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "VisImpl",
                    "RtsVis.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsVis_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsVis.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithObsoleteErrorInterfaceFallsBackWithoutRecompileFail()
    {
        // Regression guard: the reconstructed explicit member names the interface twice — the base
        // list `: RtsObs.IProbe` and the qualifier `void RtsObs.IProbe.M()`. If the interface the
        // recompile resolves is marked `[Obsolete(..., error: true)]`, naming it is a hard CS0619
        // (the emitted `#pragma warning disable` suppresses only the warning form), turning the
        // sanitized ContextFail floor into a RecompileFail. A target cannot itself be compiled
        // against an obsolete-error interface (CS0619 at its own build), so the obsolete form only
        // arises as version drift: the target is built against a clean interface, and the sibling
        // resolved at reconstruction has since become obsolete-error. The gate must decline.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referenceDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referencePath = CompileFixture(
            "namespace RtsObs { public interface IProbe { void M(); } }",
            directory: referenceDir,
            assemblyName: "RtsObsContracts");
        CompileFixture(
            """
            namespace RtsObs { [System.Obsolete("gone", true)] public interface IProbe { void M(); } }
            """,
            directory: fixtureDir,
            assemblyName: "RtsObsContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class ObsImpl : RtsObs.IProbe
            {
                void RtsObs.IProbe.M() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(referencePath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ObsImpl",
                    "RtsObs.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsObs_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsObs.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            if (Directory.Exists(referenceDir))
                Directory.Delete(referenceDir, recursive: true);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithCompilerFeatureRequiredInterfaceFallsBackWithoutRecompileFail()
    {
        // Regression guard: naming the interface in the reconstructed base list (`: N.IProbe`)
        // forces the recompile to bind to it, which demands every feature the interface requires
        // via [CompilerFeatureRequired]. If the resolved interface carries an unsatisfiable feature
        // marker, binding it is a hard CS9041, turning the sanitized ContextFail floor (which never
        // names the interface, so never triggers the requirement) into a RecompileFail. This
        // attribute is not emittable from C# source (CS8335), so the poison sibling is authored
        // directly as metadata: a target built against a clean single-member interface, whose
        // sibling resolved at reconstruction demands an unknown compiler feature.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referenceDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referencePath = CompileFixture(
            "namespace RtsCfr { public interface IProbe { void M(); } }",
            directory: referenceDir,
            assemblyName: "RtsCfrContracts");
        Directory.CreateDirectory(fixtureDir);
        File.WriteAllBytes(
            Path.Combine(fixtureDir, "RtsCfrContracts.dll"),
            BuildCompilerFeatureRequiredInterfaceImage(
                assemblyName: "RtsCfrContracts",
                namespaceName: "RtsCfr",
                typeName: "IProbe",
                methodName: "M"));
        var assemblyPath = CompileFixture(
            """
            public sealed class CfrImpl : RtsCfr.IProbe
            {
                void RtsCfr.IProbe.M() { }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(referencePath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "CfrImpl",
                    "RtsCfr.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsCfr_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsCfr.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            if (Directory.Exists(referenceDir))
                Directory.Delete(referenceDir, recursive: true);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithGenericParameterDriftFallsBackWithoutRecompileFail()
    {
        // Regression guard: SignatureDecoder spells generic method parameters by their metadata
        // name, not their position, so `int M<T, U>(U)` and `int M<U, T>(U)` both decode their
        // parameter to "U" and compare equal — yet the parameter is the 2nd type parameter in
        // one and the 1st in the other. The target is compiled against `int M<T, U>(U)`, but the
        // sibling resolved at reconstruction declares `int M<U, T>(U)`. Engaging would emit an
        // explicit member binding to no interface member (CS0539 = RecompileFail). The gate must
        // decline any signature carrying generic parameters and keep the sanitized floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referenceDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referencePath = CompileFixture(
            "namespace RtsGen { public interface IProbe { int M<T, U>(U value); } }",
            directory: referenceDir,
            assemblyName: "RtsGenContracts");
        CompileFixture(
            "namespace RtsGen { public interface IProbe { int M<U, T>(U value); } }",
            directory: fixtureDir,
            assemblyName: "RtsGenContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class GenImpl : RtsGen.IProbe
            {
                int RtsGen.IProbe.M<T, U>(U value) => 0;
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(referencePath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "GenImpl",
                    "RtsGen.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsGen_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsGen.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            if (Directory.Exists(referenceDir))
                Directory.Delete(referenceDir, recursive: true);
        }
    }

    [Fact]
    public void CompileBackTargets_ExternalExplicitInterfaceWithConstraintOnlyGenericFallsBackWithoutRecompileFail()
    {
        // Regression guard: a generic type parameter can appear ONLY in a constraint, invisible
        // to the return/parameter signature the probe inspects. `void M<T>() where T : Base` has
        // an empty signature, so a signature-only gate would engage. An explicit interface member
        // cannot restate constraints — it inherits them from the resolved interface. The target is
        // compiled against the constrained interface (its body calls the constraint member), but
        // the sibling resolved at reconstruction declares `void M<T>()` with NO constraint.
        // Engaging would emit `void RtsCon.IProbe.M<T>()` whose inherited (drifted, unconstrained)
        // T no longer permits the call — CS1061 = RecompileFail. The sanitized floor instead emits
        // a plain generic method that restates `where T : Base`, so it still compiles. The gate
        // must decline any generic interface method and keep the ContextFail floor.
        var fixtureDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referenceDir = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        var referencePath = CompileFixture(
            "namespace RtsCon { public class Base { public void Foo() { } } public interface IProbe { void M<T>() where T : Base; } }",
            directory: referenceDir,
            assemblyName: "RtsConContracts");
        CompileFixture(
            "namespace RtsCon { public class Base { public void Foo() { } } public interface IProbe { void M<T>(); } }",
            directory: fixtureDir,
            assemblyName: "RtsConContracts");
        var assemblyPath = CompileFixture(
            """
            public sealed class ConImpl : RtsCon.IProbe
            {
                void RtsCon.IProbe.M<T>()
                {
                    default(T).Foo();
                }
            }
            """,
            directory: fixtureDir,
            assemblyName: "fixture",
            additionalReferences: [MetadataReference.CreateFromFile(referencePath)]);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ConImpl",
                    "RtsCon.IProbe.M",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("RtsCon_IProbe_M", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("RtsCon.IProbe.M", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            if (Directory.Exists(referenceDir))
                Directory.Delete(referenceDir, recursive: true);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsGenericExplicitInterfaceMethod()
    {
        // A generic method on a non-generic interface implemented explicitly keeps its method
        // type parameters in the reconstructed `IBox.Wrap<T>(...)` header (constraints are
        // inherited from the interface and must be omitted).
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitGenericFixture : IBox
            {
                T IBox.Wrap<T>(T value)
                {
                    return value;
                }
            }

            public interface IBox
            {
                T Wrap<T>(T value);
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitGenericFixture",
                    "IBox.Wrap",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("IBox.Wrap<T>(T value)", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IBox_Wrap", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_NestedExplicitInterfaceMethodFallsBackToPlainWithoutRecompileFail()
    {
        // Negative case for the explicit-interface method reconstruction (#3112): a nested
        // interface (e.g. the corpus's `MutexSlim.IPendingLockToken`, `SqlMapper.ITypeHandler`)
        // is only reached through its enclosing root and is not a standalone closure
        // requirement, so its member declaration cannot be appended to a reconstructed
        // interface shell. RTS must NOT emit an unbindable `Outer.IInner.Ping()` (which would
        // turn a method-not-found ContextFail into a CS0539/CS0246 RecompileFail); it reverts
        // to the plain sanitized shape, preserving the pre-fix ContextFail with no regression.
        var assemblyPath = CompileFixture("""
            namespace Sample
            {
                public sealed class NestedExplicitFixture : Outer.IInner
                {
                    void Outer.IInner.Ping()
                    {
                        System.Console.WriteLine("ping");
                    }
                }

                public static class Outer
                {
                    public interface IInner
                    {
                        void Ping();
                    }
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "Sample.NestedExplicitFixture",
                    "Sample.Outer.IInner.Ping",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            // The target reverts to the plain sanitized shape rather than the explicit spelling.
            Assert.Contains("Sample_Outer_IInner_Ping", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("void Sample.Outer.IInner.Ping(", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsStaticAbstractExplicitInterfaceMethod()
    {
        // Close positive case for the operator/DIM discriminators (#3112, adversarial review):
        // an explicit implementation of a NON-operator static-abstract interface method must
        // still reconstruct as an explicit implementation and round-trip Exact — the `op_`
        // and default-interface-method fallbacks must not over-trigger on it.
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitStaticFixture : IParseable
            {
                static int IParseable.Parse(string text)
                {
                    return text.Length;
                }
            }

            public interface IParseable
            {
                static abstract int Parse(string text);
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitStaticFixture",
                    "IParseable.Parse",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("static int IParseable.Parse(string text)", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IParseable_Parse", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExplicitInterfaceOperatorFallsBackToPlainWithoutRecompileFail()
    {
        // Negative case (#3112, adversarial review): an explicit-interface implementation of a
        // static-abstract operator cannot be reconstructed by the explicit-method path — the
        // explicit target spelling would carry the raw `op_Addition` metadata name (via
        // CSharpIdentifier.Sanitize) instead of C# `operator +` syntax, so it would not match
        // the interface's `operator` member (CS0539). RTS must fall back to the plain sanitized
        // shape (main's behavior) rather than regress the method-not-found ContextFail into a
        // RecompileFail.
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitOperatorFixture : INonGenericAdd
            {
                static INonGenericAdd INonGenericAdd.operator +(INonGenericAdd left, INonGenericAdd right)
                {
                    return left;
                }
            }

            public interface INonGenericAdd
            {
                static abstract INonGenericAdd operator +(INonGenericAdd left, INonGenericAdd right);
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitOperatorFixture",
                    "INonGenericAdd.op_Addition",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("INonGenericAdd_op_Addition", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("INonGenericAdd.op_Addition(", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_RoundTripsExplicitInterfaceOpPrefixedNonOperatorMethod()
    {
        // Close positive case for the operator discriminator (#3112, adversarial review):
        // a method whose metadata name merely starts with `op_` but is NOT a recognized
        // operator (OperatorNames.FormatDisplayName returns it unchanged, and the printer
        // renders it as a plain `int op_Custom()` member) must still reconstruct as an
        // explicit implementation and round-trip Exact. The operator fallback must key off
        // recognized-operator rendering, not the bare `op_` prefix, so it does not
        // over-trigger here.
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitOpNameFixture : IHasOpName
            {
                int IHasOpName.op_Custom()
                {
                    return 42;
                }
            }

            public interface IHasOpName
            {
                int op_Custom();
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitOpNameFixture",
                    "IHasOpName.op_Custom",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains("int IHasOpName.op_Custom()", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("IHasOpName_op_Custom", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExplicitInterfaceDefaultMethodFallsBackToPlainWithoutRecompileFail()
    {
        // Negative case (#3112, adversarial review): an explicit-interface implementation of a
        // default interface method (virtual, non-abstract, has a body) cannot be reconstructed
        // by the explicit-method path — the interface member reconstructs bodyless
        // (StubBody.None) while remaining `virtual`, which is invalid because a non-abstract
        // virtual interface method requires a body (CS0501). RTS must fall back to the plain
        // sanitized shape (main's behavior) rather than regress the method-not-found ContextFail
        // into a RecompileFail.
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitDimFixture : IDefaultMethod
            {
                int IDefaultMethod.Compute()
                {
                    return 1;
                }
            }

            public interface IDefaultMethod
            {
                int Compute() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitDimFixture",
                    "IDefaultMethod.Compute",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("IDefaultMethod_Compute", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("int IDefaultMethod.Compute(", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_ExplicitStaticVirtualInterfaceMethodFallsBackToPlainWithoutRecompileFail()
    {
        // Negative case (#3112, adversarial review): an explicit-interface implementation of a
        // C# 11 `static virtual` interface method (has a body, non-abstract) cannot be
        // reconstructed by the explicit-method path — the interface member reconstructs bodyless
        // and non-abstract, which is invalid because a non-abstract interface method requires a
        // body (CS0501). The body/abstract discriminator must key off the declaration's Abstract
        // flag directly: `static virtual` methods carry Virtual without NewSlot, so the narrower
        // IsVirtualMethod helper (which requires NewSlot) would miss them and let them through.
        // RTS must fall back to the plain sanitized shape (main's behavior) rather than regress
        // the method-not-found ContextFail into a RecompileFail.
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitStaticVirtualFixture : IStaticVirtual
            {
                static void IStaticVirtual.Test()
                {
                    System.Console.WriteLine("test");
                }
            }

            public interface IStaticVirtual
            {
                static virtual void Test()
                {
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitStaticVirtualFixture",
                    "IStaticVirtual.Test",
                    0)]));

            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("IStaticVirtual_Test", result.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("void IStaticVirtual.Test(", result.Source, StringComparison.Ordinal);
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

    /// <summary>
    /// Gates the invariant the compile-back floor's safety argument rests on (#3783):
    /// fault isolation is never produced for a source member with no authored body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bodyless source member is exactly the input that drives
    /// <c>ReturnToSenderSourceProbe.AddBodylessSourceResult</c>, which is a second
    /// producer of <see cref="ReturnToSenderSourceOutcome.Invalid"/> reachable with a
    /// successful floor status. That path could only contaminate `invalidBreakdown`
    /// if such a row could also carry attribution — and it cannot, because the
    /// producer below requires an authored body while the bodyless path requires the
    /// absence of one. The two are mutually exclusive on the same index lookup.
    /// </para>
    /// <para>
    /// This test isolates that guard: the request and assembly are real, and only the
    /// source index is substituted so the lookup succeeds with a null body. Without
    /// the substitution a miss would return null for the wrong reason and prove nothing.
    /// </para>
    /// <para>
    /// This gates the common path only. Isolation resolves its source member with
    /// <c>CorpusMethodIdentity.SignatureText</c> while the index is keyed by
    /// <c>SignatureIdentity</c>, so its signature lookup always misses and falls
    /// back to the ordinal (#3804). Where those disagree the two sides can select
    /// different overloads, and this invariant does not cover that case. The floor
    /// clearing does not depend on it either way.
    /// </para>
    /// </remarks>
    [Fact]
    public void TryIsolateRecompileFailure_ReturnsNullWhenTheSourceMemberHasNoAuthoredBody()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("Bodyless.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            var bodyless = ReturnToSenderSourceIndex.FromMembers(
            [
                new ReturnToSenderSourceMember("Class1", "M", 0, "", sourcePath, Body: null),
            ]);

            // Same target and same rejected body that yields BodyDefect against a
            // real index; only the authored body is absent.
            Assert.Null(TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                sourceIndexOverride: bodyless));

            Assert.NotNull(TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;"));
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
        string rejectedTargetBody,
        ReturnToSenderSourceIndex? sourceIndexOverride = null)
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
        var sourceIndex = sourceIndexOverride ?? ReturnToSenderSourceIndex.TryCreate([sourcePath]);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable,
            allowUnsafe: true);
        var references = RoslynTestReferences.TrustedPlatform.ToArray();

        var decompiledArtifact = CompileBackSourceComposer.Compose(request);
        var decompiledTree = CSharpSyntaxTree.ParseText(decompiledArtifact.Source, parseOptions);
        var decompiledDiagnostics = CSharpCompilation
            .Create("return-to-sender-decompiled", [decompiledTree], references, compileOptions)
            .GetDiagnostics();

        return ReturnToSender.TryIsolateRecompileFailure(
            request,
            decompiledArtifact.Source,
            decompiledDiagnostics,
            sourceIndex,
            parseOptions,
            compileOptions,
            references);
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
                    Assert.Contains("public string Name { get; init; }", toString.Source);
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

    [Fact]
    public void CompileBackTargets_FullExplicitInterfaceEventTargetDoesNotDoubleDeclare()
    {
        // Issue #3007 follow-up (PR #3075 review): the Full member surface folds events by the
        // sanitized full metadata name ("IBaseEvents.Changed") while an explicit-interface event
        // target requirement carries the stripped identity ("Changed"). Enabling the surface for
        // such a target missed the fold and appended a SECOND `event Action IBaseEvents.Changed`
        // with a `throw null` accessor (CS8646/CS0102) while still reporting BodyComplete=true — a
        // double-declaration false success. Declining the surface for explicit-interface targets
        // restores the pre-#3007 single-accessor shape and the honest incomplete floor.
        var assemblyPath = CompileFixture("""
            using System;

            public interface IBaseEvents
            {
                event Action Changed;
            }

            public sealed class ExplicitEventFixture : IBaseEvents
            {
                private Action? _changed;

                event Action IBaseEvents.Changed
                {
                    add { _changed += value; }
                    remove { _changed -= value; }
                }
            }
            """);
        try
        {
            var result = Assert.Single(ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("ExplicitEventFixture", "IBaseEvents.add_Changed", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            // Exactly one explicit-interface event declaration; no appended duplicate.
            var declarations = result.Source.Split("event Action IBaseEvents.Changed").Length - 1;
            Assert.True(
                declarations == 1,
                $"Expected a single explicit-interface event declaration, found {declarations}.{Environment.NewLine}{result.Source}");
            Assert.DoesNotContain("throw null", result.Source, StringComparison.Ordinal);

            // The sibling remover and the constructor are not represented under the declined
            // surface, so BodyComplete is honestly false rather than an inflated double-declaration
            // success (coherent explicit-interface reconstruction is out of #3007's scope).
            Assert.False(
                result.BodyComplete,
                string.Join(Environment.NewLine, result.FullBodies.Select(body => $"{body.Member}: {body.Status}: {body.Failure}")));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_FullFieldLikeEventTargetStaysMethodRouted()
    {
        // Issue #3007 follow-up (PR #3075 review): a field-like event (`event Action Changed;`)
        // has a compiler-generated backing field whose name equals the event. Routing its accessor
        // through the Full member surface would emit that backing field as a separate
        // `Action Changed;` next to the reconstructed event (CS0102/CS0229). Coherent field-like
        // reconstruction is out of #3007's scope, so field-like accessors are excluded from the
        // Full broadening and stay method-routed exactly as before this PR — the surface's coherent
        // single-event shape must NOT be produced for them.
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
                [new ReturnToSender.RequestedTarget("OrdinaryEventFixture", "add_Changed", 0)],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            // Method-routed: the standalone accessor method is present (the pre-#3007 baseline
            // shape). The coherent event surface would instead fold both accessors into a single
            // `event { add remove }` with no standalone method, which is what this exclusion
            // deliberately avoids for field-like events.
            Assert.Contains("void add_Changed", result.Source, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    // Emits a loadable IL-only assembly containing a single-member public interface whose type
    // definition carries [CompilerFeatureRequired("<unknown feature>")]. The attribute is not
    // authorable from C# source (CS8335 even when self-defined), so it is written directly as
    // metadata: a TypeReference to the real corelib CompilerFeatureRequiredAttribute, a
    // MemberReference to its (string) constructor, and a CustomAttribute naming an unknown feature
    // with IsOptional defaulting to false — the shape a downlevel/hand-authored producer emits and
    // that raises CS9041 when a consumer binds to the interface.
    static byte[] BuildCompilerFeatureRequiredInterfaceImage(
        string assemblyName,
        string namespaceName,
        string typeName,
        string methodName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.None);

        var coreLib = typeof(object).Assembly.GetName();
        var coreLibRef = metadata.AddAssemblyReference(
            metadata.GetOrAddString(coreLib.Name!),
            coreLib.Version!,
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(coreLib.GetPublicKeyToken()!),
            flags: default,
            hashValue: default);
        var cfrTypeRef = metadata.AddTypeReference(
            coreLibRef,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerFeatureRequiredAttribute"));
        var cfrCtorSig = new BlobBuilder();
        cfrCtorSig.WriteByte(0x20); // HASTHIS, default calling convention
        cfrCtorSig.WriteCompressedInteger(1); // one parameter
        cfrCtorSig.WriteByte(0x01); // return type: void
        cfrCtorSig.WriteByte(0x0e); // parameter type: string
        var cfrCtorRef = metadata.AddMemberReference(
            cfrTypeRef,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(cfrCtorSig));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var interfaceHandle = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString(namespaceName),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var methodSig = new BlobBuilder();
        methodSig.WriteByte(0x20); // HASTHIS, default calling convention
        methodSig.WriteCompressedInteger(0); // no parameters
        methodSig.WriteByte(0x01); // return type: void
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(methodName),
            metadata.GetOrAddBlob(methodSig),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(0x0001); // custom attribute prolog
        attributeValue.WriteSerializedString("TotallyUnknownFeature");
        attributeValue.WriteUInt16(0); // zero named arguments
        metadata.AddCustomAttribute(
            interfaceHandle,
            cfrCtorRef,
            metadata.GetOrAddBlob(attributeValue));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    // Hand-authored IL exercising the shadowing-sibling regression: a class with a clean
    // explicit-interface metadata name for System.Collections.IEnumerable.GetEnumerator plus
    // a sibling type `N.System` in the same namespace. Assembled with ilasm because no C#
    // compiler can produce a clean explicit-override name alongside an in-scope shadow.
    const string ShadowingSiblingIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly shadowrepro { }
        .module shadowrepro.dll

        .namespace N
        {
          .class public auto ansi sealed beforefieldinit Seq
              extends [System.Runtime]System.Object
              implements [System.Runtime]System.Collections.IEnumerable
          {
            .method private hidebysig newslot virtual final
                instance class [System.Runtime]System.Collections.IEnumerator
                'System.Collections.IEnumerable.GetEnumerator'() cil managed
            {
              .override [System.Runtime]System.Collections.IEnumerable::GetEnumerator
              ldnull
              throw
            }
            .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
            {
              ldarg.0
              call instance void [System.Runtime]System.Object::.ctor()
              ret
            }
          }

          .class public auto ansi sealed beforefieldinit System
              extends [System.Runtime]System.Object
          {
            .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
            {
              ldarg.0
              call instance void [System.Runtime]System.Object::.ctor()
              ret
            }
          }
        }
        """;

    // External contract for the keyword-namespace shadow regression: an interface in a
    // namespace whose segment is the C# keyword `class` (raw metadata `class.IProbe`).
    const string KeywordContractsIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly KeywordContracts { }
        .module KeywordContracts.dll

        .class interface public abstract auto ansi 'class'.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void M() cil managed {}
        }
        """;

    // Target for the keyword-namespace shadow regression: N.Seq explicitly implements the
    // external `class.IProbe` with a clean metadata override name (`class.IProbe.M`), and a
    // sibling type N.'class' shadows the `class` root of the spelling once reconstructed.
    const string KeywordShadowFixtureIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly extern KeywordContracts { }
        .assembly keywordfixture { }
        .module keywordfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [KeywordContracts]'class'.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'class.IProbe.M'() cil managed
          {
            .override [KeywordContracts]'class'.IProbe::M
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }

        .class public auto ansi sealed beforefieldinit N.'class'
            extends [System.Runtime]System.Object
        {
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the unrepresentable-name regression: an interface whose namespace
    // segment is a compiler-unspeakable name (`<Bad>`) — legal in metadata, not a legal C#
    // identifier — so Clean() sanitizes it lossily to a different name (`__Bad_`).
    const string UnrepresentableContractsIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly GeneratedContracts { }
        .module GeneratedContracts.dll

        .class interface public abstract auto ansi '<Bad>'.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void M() cil managed {}
        }
        """;

    // Target for the unrepresentable-name regression: N.Seq explicitly implements the external
    // `<Bad>.IProbe`. The reconstruction would emit the sanitized `__Bad_.IProbe`, which names
    // no real type (CS0246) — the gate must decline to the sanitized ContextFail floor instead.
    const string UnrepresentableFixtureIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly extern GeneratedContracts { }
        .assembly badfixture { }
        .module badfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [GeneratedContracts]'<Bad>'.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void '<Bad>.IProbe.M'() cil managed
          {
            .override [GeneratedContracts]'<Bad>'.IProbe::M
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the unspeakable-member regression: an interface with a legal name
    // (`Good.IProbe`) but a method whose metadata name is compiler-unspeakable (`<Bad>`).
    const string UnspeakableMemberContractsIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly BadMethodContracts { }
        .module BadMethodContracts.dll

        .class interface public abstract auto ansi Good.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void '<Bad>'() cil managed {}
        }
        """;

    // Target for the unspeakable-member regression: N.Seq explicitly implements the external
    // `Good.IProbe.<Bad>`. The reconstruction would emit `Good.IProbe.__Bad_()`, which binds
    // to no interface member (CS0539) — the gate must decline to the sanitized ContextFail
    // floor instead.
    const string UnspeakableMemberFixtureIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly extern BadMethodContracts { }
        .assembly badmethodfixture { }
        .module badmethodfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [BadMethodContracts]Good.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'Good.IProbe.<Bad>'() cil managed
          {
            .override [BadMethodContracts]Good.IProbe::'<Bad>'
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the format-character regressions: the `%ZWNJ%` placeholder is
    // replaced with U+200C (a Unicode format character) at test time. Roslyn strips format
    // characters when binding identifiers, so a member name `M\u200C` binds as `M` — a name
    // that is identifier-like yet does not round-trip. Interface variant: namespace `G\u200Cood`.
    const string CfMemberContractsIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly CfContracts { }
        .module CfContracts.dll

        .class interface public abstract auto ansi Good.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void 'M%ZWNJ%'() cil managed {}
        }
        """;

    const string CfMemberFixtureIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly extern CfContracts { }
        .assembly cffixture { }
        .module cffixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [CfContracts]Good.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'Good.IProbe.M%ZWNJ%'() cil managed
          {
            .override [CfContracts]Good.IProbe::'M%ZWNJ%'
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    const string CfNamespaceContractsIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly CfNsContracts { }
        .module CfNsContracts.dll

        .class interface public abstract auto ansi 'G%ZWNJ%ood'.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void M() cil managed {}
        }
        """;

    const string CfNamespaceFixtureIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly extern CfNsContracts { }
        .assembly cfnsfixture { }
        .module cfnsfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [CfNsContracts]'G%ZWNJ%ood'.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'G%ZWNJ%ood.IProbe.M'() cil managed
          {
            .override [CfNsContracts]'G%ZWNJ%ood'.IProbe::M
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the decomposed-identifier (non-NFC) regression: the `%COMB%`
    // placeholder is replaced with U+0301 (combining acute accent) at test time, so the member
    // name is `e` + U+0301 — identifier-like, format-character-free, and NOT in NFC. Roslyn binds
    // it verbatim (no normalization), so it round-trips Exact and must NOT be declined.
    const string NfcMemberContractsIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly NfcContracts { }
        .module NfcContracts.dll

        .class interface public abstract auto ansi Good.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void 'e%COMB%'() cil managed {}
        }
        """;

    const string NfcMemberFixtureIl = """
        .assembly extern System.Runtime { .ver 0:0:0:0 }
        .assembly extern NfcContracts { }
        .assembly nfcfixture { }
        .module nfcfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [NfcContracts]Good.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'Good.IProbe.e%COMB%'() cil managed
          {
            .override [NfcContracts]Good.IProbe::'e%COMB%'
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // Locates a usable ilasm: an ILASM_PATH override, then PATH, then the restored
    // runtime.<rid>.microsoft.netcore.ilasm NuGet package cache. Returns null when none is
    // available so the caller can skip.
    static string? TryLocateIlasm()
    {
        string exe = OperatingSystem.IsWindows() ? "ilasm.exe" : "ilasm";

        var overridePath = Environment.GetEnvironmentVariable("ILASM_PATH");
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            return overridePath;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
                continue;
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
                return candidate;
        }

        var nuget = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        if (Directory.Exists(nuget))
        {
            foreach (var pkg in Directory.EnumerateDirectories(nuget)
                .Where(d => Path.GetFileName(d).Contains("microsoft.netcore.ilasm", StringComparison.OrdinalIgnoreCase)))
            {
                var hit = Directory.EnumerateFiles(pkg, exe, SearchOption.AllDirectories).FirstOrDefault();
                if (hit is not null)
                    return hit;
            }
        }

        return null;
    }

    static string AssembleIlFixture(string ilasm, string il, string directory, string assemblyName)
    {
        Directory.CreateDirectory(directory);
        var ilPath = Path.Combine(directory, assemblyName + ".il");
        var dllPath = Path.Combine(directory, assemblyName + ".dll");
        File.WriteAllText(ilPath, il);

        var psi = new System.Diagnostics.ProcessStartInfo(ilasm)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory,
        };
        psi.ArgumentList.Add(ilPath);
        psi.ArgumentList.Add("-dll");
        psi.ArgumentList.Add("-output=" + dllPath);

        using var process = System.Diagnostics.Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            File.Exists(dllPath),
            $"ilasm did not produce an assembly:{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        return dllPath;
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
