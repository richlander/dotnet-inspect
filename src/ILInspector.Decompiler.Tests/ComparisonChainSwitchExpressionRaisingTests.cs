using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Slice 1 of the #2978 full-raise: csc lowers a small value <c>switch</c>
/// expression (few labels) to an equality comparison chain (brfalse/beq) whose
/// arms each store one dedicated result temp read exactly once at a convergence
/// point (a <c>return</c> or a copy into another temp). <see
/// cref="ILInspector.Decompiler.Pipeline.SwitchRaisingPass"/> raises that faithful
/// signal back into a <see cref="SwitchExpression"/>. The close negative is a
/// hand-written equality chain that returns directly with no result temp — it
/// must stay <c>if</c>/<c>else</c>.
/// </summary>
public class ComparisonChainSwitchExpressionRaisingTests
{
    static IrFunction Raised(string type, string method)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, type, method);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static string Print(string type, string method) =>
        CSharpPrinter.Print(Raised(type, method)).Output!.ReplaceLineEndings("\n");

    [Fact]
    public void ReturnJoinValueChain_RaisesToSwitchExpression()
    {
        var function = Raised(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyLength));

        var expression = Assert.Single(function.Descendants.OfType<SwitchExpression>());
        Assert.Equal(3, expression.Arms.Count);
        Assert.Single(expression.Arms, arm => arm.IsDefault);

        // The dispatch's equality tests and their branches are fully consumed.
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
        Assert.Empty(function.Descendants.OfType<Comparison>());
    }

    [Fact]
    public void ReturnJoinValueChain_RendersGoverningExpressionAndArms()
    {
        var output = Print(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyLength));

        Assert.Contains("s.Length switch", output);
        Assert.Contains("0 => Fail(out x),", output);
        Assert.Contains("1 => Win(1, out x),", output);
        Assert.Contains("_ => Fail(out x),", output);
        Assert.DoesNotContain("if (", output);
    }

    [Fact]
    public void CopyJoinInnerChain_RaisesInsideScatteredDispatchWitness()
    {
        // The #2978 witness's inner `s.Length switch` converges by copying its
        // result temp into the outer temp (copy-to-temp join), not a return.
        var output = Print(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.Dispatch));

        Assert.Contains("s.Length switch", output);
        Assert.Contains("0 => Fail(out x),", output);
        Assert.Contains("1 => Win(1, out x),", output);
        Assert.Contains("_ => Fail(out x),", output);
    }

    [Fact]
    public void HandWrittenEqualityChain_DeclinesSwitchExpressionRaise()
    {
        // Direct returns per test — no result temp, no convergence read, and
        // s.Length is re-read each time. Must stay if/else.
        var function = Raised(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyLengthManual));

        Assert.Empty(function.Descendants.OfType<SwitchExpression>());
    }

    // Builds the exact comparison-chain value-switch shape the raiser accepts —
    // `v == 0 => 10, v == 1 => 20, _ => 30` storing one result temp — parameterized
    // by the governing value's declared type. With <paramref name="extraTempRead"/>
    // the join reads the result temp a second time, so the temp is no longer the
    // single-read convergence signal a real switch expression produces.
    static IrFunction BuildComparisonChainSwitch(TypeRef governingType, bool extraTempRead = false)
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        LoadArgument V() => new(0, "v", governingType);
        Comparison Eq(int label) => new(ComparisonKind.Equal, isUnsigned: false, V(), new Constant(label, int32));

        Block ValueBlock(int offset, int result) =>
            AddAll(new Block(offset), new StoreLocal(0, int32, new Constant(result, int32)), new Branch(0x50));

        var head = AddAll(new Block(0x00), new ConditionalBranch(Eq(0), 0x40));   // v == 0 => arm0
        var test1 = AddAll(new Block(0x10), new ConditionalBranch(Eq(1), 0x30));  // v == 1 => arm1
        var defaultArm = ValueBlock(0x20, 30);
        var arm1 = ValueBlock(0x30, 20);
        var arm0 = ValueBlock(0x40, 10);
        var join = new Block(0x50);
        if (extraTempRead)
            join.Add(new StoreLocal(1, int32, new LoadLocal(0, int32)));   // a second read of the result temp
        join.Add(new Return(new LoadLocal(0, int32)));                     // (single read in the faithful shape)

        var container = new BlockContainer();
        foreach (var block in new[] { head, test1, defaultArm, arm1, arm0, join })
            container.Add(block);

        var signature = new MethodSignature(int32, [new Parameter("v", governingType)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Sample"), signature, [int32, int32], container);
    }

    static Block AddAll(Block block, params IrNode[] children)
    {
        foreach (var child in children)
            block.Add(child);
        return block;
    }

    static bool RaisesSwitchExpression(IrFunction function)
    {
        new SwitchRaisingPass().Run(function, PassContext.None);
        return function.Descendants.OfType<SwitchExpression>().Any();
    }

    [Fact]
    public void ReusedResultTemp_DeclinesAtConvergenceCheck()
    {
        // Isolates the single-read convergence guard. csc's real switch-expression
        // lowering always reads its result temp exactly once (any downstream reuse
        // reads a *later* copy), so a genuine multi-read of the result temp only
        // arises in hand-written or obfuscated IL. The single-read twin raises,
        // proving the shape is accepted, so the extra-read variant's decline is
        // the convergence check's doing — not an earlier rejection.
        Assert.True(RaisesSwitchExpression(
            BuildComparisonChainSwitch(TypeRef.CoreLib("System", "Int32"))));
        Assert.False(RaisesSwitchExpression(
            BuildComparisonChainSwitch(TypeRef.CoreLib("System", "Int32"), extraTempRead: true)));
    }

    [Fact]
    public void BooleanGoverningValue_DeclinesSwitchExpressionRaise()
    {
        // Self-validating twin: the identical chain on an Int32 governing value
        // raises, proving the synthetic shape is one the raiser genuinely accepts.
        Assert.True(RaisesSwitchExpression(
            BuildComparisonChainSwitch(TypeRef.CoreLib("System", "Int32"))));

        // So on a Boolean governing value the decline is the bool guard's doing.
        // csc never lowers a bool `switch` to this equality chain; the guard
        // defends the arbitrary/obfuscated-IL path from printing an invalid
        // `b switch { 0 => …, 1 => … }` (int is not convertible to bool, CS0029).
        Assert.False(RaisesSwitchExpression(
            BuildComparisonChainSwitch(TypeRef.CoreLib("System", "Boolean"))));
    }

    [Fact]
    public void UnsignedGoverningValue_DeclinesToAvoidInvalidLabel()
    {
        // A uint governing value whose label is uint.MaxValue (IL ldc.i4.m1) is
        // recorded as a negative int32 and would misprint as -1 (CS0031). The
        // raiser must decline a uint governing value with a negative label.
        var function = Raised(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyUnsigned));

        Assert.Empty(function.Descendants.OfType<SwitchExpression>());

        var output = Print(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyUnsigned));
        Assert.DoesNotContain("-1 =>", output);
    }

    [Fact]
    public void EnumGoverningValue_RaisesWithMemberNames()
    {
        // Enum labels are int-backed; the printer renders them as member names
        // via the governing type. The uint guard must not decline enum switches.
        var function = Raised(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyEnum));

        Assert.Single(function.Descendants.OfType<SwitchExpression>());

        var output = Print(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyEnum));
        Assert.Contains("DispatchKind.Zero => Fail(out x),", output);
        Assert.Contains("DispatchKind.One => Win(1, out x),", output);
        Assert.DoesNotContain("if (", output);
    }

    [Fact]
    public void ByteGoverningValue_RaisesToSwitchExpression()
    {
        // Byte labels are small non-negative int32 constants that print
        // faithfully; the uint guard must not decline them.
        var function = Raised(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyByte));

        Assert.Single(function.Descendants.OfType<SwitchExpression>());

        var output = Print(
            typeof(ScatteredReturnDispatchSample).FullName!,
            nameof(ScatteredReturnDispatchSample.ClassifyByte));
        Assert.DoesNotContain("-1 =>", output);
        Assert.DoesNotContain("if (", output);
    }
}
