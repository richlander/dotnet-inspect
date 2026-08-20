using ILInspector.JsExportSurface.Fixtures;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="TsBindGenCommand"/> end to end: generate mode, a matching
/// <c>--diff-against</c> file (exit 0), and a deliberately mismatched one (exit 1, with generated
/// output printed to stderr).
/// </summary>
public sealed class TsBindGenCommandTests
{
    private static string FixtureAssemblyPath => typeof(FixtureExports).Assembly.Location;

    [Fact]
    public void Invoke_WithoutDiffAgainst_PrintsGeneratedDtsAndReturnsZero()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("export interface WidgetDto {", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Invoke_ResolvesWireContractsFromBodyEvidence()
    {
        // The CLI must open a LibraryBodyIndex and pass it through so the emitted return type is
        // the resolved WidgetDto, not the erased string envelope declared on GetWidget itself.
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "export declare function getWidget(name: string, count: number): WidgetDto;",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithMatchingDiffAgainstFile_ReturnsZero()
    {
        var generateOutput = new StringWriter();
        Assert.Equal(0, TsBindGenCommand.Invoke([FixtureAssemblyPath], generateOutput, new StringWriter()));

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, generateOutput.ToString());

            var output = new StringWriter();
            var error = new StringWriter();
            int exitCode = TsBindGenCommand.Invoke(
                [FixtureAssemblyPath, "--diff-against", tempFile], output, error);

            Assert.Equal(0, exitCode);
            Assert.Contains("no drift detected", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Invoke_WithMismatchedDiffAgainstFile_ReturnsOneAndPrintsGeneratedOutput()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "export interface WidgetDto {\n  tags: string[];\n}\n");

            var output = new StringWriter();
            var error = new StringWriter();
            int exitCode = TsBindGenCommand.Invoke(
                [FixtureAssemblyPath, "--diff-against", tempFile], output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains("drift detected", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("tags: number[];", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Invoke_WithMissingAssembly_ReturnsOneAndReportsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke(
            ["/nonexistent/path/does-not-exist.dll"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("assembly not found", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithMissingDiffAgainstFile_ReturnsOneAndReportsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke(
            [FixtureAssemblyPath, "--diff-against", "/nonexistent/hand-written.d.ts"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("--diff-against file not found", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithMalformedAssembly_ReturnsOneAndReportsErrorInsteadOfCrashing()
    {
        // A file that exists but is not a valid PE image must fail cleanly, not throw an
        // unhandled BadImageFormatException out of the CLI.
        string notAnAssembly = Path.GetTempFileName();
        try
        {
            File.WriteAllText(notAnAssembly, "this is not a .NET assembly");

            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke([notAnAssembly], output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains("could not read", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(notAnAssembly);
        }
    }
}
