using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.PolymorphicContractsFixtures;
using ILInspector.JsExportSurface.PolymorphicExportFixtures;
using ILInspector.JsExportSurface.UnsupportedPolymorphicExportFixtures;
using ILInspector.Metadata;
using TsJsExport;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsonPolymorphicWireTests
{
    static readonly Lazy<LibraryBodyIndex> Bodies = new(() =>
        LibraryBodyIndex.Open(
            typeof(PolymorphicExports).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow));
    static readonly Lazy<LibraryBodyIndex> ContractBodies = new(() =>
        LibraryBodyIndex.Open(
            typeof(PackageDocumentationOutcome).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow));

    [Fact]
    public void Extract_RetainsAuthenticStringAndUnsupportedNumericMetadata()
    {
        ApiSurface contracts = Extract(
            typeof(PackageDocumentationOutcome).Assembly.Location);
        ApiType valid = Assert.Single(
            contracts.Types,
            type => type.FullName
                == typeof(PackageDocumentationOutcome).FullName);
        ApiJsonPolymorphismEvidence validMetadata =
            Assert.IsType<ApiJsonPolymorphismEvidence>(
                valid.JsonPolymorphism);

        Assert.Equal(1, validMetadata.PolymorphicAttributeCount);
        Assert.Equal("kind", validMetadata.TypeDiscriminatorPropertyName);
        Assert.Equal(2, validMetadata.DerivedTypeAttributeCount);
        Assert.Equal(
            ["available", "absent"],
            validMetadata.DerivedTypes.Select(
                derived => derived.TypeDiscriminator));
        Assert.Null(validMetadata.UnsupportedReason);

        ApiType numeric = Assert.Single(
            contracts.Types,
            type => type.FullName == typeof(NumericOutcome).FullName);
        ApiJsonPolymorphismEvidence numericMetadata =
            Assert.IsType<ApiJsonPolymorphismEvidence>(
                numeric.JsonPolymorphism);
        Assert.Equal(1, numericMetadata.DerivedTypeAttributeCount);
        Assert.Empty(numericMetadata.DerivedTypes);
        Assert.Contains(
            "string discriminator",
            numericMetadata.UnsupportedReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProducesReferencedDiscriminatedObjectUnion()
    {
        var surface = Build(nameof(PolymorphicExports.GetDocumentation));
        JsExportPolymorphicUnion union = Assert.Single(
            surface.PolymorphicUnions,
            candidate => candidate.Definition.FullName
                == typeof(PackageDocumentationOutcome).FullName);

        Assert.Null(union.UnsupportedReason);
        Assert.Equal("kind", union.TypeDiscriminatorPropertyName);
        Assert.Equal(
            ["available", "absent"],
            union.Cases.Select(@case => @case.TypeDiscriminator));
        Assert.All(
            union.Cases,
            @case => Assert.DoesNotContain(
                @case.Definition,
                surface.Records));
        Assert.Equal(
            JsonWireDirection.Serialize,
            surface.WireDirections[union.Definition]);
        Assert.All(
            union.Cases,
            @case => Assert.Equal(
                JsonWireDirection.Serialize,
                surface.WireDirections[@case.Definition]));

        string source = GenerateFacade(
            typeof(PolymorphicExports).Assembly.Location);

        Assert.Contains(
            """
            export interface Available {
              readonly kind: "available";
              readonly subject?: PackageSubject;
              readonly detail?: string;
              readonly documentation?: PackageDocumentation;
              readonly source_kind: PackageSourceKind;
              readonly evidence?: JsonValue;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface Absent {
              readonly kind: "absent";
              readonly subject?: PackageSubject;
              readonly detail?: string;
              readonly reason?: string;
              readonly sourcesTruncated?: boolean;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type JsonValue =",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type PackageDocumentationOutcome = Available | Absent;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type PackageSourceKind = \"Package\" | \"Platform\" | number;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly version?: string;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "getDocumentation(available: boolean): PackageDocumentationOutcome",
            source,
            StringComparison.Ordinal);

        using JsonDocument payload = JsonDocument.Parse(
            PolymorphicExports.GetDocumentation(available: true));
        JsonElement root = payload.RootElement;
        Assert.Equal("available", root.GetProperty("kind").GetString());
        Assert.Equal(
            "Example.Package",
            root.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal(
            "Package",
            root.GetProperty("source_kind").GetString());
        Assert.Equal(
            "Summary",
            root.GetProperty("documentation")
                .GetProperty("summary")
                .GetString());
        Assert.False(root.TryGetProperty("evidence", out _));
    }

    [Fact]
    public void Emit_RejectsNonStringPolymorphicDiscriminator()
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-polymorphic-unsupported-{Guid.NewGuid():N}.ts");
        const string existing = "// existing output\n";
        File.WriteAllText(outputPath, existing);
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            int exitCode = TsJsExportCommand.Invoke(
                [
                    typeof(UnsupportedPolymorphicExports).Assembly.Location,
                    "--assembly-search-path",
                    typeof(PackageDocumentationOutcome).Assembly.Location,
                    "--runtime-module",
                    "./dotnet.js",
                    "--output",
                    outputPath,
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Equal(existing, File.ReadAllText(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
        Assert.Contains(
            "NumericOutcome JSON polymorphism",
            error.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "string discriminator",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RejectsDuplicateDerivedTypeDiscriminator()
    {
        var surface = Build(
            nameof(PolymorphicExports.GetDocumentation),
            contracts =>
            {
                ApiType root = Assert.Single(
                    contracts.Types,
                    type => type.FullName
                        == typeof(PackageDocumentationOutcome).FullName);
                ApiJsonPolymorphismEvidence metadata =
                    Assert.IsType<ApiJsonPolymorphismEvidence>(
                        root.JsonPolymorphism);
                root.JsonPolymorphism = metadata with
                {
                    DerivedTypeAttributeCount =
                        metadata.DerivedTypeAttributeCount + 1,
                    DerivedTypes =
                    [
                        .. metadata.DerivedTypes,
                        metadata.DerivedTypes[0] with
                        {
                            Type = metadata.DerivedTypes[1].Type,
                        },
                    ],
                };
            });

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => TypeScriptFacadeEmitter.Emit(
                    surface,
                    "./dotnet.js"));

        Assert.Contains(
            "cases and string discriminators must be unique",
            exception.Message,
            StringComparison.Ordinal);
    }

    static string GenerateFacade(string assemblyPath)
    {
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            $"ts-jsexport-polymorphic-{Guid.NewGuid():N}.ts");
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            int exitCode = TsJsExportCommand.Invoke(
                [
                    assemblyPath,
                    "--assembly-search-path",
                    typeof(PackageDocumentationOutcome).Assembly.Location,
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
            return File.ReadAllText(outputPath);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    static ILInspector.JsExportSurface.JsExportSurface Build(
        string method,
        Action<ApiSurface>? configureContracts = null)
    {
        ApiSurface exports = Extract(
            typeof(PolymorphicExports).Assembly.Location);
        foreach (ApiMember member
            in exports.Types.SelectMany(type => type.Members))
        {
            if (member.HasRuntimeJsExport
                && member.Name != method)
            {
                member.HasRuntimeJsExport = false;
                member.RuntimeJsExportAttributeCount = 0;
                member.HasMalformedRuntimeJsExportAttribute = false;
            }
        }

        ApiSurface contracts = Extract(
            typeof(PackageDocumentationOutcome).Assembly.Location);
        configureContracts?.Invoke(contracts);
        ApiAssemblyIdentity contractIdentity =
            Assert.IsType<ApiAssemblyIdentity>(
                contracts.AssemblyIdentity);
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            referencedTypeDefinitions =
                contracts.Types.ToDictionary(
                    type => new ApiTypeReferenceIdentity(
                        contractIdentity,
                        type.FullName,
                        type.DefinitionName));

        return JsExportSurfaceBuilder.Build(
            exports,
            Bodies.Value,
            referencedTypeDefinitions,
            contracts.Types.ToDictionary(
                type => type,
                _ => ContractBodies.Value));
    }

    static ApiSurface Extract(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(pe, includeAll: true);
    }
}
