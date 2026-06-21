using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An IL `switch` opcode the switch-raising pass could not lift into a structured
// `switch` stays in the tree as a SwitchBranch jump table. The printer must
// render it as valid lowered C# — a `switch` block with one terminating case per
// target — not the placeholder `switch (...) goto [..]` form, which is not legal
// C# (CS1513/CS1514). Out-of-range values fall through, matching the opcode.
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
        Assert.Contains("switch (x)", output);
        Assert.Contains("case 0:\n        goto IL_0010;\n        break;", output);
        Assert.Contains("case 1:\n        goto IL_0020;\n        break;", output);
        Assert.Contains("case 2:\n        goto IL_0030;\n        break;", output);
    }

    [Fact]
    public void DuplicateTargets_EmitOnePerCaseIndex()
    {
        // IL switch tables routinely repeat a target (a shared fall-through); each
        // case index still needs its own arm.
        var result = RenderSwitch(0x10, 0x20, 0x10);
        var output = result.Output ?? "";

        Assert.Contains("case 0:\n        goto IL_0010;\n        break;", output);
        Assert.Contains("case 1:\n        goto IL_0020;\n        break;", output);
        Assert.Contains("case 2:\n        goto IL_0010;\n        break;", output);
    }
}
