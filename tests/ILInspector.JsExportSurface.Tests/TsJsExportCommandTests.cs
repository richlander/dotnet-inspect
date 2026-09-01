using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using TsJsExport;

namespace ILInspector.JsExportSurface.Tests;

public sealed class TsJsExportCommandTests
{
    [Fact]
    public void Invoke_WithMalformedMetadataRoot_ReturnsOneAndReportsTypedError()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-malformed-root-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(
                path,
                MetadataAdmissionFixture.WithUnmappableMetadataDirectory(
                    typeof(FixtureExports).Assembly.Location));
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [path, "--runtime-module", "./dotnet.js"],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "UnmappableMetadataDirectory",
                error.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Invoke_WithWindowsMetadata_ReturnsOneAndReportsTypedError()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-windows-metadata-{Guid.NewGuid():N}.winmd");
        try
        {
            File.WriteAllBytes(
                path,
                MetadataAdmissionFixture.ManagedWindowsMetadata());
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [path, "--runtime-module", "./dotnet.js"],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "Windows Metadata",
                error.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Invoke_WithMetadataStreamCountOverflow_ReturnsBoundedError()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-stream-count-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(
                path,
                MetadataAdmissionFixture.WithOverflowingMetadataStreamCount(
                    typeof(FixtureExports).Assembly.Location));
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [path, "--runtime-module", "./dotnet.js"],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "could not read",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Unhandled exception",
                error.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
    public void Invoke_FilteredGeneratedTypeExportFailsBeforePublication()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-filtered-generated-type-{Guid.NewGuid():N}.ts");
        const string existing = "// existing output\n";
        try
        {
            File.WriteAllText(outputPath, existing);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(LambdaExportFixture).Assembly.Location,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    outputPath,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "filtered MethodDefs",
                error.ToString(),
                StringComparison.Ordinal);
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
