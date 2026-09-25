using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class JsonDocumentDisposeRaisingTests
{
    [Fact]
    public void JsonDocumentDispose_RaisesSourceLikeControlFlow()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "StructuredTypes",
            "System.Text.Json.dll");
        Assert.True(File.Exists(assemblyPath), assemblyPath);

        using var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(
            source,
            "System.Text.Json.JsonDocument",
            "Dispose");
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        Assert.True(result.Succeeded);

        string output = result.Output!.ReplaceLineEndings("\n");
        Assert.Contains("|| !IsDisposable)", output);
        Assert.Contains("Interlocked.Exchange", output);
        Assert.Contains("?.Dispose();", output);
        Assert.DoesNotContain("_ = Interlocked.Exchange", output);
        Assert.DoesNotContain("goto IL_", output);
        Assert.DoesNotContain("\nIL_", output);
        Assert.DoesNotContain("S_", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void JsonDocumentDispose_RecompilesExactly()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "StructuredTypes",
            "System.Text.Json.dll");

        var result = Assert.Single(FidelityCheck.Evaluate(
            assemblyPath,
            type => type == "System.Text.Json.JsonDocument",
            method => method.Method == "Dispose"));

        Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"Status: {result.Status}\n"
            + $"Original: {result.OriginalOpcodes}\n"
            + $"Recompiled: {result.RecompiledOpcodes}\n"
            + $"Detail: {result.Detail}\n"
            + $"Diff: {result.FidelityDiff}");
    }
}
