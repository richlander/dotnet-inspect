using System.Text;
using System.Text.Json;

using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Direct;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class LibrarySymbolDocumentOperationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    private static readonly LibrarySymbolDocumentSource s_source =
        new(
            "System.Text.Json.dll",
            "nuget",
            "System.Text.Json",
            "10.0.14",
            "lib/net10.0/System.Text.Json.dll",
            "net10.0");

    [Fact]
    public async Task WritesTheDesignedFactsForSystemTextJson()
    {
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) =
            await ProduceAsync(MetadataLibrarySymbolAdmission.PublicApi);
        var written = Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome);
        Assert.True(written.IsComplete);
        DumpForInspection(bytes, "public_api");

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Assert.Equal(
            [
                "schema_version", "generator", "library", "request", "types",
                "type_collisions", "members", "parameter_type_members",
                "return_type_members", "produced_type_members",
                "attribute_type_members", "suffix_family_types",
                "name_word_types", "base_type_derived_types",
                "interface_implementing_types", "section_outcomes", "counts",
                "outcome",
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(written.Types, root.GetProperty("types").EnumerateObject().Count()
            + root.GetProperty("type_collisions").GetArrayLength());
        Assert.Equal(written.Members, root.GetProperty("members").GetArrayLength());
        Assert.Equal(
            "2ddc75da-7a95-4826-8729-839cc65f9eea",
            root.GetProperty("library").GetProperty("module_version_id").GetString());

        // JsonConverter is public, abstract, and unsealed, yet only this
        // Library can derive from it (#inheritance).
        JsonElement converter = Type(root, "T:System.Text.Json.Serialization.JsonConverter");
        Assert.Equal("internal", converter.GetProperty("constructor_accessibility").GetString());
        Assert.True(converter.GetProperty("is_abstract").GetBoolean());
        Assert.True(converter.GetProperty("visible_outside_library").GetBoolean());
        Assert.True(converter.GetProperty("required_overrides_complete").GetBoolean());
        Assert.True(converter.GetProperty("inaccessible_required_overrides").GetInt32() > 0);

        JsonElement generic = Type(root, "T:System.Text.Json.Serialization.JsonConverter`1");
        Assert.Equal("JsonConverter<T>", generic.GetProperty("name").GetString());
        Assert.Equal("T:System.Text.Json.Serialization.JsonConverter", generic.GetProperty("base_type").GetString());
        Assert.Equal(
            [
                "M:System.Text.Json.Serialization.JsonConverter`1.Read(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)",
                "M:System.Text.Json.Serialization.JsonConverter`1.Write(System.Text.Json.Utf8JsonWriter,`0,System.Text.Json.JsonSerializerOptions)",
            ],
            Strings(generic.GetProperty("required_overrides")));
        Assert.Equal(0, generic.GetProperty("inaccessible_required_overrides").GetInt32());

        // JsonAttribute's base is System.Attribute, outside this Library.
        Assert.False(
            Type(root, "T:System.Text.Json.Serialization.JsonAttribute")
                .GetProperty("required_overrides_complete").GetBoolean());

        // The forwarder keeps its own record with explicit empty facts.
        JsonElement forwarder = Type(root, "T:System.Runtime.CompilerServices.IsExternalInit");
        Assert.Equal("forwarder", forwarder.GetProperty("declaration_kind").GetString());
        Assert.Equal("System.Runtime", forwarder.GetProperty("forwarded_to_assembly").GetString());

        // Members that take a Stream at any depth; what produces a JsonDocument.
        string[] streamMembers = Strings(root.GetProperty("parameter_type_members").GetProperty("T:System.IO.Stream"));
        Assert.Contains(
            "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
            streamMembers);
        string[] producers = Strings(root.GetProperty("produced_type_members").GetProperty("T:System.Text.Json.JsonDocument"));
        Assert.Contains(
            "M:System.Text.Json.JsonDocument.TryParseValue(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonDocument@)",
            producers);
        Assert.Contains(producers, id => id.StartsWith("M:System.Text.Json.JsonSerializer.SerializeToDocument", StringComparison.Ordinal));

        JsonElement parseAsync = Member(
            root,
            "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)");
        Assert.Equal(
            ["T:System.Text.Json.JsonDocument", "T:System.Threading.Tasks.Task`1"],
            Strings(parseAsync.GetProperty("return_type_mentions")));
        Assert.Equal(["T:System.Text.Json.JsonDocument"], Strings(parseAsync.GetProperty("produced_types")));
        Assert.Equal("method", parseAsync.GetProperty("kind").GetString());
        Assert.True(parseAsync.GetProperty("is_static").GetBoolean());

        JsonElement tryParse = Member(
            root,
            "M:System.Text.Json.JsonDocument.TryParseValue(System.Text.Json.Utf8JsonReader@,System.Text.Json.JsonDocument@)");
        Assert.Equal(["T:System.Boolean"], Strings(tryParse.GetProperty("return_type_mentions")));
        Assert.Equal(
            ["T:System.Boolean", "T:System.Text.Json.JsonDocument"],
            Strings(tryParse.GetProperty("produced_types")));
        Assert.Equal(
            ["ref", "out"],
            tryParse.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("modifier").GetString()!));

        // JsonObject's explicit IDictionary Keys produces its collection
        // under public_api, as its getter is callable through the interface.
        Assert.Contains(
            root.GetProperty("members").EnumerateArray(),
            member => member.GetProperty("declaring_type").GetString() == "T:System.Text.Json.Nodes.JsonObject"
                && member.GetProperty("documentation_id").GetString()!.Contains("Keys", StringComparison.Ordinal)
                && Strings(member.GetProperty("produced_types")).Contains("T:System.Collections.Generic.ICollection`1"));

        // A non-generic Task produces nothing.
        Assert.All(
            root.GetProperty("members").EnumerateArray()
                .Where(member => member.GetProperty("return_type").GetString() == "System.Threading.Tasks.Task"),
            member => Assert.Empty(member.GetProperty("produced_types").EnumerateArray()));

        // The design's measured facts for this Library (#real-asset): 85
        // Types including one forwarder, 30 Members taking exactly a Stream,
        // 13 producing a JsonDocument, and 54 marked RequiresUnreferencedCode.
        Assert.Equal(85, written.Types);
        Assert.Equal(
            30,
            root.GetProperty("members").EnumerateArray().Count(member =>
                member.GetProperty("parameters").EnumerateArray().Any(parameter =>
                    parameter.GetProperty("type").GetString() == "System.IO.Stream")));
        Assert.Equal(13, producers.Length);
        Assert.Equal(
            54,
            root.GetProperty("attribute_type_members")
                .GetProperty("T:System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute")
                .GetArrayLength());

        // Name families (#name-families): JsonConverterFactory and
        // JsonConverterAttribute share the word Converter but not the
        // Converter suffix family; the forwarder has words and no families.
        Assert.Equal(
            ["declarations", "name_families"],
            Strings(root.GetProperty("request").GetProperty("sections")));
        Assert.Contains(
            root.GetProperty("section_outcomes").EnumerateArray(),
            section => section.GetProperty("name").GetString() == "name_families"
                && section.GetProperty("status").GetString() == "complete");
        AssertFamilies(Type(root, "T:System.Text.Json.Serialization.JsonConverterFactory"), "Factory", "ConverterFactory", ["Json", "Converter", "Factory"]);
        AssertFamilies(Type(root, "T:System.Text.Json.Serialization.JsonConverterAttribute"), "Attribute", "ConverterAttribute", ["Json", "Converter", "Attribute"]);
        AssertFamilies(converter, "Converter", "JsonConverter", ["Json", "Converter"]);
        AssertFamilies(generic, "Converter", "JsonConverter", ["Json", "Converter"]);
        AssertFamilies(Type(root, "T:System.Text.Json.JsonDocument"), "Document", "JsonDocument", ["Json", "Document"]);
        AssertFamilies(forwarder, "", "", ["Is", "External", "Init"]);
        string[] converterFamily = Strings(root.GetProperty("suffix_family_types").GetProperty("Converter"));
        Assert.Contains("T:System.Text.Json.Serialization.JsonConverter", converterFamily);
        Assert.Contains("T:System.Text.Json.Serialization.JsonConverter`1", converterFamily);
        Assert.DoesNotContain("T:System.Text.Json.Serialization.JsonConverterFactory", converterFamily);
        Assert.Contains(
            "T:System.Text.Json.Serialization.JsonConverterFactory",
            Strings(root.GetProperty("name_word_types").GetProperty("Converter")));
        Assert.Contains(
            "T:System.Runtime.CompilerServices.IsExternalInit",
            Strings(root.GetProperty("name_word_types").GetProperty("External")));
        Assert.Equal("Document", parseAsync.GetProperty("declaring_type_suffix_family").GetString());
        Assert.Equal("JsonDocument", parseAsync.GetProperty("declaring_type_two_word_suffix_family").GetString());
        Assert.Equal(["Json", "Document"], Strings(parseAsync.GetProperty("declaring_type_name_words")));

        // Every record is explicit: no null anywhere.
        AssertNoNulls(root);
    }

    [Fact]
    public async Task DocumentationEqualsTheDesignVectorsAndSettlesEveryRecord()
    {
        byte[] xml = await RealXmlAsync();
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) = await ProduceAsync(
            MetadataLibrarySymbolAdmission.All,
            compiledXml: xml);
        Assert.True(Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome).IsComplete);
        DumpForInspection(bytes, "all.documented");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Assert.Equal(
            ["declarations", "name_families", "documentation"],
            Strings(root.GetProperty("request").GetProperty("sections")));
        JsonElement section = Assert.Single(
            root.GetProperty("section_outcomes").EnumerateArray(),
            section => section.GetProperty("name").GetString() == "documentation");
        Assert.Equal("complete", section.GetProperty("status").GetString());

        // Every Type and Member in the design's excerpt carries the excerpt's
        // documentation object.
        using JsonDocument vector = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                RealAsset("system-text-json.symbols.json"),
                TestContext.Current.CancellationToken));
        foreach (JsonProperty type in vector.RootElement.GetProperty("types").EnumerateObject())
        {
            Assert.Equal(
                Documentation(type.Value),
                Documentation(Type(root, type.Name)));
        }
        foreach (JsonElement member in vector.RootElement.GetProperty("members").EnumerateArray())
        {
            Assert.Equal(
                Documentation(member),
                Documentation(Member(root, member.GetProperty("documentation_id").GetString()!)));
        }

        // Every record settles: complete with a summary, no_documentation
        // without one, or forwarder.
        IEnumerable<JsonElement> records =
        [
            .. root.GetProperty("types").EnumerateObject().Select(type => type.Value),
            .. root.GetProperty("members").EnumerateArray(),
        ];
        Assert.All(records, record =>
        {
            (string status, string summary) = Documentation(record);
            Assert.Contains(status, (string[])["complete", "no_documentation", "forwarder"]);
            Assert.Equal(status == "complete", summary.Length > 0);
        });
        // all admits private Members, which reference XML does not document.
        Assert.Contains(records, record => Documentation(record).Status == "no_documentation");

        // The interactive per-subject settlement reports the same text.
        Assert.Equal(
            "Gets the root element of this JSON document.",
            XmlDocumentationReader.ReadMember(
                new MemoryStream(xml),
                new XmlDocMemberIdentity("P:System.Text.Json.JsonDocument.RootElement"))!.Summary);
        AssertNoNulls(root);
    }

    [Fact]
    public async Task DocumentationSettlesWithoutACompanionAndFailsVisiblyOnABadOne()
    {
        // A Library with no compiled XML: every record has no documentation.
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) = await ProduceAsync(
            MetadataLibrarySymbolAdmission.PublicApi,
            documentationWithoutCompanion: true);
        Assert.True(Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome).IsComplete);
        using (JsonDocument document = JsonDocument.Parse(bytes))
        {
            JsonElement root = document.RootElement;
            Assert.Equal(
                ("no_documentation", ""),
                Documentation(Type(root, "T:System.Text.Json.JsonDocument")));
            Assert.Equal(
                ("forwarder", ""),
                Documentation(Type(root, "T:System.Runtime.CompilerServices.IsExternalInit")));
        }

        // A malformed companion: the section fails, records say failed, and
        // the whole document is still written as partial.
        (outcome, bytes) = await ProduceAsync(
            MetadataLibrarySymbolAdmission.PublicApi,
            compiledXml: Encoding.UTF8.GetBytes("<doc><members><member name=\"T:X\"><summary>open"));
        var written = Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome);
        Assert.False(written.IsComplete);
        Assert.Equal(
            LibrarySymbolSectionStatus.Failed,
            Assert.Single(written.Sections, section => section.Name == "documentation").Status);
        using (JsonDocument document = JsonDocument.Parse(bytes))
        {
            Assert.Equal(
                ("failed", ""),
                Documentation(Type(document.RootElement, "T:System.Text.Json.JsonDocument")));
            Assert.Equal("partial", document.RootElement.GetProperty("outcome").GetString());
        }

        // An over-budget companion aborts the write.
        (outcome, _) = await ProduceAsync(
            MetadataLibrarySymbolAdmission.PublicApi,
            compiledXml: await RealXmlAsync(),
            maximumXmlBytes: 1024);
        Assert.Equal(
            LibrarySymbolDocumentAbort.DocumentationBudget,
            Assert.IsType<LibrarySymbolDocumentOutcome.Aborted>(outcome).Reason);

        // The documentation section needs the host's evidence.
        Assert.Throws<ArgumentException>(() => new LibrarySymbolDocumentRequest(
            MetadataLibrarySymbolAdmission.PublicApi,
            new LibrarySymbolDocumentGenerator("dotnet-inspect", "test"),
            s_source,
            s_bounds,
            [LibrarySymbolDocumentSection.Documentation]));
    }

    private static (string Status, string Summary) Documentation(JsonElement record)
    {
        JsonElement documentation = record.GetProperty("documentation");
        return (
            documentation.GetProperty("status").GetString()!,
            documentation.GetProperty("summary").GetString()!);
    }

    [Fact]
    public async Task NameFamiliesCanBeLeftOut()
    {
        (LibrarySymbolDocumentOutcome outcome, byte[] bytes) = await ProduceAsync(
            MetadataLibrarySymbolAdmission.PublicApi,
            [LibrarySymbolDocumentSection.Declarations]);
        Assert.True(Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome).IsComplete);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Assert.Equal(["declarations"], Strings(root.GetProperty("request").GetProperty("sections")));
        Assert.Contains(
            root.GetProperty("section_outcomes").EnumerateArray(),
            section => section.GetProperty("name").GetString() == "name_families"
                && section.GetProperty("status").GetString() == "not_requested");
        Assert.All(
            root.GetProperty("types").EnumerateObject(),
            type =>
            {
                Assert.Equal("", type.Value.GetProperty("suffix_family").GetString());
                Assert.Empty(type.Value.GetProperty("name_words").EnumerateArray());
            });
        Assert.Empty(root.GetProperty("suffix_family_types").EnumerateObject());
        Assert.Empty(root.GetProperty("name_word_types").EnumerateObject());
    }

    private static void AssertFamilies(
        JsonElement type,
        string suffixFamily,
        string twoWordSuffixFamily,
        string[] nameWords)
    {
        Assert.Equal(suffixFamily, type.GetProperty("suffix_family").GetString());
        Assert.Equal(twoWordSuffixFamily, type.GetProperty("two_word_suffix_family").GetString());
        Assert.Equal(nameWords, Strings(type.GetProperty("name_words")));
    }

    [Fact]
    public async Task RecordsEqualTheTypeDocument()
    {
        (_, byte[] bytes) = await ProduceAsync(MetadataLibrarySymbolAdmission.All);
        DumpForInspection(bytes, "all");
        using JsonDocument symbols = JsonDocument.Parse(bytes);

        byte[] content = await RealAssetAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        foreach ((string ns, string name) in new[]
        {
            ("System.Text.Json", "JsonSerializer"),
            ("System.Text.Json", "JsonDocument"),
            ("System.Text.Json.Serialization", "JsonConverter`1"),
            ("System.Text.Json", "Utf8JsonWriter"),
        })
        {
            var document = Assert.IsType<TypeDocumentInspectionOutcome.Available>(
                TypeDocumentInspectionOperation.Execute(
                    new(
                        library.Reference,
                        new(
                            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                                MetadataTypeDefinitionName.Create(ns, [name])).Name,
                            s_bounds,
                            accessibility: TypeMemberGroupAccessibilityFilter.All,
                            includeHidden: true)),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken).Content).Document;
            string declaringType = $"T:{ns}.{name}";
            var records = symbols.RootElement.GetProperty("members").EnumerateArray()
                .Where(member => member.GetProperty("declaring_type").GetString() == declaringType)
                .Select(member => (
                    member.GetProperty("documentation_id").GetString(),
                    member.GetProperty("canonical_signature").GetString(),
                    member.GetProperty("display_signature").GetString(),
                    member.GetProperty("metadata_token").GetInt32()))
                .ToArray();
            Assert.Equal(
                document.Members.Select(member => (
                    (string?)member.Subject.DocumentationId.ToString(),
                    (string?)member.CanonicalSignature.ToString(),
                    (string?)member.DisplaySignature.ToString(),
                    member.Subject.MetadataToken)),
                records);
        }
        await library.RetireAsync();
    }

    [Theory]
    [InlineData(10, 100_000, LibrarySymbolDocumentAbort.TypeBudget)]
    [InlineData(5_000, 5, LibrarySymbolDocumentAbort.MemberBudget)]
    public async Task BudgetExhaustionAbortsTheWrite(
        int maxTypes,
        int maxMembers,
        LibrarySymbolDocumentAbort expected)
    {
        byte[] content = await RealAssetAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        using var destination = new MemoryStream();
        LibrarySymbolDocumentOutcome outcome = LibrarySymbolDocumentOperation.Execute(
            new LibrarySymbolDocumentRequest(
                MetadataLibrarySymbolAdmission.PublicApi,
                new LibrarySymbolDocumentGenerator("dotnet-inspect", "test"),
                s_source,
                new ApiSurfaceExtractionBounds(
                    maxTypes,
                    maxMembers,
                    maxInspectionFailures: 1_000,
                    maxTypeForwarders: 10_000,
                    maxMetadataRows: 1_000_000,
                    maxRetainedTextCharacters: 20_000_000)),
            library.IssueOperation(),
            destination,
            TestContext.Current.CancellationToken);
        await library.RetireAsync();

        // The bytes already written are not a document; the host discards
        // them and leaves any earlier document unchanged.
        Assert.Equal(
            expected,
            Assert.IsType<LibrarySymbolDocumentOutcome.Aborted>(outcome).Reason);
    }

    [Fact]
    public async Task SameLibraryAndRequestWriteIdenticalBytes()
    {
        (_, byte[] first) = await ProduceAsync(MetadataLibrarySymbolAdmission.PublicApi);
        (_, byte[] second) = await ProduceAsync(MetadataLibrarySymbolAdmission.PublicApi);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task EveryRecordAndLookupEntryIsOneLine()
    {
        (_, byte[] bytes) = await ProduceAsync(MetadataLibrarySymbolAdmission.All);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        string[] lines = Encoding.UTF8.GetString(bytes).Split('\n');

        int records = 0;
        int pairs = 0;
        foreach (string line in lines)
        {
            string trimmed = line.EndsWith(',') ? line[..^1] : line;
            if (trimmed.StartsWith("{\"documentation_id\"", StringComparison.Ordinal))
            {
                using JsonDocument record = JsonDocument.Parse(trimmed);
                records++;
                continue;
            }
            if (trimmed.StartsWith('"') && !trimmed.EndsWith('{') && !trimmed.EndsWith('['))
            {
                using JsonDocument pair = JsonDocument.Parse("{" + trimmed + "}");
                pairs++;
            }
        }

        string[] lookups =
        [
            "parameter_type_members", "return_type_members", "produced_type_members",
            "attribute_type_members", "suffix_family_types", "name_word_types",
            "base_type_derived_types", "interface_implementing_types",
        ];
        Assert.Equal(
            root.GetProperty("members").GetArrayLength() + root.GetProperty("type_collisions").GetArrayLength(),
            records);
        const int headerAndTrailerPairs = 7;
        Assert.Equal(
            root.GetProperty("types").EnumerateObject().Count()
                + lookups.Sum(name => root.GetProperty(name).EnumerateObject().Count())
                + headerAndTrailerPairs,
            pairs);
    }

    [Fact]
    public async Task LookupsDeriveFromTheRecords()
    {
        (_, byte[] bytes) = await ProduceAsync(MetadataLibrarySymbolAdmission.PublicApi);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        JsonElement[] members = [.. root.GetProperty("members").EnumerateArray()];
        JsonElement[] types =
        [
            .. root.GetProperty("types").EnumerateObject().Select(type => type.Value),
            .. root.GetProperty("type_collisions").EnumerateArray().Select(collision => collision.GetProperty("record")),
        ];
        string[] typeIds =
        [
            .. root.GetProperty("types").EnumerateObject().Select(type => type.Name),
            .. root.GetProperty("type_collisions").EnumerateArray().Select(collision => collision.GetProperty("documentation_id").GetString()!),
        ];

        AssertLookup(root, "parameter_type_members", members.SelectMany(member =>
            member.GetProperty("parameters").EnumerateArray()
                .Where(parameter => parameter.GetProperty("modifier").GetString() != "out")
                .SelectMany(parameter => Strings(parameter.GetProperty("type_mentions")))
                .Select(key => (key, Id(member)))));
        AssertLookup(root, "return_type_members", members.SelectMany(member =>
            Strings(member.GetProperty("return_type_mentions")).Select(key => (key, Id(member)))));
        AssertLookup(root, "produced_type_members", members.SelectMany(member =>
            Strings(member.GetProperty("produced_types")).Select(key => (key, Id(member)))));
        AssertLookup(root, "attribute_type_members", members.SelectMany(member =>
            Strings(member.GetProperty("attribute_types")).Select(key => (key, Id(member)))));
        AssertLookup(root, "suffix_family_types", types.Zip(typeIds)
            .Where(pair => pair.First.GetProperty("suffix_family").GetString() != "")
            .Select(pair => (pair.First.GetProperty("suffix_family").GetString()!, pair.Second)));
        AssertLookup(root, "name_word_types", types.Zip(typeIds)
            .SelectMany(pair => Strings(pair.First.GetProperty("name_words")).Select(key => (key, pair.Second))));
        Assert.NotEmpty(root.GetProperty("name_word_types").EnumerateObject());
        AssertLookup(root, "base_type_derived_types", types.Zip(typeIds)
            .Where(pair => pair.First.GetProperty("base_type").GetString() != "")
            .Select(pair => (pair.First.GetProperty("base_type").GetString()!, pair.Second)));
        AssertLookup(root, "interface_implementing_types", types.Zip(typeIds)
            .SelectMany(pair => Strings(pair.First.GetProperty("interfaces")).Select(key => (key, pair.Second))));

        static string Id(JsonElement member) => member.GetProperty("documentation_id").GetString()!;
    }

    private static void AssertLookup(
        JsonElement root,
        string name,
        IEnumerable<(string Key, string Value)> expected)
    {
        var derived = expected
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (
                group.Key,
                string.Join("|", group.Select(pair => pair.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))));
        var written = root.GetProperty(name).EnumerateObject()
            .Select(entry => (entry.Name, string.Join("|", Strings(entry.Value))));
        Assert.Equal(derived, written);
    }

    private static async Task<(LibrarySymbolDocumentOutcome Outcome, byte[] Bytes)> ProduceAsync(
        MetadataLibrarySymbolAdmission admission,
        LibrarySymbolDocumentSection[]? sections = null,
        byte[]? compiledXml = null,
        bool documentationWithoutCompanion = false,
        int maximumXmlBytes = 16 * 1024 * 1024)
    {
        byte[] content = await RealAssetAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content),
                implementation: content,
                compiledXml: compiledXml);
        LibrarySymbolDocumentDocumentation? documentation =
            compiledXml is null && !documentationWithoutCompanion
                ? null
                : new(
                    DirectLibraryDocumentationHouseAdapter
                        .CreateLibraryCompiledXmlContributions(library.Reference),
                    new DocumentationHouseLimits(
                        8,
                        maximumXmlBytes,
                        XmlDocumentationReadLimits.Default),
                    TimeSpan.FromMinutes(1));
        using var destination = new MemoryStream();
        LibrarySymbolDocumentOutcome outcome = LibrarySymbolDocumentOperation.Execute(
            new LibrarySymbolDocumentRequest(
                admission,
                new LibrarySymbolDocumentGenerator("dotnet-inspect", "test"),
                s_source,
                s_bounds,
                sections,
                documentation),
            library.IssueOperation(),
            destination,
            TestContext.Current.CancellationToken);
        await library.RetireAsync();
        return (outcome, destination.ToArray());
    }

    /// <summary>
    /// System.Text.Json 10.0.14 from nuget.org, the design's real asset.
    /// </summary>
    private static Task<byte[]> RealXmlAsync() =>
        File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);

    private static string RealAsset(string fileName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "LibrarySymbolDocument",
            fileName);

    private static Task<byte[]> RealAssetAsync() =>
        File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "LibrarySymbolDocument",
                "System.Text.Json.dll"),
            TestContext.Current.CancellationToken);

    private static void DumpForInspection(byte[] bytes, string admission)
    {
        if (Environment.GetEnvironmentVariable("SYMBOL_DOCUMENT_DUMP") is { Length: > 0 } directory)
            File.WriteAllBytes(Path.Combine(directory, $"system-text-json.{admission}.symbols.json"), bytes);
    }

    private static JsonElement Type(JsonElement root, string id) =>
        root.GetProperty("types").GetProperty(id);

    private static JsonElement Member(JsonElement root, string id) =>
        Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            member => member.GetProperty("documentation_id").GetString() == id);

    private static string[] Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(value => value.GetString()!)];

    private static void AssertNoNulls(JsonElement element)
    {
        Assert.NotEqual(JsonValueKind.Null, element.ValueKind);
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
                AssertNoNulls(property.Value);
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                AssertNoNulls(item);
        }
    }
}
