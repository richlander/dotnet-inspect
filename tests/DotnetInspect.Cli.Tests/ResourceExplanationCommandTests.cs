using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotnetInspect.ProductVocabularyTesting;
using DotnetInspect.Cli.Commands;
using DotnetInspector.InspectionContracts;
using DotnetInspector.Networking;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using QuerySpace;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed partial class ResourceExplanationCommandTests : IDisposable
{
    public void Dispose()
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(null);
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
    }

    [Fact]
    public async Task RelationsCategory_RegistersCanonicalNavigationToItsSections()
    {
        var result = await RunAsync("explain", "member/categories/relations", "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        Assert.Equal("@Relations", root.GetProperty("facts").GetProperty("name").GetString());
        JsonElement members = root.GetProperty("relationships").GetProperty("category-member");
        Assert.Equal("Complete", members.GetProperty("completeness").GetString());
        Assert.Equal(
            ["member/sections/derived-types", "member/sections/implementers"],
            members.GetProperty("targets").EnumerateArray()
                .Select(target => target.GetProperty("addresses")[0].GetProperty("value").GetString())
                .ToArray());
    }

    [Fact]
    public async Task SelectedStyleData_ClosesTierReferencesAndUsesCompleteMembershipTables()
    {
        var result = await RunAsync("explain", "vocabularies/csharp.style-choices", ".data", "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement vocabularies = document.RootElement.GetProperty("vocabularies");
        JsonElement choices = vocabularies.GetProperty("csharp.style-choices");
        Assert.Equal(17, choices.GetProperty("values").EnumerateObject().Count());
        Assert.Equal(4, vocabularies.GetProperty("csharp.style-tiers").GetProperty("values").EnumerateObject().Count());
        JsonElement sets = choices.GetProperty("property_sets");
        Assert.Equal(2, sets.GetProperty("byte_divergent").GetArrayLength());
        Assert.Equal(5, sets.GetProperty("oracle_endorsed").GetArrayLength());
        Assert.Single(sets.GetProperty("corpus_endorsed").EnumerateArray());
        Assert.Equal(2, choices.GetProperty("property_groups").GetProperty("conflict_group").EnumerateObject().Count());
        foreach (JsonProperty choice in choices.GetProperty("values").EnumerateObject())
        {
            Assert.False(choice.Value.TryGetProperty("byte_divergent", out _));
            Assert.False(choice.Value.TryGetProperty("conflict_group", out _));
        }
    }

    [Fact]
    public async Task SelectedFacetData_DistinguishesRequiredContextFromExposedTerms()
    {
        var result = await RunAsync("explain", "package-query/query/facets/library-literal", ".data", "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        Assert.Equal(2, root.GetProperty("facets").EnumerateObject().Count());
        Assert.Equal("library-target", root.GetProperty("facets").GetProperty("library-literal")
            .GetProperty("requires")[0].GetString());
        JsonElement exposed = root.GetProperty("bindings").GetProperty("dotnet-inspect.cli/package-query")
            .GetProperty("exposed_facets");
        Assert.Equal(["library-literal"], exposed.EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.DoesNotContain(exposed.EnumerateArray(), value => value.GetString() == "library-target");
    }

    [Fact]
    public async Task SelectedFacetData_PreservesValueMeaningsAndAffordancesWithoutControls()
    {
        var result = await RunAsync(
            "explain",
            "package-query/query/facets/dependency-target",
            ".data",
            "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement facet = document.RootElement
            .GetProperty("facets")
            .GetProperty("dependency-target");

        Assert.False(facet.TryGetProperty("control_kind", out _));
        Assert.False(facet.TryGetProperty("presentation_kind", out _));
        Assert.False(facet.TryGetProperty("options", out _));
        Assert.Equal(
            "nuspec",
            facet.GetProperty("execution_class").GetString());
        Assert.Equal(
            [
                "all",
                "net11.0",
                "net10.0",
                "net9.0",
                "net8.0",
                "net7.0",
                "net6.0",
                "netstandard2.1",
                "netstandard2.0",
                "net48",
                "net472",
            ],
            facet.GetProperty("value_descriptions")
                .EnumerateObject()
                .Select(option => option.Name));
        Assert.Equal(
            ["package.dependencies.inspect"],
            facet.GetProperty("related_operations")
                .EnumerateArray()
                .Select(operation => operation.GetString()));
    }

    [Fact]
    public async Task SelectedFacetData_ReferencesProductEcosystemOptionsByIdentity()
    {
        var result = await RunAsync(
            "explain",
            "package-query/query/facets/depends-ecosystem",
            ".data",
            "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement facet = document.RootElement
            .GetProperty("facets")
            .GetProperty("depends-ecosystem");

        Assert.Equal(
            "product-ecosystems",
            facet.GetProperty("option_source").GetString());
        Assert.False(facet.TryGetProperty("options", out _));
        Assert.False(facet.TryGetProperty("value_descriptions", out _));
    }

    [Fact]
    public async Task SelectedLiteralHal_RegistersOperandAndHostRulesWithoutAcquisition()
    {
        var result = await RunAsync("explain", "package-query/query/facets/library-literal", ".hal", "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        string[] operandRules = root.GetProperty("_embedded").GetProperty("facets")[0].GetProperty("input_rules").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        Assert.Contains(operandRules, rule => rule.Contains("not an allow list", StringComparison.Ordinal));
        Assert.Contains(operandRules, rule => rule.Contains("1..1024 UTF-16", StringComparison.Ordinal));
        Assert.Contains(operandRules, rule => rule.Contains("ordinal substring", StringComparison.Ordinal));
        Assert.Contains(operandRules, rule => rule.Contains("distinct values are incompatible", StringComparison.Ordinal));
        JsonElement binding = root.GetProperty("_embedded").GetProperty("bindings")[0];
        Assert.Contains(binding.GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("--tfm TFM, not --where", StringComparison.Ordinal));
        Assert.Contains(binding.GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("--json", StringComparison.Ordinal));
        Assert.Equal("Complete", root.GetProperty("scope").GetProperty("completeness").GetString());
    }

    [Theory]
    [InlineData(".data", "depends-transitive")]
    [InlineData(".hal", "depends-transitive")]
    [InlineData(".data", "dependency-depth")]
    [InlineData(".hal", "dependency-depth")]
    public async Task SelectedTransitiveFacet_ClosesPrerequisitesAndPreparesAcceptedPlans(
        string selection, string key)
    {
        var result = await RunAsync("explain", "package-query/query/facets/" + key, selection, "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        bool hal = selection == ".hal";
        Dictionary<string, JsonElement> facets = hal
            ? root.GetProperty("_embedded").GetProperty("facets").EnumerateArray()
                .ToDictionary(facet => facet.GetProperty("key").GetString()!)
            : root.GetProperty("facets").EnumerateObject()
                .ToDictionary(facet => facet.Name, facet => facet.Value);
        Assert.Equal(
            key == "depends-transitive"
                ? ["dependency-depth", "dependency-target", "depends-transitive"]
                : new[] { "dependency-depth", "dependency-target" },
            facets.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Complete", root.GetProperty("scope")
            .GetProperty("completeness").GetString());
        JsonElement target = facets["dependency-target"];
        JsonElement depth = facets["dependency-depth"];
        Assert.Equal(["dependency-target"],
            depth.GetProperty("requires").EnumerateArray().Select(value => value.GetString()));
        // The target accepts alternatives; it must not require all four predicates.
        Assert.False(target.TryGetProperty("requires", out _));
        string[] Rules(JsonElement facet) => facet
            .GetProperty("input_rules").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        Assert.Contains(Rules(target), rule => rule.Contains("depends-ecosystem, or dependencies", StringComparison.Ordinal));
        Assert.Contains(Rules(depth), rule => rule.Contains("exact depends (eq) or legacy depends-transitive", StringComparison.Ordinal));
        Assert.Contains(Rules(depth), rule => rule.Contains("at most 5 package candidates", StringComparison.Ordinal));
        JsonElement binding = hal ? root.GetProperty("_embedded").GetProperty("bindings")[0]
            : root.GetProperty("bindings").GetProperty("dotnet-inspect.cli/package-query");
        Assert.Contains(binding.GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("--take N", StringComparison.Ordinal));

        if (key == "depends-transitive")
        {
            JsonElement transitive = facets["depends-transitive"];
            Assert.Equal(["dependency-target", "dependency-depth"],
                transitive.GetProperty("requires").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains(Rules(transitive), rule => rule.Contains("all is not accepted", StringComparison.Ordinal));
            Assert.False(transitive.TryGetProperty("_links", out _));
        }

        JsonElement Facts(JsonElement facet) => facet;
        PortableQueryTerm Term(string termKey, string value) => new(termKey, PortableQueryOperator.Equal, value);
        string targetValue = Facts(target).GetProperty("examples")[0].GetString()!;
        string predicateKey = key == "depends-transitive" ? key : "depends";
        string packageValue = key == "depends-transitive"
            ? Facts(facets[key]).GetProperty("examples")[0].GetString()!
            : "Microsoft.Extensions.Primitives";
        string[] depths = Facts(depth).GetProperty("values").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        Assert.Equal(["2", "3", "4"], depths);
        foreach (string depthValue in depths)
        {
            PackageQueryPlan plan = Assert.IsType<PackageQueryPlanResult.Accepted>(PackageQuery.PlanInput(
                "Microsoft.Extensions.Http", terms:
                [Term(predicateKey, packageValue), Term("dependency-target", targetValue),
                    Term("dependency-depth", depthValue)])).Plan;
            Assert.True(plan.RequiresDependencyTraversal);
            Assert.Equal(int.Parse(depthValue, CultureInfo.InvariantCulture), plan.DependencyDepth);
            Assert.Equal(targetValue, plan.DependencyTarget.RequestedTargetFramework);
        }
        // The Library target gesture cannot satisfy the dependency target prerequisite.
        Assert.Equal(PackageQueryRequestFailureReason.TransitiveDependencyRequiresTarget,
            Assert.IsType<PackageQueryPlanResult.Rejected>(PackageQuery.PlanInput(
                "Microsoft.Extensions.Http", terms:
                [Term(predicateKey, packageValue), Term("dependency-depth", depths[0]),
                    Term("library-literal", "https://")],
                targetFramework: targetValue)).Failure.Reason);
    }

    [Theory]
    [InlineData(".data")]
    [InlineData(".hal")]
    public async Task SelectedDependsFacet_DescribesOptionalTraversalWithoutRequiringIt(string selection)
    {
        var result = await RunAsync("explain", "package-query/query/facets/depends", selection, "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement facet = selection == ".hal" ? document.RootElement.GetProperty("_embedded").GetProperty("facets")[0]
            : document.RootElement.GetProperty("facets").GetProperty("depends");
        Assert.False(facet.TryGetProperty("requires", out _));
        JsonElement facts = facet;
        Assert.Contains(facts.GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("starts-with remains direct", StringComparison.Ordinal));
        string value = facts.GetProperty("examples")[0].GetString()!;
        PackageQueryPlan plan = Assert.IsType<PackageQueryPlanResult.Accepted>(PackageQuery.PlanInput(
            "Microsoft.Extensions.Http", terms: [new("depends", PortableQueryOperator.Equal, value)])).Plan;
        Assert.False(plan.RequiresDependencyTraversal);
    }

    [Theory]
    [InlineData("vocabularies/csharp.style-choices")]
    [InlineData("package-query/query")]
    public async Task SelectedHal_LeadsWithDataAndNavigationAndFollowsInHalMode(string path)
    {
        var direct = await RunAsync("explain", path, ".data", "--json");
        var hal = await RunAsync("explain", path, ".hal", "--json");
        Assert.Equal(0, direct.ExitCode);
        Assert.Equal(0, hal.ExitCode);
        using JsonDocument document = JsonDocument.Parse(hal.Output);
        JsonElement root = document.RootElement;
        Assert.False(root.TryGetProperty("facts", out _));
        Assert.False(root.TryGetProperty("identity", out _));
        Assert.False(root.TryGetProperty("selection", out _));
        Assert.False(root.TryGetProperty("name", out _));
        JsonElement links = root.GetProperty("_links");
        Assert.False(links.TryGetProperty("curies", out _));
        Assert.False(links.TryGetProperty("describedby", out _));
        Assert.Equal("application/hal+json", links.GetProperty("self").GetProperty("type").GetString());
        string href = links.GetProperty("self").GetProperty("href").GetString()!;
        var followed = await RunAsync("explain", href, "--json");
        Assert.Equal(0, followed.ExitCode);
        using JsonDocument followDocument = JsonDocument.Parse(followed.Output);
        Assert.Equal(root.GetRawText(), followDocument.RootElement.GetRawText());
        if (links.TryGetProperty("describedby", out JsonElement describedby))
        {
            var contractResult = await RunAsync("explain", describedby.GetProperty("href").GetString()!, "--json");
            Assert.Equal(0, contractResult.ExitCode);
            using JsonDocument contractDocument = JsonDocument.Parse(contractResult.Output);
            Assert.True(contractDocument.RootElement.TryGetProperty("schemas", out _));
        }
        JsonElement embedded = root.GetProperty("_embedded");
        Assert.All(embedded.EnumerateObject(), relation => Assert.Equal(JsonValueKind.Array, relation.Value.ValueKind));
        if (path.StartsWith("vocabularies/", StringComparison.Ordinal))
            Assert.Equal(17, embedded.GetProperty("vocabularies").EnumerateArray()
                .Single(vocabulary => vocabulary.GetProperty("id").GetString() == "csharp.style-choices")
                .GetProperty("_embedded").GetProperty("values").GetArrayLength());
        else
            Assert.Equal(19, embedded.GetProperty("facets").GetArrayLength());
    }

    [Theory]
    [InlineData(".data", "--json")]
    [InlineData(".contract", "--json")]
    [InlineData("--depth", "1", "--json")]
    [InlineData()]
    public async Task ProjectedHalAddress_RejectsConflictingSelectionOrDepth(params string[] options)
    {
        var result = await RunAsync(["explain", "inspect-resource:/package-query/query?projection=hal", .. options]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("projected resource address", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("explain", "vocabularies/csharp.style-choices", ".data", "--depth", "1", "--json")]
    [InlineData("explain", "library", ".data", "--json")]
    [InlineData("explain", "literal", ".hal", "--json")]
    [InlineData("explain", "package-query/query", ".data")]
    public async Task SelectedData_RejectsUnsupportedSelections(params string[] arguments)
    {
        var result = await RunAsync(arguments);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
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
            ".contract", "--json");

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
    // Coordinate sections, which only the Library address view selects.
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
            ".contract", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        Assert.Equal(shape, TextFact(root, "shape"));
        Assert.Equal(cardinality, TextFact(root, "cardinality"));
    }

    [Theory]
    [InlineData("library-metrics")]
    [InlineData("dependency-structure")]
    [InlineData("member-metrics")]
    [InlineData("top-leverage")]
    [InlineData("clone-candidates")]
    [InlineData("array-pool-escapes")]
    [InlineData("performance-boxing")]
    public async Task Explain_LibrarySection_ReportsOwnerDescription(
        string section)
    {
        // Adopted Library sections explain what they report: Summary, Basis,
        // and Empty are owner-issued text, never the section name.
        var result = await RunAsync(
            "explain",
            $"library/sections/{section}",
            ".contract", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        string name = TextFact(root, "name");
        foreach (string part in new[] { "summary", "basis", "empty" })
        {
            string text = TextFact(root, part);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.NotEqual(name, text);
        }
    }

    [Fact]
    public async Task Explain_LibrarySectionDescription_RendersAndUndeclaredIsAbsent()
    {
        var described = await RunAsync(
            "explain",
            "library/sections/top-leverage");

        Assert.Equal(0, described.ExitCode);
        Assert.Empty(described.Error);
        Assert.Contains("Summary: ", described.Output);
        Assert.Contains("Basis: ", described.Output);
        Assert.Contains("Empty: ", described.Output);

        // A section that has not declared a description reports every part
        // absent rather than substituting its name or an empty string.
        // Library has completed adoption, so the undeclared case is a
        // section of a command that has not.
        var undeclared = await RunAsync(
            "explain",
            "member/sections/derived-types",
            ".contract", "--json");

        Assert.Equal(0, undeclared.ExitCode);
        using JsonDocument document = JsonDocument.Parse(undeclared.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        foreach (string part in new[] { "summary", "basis", "empty", "boundary" })
        {
            Assert.Equal(
                "Absent",
                Fact(root, part).GetProperty("state").GetString());
        }
    }

    [Fact]
    public async Task Explain_LibraryDependencyStructure_IsANamespaceEdgeTable()
    {
        // Dependency Structure offers a Table of namespace edges and a
        // Document; its Mermaid diagram stays an explicit lowering.
        var result = await RunAsync(
            "explain",
            "library/sections/dependency-structure",
            ".contract", "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement.GetProperty("resources")[0];
        Assert.Equal("table", TextFact(root, "shape"));
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
            ".contract", "--json");

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
            ".contract", "--json");

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
            ".contract", "--json");

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
            ".contract", "--json");
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

        var explanation = await RunAsync("explain", path, ".contract", "--json");

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
            ".contract", "--json");

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
            ".contract", "--json");

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

    [Theory]
    [InlineData("context member", "library/sections/context-member")]
    [InlineData("metadata heap", "library/sections/metadata-heap")]
    public async Task CoordinateSection_SearchesTheLibraryCatalogThroughTheAddressBinding(
        string text,
        string path)
    {
        // The Library address view's coordinate sections join the one
        // Library catalog, and search binds them only to the view that can
        // select them.
        var search = await RunAsync("explain", text, "--json");

        Assert.Equal(0, search.ExitCode);
        Assert.Empty(search.Error);
        using JsonDocument document = JsonDocument.Parse(search.Output);
        JsonElement result = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .First();
        Assert.Equal(path, result.GetProperty("resource_path").GetString());
        Assert.Equal(
            "library",
            result.GetProperty("structural_identity")
                .GetProperty("catalog")
                .GetString());
        JsonElement binding =
            Assert.Single(
                result.GetProperty("production_bindings")
                    .EnumerateArray());
        Assert.Equal(
            "library address -S",
            binding.GetProperty("gesture").GetString());
        Assert.Equal(
            "dotnet-inspect.cli/library-address-sections",
            binding.GetProperty("identity").GetString());
    }

    [Fact]
    public async Task LibraryCatalog_ReachesTheCoordinateSections()
    {
        var catalog = await RunAsync("explain", "library");

        Assert.Equal(0, catalog.ExitCode);
        Assert.Contains("library/categories/context", catalog.Output);

        var context = await RunAsync("explain", "library/categories/context");

        Assert.Equal(0, context.ExitCode);
        Assert.Contains("library/sections/context-member", context.Output);

        var metadata = await RunAsync(
            "explain",
            "library/categories/metadata");

        Assert.Equal(0, metadata.ExitCode);
        Assert.Contains("library/sections/metadata-heap", metadata.Output);
    }

    [Theory]
    [InlineData("unsafe", "library/sections/unsafe-members")]
    [InlineData("metrics", "library/sections/library-metrics")]
    [InlineData("leverage", "library/sections/top-leverage")]
    [InlineData("families", "library/sections/architectural-families")]
    public async Task TaskTerm_SearchesTheLibraryStructuralCatalog(
        string text,
        string path)
    {
        var search = await RunAsync("explain", text, "--json");

        Assert.Equal(0, search.ExitCode);
        Assert.Empty(search.Error);
        using JsonDocument document = JsonDocument.Parse(search.Output);
        JsonElement result = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .Single(candidate =>
                candidate.GetProperty("resource_path").GetString() == path);
        Assert.Equal(1.0, result.GetProperty("similarity").GetDouble());
        Assert.Equal(
            "Section",
            result.GetProperty("resource_kind").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            result.GetProperty("resource_identity").ValueKind);
        Assert.Equal(
            "library",
            result.GetProperty("structural_identity")
                .GetProperty("catalog")
                .GetString());
        JsonElement binding =
            Assert.Single(
                result.GetProperty("production_bindings")
                    .EnumerateArray());
        Assert.Equal("library -S", binding.GetProperty("gesture").GetString());
        Assert.Equal("library", binding.GetProperty("resource_path").GetString());

        var explanation = await RunAsync("explain", path, "--json");
        Assert.Equal(0, explanation.ExitCode);
        Assert.Empty(explanation.Error);
        using JsonDocument exact = JsonDocument.Parse(explanation.Output);
        Assert.Equal(path, exact.RootElement.GetProperty("path").GetString());
    }

    [Theory]
    [InlineData(
        "Does this library have namespace dependency cycles?",
        "library/sections/dependency-structure")]
    [InlineData(
        "namespace dependency cycles",
        "library/sections/dependency-structure")]
    [InlineData("array pool escapes", "library/sections/array-pool-escapes")]
    public async Task Phrase_RanksTheOwningSectionFirst(
        string text,
        string path)
    {
        var search = await RunAsync("explain", text, "--json");

        Assert.Equal(0, search.ExitCode);
        Assert.Empty(search.Error);
        using JsonDocument document = JsonDocument.Parse(search.Output);
        Assert.Equal(
            0.25,
            document.RootElement
                .GetProperty("similarity_threshold")
                .GetDouble());
        Assert.Equal(
            path,
            document.RootElement
                .GetProperty("results")[0]
                .GetProperty("resource_path")
                .GetString());
    }

    [Theory]
    [InlineData("files", "package/sections/files", "package -S")]
    [InlineData("delegates", "type/sections/delegates", "type -S")]
    [InlineData("unsafe", "member/sections/unsafe-members", "member -S")]
    [InlineData(
        "unsafe",
        "member-overload/sections/unsafe-operations",
        "member -S")]
    [InlineData(
        "unsafe",
        "member-detail/sections/unsafe-operations",
        "member -S")]
    public async Task Search_AdmitsEveryExplainedStructuralCatalog(
        string text,
        string path,
        string gesture)
    {
        var search = await RunAsync("explain", text, "-n", "100", "--json");

        Assert.Equal(0, search.ExitCode);
        Assert.Empty(search.Error);
        using JsonDocument document = JsonDocument.Parse(search.Output);
        JsonElement result = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .Single(candidate =>
                candidate.GetProperty("resource_path").GetString() == path);
        Assert.Equal(
            path[..path.IndexOf('/')],
            result.GetProperty("structural_identity")
                .GetProperty("catalog")
                .GetString());
        Assert.Equal(
            gesture,
            result.GetProperty("production_bindings")[0]
                .GetProperty("gesture")
                .GetString());
    }

    [Fact]
    public async Task DiffAnalysisRoute_ExplainsAsAnInspectionRouteBoundToDiff()
    {
        Assert.Contains(
            DiffAnalysisCommandCapability.CompareBinding,
            DiffAnalysisCommandCapability.Catalog.Bindings);
        Assert.Same(
            DiffAnalysisCatalog.Route,
            DiffAnalysisCommandCapability.CompareBinding.Route);

        var route = await RunAsync(
            "explain",
            "diff-analysis/routes/compare",
            "--json");

        Assert.Equal(0, route.ExitCode);
        Assert.Empty(route.Error);
        Assert.Contains("\"execution\":\"inspection\"", route.Output);
        Assert.Contains("diff-analysis/bindings/cli", route.Output);

        var document = await RunAsync("explain", "diff-analysis", "--json");

        Assert.Equal(0, document.ExitCode);
        using JsonDocument exact = JsonDocument.Parse(document.Output);
        Assert.Equal(
            "diff-analysis",
            exact.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Search_AdmitsEveryAnalysisDiffBinds()
    {
        var search = await RunAsync(
            "explain",
            "diff analyses",
            "-n",
            "100",
            "--json");

        Assert.Equal(0, search.ExitCode);
        Assert.Empty(search.Error);
        using JsonDocument document = JsonDocument.Parse(search.Output);
        JsonElement[] analyses =
        [
            .. document.RootElement
                .GetProperty("results")
                .EnumerateArray()
                .Where(static result =>
                    result.GetProperty("resource_kind").GetString()
                        == "Analysis"),
        ];
        Assert.Equal(
            DiffAnalysisCommandCapability.Identities
                .Select(static identity => "analyses/" + identity)
                .Order(StringComparer.Ordinal),
            analyses
                .Select(static result =>
                    result.GetProperty("resource_path").GetString()!)
                .Order(StringComparer.Ordinal));
        foreach (JsonElement analysis in analyses)
        {
            Assert.Equal(
                "diff --analysis",
                Assert.Single(
                        analysis.GetProperty("production_bindings")
                            .EnumerateArray())
                    .GetProperty("gesture")
                    .GetString());
            Assert.Equal(
                "diff-analysis/routes/compare",
                Assert.Single(
                        analysis.GetProperty("owning_routes")
                            .EnumerateArray())
                    .GetProperty("resource_path")
                    .GetString());
            var exact = await RunAsync(
                "explain",
                analysis.GetProperty("resource_path").GetString()!,
                "--json");
            Assert.Equal(0, exact.ExitCode);
        }
    }

    [Theory]
    [InlineData("string literal diff", "analyses/string-literals")]
    [InlineData("allocation changes", "analyses/allocation")]
    public async Task Phrase_RanksTheAnalysisFirst(string text, string path)
    {
        var search = await RunAsync("explain", text, "--json");

        Assert.Equal(0, search.ExitCode);
        using JsonDocument document = JsonDocument.Parse(search.Output);
        Assert.Equal(
            path,
            document.RootElement
                .GetProperty("results")[0]
                .GetProperty("resource_path")
                .GetString());
    }

    [Fact]
    public async Task BareExplain_DefaultsToTheRoutingTableAsTsv()
    {
        // Explain is primarily for agents, so its starting point is the
        // routing table alone: one TSV row per entry.
        var result = await RunAsync("explain");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        string[] lines = result.Output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n')
            .Split('\n');
        Assert.Equal(
            "role\tpath\tname\tpurpose\taccepts\tdestinations\tquestions",
            lines[0]);
        Assert.Equal(10, lines.Length);
        Assert.Contains(
            lines,
            static line => line.StartsWith(
                "operation\toperations/diff-analysis\tDiff analyses\t",
                StringComparison.Ordinal)
                && line.Contains(
                    "\tlibrary, type, member\tdiff-analysis, analyses\t",
                    StringComparison.Ordinal));
        Assert.DoesNotContain("# Explain", result.Output);
    }

    [Theory]
    [InlineData("operations", "--jsonl", 3)]
    [InlineData("subject-kinds/library", "--tsv", 2)]
    public async Task RootPathTableSelection_ListsTheEntriesThePathReaches(
        string path,
        string format,
        int lines)
    {
        var result = await RunAsync("explain", path, format);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            lines,
            result.Output.TrimEnd('\n').Split('\n').Length);
    }

    [Theory]
    [InlineData("--depth", "0")]
    [InlineData("--envelope")]
    public async Task BareExplainTable_RejectsDocumentOptions(
        params string[] arguments)
    {
        var result = await RunAsync(["explain", .. arguments]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("do not apply", result.Error);
    }

    [Fact]
    public async Task BareExplainMarkdown_ReadsTheRootAsItsRoutingTable()
    {
        var result = await RunAsync("explain", "--markdown");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("# Explain root", result.Output);
        Assert.Contains("Kind: Explanation root", result.Output);
        Assert.Contains("## Routing", result.Output);
        Assert.Contains(
            "| subject-kind | subject-kinds/library | Library |",
            result.Output);
        Assert.Contains(
            "| library, type |",
            result.Output);
        Assert.Contains(
            "| operation | operations/diff-analysis | Diff analyses |",
            result.Output);
        Assert.Contains(
            "| library, type, member | diff-analysis, analyses |",
            result.Output);
        Assert.Contains(
            "| reference | references/vocabularies | Value vocabularies |",
            result.Output);
        Assert.DoesNotContain("## Expanded Resources", result.Output);
        Assert.DoesNotContain("## Related Resources", result.Output);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("--depth", "0", "--markdown")]
    public async Task RootBelowItsEntries_ListsItsRoutingCollections(
        params string[] arguments)
    {
        // Below the depth that reaches routing entries, the root keeps its
        // collection relationships as the way onward.
        var result = await RunAsync(["explain", .. arguments]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("## Routing", result.Output);
        Assert.Contains("## Related Resources", result.Output);
        foreach (string collection in new[]
                 {
                     "subject-kinds",
                     "operations",
                     "references",
                 })
        {
            Assert.Contains(
                $"| Routing collection (root-collection) | Available | "
                    + $"{collection} |",
                result.Output);
        }
    }

    [Fact]
    public async Task BareExplainJson_EmbedsEveryRoutingEntry()
    {
        var result = await RunAsync("explain", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "root",
            document.RootElement.GetProperty("path").GetString());
        Assert.Equal(
            9,
            Regex.Matches(
                    result.Output,
                    "\"path\":\"((subject-kinds|operations|references)/[a-z-]+)\"")
                .Select(static match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task ExplainHelp_SucceedsWithoutAnOperand()
    {
        var result = await RunAsync("explain", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("[<resource-or-search>]", result.Output);
        Assert.Contains("Omit it to explain the root", result.Output);
    }

    [Fact]
    public async Task RoutingEntry_ExplainsItsHintsAndDestinations()
    {
        var result = await RunAsync("explain", "operations/package-query");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Kind: Routing entry", result.Output);
        Assert.Contains("Role: operation", result.Output);
        Assert.Contains(
            "Issuer: Inspection Capability Composition",
            result.Output);
        Assert.Contains("Purpose: Finds the packages", result.Output);
        Assert.Contains(
            "Questions: Which packages depend on a given package?",
            result.Output);
        Assert.Contains(
            "| Routing destination (routing-destination-document) | "
                + "Available | package-query |",
            result.Output);
    }

    [Fact]
    public async Task BareExplain_ProjectsALoneContractToken()
    {
        var result = await RunAsync("explain", ".contract", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "root",
            document.RootElement.GetProperty("requested_path").GetString());
    }

    [Theory]
    [InlineData("-n", "3")]
    [InlineData("-Q", "x")]
    public async Task BareExplain_RejectsSearchAndValueOptions(
        string option,
        string value)
    {
        var result = await RunAsync("explain", option, value);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    // The CLI's root gate: the declarations of every owner the CLI admits
    // reach every resource of its complete catalog, so the reachability
    // invariant holds and the CLI may publish its root.
    [Fact]
    public void ExplanationRoot_ReachesEveryResourceOfTheCompleteCatalog()
    {
        ResourceExplanationRootComposition composition =
            ResourceExplanationCommand.CreateRootComposition()!;

        Assert.Empty(composition.Unreachable);
        Assert.True(composition.IsPublishable);
        Assert.Equal(
            ["vocabularies"],
            Targets(
                composition.Catalog,
                "references/vocabularies",
                "routing-destination-collection"));
        Assert.Equal(
            ["library", "member", "package", "type"],
            Targets(composition.Catalog, "subject-kinds", "collection-routing-entry")
                .Select(static path => path.Split('/')[^1])
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["library", "type"],
            Targets(
                composition.Catalog,
                "subject-kinds/library",
                "routing-destination-catalog"));
        Assert.Equal(
            ["member-overload", "member-detail"],
            Targets(
                composition.Catalog,
                "subject-kinds/member",
                "routing-destination-catalog"));
        Assert.Equal(
            ["package-query", "package-files", "diff-analysis"],
            Targets(composition.Catalog, "operations", "collection-routing-entry")
                .Select(static path => path.Split('/')[^1]));
        Assert.Equal(
            ["package-query"],
            Targets(
                composition.Catalog,
                "operations/package-query",
                "routing-destination-document"));
        Assert.Equal(
            ["package-files"],
            Targets(
                composition.Catalog,
                "operations/package-files",
                "routing-destination-document"));
        Assert.Equal(
            ["analyses"],
            Targets(
                composition.Catalog,
                "operations/diff-analysis",
                "routing-destination-collection"));
        Assert.Equal(
            ["subject-kinds/library", "subject-kinds/type", "subject-kinds/member"],
            Targets(
                composition.Catalog,
                "operations/diff-analysis",
                "accepts-subject-kind"));
        Assert.Equal(
            ["operations/package-query", "operations/package-files"],
            Targets(
                composition.Catalog,
                "subject-kinds/package",
                "accepted-by-operation"));

        static IEnumerable<string> Targets(
            ResourceExplanationCatalog catalog,
            string path,
            string relationship)
        {
            var source = Assert.IsType<ResourcePathResolution.Resolved>(
                catalog.Resolve(path));
            var paths = catalog.Resources.ToDictionary(
                static resource => resource.Key,
                static resource => resource.Path!.Value);
            return catalog.Relationships
                .Where(candidate =>
                    candidate.Source == source.Key
                    && candidate.Relationship.Value == relationship)
                .SelectMany(static candidate => candidate.Targets)
                .Select(target => paths[target.Resource])
                .ToArray();
        }
    }

    // Search reads the structural projections without building their
    // explanation graphs, so this witnesses the wiring: every admitted path
    // resolves in the exact catalog to its section or category, and every
    // explained section or category is admitted.
    [Fact]
    public void StructuralSearchCatalogs_MatchTheExactExplanationCatalog()
    {
        ResourceExplanationCatalog complete =
            ResourceExplanationCommand.CreateCompleteCatalog()!;
        var admitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (InspectionStructuralCatalogRegistration catalog
                 in InspectionCapabilityCatalog.Create(ResourceExplanationCommand
                     .CreateStructuralCapabilityModules()).StructuralCatalogs)
        {
            foreach (StructuralResourcePathRegistration registration
                     in catalog.Projection.ResourcePaths.Where(static path =>
                         path.Identity.Kind
                             is DiscoveryResourceKind.Section
                             or DiscoveryResourceKind.Category))
            {
                var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
                    complete.Resolve(registration.Path.Value));
                Assert.Equal(
                    StructuralType(registration.Identity.Kind),
                    resolved.Key.ResourceType.Value);
                Assert.True(admitted.Add(registration.Path.Value));
            }
        }

        string[] explained =
        [
            .. complete.Resources
                .Where(static resource =>
                    resource.Key.ResourceType.Value
                        is "structural-section" or "structural-category")
                .Select(static resource => resource.Path!.Value),
        ];
        Assert.NotEmpty(explained);
        Assert.Equal(
            explained.Order(StringComparer.Ordinal),
            admitted.Order(StringComparer.Ordinal));

        static string StructuralType(DiscoveryResourceKind kind) =>
            kind == DiscoveryResourceKind.Section
                ? "structural-section"
                : "structural-category";
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
            ".contract", "--json");

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
            ".contract", "--json");

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
            ".contract", "--json");

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
            ".contract", "--json");

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
            ".contract", "--json");

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
                ".contract", "--json");

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
    public async Task VocabularyExplanation_CompactAndSelectedMatchesThePinnedCrossHostContent()
    {
        foreach (ExplanationContentPin pin in ProductVocabularyPin.CompactContent)
        {
            string[] args = pin.Selection is null
                ? ["explain", pin.Path, "--depth", pin.Depth.ToString(CultureInfo.InvariantCulture), "--json"]
                : ["explain", pin.Path, "." + pin.Selection, "--json"];
            var result = await RunAsync(args);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            string content = result.Output.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
            Assert.Equal(pin.Digest, "sha256:" + Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(content))));
        }
    }

    [Fact]
    public async Task VocabularyTermMap_LinksToItsTargetVocabulary()
    {
        var result = await RunAsync(
            "explain",
            "vocabularies/csharp.style-choices",
            ".contract", "--json");

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
            ".contract",
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
            ".contract", "--json");

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

    [Theory]
    [InlineData("literal")]
    [InlineData("unsafe")]
    public async Task CapabilitySearch_DoesNotAttemptPackageAcquisition(
        string text)
    {
        int requests = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RecordingFailureHandler(
                () => requests++));

        var result = await RunAsync(
            "explain",
            text);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("vocabularies/csharp.body-kinds", 0)]
    [InlineData("vocabularies/csharp.style-choices", 1)]
    [InlineData("package-query/query/facets/library-literal", 0)]
    public async Task CompactData_ExposesFactsAndUsableLinksWithoutDeclarations(
        string path, int depth)
    {
        var data = await RunAsync("explain", path, "--depth", depth.ToString(
            CultureInfo.InvariantCulture), "--json");
        var contract = await RunAsync("explain", path, ".contract", "--depth",
            depth.ToString(CultureInfo.InvariantCulture), "--json");
        Assert.Equal(0, data.ExitCode);
        Assert.Equal(0, contract.ExitCode);
        using JsonDocument compact = JsonDocument.Parse(data.Output);
        using JsonDocument complete = JsonDocument.Parse(contract.Output);
        Assert.False(compact.RootElement.TryGetProperty("schemas", out _));
        Assert.True(complete.RootElement.TryGetProperty("schemas", out _));
        Assert.True(data.Output.Length < contract.Output.Length / 2);
        string href = compact.RootElement.GetProperty("_links").GetProperty("self")
            .GetProperty("href").GetString()!;
        var followed = await RunAsync("explain", href, "--json");
        Assert.Equal(0, followed.ExitCode);
        using JsonDocument same = JsonDocument.Parse(followed.Output);
        Assert.Equal(path, same.RootElement.GetProperty("path").GetString());
        JsonElement facts = compact.RootElement.GetProperty("facts");
        Assert.Equal(TextFact(complete.RootElement.GetProperty("resources")[0], "name"),
            facts.GetProperty("name").GetString());
        if (path.Contains("library-literal", StringComparison.Ordinal))
        {
            Assert.Equal("library-literal", facts.GetProperty("key").GetString());
            Assert.NotEmpty(facts.GetProperty("operators").EnumerateArray());
        }
    }

    [Fact]
    public async Task ContractProjection_RejectsSearchAndUnknownProjection()
    {
        Assert.NotEqual(0, (await RunAsync("explain", "literal", ".contract", "--json")).ExitCode);
        Assert.NotEqual(0, (await RunAsync("explain", "library", ".missing", "--json")).ExitCode);
    }

    [Fact]
    public async Task CompactData_DistinguishesAbsentFactFromAvailableEmptyValues()
    {
        var result = await RunAsync("explain", "member-detail/sections/call-graph", "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument data = JsonDocument.Parse(result.Output);
        Assert.False(data.RootElement.GetProperty("facts").TryGetProperty("shape", out _));
        Assert.Equal("Absent", data.RootElement.GetProperty("fact_states")
            .GetProperty("shape").GetProperty("state").GetString());
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
