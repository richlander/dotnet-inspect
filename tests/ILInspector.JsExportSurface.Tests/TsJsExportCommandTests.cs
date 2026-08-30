using ILInspector.JsExportSurface.Fixtures;
using TsJsExport;

namespace ILInspector.JsExportSurface.Tests;

public sealed class TsJsExportCommandTests
{
    [Fact]
    public void Invoke_RequiresRuntimeModuleSpecifier()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsJsExportCommand.Invoke(
            [typeof(TsJsExportCommand).Assembly.Location],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains(
            "--runtime-module requires a non-empty module specifier",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_DoesNotPublishPartialOutputWhenSurfaceIsUnsupported()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-{Guid.NewGuid():N}.ts");
        const string existing = "// existing output\n";
        try
        {
            File.WriteAllText(outputPath, existing);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(FixtureExports).Assembly.Location,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    outputPath,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.NotEmpty(error.ToString());
            Assert.Equal(existing, File.ReadAllText(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void Invoke_PublishesOneTypeScriptModule()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-{Guid.NewGuid():N}.ts");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(TsJsExportCommand).Assembly.Location,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    outputPath,
                ],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Empty(output.ToString());
            Assert.Empty(error.ToString());
            string source = File.ReadAllText(outputPath);
            Assert.Contains(
                """import { dotnet, type RuntimeAPI } from "./dotnet.js";""",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "export function initializeRuntime(): Promise<void>",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "export function runEntryPoint(",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "export declare function",
                source,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}
