using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An IL `switch` opcode the switch-raising pass could not lift into a structured
// `switch` stays in the tree as a SwitchBranch jump table. The printer must
// render it as valid lowered C# — a single-evaluated temp plus one `if`/`goto`
// per target — not a C# `switch` whose cases goto labels outside the switch
// section (CS0159). Out-of-range values fall through, matching the opcode.
public class SwitchBranchRenderingTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static DecompilerResult RenderSwitch(params int[] targets)
    {
        var entry = new Block(0);
        entry.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [.. targets]));
        entry.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(entry);
        // Each target needs a labeled landing block so the labels are emitted.
        foreach (int target in targets.Distinct().OrderBy(t => t))
        {
            var block = new Block(target);
            block.Add(new Return(null));
            container.Add(block);
        }

        var signature = new MethodSignature(Void, [new Parameter("x", Int32)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("System", "Sample"), signature, [], container);
        return CSharpPrinter.Print(function);
    }

    [Fact]
    public void JumpTable_RendersValidCaseGotoBlock()
    {
        var result = RenderSwitch(0x10, 0x20, 0x30);
        var output = result.Output ?? "";

        Assert.DoesNotContain("goto [", output);
        Assert.Contains("int __dotnet_inspect_switch0 = default;", output);
        Assert.Contains("__dotnet_inspect_switch0 = (int)(x);", output);
        Assert.Contains("if (__dotnet_inspect_switch0 == 0) goto IL_0010;", output);
        Assert.Contains("if (__dotnet_inspect_switch0 == 1) goto IL_0020;", output);
        Assert.Contains("if (__dotnet_inspect_switch0 == 2) goto IL_0030;", output);
    }

    [Fact]
    public void DuplicateTargets_EmitOnePerCaseIndex()
    {
        // IL switch tables routinely repeat a target (a shared fall-through); each
        // case index still needs its own arm.
        var result = RenderSwitch(0x10, 0x20, 0x10);
        var output = result.Output ?? "";

        Assert.Contains("if (__dotnet_inspect_switch0 == 0) goto IL_0010;", output);
        Assert.Contains("if (__dotnet_inspect_switch0 == 1) goto IL_0020;", output);
        Assert.Contains("if (__dotnet_inspect_switch0 == 2) goto IL_0010;", output);
    }

    [Fact]
    public void Structuring_PreservesFlattenedSwitchTargetLabels()
    {
        // OperandLength has residual switch tables whose target blocks are also
        // comparison-tree return leaves. Structuring must keep those labels
        // available for the lowered if/goto switch rendering.
        using var source = MetadataSource.Open(typeof(IlProjection).Assembly.Location);
        var function = IrImporter.Import(source, typeof(IlProjection).FullName!, "OperandLength");
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        string output = result.Output ?? "";

        Assert.Contains("if (__dotnet_inspect_switch", output);
        Assert.Contains("goto IL_024F;", output);
        Assert.Contains("IL_024F:", output);
        Assert.DoesNotContain("case 0: goto", output);
    }
}
