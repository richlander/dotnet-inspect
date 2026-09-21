using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class QueryDiscoveryTests
{
    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
        => ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task BareQuery_ListsOnlyImplementedQuerySections(string command)
    {
        var result = await Run(command, "-Q", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement sections = json.RootElement.GetProperty("sections");
        Assert.Contains(sections.EnumerateArray(), section =>
            section.GetProperty("section").GetString() == "Body Shapes");
        Assert.DoesNotContain(sections.EnumerateArray(), section =>
            section.GetProperty("section").GetString() == "Top Leverage");
        foreach (JsonElement section in sections.EnumerateArray())
        {
            Assert.Equal("Query: " + section.GetProperty("section").GetString(),
                section.GetProperty("query_section").GetString());
            Assert.False(section.TryGetProperty("facets", out _));
            Assert.True(section.GetProperty("facet_count").GetInt32() > 0);
        }
    }

    [Theory]
    [InlineData("library", "Performance: Boxing")]
    [InlineData("type", "Performance Triage")]
    [InlineData("member", "Performance Triage")]
    public async Task NamedQuery_DescribesBindingsWithoutAcquiringTarget(string command, string section)
    {
        var result = await Run(command, "--package", "/missing/query-discovery.nupkg",
            "-Q", section, "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement described = Assert.Single(json.RootElement.GetProperty("sections").EnumerateArray());
        JsonElement rootReach = Assert.Single(described.GetProperty("facets").EnumerateArray(),
            facet => facet.GetProperty("name").GetString() == "RootReach");
        Assert.Equal(["--where", "--order-by", "--top"],
            rootReach.GetProperty("operators").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["=", "!=", ">=", "<="],
            rootReach.GetProperty("comparisons").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("integer", rootReach.GetProperty("value_kind").GetString());
    }

    [Theory]
    [InlineData("Consumer Use Sites")]
    [InlineData("Provider API Types")]
    [InlineData("Direct Use Clusters")]
    [InlineData("Call Sites")]
    [InlineData("Public Root Paths")]
    public async Task GraphLibrariesQuery_ExposesInheritedClusterWithoutAcquiringPair(
        string sectionName)
    {
        var result = await Run(
            "graph",
            "libraries",
            "-Q",
            sectionName,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement section = Assert.Single(
            json.RootElement.GetProperty("sections").EnumerateArray());
        Assert.Equal(
            sectionName,
            section.GetProperty("section").GetString());
        JsonElement cluster = Assert.Single(
            section.GetProperty("facets").EnumerateArray());
        Assert.Equal(
            "Cluster",
            cluster.GetProperty("name").GetString());
        Assert.Equal(
            ["--where"],
            cluster.GetProperty("operators")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["="],
            cluster.GetProperty("comparisons")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            "--where \"Cluster=3\"",
            cluster.GetProperty("example").GetString());

        var companion = await Run(
            "graph",
            "libraries",
            "-S",
            $"Query: {sectionName}",
            "--json");
        Assert.Equal(0, companion.ExitCode);
        Assert.Empty(companion.Error);
        Assert.Equal(result.Output, companion.Output);
    }

    [Fact]
    public async Task DependsQuery_ProjectsRegisteredTypeCapabilitiesWithoutAcquisition()
    {
        var result = await Run(
            "depends",
            "Missing.Type",
            "--library",
            "/missing/query-discovery.dll",
            "-Q",
            "Dependency Graph",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement section = Assert.Single(
            json.RootElement.GetProperty("sections").EnumerateArray());
        JsonElement[] facets =
            [.. section.GetProperty("facets").EnumerateArray()];
        Assert.Equal(
            ["Source", "Target", "Kind", "Traversal", "Depth"],
            facets.Select(facet =>
                facet.GetProperty("name").GetString()));
        Assert.Equal(
            ["--where", "--order-by", "--top"],
            facets[0].GetProperty("operators")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            Enum.GetNames<TypeDependencyRelationshipKind>(),
            facets[2].GetProperty("values")
                .EnumerateArray()
                .Select(value => value.GetString()));
    }

    [Fact]
    public async Task DependsQuery_NonJsonDiscoveryRendersRegisteredKindValues()
    {
        var result = await Run(
            "depends",
            "-Q",
            "Dependency Graph",
            "--table");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        foreach (string kind in Enum.GetNames<TypeDependencyRelationshipKind>())
            Assert.Contains(kind, result.Output);
        Assert.DoesNotContain("C# Body Kinds", result.Output);
    }

    [Theory]
    [InlineData(
        FindQueryRouteKind.TypeResults,
        "Results")]
    [InlineData(
        FindQueryRouteKind.MemberResults,
        "Members")]
    public async Task FindQuery_PreservesFacetFreeRouteDiscovery(
        FindQueryRouteKind kind,
        string sectionName)
    {
        Assert.Equal(
            sectionName,
            FindQueryOptions.Section(kind));

        var result = await Run(
            "find",
            "-Q",
            sectionName,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement section = Assert.Single(
            json.RootElement.GetProperty("sections").EnumerateArray());
        Assert.Equal(
            sectionName,
            section.GetProperty("section").GetString());
        Assert.Equal(0, section.GetProperty("facet_count").GetInt32());
        Assert.Empty(
            section.GetProperty("facets").EnumerateArray());
    }

    [Fact]
    public async Task LibraryQuery_ProjectsRegisteredReferenceCapabilityWithoutAcquisition()
    {
        var result = await Run(
            "library",
            "query",
            "-Q",
            LibraryQuerySections.LibrariesName,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement section = Assert.Single(
            json.RootElement.GetProperty("sections").EnumerateArray());
        Assert.Equal(
            LibraryQuerySections.LibrariesName,
            section.GetProperty("section").GetString());
        JsonElement reference = Assert.Single(
            section.GetProperty("facets").EnumerateArray());
        Assert.Equal(
            LibraryQuery.ReferencesTermKey,
            reference.GetProperty("name").GetString());
        Assert.Equal(
            ["--where"],
            reference.GetProperty("operators")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["="],
            reference.GetProperty("comparisons")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            "metadata",
            reference.GetProperty("execution_class").GetString());
    }

    [Fact]
    public async Task BodyShapeQuery_NonJsonDiscoveryReferencesKindVocabulary()
    {
        var result = await Run(
            "library",
            "-Q",
            SectionNames.BodyShapes,
            "--table");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("C# Body Kinds", result.Output);
        Assert.Contains(
            "vocabulary -S \"C# Body Kinds\"",
            result.Output);
    }

    [Fact]
    public async Task DependencyHierarchy_InheritsDepthAcrossCommandAndPackageSection()
    {
        var depends = await Run(
            "depends",
            "-Q",
            "Dependency Hierarchy",
            "--json");
        var package = await Run(
            "package",
            "-Q",
            "Dependency Hierarchy",
            "--json");

        Assert.Equal(0, depends.ExitCode);
        Assert.Empty(depends.Error);
        Assert.Equal(0, package.ExitCode);
        Assert.Empty(package.Error);
        using var dependsJson = JsonDocument.Parse(depends.Output);
        using var packageJson = JsonDocument.Parse(package.Output);
        JsonElement dependsFacets =
            dependsJson.RootElement.GetProperty("sections")[0]
                .GetProperty("facets");
        JsonElement packageFacets =
            packageJson.RootElement.GetProperty("sections")[0]
                .GetProperty("facets");
        Assert.True(JsonElement.DeepEquals(
            dependsFacets,
            packageFacets));
        JsonElement depth = Assert.Single(
            dependsFacets.EnumerateArray());
        Assert.Equal(
            "Depth",
            depth.GetProperty("name").GetString());
        Assert.Equal(
            ["--depth"],
            depth.GetProperty("operators")
                .EnumerateArray()
                .Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("library", true)]
    [InlineData("type", false)]
    [InlineData("member", false)]
    public async Task BodyShapes_ExposesExactKindsAndOnlySupportedComposition(string command, bool composed)
    {
        var result = await Run(command, "-Q", "Body Shapes", "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement facets = json.RootElement.GetProperty("sections")[0].GetProperty("facets");
        JsonElement kind = facets[0];
        Assert.Equal("Kind", kind.GetProperty("name").GetString());
        Assert.Equal(["="], kind.GetProperty("comparisons").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(BodyKindQueryOptions.QueryKey.Values,
            kind.GetProperty("values").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(composed, facets.GetArrayLength() > 1);
        Assert.All(facets.EnumerateArray(), facet =>
            Assert.Equal(["--where"],
                facet.GetProperty("operators").EnumerateArray().Select(value => value.GetString())));
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task CloneCandidates_ExposesBreadthAndDiscovery(string command)
    {
        var result = await Run(command, "-Q", SectionNames.CloneCandidates, "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);

        using var json = JsonDocument.Parse(result.Output);
        JsonElement section = Assert.Single(
            json.RootElement.GetProperty("sections").EnumerateArray());
        Assert.Equal(
            "Query: " + SectionNames.CloneCandidates,
            section.GetProperty("query_section").GetString());
        JsonElement[] facets =
            [.. section.GetProperty("facets").EnumerateArray()];
        Assert.Equal(["Breadth", "Discovery"],
            facets.Select(facet => facet.GetProperty("name").GetString()));
        Assert.Equal(
            ["Self", "SelfAndRegisteredEcosystems", "Everything"],
            facets[0].GetProperty("values").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["SimilarNames", "All"],
            facets[1].GetProperty("values").EnumerateArray()
                .Select(value => value.GetString()));

        var companion = await Run(
            command,
            "-S",
            "Query: " + SectionNames.CloneCandidates,
            "--json");
        Assert.Equal(0, companion.ExitCode);
        Assert.Equal(result.Output, companion.Output);
    }

    [Theory]
    [InlineData("@Performance")]
    [InlineData("Performance Triage")]
    [InlineData("Performance:*")]
    public async Task QuerySelectors_ReuseCategoriesAliasesAndGlobs(string selector)
    {
        var result = await Run("library", "-Q", selector, "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains(json.RootElement.GetProperty("sections").EnumerateArray(),
            section => section.GetProperty("section").GetString() == SectionNames.PerformanceBoxing);
    }

    [Theory]
    [InlineData("-S")]
    [InlineData("-D")]
    [InlineData("--effective")]
    [InlineData("--schema")]
    [InlineData("--tree")]
    [InlineData("--print")]
    [InlineData("--bare")]
    public async Task QueryRejectsConflictingModes(string flag)
    {
        var result = await Run("library", "/missing/target.dll", "-Q", "Body Shapes", flag);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(flag, result.Error);
        Assert.DoesNotContain("Unhandled", result.Error);
    }

    [Theory]
    [InlineData("-D")]
    [InlineData("-S")]
    public async Task QueryRejectsDataDiscoveryModes(string mode)
    {
        var result = await Run(
            "package",
            "query",
            "-Q",
            "Packages",
            mode,
            "Packages");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"-Q cannot be combined with {mode}",
            result.Error);
    }

    [Theory]
    [InlineData(
        "--rows",
        "bad",
        "--rows requires N..M, N.., or ..M")]
    [InlineData(
        "--take",
        "bad",
        "--take requires a positive whole number.")]
    public async Task QueryConflictFollowsSharedSelectionFailure(
        string option,
        string value,
        string expected)
    {
        var result = await Run(
            "package",
            "query",
            option,
            value,
            "-Q",
            "Packages",
            "-D",
            "Packages");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(expected, result.Error);
        Assert.DoesNotContain(
            "-Q cannot be combined",
            result.Error);
    }

    [Theory]
    [InlineData("--where", "Kind=ObjectCreationExpression")]
    [InlineData("--order-by", "RootReach desc")]
    [InlineData("--top", "10")]
    public async Task QueryDoesNotSilentlyApplyOrDiscardExecutionOperators(string option, string value)
    {
        var result = await Run("library", "-Q", "Body Shapes", option, value);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("does not execute", result.Error);
    }

    [Theory]
    [InlineData("-Q", "--where", "Kind=ObjectCreationExpression")]
    [InlineData("-Q", "--order-by", "RootReach desc")]
    [InlineData("-Q", "--top", "10")]
    [InlineData("-Q", "--unknown-query-option", "value")]
    [InlineData("-S", "--where", "Kind=ObjectCreationExpression")]
    [InlineData("-D", "--where", "Kind=ObjectCreationExpression")]
    public async Task PackageQueryDiscovery_PreservesUnrecognizedOptionDiagnostics(
        string mode, string option, string value)
    {
        string section = mode == "-Q" ? "Package Info" : "Query: Package Info";
        var result = await Run("package", mode, section, option, value, "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains($"Unrecognized option '{option}'", result.Error);
    }

    [Theory]
    [InlineData("package", "Package Info")]
    [InlineData("find", "Results")]
    [InlineData("library", "Top Leverage")]
    public async Task NonQueryableSection_IsExplicitNotUnknownOrCoreOnlyAdvertisement(string command, string section)
    {
        var result = await Run(command, "-Q", section, "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement described = json.RootElement.GetProperty("sections")[0];
        Assert.Equal(0, described.GetProperty("facet_count").GetInt32());
        Assert.Contains("no CLI query", described.GetProperty("summary").GetString());
        Assert.Empty(described.GetProperty("facets").EnumerateArray());
    }

    [Theory]
    [InlineData("Integrations")]
    [InlineData("Integration Opportunities")]
    public async Task IntegrationQuery_ExposesConceptAndEcosystemBindings(string section)
    {
        var result = await Run("library", "--package", "/missing/query-discovery.nupkg",
            "-Q", section, "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement described = Assert.Single(json.RootElement.GetProperty("sections").EnumerateArray());
        JsonElement[] facets =
            [.. described.GetProperty("facets").EnumerateArray()];
        Assert.Equal(["integration", "ecosystem"], facets.Select(
            facet => facet.GetProperty("name").GetString()));
        Assert.All(facets, facet =>
        {
            Assert.Equal(["--where"], facet.GetProperty("operators")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(["="], facet.GetProperty("comparisons")
                .EnumerateArray().Select(value => value.GetString()));
        });
        Assert.Equal(
            IntegrationQueryOptions.IntegrationQueryKey.Values,
            facets[0].GetProperty("values").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["ecosystem.aspire"],
            facets[1].GetProperty("values").EnumerateArray()
                .Select(value => value.GetString()));
        foreach (string value in IntegrationQueryOptions.EcosystemQueryKey.Values)
            Assert.True(IntegrationQueryOptions.TryExtract(
                [$"ecosystem={value}"], out _, out _, out var error), error.ToString());
        foreach (string value in IntegrationQueryOptions.IntegrationQueryKey.Values)
            Assert.True(IntegrationQueryOptions.TryExtract(
                [$"integration={value}"], out _, out _, out var error), error.ToString());
    }

    [Fact]
    public async Task PackageQueryDiscovery_IsInertAndDescribesExecutableTerms()
    {
        var result = await Run(
            "package",
            "query",
            "-Q",
            "Packages",
            "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "package query",
            json.RootElement.GetProperty("command").GetString());
        JsonElement[] facets =
        [
            .. json.RootElement.GetProperty("sections")[0]
                .GetProperty("facets").EnumerateArray(),
        ];
        Assert.Equal(
            PackageQueryOptions.QueryKeys.Select(key => key.Name),
            facets.Select(facet => facet.GetProperty("name").GetString()));
        JsonElement toolFormat = facets.Single(facet =>
            facet.GetProperty("name").GetString()
                == PackageQuery.ToolFormatTermKey);
        Assert.Equal(
            ["v1", "v2"],
            toolFormat.GetProperty("values").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            "package-content",
            toolFormat.GetProperty("execution_class").GetString());
        Assert.Equal(
            "metadata",
            facets.Single(facet =>
                facet.GetProperty("name").GetString()
                    == PackageQuery.ReferencesTermKey)
                .GetProperty("execution_class").GetString());
        JsonElement depends = facets.Single(facet =>
            facet.GetProperty("name").GetString()
                == PackageQuery.DependsTermKey);
        Assert.Equal(
            "NuGet package ID or prefix",
            depends.GetProperty("value_kind").GetString());
        Assert.Equal(
            ["=", "starts-with"],
            depends.GetProperty("comparisons").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["any", "MIT", "OSMF"],
            facets.Single(facet =>
                facet.GetProperty("name").GetString()
                    == PackageQuery.LicenseTermKey)
                .GetProperty("values").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            "all or NuGet target framework",
            facets.Single(facet =>
                facet.GetProperty("name").GetString()
                    == PackageQuery.DependencyTargetTermKey)
                .GetProperty("value_kind").GetString());
    }

    [Fact]
    public async Task UnknownSection_FailsWithSuggestions()
    {
        var result = await Run("library", "-Q", "Body Shape");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Body Shapes", result.Error);
    }

    [Fact]
    public async Task CompanionSelection_IsEquivalentAndSupportsSchemaDiscovery()
    {
        var query = await Run("type", "-Q", "Body Shapes", "--json");
        var selected = await Run("type", "-S", "Query: Body Shapes", "--json");
        Assert.Equal(0, selected.ExitCode);
        Assert.Equal(query.Output, selected.Output);
        var schema = await Run("type", "-D", "Query: Body Shapes", "--schema", "--json");
        Assert.Equal(0, schema.ExitCode);
        Assert.Contains("Operators", schema.Output);
        Assert.Contains("Comparisons", schema.Output);
    }

    [Theory]
    [InlineData("-S", false)]
    [InlineData("-S", true)]
    [InlineData("--select", false)]
    [InlineData("--select", true)]
    public async Task PackageQueryCompanionSelection_DoesNotExecuteQuery(
        string option,
        bool attached)
    {
        string[] selector = attached
            ? [$"{option}=Query: Packages"]
            : [option, "Query: Packages"];
        var result = await Run(
            ["package", "query", .. selector, "--json"]);
        var query = await Run(
            "package",
            "query",
            "-Q",
            "Packages",
            "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(query.Output, result.Output);
    }

    [Fact]
    public async Task DefaultMarkdown_PreservesCopyableQuerySyntax()
    {
        var result = await Run("type", "-Q", "Performance Triage", "-v:d");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("`=, !=, >=, <=`", result.Output);
        Assert.Contains("`--where \"RootReach>=10\"`", result.Output);
        Assert.DoesNotContain("&gt;", result.Output);
        Assert.DoesNotContain("&lt;", result.Output);
    }

    [Fact]
    public async Task ProjectedJson_PreservesQuerySyntaxWithoutPresentationMarkup()
    {
        var result = await Run("type", "-Q", "Performance Triage", "--fields", "Example", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement rows = Assert.Single(json.RootElement.EnumerateObject()).Value;
        Assert.Contains(rows.EnumerateArray(),
            row => row.GetProperty("example").GetString() == "--where \"RootReach>=10\"");
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    [InlineData("--markdown")]
    public async Task CompanionSchemaDiscovery_RequiresOneSection(string format)
    {
        var result = await Run("type", "-D", "Query:*", format);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("requires one section", result.Error);
    }

    [Fact]
    public async Task CompanionSelection_CannotRunAlongsideData()
    {
        var result = await Run("library", "-S", "Query: Body Shapes,Signals");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot be mixed", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task OrdinarySchemaDiscovery_DoesNotAcquireQueryCompanions()
    {
        var result = await Run("library", "-D", "--schema", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Query:", result.Output);
    }

    [Theory]
    [InlineData("--tsv", "--columns")]
    [InlineData("--jsonl", "--columns")]
    [InlineData("--json", "--columns")]
    [InlineData("--plaintext", "--columns")]
    [InlineData("--tsv", "--fields")]
    [InlineData("--jsonl", "--fields")]
    [InlineData("--json", "--fields")]
    [InlineData("--plaintext", "--fields")]
    public async Task QueryRows_UseSharedProjectionAndWindow(string format, string projection)
    {
        var result = await Run("type", "-Q", "Performance Triage",
            projection, "Facet", "--rows", "2", format);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Member", result.Output);
        Assert.Contains("Candidate", result.Output);
        Assert.DoesNotContain("RootReach", result.Output);
        Assert.DoesNotContain("--where \"Member", result.Output);
    }

    [Fact]
    public async Task QueryProjection_CombinesFieldAndColumnAliases()
    {
        var result = await Run("type", "-Q", "Body Shapes",
            "--fields", "facet", "--columns", "val*", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement row = Assert.Single(json.RootElement.EnumerateObject()).Value[0];
        Assert.Equal(["facet", "values"], row.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task BareQueryProjection_UsesFieldAlias()
    {
        var result = await Run("library", "-Q", "--fields", "section", "--rows", "1", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement rows = Assert.Single(json.RootElement.EnumerateObject()).Value;
        JsonElement row = Assert.Single(rows.EnumerateArray());
        Assert.Equal("section", Assert.Single(row.EnumerateObject()).Name);
    }

    [Fact]
    public async Task QueryCounts_DescribeMetadataRows()
    {
        var result = await Run("type", "-Q", "Performance Triage", "--rows", "2", "--count");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2", result.Output.Trim());
        var bare = await Run("type", "-Q", "--count");
        Assert.Equal(0, bare.ExitCode);
        Assert.Equal("4", bare.Output.Trim());
    }

    [Fact]
    public async Task PackageQueryDiscovery_AppliesSemanticSelectionStages()
    {
        var result = await Run(
            "package",
            "query",
            "-Q",
            "Packages",
            "-n",
            "1",
            "--rows",
            "2..2",
            "--count");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Package Query row selection stage 2 requires query facet row 2, "
                + "but only 1 query facet rows are available.",
            result.Error);
    }

    [Fact]
    public async Task FindSchemaDiscovery_CountsSelectedSemanticRows()
    {
        var result = await Run(
            "find",
            "JsonDocument",
            "-D",
            "Results",
            "-n",
            "1",
            "--count");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task MultiSectionStreams_AreRejectedRatherThanFlattened()
    {
        var result = await Run("type", "-Q", "Performance Triage,Body Shapes", "--jsonl");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("one section", result.Error);
    }

    [Fact]
    public async Task LongAliasAndAttachedSelector_AreEquivalent()
    {
        var shortForm = await Run("type", "-Q", "Body Shapes", "--json");
        var longForm = await Run("type", "--query-help=Body Shapes", "--json");
        Assert.Equal(0, longForm.ExitCode);
        Assert.Equal(shortForm.Output, longForm.Output);
    }

    [Fact]
    public async Task CommandlessQuery_RequiresAnAcquisitionFreeRoute()
    {
        var result = await Run("not-a-real-query-target", "-Q", "Body Shapes");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("explicit command", result.Error);
    }

    [Theory]
    [InlineData("-Q", "Body Shapes", "Performance Triage")]
    [InlineData("-D", "Query: Body Shapes", "Query: Performance Triage")]
    [InlineData("-D", "Signature", "IL")]
    public async Task CommandlessRepeatedSelectors_UseParserDiagnostic(
        string option, string first, string second)
    {
        var result = await Run("Missing.Helpers", option, first, option, second);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("expects a single argument", result.Error);
        Assert.DoesNotContain("InvalidOperationException", result.Error);
    }

    [Fact]
    public async Task CommandlessRepeatedCompanionSelection_RetainsListMerging()
    {
        var result = await Run("/missing/query-discovery.dll",
            "-S", "Query: Body Shapes", "-S", "Query: Performance: Arrays", "--json");
        var query = await Run("library", "-Q", "Body Shapes,Performance: Arrays", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(query.Output, result.Output);
    }

    [Fact]
    public async Task ExplicitCompanionWildcard_DoesNotChangeOrdinaryDataWildcards()
    {
        var result = await Run("type", "-S", "Query: Body*", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Query: Body Shapes", result.Output);
        var sections = LibrarySections.CreateCatalog().Sections.SelectableSectionNames;
        Assert.DoesNotContain(sections, name => name.StartsWith("Query:"));
    }

    [Fact]
    public void ExecutableRowVocabularyCapabilitiesDriveQueryDiscovery()
    {
        RowQueryVocabulary<QueryProjectionRow> whereOnly =
            QueryProjectionVocabulary(
                [RowQueryOperator.Equals],
                ordered: false);
        RowQueryVocabulary<QueryProjectionRow> asymmetric =
            QueryProjectionVocabulary(
                [
                    RowQueryOperator.Equals,
                    RowQueryOperator.LessOrEqual,
                ],
                ordered: true);

        SectionQueryKey initial = Assert.Single(
            RowQueryKeyProjection.Create(
                whereOnly,
                _ => new("integer", [], "10"),
                []));
        SectionQueryKey changed = Assert.Single(
            RowQueryKeyProjection.Create(
                asymmetric,
                _ => new("integer", [], "10"),
                []));

        Assert.Equal(["--where"], initial.Operators);
        Assert.Equal(["="], initial.Comparisons);
        Assert.Equal(
            ["--where", "--order-by", "--top"],
            changed.Operators);
        Assert.Equal(["=", "<="], changed.Comparisons);

        RowQueryKey<QueryProjectionRow> key =
            Assert.Single(asymmetric.Keys);
        Assert.True(
            PerformanceTriageOptions.TryBindPredicateOperator(
                key,
                RowPredicateOperator.LessOrEqual,
                out RowQueryOperator accepted));
        Assert.Equal(RowQueryOperator.LessOrEqual, accepted);
        Assert.False(
            PerformanceTriageOptions.TryBindPredicateOperator(
                key,
                RowPredicateOperator.GreaterOrEqual,
                out _));
        Assert.False(
            PerformanceTriageOptions.TryBindPredicateOperator(
                key,
                RowPredicateOperator.StartsWith,
                out _));
    }

    [Fact]
    public void PerformanceDiscoveryProjectsItsExecutableVocabulary()
    {
        RowQueryVocabulary<ILInspector.Analysis.OptimizationOpportunity> vocabulary =
            PerformanceTriageRowQuery.ExecutableVocabulary;
        ImmutableArray<SectionQueryKey> keys =
            PerformanceTriageRowQuery.QueryKeys;

        Assert.Equal(
            [.. vocabulary.Keys.Select(key => key.Key), "Triage"],
            keys.Select(key => key.Name));
        Assert.Contains(
            vocabulary.NamedOrders,
            order => order.Key == "AllocationFanout");
        Assert.DoesNotContain(
            keys,
            key => key.Name == "AllocationFanout");

        foreach (RowQueryKey<ILInspector.Analysis.OptimizationOpportunity>
            key in vocabulary.Keys)
        {
            SectionQueryKey projection = Assert.Single(
                keys,
                candidate => candidate.Name == key.Key);
            Assert.Equal(
                key.Operators.Count > 0,
                projection.Operators.Contains("--where"));
            Assert.Equal(
                key.SupportsOrdering,
                projection.Operators.Contains("--order-by"));
            Assert.Equal(
                key.SupportsOrdering,
                projection.Operators.Contains("--top"));
        }

        foreach (SectionQueryKey key in keys)
        {
            Assert.Equal(PerformanceTriageOptions.FilterableFields.Contains(key.Name),
                key.Operators.Contains("--where"));
            Assert.Equal(PerformanceTriageOptions.SortableFields.Contains(key.Name),
                key.Operators.Contains("--order-by"));
            Assert.Equal(key.Operators.Contains("--order-by"), key.Operators.Contains("--top"));
            foreach (string comparison in key.Comparisons)
            {
                string value = key.ValueKind switch { "integer" => "10", "rank" => "high", _ => "*" };
                var options = new PerformanceTriageOptions { Where = [$"{key.Name}{comparison}{value}"] };
                Assert.True(options.TryGetPredicates(out _, out var error), error.ToString());
            }
            foreach (string value in key.Values)
            {
                var options = new PerformanceTriageOptions { Where = [$"{key.Name}={value}"] };
                Assert.True(options.TryGetPredicates(out _, out var error), error.ToString());
            }
            if (key.Operators.Contains("--order-by"))
            {
                var options = new PerformanceTriageOptions { OrderBy = $"{key.Name} desc", Top = 10 };
                Assert.True(options.TryGetOrderTerms(out _, out var error), error.ToString());
            }
        }
    }

    private static RowQueryVocabulary<QueryProjectionRow> QueryProjectionVocabulary(
        IReadOnlyList<RowQueryOperator> operators,
        bool ordered)
    {
        Func<
            RowQueryOrderDirection,
            IComparer<RowQueryValue<int>>>? comparerFactory =
            ordered
                ? direction => RowQueryValueOrder.Create(
                    Comparer<int>.Default,
                    direction,
                    missingLast: false)
                : null;
        RowQueryKey<QueryProjectionRow> field =
            RowQueryKey<QueryProjectionRow>.Create(
                RowQueryKeyIdentity.Create(),
                "Score",
                operators,
                row => RowQueryValue<int>.Present(row.Score),
                (_, _) => _ => true,
                comparerFactory);
        return RowQueryVocabulary<QueryProjectionRow>.Create(
            RowQueryVocabularyIdentity.Create(),
            [field],
            []);
    }

    private sealed record QueryProjectionRow(int Score);
}
