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
    public async Task CompileBackTargets_RoundTripsStructPropertyTargets()
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
            var results = await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_SurfacesStructNonAutoPropertyWithoutBackingField()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_RoundTripsStaticClassTargets()
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
            var results = await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_DoesNotDuplicateBodyBackedSetterDuringClosureSurface()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_EmitsGetterStubForSetterBodyThatReadsProperty()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackFirstPropertyGetter_SurfacesUnsafeNestedClosureMember()
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
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public async Task CompileBackTargets_DoesNotDuplicateSelfRecursiveTargetMethodDuringClosureSurface()
    {
        // Guard for the RTS-parity known-gap row TypeResolver::GetTypeNameFromReference:
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_PopulatesEnumMembersWhenTargetReferencesThemByName()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ReconstructsNonIntEnumUnderlyingTypeForMemberValues()
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
                var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullExplicitInterfaceEventTargetDoesNotDoubleDeclare()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_FullFieldLikeEventTargetStaysMethodRouted()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
}
