using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.OperatorFixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.JsExportSurface.ScalarFixtures;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

public sealed class TsBindGenCommandTests
{
    private const string DeclarationModuleSpecifier =
        "./fixture-contract.js";

    private static string FixtureAssemblyPath => typeof(FixtureExports).Assembly.Location;

    private static string CleanAssemblyPath => typeof(TsBindGenCommand).Assembly.Location;

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
        Assert.Contains("  readonly Name: string;", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("  readonly Count: number;", output.ToString(), StringComparison.Ordinal);
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
            Assert.Contains(
                "readonly tags: ReadonlyArray<number>;",
                error.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Invoke_WithMissingDiffAgainst_DoesNotOverwriteExistingJavaScript()
    {
        string diffPath = Path.Combine(AppContext.BaseDirectory, "tsbindgen-missing-diff.d.ts");
        string emitJsPath = Path.Combine(AppContext.BaseDirectory, "tsbindgen-missing-diff.js");
        const string existingJavaScript = "// existing JavaScript wrapper";
        try
        {
            File.Delete(diffPath);
            File.WriteAllText(emitJsPath, existingJavaScript);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    CleanAssemblyPath,
                    "--diff-against",
                    diffPath,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("--diff-against file not found", error.ToString(), StringComparison.Ordinal);
            Assert.Equal(existingJavaScript, File.ReadAllText(emitJsPath));
        }
        finally
        {
            File.Delete(diffPath);
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_WithStaleDiffAgainst_DoesNotOverwriteExistingJavaScript()
    {
        string diffPath = Path.Combine(AppContext.BaseDirectory, "tsbindgen-stale-diff.d.ts");
        string emitJsPath = Path.Combine(AppContext.BaseDirectory, "tsbindgen-stale-diff.js");
        const string existingJavaScript = "// existing JavaScript wrapper";
        try
        {
            File.WriteAllText(diffPath, "stale declarations");
            File.WriteAllText(emitJsPath, existingJavaScript);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    CleanAssemblyPath,
                    "--diff-against",
                    diffPath,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("drift detected", error.ToString(), StringComparison.Ordinal);
            Assert.Equal(existingJavaScript, File.ReadAllText(emitJsPath));
        }
        finally
        {
            File.Delete(diffPath);
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_WithCoveredDiffAgainst_PublishesJavaScript()
    {
        var generatedOutput = new StringWriter();
        var generatedError = new StringWriter();
        Assert.Equal(
            0,
            TsBindGenCommand.Invoke(
                [CleanAssemblyPath],
                generatedOutput,
                generatedError));
        Assert.Empty(generatedError.ToString());

        string diffPath = Path.Combine(AppContext.BaseDirectory, "tsbindgen-covered-diff.d.ts");
        string emitJsPath = Path.Combine(AppContext.BaseDirectory, "tsbindgen-covered-diff.js");
        try
        {
            File.WriteAllText(diffPath, generatedOutput.ToString());
            File.Delete(emitJsPath);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    CleanAssemblyPath,
                    "--diff-against",
                    diffPath,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Contains("no drift detected", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
            Assert.Contains(
                "export async function initializeEngine",
                File.ReadAllText(emitJsPath),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(diffPath);
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_EmitJsRequiresDeclarationModule()
    {
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-missing-declaration-module.js");
        try
        {
            File.Delete(emitJsPath);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [CleanAssemblyPath, "--emit-js", emitJsPath],
                output,
                error);

            Assert.Equal(
                1,
                exitCode);
            Assert.Empty(output.ToString());
            Assert.Equal(
                "tsbindgen: --emit-js requires --declaration-module."
                    + Environment.NewLine,
                error.ToString());
            Assert.False(File.Exists(emitJsPath));
        }
        finally
        {
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_DeclarationModuleRequiresJavaScriptOutput()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke(
            [CleanAssemblyPath, "--declaration-module", "./custom.js"],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Equal(
            "tsbindgen: --declaration-module requires --emit-js."
                + Environment.NewLine,
            error.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Invoke_RefusesEmptyDeclarationModule(
        string declarationModuleSpecifier)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke(
            [
                CleanAssemblyPath,
                "--emit-js",
                "unused.js",
                "--declaration-module",
                declarationModuleSpecifier,
            ],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Equal(
            "tsbindgen: --declaration-module must not be empty or whitespace."
                + Environment.NewLine,
            error.ToString());
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
    public void Invoke_WithDiagnostics_DoesNotAttemptInvalidEmitJsPath()
    {
        string missingDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "missing-tsbindgen-output-directory",
            "generated.js");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke(
            [
                FixtureAssemblyPath,
                "--emit-js",
                missingDirectory,
                "--declaration-module",
                DeclarationModuleSpecifier,
            ],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "has no TypeScript mapping",
            error.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
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
    public void Invoke_IncompleteExtractionFailsWithoutOutput()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-incomplete-surface.dll");
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-incomplete-surface.js");
        try
        {
            File.WriteAllBytes(assemblyPath, BuildIncompleteSurfaceImage());
            File.Delete(emitJsPath);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    assemblyPath,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.False(File.Exists(emitJsPath));
            Assert.Contains(
                "metadata token 0x02000002: metadata extraction did not "
                    + "produce a complete surface.",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain("Rejected", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(assemblyPath);
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_ManifestlessModuleContainsEmitJsFailure()
    {
        string modulePath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-manifestless.netmodule");
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-manifestless.js");
        const string existingJavaScript = "// existing JavaScript wrapper";
        try
        {
            File.WriteAllBytes(
                modulePath,
                BuildManifestlessModuleImage());
            File.WriteAllText(
                emitJsPath,
                existingJavaScript);

            var declarationOutput = new StringWriter();
            var declarationError = new StringWriter();
            Assert.Equal(
                0,
                TsBindGenCommand.Invoke(
                    [modulePath],
                    declarationOutput,
                    declarationError));
            Assert.Empty(declarationError.ToString());

            var output = new StringWriter();
            var error = new StringWriter();
            int exitCode = TsBindGenCommand.Invoke(
                [
                    modulePath,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Equal(
                "tsbindgen: --emit-js requires an assembly manifest identity."
                    + Environment.NewLine,
                error.ToString());
            Assert.Equal(
                existingJavaScript,
                File.ReadAllText(emitJsPath));
        }
        finally
        {
            File.Delete(modulePath);
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_PrintsDiagnosticsAndReturnsOneForUnmappedTypes()
    {
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-unmapped.js");
        File.Delete(emitJsPath);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = TsBindGenCommand.Invoke(
            [
                typeof(NeedsUnmappedTypeFixtureExports).Assembly.Location,
                "--emit-js",
                emitJsPath,
                "--declaration-module",
                DeclarationModuleSpecifier,
            ],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("NeedsUnmappedTypeFixture.Unmapped: System.Guid has no TypeScript mapping.", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("unknown", output.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(emitJsPath));
    }

    [Fact]
    public void Invoke_ControlCharacterJsonPropertyNameOnFieldFailsWithoutDeclarationOutput()
    {
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-control-property-name.js");
        try
        {
            File.Delete(emitJsPath);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    typeof(ControlPropertyNameFixture).Assembly.Location,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            Assert.False(File.Exists(emitJsPath));
            Assert.Contains(
                "member 0x",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Contains(
                " [JsonPropertyName]: control-character JSON property names "
                    + "are not supported.",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain("field\nbreak", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_UnsupportedScalarContextOptionsFailsBeforeDeclarationOrWrapperPublication()
    {
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-unsupported-scalar-context-options.js");
        const string existingJavaScript = "// existing JavaScript wrapper";
        try
        {
            File.WriteAllText(emitJsPath, existingJavaScript);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    typeof(ScalarContextOptionsFixtureExports).Assembly.Location,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "serializer context options are unsupported",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(existingJavaScript, File.ReadAllText(emitJsPath));
        }
        finally
        {
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_JsExportOperatorFailsBeforeDeclarationOrWrapperPublication()
    {
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-js-export-operator.js");
        const string existingJavaScript = "// existing JavaScript wrapper";
        try
        {
            File.WriteAllText(emitJsPath, existingJavaScript);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    typeof(JsExportOperatorFixture).Assembly.Location,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "JS exports must be ordinary methods",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(existingJavaScript, File.ReadAllText(emitJsPath));
        }
        finally
        {
            File.Delete(emitJsPath);
        }
    }

    [Fact]
    public void Invoke_FilteredGeneratedTypeExportFailsBeforePublication()
    {
        string emitJsPath = Path.Combine(
            AppContext.BaseDirectory,
            "tsbindgen-filtered-generated-type-export.js");
        const string existingJavaScript = "// existing JavaScript wrapper";
        try
        {
            File.WriteAllText(emitJsPath, existingJavaScript);
            var output = new StringWriter();
            var error = new StringWriter();

            int exitCode = TsBindGenCommand.Invoke(
                [
                    typeof(LambdaExportFixture).Assembly.Location,
                    "--emit-js",
                    emitJsPath,
                    "--declaration-module",
                    DeclarationModuleSpecifier,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains(
                "filtered MethodDefs",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(existingJavaScript, File.ReadAllText(emitJsPath));
        }
        finally
        {
            File.Delete(emitJsPath);
        }
    }

    static byte[] BuildIncompleteSurfaceImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle rejected = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Rejected"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(rejected, rejected);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Sibling"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildManifestlessModuleImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
