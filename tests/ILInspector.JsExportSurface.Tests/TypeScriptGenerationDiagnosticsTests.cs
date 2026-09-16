namespace ILInspector.JsExportSurface.Tests;

public sealed class TypeScriptGenerationDiagnosticsTests
{
    [Fact]
    public void ReportUnmappedType_ContainsArtifactText()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();

        diagnostics.ReportUnmappedType(
            "Type\n\u001b[2J.Member",
            "Bad\u0007\u202EType");

        TypeScriptGenerationDiagnostic diagnostic =
            Assert.Single(diagnostics.UnmappedTypes);
        Assert.Equal("Type \\u001B[2J.Member", diagnostic.Location);
        Assert.Equal("Bad\\u0007\\u202EType", diagnostic.CSharpType);
    }
}
