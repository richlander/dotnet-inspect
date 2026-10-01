using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using DotnetInspector.JsonSchema;
using DotnetInspector.Vocabulary;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsonSchemaVocabularyDescriptorBuilderTests
{
    [Fact]
    public void Build_PublishesCanonicalPositionalRowAndStableBindings()
    {
        VocabularySnapshot snapshot = CreateSnapshot();
        JsonPositionalRowContract row = CreateRow();
        JsonSchemaContractDeclaration declaration = new(
            new("package-query.durable-row"),
            JsonWireDirection.Serialize,
            new JsonSchemaContractRoot.Positional(row),
            [
                new(
                    new JsonSchemaBindingTarget.PositionalSlot(
                        row,
                        row.Slots[0]),
                    Term(snapshot, "package")),
                new(
                    new JsonSchemaBindingTarget.PositionalSlot(
                        row,
                        row.Slots[1]),
                    Term(snapshot, "downloads")),
            ],
            JsonSchemaBindingCoverage.CompleteDisplaySlots);

        JsonSchemaVocabularyDescriptor first = Build(
            declaration,
            snapshot,
            snapshot.Identity);
        JsonSchemaVocabularyDescriptor second = Build(
            declaration,
            snapshot,
            snapshot.Identity);

        Assert.Equal(first.SchemaIdentity, second.SchemaIdentity);
        Assert.Equal(
            first.DescriptorIdentity,
            second.DescriptorIdentity);
        Assert.Equal(
            JsonSchemaVocabularyDescriptorBuilder.SerializeCanonical(
                first),
            JsonSchemaVocabularyDescriptorBuilder.SerializeCanonical(
                second));
        Assert.Equal(
            "urn:dotnet-inspect:json-schema:"
                + first.SchemaIdentity.Value,
            first.Schema.GetProperty("$id").GetString());
        Assert.Equal(
            JsonSchemaVocabularyDescriptorBuilder.Dialect,
            first.Schema.GetProperty("$schema").GetString());
        Assert.Equal(
            JsonValueKind.Array,
            first.Schema.GetProperty("prefixItems").ValueKind);
        Assert.Equal(
            2,
            first.Schema.GetProperty("minItems").GetInt32());
        Assert.Equal(
            2,
            first.Schema.GetProperty("maxItems").GetInt32());
        Assert.False(
            first.Schema.GetProperty("items").GetBoolean());
        Assert.Collection(
            first.Bindings,
            binding =>
            {
                Assert.Equal(
                    "/prefixItems/0",
                    binding.SchemaLocation);
                Assert.Equal("package", binding.Term.Value);
            },
            binding =>
            {
                Assert.Equal(
                    "/prefixItems/1",
                    binding.SchemaLocation);
                Assert.Equal("downloads", binding.Term.Value);
            });
    }

    [Fact]
    public void Build_RejectsStaleVocabularySnapshot()
    {
        VocabularySnapshot snapshot = CreateSnapshot();
        JsonPositionalRowContract row = CreateRow();
        JsonSchemaContractDeclaration declaration = new(
            new("package-query.durable-row"),
            JsonWireDirection.Serialize,
            new JsonSchemaContractRoot.Positional(row));
        var stale = new VocabularySnapshotIdentity(
            $"sha256:{new string('0', 64)}");

        JsonSchemaVocabularyException exception =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                Build(declaration, snapshot, stale));

        Assert.Contains(
            "expected vocabulary snapshot",
            exception.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsUnknownTermAndDuplicateLocation()
    {
        VocabularySnapshot snapshot = CreateSnapshot();
        JsonPositionalRowContract row = CreateRow();
        JsonSchemaBindingTarget target =
            new JsonSchemaBindingTarget.PositionalSlot(
                row,
                row.Slots[0]);
        var unknown = new VocabularyTermIdentity(
            snapshot.Vocabularies[0].Identity,
            "unknown");
        JsonSchemaContractDeclaration unknownDeclaration = new(
            new("package-query.durable-row"),
            JsonWireDirection.Serialize,
            new JsonSchemaContractRoot.Positional(row),
            [new(target, unknown)]);

        JsonSchemaVocabularyException unknownException =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                Build(
                    unknownDeclaration,
                    snapshot,
                    snapshot.Identity));
        Assert.Contains(
            "has no term",
            unknownException.Reason,
            StringComparison.Ordinal);

        JsonSchemaContractDeclaration duplicateDeclaration = new(
            new("package-query.durable-row"),
            JsonWireDirection.Serialize,
            new JsonSchemaContractRoot.Positional(row),
            [
                new(target, Term(snapshot, "package")),
                new(target, Term(snapshot, "downloads")),
            ]);

        JsonSchemaVocabularyException duplicateException =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                Build(
                    duplicateDeclaration,
                    snapshot,
                    snapshot.Identity));
        Assert.Equal(
            "multiple vocabulary terms target one schema location",
            duplicateException.Reason);
    }

    [Fact]
    public void Build_RequiresEveryDisplaySlotWhenCoverageIsComplete()
    {
        VocabularySnapshot snapshot = CreateSnapshot();
        JsonPositionalRowContract row = CreateRow();
        JsonSchemaContractDeclaration declaration = new(
            new("package-query.durable-row"),
            JsonWireDirection.Serialize,
            new JsonSchemaContractRoot.Positional(row),
            [
                new(
                    new JsonSchemaBindingTarget.PositionalSlot(
                        row,
                        row.Slots[0]),
                    Term(snapshot, "package")),
            ],
            JsonSchemaBindingCoverage.CompleteDisplaySlots);

        JsonSchemaVocabularyException exception =
            Assert.Throws<JsonSchemaVocabularyException>(() =>
                Build(declaration, snapshot, snapshot.Identity));

        Assert.Equal(
            "/prefixItems/1",
            exception.Location);
        Assert.Equal(
            "a displayable positional slot has no vocabulary binding",
            exception.Reason);
    }

    [Fact]
    public void Build_ReplacesOnlyIdentitiesAffectedByContractChanges()
    {
        VocabularySnapshot snapshot = CreateSnapshot();
        JsonPositionalRowContract row = CreateRow();
        JsonSchemaVocabularyDescriptor baseline = Build(
            Declaration(
                row,
                Term(snapshot, "package")),
            snapshot,
            snapshot.Identity);
        JsonSchemaVocabularyDescriptor rebound = Build(
            Declaration(
                row,
                Term(snapshot, "downloads")),
            snapshot,
            snapshot.Identity);
        VocabularySnapshot renamedSnapshot =
            CreateSnapshot("Renamed package query");
        JsonSchemaVocabularyDescriptor resnapshotted = Build(
            Declaration(
                row,
                Term(renamedSnapshot, "package")),
            renamedSnapshot,
            renamedSnapshot.Identity);
        var reordered = new JsonPositionalRowContract(
            row.Slots.Reverse());
        JsonSchemaVocabularyDescriptor reorderedDescriptor = Build(
            Declaration(
                reordered,
                Term(snapshot, "package")),
            snapshot,
            snapshot.Identity);

        Assert.Equal(
            baseline.SchemaIdentity,
            rebound.SchemaIdentity);
        Assert.NotEqual(
            baseline.DescriptorIdentity,
            rebound.DescriptorIdentity);
        Assert.Equal(
            baseline.SchemaIdentity,
            resnapshotted.SchemaIdentity);
        Assert.NotEqual(
            baseline.DescriptorIdentity,
            resnapshotted.DescriptorIdentity);
        Assert.NotEqual(
            baseline.SchemaIdentity,
            reorderedDescriptor.SchemaIdentity);
        Assert.NotEqual(
            baseline.DescriptorIdentity,
            reorderedDescriptor.DescriptorIdentity);

        static JsonSchemaContractDeclaration Declaration(
            JsonPositionalRowContract row,
            VocabularyTermIdentity term) =>
            new(
                new("package-query.durable-row"),
                JsonWireDirection.Serialize,
                new JsonSchemaContractRoot.Positional(row),
                [
                    new(
                        new JsonSchemaBindingTarget.PositionalSlot(
                            row,
                            row.Slots[0]),
                        term),
                ]);
    }

    [Fact]
    public void Build_PreservesInBoxIntegerKindAndAddsExactRange()
    {
        VocabularySnapshot snapshot = CreateSnapshot();
        var row = new JsonPositionalRowContract(
            [
                new(
                    "count",
                    ApiTypeShape.PrimitiveType(
                        ApiPrimitiveType.Int32),
                    allowsNull: false),
            ]);
        JsonSchemaVocabularyDescriptor descriptor = Build(
            new(
                new("integer-row"),
                JsonWireDirection.Serialize,
                new JsonSchemaContractRoot.Positional(row)),
            snapshot,
            snapshot.Identity);
        JsonNode actual = JsonNode.Parse(
            descriptor.Schema.GetProperty("prefixItems")[0]
                .GetRawText())!;
        JsonNode expected =
            JsonSerializerOptions.Default.GetJsonSchemaAsNode(
                typeof(int));

        Assert.Equal(
            expected["type"]!.GetValue<string>(),
            actual["type"]!.GetValue<string>());
        Assert.Equal(
            int.MinValue,
            actual["minimum"]!.GetValue<long>());
        Assert.Equal(
            int.MaxValue,
            actual["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Build_KeepsSameNamedDefinitionsFromDifferentAssembliesDistinct()
    {
        MetadataTypeDefinitionName definitionName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Contracts",
                    ["Value"])).Name;
        var leftIdentity = new ApiTypeReferenceIdentity(
            new("LibraryA", new Version(1, 0), null, null),
            "Contracts.Value",
            definitionName);
        var rightIdentity = new ApiTypeReferenceIdentity(
            new("LibraryB", new Version(1, 0), null, null),
            "Contracts.Value",
            definitionName);
        ApiType left = Record(
            "Text",
            ApiTypeShape.PrimitiveType(
                ApiPrimitiveType.String));
        ApiType right = Record(
            "Count",
            ApiTypeShape.PrimitiveType(
                ApiPrimitiveType.Int32));
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                Records = [left, right],
                ReferencedTypeDefinitions =
                    new Dictionary<ApiTypeReferenceIdentity, ApiType>
                    {
                        [leftIdentity] = left,
                        [rightIdentity] = right,
                    },
            };
        var row = new JsonPositionalRowContract(
            [
                new(
                    "left",
                    ApiTypeShape.Named(
                        leftIdentity,
                        isValueType: false),
                    allowsNull: false),
                new(
                    "right",
                    ApiTypeShape.Named(
                        rightIdentity,
                        isValueType: false),
                    allowsNull: false),
            ]);
        VocabularySnapshot snapshot = CreateSnapshot();

        JsonSchemaVocabularyDescriptor descriptor =
            JsonSchemaVocabularyDescriptorBuilder.Build(
                surface,
                JsonWireDeclarationPlan.Create(surface),
                new(
                    new("same-name-row"),
                    JsonWireDirection.Serialize,
                    new JsonSchemaContractRoot.Positional(row)),
                snapshot,
                snapshot.Identity);

        JsonElement definitions =
            descriptor.Schema.GetProperty("$defs");
        JsonElement prefixItems =
            descriptor.Schema.GetProperty("prefixItems");
        string leftName = ReferenceName(prefixItems[0]);
        string rightName = ReferenceName(prefixItems[1]);
        Assert.NotEqual(leftName, rightName);
        JsonElement leftProperties = definitions
            .GetProperty(leftName)
            .GetProperty("properties");
        JsonElement rightProperties = definitions
            .GetProperty(rightName)
            .GetProperty("properties");
        Assert.True(leftProperties.TryGetProperty("Text", out _));
        Assert.False(leftProperties.TryGetProperty("Count", out _));
        Assert.True(rightProperties.TryGetProperty("Count", out _));
        Assert.False(rightProperties.TryGetProperty("Text", out _));

        ApiType Record(
            string memberName,
            ApiTypeShape memberShape) =>
            new()
            {
                Namespace = "Contracts",
                Name = "Value",
                DefinitionName = definitionName,
                Kind = "class",
                Members =
                [
                    new()
                    {
                        Name = memberName,
                        Kind = "property",
                        HasGetter = true,
                        IndexParameterCount = 0,
                        SignatureModel = new()
                        {
                            ReturnTypeShape = memberShape,
                        },
                    },
                ],
            };

        static string ReferenceName(JsonElement schema) =>
            schema.GetProperty("$ref")
                .GetString()!["#/$defs/".Length..];
    }

    [Fact]
    public void Build_RejectsTypeLevelUnmappedMemberHandling()
    {
        string path = typeof(StrictUnmappedInputFixture)
            .Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface =
            ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
        ApiType type = Assert.Single(
            apiSurface.Types,
            candidate => candidate.Name
                == nameof(StrictUnmappedInputFixture));
        Assert.False(type.HasUnsupportedJsonWireAttributes);
        Assert.Equal(
            JsonWireUnmappedMemberHandling.Disallow,
            type.JsonUnmappedMemberHandling);
        ApiAssemblyIdentity assembly = Assert.IsType<
            ApiAssemblyIdentity>(apiSurface.AssemblyIdentity);
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Records = [type],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [type] = JsonWireDirection.Deserialize,
                    },
            };
        VocabularySnapshot snapshot = CreateSnapshot();

        JsonSchemaVocabularyDescriptor descriptor =
            JsonSchemaVocabularyDescriptorBuilder.Build(
                surface,
                JsonWireDeclarationPlan.Create(surface),
                new(
                    new("strict-input"),
                    JsonWireDirection.Deserialize,
                    new JsonSchemaContractRoot.Object(type)),
                snapshot,
                snapshot.Identity);

        string definitionName =
            descriptor.Schema.GetProperty("$ref")
                .GetString()!["#/$defs/".Length..];
        Assert.False(
            descriptor.Schema.GetProperty("$defs")
                .GetProperty(definitionName)
                .GetProperty("additionalProperties")
                .GetBoolean());
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(
                """{"Name":"ok","unexpected":1}""",
                StrictUnmappedInputJsonContext.Default
                    .StrictUnmappedInputFixture));
    }

    static JsonSchemaVocabularyDescriptor Build(
        JsonSchemaContractDeclaration declaration,
        VocabularySnapshot snapshot,
        VocabularySnapshotIdentity expectedIdentity)
    {
        var surface = new JsExportSurface();
        return JsonSchemaVocabularyDescriptorBuilder.Build(
            surface,
            JsonWireDeclarationPlan.Create(surface),
            declaration,
            snapshot,
            expectedIdentity);
    }

    static JsonPositionalRowContract CreateRow() =>
        new(
            [
                new(
                    "package",
                    ApiTypeShape.PrimitiveType(
                        ApiPrimitiveType.String),
                    allowsNull: false),
                new(
                    "downloads",
                    ApiTypeShape.PrimitiveType(
                        ApiPrimitiveType.Int64),
                    allowsNull: false),
            ]);

    static VocabularySnapshot CreateSnapshot(
        string displayLabel = "Package query")
    {
        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity vocabulary = new(
            catalog,
            "package-query");
        return VocabularySnapshot.Create(
            1,
            catalog,
            [
                new(
                    vocabulary,
                    displayLabel,
                    null,
                    maps: null,
                    [
                        new(
                            new(vocabulary, "package"),
                            "Package",
                            null),
                        new(
                            new(vocabulary, "downloads"),
                            "Downloads",
                            null),
                    ]),
            ]);
    }

    static VocabularyTermIdentity Term(
        VocabularySnapshot snapshot,
        string value) =>
        new(snapshot.Vocabularies[0].Identity, value);
}
