using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

namespace NuGetFetch.Tests;

public sealed class NuGetCatalogAcquisitionTests
{
    private const string ServiceIndex =
        "https://feed.example/v3/index.json";
    private const string Catalog =
        "https://feed.example/v3/catalog/index.json";
    private const string AlternateCatalog =
        "https://feed.example/v3/catalog-alt/index.json";
    private const string Page1 =
        "https://feed.example/v3/catalog/page1.json";
    private const string Page2 =
        "https://feed.example/v3/catalog/page2.json";
    private const string Page3 =
        "https://feed.example/v3/catalog/page3.json";
    private const string DetailsLeaf =
        "https://feed.example/v3/catalog/leaf/details.json";
    private const string ExternalDetailsLeaf =
        "https://metadata.example/catalog/leaf/details.json";

    private static readonly DateTimeOffset Day0 = Utc(2026, 1, 1);
    private static readonly DateTimeOffset Day1 = Utc(2026, 1, 2);
    private static readonly DateTimeOffset Day2 = Utc(2026, 1, 3);
    private static readonly DateTimeOffset Day3 = Utc(2026, 1, 4);
    private static readonly DateTimeOffset Day4 = Utc(2026, 1, 5);

    [Fact]
    public void RequestAndOptionsExposeOnlyCatalogAcquisitionBounds()
    {
        var maximum = new NuGetCatalogRequest(
            Day0,
            Day0 + NuGetCatalogRequest.MaximumInterval);

        Assert.Equal(Day0, maximum.FromExclusive);
        Assert.Equal(
            Day0 + TimeSpan.FromDays(42),
            maximum.ThroughInclusive);
        Assert.Equal(
            ["FromExclusive", "ThroughInclusive"],
            typeof(NuGetCatalogRequest)
                .GetProperties()
                .Where(property => property.GetMethod?.IsStatic == false)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));

        Assert.Throws<ArgumentException>(
            () => new NuGetCatalogRequest(
                Day0.ToOffset(TimeSpan.FromHours(1)),
                Day1));
        Assert.Throws<ArgumentException>(
            () => new NuGetCatalogRequest(
                Day0,
                Day1.ToOffset(TimeSpan.FromHours(-1))));
        Assert.Throws<ArgumentException>(
            () => new NuGetCatalogRequest(Day0, Day0));
        Assert.Throws<ArgumentException>(
            () => new NuGetCatalogRequest(Day1, Day0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NuGetCatalogRequest(
                Day0,
                Day0 + TimeSpan.FromDays(42) + TimeSpan.FromTicks(1)));

        var options = new NuGetFetchOptions();
        Assert.Equal(512, options.MaxCatalogPages);
        Assert.Equal(1024, options.MaxCatalogHttpAttempts);
        Assert.Equal(512L * 1024 * 1024, options.MaxCatalogDecodedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NuGetFetchOptions.Validate(
                options with { MaxCatalogPages = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NuGetFetchOptions.Validate(
                options with { MaxCatalogHttpAttempts = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NuGetFetchOptions.Validate(
                options with { MaxCatalogDecodedBytes = 0 }));

        foreach (Type type in new[]
        {
            typeof(NuGetCatalogEvent),
            typeof(NuGetCatalogPage),
            typeof(NuGetCatalogPackageReceipt),
            typeof(PackageSourceOperationResult<NuGetCatalogPage>),
            typeof(PackageSourceOperationResult<NuGetCatalogPackageReceipt>),
        })
        {
            ConstructorInfo constructor = Assert.Single(
                type.GetConstructors(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic));
            Assert.False(constructor.IsPublic);
            Assert.Equal(
                typeof(object),
                constructor.GetParameters()[0].ParameterType);
        }
    }

    [Fact]
    public async Task AdvertisedCatalogUsesSortedPlusOnePagesAndExactWindow()
    {
        string before = "https://feed.example/v3/catalog/before.json";
        string after = "https://feed.example/v3/catalog/after.json";
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":["Other","Catalog/3.0.0"]}""")),
            [Catalog] = Json(
                IndexDocumentWithCount(
                    Day4,
                    999,
                    Page(after, Day4),
                    Page(Page2, Day3),
                    Page(before, Day0),
                    Page(Page1, Day1))),
            [Page1] = Json(
                PageDocumentWithCount(
                    Catalog,
                    Day1,
                    5000,
                    Item(
                        "https://feed.example/leaf/z.json",
                        "Repeat.Package",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"),
                    Item(
                        "https://feed.example/leaf/exclusive.json",
                        "Excluded",
                        "1.0.0",
                        Day0,
                        "nuget:PackageDetails"),
                    Item(
                        "https://feed.example/leaf/a.json",
                        "Alpha",
                        "2.0.0",
                        Day0 + TimeSpan.FromHours(12),
                        "nuget:PackageDetails"))),
            [Page2] = Json(
                PageDocument(
                    Catalog,
                    Day3,
                    Item(
                        "https://feed.example/leaf/after.json",
                        "Excluded",
                        "2.0.0",
                        Day3,
                        "nuget:PackageDetails"),
                    Item(
                        "https://feed.example/leaf/tie-z.json",
                        "Repeat.Package",
                        "1.0.0",
                        Day1 + TimeSpan.FromHours(12),
                        "nuget:PackageDetails"),
                    Item(
                        "https://feed.example/leaf/inclusive.json",
                        "Deleted.Package",
                        "3.0.0",
                        Day2,
                        "nuget:PackageDelete"),
                    Item(
                        "https://feed.example/leaf/tie-a.json",
                        "Repeat.Package",
                        "1.0.0",
                        Day1 + TimeSpan.FromHours(12),
                        "nuget:PackageDetails"))),
        };
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler, association: association);
        var request = new NuGetCatalogRequest(Day0, Day2);

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(source, request);

        Assert.Equal(2, outcomes.Count);
        NuGetCatalogPage first = Succeeded(outcomes[0]);
        NuGetCatalogPage terminal = Succeeded(outcomes[1]);
        Assert.Same(source.Source, first.Source);
        Assert.Same(association, first.Source.Association);
        Assert.Same(request, first.Request);
        Assert.Equal(Day4, first.CapturedHorizon);
        Assert.Equal(1, first.PagesAcquired);
        Assert.Equal(3, first.HttpAttempts);
        Assert.Equal(2, first.InWindowEventCount);
        Assert.Null(first.Completion);
        Assert.Equal(
            ["alpha", "repeat.package"],
            first.Events.Select(item => item.Coordinate.PackageId));
        Assert.Equal("Alpha", first.Events[0].PackageId);
        Assert.Equal("2.0.0", first.Events[0].Version);

        Assert.Equal(
            [
                "https://feed.example/leaf/tie-a.json",
                "https://feed.example/leaf/tie-z.json",
                "https://feed.example/leaf/inclusive.json",
            ],
            terminal.Events.Select(item => item.LeafUrl));
        Assert.Equal(
            [
                NuGetCatalogEventKind.Details,
                NuGetCatalogEventKind.Details,
                NuGetCatalogEventKind.Delete,
            ],
            terminal.Events.Select(item => item.Kind));
        Assert.Equal(2, terminal.PagesAcquired);
        Assert.Equal(4, terminal.HttpAttempts);
        Assert.Equal(5, terminal.InWindowEventCount);
        Assert.Equal(
            NuGetCatalogCompletion.WindowExhausted,
            terminal.Completion);
        Assert.True(terminal.DecodedBytes > first.DecodedBytes);
        Assert.Equal(
            [ServiceIndex, Catalog, Page1, Page2],
            handler.Requested);
        Assert.DoesNotContain(before, handler.Requested);
        Assert.DoesNotContain(after, handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            url => url.Contains("/leaf/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, NuGetCatalogCompletion.PageLimitReached)]
    [InlineData(2, NuGetCatalogCompletion.WindowExhausted)]
    public async Task TiedCrossingPagesStopOnlyAtCoverageOrAVisibleLimit(
        int maximumPages,
        NuGetCatalogCompletion expectedCompletion)
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day3,
                    Page(Page1, Day3),
                    Page(Page2, Day3))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day3,
                    Item(
                        "https://feed.example/leaf/after-first.json",
                        "After.First",
                        "1.0.0",
                        Day3,
                        "nuget:PackageDetails"))),
            [Page2] = Json(
                PageDocument(
                    Catalog,
                    Day3,
                    Item(
                        "https://feed.example/leaf/in-window.json",
                        "In.Window",
                        "1.0.0",
                        Day2,
                        "nuget:PackageDetails"),
                    Item(
                        "https://feed.example/leaf/after-second.json",
                        "After.Second",
                        "1.0.0",
                        Day3,
                        "nuget:PackageDetails"))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    MaxCatalogPages = maximumPages,
                });

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                new NuGetCatalogRequest(Day0, Day2));

        Assert.Equal(maximumPages, outcomes.Count);
        NuGetCatalogPage terminal = Succeeded(outcomes[^1]);
        Assert.Equal(expectedCompletion, terminal.Completion);
        if (maximumPages == 1)
        {
            Assert.Empty(terminal.Events);
        }
        else
        {
            Assert.Equal(
                "in.window",
                Assert.Single(terminal.Events).Coordinate.PackageId);
        }

        Assert.Equal(
            maximumPages == 1
                ? [ServiceIndex, Catalog, Page1]
                : [ServiceIndex, Catalog, Page1, Page2],
            handler.Requested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualMaximumPagesDoNotInferOrderFromAdvertisedUrls(
        bool laterOnlyPageFirst)
    {
        string existingPage = PageDocument(
            Catalog,
            Day3,
            Item(
                "https://feed.example/leaf/earlier.json",
                "Earlier",
                "1.0.0",
                Day2,
                "nuget:PackageDetails"),
            Item(
                "https://feed.example/leaf/existing.json",
                "Existing",
                "1.0.0",
                Day3,
                "nuget:PackageDetails"));
        string laterOnlyPage = PageDocument(
            Catalog,
            Day3,
            Item(
                "https://feed.example/leaf/later.json",
                "Later",
                "1.0.0",
                Day3,
                "nuget:PackageDetails"));
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day3,
                    Page(Page1, Day3),
                    Page(Page2, Day3))),
            [Page1] = Json(
                laterOnlyPageFirst ? laterOnlyPage : existingPage),
            [Page2] = Json(
                laterOnlyPageFirst ? existingPage : laterOnlyPage),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                new NuGetCatalogRequest(Day0, Day3));

        Assert.Equal(2, outcomes.Count);
        NuGetCatalogPage first = Succeeded(outcomes[0]);
        NuGetCatalogPage second = Succeeded(outcomes[1]);
        Assert.Equal(
            laterOnlyPageFirst
                ? ["Later"]
                : ["Earlier", "Existing"],
            first.Events.Select(item => item.PackageId));
        Assert.Equal(
            laterOnlyPageFirst
                ? ["Earlier", "Existing"]
                : ["Later"],
            second.Events.Select(item => item.PackageId));
        if (laterOnlyPageFirst)
        {
            Assert.True(
                first.Events[^1].CommitTimestamp
                > second.Events[0].CommitTimestamp);
        }

        NuGetCatalogPage terminal = Succeeded(outcomes[^1]);
        Assert.Equal(2, terminal.PagesAcquired);
        Assert.Equal(3, terminal.InWindowEventCount);
        Assert.Equal(
            NuGetCatalogCompletion.WindowExhausted,
            terminal.Completion);
        Assert.Equal(
            [ServiceIndex, Catalog, Page1, Page2],
            handler.Requested);
    }

    [Fact]
    public async Task CapturedHorizonFiltersAPageThatGrowsAfterTheIndex()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day3,
                Item(
                    "https://feed.example/leaf/covered.json",
                    "Covered",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails"),
                Item(
                    "https://feed.example/leaf/grew.json",
                    "Too.New",
                    "1.0.0",
                    Day2,
                    "nuget:PackageDetails")));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day2))));

        Assert.Equal(Day1, terminal.CapturedHorizon);
        Assert.Equal(
            NuGetCatalogCompletion.SourceHorizonReached,
            terminal.Completion);
        Assert.Equal(
            "covered",
            Assert.Single(terminal.Events).Coordinate.PackageId);
    }

    [Fact]
    public async Task InconsistentIndexHorizonCannotClaimWindowCoverage()
    {
        var handler = StandardHandler(
            IndexDocument(Day2, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    "https://feed.example/leaf/older.json",
                    "Older",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day2))).Failure);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal([ServiceIndex, Catalog], handler.Requested);
    }

    [Fact]
    public async Task StaleActivePageIsRetriedBeforePublication()
    {
        string stale = PageDocument(
            Catalog,
            Day0,
            Item(
                "https://feed.example/leaf/stale.json",
                "Stale",
                "1.0.0",
                Day0,
                "nuget:PackageDetails"));
        string current = PageDocument(
            Catalog,
            Day1,
            Item(
                "https://feed.example/leaf/current.json",
                "Current",
                "1.0.0",
                Day1,
                "nuget:PackageDetails"));
        int attempts = 0;
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day1, Page(Page1, Day1))),
        };
        handler.Set(
            Page1,
            (request, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        ++attempts == 1 ? stale : current,
                        Encoding.UTF8,
                        "application/json"),
                    RequestMessage = request,
                }));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))));

        Assert.Equal(2, attempts);
        Assert.Equal(
            "current",
            Assert.Single(terminal.Events).Coordinate.PackageId);
        Assert.Equal(
            NuGetCatalogCompletion.WindowExhausted,
            terminal.Completion);
    }

    [Fact]
    public async Task PersistentlyStaleActivePageFailsAfterStandardRetries()
    {
        int attempts = 0;
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day1, Page(Page1, Day1))),
        };
        handler.Set(
            Page1,
            (request, _) =>
            {
                attempts++;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            PageDocument(
                                Catalog,
                                Day0,
                                Item(
                                    "https://feed.example/leaf/stale.json",
                                    "Stale",
                                    "1.0.0",
                                    Day0,
                                    "nuget:PackageDetails")),
                            Encoding.UTF8,
                            "application/json"),
                        RequestMessage = request,
                    });
            });
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(NuGetHttpRetry.MaximumRetries + 1, attempts);
        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
    }

    [Fact]
    public async Task EqualTimestampPagesUseAdvertisedUrlTieBreaker()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day1,
                    Page(Page2, Day1),
                    Page(Page1, Day1))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/z.json",
                        "First.Page",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
            [Page2] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/a.json",
                        "Second.Page",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                new NuGetCatalogRequest(Day0, Day1));

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(
            "first.page",
            Assert.Single(Succeeded(outcomes[0]).Events).Coordinate.PackageId);
        Assert.Equal(
            "second.page",
            Assert.Single(Succeeded(outcomes[1]).Events).Coordinate.PackageId);
        Assert.Equal(
            [ServiceIndex, Catalog, Page1, Page2],
            handler.Requested);
    }

    [Fact]
    public async Task EmptyCoveredPrefixStillProducesOneTerminalValue()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day0, Page(Page1, Day0))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day2))));

        Assert.Empty(terminal.Events);
        Assert.Equal(0, terminal.PagesAcquired);
        Assert.Equal(2, terminal.HttpAttempts);
        Assert.Equal(0, terminal.InWindowEventCount);
        Assert.Equal(
            NuGetCatalogCompletion.SourceHorizonReached,
            terminal.Completion);
        Assert.Equal([ServiceIndex, Catalog], handler.Requested);
    }

    [Theory]
    [InlineData(false, PackageSourceFailureKind.Unsupported)]
    [InlineData(true, PackageSourceFailureKind.InvalidResponse)]
    public async Task CatalogDiscoveryHasNoHardCodedFallback(
        bool malformedSupportedResource,
        PackageSourceFailureKind expected)
    {
        string resources = malformedSupportedResource
            ? """{"@id":"not a URL","@type":"Catalog/3.0.0"}"""
            : """{"@id":"https://feed.example/search","@type":"SearchQueryService"}""";
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(ServiceDocument(resources)),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(expected, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Catalog, failure.Capability);
        Assert.Same(source.Source, failure.Source);
        Assert.Equal([ServiceIndex], handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            url => url.Contains("nuget.org", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EquivalentCatalogFailsOverOnlyBeforePagePublication()
    {
        string alternatePage =
            "https://feed.example/v3/catalog-alt/page.json";
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"},"""
                    + $$"""{"@id":"{{AlternateCatalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day1, Page(Page1, Day1))),
            [Page1] = Json("""{}"""),
            [AlternateCatalog] = Json(
                IndexDocument(Day1, Page(alternatePage, Day1))),
            [alternatePage] = Json(
                PageDocument(
                    AlternateCatalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/alternate.json",
                        "Alternate",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))));

        Assert.Equal("alternate", Assert.Single(terminal.Events).Coordinate.PackageId);
        Assert.Equal(5, terminal.HttpAttempts);
        Assert.Equal(
            [
                ServiceIndex,
                Catalog,
                Page1,
                AlternateCatalog,
                alternatePage,
            ],
            handler.Requested);
    }

    [Fact]
    public async Task FailureAfterPublishedPageDoesNotMixCatalogRoots()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"},"""
                    + $$"""{"@id":"{{AlternateCatalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day2,
                    Page(Page1, Day1),
                    Page(Page2, Day2))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/first.json",
                        "First",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
            [Page2] = Json("""{"commitId":"broken"}"""),
            [AlternateCatalog] = Json(
                IndexDocument(Day2, Page(Page3, Day2))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                new NuGetCatalogRequest(Day0, Day2));

        Assert.Equal(2, outcomes.Count);
        Assert.Equal("first", Assert.Single(Succeeded(outcomes[0]).Events).Coordinate.PackageId);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            outcomes[1].Failure!.Kind);
        Assert.DoesNotContain(AlternateCatalog, handler.Requested);
    }

    [Theory]
    [InlineData(CatalogLimit.Page)]
    [InlineData(CatalogLimit.Request)]
    [InlineData(CatalogLimit.DecodedBytes)]
    public async Task AcquisitionBoundsAreTypedPartialCompletion(
        CatalogLimit limit)
    {
        string service = ServiceDocument(
            $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""");
        string index = IndexDocument(
            Day2,
            Page(Page1, Day1),
            Page(Page2, Day2));
        string firstPage = PageDocument(
            Catalog,
            Day1,
            Item(
                "https://feed.example/leaf/first.json",
                "First",
                "1.0.0",
                Day1,
                "nuget:PackageDetails"));
        string secondPage = PageDocument(
            Catalog,
            Day2,
            Item(
                "https://feed.example/leaf/second.json",
                "Second",
                "1.0.0",
                Day2,
                "nuget:PackageDetails"));
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(service),
            [Catalog] = Json(index),
            [Page1] = Json(firstPage),
            [Page2] = Json(secondPage),
        };
        long bytesThroughFirst =
            Utf8Length(service) + Utf8Length(index) + Utf8Length(firstPage);
        var options = new NuGetFetchOptions
        {
            MaxCatalogPages =
                limit == CatalogLimit.Page ? 1 : 512,
            MaxCatalogHttpAttempts =
                limit == CatalogLimit.Request ? 3 : 1024,
            MaxCatalogDecodedBytes =
                limit == CatalogLimit.DecodedBytes
                    ? bytesThroughFirst
                    : NuGetFetchOptions.DefaultMaxCatalogDecodedBytes,
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler, options);

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day2))));

        Assert.Equal("first", Assert.Single(terminal.Events).Coordinate.PackageId);
        Assert.Equal(1, terminal.PagesAcquired);
        Assert.Equal(1, terminal.InWindowEventCount);
        Assert.Equal(
            limit switch
            {
                CatalogLimit.Page =>
                    NuGetCatalogCompletion.PageLimitReached,
                CatalogLimit.Request =>
                    NuGetCatalogCompletion.RequestLimitReached,
                CatalogLimit.DecodedBytes =>
                    NuGetCatalogCompletion.DecodedByteLimitReached,
                _ => throw new ArgumentOutOfRangeException(nameof(limit)),
            },
            terminal.Completion);
        Assert.DoesNotContain(Page2, handler.Requested);
    }

    [Fact]
    public async Task RetriesConsumeCatalogWideAttemptBudget()
    {
        string page = PageDocument(
            Catalog,
            Day1,
            Item(
                "https://feed.example/leaf/retried.json",
                "Retried",
                "1.0.0",
                Day1,
                "nuget:PackageDetails"));
        int attempts = 0;
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day1, Page(Page1, Day1))),
        };
        handler.Set(
            Page1,
            (request, _) => Task.FromResult(
                ++attempts == 1
                    ? new HttpResponseMessage(
                        HttpStatusCode.ServiceUnavailable)
                    {
                        RequestMessage = request,
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            page,
                            Encoding.UTF8,
                            "application/json"),
                        RequestMessage = request,
                    }));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))));

        Assert.Equal(2, attempts);
        Assert.Equal(4, terminal.HttpAttempts);
        Assert.Equal(
            [ServiceIndex, Catalog, Page1, Page1],
            handler.Requested);
    }

    [Theory]
    [InlineData(3, NuGetCatalogCompletion.RequestLimitReached)]
    [InlineData(4, NuGetCatalogCompletion.WindowExhausted)]
    public async Task RedirectHopsConsumeCatalogWideAttemptBudget(
        int maximumAttempts,
        NuGetCatalogCompletion expectedCompletion)
    {
        string page = PageDocument(
            Catalog,
            Day1,
            Item(
                "https://feed.example/leaf/redirected.json",
                "Redirected",
                "1.0.0",
                Day1,
                "nuget:PackageDetails"));
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day1, Page(Page1, Day1))),
            [Page2] = Json(page),
        };
        handler.Set(
            Page1,
            (request, _) =>
            {
                var response =
                    new HttpResponseMessage(HttpStatusCode.Redirect)
                    {
                        RequestMessage = request,
                    };
                response.Headers.Location = new Uri(Page2);
                return Task.FromResult(response);
            });
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    MaxCatalogHttpAttempts = maximumAttempts,
                });

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))));

        Assert.Equal(expectedCompletion, terminal.Completion);
        Assert.Equal(maximumAttempts, terminal.HttpAttempts);
        Assert.Equal(
            maximumAttempts == 3
                ? [ServiceIndex, Catalog, Page1]
                : [ServiceIndex, Catalog, Page1, Page2],
            handler.Requested);
        Assert.Equal(
            maximumAttempts == 3 ? 0 : 1,
            terminal.Events.Length);
    }

    [Fact]
    public async Task ExhaustedDecodedBudgetStopsBeforeTheFirstPage()
    {
        string service = ServiceDocument(
            $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""");
        string index = IndexDocument(
            Day1,
            Page(Page1, Day1));
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(service),
            [Catalog] = Json(index),
            [Page1] = new RouteResponse(
                HttpStatusCode.ServiceUnavailable,
                ""),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    MaxCatalogDecodedBytes =
                        Utf8Length(service) + Utf8Length(index),
                });

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))));

        Assert.Equal(
            NuGetCatalogCompletion.DecodedByteLimitReached,
            terminal.Completion);
        Assert.Equal([ServiceIndex, Catalog], handler.Requested);
    }

    [Fact]
    public async Task ExhaustedAttemptBudgetSkipsRetryDelay()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day1, Page(Page1, Day1))),
            [Page1] = new RouteResponse(
                HttpStatusCode.ServiceUnavailable,
                ""),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    MaxCatalogHttpAttempts = 3,
                    OperationTimeout = TimeSpan.FromMilliseconds(50),
                });

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))));

        Assert.Equal(
            NuGetCatalogCompletion.RequestLimitReached,
            terminal.Completion);
        Assert.Equal([ServiceIndex, Catalog, Page1], handler.Requested);
    }

    [Fact]
    public async Task AggregateByteCrossingRejectsTheWholeActivePage()
    {
        string service = ServiceDocument(
            $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""");
        string index = IndexDocument(
            Day2,
            Page(Page1, Day1),
            Page(Page2, Day2));
        string firstPage = PageDocument(
            Catalog,
            Day1,
            Item(
                "https://feed.example/leaf/first.json",
                "First",
                "1.0.0",
                Day1,
                "nuget:PackageDetails"));
        string secondPage = PageDocumentWithPadding(
            Catalog,
            Day2,
            Item(
                "https://feed.example/leaf/not-published.json",
                "Not.Published",
                "1.0.0",
                Day2,
                "nuget:PackageDetails"),
            padding: new string('x', 256));
        long maximum =
            Utf8Length(service)
            + Utf8Length(index)
            + Utf8Length(firstPage)
            + Utf8Length(secondPage) / 2;
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(service),
            [Catalog] = Json(index),
            [Page1] = Json(firstPage),
            [Page2] = Json(secondPage),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    MaxCatalogDecodedBytes = maximum,
                });

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                new NuGetCatalogRequest(Day0, Day2));

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(
            "first",
            Assert.Single(Succeeded(outcomes[0]).Events).Coordinate.PackageId);
        NuGetCatalogPage terminal = Succeeded(outcomes[1]);
        Assert.Empty(terminal.Events);
        Assert.Equal(1, terminal.PagesAcquired);
        Assert.Equal(1, terminal.InWindowEventCount);
        Assert.Equal(maximum, terminal.DecodedBytes);
        Assert.Equal(
            NuGetCatalogCompletion.DecodedByteLimitReached,
            terminal.Completion);
        Assert.Contains(Page2, handler.Requested);
    }

    [Fact]
    public async Task PerResponseLimitAndMalformedPageRemainTypedFailures()
    {
        string page = PageDocumentWithPadding(
            Catalog,
            Day1,
            Item(
                "https://feed.example/leaf/large.json",
                "Large",
                "1.0.0",
                Day1,
                "nuget:PackageDetails"),
            padding: new string('x', 4096));
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            page);
        int setupMaximum = checked((int)Math.Max(
            Utf8Length(handler.Body(ServiceIndex)),
            Utf8Length(handler.Body(Catalog))));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = setupMaximum,
                });

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(PackageSourceFailureKind.ResponseRejected, failure.Kind);
    }

    [Fact]
    public async Task MalformedUtf16TextRemainsATypedFailure()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                """{"version":"\uD800","resources":[]}"""),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal([ServiceIndex], handler.Requested);
    }

    [Theory]
    [InlineData(MalformedCatalogDocument.ServiceIndex)]
    [InlineData(MalformedCatalogDocument.CatalogIndex)]
    [InlineData(MalformedCatalogDocument.CatalogPage)]
    public async Task MalformedUtf16PropertyNameRemainsATypedFailure(
        MalformedCatalogDocument document)
    {
        string service = document == MalformedCatalogDocument.ServiceIndex
            ? """{"version":"3.0.0","resources":[],"\uD800":true}"""
            : ServiceDocument(
                $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""");
        string index = document == MalformedCatalogDocument.CatalogIndex
            ? $$"""
                {"commitId":"index","commitTimeStamp":"{{Stamp(Day1)}}",
                "count":1,"items":[{{Page(Page1, Day1)}}],"\uD800":true}
                """
            : IndexDocument(Day1, Page(Page1, Day1));
        string page = document == MalformedCatalogDocument.CatalogPage
            ? $$"""
                {"commitId":"page","commitTimeStamp":"{{Stamp(Day1)}}",
                "count":0,"parent":"{{Catalog}}","items":[],"\uD800":true}
                """
            : PageDocument(Catalog, Day1);
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(service),
            [Catalog] = Json(index),
            [Page1] = Json(page),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal(
            document switch
            {
                MalformedCatalogDocument.ServiceIndex =>
                    [ServiceIndex],
                MalformedCatalogDocument.CatalogIndex =>
                    [ServiceIndex, Catalog],
                MalformedCatalogDocument.CatalogPage =>
                    [ServiceIndex, Catalog, Page1],
                _ => throw new ArgumentOutOfRangeException(nameof(document)),
            },
            handler.Requested);
        Assert.Equal(PackageSourceCapabilities.Catalog, failure.Capability);
    }

    [Fact]
    public async Task MalformedLaterPagePublishesNoEventsFromThatPage()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day2,
                    Page(Page1, Day1),
                    Page(Page2, Day2))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/first.json",
                        "First",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
            [Page2] = Json(
                PageDocument(
                    Catalog,
                    Day2,
                    Item(
                        "https://feed.example/leaf/valid.json",
                        "Would.Be.Partial",
                        "1.0.0",
                        Day2,
                        "nuget:PackageDetails"),
                    Item(
                        "https://feed.example/leaf/invalid.json",
                        "../invalid",
                        "1.0.0",
                        Day2,
                        "nuget:PackageDetails"))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                new NuGetCatalogRequest(Day0, Day2));

        Assert.Equal(2, outcomes.Count);
        Assert.Equal("first", Assert.Single(Succeeded(outcomes[0]).Events).Coordinate.PackageId);
        Assert.Null(outcomes[1].Value);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            outcomes[1].Failure!.Kind);
    }

    [Theory]
    [InlineData(DuplicateLocation.Service)]
    [InlineData(DuplicateLocation.Index)]
    [InlineData(DuplicateLocation.Page)]
    [InlineData(DuplicateLocation.PageItem)]
    public async Task DuplicatePropertiesAreRejectedAtEveryDepth(
        DuplicateLocation location)
    {
        string resource =
            $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""";
        string service = location == DuplicateLocation.Service
            ? "{\"version\":\"3.0.0\",\"resources\":["
                + resource
                + "],\"ignored\":{\"x\":1,\"x\":2}}"
            : ServiceDocument(resource);
        string index = location == DuplicateLocation.Index
            ? "{\"commitId\":\"index\",\"commitTimeStamp\":\""
                + Stamp(Day1)
                + "\",\"count\":1,\"items\":["
                + Page(Page1, Day1)
                + "],\"ignored\":{\"x\":1,\"x\":2}}"
            : IndexDocument(Day1, Page(Page1, Day1));
        string item = Item(
            "https://feed.example/leaf/one.json",
            "One",
            "1.0.0",
            Day1,
            "nuget:PackageDetails",
            location == DuplicateLocation.PageItem
                ? ""","ignored":{"x":1,"x":2}"""
                : "");
        string page = location == DuplicateLocation.Page
            ? "{\"commitId\":\"page\",\"commitTimeStamp\":\""
                + Stamp(Day1)
                + "\",\"count\":1,\"parent\":\""
                + Catalog
                + "\",\"items\":["
                + item
                + "],\"ignored\":{\"x\":1,\"x\":2}}"
            : PageDocument(Catalog, Day1, item);
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(service),
            [Catalog] = Json(index),
            [Page1] = Json(page),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
    }

    [Theory]
    [InlineData("file:///catalog/page.json", false)]
    [InlineData("https://user:secret@feed.example/leaf.json", true)]
    public async Task MalformedAdvertisedUrlsRejectTheContainingDocument(
        string malformed,
        bool leaf)
    {
        string pageUrl = leaf ? Page1 : malformed;
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(Day1, Page(pageUrl, Day1))),
        };
        if (leaf)
        {
            handler[Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        malformed,
                        "Package",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails")));
        }

        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);
        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
    }

    [Fact]
    public async Task CredentialsStayOnSourceOriginAndLeafUrlIsNormalized()
    {
        const string CrossOriginPage =
            "https://cdn.example/catalog/page.json?sig=%2f";
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day1,
                    Page(CrossOriginPage, Day1))),
            [CrossOriginPage] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://bücher.example/leaf/naïve.json",
                        "Package",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
        };
        var credential = new PackageSourceCredential("user", "secret");
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler, credential: credential);

        NuGetCatalogPage terminal = Succeeded(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))));

        Assert.Equal("user:secret", handler.DecodedAuthFor(ServiceIndex));
        Assert.Equal("user:secret", handler.DecodedAuthFor(Catalog));
        Assert.Null(handler.AuthFor(CrossOriginPage));
        Assert.Equal(
            "https://xn--bcher-kva.example/leaf/na%C3%AFve.json",
            Assert.Single(terminal.Events).LeafUrl);
        Assert.DoesNotContain(
            handler.Requested,
            url => url.Contains("xn--bcher-kva", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PageAcquisitionIsBackpressured()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day2,
                    Page(Page1, Day1),
                    Page(Page2, Day2))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/first.json",
                        "First",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
            [Page2] = Json(
                PageDocument(
                    Catalog,
                    Day2,
                    Item(
                        "https://feed.example/leaf/second.json",
                        "Second",
                        "1.0.0",
                        Day2,
                        "nuget:PackageDetails"))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        await using IAsyncEnumerator<
            PackageSourceOperationResult<NuGetCatalogPage>> enumerator =
            source.AcquireCatalogAsync(
                    new NuGetCatalogRequest(Day0, Day2),
                    TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(
                    TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal([ServiceIndex, Catalog, Page1], handler.Requested);
        await Task.Delay(
            TimeSpan.FromMilliseconds(20),
            TestContext.Current.CancellationToken);
        Assert.Equal([ServiceIndex, Catalog, Page1], handler.Requested);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal([ServiceIndex, Catalog, Page1, Page2], handler.Requested);
        Assert.Equal(
            NuGetCatalogCompletion.WindowExhausted,
            enumerator.Current.Value!.Completion);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task ConsumerTimeDoesNotSpendTheInternalOperationDeadline()
    {
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day2,
                    Page(Page1, Day1),
                    Page(Page2, Day2))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/first.json",
                        "First",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
            [Page2] = Json(
                PageDocument(
                    Catalog,
                    Day2,
                    Item(
                        "https://feed.example/leaf/second.json",
                        "Second",
                        "1.0.0",
                        Day2,
                        "nuget:PackageDetails"))),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    OperationTimeout = TimeSpan.FromSeconds(1),
                });
        await using IAsyncEnumerator<
            PackageSourceOperationResult<NuGetCatalogPage>> enumerator =
            source.AcquireCatalogAsync(
                    new NuGetCatalogRequest(Day0, Day2),
                    TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(
                    TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        await Task.Delay(
            TimeSpan.FromMilliseconds(1100),
            TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(
            NuGetCatalogCompletion.WindowExhausted,
            enumerator.Current.Value!.Completion);
    }

    [Fact]
    public async Task CallerOwnedContextRetainsItsWallClockDeadline()
    {
        var handler = TwoPageHandler();
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<
            PackageSourceOperationResult<NuGetCatalogPage>> enumerator =
            source.AcquireCatalogAsync(
                    new NuGetCatalogRequest(Day0, Day2),
                    TestContext.Current.CancellationToken,
                    operation)
                .GetAsyncEnumerator(
                    TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        await Task.Delay(
            TimeSpan.FromMilliseconds(1100),
            TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(
            PackageSourceFailureKind.Timeout,
            enumerator.Current.Failure!.Kind);
        Assert.Throws<NuGetOperationTimeoutException>(
            operation.ThrowIfExpired);
        Assert.Equal(
            [ServiceIndex, Catalog, Page1],
            handler.Requested);
    }

    [Theory]
    [InlineData("12:00:00Z")]
    [InlineData("2026-01-02 00:00:00Z")]
    [InlineData("2026-01-02T00:00:00.Z")]
    [InlineData("2026-01-02T00:00:00z")]
    [InlineData("2026-01-02T00:00:00-00:00")]
    [InlineData("2026-01-02T00:00:00.12345678Z")]
    public async Task IncompleteOrNoncanonicalTimestampRemainsTypedFailure(
        string timestamp)
    {
        string index = $$"""
            {"commitId":"index","commitTimeStamp":"{{timestamp}}",
            "count":0,"items":[]}
            """;
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(index),
        };
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal([ServiceIndex, Catalog], handler.Requested);
    }

    [Fact]
    public async Task CallerOwnedContextRemainsUsableAfterStreamCompletion()
    {
        var handler = TwoPageHandler();
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                new NuGetCatalogRequest(Day0, Day2),
                operationContext: operation);

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(
            NuGetCatalogCompletion.WindowExhausted,
            Succeeded(outcomes[^1]).Completion);
        operation.ThrowIfExpired();
    }

    [Fact]
    public async Task CallerCancellationPropagatesAfterPublishedPages()
    {
        var secondRequested =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RouteHandler
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day2,
                    Page(Page1, Day1),
                    Page(Page2, Day2))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/first.json",
                        "First",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
        };
        handler.Set(
            Page2,
            async (_, cancellationToken) =>
            {
                secondRequested.TrySetResult();
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        await using IAsyncEnumerator<
            PackageSourceOperationResult<NuGetCatalogPage>> enumerator =
            source.AcquireCatalogAsync(
                    new NuGetCatalogRequest(Day0, Day2),
                    cancellation.Token)
                .GetAsyncEnumerator(
                    TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        ValueTask<bool> next = enumerator.MoveNextAsync();
        await secondRequested.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => next.AsTask());
    }

    [Fact]
    public async Task CallerCancellationWinsARacingSourceFailure()
    {
        var handler = TwoPageHandler();
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        handler.Set(
            Page2,
            (request, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        RequestMessage = request,
                    });
            });
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        await using IAsyncEnumerator<
            PackageSourceOperationResult<NuGetCatalogPage>> enumerator =
            source.AcquireCatalogAsync(
                    new NuGetCatalogRequest(Day0, Day2),
                    cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => enumerator.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task OperationDeadlineIsATypedTerminalFailure()
    {
        var handler = new RouteHandler();
        handler.Set(
            ServiceIndex,
            async (_, cancellationToken) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(20),
                    OperationTimeout = TimeSpan.FromMilliseconds(80),
                });

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Catalog, failure.Capability);
    }

    [Fact]
    public async Task UnsupportedEventKindRejectsThePage()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    "https://feed.example/leaf/unknown.json",
                    "Unknown",
                    "1.0.0",
                    Day1,
                    "nuget:PackagePublish")));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            Assert.Single(
                await ReadAllAsync(
                    source,
                    new NuGetCatalogRequest(Day0, Day1))).Failure);
        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
    }

    [Fact]
    public async Task NuGetOrgPackageDetailsLeafReturnsCreatedReceipt()
    {
        const string commitId =
            "616117f5-d9dd-4664-82b9-74d87169bbe9";
        DateTimeOffset from = DateTimeOffset.Parse(
            "2017-10-31T00:00:00Z",
            CultureInfo.InvariantCulture);
        DateTimeOffset commitTimestamp = DateTimeOffset.Parse(
            "2017-10-31T23:30:32.4197849Z",
            CultureInfo.InvariantCulture);
        DateTimeOffset created = DateTimeOffset.Parse(
            "2017-10-31T23:29:22.387Z",
            CultureInfo.InvariantCulture);
        var handler = StandardHandler(
            IndexDocument(
                commitTimestamp,
                Page(Page1, commitTimestamp)),
            PageDocument(
                Catalog,
                commitTimestamp,
                Item(
                    DetailsLeaf,
                    "Util.Biz.Payments",
                    "0.0.4-preview",
                    commitTimestamp,
                    "nuget:PackageDetails",
                    commitId: commitId)));
        handler[DetailsLeaf] = Json(
            """
            {
              "@type": ["PackageDetails", "catalog:Permalink"],
              "catalog:commitId": "616117f5-d9dd-4664-82b9-74d87169bbe9",
              "catalog:commitTimeStamp": "2017-10-31T23:30:32.4197849Z",
              "created": "2017-10-31T23:29:22.387Z",
              "id": "Util.Biz.Payments",
              "published": "2017-10-31T23:29:22.387Z",
              "version": "0.0.4-preview"
            }
            """);
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(
                source,
                new NuGetCatalogRequest(
                    from,
                    commitTimestamp));

        NuGetCatalogPackageReceipt receipt = SucceededReceipt(
            await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken));

        Assert.Same(source.Source, receipt.Source);
        Assert.Same(detailsEvent, receipt.DetailsEvent);
        Assert.Equal(created, receipt.ReceivedAt);
        Assert.Equal(
            NuGetCatalogPackageReceiptBasis.Created,
            receipt.Basis);
        Assert.Equal(
            [ServiceIndex, Catalog, Page1, DetailsLeaf],
            handler.Requested);
    }

    [Fact]
    public async Task PackageDetailsLeafUsesPublishedFallbackAndNormalizesUtc()
    {
        DateTimeOffset published = new(
            2026,
            1,
            1,
            2,
            0,
            0,
            TimeSpan.FromHours(2));
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler[DetailsLeaf] = Json(
            DetailsDocument(
                "Contoso",
                "1.0.0",
                Day1,
                published,
                created: null));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        NuGetCatalogPackageReceipt receipt = SucceededReceipt(
            await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken));

        Assert.Equal(published.ToUniversalTime(), receipt.ReceivedAt);
        Assert.Equal(TimeSpan.Zero, receipt.ReceivedAt.Offset);
        Assert.Equal(
            NuGetCatalogPackageReceiptBasis.PublishedFallback,
            receipt.Basis);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("type-shape")]
    [InlineData("package")]
    [InlineData("version")]
    [InlineData("commit")]
    [InlineData("commit-time")]
    [InlineData("created")]
    [InlineData("published")]
    [InlineData("duplicate")]
    public async Task MalformedOrMismatchedDetailsLeafIsInvalidResponse(
        string fault)
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler[DetailsLeaf] = Json(
            FaultedDetailsDocument(fault));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            (await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken)).Failure);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Catalog, failure.Capability);
        Assert.Equal(detailsEvent.Coordinate, failure.Coordinate);
        Assert.Same(source.Source, failure.Source);
    }

    [Fact]
    public async Task MissingRetainedLeafIsInvalidRatherThanCoordinateAbsence()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            (await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken)).Failure);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.NotEqual(PackageSourceFailureKind.NotFound, failure.Kind);
        Assert.Equal(detailsEvent.Coordinate, failure.Coordinate);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DetailsLeafAuthenticationFailureIsTyped(
        HttpStatusCode statusCode)
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler[DetailsLeaf] = new RouteResponse(statusCode, "");
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            (await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken)).Failure);

        Assert.Equal(
            PackageSourceFailureKind.AuthenticationRequired,
            failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Catalog, failure.Capability);
        Assert.Equal(detailsEvent.Coordinate, failure.Coordinate);
        Assert.Same(source.Source, failure.Source);
    }

    [Fact]
    public async Task DetailsLeafTransportFailureIsTyped()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler.Set(
            DetailsLeaf,
            (_, _) => Task.FromException<HttpResponseMessage>(
                new HttpRequestException(
                    "Rejected by intermediary.",
                    inner: null,
                    HttpStatusCode.BadRequest)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            (await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken)).Failure);

        Assert.Equal(PackageSourceFailureKind.Transport, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Catalog, failure.Capability);
        Assert.Equal(detailsEvent.Coordinate, failure.Coordinate);
        Assert.Same(source.Source, failure.Source);
    }

    [Fact]
    public async Task OffOriginDetailsLeafDoesNotReceiveSourceCredential()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    ExternalDetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler[ExternalDetailsLeaf] = Json(
            DetailsDocument(
                "Contoso",
                "1.0.0",
                Day1,
                Day0 + TimeSpan.FromHours(1),
                Day0));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                credential: new PackageSourceCredential(
                    "user",
                    "token"));
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        _ = SucceededReceipt(
            await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken));

        Assert.Null(handler.AuthFor(ExternalDetailsLeaf));
        Assert.Equal("user:token", handler.DecodedAuthFor(ServiceIndex));
    }

    [Fact]
    public async Task DeleteAndForeignEventsAreRejectedBeforeNetworkWork()
    {
        var deleteHandler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDelete")));
        using INuGetCatalogPackageSourceClient first =
            CreateSource(deleteHandler);
        NuGetCatalogEvent deleteEvent =
            await AcquireSingleEventAsync(first);
        int requestsBeforeDelete = deleteHandler.Requested.Count;

        await Assert.ThrowsAsync<ArgumentException>(
            () => first.GetPackageReceiptAsync(
                deleteEvent,
                TestContext.Current.CancellationToken));
        Assert.Equal(requestsBeforeDelete, deleteHandler.Requested.Count);

        var otherHandler = new RouteHandler();
        using INuGetCatalogPackageSourceClient other =
            CreateSource(otherHandler);
        int requestsBeforeForeign = otherHandler.Requested.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => other.GetPackageReceiptAsync(
                deleteEvent,
                TestContext.Current.CancellationToken));
        Assert.Equal(requestsBeforeForeign, otherHandler.Requested.Count);
    }

    [Fact]
    public async Task DetailsLeafHonorsMetadataResponseBound()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler[DetailsLeaf] = Json(
            DetailsDocument(
                "Contoso",
                "1.0.0",
                Day1,
                Day0,
                Day0,
                new string('x', 1_024)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = 512,
                });
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            (await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken)).Failure);

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            failure.Kind);
        Assert.Equal(detailsEvent.Coordinate, failure.Coordinate);
    }

    [Fact]
    public async Task DetailsLeafOperationDeadlineIsTypedTimeout()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler.Set(
            DetailsLeaf,
            async (_, cancellationToken) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(20),
                    OperationTimeout = TimeSpan.FromMilliseconds(80),
                });
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);

        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(
            (await source.GetPackageReceiptAsync(
                detailsEvent,
                TestContext.Current.CancellationToken)).Failure);

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(detailsEvent.Coordinate, failure.Coordinate);
    }

    [Fact]
    public async Task CallerCancellationPropagatesFromDetailsLeaf()
    {
        var handler = StandardHandler(
            IndexDocument(Day1, Page(Page1, Day1)),
            PageDocument(
                Catalog,
                Day1,
                Item(
                    DetailsLeaf,
                    "Contoso",
                    "1.0.0",
                    Day1,
                    "nuget:PackageDetails")));
        handler.Set(
            DetailsLeaf,
            async (_, cancellationToken) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        using INuGetCatalogPackageSourceClient source =
            CreateSource(handler);
        NuGetCatalogEvent detailsEvent =
            await AcquireSingleEventAsync(source);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.GetPackageReceiptAsync(
                detailsEvent,
                cancellation.Token));
    }

    private static INuGetCatalogPackageSourceClient CreateSource(
        RouteHandler handler,
        NuGetFetchOptions? options = null,
        PackageSourceAssociation? association = null,
        PackageSourceCredential? credential = null)
    {
        IPackageSourceClient source = PackageSourceClientFactory.Create(
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri(ServiceIndex)),
            association ?? PackageSourceAssociation.Create(),
            handler,
            options,
            credential);
        Assert.True(
            source.Capabilities.HasFlag(PackageSourceCapabilities.Catalog));
        return Assert.IsAssignableFrom<INuGetCatalogPackageSourceClient>(
            source);
    }

    private static async Task<NuGetCatalogEvent> AcquireSingleEventAsync(
        INuGetCatalogPackageSourceClient source,
        NuGetCatalogRequest? request = null)
    {
        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>> outcomes =
            await ReadAllAsync(
                source,
                request ?? new NuGetCatalogRequest(Day0, Day1));
        NuGetCatalogPage page = Succeeded(Assert.Single(outcomes));
        return Assert.Single(page.Events);
    }

    private static async Task<
        IReadOnlyList<PackageSourceOperationResult<NuGetCatalogPage>>>
        ReadAllAsync(
            INuGetCatalogPackageSourceClient source,
            NuGetCatalogRequest request,
            NuGetOperationContext? operationContext = null)
    {
        var outcomes =
            new List<PackageSourceOperationResult<NuGetCatalogPage>>();
        await foreach (
            PackageSourceOperationResult<NuGetCatalogPage> outcome
            in source.AcquireCatalogAsync(
                request,
                TestContext.Current.CancellationToken,
                operationContext))
        {
            outcomes.Add(outcome);
        }

        return outcomes;
    }

    private static NuGetCatalogPage Succeeded(
        PackageSourceOperationResult<NuGetCatalogPage> outcome)
    {
        Assert.Null(outcome.Failure);
        return Assert.IsType<NuGetCatalogPage>(outcome.Value);
    }

    private static NuGetCatalogPackageReceipt SucceededReceipt(
        PackageSourceOperationResult<NuGetCatalogPackageReceipt> outcome)
    {
        Assert.Null(outcome.Failure);
        return Assert.IsType<NuGetCatalogPackageReceipt>(outcome.Value);
    }

    private static RouteHandler StandardHandler(
        string index,
        string page) =>
        new()
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(index),
            [Page1] = Json(page),
        };

    private static RouteHandler TwoPageHandler() =>
        new()
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(
                IndexDocument(
                    Day2,
                    Page(Page1, Day1),
                    Page(Page2, Day2))),
            [Page1] = Json(
                PageDocument(
                    Catalog,
                    Day1,
                    Item(
                        "https://feed.example/leaf/first.json",
                        "First",
                        "1.0.0",
                        Day1,
                        "nuget:PackageDetails"))),
            [Page2] = Json(
                PageDocument(
                    Catalog,
                    Day2,
                    Item(
                        "https://feed.example/leaf/second.json",
                        "Second",
                        "1.0.0",
                        Day2,
                        "nuget:PackageDetails"))),
        };

    private static string ServiceDocument(string resources) =>
        $$"""{"version":"3.0.0","resources":[{{resources}}]}""";

    private static string IndexDocument(
        DateTimeOffset horizon,
        params string[] pages) =>
        IndexDocument(horizon, pages, pages.Length);

    private static string IndexDocumentWithCount(
        DateTimeOffset horizon,
        int count,
        params string[] pages) =>
        IndexDocument(horizon, pages, count);

    private static string IndexDocument(
        DateTimeOffset horizon,
        string[] pages,
        int count) =>
        $$"""
        {"commitId":"index","commitTimeStamp":"{{Stamp(horizon)}}",
        "count":{{count}},"items":[{{string.Join(',', pages)}}]}
        """;

    private static string Page(
        string url,
        DateTimeOffset horizon) =>
        $$"""
        {"@id":"{{url}}","commitId":"page",
        "commitTimeStamp":"{{Stamp(horizon)}}","count":999}
        """;

    private static string PageDocument(
        string parent,
        DateTimeOffset horizon,
        params string[] items) =>
        PageDocument(parent, horizon, items, count: items.Length);

    private static string PageDocumentWithCount(
        string parent,
        DateTimeOffset horizon,
        int count,
        params string[] items) =>
        PageDocument(parent, horizon, items, count);

    private static string PageDocumentWithPadding(
        string parent,
        DateTimeOffset horizon,
        string item,
        string padding) =>
        PageDocument(
            parent,
            horizon,
            [item],
            count: 1,
            padding);

    private static string PageDocument(
        string parent,
        DateTimeOffset horizon,
        string[] items,
        int count,
        string? padding = null)
    {
        string paddingProperty = padding is null
            ? ""
            : ",\"padding\":\"" + padding + "\"";
        return $$"""
            {"commitId":"page","commitTimeStamp":"{{Stamp(horizon)}}",
            "count":{{count}},"parent":"{{parent}}","items":[{{string.Join(',', items)}}]
            {{paddingProperty}}}
            """;
    }

    private static string Item(
        string leafUrl,
        string packageId,
        string version,
        DateTimeOffset timestamp,
        string kind,
        string suffix = "",
        string commitId = "commit") =>
        $$"""
        {"@id":"{{leafUrl}}","@type":"{{kind}}","commitId":"{{commitId}}",
        "commitTimeStamp":"{{Stamp(timestamp)}}","nuget:id":"{{packageId}}",
        "nuget:version":"{{version}}"{{suffix}}}
        """;

    private static string DetailsDocument(
        string packageId,
        string version,
        DateTimeOffset commitTimestamp,
        DateTimeOffset published,
        DateTimeOffset? created,
        string? padding = null)
    {
        string createdProperty = created is { } value
            ? ",\"created\":\"" + Stamp(value) + "\""
            : "";
        string paddingProperty = padding is null
            ? ""
            : ",\"padding\":\"" + padding + "\"";
        return $$"""
        {"@type":["PackageDetails","catalog:Permalink"],
        "catalog:commitId":"commit",
        "catalog:commitTimeStamp":"{{Stamp(commitTimestamp)}}",
        "id":"{{packageId}}","version":"{{version}}",
        "published":"{{published:yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz}}"
        {{createdProperty}}{{paddingProperty}}}
        """;
    }

    private static string FaultedDetailsDocument(string fault)
    {
        string type = fault switch
        {
            "kind" => "\"PackageDelete\"",
            "type-shape" => "[\"PackageDetails\",42]",
            _ => "[\"PackageDetails\",\"catalog:Permalink\"]",
        };
        string packageId =
            fault == "package" ? "Other.Package" : "Contoso";
        string version = fault == "version" ? "2.0.0" : "1.0.0";
        string commit = fault == "commit" ? "other" : "commit";
        string commitTimestamp = fault == "commit-time"
            ? Stamp(Day0)
            : Stamp(Day1);
        string created = fault == "created"
            ? "2026-01-01"
            : Stamp(Day0);
        string published = fault == "published"
            ? "2026-01-01T00:00:00"
            : Stamp(Day0);
        string duplicate =
            fault == "duplicate" ? ",\"id\":\"Contoso\"" : "";
        return $$"""
        {"@type":{{type}},"catalog:commitId":"{{commit}}",
        "catalog:commitTimeStamp":"{{commitTimestamp}}",
        "id":"{{packageId}}","version":"{{version}}",
        "created":"{{created}}","published":"{{published}}"{{duplicate}}}
        """;
    }

    private static string Stamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static long Utf8Length(string value) =>
        Encoding.UTF8.GetByteCount(value);

    private static RouteResponse Json(string body) =>
        new(HttpStatusCode.OK, body);

    public enum CatalogLimit
    {
        Page,
        Request,
        DecodedBytes,
    }

    public enum DuplicateLocation
    {
        Service,
        Index,
        Page,
        PageItem,
    }

    public enum MalformedCatalogDocument
    {
        ServiceIndex,
        CatalogIndex,
        CatalogPage,
    }

    private sealed record RouteResponse(
        HttpStatusCode StatusCode,
        string Body);

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<
            string,
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>>> _routes =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _bodies =
            new(StringComparer.Ordinal);
        private readonly List<
            (string Url, AuthenticationHeaderValue? Authorization)> _requests =
            [];

        public RouteResponse this[string url]
        {
            set
            {
                _bodies[url] = value.Body;
                Set(
                    url,
                    (request, _) => Task.FromResult(
                        new HttpResponseMessage(value.StatusCode)
                        {
                            Content = new StringContent(
                                value.Body,
                                Encoding.UTF8,
                                "application/json"),
                            RequestMessage = request,
                        }));
            }
        }

        public IReadOnlyList<string> Requested =>
            _requests.Select(item => item.Url).ToArray();

        public string Body(string url) => _bodies[url];

        public AuthenticationHeaderValue? AuthFor(string url) =>
            _requests.FirstOrDefault(
                item => item.Url.Equals(
                    url,
                    StringComparison.Ordinal)).Authorization;

        public string? DecodedAuthFor(string url)
        {
            AuthenticationHeaderValue? authorization = AuthFor(url);
            return authorization?.Parameter is { } parameter
                ? Encoding.UTF8.GetString(
                    Convert.FromBase64String(parameter))
                : null;
        }

        public void Set(
            string url,
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> response) =>
            _routes[url] = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            _requests.Add((url, request.Headers.Authorization));
            return _routes.TryGetValue(url, out var response)
                ? response(request, cancellationToken)
                : Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        RequestMessage = request,
                    });
        }
    }
}
