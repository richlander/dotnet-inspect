using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.ProductVocabularyTesting;
using DotnetInspect.Cli.Commands;
using DotnetInspector.InspectionContracts;
using DotnetInspector.Networking;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ResourceExplanationCommandTests : IDisposable
{
    public void Dispose()
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(null);
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
    }

    [Fact]
    public async Task Explain_PackageSection_ReportsDeclaredShapeAndCardinality()
    {
        // Package structural sections are explainable with the shape and
        // cardinality their owner declared; the structural identity carries
        // the catalog so a package section never collides with a library one.
        var human = await RunAsync(
            "explain",
            "package/sections/files");

        Assert.Equal(0, human.ExitCode);
        Assert.Empty(human.Error);
        Assert.Contains(
            "Shape: hierarchy | Cardinality: inventory",
            human.Output);

        var json = await RunAsync(
            "explain",
            "package/sections/readme",
            "--json");

        Assert.Equal(0, json.ExitCode);
        Assert.Empty(json.Error);
        using JsonDocument document = JsonDocument.Parse(json.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        Assert.Equal(
            "structural-section",
            ResourceType(root));
        Assert.Equal("text", TextFact(root, "shape"));
        Assert.Equal("scalar", TextFact(root, "cardinality"));

        var catalog = await RunAsync("explain", "package");

        Assert.Equal(0, catalog.ExitCode);
        Assert.Empty(catalog.Error);
        Assert.Contains("Kind: Catalog | Name: Package", catalog.Output);

    }

    [Theory]
    [InlineData("references", "table", "inventory")]
    [InlineData("library-info", "table", "scalar")]
    [InlineData("symbols", "table", "scalar")]
    [InlineData("source-link-availability", "table", "scalar")]
    [InlineData("metadata-image", "table", "scalar")]
    [InlineData("metadata-type-def", "table", "inventory")]
    [InlineData("reference-hierarchy", "hierarchy", "inventory")]
    // Coordinate sections resolve through the Library address route.
    [InlineData("context-member", "table", "inventory")]
    [InlineData("context-exception", "table", "inventory")]
    [InlineData("metadata-heap", "table", "inventory")]
    public async Task Explain_LibrarySection_ReportsDeclaredShapeAndCardinality(
        string section,
        string shape,
        string cardinality)
    {
        // Library adopts Section shapes: every section declares its shape,
        // and field-set records that describe one subject are scalar.
        var result = await RunAsync(
            "explain",
            $"library/sections/{section}",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        Assert.Equal(shape, TextFact(root, "shape"));
        Assert.Equal(cardinality, TextFact(root, "cardinality"));
    }

    [Fact]
    public async Task Explain_LibraryDependencyStructure_IsAGraphWithoutShape()
    {
        // Dependency Structure is a Graph of namespace nodes and edges, not
        // one of the three shapes, so it keeps its diagram formats.
        var result = await RunAsync(
            "explain",
            "library/sections/dependency-structure",
            "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        Assert.Equal(
            "Absent",
            Fact(root, "shape").GetProperty("state").GetString());
        Assert.Equal("inventory", TextFact(root, "cardinality"));
    }

    [Theory]
    [InlineData("package-query", "inspection-document")]
    [InlineData("package-files", "inspection-document")]
    public async Task Explain_CapabilityRootUsesItsOwnerCatalog(
        string path,
        string resourceType)
    {
        var result = await RunAsync(
            "explain",
            path,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            path,
            document.RootElement
                .GetProperty("requested_path")
                .GetString());
        Assert.Equal(
            resourceType,
            ResourceType(
                document.RootElement
                    .GetProperty("resources")[0]));
    }

    [Fact]
    public async Task Explain_TypeAndMemberSections_ReportDeclaredShapeAndCardinality()
    {
        // The type listing and the three member catalogs are explainable under
        // the catalog names the member explanation already publishes, each
        // section with the shape and cardinality its owner declared.
        var classes = await RunAsync("explain", "type/sections/classes");

        Assert.Equal(0, classes.ExitCode);
        Assert.Empty(classes.Error);
        Assert.Contains(
            "Shape: table | Cardinality: inventory",
            classes.Output);

        var typeInfo = await RunAsync(
            "explain",
            "member/sections/type-info",
            "--json");

        Assert.Equal(0, typeInfo.ExitCode);
        Assert.Empty(typeInfo.Error);
        using JsonDocument typeInfoDocument = JsonDocument.Parse(typeInfo.Output);
        JsonElement typeInfoRoot = typeInfoDocument.RootElement.GetProperty("resources")[0];
        Assert.Equal("structural-section", ResourceType(typeInfoRoot));
        Assert.Equal("member/sections/type-info", typeInfoRoot.GetProperty("path").GetString());
        Assert.Equal("table", TextFact(typeInfoRoot, "shape"));
        Assert.Equal("scalar", TextFact(typeInfoRoot, "cardinality"));

        // Source is the Text with a declared Lines inventory, executed by the
        // CLI on Complete execution.
        var source = await RunAsync(
            "explain",
            "member-detail/sections/source",
            "--json");

        Assert.Equal(0, source.ExitCode);
        Assert.Empty(source.Error);
        JsonElement sourceRoot =
            JsonDocument.Parse(source.Output).RootElement.GetProperty("resources")[0];
        Assert.Equal("structural-section", ResourceType(sourceRoot));
        Assert.Equal("member-detail/sections/source", sourceRoot.GetProperty("path").GetString());
        Assert.Equal("text", TextFact(sourceRoot, "shape"));
        Assert.Equal("inventory", TextFact(sourceRoot, "cardinality"));

        // Call Graph is a Graph: no shape, but the tree and Mermaid formats.
        var callGraph = await RunAsync(
            "explain",
            "member-detail/sections/call-graph");

        Assert.Equal(0, callGraph.ExitCode);
        Assert.Empty(callGraph.Error);
        Assert.DoesNotContain("Shape:", callGraph.Output);
        Assert.Contains("Tree, Mermaid", callGraph.Output);

        var overload = await RunAsync(
            "explain",
            "member-overload/sections/methods");

        Assert.Equal(0, overload.ExitCode);
        Assert.Contains(
            "Shape: table | Cardinality: inventory",
            overload.Output);
    }

    [Fact]
    public async Task ExactSection_RendersResourceAndRelatedPaths()
    {
        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarchy");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "# Explain library/sections/reference-hierarchy",
            result.Output);
        Assert.Contains(
            "library/sections/reference-hierarchy/items/column/target",
            result.Output);
        Assert.Contains("Structural section", result.Output);
    }

    [Fact]
    public async Task DefaultHumanExplanation_PreservesTargetlessRelationships()
    {
        var json = await RunAsync(
            "explain",
            "library/categories",
            "--json");
        var human = await RunAsync(
            "explain",
            "library/categories");

        Assert.Equal(0, json.ExitCode);
        Assert.Empty(json.Error);
        Assert.Equal(0, human.ExitCode);
        Assert.Empty(human.Error);
        using JsonDocument document = JsonDocument.Parse(json.Output);
        string[] targetlessRelationships =
        [
            .. document.RootElement
                .GetProperty("relationships")
                .EnumerateArray()
                .Where(static relationship =>
                    relationship.GetProperty("targets")
                        .GetArrayLength() == 0)
                .Select(static relationship =>
                    relationship.GetProperty("relationship")
                        .GetProperty("value")
                        .GetString()!),
        ];

        Assert.NotEmpty(targetlessRelationships);
        Assert.All(
            targetlessRelationships,
            relationship =>
                Assert.Contains(
                    $"({relationship})",
                    human.Output));
        Assert.Contains("| Available | (none) |", human.Output);
        Assert.Contains("| Complete |", human.Output);
    }

    [Fact]
    public async Task DiscoveryPath_CanBePassedUnchangedToExplain()
    {
        var human = await RunAsync(
            "library",
            "-D",
            "@Dependencies");

        Assert.Equal(0, human.ExitCode);
        Assert.Empty(human.Error);
        Assert.Contains("| Name | Kind | Path |", human.Output);
        Assert.Contains(
            "| Reference Hierarchy | section "
            + "| library/sections/reference-hierarchy |",
            human.Output);

        var tree = await RunAsync(
            "library",
            "-D",
            "@Dependencies",
            "--tree");

        Assert.Equal(0, tree.ExitCode);
        Assert.Empty(tree.Error);
        Assert.Contains(
            "Reference Hierarchy "
            + "[library/sections/reference-hierarchy]",
            tree.Output);

        var discovery = await RunAsync(
            "library",
            "-D",
            "@Dependencies",
            "--json");

        Assert.Equal(0, discovery.ExitCode);
        Assert.Empty(discovery.Error);
        using JsonDocument discoveryDocument =
            JsonDocument.Parse(discovery.Output);
        JsonElement row = discoveryDocument.RootElement
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("name").GetString()
                == "Reference Hierarchy");
        string path = row.GetProperty("path").GetString()!;
        Assert.Equal(
            "library/sections/reference-hierarchy",
            path);

        var effective = await RunAsync(
            "library",
            "System.Text.Json",
            "-D",
            "@Dependencies",
            "--json");

        Assert.Equal(0, effective.ExitCode);
        Assert.Empty(effective.Error);
        using JsonDocument effectiveDocument =
            JsonDocument.Parse(effective.Output);
        JsonElement effectiveRow = effectiveDocument.RootElement
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("name").GetString()
                == "Reference Hierarchy");
        Assert.Equal(
            path,
            effectiveRow.GetProperty("path").GetString());

        var projected = await RunAsync(
            "library",
            "-D",
            "@Dependencies",
            "--json",
            "--fields",
            "Name,Path");

        Assert.Equal(0, projected.ExitCode);
        Assert.Empty(projected.Error);
        using JsonDocument projectedDocument =
            JsonDocument.Parse(projected.Output);
        JsonElement projectedRow = projectedDocument.RootElement
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("name").GetString()
                == "Reference Hierarchy");
        Assert.Equal(
            path,
            projectedRow.GetProperty("path").GetString());

        var detailed = await RunAsync(
            "library",
            "-D",
            "Reference Hierarchy",
            "--details",
            "--json");

        Assert.Equal(0, detailed.ExitCode);
        Assert.Empty(detailed.Error);
        using JsonDocument detailedDocument =
            JsonDocument.Parse(detailed.Output);
        Assert.Equal(
            path,
            detailedDocument.RootElement[0]
                .GetProperty("path")
                .GetString());

        var explanation = await RunAsync("explain", path, "--json");

        Assert.Equal(0, explanation.ExitCode);
        Assert.Empty(explanation.Error);
        using JsonDocument explanationDocument =
            JsonDocument.Parse(explanation.Output);
        Assert.Equal(
            path,
            explanationDocument.RootElement
                .GetProperty("requested_path")
                .GetString());

        var pathless = await RunAsync("diff", "-D");

        Assert.Equal(0, pathless.ExitCode);
        Assert.Empty(pathless.Error);
        Assert.Contains("| Name | Kind |", pathless.Output);
        Assert.DoesNotContain("| Name | Kind | Path |", pathless.Output);
    }

    [Fact]
    public async Task PackageQueryFacetPath_CanBePassedUnchangedToExplain()
    {
        var discovery = await RunAsync(
            "package",
            "query",
            "-Q",
            "Packages",
            "--json");

        Assert.Equal(0, discovery.ExitCode);
        Assert.Empty(discovery.Error);
        using JsonDocument discoveryDocument =
            JsonDocument.Parse(discovery.Output);
        JsonElement literal = discoveryDocument.RootElement
            .GetProperty("sections")[0]
            .GetProperty("facets")
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("name").GetString()
                == "library-literal");
        string path =
            literal.GetProperty("resource_path").GetString()!;
        Assert.Equal(
            "package-query/query/facets/library-literal",
            path);

        var explanation = await RunAsync(
            "explain",
            path,
            "--depth",
            "2",
            "--json");

        Assert.Equal(0, explanation.ExitCode);
        Assert.Empty(explanation.Error);
        using JsonDocument explanationDocument =
            JsonDocument.Parse(explanation.Output);
        JsonElement root =
            explanationDocument.RootElement
                .GetProperty("resources")[0];
        Assert.Equal(
            "query-facet",
            ResourceType(root));
        Assert.Equal(
            "library-literal",
            TextFact(root, "key"));
        Assert.Equal(
            ["https://"],
            TextFacts(root, "examples"));
        Assert.Contains(
            explanationDocument.RootElement
                .GetProperty("resources")
                .EnumerateArray(),
            resource =>
                ResourceType(resource) == "consumer-binding");
        Assert.Contains(
            explanationDocument.RootElement
                .GetProperty("relationships")
                .EnumerateArray(),
            relationship =>
                RelationshipKind(relationship) == "exposed-by"
                && TargetPaths(relationship).Contains(
                    "package-query/bindings/cli"));
        Assert.Contains(
            explanationDocument.RootElement
                .GetProperty("relationships")
                .EnumerateArray(),
            relationship =>
                RelationshipKind(relationship) == "required-context"
                && TargetPaths(relationship).Contains(
                    "package-query/query/facets/library-target"));
    }

    [Fact]
    public async Task PackageFiles_ExplainsRouteQuerySpaceAndCliBinding()
    {
        InspectionCapabilityCatalog capabilityCatalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageFileInventoryCapability.ProductModule,
                    PackageFileInventoryCommandCapability.Module,
                ]);
        Assert.Empty(capabilityCatalog.AdoptionGaps);

        var result = await RunAsync(
            "explain",
            "package-files/routes/default",
            "--depth",
            "2",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Contains(
            document.RootElement
                .GetProperty("resources")
                .EnumerateArray(),
            resource =>
                ResourceType(resource) == "inspection-document"
                && resource.GetProperty("path").GetString()
                    == "package-files");
        Assert.Contains(
            document.RootElement
                .GetProperty("resources")
                .EnumerateArray(),
            resource =>
                ResourceType(resource) == "query-space"
                && resource.GetProperty("path").GetString()
                    == "package-files/query");
        Assert.Contains(
            document.RootElement
                .GetProperty("resources")
                .EnumerateArray(),
            resource =>
                ResourceType(resource) == "consumer-binding"
                && resource.GetProperty("path").GetString()
                    == "package-files/bindings/cli");
    }

    [Fact]
    public async Task PackageFiles_SearchesInstalledCapability()
    {
        var result = await RunAsync(
            "explain",
            "package file inventory",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement first =
            document.RootElement.GetProperty("results")[0];
        Assert.Equal(
            "package-files",
            first.GetProperty("resource_path").GetString());
        Assert.Equal(
            PackageFileInventoryCapability.Document.Descriptor.Identity,
            first.GetProperty("resource_identity")
                .GetProperty("identity")
                .GetString());
        Assert.Equal(
            "package <id> -S \"Files\"",
            first.GetProperty("production_bindings")[0]
                .GetProperty("gesture")
                .GetString());
    }

    [Fact]
    public async Task Literal_SearchesCapabilitiesAndReturnsAnExactPath()
    {
        var search = await RunAsync(
            "explain",
            "literal",
            "--json");

        Assert.Equal(0, search.ExitCode);
        Assert.Empty(search.Error);
        using JsonDocument searchDocument =
            JsonDocument.Parse(search.Output);
        JsonElement first =
            searchDocument.RootElement
                .GetProperty("results")[0];
        Assert.Equal(
            "library-literal",
            first.GetProperty("canonical_keys")[0].GetString());
        Assert.Equal(
            "package-query/query/facets/library-literal",
            first.GetProperty("resource_path").GetString());
        Assert.Equal(
            "package query",
            first.GetProperty("production_bindings")[0]
                .GetProperty("gesture")
                .GetString());

        string path = first.GetProperty("resource_path").GetString()!;
        var explanation = await RunAsync(
            "explain",
            path,
            "--json");

        Assert.Equal(0, explanation.ExitCode);
        Assert.Empty(explanation.Error);
        using JsonDocument explanationDocument =
            JsonDocument.Parse(explanation.Output);
        Assert.Equal(
            "library-literal",
            TextFact(
                explanationDocument.RootElement
                    .GetProperty("resources")[0],
                "key"));
    }

    [Fact]
    public async Task MisspelledLiteral_UsesSimilaritySearch()
    {
        var result = await RunAsync(
            "explain",
            "litteral",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement first =
            document.RootElement.GetProperty("results")[0];
        Assert.Equal(
            "package-query/query/facets/library-literal",
            first.GetProperty("resource_path").GetString());
        Assert.Equal(
            0.875,
            first.GetProperty("similarity").GetDouble());
    }

    [Fact]
    public async Task NoncanonicalSlashBearingExampleSearchesCapabilities()
    {
        var result = await RunAsync(
            "explain",
            "https://",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "https://",
            document.RootElement.GetProperty("query").GetString());
        Assert.Equal(
            "package-query/query/facets/library-literal",
            document.RootElement.GetProperty("results")[0]
                .GetProperty("resource_path")
                .GetString());
        Assert.Equal(
            "ExampleValue",
            document.RootElement.GetProperty("results")[0]
                .GetProperty("match_source")
                .GetString());
        Assert.Equal(
            1,
            document.RootElement.GetProperty("match_count").GetInt32());
    }

    [Fact]
    public async Task NoMatchTable_PrintsVisibleStatus()
    {
        var result = await RunAsync(
            "explain",
            "zzzzzzzzzzzzzzzz",
            "--table");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            $"No installed capabilities matched.{Environment.NewLine}",
            result.Output);
    }

    [Fact]
    public async Task RegisteredSingleSegmentPath_RemainsExactExplanation()
    {
        var result = await RunAsync(
            "explain",
            "library",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "library",
            document.RootElement
                .GetProperty("requested_path")
                .GetString());
    }

    [Fact]
    public async Task Vocabularies_ListsEveryShippedProductVocabulary()
    {
        var result = await RunAsync(
            "explain",
            "vocabularies",
            "--depth",
            "1",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement members = document.RootElement
            .GetProperty("relationships")
            .EnumerateArray()
            .Single(relationship =>
                RelationshipKind(relationship) == "collection-vocabulary");
        Assert.Equal(
            [
                "vocabularies/api.accessibility",
                "vocabularies/csharp.style-tiers",
                "vocabularies/csharp.style-choices",
                "vocabularies/csharp.body-kinds",
                "vocabularies/package-query.durable-row",
            ],
            TargetPaths(members));
    }

    [Fact]
    public async Task Vocabulary_ExplainsAcceptedInputsAndPointsToBulkValues()
    {
        var json = await RunAsync(
            "explain",
            "vocabularies/csharp.body-kinds",
            "--json");

        Assert.Equal(0, json.ExitCode);
        Assert.Empty(json.Error);
        using JsonDocument document = JsonDocument.Parse(json.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        Assert.Equal("value-vocabulary", ResourceType(root));
        Assert.Equal("C# Body Kinds", TextFact(root, "name"));
        Assert.Equal(["decompiler.body-kind"], TextFacts(root, "accepted-by"));

        var human = await RunAsync(
            "explain",
            "vocabularies/csharp.body-kinds");

        Assert.Equal(0, human.ExitCode);
        Assert.Empty(human.Error);
        Assert.Contains("Product Vocabulary", human.Output);
        Assert.Contains("decompiler.body-kind", human.Output);
        Assert.Contains(
            "explain vocabularies/csharp.body-kinds --depth 1",
            human.Output);
    }

    [Fact]
    public async Task VocabularyValue_ExplainsMapValuesAndLinksItsTier()
    {
        var bulk = await RunAsync(
            "explain",
            "vocabularies/csharp.style-tiers",
            "--depth",
            "1",
            "--json");

        Assert.Equal(0, bulk.ExitCode);
        Assert.Empty(bulk.Error);
        using (JsonDocument document = JsonDocument.Parse(bulk.Output))
        {
            JsonElement values = document.RootElement
                .GetProperty("relationships")
                .EnumerateArray()
                .Single(relationship =>
                    RelationshipKind(relationship) == "vocabulary-value");
            Assert.All(
                TargetPaths(values),
                static path => Assert.StartsWith(
                    "vocabularies/csharp.style-tiers/values/",
                    path,
                    StringComparison.Ordinal));
            Assert.Equal(
                TargetPaths(values).Length,
                document.RootElement.GetProperty("resources").GetArrayLength()
                    - 1);
        }

        var human = await RunAsync(
            "explain",
            "vocabularies/csharp.style-choices/values/"
                + "var-spelling-style.var-elsewhere");

        Assert.Equal(0, human.ExitCode);
        Assert.Empty(human.Error);
        Assert.Contains("var-spelling-style:var-elsewhere", human.Output);
        Assert.Contains("oracle_endorsed = ", human.Output);
        Assert.Contains("vocabularies/csharp.style-tiers/values/", human.Output);
    }

    [Fact]
    public async Task VocabularyValueListing_ShowsTheExactIdentityQueriesAccept()
    {
        var values = await RunAsync(
            "explain",
            "vocabularies/csharp.body-kinds",
            "--depth",
            "1");

        Assert.Equal(0, values.ExitCode);
        Assert.Empty(values.Error);
        Assert.Contains("| Path | Kind | Name | Identity | Owner |", values.Output);
        Assert.Contains(
            "| vocabularies/csharp.body-kinds/values/breakstatement | "
                + "Vocabulary value | Break | BreakStatement |",
            values.Output);

        var vocabularies = await RunAsync(
            "explain",
            "vocabularies",
            "--depth",
            "1");

        Assert.Equal(0, vocabularies.ExitCode);
        Assert.DoesNotContain("| Identity |", vocabularies.Output);
    }

    [Fact]
    public async Task VocabularyExplanation_MatchesThePinnedCrossHostContent()
    {
        foreach (ExplanationContentPin pin in ProductVocabularyPin.ExplanationContent)
        {
            var result = await RunAsync(
                "explain",
                pin.Path,
                "--depth",
                pin.Depth.ToString(CultureInfo.InvariantCulture),
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            string content = result.Output
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .TrimEnd('\n');
            Assert.Equal(
                pin.Digest,
                "sha256:" + Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(content))));
        }
    }

    [Fact]
    public async Task VocabularyTermMap_LinksToItsTargetVocabulary()
    {
        var result = await RunAsync(
            "explain",
            "vocabularies/csharp.style-choices",
            "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement tier = document.RootElement
            .GetProperty("relationships")
            .EnumerateArray()
            .Single(relationship =>
                RelationshipKind(relationship) == "term-map-target");
        Assert.Equal(["vocabularies/csharp.style-tiers"], TargetPaths(tier));
    }

    [Fact]
    public async Task PackageQueryVocabulary_ExplainsDurableRowFields()
    {
        var result = await RunAsync(
            "explain",
            "vocabularies/package-query.durable-row",
            "--depth",
            "1",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        Assert.Equal("value-vocabulary", ResourceType(root));
        Assert.Equal(
            PackageQueryDurableRowVocabulary.Label,
            TextFact(root, "name"));
        Assert.Equal(
            [PackageQueryDurableRowContract.ContractIdentity],
            TextFacts(root, "accepted-by"));
        Assert.Equal(
            [
                PackageQueryDurableRowContract.PackageId,
                PackageQueryDurableRowContract.Version,
                PackageQueryDurableRowContract.Tier,
                PackageQueryDurableRowContract.Answers,
                PackageQueryDurableRowContract.Evidence,
                PackageQueryDurableRowContract.TotalDownloads,
                PackageQueryDurableRowContract.Verified,
                PackageQueryDurableRowContract.Producer,
                PackageQueryDurableRowContract.Description,
                PackageQueryDurableRowContract.RootRequest,
                PackageQueryDurableRowContract.Owners,
                PackageQueryDurableRowContract.Manifest,
                PackageQueryDurableRowContract.EcosystemAdmission,
            ],
            document.RootElement
                .GetProperty("resources")
                .EnumerateArray()
                .Where(resource => ResourceType(resource) == "vocabulary-value")
                .Select(resource => TextFact(resource, "identity")));
    }

    [Fact]
    public async Task UnknownVocabulary_SuggestsTheCanonicalPath()
    {
        var result = await RunAsync(
            "explain",
            "vocabularies/csharp.body-kind");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("was not found", result.Error);
        Assert.Contains(
            result.Error.Split('\n'),
            static line => line.Trim() == "vocabularies/csharp.body-kinds");
    }

    [Fact]
    public async Task CanonicalUnknownMultiSegmentPath_RemainsExactFailure()
    {
        var result = await RunAsync(
            "explain",
            "package-query/query/facets/not-real");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("was not found", result.Error);
        Assert.DoesNotContain("No installed capabilities", result.Error);
    }

    [Fact]
    public async Task CanonicalUnknownPath_SuggestsAcrossFocusedCatalogs()
    {
        var result = await RunAsync(
            "explain",
            "library/package-query");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("was not found", result.Error);
        Assert.Contains(
            result.Error.Split('\n'),
            static line => line.Trim() == "package-query");
    }

    [Fact]
    public async Task OversizedSearchText_IsRejectedBeforePathSuggestions()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await RunAsync(
            "explain",
            new string('a', 100_000));
        stopwatch.Stop();

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "must not exceed 128 UTF-16 code units",
            result.Error);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Oversized search rejection took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SearchRejectsExactExplanationDepth()
    {
        var result = await RunAsync(
            "explain",
            "literal",
            "--depth",
            "1");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--depth applies only to exact Resource Explanation paths",
            result.Error);
    }

    [Fact]
    public async Task SearchLimitBoundsReturnedResults()
    {
        // "package" is an exact structural catalog path since the Package
        // adoption of Section shapes, so search uses a non-path term.
        var result = await RunAsync(
            "explain",
            "query",
            "-n",
            "1",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.True(
            document.RootElement.GetProperty("match_count").GetInt32()
            > 1);
        Assert.Equal(
            1,
            document.RootElement.GetProperty("returned_count").GetInt32());
        Assert.True(
            document.RootElement.GetProperty("is_truncated").GetBoolean());
        Assert.Equal(
            1,
            document.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Json_UsesTheHostNeutralContentShape()
    {
        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarchy",
            "--depth",
            "1",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "library/sections/reference-hierarchy",
            document.RootElement
                .GetProperty("requested_path")
                .GetString());
        Assert.Equal(
            "structural-section",
            ResourceType(
                document.RootElement
                    .GetProperty("resources")[0]));
        Assert.True(
            document.RootElement
                .GetProperty("resources")
                .GetArrayLength()
            > 1);
    }

    [Fact]
    public async Task UnknownPath_FailsWithBoundedSuggestions()
    {
        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarch");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("was not found", result.Error);
        Assert.Contains(
            "library/sections/reference-hierarchy",
            result.Error);
        Assert.InRange(
            result.Error.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Count(static line => line.StartsWith("  ")),
            1,
            5);
    }

    [Fact]
    public async Task Explain_DoesNotAttemptPackageAcquisition()
    {
        int requests = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RecordingFailureHandler(
                () => requests++));

        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarchy");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task CapabilitySearch_DoesNotAttemptPackageAcquisition()
    {
        int requests = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RecordingFailureHandler(
                () => requests++));

        var result = await RunAsync(
            "explain",
            "literal");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, requests);
    }

    private static string ResourceType(JsonElement resource) =>
        resource.GetProperty("key")
            .GetProperty("resource_type")
            .GetProperty("value")
            .GetString()!;

    private static JsonElement Fact(
        JsonElement resource,
        string identity) =>
        resource.GetProperty("facts")
            .EnumerateArray()
            .Single(fact =>
                fact.GetProperty("fact")
                    .GetProperty("value")
                    .GetString()
                == identity);

    private static string TextFact(
        JsonElement resource,
        string identity) =>
        Fact(resource, identity)
            .GetProperty("values")[0]
            .GetProperty("value")
            .GetProperty("text")
            .GetString()!;

    private static string?[] TextFacts(
        JsonElement resource,
        string identity) =>
        Fact(resource, identity)
            .GetProperty("values")
            .EnumerateArray()
            .Select(value =>
                value.GetProperty("value")
                    .GetProperty("text")
                    .GetString())
            .ToArray();

    private static string RelationshipKind(JsonElement relationship) =>
        relationship.GetProperty("relationship")
            .GetProperty("value")
            .GetString()!;

    private static string?[] TargetPaths(JsonElement relationship) =>
        relationship.GetProperty("targets")
            .EnumerateArray()
            .SelectMany(target =>
                target.GetProperty("addresses").EnumerateArray())
            .Select(address =>
                address.GetProperty("value")
                    .GetProperty("value")
                    .GetProperty("text")
                    .GetString())
            .ToArray();

    private static Task<(
        int ExitCode,
        string Output,
        string Error)> RunAsync(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });

    private sealed class RecordingFailureHandler(Action record) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            record();
            return Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.InternalServerError));
        }
    }
}
