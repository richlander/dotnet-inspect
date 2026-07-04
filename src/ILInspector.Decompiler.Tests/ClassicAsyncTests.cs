using DotnetInspector.Fixtures;
using System;
using System.Linq;
using Xunit;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ClassicAsyncTests
{
    [Fact]
    public void DumpMethod_WithClassicAsync_ResolvesImportAndReconstructsAwait()
    {
        var source = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DecompilerClassicAsync.AssemblyPath());
        var dump = StageDump.DumpMethod(source, "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures", "AwaitValue");
        
        Assert.NotNull(dump.Output);
        Assert.Contains("AwaitExpression", dump.Output);
    }
}
