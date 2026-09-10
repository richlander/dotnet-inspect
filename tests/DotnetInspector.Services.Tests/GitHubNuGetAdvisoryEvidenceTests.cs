using System.Net;
using System.Text;
using NuGetFetch;
using NuGet.Versioning;

namespace DotnetInspector.Services.Tests;

public sealed class GitHubNuGetAdvisoryEvidenceTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClassifiesAffectedAndFixedCoordinatesSeparately()
    {
        using var handler = new RoutingHandler((_, _) => Json(AdvisoryPage(
            packageId: "Example.Client",
            range: "< 2.1.1",
            fixedVersion: "2.1.1")));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            timeProvider: new FixedTimeProvider(ObservedAt));
        GitHubNuGetAdvisoryRequest request = Request(
            At("Example.Client", "2.1.0"),
            At("Example.Client", "2.1.1"));

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                request,
                TestContext.Current.CancellationToken);

        Assert.True(result.Complete);
        Assert.Equal(ObservedAt, result.ObservedAt);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg,
            result.PackageProducer);
        Assert.Equal(1, result.ApiRequests);
        Assert.Empty(result.Failures);

        GitHubNuGetAdvisoryPackageEvidence affected = result.Packages[0];
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            affected.CurrentContextAvailability);
        Assert.Equal(
            "GHSA-AAAA-BBBB-CCCC",
            Assert.Single(affected.CurrentAdvisories).GhsaId);
        Assert.Empty(affected.FixedVersionAdvisories);

        GitHubNuGetAdvisoryPackageEvidence fixedVersion = result.Packages[1];
        Assert.Empty(fixedVersion.CurrentAdvisories);
        GitHubNuGetAdvisoryReference fixedReference =
            Assert.Single(fixedVersion.FixedVersionAdvisories);
        Assert.Equal("CVE-2026-1234", fixedReference.CveId);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 9, 16, 4, 11, TimeSpan.Zero),
            fixedReference.PublishedAt);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero),
            fixedReference.UpdatedAt);
    }

    [Fact]
    public async Task CompleteEmptyMeansCheckedWithoutMatch()
    {
        using var handler = new RoutingHandler((_, _) => Json("[]"));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.True(result.Complete);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            package.CurrentContextAvailability);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            package.FixedVersionAvailability);
        Assert.Empty(package.CurrentAdvisories);
        Assert.Empty(package.FixedVersionAdvisories);
    }

    [Fact]
    public async Task MalformedRangeMakesOnlyCurrentContextPartial()
    {
        using var handler = new RoutingHandler((_, _) => Json(AdvisoryPage(
            packageId: "Example.Client",
            range: "not a version range",
            fixedVersion: "2.1.1")));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.1")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.False(result.Complete);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.CurrentContextAvailability);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            package.FixedVersionAvailability);
        Assert.Single(package.FixedVersionAdvisories);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidData,
            result.Failures);
    }

    [Fact]
    public async Task MalformedFixedVersionMakesOnlyFixedEvidencePartial()
    {
        using var handler = new RoutingHandler((_, _) => Json(AdvisoryPage(
            packageId: "Example.Client",
            range: "< 2.1.1",
            fixedVersion: "not a version")));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            package.CurrentContextAvailability);
        Assert.Single(package.CurrentAdvisories);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.FixedVersionAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidData,
            result.Failures);
    }

    [Fact]
    public async Task MissingFixedVersionMakesOnlyFixedEvidencePartial()
    {
        const string page =
            """
            [
              {
                "ghsa_id": "GHSA-aaaa-bbbb-cccc",
                "cve_id": "CVE-2026-1234",
                "type": "reviewed",
                "severity": "high",
                "published_at": "2026-09-09T16:04:11Z",
                "updated_at": "2026-09-09T18:00:00Z",
                "withdrawn_at": null,
                "vulnerabilities": [
                  {
                    "package": {
                      "ecosystem": "nuget",
                      "name": "Example.Client"
                    },
                    "vulnerable_version_range": "< 2.1.1"
                  }
                ]
              }
            ]
            """;
        using var handler = new RoutingHandler((_, _) => Json(page));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            package.CurrentContextAvailability);
        Assert.Single(package.CurrentAdvisories);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.FixedVersionAvailability);
        Assert.Empty(package.FixedVersionAdvisories);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidData,
            result.Failures);
    }

    [Fact]
    public async Task ExplicitNullFixedVersionRemainsComplete()
    {
        using var handler = new RoutingHandler((_, _) => Json(AdvisoryPage(
            packageId: "Example.Client",
            range: "< 2.1.1",
            fixedVersion: null)));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.True(result.Complete);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            package.CurrentContextAvailability);
        Assert.Single(package.CurrentAdvisories);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            package.FixedVersionAvailability);
        Assert.Empty(package.FixedVersionAdvisories);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task RetainsPositiveEvidenceWhenContinuationFails()
    {
        using var handler = new RoutingHandler((request, call) =>
        {
            if (call == 1)
            {
                const string next =
                    "https://api.github.com/advisories"
                    + "?ecosystem=nuget&type=reviewed&is_withdrawn=false"
                    + "&per_page=100&affects=Example.Client&after=cursor";
                HttpResponseMessage response = Json(AdvisoryPage(
                    packageId: "Example.Client",
                    range: "< 2.1.1",
                    fixedVersion: "2.1.1"));
                response.Headers.TryAddWithoutValidation(
                    "Link",
                    $"<{next}>; rel=\"next\"");
                return response;
            }

            Assert.Contains("after=cursor", request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.Equal(2, result.ApiRequests);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.CurrentContextAvailability);
        Assert.Single(package.CurrentAdvisories);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.SourceUnavailable,
            result.Failures);
    }

    [Fact]
    public async Task RejectsContinuationThatChangesAuthority()
    {
        using var handler = new RoutingHandler((_, _) =>
        {
            HttpResponseMessage response = Json(AdvisoryPage(
                packageId: "Example.Client",
                range: "< 2.1.1",
                fixedVersion: "2.1.1"));
            response.Headers.TryAddWithoutValidation(
                "Link",
                "<https://attacker.example/advisories?after=cursor>; rel=\"next\"");
            return response;
        });
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.CurrentContextAvailability);
        Assert.Single(package.CurrentAdvisories);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidContinuation,
            result.Failures);
    }

    [Fact]
    public async Task RejectsContinuationThatChangesAdvisoryScope()
    {
        using var handler = new RoutingHandler((_, _) =>
        {
            HttpResponseMessage response = Json("[]");
            response.Headers.TryAddWithoutValidation(
                "Link",
                "<https://api.github.com/advisories"
                + "?ecosystem=nuget&type=unreviewed&is_withdrawn=false"
                + "&per_page=100&affects=Example.Client&after=cursor>"
                + "; rel=\"next\"");
            return response;
        });
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            Assert.Single(result.Packages).CurrentContextAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidContinuation,
            result.Failures);
    }

    [Fact]
    public async Task RejectsContinuationThatAddsAdvisoryFilter()
    {
        using var handler = new RoutingHandler((_, _) =>
        {
            HttpResponseMessage response = Json("[]");
            response.Headers.TryAddWithoutValidation(
                "Link",
                "<https://api.github.com/advisories"
                + "?ecosystem=nuget&type=reviewed&is_withdrawn=false"
                + "&per_page=100&affects=Example.Client"
                + "&severity=critical&after=cursor>; rel=\"next\"");
            return response;
        });
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.Equal(1, handler.Calls);
        Assert.False(result.Complete);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.CurrentContextAvailability);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.FixedVersionAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidContinuation,
            result.Failures);
    }

    [Fact]
    public async Task RequestLimitLeavesLaterBatchUnavailable()
    {
        using var handler = new RoutingHandler((_, _) => Json(AdvisoryPage(
            packageId: "Example.One",
            range: "< 2.0.0",
            fixedVersion: "2.0.0")));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                MaxPackageIdsPerRequest = 1,
                MaxApiRequests = 1,
            });

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(
                    At("Example.One", "1.0.0"),
                    At("Example.Two", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            result.Packages[0].CurrentContextAvailability);
        Assert.Single(result.Packages[0].CurrentAdvisories);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            result.Packages[1].CurrentContextAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.RequestLimitReached,
            result.Failures);
    }

    [Fact]
    public async Task AggregateByteLimitDoesNotTreatResponseAsChecked()
    {
        string firstPage = AdvisoryPage(
            packageId: "Example.One",
            range: "< 2.0.0",
            fixedVersion: "2.0.0");
        using var handler = new RoutingHandler(
            (_, call) => Json(call == 1 ? firstPage : "[]"));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                MaxPackageIdsPerRequest = 1,
                MaxAggregateResponseBytes =
                    Encoding.UTF8.GetByteCount(firstPage) + 1,
            });

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(
                    At("Example.One", "1.0.0"),
                    At("Example.Two", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Complete,
            result.Packages[0].CurrentContextAvailability);
        Assert.Single(result.Packages[0].CurrentAdvisories);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            result.Packages[1].CurrentContextAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.AggregateResponseByteLimitReached,
            result.Failures);
    }

    [Fact]
    public async Task OversizedBodiesCountTowardAggregateByteLimit()
    {
        using var handler = new RoutingHandler((_, call) =>
            call == 1
                ? Json("[]")
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(
                        new NonSeekableStream(new byte[300])),
                });
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                MaxPackageIdsPerRequest = 1,
                MaxResponseBytes = 128,
                MaxAggregateResponseBytes = 200,
            });

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(
                    At("Example.One", "1.0.0"),
                    At("Example.Two", "1.0.0"),
                    At("Example.Three", "1.0.0"),
                    At("Example.Four", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Calls);
        Assert.Equal(201, result.ResponseBytes);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.AggregateResponseByteLimitReached,
            result.Failures);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.ResponseByteLimitReached,
            result.Failures);
    }

    [Fact]
    public async Task ResponseByteLimitIsDistinctFromCheckedEmpty()
    {
        using var handler = new RoutingHandler((_, _) => Json("[]"));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                MaxResponseBytes = 1,
            });

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            Assert.Single(result.Packages).CurrentContextAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.ResponseByteLimitReached,
            result.Failures);
    }

    [Fact]
    public async Task RateLimitOrForbiddenIsDistinctFromCheckedEmpty()
    {
        using var handler = new RoutingHandler(
            (_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                MaxPackageIdsPerRequest = 1,
            });

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(
                    At("Example.One", "1.0.0"),
                    At("Example.Two", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            result.Packages[0].CurrentContextAvailability);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            result.Packages[1].CurrentContextAvailability);
        Assert.Equal(1, handler.Calls);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.RateLimitOrForbidden,
            result.Failures);
    }

    [Fact]
    public async Task DoesNotCreditIncidentalCrossBatchEvidence()
    {
        const string crossPackageAdvisory = """
            [
              {
                "ghsa_id": "GHSA-aaaa-bbbb-cccc",
                "cve_id": "CVE-2026-1234",
                "type": "reviewed",
                "severity": "high",
                "published_at": "2026-09-09T16:04:11Z",
                "updated_at": "2026-09-09T18:00:00Z",
                "withdrawn_at": null,
                "vulnerabilities": [
                  {
                    "package": {
                      "ecosystem": "nuget",
                      "name": "Example.One"
                    },
                    "vulnerable_version_range": "< 2.0.0",
                    "first_patched_version": "2.0.0"
                  },
                  {
                    "package": {
                      "ecosystem": "nuget",
                      "name": "Example.Two"
                    },
                    "vulnerable_version_range": "< 2.0.0",
                    "first_patched_version": "2.0.0"
                  }
                ]
              }
            ]
            """;
        using var handler = new RoutingHandler(
            (_, call) => call == 1
                ? Json(crossPackageAdvisory)
                : new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                MaxPackageIdsPerRequest = 1,
            });

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(
                    At("Example.One", "1.0.0"),
                    At("Example.Two", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Single(result.Packages[0].CurrentAdvisories);
        Assert.Empty(result.Packages[1].CurrentAdvisories);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            result.Packages[1].CurrentContextAvailability);
    }

    [Fact]
    public async Task AcceptsDocumentedUnknownSeverity()
    {
        string page = AdvisoryPage(
            packageId: "Example.Client",
            range: "< 2.0.0",
            fixedVersion: "2.0.0")
            .Replace(
                "\"severity\": \"high\"",
                "\"severity\": \"unknown\"",
                StringComparison.Ordinal);
        using var handler = new RoutingHandler((_, _) => Json(page));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            GitHubNuGetAdvisorySeverity.Unknown,
            Assert.Single(
                Assert.Single(result.Packages).CurrentAdvisories).Severity);
        Assert.True(result.Complete);
    }

    [Fact]
    public async Task MalformedEscapedStringProducesInvalidData()
    {
        string page = AdvisoryPage(
            packageId: "Example.Client",
            range: "< 2.0.0",
            fixedVersion: "2.0.0")
            .Replace(
                "\"severity\": \"high\"",
                "\"severity\": \"\\uD800\"",
                StringComparison.Ordinal);
        using var handler = new RoutingHandler((_, _) => Json(page));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "1.0.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.False(result.Complete);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.CurrentContextAvailability);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.FixedVersionAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidData,
            result.Failures);
    }

    [Fact]
    public async Task MatchesRequestAdmittedUnicodePackageId()
    {
        const string packageId = "Тест.Client";
        using var handler = new RoutingHandler((_, _) => Json(AdvisoryPage(
            packageId,
            range: "< 2.1.1",
            fixedVersion: "2.1.1")));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At(packageId, "2.1.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.True(result.Complete);
        Assert.Equal("тест.client", package.Coordinate.PackageId);
        Assert.Single(package.CurrentAdvisories);
        Assert.Empty(package.FixedVersionAdvisories);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task DateOnlyAdvisoryTimestampIsInvalid()
    {
        string page = AdvisoryPage(
            packageId: "Example.Client",
            range: "< 2.0.0",
            fixedVersion: "2.0.0")
            .Replace(
                "\"published_at\": \"2026-09-09T16:04:11Z\"",
                "\"published_at\": \"2026-09-09\"",
                StringComparison.Ordinal);
        using var handler = new RoutingHandler((_, _) => Json(page));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Empty(Assert.Single(result.Packages).CurrentAdvisories);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidData,
            result.Failures);
    }

    [Fact]
    public async Task OperationDeadlineBoundsLocalEvaluation()
    {
        string page = AdvisoryPage(
            packageId: "Example.Client",
            range: ">= 0.0.0",
            fixedVersion: null);
        using var handler = new RoutingHandler((_, _) => Json(page));
        using var client = new HttpClient(handler);
        PackageSourceCoordinate[] coordinates = Enumerable.Range(0, 100)
            .Select(index => At("Example.Client", $"1.0.{index}"))
            .ToArray();

        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                OperationTimeout = TimeSpan.FromMilliseconds(20),
            },
            new AdvancingTimeProvider());

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(coordinates),
                TestContext.Current.CancellationToken);

        Assert.False(result.Complete);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            Assert.Single(result.Packages.Select(
                static package => package.CurrentContextAvailability)
                .Distinct()));
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.DeadlineReached,
            result.Failures);
    }

    [Fact]
    public async Task OperationDeadlineIsUnavailableRatherThanEmpty()
    {
        using var client = new HttpClient(new DelayedHandler());
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                OperationTimeout = TimeSpan.FromMilliseconds(20),
            });

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            Assert.Single(result.Packages).CurrentContextAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.DeadlineReached,
            result.Failures);
    }

    [Fact]
    public async Task UriLimitRejectsOversizedSingletonAfterRollover()
    {
        using var handler = new RoutingHandler((_, _) => Json("[]"));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(
            client,
            new GitHubNuGetAdvisoryOptions
            {
                MaxRequestUriBytes = 512,
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcquireAsync(
                Request(
                    At("Example.One", "1.0.0"),
                    At(new string('Ж', 100), "1.0.0")),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(
        "\"type\": \"reviewed\"",
        "\"type\": \"unreviewed\"")]
    [InlineData(
        "\"withdrawn_at\": null",
        "\"withdrawn_at\": \"2026-09-10T00:00:00Z\"")]
    public async Task RejectsAdvisoryOutsideReviewedNonWithdrawnScope(
        string oldValue,
        string newValue)
    {
        string page = AdvisoryPage(
            packageId: "Example.Client",
            range: "< 2.0.0",
            fixedVersion: "2.0.0")
            .Replace(oldValue, newValue, StringComparison.Ordinal);
        using var handler = new RoutingHandler((_, _) => Json(page));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "1.0.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.Empty(package.CurrentAdvisories);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Partial,
            package.CurrentContextAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidData,
            result.Failures);
    }

    [Fact]
    public async Task DuplicateBearingJsonIsUnavailableRatherThanEmpty()
    {
        const string duplicate = """
            [
              {
                "ghsa_id": "GHSA-aaaa-bbbb-cccc",
                "ghsa_id": "GHSA-dddd-eeee-ffff"
              }
            ]
            """;
        using var handler = new RoutingHandler((_, _) => Json(duplicate));
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(At("Example.Client", "1.0.0")),
                TestContext.Current.CancellationToken);

        GitHubNuGetAdvisoryPackageEvidence package =
            Assert.Single(result.Packages);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            package.CurrentContextAvailability);
        Assert.Contains(
            GitHubNuGetAdvisoryFailureKind.InvalidData,
            result.Failures);
    }

    [Fact]
    public async Task DuplicateCoordinatesUseOneResultAndOneSelector()
    {
        using var handler = new RoutingHandler((request, _) =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Equal(
                query.IndexOf("Example.Client", StringComparison.Ordinal),
                query.LastIndexOf("Example.Client", StringComparison.Ordinal));
            return Json("[]");
        });
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        GitHubNuGetAdvisoryAcquisition result =
            await service.AcquireAsync(
                Request(
                    At("Example.Client", "1.0.0"),
                    At("example.client", "1.0.0")),
                TestContext.Current.CancellationToken);

        Assert.Single(result.Packages);
        Assert.Equal(1, result.ApiRequests);
    }

    [Theory]
    [InlineData("< 13.0.1", "13.0.0", true)]
    [InlineData("< 13.0.1", "13.0.1", false)]
    [InlineData(">= 8.0.0, <= 8.0.30", "8.0.30", true)]
    [InlineData(">= 8.0.0, <= 8.0.30", "8.0.31", false)]
    [InlineData(
        "<= 1.0.0 || >= 2.0.0, < 2.1.0",
        "2.0.5",
        true)]
    [InlineData(
        ">= 11.0.0-preview.1, < 11.0.0-rc.1",
        "11.0.0-preview.7",
        true)]
    [InlineData("[8.0.0,8.0.31)", "8.0.30", true)]
    [InlineData("8.0.30", "8.0.31", false)]
    public void EvaluatesNuGetAdvisoryRange(
        string expression,
        string version,
        bool expected)
    {
        bool parsed = GitHubNuGetAdvisoryService.TryEvaluateRange(
            expression,
            NuGetVersion.Parse(version),
            out bool actual);

        Assert.True(parsed);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RequestRejectsNonNuGetOrgProducer()
    {
        using IPackageSourceClient privateSource =
            PackageSourceClientFactory.Create(
                new NuGetFetch.PackageSource(
                    "private",
                    "https://private.example/v3/index.json"),
                PackageSourceAssociation.Create());

        Assert.Throws<ArgumentException>(() =>
            new GitHubNuGetAdvisoryRequest(
                privateSource.Source.Producer,
                [At("Example.Client", "1.0.0")]));
    }

    private static PackageSourceCoordinate At(string packageId, string version) =>
        PackageSourceCoordinate.Create(packageId, version);

    private static GitHubNuGetAdvisoryRequest Request(
        params PackageSourceCoordinate[] coordinates) =>
        new(PackageProducerIdentity.NuGetOrg, coordinates);

    private static string AdvisoryPage(
        string packageId,
        string range,
        string? fixedVersion)
    {
        string fixedValue = fixedVersion is null
            ? "null"
            : $"\"{fixedVersion}\"";
        return $$"""
        [
          {
            "ghsa_id": "GHSA-aaaa-bbbb-cccc",
            "cve_id": "CVE-2026-1234",
            "type": "reviewed",
            "severity": "high",
            "published_at": "2026-09-09T16:04:11Z",
            "updated_at": "2026-09-09T18:00:00Z",
            "withdrawn_at": null,
            "vulnerabilities": [
              {
                "package": {
                  "ecosystem": "nuget",
                  "name": "{{packageId}}"
                },
                "vulnerable_version_range": "{{range}}",
                "first_patched_version": {{fixedValue}}
              }
            ]
          }
        ]
        """;
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() =>
            Interlocked.Increment(ref _timestamp);
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, bytes.Length - _position);
            if (count == 0)
                return ValueTask.FromResult(0);

            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> route)
        : HttpMessageHandler
    {
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);
            HttpResponseMessage response = route(request, call);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The deadline did not cancel the request.");
        }
    }
}
