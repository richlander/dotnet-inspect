using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Metadata;

namespace DotnetInspector.ILRoundtrip.Tests;

/// <summary>
/// Round-trip baseline: dotnet-inspect disassembly, wrapped in scaffolding, must
/// assemble cleanly with the vendored managed ILAssembler (vendor/ilassembler
/// branch) and decode back to the same opcode stream.
/// </summary>
public class ILAssemblerRoundtripTests
{
    static readonly string FixtureAssembly = typeof(RoundtripFixtures).Assembly.Location;

    [Theory]
    [InlineData("Add")]
    [InlineData("Max")]
    [InlineData("SumLoop")]
    [InlineData("IsGreater")]
    [InlineData("Identity")]
    [InlineData("Greet")]
    [InlineData("StringLength")]
    [InlineData("MakeList")]
    [InlineData("ParseOrNegativeOne")]
    public void FixtureMethod_RoundtripsWithOpcodeEquality(string methodName)
    {
        using var stream = File.OpenRead(FixtureAssembly);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var method = IlasmScaffold.FindMethod(reader, nameof(RoundtripFixtures), methodName);
        var original = ILDisassembler.Disassemble(peReader, reader, method);
        Assert.NotNull(original);

        string il = IlasmScaffold.BuildCompilationUnit(peReader, reader, method);
        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, $"Assembly failed:\n{result.Describe()}\n--- input ---\n{il}");

        var roundtripped = IlasmScaffold.DisassembleByName(result.Image!, methodName);
        Assert.NotNull(roundtripped);

        var originalOps = original.Select(i => IlasmScaffold.CanonicalOpcode(i.OpCodeName)).ToList();
        var roundtrippedOps = roundtripped.Select(i => IlasmScaffold.CanonicalOpcode(i.OpCodeName)).ToList();
        Assert.Equal(originalOps, roundtrippedOps);
    }

    [Fact]
    public void HandwrittenEH_RoundtripsWithCatchClause()
    {
        // Catch clause in label form; class name un-namespaced (ILAssembler does
        // not yet resolve member refs to dotted typedef names).
        string il = """
            .assembly extern System.Runtime { }
            .assembly roundtrip { }
            .class public auto ansi MiniEH
            {
              .method public hidebysig static int32 M(string 's') cil managed
              {
                .maxstack 2
                .locals init (int32 V_0)
                IL_0000: nop
                IL_0001: nop
                IL_0002: ldarg.0
                IL_0003: call         int32 [System.Runtime]System.Int32::Parse(string)
                IL_0008: stloc.0
                IL_0009: leave.s      IL_0011
                IL_000B: pop
                IL_000C: ldc.i4.m1
                IL_000D: stloc.0
                IL_000F: leave.s      IL_0011
                IL_0011: ldloc.0
                IL_0012: ret
                .try IL_0001 to IL_000B catch class [System.Runtime]System.FormatException handler IL_000B to IL_0011
              }
            }
            """;

        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, result.Describe());

        var reader = result.Image!.GetMetadataReader();
        var method = IlasmScaffold.FindMethod(reader, "MiniEH", "M");
        var body = result.Image!.GetMethodBody(method.RelativeVirtualAddress);

        var region = Assert.Single(body.ExceptionRegions);
        Assert.Equal(ExceptionRegionKind.Catch, region.Kind);
        Assert.True(region.HandlerOffset > region.TryOffset,
            $"handler ({region.HandlerOffset}) should start after try ({region.TryOffset})");
        Assert.True(region.TryLength > 0 && region.HandlerLength > 0);
    }

    [Fact]
    public void CanonicalCallOperand_Assembles()
    {
        // Pins the canonical member-ref syntax the disassembler needs to emit for
        // round-trips: return type + assembly qualifier + IL primitive names.
        string il = """
            .assembly extern System.Runtime { }
            .assembly roundtrip { }
            .class public auto ansi MiniCall
            {
              .method public hidebysig static string M(string 'name') cil managed
              {
                .maxstack 8
                IL_0000: ldstr        "Hello, "
                IL_0005: ldarg.0
                IL_0006: call         string [System.Runtime]System.String::Concat(string, string)
                IL_000B: ret
              }
            }
            """;

        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, result.Describe());

        var ops = IlasmScaffold.DisassembleByName(result.Image!, "M")!
            .Select(i => i.OpCodeName).ToList();
        Assert.Equal(["ldstr", "ldarg.0", "call", "ret"], ops);
    }

    [Fact]
    public void CanonicalGenericOperand_Assembles()
    {
        string il = """
            .assembly extern System.Runtime { }
            .assembly extern System.Collections { }
            .assembly roundtrip { }
            .class public auto ansi MiniGeneric
            {
              .method public hidebysig static class [System.Collections]System.Collections.Generic.List`1<int32> M(int32 'seed') cil managed
              {
                .maxstack 2
                .locals init (class [System.Collections]System.Collections.Generic.List`1<int32> V_0)
                IL_0000: newobj       instance void class [System.Collections]System.Collections.Generic.List`1<int32>::.ctor()
                IL_0005: stloc.0
                IL_0006: ldloc.0
                IL_0007: ldarg.0
                IL_0008: callvirt     instance void class [System.Collections]System.Collections.Generic.List`1<int32>::Add(!0)
                IL_000D: ldloc.0
                IL_000E: ret
              }
            }
            """;

        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, result.Describe());

        var ops = IlasmScaffold.DisassembleByName(result.Image!, "M")!
            .Select(i => i.OpCodeName).ToList();
        Assert.Equal(["newobj", "stloc.0", "ldloc.0", "ldarg.0", "callvirt", "ldloc.0", "ret"], ops);
    }

    [Fact]
    public void SwitchWithIntegerOffsets_Assembles()
    {
        // Comma-separated IL_xxxx label lists hit an upstream grammar gap; integer
        // offsets are the working form. If this starts failing after a vendor sync,
        // re-check the upstream `labels` grammar rule.
        string il = """
            .assembly roundtrip { }
            .class public auto ansi MiniSwitch
            {
              .method public hidebysig static int32 M(int32 'x') cil managed
              {
                .maxstack 1
                ldarg.0
                switch (12, 14)
                ldc.i4.0
                ret
                ldc.i4.1
                ret
                ldc.i4.2
                ret
              }
            }
            """;

        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, result.Describe());

        var ops = IlasmScaffold.DisassembleByName(result.Image!, "M")!
            .Select(i => i.OpCodeName).ToList();
        Assert.Contains("switch", ops);
    }

    [Fact]
    public void ParserErrors_AreDetectedByHarness()
    {
        // ILAssembler does not surface ANTLR syntax errors as diagnostics (upstream
        // gap) — it can return a "successful" image with the bad method silently
        // missing. The harness must catch them via captured stderr or these tests
        // could pass vacuously.
        string il = """
            .assembly roundtrip { }
            .class public auto ansi MiniBad
            {
              .method public hidebysig static void M() cil managed
              {
                .maxstack 8
                this is not valid IL at all
                ret
              }
            }
            """;

        var result = IlasmScaffold.Assemble(il);
        Assert.False(result.Succeeded);
        Assert.NotEqual("", result.ParserErrors);
    }
}
