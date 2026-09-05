using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.MemberConverterFixtures;
using ILInspector.JsExportSurface.NestedContextConstructorFixtures;
using ILInspector.JsExportSurface.NestedContextFixtures;
using ILInspector.JsExportSurface.NestedContextUnsupportedFixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.JsExportSurface.TypeScriptFixtures;
using TsJsExport;
using TsJsExport.ContextFixtures.Alpha;
using TsJsExport.ContextFixtures.Host;

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
    public void Invoke_OmitsJsonIncludedMembersWhoseValueTypesAreInaccessible()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-hidden-jsoninclude-{Guid.NewGuid():N}.ts");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(TypeScriptFixtureExports).Assembly.Location,
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
                """
                export interface HiddenTypeJsonIncludeDto {
                  readonly public: string;
                }
                """,
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain("hiddenProperty", source, StringComparison.Ordinal);
            Assert.DoesNotContain("hiddenField", source, StringComparison.Ordinal);
            Assert.DoesNotContain("HiddenValue", source, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void Invoke_PreservesUnsupportedMemberConverterDiagnostic()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-member-converter-{Guid.NewGuid():N}.ts");
        const string existing = "// existing output\n";
        try
        {
            File.WriteAllText(outputPath, existing);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(MemberConverterFixtureExports).Assembly.Location,
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
                "ConverterControlledDto.Value: unsupported custom JsonConverter has no TypeScript mapping.",
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
    public void Invoke_IgnoresUnserializedNestedContextMembersAndUnreachedContexts()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-nested-context-safe-{Guid.NewGuid():N}.ts");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(NestedContextFixtureExports).Assembly.Location,
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
                """
                export interface NestedContextSafeDto {
                  readonly Public: string;
                }
                """,
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                """
                export interface SimpleDto {
                  readonly Value: string;
                }
                """,
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                """
                export interface CrossContextTopDto {
                  readonly Public: string;
                }
                """,
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                """
                export interface CrossContextNestedDto {
                  readonly Value: number;
                }
                """,
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "UnreachedNestedContextDto",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Ignored", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Shared", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Item", source, StringComparison.Ordinal);
            Assert.DoesNotContain("HiddenValue", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Hidden:", source, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void Invoke_RejectsReachedNestedContextProtectedValueTypes()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-nested-context-unsupported-{Guid.NewGuid():N}.ts");
        const string existing = "// existing output\n";
        try
        {
            File.WriteAllText(outputPath, existing);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(NestedContextProtectedValueDto)
                        .Assembly.Location,
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
                "same-assembly value types depend on nested JsonSerializerContext accessibility are unsupported",
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
    public void Invoke_RejectsNestedContextConstructorBoundValueTypes()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-nested-context-constructor-{Guid.NewGuid():N}.ts");
        const string existing = "// existing output\n";
        try
        {
            File.WriteAllText(outputPath, existing);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(NestedContextConstructorBoundDto)
                        .Assembly.Location,
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
                "same-assembly value types depend on nested JsonSerializerContext accessibility are unsupported",
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
                """import { dotnet } from "./dotnet.js";""",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "export function createRuntime(): Promise<JsExportRuntime>",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "export function initializeRuntime(",
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

    [Fact]
    public void ContextModeWritesCanonicalCompleteSet()
    {
        string outputPath = NewOutputPath("complete-set");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(MultiAssemblyContext).Assembly.Location,
                    "--context",
                    typeof(MultiAssemblyContext).FullName!,
                    "--assembly-search-path",
                    AppContext.BaseDirectory,
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
            Assert.Equal(
                [
                    "TsJsExport.ContextFixtures.Alpha.ts",
                    "TsJsExport.ContextFixtures.Beta.ts",
                    "TsJsExport.ContextFixtures.Host.ts",
                ],
                Directory.GetFiles(outputPath)
                    .Select(Path.GetFileName)
                    .Order(StringComparer.Ordinal));
        }
        finally
        {
            DeleteDirectory(outputPath);
        }
    }

    [Fact]
    public void ContextModeRejectsExistingOutputDirectory()
    {
        string outputPath = NewOutputPath("existing");
        try
        {
            int firstExitCode = TsJsExportCommand.Invoke(
                [
                    typeof(MultiAssemblyContext).Assembly.Location,
                    "--context",
                    typeof(MultiAssemblyContext).FullName!,
                    "--assembly-search-path",
                    AppContext.BaseDirectory,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    outputPath,
                ],
                new StringWriter(),
                new StringWriter());
            string[] originalFiles = Directory.GetFiles(outputPath);
            var output = new StringWriter();
            var error = new StringWriter();

            int secondExitCode = TsJsExportCommand.Invoke(
                [
                    typeof(AlphaOnlyContext).Assembly.Location,
                    "--context",
                    typeof(AlphaOnlyContext).FullName!,
                    "--assembly-search-path",
                    AppContext.BaseDirectory,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    outputPath,
                ],
                output,
                error);

            Assert.Equal(0, firstExitCode);
            Assert.Equal(1, secondExitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "context output path already exists",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(originalFiles, Directory.GetFiles(outputPath));
        }
        finally
        {
            DeleteDirectory(outputPath);
        }
    }

    [Fact]
    public void ContextAndDirectModesProduceIdenticalSingleFacade()
    {
        string outputDirectory = NewOutputPath("single-context");
        string directOutput = NewOutputPath("single-direct") + ".ts";
        try
        {
            var contextError = new StringWriter();
            int contextExitCode = TsJsExportCommand.Invoke(
                [
                    typeof(AlphaOnlyContext).Assembly.Location,
                    "--context",
                    typeof(AlphaOnlyContext).FullName!,
                    "--assembly-search-path",
                    AppContext.BaseDirectory,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    outputDirectory,
                ],
                new StringWriter(),
                contextError);
            var directError = new StringWriter();
            int directExitCode = TsJsExportCommand.Invoke(
                [
                    typeof(AlphaExports).Assembly.Location,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    directOutput,
                ],
                new StringWriter(),
                directError);

            Assert.Equal(0, contextExitCode);
            Assert.Equal(0, directExitCode);
            Assert.Empty(contextError.ToString());
            Assert.Empty(directError.ToString());
            Assert.Equal(
                File.ReadAllBytes(directOutput),
                File.ReadAllBytes(
                    Path.Combine(
                        outputDirectory,
                        "TsJsExport.ContextFixtures.Alpha.ts")));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
            File.Delete(directOutput);
        }
    }

    [Fact]
    public void ContextModeRequiresOutputDirectory()
    {
        var error = new StringWriter();

        int exitCode = TsJsExportCommand.Invoke(
            [
                typeof(AlphaOnlyContext).Assembly.Location,
                "--context",
                typeof(AlphaOnlyContext).FullName!,
                "--runtime-module",
                "./dotnet.js",
            ],
            new StringWriter(),
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "--output is required with --context",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblySearchPathRequiresContextMode()
    {
        var error = new StringWriter();

        int exitCode = TsJsExportCommand.Invoke(
            [
                typeof(AlphaExports).Assembly.Location,
                "--assembly-search-path",
                AppContext.BaseDirectory,
                "--runtime-module",
                "./dotnet.js",
            ],
            new StringWriter(),
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "--assembly-search-path requires --context",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextModeRequiresAssemblySearchPath()
    {
        string outputPath = NewOutputPath("missing-search-path");
        var error = new StringWriter();

        int exitCode = TsJsExportCommand.Invoke(
            [
                typeof(AlphaOnlyContext).Assembly.Location,
                "--context",
                typeof(AlphaOnlyContext).FullName!,
                "--runtime-module",
                "./dotnet.js",
                "--output",
                outputPath,
            ],
            new StringWriter(),
            error);

        Assert.Equal(1, exitCode);
        Assert.False(Path.Exists(outputPath));
        Assert.Contains(
            "at least one --assembly-search-path is required with --context",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextFailureDoesNotCreateOutputDirectory()
    {
        string outputPath = NewOutputPath("failure");
        var error = new StringWriter();

        int exitCode = TsJsExportCommand.Invoke(
            [
                typeof(EmptySurfaceContext).Assembly.Location,
                "--context",
                typeof(EmptySurfaceContext).FullName!,
                "--assembly-search-path",
                AppContext.BaseDirectory,
                "--runtime-module",
                "./dotnet.js",
                "--output",
                outputPath,
            ],
            new StringWriter(),
            error);

        Assert.Equal(1, exitCode);
        Assert.False(Path.Exists(outputPath));
        Assert.Contains(
            "has no supported [JSExport] methods",
            error.ToString(),
            StringComparison.Ordinal);
    }

    static string NewOutputPath(string scenario) =>
        Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-{scenario}-{Guid.NewGuid():N}");

    static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
