using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Real-IL fixture for <see cref="SwitchBranchRenderingTests.Structuring_PreservesFlattenedSwitchTargetLabels"/>:
/// a dense group compiles to an IL <c>switch</c>, while the sparse case compiles
/// to a comparison branch sharing the same return leaf. This is the smallest
/// compiler-produced shape needed to verify that structuring preserves every
/// flattened switch target label.
/// </summary>
static class SwitchTableFixture
{
    public static int Classify(int value)
    {
        switch (value)
        {
            case 0:
            case 2:
                return 8;
            case 1:
            case 3:
            case 100:
                return 2;
            default:
                return 0;
        }
    }
}

/// <summary>
/// Real-IL fixture for
/// <see cref="SwitchBranchRenderingTests.FullPipeline_RaisesSwitchWhoseCaseContainsLoop"/>:
/// a <c>switch</c> one of whose case sections carries a loop. csc lowers the loop
/// to a bottom-tested back-edge, so the section is not a straight-line
/// single-entry region — the shape that used to keep the whole switch flat
/// (issue #3161) until <c>SwitchRaisingPass</c> learned to own a section that
/// contains a natural loop.
/// </summary>
static class SwitchLoopingCaseFixture
{
    public static int Reduce(int kind, int[] values)
    {
        switch (kind)
        {
            case 0:
                int total = 0;
                foreach (int value in values)
                {
                    total += value;
                }
                return total;
            case 1:
                return values.Length;
            case 2:
                return -values.Length;
            case 3:
                return 42;
            default:
                return -1;
        }
    }
}

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
        Assert.Contains("int __switchValue0 = default;", output);
        Assert.Contains("__switchValue0 = (int)(x);", output);
        Assert.Contains("if (__switchValue0 == 0) goto IL_0010;", output);
        Assert.Contains("if (__switchValue0 == 1) goto IL_0020;", output);
        Assert.Contains("if (__switchValue0 == 2) goto IL_0030;", output);
    }

    [Fact]
    public void DuplicateTargets_EmitOnePerCaseIndex()
    {
        // IL switch tables routinely repeat a target (a shared fall-through); each
        // case index still needs its own arm.
        var result = RenderSwitch(0x10, 0x20, 0x10);
        var output = result.Output ?? "";

        Assert.Contains("if (__switchValue0 == 0) goto IL_0010;", output);
        Assert.Contains("if (__switchValue0 == 1) goto IL_0020;", output);
        Assert.Contains("if (__switchValue0 == 2) goto IL_0010;", output);
    }

    [Fact]
    public void Structuring_PreservesFlattenedSwitchTargetLabels()
    {
        // The dense jump-table targets are also comparison-tree return leaves.
        // Structuring must keep those labels available for lowered rendering.
        using var source = MetadataSource.Open(typeof(SwitchTableFixture).Assembly.Location);
        var function = IrImporter.Import(source, typeof(SwitchTableFixture).FullName!, nameof(SwitchTableFixture.Classify));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        string output = result.Output ?? "";

        Assert.Contains("if (__switchValue", output);
        Assert.DoesNotContain("case 0: goto", output);
        AssertGotoTargetsHaveLabels(output);
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

        Assert.Contains("if (__switchValue0 == 0) goto IL_0010;", output);
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

        Assert.Contains("if (__switchValue0 == 0) goto IL_0010;", output);
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

        Assert.Contains("if (__switchValue0 == 0) goto IL_0020;", output);
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

        Assert.Contains("if (__switchValue0 == 0) goto IL_0010;", output);
        Assert.Contains("IL_0010:", output);
        AssertGotoTargetsHaveLabels(output);
    }

    [Fact]
    public void FullPipeline_RaisesSwitchWhoseCaseContainsLoop()
    {
        // A compiled `switch` whose `case 0` carries a `foreach` loop. csc lowers
        // the loop to a back-edge, so the section is not a straight-line
        // single-entry region. Before #3161 this kept the whole switch flat (its
        // `default`/looping section could not be owned); now SwitchRaisingPass
        // owns the loop-bearing section and StructuringPass raises the loop.
        using var source = MetadataSource.Open(typeof(SwitchLoopingCaseFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(SwitchLoopingCaseFixture).FullName!,
            nameof(SwitchLoopingCaseFixture.Reduce));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        string output = result.Output ?? "";

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("switch (", output);
        Assert.DoesNotContain("__switchValue", output);
        AssertGotoTargetsHaveLabels(output);
    }

    [Fact]
    public void FullPipeline_RaisesRealSwitchWithLoopingSections()
    {
        // Regression witness for #3161 on a real compiler-produced method:
        // `CSharpSpellability.TypeIssue` is a `switch (type.Kind)` whose sections
        // carry `foreach`/`for` loops. It used to stay a flat residual dispatch
        // (`if (__switchValue…) goto …`); now it raises into a structured switch
        // with Full fidelity and no residual dispatch temp.
        using var source = MetadataSource.Open(typeof(CSharpSpellability).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CSharpSpellability).FullName!,
            "TypeIssue");
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        string output = result.Output ?? "";

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("switch (", output);
        Assert.DoesNotContain("__switchValue", output);
        AssertGotoTargetsHaveLabels(output);
    }

    [Fact]
    public void FullPipeline_RaisesGuardsBeforeLoopingSwitch()
    {
        // Regression witness for #3162 (part of #3159), the DeepEquals prologue:
        // a guard-throw (`if (!c) throw;`) and a guard-return
        // (`if (a != b) return false;`) standing in front of a `switch` whose
        // sections carry EH-entangled `foreach` loops. csc lowers each guard to a
        // forward branch over a throw/return island (`if (c) goto L; …; L:`).
        // Before #3161 the unraised switch kept the whole container flat, so those
        // guards survived as residual `IL_xxxx:` labels and `goto`s; now
        // SwitchRaisingPass owns the loop-bearing sections, StructuringPass
        // structures the container, and the guards fold into inverted `if` clauses
        // with no residual labels or gotos.
        using var source = MetadataSource.Open(typeof(GuardsBeforeLoopingSwitchFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(GuardsBeforeLoopingSwitchFixture).FullName!,
            nameof(GuardsBeforeLoopingSwitchFixture.Compare));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        string output = result.Output ?? "";

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);

        // The switch raised and both loops structured.
        Assert.Contains("switch (", output);
        Assert.DoesNotContain("__switchValue", output);

        // The two prologue guards folded into inverted `if` clauses — the #3162
        // outcome: guard-throw negated, guard-return negated, no `goto`/labels.
        Assert.Contains("if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())", output);
        Assert.Contains("throw new InsufficientExecutionStackException();", output);
        Assert.Contains("if (kind != right.Count)", output);

        // No residual forward-branch scaffolding survives anywhere in the body.
        Assert.DoesNotContain("goto ", output);
        Assert.DoesNotMatch(new Regex(@"(?m)^\s*IL_[0-9A-Fa-f]+:\s*$"), output);
    }

    [Fact]
    public void FullPipeline_RaisesOpenCodedPairedEnumeratorLoop()
    {
        // Regression witness for #3163 (part of #3159), the DeepEquals object arm:
        // an open-coded paired enumerator loop (two `List<int>` enumerators taken
        // by hand and advanced in lockstep with manual MoveNext()/Current, no
        // `foreach` sugar) in a switch's default section. csc lowers it to a
        // bottom-tested/mid-entry goto loop (`goto COND; BODY: …; COND: if
        // (e.MoveNext()) goto BODY;`). Before #3161 the unraised switch — entangled
        // with case 0's `foreach` try/finally — kept the whole container flat, so
        // this loop survived as raw `goto`s/labels; now SwitchRaisingPass owns the
        // loop-bearing sections and StructuringPass raises it to a structured
        // `while` with no residual `goto`/labels.
        using var source = MetadataSource.Open(typeof(OpenCodedPairedEnumeratorLoopFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(OpenCodedPairedEnumeratorLoopFixture).FullName!,
            nameof(OpenCodedPairedEnumeratorLoopFixture.Compare));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        string output = result.Output ?? "";

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);

        // The switch raised (real IL jump table over the dense cases).
        Assert.Contains("switch (", output);
        Assert.DoesNotContain("__switchValue", output);

        // The #3163 outcome: the open-coded paired enumerator loop raised to a
        // structured `while`, keeping the lockstep second-enumerator advance, with
        // no residual `goto`/labels anywhere in the body.
        Assert.Contains("while (e1.MoveNext())", output);
        Assert.Contains("e2.MoveNext();", output);
        Assert.DoesNotContain("goto ", output);
        Assert.DoesNotMatch(new Regex(@"(?m)^\s*IL_[0-9A-Fa-f]+:\s*$"), output);
    }
}

/// <summary>
/// Real-IL fixture for
/// <see cref="SwitchBranchRenderingTests.FullPipeline_RaisesGuardsBeforeLoopingSwitch"/>:
/// mirrors the distinctive prologue of
/// <c>System.Text.Json.JsonElement.DeepEquals</c> — a guard-throw
/// (<c>if (!c) throw;</c>) and a guard-return (<c>if (a != b) return false;</c>)
/// standing in front of a <c>switch</c> whose case sections carry EH-entangled
/// <c>foreach</c> loops (a <see cref="System.Collections.Generic.List{T}"/>
/// enumerator lowers to a <c>try/finally</c> with a surviving <c>Leave</c>). csc
/// lowers each guard to a forward branch over a throw/return island
/// (<c>if (c) goto L; …; L:</c>). Before #3161 the unraised switch kept the whole
/// container flat, so those guards survived as residual <c>IL_xxxx:</c> labels and
/// <c>goto</c>s (issue #3162); once <c>SwitchRaisingPass</c> owns the loop-bearing
/// sections, <c>StructuringPass</c> structures the container and folds the guards
/// into inverted <c>if</c> clauses.
/// </summary>
static class GuardsBeforeLoopingSwitchFixture
{
    public static bool Compare(int kind, System.Collections.Generic.List<int> left, System.Collections.Generic.List<int> right)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new System.InsufficientExecutionStackException();
        }
        if (kind != right.Count)
        {
            return false;
        }
        switch (kind)
        {
            case 0:
                if (left.Count != right.Count)
                {
                    return false;
                }
                foreach (int value in left)
                {
                    if (value < 0)
                    {
                        return false;
                    }
                }
                return true;
            case 1:
                return left.Count == right.Count;
            case 2:
                return true;
            default:
                int index = 0;
                foreach (int value in right)
                {
                    if (value != left[index])
                    {
                        return false;
                    }
                    index++;
                }
                return true;
        }
    }
}

/// <summary>
/// Real-IL fixture for
/// <see cref="SwitchBranchRenderingTests.FullPipeline_RaisesOpenCodedPairedEnumeratorLoop"/>:
/// mirrors the object-comparison arm of
/// <c>System.Text.Json.JsonElement.DeepEquals</c> — an <em>open-coded</em> paired
/// enumerator loop (two <see cref="System.Collections.Generic.List{T}"/>
/// enumerators taken by hand and advanced in lockstep with manual
/// <c>MoveNext()</c>/<c>Current</c>, no <c>foreach</c> sugar). csc lowers it to a
/// bottom-tested/mid-entry goto loop (<c>goto COND; BODY: …; COND: if (e.MoveNext())
/// goto BODY;</c>). It sits in a <c>switch</c> whose <c>case 0</c> carries an
/// EH-entangled <c>foreach</c> (a <c>try/finally</c> with a surviving <c>Leave</c>),
/// so before #3161 the unraised switch kept the whole container flat and this loop
/// survived as raw <c>goto</c>s/labels (issue #3163). Once <c>SwitchRaisingPass</c>
/// owns the loop-bearing sections, <c>StructuringPass</c> raises it into a
/// structured <c>while</c> with no residual <c>goto</c>/labels.
/// </summary>
static class OpenCodedPairedEnumeratorLoopFixture
{
    public static bool Compare(int kind, System.Collections.Generic.List<int> left, System.Collections.Generic.List<int> right)
    {
        switch (kind)
        {
            case 0:
                foreach (int value in left)
                {
                    if (value < 0)
                    {
                        return false;
                    }
                }
                return true;
            case 1:
                return left.Count == right.Count;
            case 2:
                return true;
            case 3:
                return left.Count > 0;
            case 4:
                return right.Count > 0;
            default:
                int count = left.Count;
                System.Collections.Generic.List<int>.Enumerator e1 = left.GetEnumerator();
                System.Collections.Generic.List<int>.Enumerator e2 = right.GetEnumerator();
                while (e1.MoveNext())
                {
                    e2.MoveNext();
                    if (e1.Current != e2.Current)
                    {
                        return false;
                    }
                    count--;
                }
                return count == 0;
        }
    }
}
