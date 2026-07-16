using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

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
    [InlineData("Fib")]
    [InlineData("ParseOrNegativeOne")]
    public void FixtureMethod_RoundtripsWithOpcodeEquality(string methodName)
    {
        using var stream = File.OpenRead(FixtureAssembly);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var method = IlasmScaffold.FindMethod(reader, nameof(RoundtripFixtures), methodName);
        var original = MetadataInstructionProducer.Disassemble(peReader, reader, method);
        Assert.NotNull(original);

        string il = IlasmScaffold.BuildCompilationUnit(peReader, reader, method);
        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, $"Assembly failed:\n{result.Describe()}\n--- input ---\n{il}");

        var roundtripped = IlasmScaffold.DisassembleByName(result.Image!, methodName, nameof(RoundtripFixtures));
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
    public void HandwrittenPrefixAndTypedOperandCanaries_RoundtripWithOpcodeEquality()
    {
        string il = """
            .assembly extern System.Runtime { }
            .assembly roundtrip { }
            .class public auto ansi MiniPrefixes
            {
              .method public hidebysig static int32 VolatileLoad(int32&) cil managed
              {
                .maxstack 1
                IL_0000: ldarg.0
                IL_0001: volatile.
                IL_0003: ldind.i4
                IL_0004: ret
              }

              .method public hidebysig static string ConstrainedToString(int32) cil managed
              {
                .maxstack 1
                .locals init (int32 V_0)
                IL_0000: ldarg.0
                IL_0001: stloc.0
                IL_0002: ldloca.s 0
                IL_0004: constrained. [System.Runtime]System.Int32
                IL_000A: callvirt     instance string [System.Runtime]System.Object::ToString()
                IL_000F: ret
              }

              .method public hidebysig static int32 ReadonlyFirst(int32[]) cil managed
              {
                .maxstack 2
                IL_0000: ldarg.0
                IL_0001: ldc.i4.0
                IL_0002: readonly.
                IL_0004: ldelema      [System.Runtime]System.Int32
                IL_0009: ldind.i4
                IL_000A: ret
              }
            }
            """;

        AssertHandwrittenMethodRoundtrips(il, "MiniPrefixes", "VolatileLoad",
            ["ldarg.0", "volatile.", "ldind.i4", "ret"]);
        AssertHandwrittenMethodRoundtrips(il, "MiniPrefixes", "ConstrainedToString",
            ["ldarg.0", "stloc.0", "ldloca.s", "constrained.", "callvirt", "ret"]);
        AssertHandwrittenMethodRoundtrips(il, "MiniPrefixes", "ReadonlyFirst",
            ["ldarg.0", "ldc.i4.0", "readonly.", "ldelema", "ldind.i4", "ret"]);
    }

    [Fact]
    public void HandwrittenBlockOperationCanaries_RoundtripWithOpcodeEquality()
    {
        string il = """
            .assembly roundtrip { }
            .class public auto ansi MiniBlockOps
            {
              .method public hidebysig static void InitBlock(uint8&, uint8, int32) cil managed
              {
                .maxstack 3
                IL_0000: ldarg.0
                IL_0001: ldarg.1
                IL_0002: ldarg.2
                IL_0003: initblk
                IL_0005: ret
              }

              .method public hidebysig static void CopyBlock(uint8&, uint8&, int32) cil managed
              {
                .maxstack 3
                IL_0000: ldarg.0
                IL_0001: ldarg.1
                IL_0002: ldarg.2
                IL_0003: cpblk
                IL_0005: ret
              }
            }
            """;

        AssertHandwrittenMethodRoundtrips(il, "MiniBlockOps", "InitBlock",
            ["ldarg.0", "ldarg.1", "ldarg.2", "initblk", "ret"]);
        AssertHandwrittenMethodRoundtrips(il, "MiniBlockOps", "CopyBlock",
            ["ldarg.0", "ldarg.1", "ldarg.2", "cpblk", "ret"]);
    }

    [Fact]
    public void HandwrittenFilterAndFaultCanaries_RoundtripWithRegionShape()
    {
        string il = """
            .assembly extern System.Runtime { }
            .assembly roundtrip { }
            .class public auto ansi MiniEHShapes
            {
              .method public hidebysig static int32 Filtered() cil managed
              {
                .maxstack 2
                .locals init (int32 V_0)
                IL_0000: ldc.i4.0
                IL_0001: stloc.0
                IL_0002: leave.s      IL_0011
                IL_0004: isinst       [System.Runtime]System.Exception
                IL_0009: ldnull
                IL_000A: cgt.un
                IL_000C: endfilter
                IL_000E: pop
                IL_000F: ldc.i4.1
                IL_0010: stloc.0
                IL_0011: ldloc.0
                IL_0012: ret
                .try IL_0000 to IL_0004 filter IL_0004 handler IL_000E to IL_0011
              }

              .method public hidebysig static int32 Faulted() cil managed
              {
                .maxstack 1
                .locals init (int32 V_0)
                IL_0000: ldc.i4.0
                IL_0001: stloc.0
                IL_0002: leave.s      IL_0009
                IL_0004: ldc.i4.1
                IL_0005: stloc.0
                IL_0006: endfinally
                IL_0009: ldloc.0
                IL_000A: ret
                .try IL_0000 to IL_0004 fault handler IL_0004 to IL_0009
              }
            }
            """;

        var filtered = AssertHandwrittenMethodRoundtrips(il, "MiniEHShapes", "Filtered",
            ["ldc.i4.0", "stloc.0", "leave.s", "isinst", "ldnull", "cgt.un", "endfilter", "pop", "ldc.i4.1", "stloc.0", "ldloc.0", "ret"]);
        Assert.Contains(filtered.ExceptionRegions, r => r.Kind == ExceptionRegionKind.Filter);

        var faulted = AssertHandwrittenMethodRoundtrips(il, "MiniEHShapes", "Faulted",
            ["ldc.i4.0", "stloc.0", "leave.s", "ldc.i4.1", "stloc.0", "endfinally", "ldloc.0", "ret"]);
        Assert.Contains(faulted.ExceptionRegions, r => r.Kind == ExceptionRegionKind.Fault);
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

    [Theory]
    // Switch targets are relative to the instruction *following* the switch
    // (ECMA-335 III.3.66), and this switch has two entries (13-byte encoding:
    // opcode + count + 2 x int32), so the real post-switch offset is 14, not
    // the cosmetic IL_000A label. `2` and `4` are the relative offsets that
    // land on IL_000C/IL_000E's actual assembled positions -- verified to
    // produce byte-identical bytecode to the label-list form below.
    [InlineData("switch (2, 4)")]                    // integer offsets
    [InlineData("switch (IL_000C, IL_000E)")]       // label list (vendor grammar fix)
    public void Switch_Assembles(string switchLine)
    {
        string il = $$"""
            .assembly roundtrip { }
            .class public auto ansi MiniSwitch
            {
              .method public hidebysig static int32 M(int32 'x') cil managed
              {
                .maxstack 1
                IL_0000: ldarg.0
                IL_0001: {{switchLine}}
                IL_000A: ldc.i4.0
                IL_000B: ret
                IL_000C: ldc.i4.1
                IL_000D: ret
                IL_000E: ldc.i4.2
                IL_000F: ret
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
    public void QuotedDottedNameSegment_AssemblyExtern_Assembles()
    {
        // Names outside the identifier charset use ECMA-335 quoted segments
        // (vendor grammar fix: dottedName gained the SQSTRING alternative).
        string il = """
            .assembly extern xunit.v3.'mtp-v1' { }
            .assembly roundtrip { }
            .class public auto ansi MiniQuoted
            {
              .method public hidebysig static void M() cil managed
              {
                .maxstack 8
                ret
              }
            }
            """;

        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, result.Describe());

        var reader = result.Image!.GetMetadataReader();
        var names = reader.AssemblyReferences
            .Select(h => reader.GetString(reader.GetAssemblyReference(h).Name))
            .ToList();
        Assert.Contains("xunit.v3.mtp-v1", names);
    }

    [Fact]
    public void DottedTypeName_SelfReferentialFieldRef_Assembles()
    {
        // Pins the vendor-branch fix for dotted typedef registration: the class
        // head used to register `Some.Dotted.MiniB` with name ".MiniB", so member
        // refs back to it failed with ILA0008.
        string il = """
            .assembly roundtrip { }
            .class public auto ansi Some.Dotted.MiniB
            {
              .field private static int32 f
              .method public hidebysig static int32 M() cil managed
              {
                .maxstack 1
                ldsfld int32 Some.Dotted.MiniB::f
                ret
              }
            }
            """;

        var result = IlasmScaffold.Assemble(il);
        Assert.True(result.Succeeded, result.Describe());

        var reader = result.Image!.GetMetadataReader();
        var typeDef = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(t => reader.GetString(t.Name) == "MiniB");
        Assert.Equal("Some.Dotted", reader.GetString(typeDef.Namespace));

        var ops = IlasmScaffold.DisassembleByName(result.Image!, "M")!
            .Select(i => i.OpCodeName).ToList();
        Assert.Equal(["ldsfld", "ret"], ops);
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

    static MethodBodyBlock AssertHandwrittenMethodRoundtrips(string il, string typeName, string methodName, IReadOnlyList<string> expectedOps)
    {
        var source = IlasmScaffold.Assemble(il);
        Assert.True(source.Succeeded, $"Source assembly failed:\n{source.Describe()}\n--- input ---\n{il}");

        var sourceImage = source.Image!;
        var reader = sourceImage.GetMetadataReader();
        var method = IlasmScaffold.FindMethod(reader, typeName, methodName);
        var original = MetadataInstructionProducer.Disassemble(sourceImage, reader, method);
        Assert.NotNull(original);
        Assert.Equal(expectedOps, original.Select(i => i.OpCodeName).ToList());

        string scaffold = IlasmScaffold.BuildCompilationUnit(sourceImage, reader, method);
        var result = IlasmScaffold.Assemble(scaffold);
        Assert.True(result.Succeeded, $"Round-trip assembly failed:\n{result.Describe()}\n--- input ---\n{scaffold}");

        var roundtripped = IlasmScaffold.DisassembleByName(
            result.Image!, methodName, typeName, IlasmScaffold.ParamTypes(method));
        Assert.NotNull(roundtripped);

        var originalOps = original.Select(i => IlasmScaffold.CanonicalOpcode(i.OpCodeName)).ToList();
        var roundtrippedOps = roundtripped.Select(i => IlasmScaffold.CanonicalOpcode(i.OpCodeName)).ToList();
        Assert.Equal(originalOps, roundtrippedOps);

        var resultImage = result.Image!;
        return resultImage.GetMethodBody(
            IlasmScaffold.FindMethod(resultImage.GetMetadataReader(), typeName, methodName).RelativeVirtualAddress);
    }
}
