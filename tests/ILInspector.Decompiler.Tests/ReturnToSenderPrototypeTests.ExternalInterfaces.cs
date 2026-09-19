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
    public async Task CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenClosureSiblingShadowsSpelling()
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
                await ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.Cluster));
            Assert.True(
                cluster.Status == FidelityCheck.CompileBackStatus.Exact,
                $"cluster {cluster.Status}: {cluster.Detail}");

            // All reconstructs N.System, which shadows the `System` root of the spelling.
            // The gate must decline to the sanitized shape (the pre-#3112 ContextFail floor)
            // rather than emit a new RecompileFail (CS0426). Strictly better or identical,
            // never worse.
            var all = Assert.Single(
                await ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenKeywordNamespaceSiblingShadowsSpelling()
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
                await ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenMemberNameIsUnrepresentable()
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
                await ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenMemberNameHasFormatCharacter()
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
                await ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenNamespaceHasFormatCharacter()
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
                await ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceKeepsExactWhenMemberNameIsDecomposed()
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

            var all = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceDeclinesWhenNameIsUnrepresentable()
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
                await ReturnToSender.CompileBackTargets(assemblyPath, [target], RoundTripScope.All));
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
    public async Task CompileBackTargets_RoundTripsExternalMultiMemberExplicitInterfaceMethod()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_MultiMemberExternalExplicitInterfaceWithUnspellableSiblingFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_MultiMemberExternalExplicitInterfaceWithOverloadedSiblingsRoundTrips()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_InheritedBaseInterfaceMemberFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_NestedExternalMultiMemberInterfaceFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_GenericExternalExplicitInterfaceFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithSignatureDriftFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithAmbiguousDefinitionFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithByRefKindDriftFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithVarArgsFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithInternalInterfaceFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithInternalMemberFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithObsoleteErrorInterfaceFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithCompilerFeatureRequiredInterfaceFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithGenericParameterDriftFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExternalExplicitInterfaceWithConstraintOnlyGenericFallsBackWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_RoundTripsGenericExplicitInterfaceMethod()
    {
        // A generic method on a non-generic interface implemented explicitly keeps its method
        // type parameters in the reconstructed `IBox.Wrap<T>(...)` header.
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_RoundTripsNullableGenericExplicitInterfaceMethod()
    {
        var assemblyPath = CompileFixture("""
            #nullable enable

            public sealed class ExplicitNullableGenericFixture : INullableBox
            {
                T? INullableBox.Wrap<T>(T? value) where T : class
                {
                    return value;
                }
            }

            public interface INullableBox
            {
                T? Wrap<T>(T? value) where T : class;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitNullableGenericFixture",
                    "INullableBox.Wrap",
                    0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.False(result.UsedCompileBackFloor, result.Detail);
            Assert.Contains(
                "T INullableBox.Wrap<T>(T value) where T : class",
                result.Source,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_NestedExplicitInterfaceMethodFallsBackToPlainWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_RoundTripsStaticAbstractExplicitInterfaceMethod()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_SelectsExplicitInterfaceOperatorBeforeLegacyFallback()
    {
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
            using (var stream = File.OpenRead(assemblyPath))
            using (var pe = new PEReader(stream))
            {
                ApiType type = Assert.Single(
                    ApiSurfaceExtractor.Extract(pe).Types,
                    candidate => candidate.Name == "ExplicitOperatorFixture");
                ApiMember member = Assert.Single(
                    type.Members,
                    candidate =>
                        candidate.Name == "INonGenericAdd.op_Addition");
                Assert.True(member.MethodModifiersAreRepresentable);
                Assert.True(member.MethodImplementationIsRepresentable);
                Assert.True(member.AccessibilityIsRepresentable);
                Assert.True(
                    CSharpMemberArtifactEligibility.IsRepresentable(
                        type,
                        member),
                    $"kind={member.Kind}; access={member.Accessibility}; "
                    + $"static={member.IsStatic}; abstract={member.IsAbstract}; "
                    + $"virtual={member.IsVirtual}; override={member.IsOverride}; "
                    + $"sealed={member.IsSealed}; arity={member.GenericArity}; "
                    + $"semantics={member.MethodSemantics}; "
                    + $"header={member.SignatureModel?.MethodDeclarationHeaderIsRepresentable}; "
                    + $"return={member.SignatureModel?.ReturnType}; "
                    + $"return-shape={member.SignatureModel?.ReturnTypeShape}; "
                    + $"parameters={member.SignatureModel?.Parameters.Count}; "
                    + $"parameter-modifiers={string.Join(",", member.SignatureModel?.Parameters.Select(parameter => parameter.Modifier) ?? [])}");
            }

            Assert.Contains(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue),
                target =>
                    target.Type == "ExplicitOperatorFixture"
                    && target.Method == "INonGenericAdd.op_Addition");

            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    "ExplicitOperatorFixture",
                    "INonGenericAdd.op_Addition",
                    0)]));
            Assert.True(
                result.Status != FidelityCheck.CompileBackStatus.RecompileFail,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains(
                "INonGenericAdd_op_Addition",
                result.Source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "INonGenericAdd.op_Addition(",
                result.Source,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_SelectsExplicitInterfaceConversionOperator()
    {
        var assemblyPath = CompileFixture("""
            public readonly struct ExplicitConversionFixture
                : IConversion<ExplicitConversionFixture>
            {
                static explicit IConversion<ExplicitConversionFixture>.operator int(
                    ExplicitConversionFixture value)
                {
                    return 1;
                }
            }

            public interface IConversion<TSelf>
                where TSelf : IConversion<TSelf>
            {
                static abstract explicit operator int(TSelf value);
            }
            """);
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            ApiType type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.Name == "ExplicitConversionFixture");
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name.EndsWith(
                    ".op_Explicit",
                    StringComparison.Ordinal));

            Assert.True(member.MethodModifiersAreRepresentable);
            Assert.True(member.MethodImplementationIsRepresentable);
            Assert.True(member.AccessibilityIsRepresentable);
            Assert.True(CSharpMemberArtifactEligibility.IsRepresentable(type, member));
            Assert.Contains(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue),
                target =>
                    target.Type == "ExplicitConversionFixture"
                    && target.Method == member.Name);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_SelectsKeywordQualifiedExplicitInterfaceOperator()
    {
        var assemblyPath = CompileFixture("""
            namespace @operator
            {
                public interface IAdd<TSelf>
                    where TSelf : IAdd<TSelf>
                {
                    static abstract TSelf operator +(TSelf left, TSelf right);
                }
            }

            public readonly struct KeywordOperatorFixture
                : @operator.IAdd<KeywordOperatorFixture>
            {
                static KeywordOperatorFixture
                    @operator.IAdd<KeywordOperatorFixture>.operator +(
                        KeywordOperatorFixture left,
                        KeywordOperatorFixture right)
                {
                    return left;
                }
            }
            """);
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            ApiType type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.Name == "KeywordOperatorFixture");
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name.EndsWith(
                    ".op_Addition",
                    StringComparison.Ordinal));

            Assert.True(member.MethodImplementationIsRepresentable);
            Assert.True(CSharpMemberArtifactEligibility.IsRepresentable(type, member));
            Assert.Contains(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue),
                target =>
                    target.Type == "KeywordOperatorFixture"
                    && target.Method == member.Name);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_SelectsReadonlyExplicitInterfaceMethod()
    {
        var assemblyPath = CompileFixture("""
            public struct ReadonlyExplicitFixture : IReadOnlyExplicit
            {
                readonly int IReadOnlyExplicit.Read()
                {
                    return 42;
                }
            }

            public interface IReadOnlyExplicit
            {
                int Read();
            }
            """);
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            ApiType type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.Name == "ReadonlyExplicitFixture");
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name == "IReadOnlyExplicit.Read");

            Assert.True(member.IsReadOnly);
            Assert.True(member.ReadOnlyMarkerIsRepresentable);
            Assert.True(member.MethodImplementationIsRepresentable);
            Assert.True(CSharpMemberArtifactEligibility.IsRepresentable(type, member));
            Assert.Contains(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue),
                target =>
                    target.Type == "ReadonlyExplicitFixture"
                    && target.Method == member.Name);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackTargets_PreservesOrdinaryExplicitOpPrefixedMethod()
    {
        var assemblyPath = CompileFixture("""
            public sealed class ExplicitOpPrefixedFixture : IOpPrefixed
            {
                static int IOpPrefixed.op_Addition(int left, int right)
                {
                    return left + right;
                }
            }

            public interface IOpPrefixed
            {
                static abstract int op_Addition(int left, int right);
            }
            """);
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            ApiType type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.Name == "ExplicitOpPrefixedFixture");
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name == "IOpPrefixed.op_Addition");

            Assert.Equal("explicit-interface-implementation", member.Kind);
            Assert.True(member.MethodImplementationIsRepresentable);
            Assert.True(CSharpMemberArtifactEligibility.IsRepresentable(type, member));
            Assert.Contains(
                FidelityCheck.SelectReturnToSenderTargets(
                    [assemblyPath],
                    cap: int.MaxValue),
                target =>
                    target.Type == "ExplicitOpPrefixedFixture"
                    && target.Method == member.Name);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RoundTripsExplicitInterfaceOpPrefixedNonOperatorMethod()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExplicitInterfaceDefaultMethodFallsBackToPlainWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackTargets_ExplicitStaticVirtualInterfaceMethodFallsBackToPlainWithoutRecompileFail()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackFirstPropertyGetter_RoundTripsExplicitInterfaceIndexer()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackFirstPropertyGetter_KeepsStaticOnExplicitInterfaceProperty()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackEventAccessor_RoundTripsExplicitInterfaceEvent(string accessorName)
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackEventAccessor_RaisesSiblingAccessorBodyInsteadOfThrowStub(
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackEventAccessor_KeepsStaticOnExplicitInterfaceEvent()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackEventAccessor_PreservesOrdinaryFieldLikeEventHandling()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackFirstPropertyGetter_PreservesRequiredImplicitInterfaceProperty()
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
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

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
    public async Task CompileBackFirstPropertyGetter_ProjectsPropertiesFromRequiredInterfaceSurface()
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
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
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
    public async Task CompileBackFirstPropertyGetter_ExposesTypedModuleAndTypeShellPlan()
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
            var result = await ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);
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
}
