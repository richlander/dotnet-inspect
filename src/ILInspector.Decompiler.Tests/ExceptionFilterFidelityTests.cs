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
}
