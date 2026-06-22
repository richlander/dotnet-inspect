using System.Linq;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// A raw <c>endfilter</c> has no standalone C# spelling. Until a filter is
/// raised to <c>catch (...) when (...)</c>, the flat residue must degrade
/// honestly instead of claiming Full.
/// </summary>
public class ExceptionFilterFidelityTests
{
    [Fact]
    public void ResidualEndFilter_DegradesToPartialAndReportsRemark()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0x20);
        container.Add(block);
        block.Add(new EndFilter(new Constant(1, i32)));

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, [], container);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var remark = Assert.Single(FidelityRemarks.Collect(function),
            r => r.Code == DiagnosticIds.UnsupportedExceptionFilter);
        Assert.Equal(0x20, remark.Offset);
        Assert.Contains("endfilter", remark.Reason);
    }

    [Fact]
    public void FilterMethod_PreservesEhTerminatorsThroughPipeline()
    {
        // #1089 row 7 catch/filter-entangled audit. A try/catch-when filter
        // (FilteredLength) stays honestly Partial because the filter region is not
        // structured, so the surrounding conditional branches stay flat too. The
        // audit invariant: no pass illegally consumes or reorders the EH
        // terminators — dropping the endfilter or a leave across the protected
        // region would change which handler runs. The endfilter and the leaves
        // survive the full pipeline unchanged (no illegal EH transform).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.FilteredLength));
        Assert.NotNull(function);
        int endFiltersBefore = function!.Descendants.OfType<EndFilter>().Count();
        int leavesBefore = function.Descendants.OfType<Leave>().Count();
        Assert.True(endFiltersBefore > 0, "fixture must import with an endfilter");

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Equal(endFiltersBefore, function.Descendants.OfType<EndFilter>().Count());
        Assert.Equal(leavesBefore, function.Descendants.OfType<Leave>().Count());
    }
}
