using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class CultureInfoEhResidualTests
{
    [Fact]
    public void CreateSpecificCulture_RemainsEhUnstructuredPivot()
    {
        // #1135 pivot: CreateSpecificCulture has two adjacent filterless catch
        // islands and EH structuring stays fully flat. This is not a narrow
        // StructuringPass lane; it needs broader EH-region support before the
        // outer conditionals can be productively raised.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Globalization.CultureInfo", "CreateSpecificCulture");
        Assert.NotNull(function);

        IrPasses.Run(function!);

        Assert.Equal("structuring: conditional-branch", Completeness.Residual(function!));
        Assert.Equal("eh-unstructured", EhShapeClassifier.Classify(function!));
        Assert.Empty(function!.Descendants.OfType<TryCatch>());
        Assert.Contains(function.Descendants, node => node is Leave);
    }
}
