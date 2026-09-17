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
{    [Fact]
    public async Task CompileBackTargets_AllFullReconstructsUnrelatedExplicitInterfaceEventAccessors()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_AllFullReconstructsUnrelatedEventAccessors()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullRejectsMultiplePrimaryTargets()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () => await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_AllFullReconstructsEveryConcreteMethodBody()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_AllFullReportsConcreteDeclarationItCannotRepresent()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullPreservesConcreteSiblingWhenTargetIsPropertyAccessor()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullPreservesAutoPropertyWhenTargetIsAutoAccessor()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullPreservesReadWriteAutoPropertyWhenTargetIsGetter()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullPreservesInitAccessorWhenTargetIsGetter()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullPreservesInitAccessorWhenTargetIsSetter()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullPreservesExplicitInitAccessorWhenTargetIsGetter()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_PreservesExplicitInterfaceInitProperty()
    {
        var assemblyPath = CompileFixture("""
            public interface IValue
            {
                int Value { get; init; }
            }

            public sealed class Holder : IValue
            {
                int IValue.Value { get; init; }
            }
            """);
        try
        {
            var target = new ReturnToSender.RequestedTarget(
                "Holder",
                "IValue.get_Value",
                0);
            var selected = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [target]));
            var full = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [target],
                RoundTripScope.All,
                RoundTripBodyPolicy.Full));

            Assert.All(
                [selected, full],
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.False(result.UsedCompileBackFloor, result.Detail);
                    Assert.Contains(
                        "int IValue.Value { get; init; }",
                        result.Source,
                        StringComparison.Ordinal);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_FullAutoInitPropertySiblingStaysSkeletonNotRecursive()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullAutoSetPropertySiblingStaysSkeletonNotRecursive()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullSuppressesStrayAutoPropertyBackingField()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullPreservesExplicitInitAccessorWhenTargetIsSetter()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullMemberSurfacePreservesSiblingInitAccessor()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_RecordSurfacePreservesSiblingInitAccessor()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_InterfaceSurfacePreservesInitAccessor()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullReconstructsPlainEventAccessorTarget()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_AllSeedsEverySupportedTopLevelRoot()
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
            var pair = await ReturnToSender.CompileBackScopes(assemblyPath, target);
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
            Assert.Null(cluster.FinalRequest!.CompilationClosure);
            Assert.Null(all.FinalRequest!.CompilationClosure);
            Assert.NotNull(cluster.DonorPe);
            Assert.NotNull(all.DonorPe);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.RecompileFail, all.Status);
            Assert.NotEqual(FidelityCheck.CompileBackStatus.ContextFail, all.Status);
            Assert.True(
                pair.Comparison.Status
                    == RoundTripScopeComparisonStatus.Completed,
                pair.Comparison.Failure);
            var comparison = Assert.Single(pair.Comparison.Members);
            Assert.Equal(RoundTripEvidenceStatus.Exact, comparison.CSharpStatus);
            Assert.Equal(IlBodyDiffOutcome.Exact, comparison.IlStatus);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }
}
