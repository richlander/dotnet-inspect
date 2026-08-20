using ILInspector.JsExportSurface.Fixtures;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

public sealed class TsBindGenCommandTests
{
    private static string FixtureAssemblyPath => typeof(FixtureExports).Assembly.Location;

    [Fact]
    public void Invoke_PrintsGeneratedDtsAndReturnsOneForUnmappedTypes()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("export interface WidgetDto {", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("NeedsUnmappedTypeFixture.Unmapped: System.Guid has no TypeScript mapping.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_UsesVerbatimJsExportFunctionNames()
    {
        var output = new StringWriter();
        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, new StringWriter());
        Assert.Equal(1, exitCode);
        Assert.Contains("export declare function QueryPackage(packageId: string): string;", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_PreservesNoPolicyContextPropertiesAlongsideCamelCaseContext()
    {
        var output = new StringWriter();
        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, new StringWriter());
        Assert.Equal(1, exitCode);
        string dts = output.ToString();
        Assert.Contains("export interface InternalContextPascalWidget {", dts, StringComparison.Ordinal);
        Assert.Contains("  Name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("export interface InternalContextCamelWidget {", dts, StringComparison.Ordinal);
        Assert.Contains("  name: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_QuotesNonIdentifierJsonPropertyNames()
    {
        var output = new StringWriter();
        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, new StringWriter());
        Assert.Equal(1, exitCode);
        Assert.Contains("  \"display-name\": string;", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("  \"\": string;", output.ToString(), StringComparison.Ordinal);
    }
}
