using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Networking;
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

        var pathless = await RunAsync("vocabulary", "-D");

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
            "QueryFacet",
            root.GetProperty("resource_kind").GetString());
        Assert.Equal(
            "library-literal",
            root.GetProperty("details")
                .GetProperty("key")
                .GetString());
        Assert.Contains(
            explanationDocument.RootElement
                .GetProperty("resources")
                .EnumerateArray(),
            resource =>
                resource.GetProperty("resource_kind").GetString()
                    == "ConsumerBinding");
        Assert.Contains(
            explanationDocument.RootElement
                .GetProperty("relationships")
                .EnumerateArray(),
            relationship =>
                relationship.GetProperty("relationship_kind").GetString()
                    == "ExposedBy"
                && relationship.GetProperty("target_path").GetString()
                    == "package-query/bindings/cli");
        Assert.Contains(
            explanationDocument.RootElement
                .GetProperty("relationships")
                .EnumerateArray(),
            relationship =>
                relationship.GetProperty("relationship_kind").GetString()
                    == "RequiredContext"
                && relationship.GetProperty("target_path").GetString()
                    == "package-query/query/facets/library-target");
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
                resource.GetProperty("resource_kind").GetString()
                    == "InspectionDocument"
                && resource.GetProperty("path").GetString()
                    == "package-files");
        Assert.Contains(
            document.RootElement
                .GetProperty("resources")
                .EnumerateArray(),
            resource =>
                resource.GetProperty("resource_kind").GetString()
                    == "QuerySpace"
                && resource.GetProperty("path").GetString()
                    == "package-files/query");
        Assert.Contains(
            document.RootElement
                .GetProperty("resources")
                .EnumerateArray(),
            resource =>
                resource.GetProperty("resource_kind").GetString()
                    == "ConsumerBinding"
                && resource.GetProperty("path").GetString()
                    == "package-files/bindings/cli");
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
            explanationDocument.RootElement
                .GetProperty("resources")[0]
                .GetProperty("details")
                .GetProperty("key")
                .GetString());
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
    public async Task NoncanonicalSlashBearingText_SearchesInsteadOfResolving()
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
            0,
            document.RootElement.GetProperty("match_count").GetInt32());
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
        var result = await RunAsync(
            "explain",
            "package",
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
            "StructuralSection",
            document.RootElement
                .GetProperty("resources")[0]
                .GetProperty("resource_kind")
                .GetString());
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
