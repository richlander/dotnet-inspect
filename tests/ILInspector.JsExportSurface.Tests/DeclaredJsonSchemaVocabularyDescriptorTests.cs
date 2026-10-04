using System.Text.Json;
using System.Reflection.PortableExecutable;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.JsonSchema;
using DotnetInspector.Sections;
using DotnetInspector.Vocabulary;
using ILInspector.JsExportSurface;
using ILInspector.JsExportSurface.JsonSchemaFixtures;
using ILInspector.Metadata;
using ILInspector.TypeScriptGeneration;
using QuerySpace.Vocabulary;
using TsJsExport;

namespace ILInspector.JsExportSurface.Tests;

public sealed class DeclaredJsonSchemaVocabularyDescriptorTests
{
    [Fact]
    public void Extract_PreservesSchemaDeclarationAndNamedDisplayability()
    {
        using FileStream stream = File.OpenRead(
            typeof(JsonSchemaMetadataFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface =
            ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
        ApiType type = Assert.Single(
            apiSurface.Types,
            candidate => candidate.FullName
                == typeof(JsonSchemaMetadataFixture).FullName);
        ApiJsExportJsonSchemaDeclaration declaration =
            Assert.IsType<ApiJsExportJsonSchemaDeclaration>(
                type.JsExportJsonSchemaDeclaration);
        Assert.Equal("fixture.schema", declaration.ContractIdentity);
        Assert.Equal("serialize", declaration.Direction);
        Assert.Equal("fixture.catalog", declaration.VocabularyCatalog);
        Assert.Null(declaration.UnsupportedReason);

        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name == "Value"
                && candidate.Kind == "property");
        ApiJsExportJsonSchemaSlotDeclaration slot =
            Assert.IsType<ApiJsExportJsonSchemaSlotDeclaration>(
                member.JsExportJsonSchemaSlotDeclaration);
        Assert.Equal(0, slot.Order);
        Assert.Equal("value", slot.NodeIdentity);
        Assert.Equal("fixture.rows", slot.Vocabulary);
        Assert.Equal("value", slot.Term);
        Assert.False(slot.Displayable);
        Assert.Null(slot.UnsupportedReason);
    }

    [Fact]
    public void Build_RejectsIncompleteAndNonContiguousDeclaredRows()
    {
        JsonSchemaVocabularyException missingSlot =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                BuildDeclaredFixture<MissingSchemaSlotFixture>());
        Assert.Contains(
            "do not exactly cover",
            missingSlot.Reason,
            StringComparison.Ordinal);

        JsonSchemaVocabularyException duplicateOrder =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                BuildDeclaredFixture<DuplicateSchemaOrderFixture>());
        Assert.Contains(
            "contiguous and zero-based",
            duplicateOrder.Reason,
            StringComparison.Ordinal);

        JsonSchemaVocabularyException skippedOrder =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                BuildDeclaredFixture<SkippedSchemaOrderFixture>());
        Assert.Contains(
            "contiguous and zero-based",
            skippedOrder.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void PackageFacade_PublishesAuthenticatedDurableRowDescriptor()
    {
        string path =
            typeof(BrowserCompileLibraryAvailability).Assembly.Location;
        var error = new StringWriter();
        bool loaded = JsExportSurfaceLoader.TryLoad(
            path,
            [AppContext.BaseDirectory],
            JsExportContractIdentity.Api,
            "test",
            error,
            out global::ILInspector.JsExportSurface.JsExportSurface?
                surface);
        Assert.True(loaded, error.ToString());
        global::ILInspector.JsExportSurface.JsExportSurface
            loadedSurface = Assert.IsType<
                global::ILInspector.JsExportSurface.JsExportSurface>(
                    surface);

        IReadOnlyList<TypeScriptStaticJsonExport> exports =
            DeclaredJsonSchemaExports.Create(
                loadedSurface,
                out JsonWireDeclarationPlan declarationPlan);
        var inspection = new JsonSchemaVocabularyInspection(
            loadedSurface,
            declarationPlan,
            JsExportContractIdentity.Api,
            VocabularySnapshotReference.FromSnapshot(
                VocabularyCatalog.Snapshot));
        JsonSchemaVocabularyInspectionRequest request =
            Assert.Single(inspection.Requests);
        InspectionEnvelope<JsonSchemaVocabularyDescriptor> envelope =
            inspection.Inspect(request);
        Assert.Equal(
            "package-query.durable-row",
            envelope.Content.Contract.Value);
        Assert.Empty(envelope.Diagnostics);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        var staleRequest = request with
        {
            VocabularySnapshotIdentity = new(
                "sha256:0000000000000000000000000000000000000000000000000000000000000000"),
        };
        JsonSchemaVocabularyException staleException = Assert.Throws<
            JsonSchemaVocabularyException>(
                () => inspection.Inspect(staleRequest));
        Assert.Contains(
            "snapshot does not match",
            staleException.Reason,
            StringComparison.Ordinal);
        JsonSchemaVocabularyException staleProducerSnapshot =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                new JsonSchemaVocabularyInspection(
                    loadedSurface,
                    declarationPlan,
                    JsExportContractIdentity.Api,
                    new VocabularySnapshotReference(
                        VocabularyCatalog.Snapshot.Catalog,
                        new(
                            "sha256:0000000000000000000000000000000000000000000000000000000000000000"),
                        [])));
        Assert.Contains(
            "provided snapshot",
            staleProducerSnapshot.Reason,
            StringComparison.Ordinal);

        TypeScriptStaticJsonExport descriptorExport =
            Assert.Single(exports);
        Assert.Equal(
            DeclaredJsonSchemaExports.ExportName,
            descriptorExport.Name);
        JsonElement descriptor = Assert.Single(
            descriptorExport.Value.EnumerateArray());
        Assert.Equal(
            "package-query.durable-row",
            descriptor.GetProperty("contract").GetString());
        Assert.Equal(
            "serialize",
            descriptor.GetProperty("direction").GetString());
        Assert.Equal(
            VocabularyCatalog.Snapshot.Identity.Value,
            descriptor
                .GetProperty("vocabularySnapshotIdentity")
                .GetString());
        Assert.Equal(
            12,
            descriptor.GetProperty("bindings").GetArrayLength());
        Assert.Equal(
            Enumerable.Range(0, 12)
                .Select(index => $"/prefixItems/{index}"),
            descriptor.GetProperty("bindings")
                .EnumerateArray()
                .Select(binding =>
                    binding.GetProperty("schemaLocation")
                        .GetString()));
        Assert.Equal(
            12,
            descriptor.GetProperty("schema")
                .GetProperty("prefixItems")
                .GetArrayLength());

        string source = TypeScriptFacadeEmitter.Emit(
            loadedSurface,
            "./runtime-loader.js",
            staticJsonExports: exports,
            declarationPlan: declarationPlan);
        Assert.Contains(
            "export const jsonSchemaVocabularyDescriptors = "
                + descriptorExport.Value.GetRawText()
                + " as const;",
            source,
            StringComparison.Ordinal);
    }

    static void BuildDeclaredFixture<T>()
    {
        using FileStream stream = File.OpenRead(typeof(T).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface =
            ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
        ApiType type = Assert.Single(
            apiSurface.Types,
            candidate => candidate.FullName == typeof(T).FullName);
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = apiSurface.AssemblyIdentity,
                Records = [type],
                AllTypes = apiSurface.Types,
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [type] = JsonWireDirection.Serialize,
                    },
                JsonSchemaRoots =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [type] = JsonWireDirection.Serialize,
                    },
            };
        JsonWireDeclarationPlan plan =
            JsonWireDeclarationPlan.Create(surface);
        DeclaredJsonSchemaVocabularyDescriptorBuilder.Build(
            surface,
            plan,
            JsExportContractIdentity.Api,
            new VocabularySnapshotReference(
                new("fixture.catalog"),
                new(
                    "sha256:0000000000000000000000000000000000000000000000000000000000000000"),
                [
                    new(
                        new(
                            new("fixture.catalog"),
                            "fixture.rows"),
                        "first"),
                    new(
                        new(
                            new("fixture.catalog"),
                            "fixture.rows"),
                        "second"),
                ]));
    }
}
