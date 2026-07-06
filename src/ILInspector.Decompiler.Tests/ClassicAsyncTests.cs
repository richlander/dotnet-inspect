using System.Linq;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class CrossMethodImportSeamTests
{
    static readonly string SampleType = typeof(CfgSampleClass).FullName!;

    [Fact]
    public void DumpMethod_WithLocalFunction_ResolvesImportAndRaisesDeclaration()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var dump = StageDump.DumpMethod(source, SampleType, nameof(CfgSampleClass.DoubleViaLocalFunction));
        
        Assert.NotNull(dump.Output);
        Assert.Contains("LocalFunctionStatement", dump.Output);
    }

    [Fact]
    public void WholeAssemblySweepPattern_WithLocalFunction_UsesImportSeam()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var withoutSeam = ImportDoubleViaLocalFunctionFromAssembly(source);
        IrPasses.Run(withoutSeam);

        var withSeam = ImportDoubleViaLocalFunctionFromAssembly(source);
        IrPasses.Run(withSeam, IrPasses.Default, PassContext.ForImport(method => IrImporter.Import(source, method)));

        Assert.DoesNotContain("LocalFunctionStatement", IrPrinter.Dump(withoutSeam));
        Assert.Contains("LocalFunctionStatement", IrPrinter.Dump(withSeam));
    }

    static IrFunction ImportDoubleViaLocalFunctionFromAssembly(MetadataSource source)
        => IrImporter.ImportAssembly(source)
            .Single(method => method.TypeName == SampleType && method.MethodName == nameof(CfgSampleClass.DoubleViaLocalFunction))
            .Function;
}
