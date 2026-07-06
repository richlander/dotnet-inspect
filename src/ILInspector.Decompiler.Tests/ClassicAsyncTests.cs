using DotnetInspector.Fixtures;
using System;
using System.Linq;
using Xunit;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ClassicAsyncTests
{
    const string AsyncFixturesType = "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";

    [Fact]
    public void DumpMethod_WithClassicAsync_ResolvesImportAndReconstructsAwait()
    {
        var source = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DecompilerClassicAsync.AssemblyPath());
        var dump = StageDump.DumpMethod(source, AsyncFixturesType, "AwaitValue");
        
        Assert.NotNull(dump.Output);
        Assert.Contains("AwaitExpression", dump.Output);
    }

    [Fact]
    public void WholeAssemblySweepPattern_WithClassicAsync_UsesImportSeam()
    {
        using var source = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DecompilerClassicAsync.AssemblyPath());

        var withoutSeam = ImportAwaitValueFromAssembly(source);
        IrPasses.Run(withoutSeam);

        var withSeam = ImportAwaitValueFromAssembly(source);
        IrPasses.Run(withSeam, IrPasses.Default, PassContext.ForImport(method => IrImporter.Import(source, method)));

        Assert.DoesNotContain("AwaitExpression", IrPrinter.Dump(withoutSeam));
        Assert.Contains("AwaitExpression", IrPrinter.Dump(withSeam));
    }

    static IrFunction ImportAwaitValueFromAssembly(MetadataSource source)
        => IrImporter.ImportAssembly(source)
            .Single(method => method.TypeName == AsyncFixturesType && method.MethodName == "AwaitValue")
            .Function;
}
