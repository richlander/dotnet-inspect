using ILInspector.JsExportSurface.Fixtures;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

public sealed class TsBindGenCommandTests
{
    private static string FixtureAssemblyPath => typeof(FixtureExports).Assembly.Location;

    [Fact]
    public void Invoke_WithoutDiffAgainst_PrintsGeneratedDtsAndReturnsOneForUnmappedTypes()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("export interface WidgetDto {", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("NeedsUnmappedTypeFixture.Unmapped: System.Guid has no TypeScript mapping.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_ResolvesRecordFromInternalJsonSerializerContext()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("export interface InternalContextPascalWidget {", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("export declare function getInternalContextWidget(name: string): InternalContextPascalWidget;", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_PreservesPascalCaseWhenContextDeclaresNoPropertyNamingPolicy()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("export interface InternalContextPascalWidget {", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("  Name: string;", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("  Count: number;", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_ResolvesWireContractsFromBodyEvidence()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("export declare function getWidget(name: string, count: number): WidgetDto;", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_UsesWrapperFunctionNames()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("export declare function queryPackage(packageId: string): string;", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("export declare function QueryPackage", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithMatchingDiffAgainstFile_ReturnsOneWhenDiagnosticsExist()
    {
        var generateOutput = new StringWriter();
        var generateError = new StringWriter();
        Assert.Equal(1, TsBindGenCommand.Invoke([FixtureAssemblyPath], generateOutput, generateError));

        string tempFile = Path.Combine(AppContext.BaseDirectory, "tsbindgen-command-match.d.ts");
        try
        {
            File.WriteAllText(tempFile, generateOutput.ToString());

            var output = new StringWriter();
            var error = new StringWriter();
            int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath, "--diff-against", tempFile], output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains("no drift detected", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("NeedsUnmappedTypeFixture.Unmapped: System.Guid has no TypeScript mapping.", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Invoke_WithMismatchedDiffAgainstFile_ReturnsOneAndPrintsGeneratedOutput()
    {
        string tempFile = Path.Combine(AppContext.BaseDirectory, "tsbindgen-command-mismatch.d.ts");
        try
        {
            File.WriteAllText(tempFile, "export interface WidgetDto {\n  tags: string[];\n}\n");

            var output = new StringWriter();
            var error = new StringWriter();
            int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath, "--diff-against", tempFile], output, error);

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

        int exitCode = TsBindGenCommand.Invoke(["/nonexistent/path/does-not-exist.dll"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("assembly not found", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithMissingDiffAgainstFile_ReturnsOneAndReportsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([FixtureAssemblyPath, "--diff-against", "/nonexistent/hand-written.d.ts"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("--diff-against file not found", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithInvalidEmitJsPath_ReturnsOneAndReportsError()
    {
        string missingDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "missing-tsbindgen-output-directory",
            "generated.js");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke(
            [FixtureAssemblyPath, "--emit-js", missingDirectory],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            $"could not write JavaScript module to '{missingDirectory}'",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithMalformedAssembly_ReturnsOneAndReportsErrorInsteadOfCrashing()
    {
        string notAnAssembly = Path.Combine(AppContext.BaseDirectory, "tsbindgen-not-an-assembly.txt");
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

    [Fact]
    public void Invoke_PrintsDiagnosticsAndReturnsOneForUnmappedTypes()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke([typeof(NeedsUnmappedTypeFixtureExports).Assembly.Location], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("NeedsUnmappedTypeFixture.Unmapped: System.Guid has no TypeScript mapping.", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("unknown", output.ToString(), StringComparison.Ordinal);
    }
}
