using System.Text.Json;
using System.Text.RegularExpressions;
using DotnetInspect.Cli.Options;
using DotnetInspector.Networking;
using QuerySpace;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PackageQueryExplanationTests : IDisposable
{
    private int requests;

    public PackageQueryExplanationTests()
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RejectNetworkHandler(() => requests++));
    }

    // PR-fast: installed metadata and query preparation only; no acquisition.
    [Theory]
    [InlineData("depends", "eq", "Polly", true)]
    [InlineData("depends", "starts-with", "Polly", true)]
    [InlineData("downloads", "starts-with", "1m", false)]
    public async Task InstalledComparisonSpelling_PreparesOnlyFacetAdmittedPredicates(
        string key, string comparison, string value, bool admitted)
    {
        var result = await Run("package", "query", "--explain");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement embedded = document.RootElement.GetProperty("_embedded");
        JsonElement facet = Assert.Single(embedded.GetProperty("facets").EnumerateArray(),
            facet => facet.GetProperty("key").GetString() == key);
        Assert.Equal(admitted, facet.GetProperty("operators").EnumerateArray()
            .Any(op => op.GetString() == comparison));
        JsonElement binding = Assert.Single(embedded.GetProperty("bindings").EnumerateArray());
        string rule = Assert.Single(binding.GetProperty("input_rules").EnumerateArray()
            .Select(rule => rule.GetString()!),
            rule => rule.Contains($"for {comparison}", StringComparison.Ordinal));
        Assert.Contains("use only operators exposed by the facet", rule);
        Match form = Regex.Match(rule, "--where \"([^\"]+)\" for " + comparison);
        Assert.True(form.Success, rule);
        string expression = form.Groups[1].Value
            .Replace("key", key, StringComparison.Ordinal)
            .Replace("value", value, StringComparison.Ordinal);

        bool accepted = PackageQueryOptions.TryCreate(
            "Microsoft.Extensions.Http.Polly", [expression],
            nuspecOnly: false, take: 1, rowSelection: null, includePrerelease: false,
            out PackageQueryOptions? options, out OptionError error);
        Assert.True(accepted == admitted, error.Message);
        if (admitted)
        {
            PackageQueryOptions prepared = Assert.IsType<PackageQueryOptions>(options);
            PortableQueryTerm term = Assert.Single(prepared.Plan.Terms);
            Assert.Equal(key, term.Key);
            Assert.Equal(comparison, PortableQueryModel.TextOf(term.Operator));
            Assert.Equal(value, term.Value);
            Assert.False(prepared.Plan.RequiresDependencyTraversal);
        }
        else
        {
            Assert.Null(options);
            Assert.Contains("does not support 'starts-with' predicates", error.ToString());
        }
        Assert.Equal(0, requests);
    }

    public void Dispose()
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(null);
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
    }

    [Theory]
    [InlineData(new[] { "package", "query", "--explain" }, 0)]
    [InlineData(new[] { "package", "query", "--explain", "--json" }, 0)]
    [InlineData(new[] { "--explain", "package", "query" }, 0)]
    [InlineData(new[] { "package", "--explain", "query", "--json" }, 0)]
    public async Task InstalledExplanation_ReturnsRootHalDatasetWithoutAcquisition(string[] args, int expectedExitCode)
    {
        var expected = await Run("explain", "package-query/query", ".hal", "--json");
        var actual = await Run(args);

        Assert.Equal(0, expected.ExitCode);
        Assert.Equal(expectedExitCode, actual.ExitCode);
        Assert.Equal(expected.Output, actual.Output);
        Assert.Equal("", actual.Error);
        Assert.Equal(0, requests);

        using JsonDocument document = JsonDocument.Parse(actual.Output);
        JsonElement root = document.RootElement;
        Assert.Equal("inspect-resource:/package-query/query?projection=hal",
            root.GetProperty("_links").GetProperty("self").GetProperty("href").GetString());
        Assert.Equal("Complete", root.GetProperty("scope").GetProperty("completeness").GetString());
        JsonElement embedded = root.GetProperty("_embedded");
        Dictionary<string, JsonElement> facets = embedded.GetProperty("facets")
            .EnumerateArray().ToDictionary(facet => facet.GetProperty("key").GetString()!);
        Assert.Contains(facets["depends"].GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("direct", StringComparison.Ordinal));
        Assert.Contains(facets["dependency-target"].GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("depends-ecosystem, or dependencies", StringComparison.Ordinal));
        Assert.Contains(facets["dependency-depth"].GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("at most 5 package candidates", StringComparison.Ordinal));
        JsonElement binding = Assert.Single(embedded.GetProperty("bindings").EnumerateArray());
        Assert.Contains(binding.GetProperty("input_rules").EnumerateArray(),
            rule => rule.GetString()!.Contains("--take N", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new[] { "Microsoft.Extensions.Http.Polly" }, 1)]
    [InlineData(new[] { "Microsoft.Extensions.Http.Polly@10.0.12" }, 1)]
    [InlineData(new[] { "--where", "depends=Polly" }, 1)]
    [InlineData(new[] { "--ecosystem", "aspire" }, 1)]
    [InlineData(new[] { "--take", "5" }, 1)]
    [InlineData(new[] { "--preview" }, 1)]
    [InlineData(new[] { "--nuspec-only" }, 1)]
    [InlineData(new[] { "--tfm", "net10.0" }, 1)]
    [InlineData(new[] { "-n", "1" }, 1)]
    [InlineData(new[] { "--rows", "1" }, 1)]
    [InlineData(new[] { "--head", "1" }, 1)]
    [InlineData(new[] { "--tail", "1" }, 1)]
    [InlineData(new[] { "--lines", "1" }, 1)]
    [InlineData(new[] { "--count" }, 1)]
    [InlineData(new[] { "--fields", "Id" }, 1)]
    [InlineData(new[] { "--columns", "Id" }, 1)]
    [InlineData(new[] { "-D" }, 1)]
    [InlineData(new[] { "-Q" }, 1)]
    [InlineData(new[] { "-S", "Packages" }, 1)]
    [InlineData(new[] { "--tree" }, 1)]
    [InlineData(new[] { "--source", "https://example.test/v3/index.json" }, 1)]
    [InlineData(new[] { "--compact", "--json" }, 1)]
    [InlineData(new[] { "--markdown" }, 1)]
    [InlineData(new[] { "--table" }, 1)]
    [InlineData(new[] { "--tsv" }, 1)]
    [InlineData(new[] { "--jsonl" }, 1)]
    [InlineData(new[] { "--envelope" }, 1)]
    [InlineData(new[] { "--plaintext" }, 1)]
    [InlineData(new[] { "--no-headers" }, 1)]
    [InlineData(new[] { "--value" }, 1)]
    [InlineData(new[] { "--urls" }, 1)]
    [InlineData(new[] { "--paths" }, 1)]
    [InlineData(new[] { "-E", ".tips" }, 1)]
    [InlineData(new[] { "-E" }, 1)]
    public async Task QueryInputsAndOutputModifiers_FailBeforeAcquisition(string[] extra, int expectedExitCode)
    {
        var result = await Run(["package", "query", "--explain", .. extra]);

        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.NotEmpty(result.Error);
        Assert.DoesNotContain("\"_embedded\"", result.Output);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(".tips")]
    [InlineData(".references")]
    [InlineData(".hal")]
    [InlineData(".depends")]
    public async Task DottedProjections_FailVisiblyBeforeAcquisition(string projection)
    {
        var result = await Run("package", "query", "--explain", projection);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("dotted projections are not available", result.Error);
        Assert.DoesNotContain("\"_embedded\"", result.Output);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task RootOutputModifier_FailsRatherThanBeingIgnored()
    {
        var result = await Run("-v", "q", "package", "query", "--explain");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("-v is not available with package query --explain", result.Error);
        Assert.DoesNotContain("\"_embedded\"", result.Output);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task RootOfflineControl_PreservesInstalledExplanation()
    {
        var expected = await Run("package", "query", "--explain");
        var result = await Run("--offline", "package", "query", "--explain");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected.Output, result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task EnvironmentFormat_IsRejectedUnlessJsonOverridesIt()
    {
        string? original = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "tsv");
            var rejected = await Run("package", "query", "--explain");
            var accepted = await Run("package", "query", "--explain", "--json");

            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("requires HAL JSON", rejected.Error);
            Assert.Empty(rejected.Output);
            Assert.Equal(0, accepted.ExitCode);
            using JsonDocument document = JsonDocument.Parse(accepted.Output);
            Assert.True(document.RootElement.TryGetProperty("_embedded", out _));
            Assert.Equal(0, requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", original);
        }
    }

    [Fact]
    public async Task InheritedPackageInput_CannotFallBackToCommandExplanation()
    {
        var result = await Run("package", "Microsoft.Extensions.Http.Polly", "query", "--explain");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("command-only", result.Error);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Help_AdvertisesInstalledExplanationAndQueryHelpKeepsItsExistingShape()
    {
        var help = await Run("package", "query", "--help");
        var queryHelp = await Run("package", "query", "-Q", "--json");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("--explain", help.Output);
        Assert.Contains("HAL JSON", help.Output);
        Assert.Equal(0, queryHelp.ExitCode);
        using JsonDocument document = JsonDocument.Parse(queryHelp.Output);
        Assert.True(document.RootElement.TryGetProperty("sections", out _));
        Assert.False(document.RootElement.TryGetProperty("_embedded", out _));
        Assert.Equal(0, requests);
    }

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    private sealed class RejectNetworkHandler(Action record) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            record();
            throw new InvalidOperationException("Installed explanation must not contact NuGet.");
        }
    }
}
