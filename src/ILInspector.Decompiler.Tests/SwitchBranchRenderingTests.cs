using System.Collections.Immutable;
using System.Text.RegularExpressions;
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
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static void AssertGotoTargetsHaveLabels(string output)
    {
        var targets = Regex.Matches(output, @"goto (IL_[0-9A-F]+);")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Order()
            .ToList();
        var labels = Regex.Matches(output, @"(?m)^\s*(IL_[0-9A-F]+):\s*$")
            .Select(match => match.Groups[1].Value)
            .ToHashSet();
        var missing = targets.Where(target => !labels.Contains(target)).ToList();

        Assert.True(missing.Count == 0,
            $"goto targets without labels: {string.Join(", ", missing)}\n{output}");
    }

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

    [Fact]
    public void Structuring_PreservesLabelWhenSwitchTargetBecomesIfStatement()
    {
        var entry = new Block(0);
        entry.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [0x10]));

        var fallthrough = new Block(0x04);
        fallthrough.Add(new Return(null));

        var target = new Block(0x10);
        target.Add(new ConditionalBranch(new LoadArgument(1, "flag", Boolean), 0x14));

        var arm = new Block(0x12);
        arm.Add(new Return(null));

        var exit = new Block(0x14);
        exit.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        exit.Add(new Return(null));

        var container = new BlockContainer();
        foreach (var block in (Block[])[entry, fallthrough, target, arm, exit])
            container.Add(block);

        var signature = new MethodSignature(
            Void,
            [new Parameter("x", Int32), new Parameter("flag", Boolean)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Sample"),
            signature,
            [Int32],
            container);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("if (__dotnet_inspect_switch0 == 0) goto IL_0010;", output);
        Assert.DoesNotContain("goto IL_0014;", output);
        Assert.Contains("IL_0010:", output);
    }

    [Fact]
    public void Structuring_PreservesLabelOutsideTargetedInfiniteLoop()
    {
        var entry = new Block(0);
        entry.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [0x10]));

        var fallthrough = new Block(0x04);
        fallthrough.Add(new Return(null));

        var loopHead = new Block(0x10);
        loopHead.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));

        var latch = new Block(0x14);
        latch.Add(new Branch(0x10));

        var container = new BlockContainer();
        foreach (var block in (Block[])[entry, fallthrough, loopHead, latch])
            container.Add(block);

        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Sample"),
            new MethodSignature(
                Void,
                [new Parameter("x", Int32)],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            container);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("if (__dotnet_inspect_switch0 == 0) goto IL_0010;", output);
        Assert.Contains("while (true)", output);
        Assert.True(
            output.IndexOf("IL_0010:", StringComparison.Ordinal)
                < output.IndexOf("while (true)", StringComparison.Ordinal),
            output);
        AssertGotoTargetsHaveLabels(output);
    }

    [Fact]
    public void Structuring_PreservesLabelOutsideTargetedConditionalLoop()
    {
        var entry = new Block(0);
        entry.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [0x20]));

        var loopEntry = new Block(0x04);
        loopEntry.Add(new Branch(0x20));

        var body = new Block(0x10);
        body.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));

        var condition = new Block(0x20);
        condition.Add(new ConditionalBranch(new LoadArgument(1, "flag", Boolean), 0x10));

        var exit = new Block(0x24);
        exit.Add(new Return(null));

        var container = new BlockContainer();
        foreach (var block in (Block[])[entry, loopEntry, body, condition, exit])
            container.Add(block);

        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Sample"),
            new MethodSignature(
                Void,
                [new Parameter("x", Int32), new Parameter("flag", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            container);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("if (__dotnet_inspect_switch0 == 0) goto IL_0020;", output);
        Assert.Contains("while (flag)", output);
        Assert.True(
            output.IndexOf("IL_0020:", StringComparison.Ordinal)
                < output.IndexOf("while (flag)", StringComparison.Ordinal),
            output);
        AssertGotoTargetsHaveLabels(output);
    }

    [Fact]
    public void Structuring_ConsumedConditionalLoopEdgeDoesNotEmitLabel()
    {
        var entry = new Block(0);
        entry.Add(new Branch(0x20));

        var body = new Block(0x10);
        body.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));

        var condition = new Block(0x20);
        condition.Add(new ConditionalBranch(new LoadArgument(0, "flag", Boolean), 0x10));

        var exit = new Block(0x24);
        exit.Add(new Return(null));

        var container = new BlockContainer();
        foreach (var block in (Block[])[entry, body, condition, exit])
            container.Add(block);

        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Sample"),
            new MethodSignature(
                Void,
                [new Parameter("flag", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            container);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("while (flag)", output);
        Assert.DoesNotContain("goto IL_0020;", output);
        Assert.DoesNotContain("IL_0020:", output);
    }

    [Fact]
    public void Structuring_ExternallyTargetedSecondCompoundLatchStaysFlat()
    {
        var entry = new Block(0);
        entry.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [0x24]));

        var loopEntry = new Block(0x04);
        loopEntry.Add(new Branch(0x20));

        var body = new Block(0x10);
        body.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));

        var firstCondition = new Block(0x20);
        firstCondition.Add(new ConditionalBranch(new LoadArgument(1, "first", Boolean), 0x28));

        var secondCondition = new Block(0x24);
        secondCondition.Add(new ConditionalBranch(new LoadArgument(2, "second", Boolean), 0x10));

        var exit = new Block(0x28);
        exit.Add(new Return(null));

        var container = new BlockContainer();
        foreach (var block in (Block[])[entry, loopEntry, body, firstCondition, secondCondition, exit])
            container.Add(block);

        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Sample"),
            new MethodSignature(
                Void,
                [
                    new Parameter("x", Int32),
                    new Parameter("first", Boolean),
                    new Parameter("second", Boolean)
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            container);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Contains("goto IL_0024;", output);
        AssertGotoTargetsHaveLabels(output);
    }

    [Fact]
    public void BooleanFolding_PreservesLabelWhenSwitchTargetBecomesGuardReturn()
    {
        var entry = new Block(0);
        entry.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [0x10]));

        var fallthrough = new Block(0x04);
        fallthrough.Add(new Return(new Constant(false, Boolean)));

        var target = new Block(0x10);
        target.Add(new ConditionalBranch(new LoadArgument(1, "flag", Boolean), 0x14));

        var arm = new Block(0x12);
        arm.Add(new Return(new Constant(true, Boolean)));

        var exit = new Block(0x14);
        exit.Add(new Return(new Constant(false, Boolean)));

        var container = new BlockContainer();
        foreach (var block in (Block[])[entry, fallthrough, target, arm, exit])
            container.Add(block);

        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Sample"),
            new MethodSignature(
                Boolean,
                [new Parameter("x", Int32), new Parameter("flag", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        new StructuringPass().Run(function, PassContext.None);
        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("if (__dotnet_inspect_switch0 == 0) goto IL_0010;", output);
        Assert.Contains("IL_0010:", output);
        AssertGotoTargetsHaveLabels(output);
    }

    [Fact]
    public void FullPipeline_PreservesEveryResidualSwitchTargetLabel()
    {
        using var source = MetadataSource.Open(typeof(CSharpSpellability).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CSharpSpellability).FullName!,
            "TypeIssue");
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        string output = result.Output ?? "";

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("if (__dotnet_inspect_switch", output);
        AssertGotoTargetsHaveLabels(output);
    }
}
