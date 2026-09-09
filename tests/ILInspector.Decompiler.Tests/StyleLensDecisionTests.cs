using System.Linq;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The applied-lens evidence: when an opt-in byte-divergent style lens (#3138)
/// actually rewrites a render, <see cref="CSharpPrinter.PrintRaised(IrFunction,
/// System.Func{MethodRef, IrFunction?}, PrinterOptions?, System.Func{TypeRef,
/// TypeRef, bool})"/> records a <see cref="DecompilerDecision"/> in the
/// <see cref="DecompilerDecisionCategories.StyleLens"/> category. That decision is
/// the surface a host reports as "a byte-divergent lens was applied here" — the
/// #3127 signal that two "valid/correct" verdicts are not opcode-faithful —
/// without reverse-engineering the rendered text. The default (lens-off) render
/// records no such decision.
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class StyleLensDecisionTests
{
    static string AssemblyPath => typeof(StyleLensDecisionTests).Assembly.Location;

    static DecompilerResult Decompile(string typeName, string method, PrinterOptions? options = null)
    {
        using var source = MetadataSource.Open(AssemblyPath);
        var function = IrImporter.Import(source, typeName, method);
        Assert.NotNull(function);
        return CSharpPrinter.PrintRaised(
            function!,
            importMethodBody: methodRef => IrImporter.Import(source, methodRef),
            options,
            source.AreProvablyDisjoint);
    }

    [Fact]
    public void ConditionalReturnLens_WhenApplied_RecordsByteDivergentDecision()
    {
        var result = Decompile(
            typeof(PreferConditionalReturnSpecimen).FullName!,
            nameof(PreferConditionalReturnSpecimen.NeitherOr),
            new PrinterOptions { PreferConditionalExpressionReturn = true });

        var decision = Assert.Single(
            result.Decisions,
            d => d.RuleId == "style-lens.prefer-conditional-return");
        Assert.Equal(DecompilerDecisionCategories.StyleLens, decision.Category);
    }

    [Fact]
    public void ConditionalReturnLens_Default_RecordsNoStyleLensDecision()
    {
        var result = Decompile(
            typeof(PreferConditionalReturnSpecimen).FullName!,
            nameof(PreferConditionalReturnSpecimen.NeitherOr));

        Assert.DoesNotContain(
            result.Decisions,
            d => d.Category == DecompilerDecisionCategories.StyleLens);
    }

    [Fact]
    public void BranchlessBooleanLens_WhenApplied_RecordsByteDivergentDecision()
    {
        var result = Decompile(
            typeof(PreferBranchlessBooleanSpecimen).FullName!,
            nameof(PreferBranchlessBooleanSpecimen.AndTailGuard),
            new PrinterOptions { PreferBranchlessBoolean = true });

        var decision = Assert.Single(
            result.Decisions,
            d => d.RuleId == "style-lens.prefer-branchless-boolean");
        Assert.Equal(DecompilerDecisionCategories.StyleLens, decision.Category);
    }

    [Fact]
    public void BranchlessBooleanLens_Default_RecordsNoStyleLensDecision()
    {
        var result = Decompile(
            typeof(PreferBranchlessBooleanSpecimen).FullName!,
            nameof(PreferBranchlessBooleanSpecimen.AndTailGuard));

        Assert.DoesNotContain(
            result.Decisions,
            d => d.Category == DecompilerDecisionCategories.StyleLens);
    }
}
