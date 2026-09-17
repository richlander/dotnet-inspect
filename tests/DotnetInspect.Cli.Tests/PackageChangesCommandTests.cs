using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGetFetch;

using CoreHttpClientFactory =
    DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PackageChangesCommandTests
{
    private const string ServiceIndex =
        "https://api.nuget.org/v3/index.json";
    private const string Catalog =
        "https://api.nuget.org/v3/catalog0/index.json";
    private const string Page =
        "https://api.nuget.org/v3/catalog0/page0.json";

    private static readonly DateTimeOffset ReferenceTime =
        Utc(2026, 10, 14);

    [Fact]
    public void ParserAcceptsExplicitReportControls()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
        [
            "package",
            "activity",
            "--ecosystem",
            "aspire",
            "--security-only",
            "--from",
            "2026-09-01T00:00:00-07:00",
            "--through",
            "2026-10-01T00:00:00-07:00",
            "-n",
            "7",
            "--json",
            "--compact",
            "--verbose",
        ]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParserDoesNotApplySemanticMaximumToRenderedLineCount()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
        [
            "package",
            "activity",
            "--ecosystem",
            "aspire",
            "-n",
            int.MaxValue.ToString(CultureInfo.InvariantCulture),
            "--lines",
        ]);

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(
        "package activity --ecosystem aspire --table",
        "--table is not supported with package activity")]
    [InlineData(
        "ecosystem aspire --changes",
        "Unrecognized command or argument '--changes'")]
    [InlineData(
        "package activity",
        "package activity requires --ecosystem")]
    [InlineData(
        "package activity --ecosystem aspire --from 2026-09-01T00:00:00Z",
        "--from and --through must be specified together")]
    [InlineData(
        "package activity --ecosystem aspire --from 2026-09-01 --through 2026-10-01",
        "must be ISO 8601 timestamps with an explicit UTC offset")]
    [InlineData(
        "package activity --ecosystem aspire -n 1001",
        "-n must be between 1 and 1000")]
    [InlineData(
        "package activity --ecosystem aspire --compact",
        "--compact requires package activity --json")]
    public void ParserRejectsUnsupportedOrAmbiguousRequests(
        string command,
        string expected)
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            command.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                expected,
                StringComparison.Ordinal));
    }

    [Fact]
    public void RetiredPackageChangesSpellingReportsReplacement()
    {
        Assert.True(CommandLineBuilder.TryGetRemovedCommandError(
            ["package", "changes", "--ecosystem", "aspire"],
            out string? error));
        Assert.Equal(
            "'package changes' has been removed. Use 'package activity' "
            + "with the same options.",
            error);
    }

    [Fact]
    public async Task JsonUsesDefaultIntervalAndExactEcosystemPackageSet()
    {
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                StandardCatalog(
                    ReferenceTime,
                    Item(
                        "Aspire.Hosting",
                        "9.5.0",
                        ReferenceTime - TimeSpan.FromDays(1))));
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));
        var options = Options(OutputFormat.Json) with
        {
            MaximumRows = 7,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageChangesCommand.ExecuteAsync(
                options,
                source,
                new GitHubNuGetAdvisoryService(
                    advisoryClient,
                    timeProvider: new FixedTimeProvider(ReferenceTime)),
                new VerboseLogger(false),
                new FixedTimeProvider(ReferenceTime)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        JsonElement request = root.GetProperty("request");
        Assert.True(request.GetProperty("used_default_interval").GetBoolean());
        Assert.Equal(
            "2026-09-02T00:00:00+00:00",
            request.GetProperty("from_exclusive").GetString());
        Assert.Equal(
            "2026-10-14T00:00:00+00:00",
            request.GetProperty("through_inclusive").GetString());
        Assert.Equal(7, request.GetProperty("maximum_rows").GetInt32());
        JsonElement packageScope = request.GetProperty("package_scope");
        Assert.Equal(
            "package-set.aspire",
            packageScope.GetProperty("selection_id").GetString());
        Assert.Contains(
            packageScope.GetProperty("package_ids").EnumerateArray(),
            package => package.GetString() == "Aspire.Hosting");
        JsonElement row = Assert.Single(
            root.GetProperty("rows").EnumerateArray());
        Assert.Equal(
            "Aspire.Hosting",
            row.GetProperty("catalog_activity")
                .GetProperty("package_id")
                .GetString());
        Assert.Equal(
            "Complete",
            root.GetProperty("summary")
                .GetProperty("completion")
                .GetString());
    }

    [Fact]
    public async Task HumanOutputShowsExplicitBoundsAndSecuritySelection()
    {
        DateTimeOffset from = Utc(2026, 9, 15);
        DateTimeOffset through = Utc(2026, 10, 1);
        using INuGetCatalogPackageSourceClient source =
            CreateSource(StandardCatalog(through));
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));
        var options = Options(OutputFormat.PlainText) with
        {
            FromExclusive = from,
            ThroughInclusive = through,
            SecurityOnly = true,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageChangesCommand.ExecuteAsync(
                options,
                source,
                new GitHubNuGetAdvisoryService(
                    advisoryClient,
                    timeProvider: new FixedTimeProvider(ReferenceTime)),
                new VerboseLogger(false),
                new FixedTimeProvider(ReferenceTime)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"({Time(from)}, {Time(through)}]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "Security relevant",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "package-set.aspire",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceHorizonLagRemainsVisibleWithoutMakingUsableOutputFail()
    {
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                StandardCatalog(
                    ReferenceTime - TimeSpan.FromMinutes(1)));
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));

        var result = await ConsoleCapture.RunAsync(
            () => PackageChangesCommand.ExecuteAsync(
                Options(OutputFormat.Json),
                source,
                new GitHubNuGetAdvisoryService(
                    advisoryClient,
                    timeProvider: new FixedTimeProvider(ReferenceTime)),
                new VerboseLogger(false),
                new FixedTimeProvider(ReferenceTime)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "Partial",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("completion")
                .GetString());
        Assert.Equal(
            "SourceHorizonReached",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("catalog_completion")
                .GetString());
    }

    [Fact]
    public async Task AdvisoryFailureKeepsReportEvidenceAndReturnsFailure()
    {
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                StandardCatalog(
                    ReferenceTime,
                    Item(
                        "Aspire.Hosting",
                        "9.5.0",
                        ReferenceTime - TimeSpan.FromDays(1))));
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.Forbidden, "[]"));

        var result = await ConsoleCapture.RunAsync(
            () => PackageChangesCommand.ExecuteAsync(
                Options(OutputFormat.Json),
                source,
                new GitHubNuGetAdvisoryService(
                    advisoryClient,
                    timeProvider: new FixedTimeProvider(ReferenceTime)),
                new VerboseLogger(false),
                new FixedTimeProvider(ReferenceTime)));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "Partial",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("completion")
                .GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());
        Assert.Equal(
            "Advisory",
            failure.GetProperty("provider").GetString());
        Assert.Single(
            document.RootElement.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task VerboseProgressStaysOnStderr()
    {
        using INuGetCatalogPackageSourceClient source =
            CreateSource(StandardCatalog(ReferenceTime));
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));

        var result = await ConsoleCapture.RunAsync(
            () => PackageChangesCommand.ExecuteAsync(
                Options(OutputFormat.Json),
                source,
                new GitHubNuGetAdvisoryService(
                    advisoryClient,
                    timeProvider: new FixedTimeProvider(ReferenceTime)),
                new VerboseLogger(true),
                new FixedTimeProvider(ReferenceTime)));

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            1,
            document.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Contains(
            "Catalog: scanning",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Catalog: scanning",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvocationOwnsSemanticLimitWithoutTruncatingJson()
    {
        bool wasOffline = CoreHttpClientFactory.IsOffline;
        try
        {
            CoreHttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions());
            CoreHttpClientFactory.ResetSharedForTesting();
            CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
                _ => StandardCatalog(Utc(2026, 9, 15)));

            var result = await InvokeAsync(
                [
                    "package",
                    "activity",
                    "--ecosystem",
                    "aspire",
                    "--from",
                    "2026-09-14T17:59:00Z",
                    "--through",
                    "2026-09-14T18:00:00Z",
                    "--json",
                    "-n",
                    "1",
                ]);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(
                1,
                document.RootElement
                    .GetProperty("request")
                    .GetProperty("maximum_rows")
                    .GetInt32());
            Assert.Equal(
                "Complete",
                document.RootElement
                    .GetProperty("summary")
                    .GetProperty("completion")
                    .GetString());
        }
        finally
        {
            CoreHttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions
                {
                    Offline = wasOffline,
                });
            CoreHttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task InvocationRejectsRenderedLineSelectionForJsonBeforeAcquisition()
    {
        var result = await InvokeAsync(
            [
                "package",
                "activity",
                "--ecosystem",
                "aspire",
                "--json",
                "-n",
                "1",
                "--lines",
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--lines and --tail-lines cannot be combined with JSON output",
            result.Error);
    }

    [Fact]
    public async Task InvocationHonorsOfflineCatalogPolicy()
    {
        bool wasOffline = CoreHttpClientFactory.IsOffline;
        try
        {
            CoreHttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions
                {
                    Offline = true,
                });
            CoreHttpClientFactory.ResetSharedForTesting();

            var result = await InvokeAsync(
                [
                    "package",
                    "activity",
                    "--ecosystem",
                    "aspire",
                    "--from",
                    "2026-09-14T17:59:00Z",
                    "--through",
                    "2026-09-14T18:00:00Z",
                    "--json",
                ]);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Network access is disabled (--offline mode)",
                result.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            CoreHttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions
                {
                    Offline = wasOffline,
                });
            CoreHttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task EcosystemWithoutPackageSetFailsBeforeAcquisition()
    {
        using INuGetCatalogPackageSourceClient source =
            CreateSource(new ThrowingHandler());
        using var advisoryClient = new HttpClient(new ThrowingHandler());

        var result = await ConsoleCapture.RunAsync(
            () => PackageChangesCommand.ExecuteAsync(
                Options(OutputFormat.Json) with
                {
                    Ecosystem = "platform",
                },
                source,
                new GitHubNuGetAdvisoryService(advisoryClient),
                new VerboseLogger(false),
                new FixedTimeProvider(ReferenceTime)));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "does not define an exact package set",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationStopsBeforeProducingADocument()
    {
        using INuGetCatalogPackageSourceClient source =
            CreateSource(new ThrowingHandler());
        using var advisoryClient = new HttpClient(new ThrowingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackageChangesCommand.ExecuteAsync(
                Options(OutputFormat.Json),
                source,
                new GitHubNuGetAdvisoryService(advisoryClient),
                new VerboseLogger(false),
                new FixedTimeProvider(ReferenceTime),
                cancellation.Token));
    }

    private static PackageChangesOptions Options(OutputFormat format) =>
        new()
        {
            Ecosystem = "aspire",
            MaximumRows = 100,
            Format = format,
        };

    private static Task<(int ExitCode, string Output, string Error)> InvokeAsync(
        string[] arguments) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(arguments, root);
            return CommandLineBuilder.InvokeWithLineWindowAsync(
                root.Parse(processed),
                processed);
        });

    private static INuGetCatalogPackageSourceClient CreateSource(
        HttpMessageHandler handler)
    {
        IPackageSourceClient source = PackageSourceClientFactory.Create(
            PackageSourceDescriptor.NuGetV3(
                "nuget.org",
                "NuGet.org",
                new Uri(ServiceIndex)),
            PackageSourceAssociation.Create(),
            handler);
        return Assert.IsAssignableFrom<INuGetCatalogPackageSourceClient>(
            source);
    }

    private static RouteHandler StandardCatalog(
        DateTimeOffset horizon,
        params string[] items) =>
        new()
        {
            [ServiceIndex] = Json(
                $$"""
                {"version":"3.0.0","resources":[
                {"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}]}
                """),
            [Catalog] = Json(
                $$"""
                {"commitId":"index",
                "commitTimeStamp":"{{Time(horizon)}}",
                "count":1,"items":[{
                "@id":"{{Page}}","commitId":"page",
                "commitTimeStamp":"{{Time(horizon)}}",
                "count":999}]}
                """),
            [Page] = Json(
                $$"""
                {"commitId":"page",
                "commitTimeStamp":"{{Time(horizon)}}",
                "count":{{items.Length}},"parent":"{{Catalog}}",
                "items":[{{string.Join(',', items)}}]}
                """),
        };

    private static string Item(
        string packageId,
        string version,
        DateTimeOffset timestamp) =>
        $$"""
        {"@id":"https://api.nuget.org/v3/catalog0/data/item.json",
        "@type":"nuget:PackageDetails",
        "commitId":"commit","commitTimeStamp":"{{Time(timestamp)}}",
        "nuget:id":"{{packageId}}","nuget:version":"{{version}}"}
        """;

    private static string Time(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static RouteResponse Json(string body) =>
        new(HttpStatusCode.OK, body);

    private sealed record RouteResponse(
        HttpStatusCode StatusCode,
        string Body);

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, RouteResponse> _routes =
            new(StringComparer.Ordinal);

        public RouteResponse this[string url]
        {
            set => _routes[url] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            RouteResponse response = _routes.TryGetValue(
                url,
                out RouteResponse? configured)
                ? configured
                : new RouteResponse(HttpStatusCode.NotFound, "{}");
            return Task.FromResult(new HttpResponseMessage(
                response.StatusCode)
            {
                Content = new StringContent(
                    response.Body,
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request,
            });
        }
    }

    private sealed class SingleResponseHandler(
        HttpStatusCode statusCode,
        string body)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request,
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The report opened an unexpected transport.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
