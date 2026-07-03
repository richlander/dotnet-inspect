using System;
using System.IO;
using System.Linq;
using Xunit;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ClassicAsyncTests
{
    [Fact]
    public void DumpMethod_WithClassicAsync_ResolvesImportAndReconstructsAwait()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "bin", "ILInspector.Decompiler.Fixtures.ClassicAsync", "release", "ILInspector.Decompiler.Fixtures.ClassicAsync.dll");
        if (!File.Exists(fixturePath))
            fixturePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "bin", "ILInspector.Decompiler.Fixtures.ClassicAsync", "release", "ILInspector.Decompiler.Fixtures.ClassicAsync.dll");
            
        Assert.True(File.Exists(fixturePath), "ClassicAsync fixture DLL not found");

        var source = MetadataSource.OpenWithoutSymbols(fixturePath);
        var dump = StageDump.DumpMethod(source, "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures", "AwaitValue");
        
        Assert.NotNull(dump.Output);
        Assert.Contains("AwaitExpression", dump.Output);
    }
}
