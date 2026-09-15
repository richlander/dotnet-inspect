using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Issue #1841 (#1202 residual): foreach over a struct array element takes the
// element address (ldelema); the collection must render as `Rows[i]`, not the
// invalid `ref Rows[i]` (CS1525). LoadElementAddress is normalized to LoadElement.
public class ForeachElementCollectionTests
{
    [Fact]
    public void ForeachOverStructArrayElement_RendersValueCollection_NotRef()
    {
        using var source = MetadataSource.Open(typeof(ForeachElementProbe).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ForeachElementProbe).FullName!, nameof(ForeachElementProbe.Sum));
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var body = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method)).Output!;

        Assert.Contains("in Rows[", body);
        Assert.DoesNotContain("in ref", body);
    }
}
