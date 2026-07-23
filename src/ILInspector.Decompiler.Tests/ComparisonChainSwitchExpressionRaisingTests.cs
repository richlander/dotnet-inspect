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
