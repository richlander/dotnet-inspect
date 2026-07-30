using System.Net;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// A source that answers 401 is not the same as a package that does not exist. These tests pin
/// that distinction, which is what issue #3417 bug 1 reported: an unreadable feed was reported
/// as "package not found", so an expired credential looked like a typo in the package name.
/// </summary>
public class FeedFailureTelemetryTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ASourceThatRefusesAccessIsReportedAsUnreadableRatherThanAsAMissingPackage(
        HttpStatusCode status)
    {
        using var scope = FeedFailureTelemetry.Scope();
        using var client = new HttpClient(new FixedStatusHandler(status));

        var body = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://pkgs.dev.azure.com/org/proj/_packaging/feed/nuget/v3/flat2/markout/index.json",
            retryCount: 0,
            cancellationToken: TestContext.Current.CancellationToken,
            trafficKind: NetworkTrafficKind.PackageVersionList);

        Assert.Null(body);

        var described = FeedFailureTelemetry.Current!.DescribeFailure("markout");

        Assert.NotNull(described);
        Assert.DoesNotContain("not found", described, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{(int)status}", described, StringComparison.Ordinal);
        Assert.Contains("listing versions", described, StringComparison.Ordinal);
        Assert.Contains("markout", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// The control that gives the test above its meaning. A recorder that captured every
    /// non-success status would satisfy the assertions above while destroying the real
    /// "package does not exist" message, so 404 must record nothing and leave the caller to
    /// fall back to "not found".
    /// </summary>
    [Fact]
    public async Task ANotFoundIsNotRecorded_SoAGenuinelyAbsentPackageStillReportsAsNotFound()
    {
        using var scope = FeedFailureTelemetry.Scope();
        using var client = new HttpClient(new FixedStatusHandler(HttpStatusCode.NotFound));

        var body = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://api.nuget.org/v3-flatcontainer/no-such-package/index.json",
            retryCount: 0,
            cancellationToken: TestContext.Current.CancellationToken,
            trafficKind: NetworkTrafficKind.PackageVersionList);

        Assert.Null(body);
        Assert.False(FeedFailureTelemetry.Current!.HasFailures);
        Assert.Null(FeedFailureTelemetry.Current!.DescribeFailure("no-such-package"));
    }

    /// <summary>
    /// A 401 on one source while another answers must not poison the successful lookup, so the
    /// collector is only consulted when the overall result is empty. This pins that a recorded
    /// failure is advisory rather than fatal.
    /// </summary>
    [Fact]
    public async Task AFailureIsRecordedPerUrlSoSeveralSourcesEachAppearOnce()
    {
        using var scope = FeedFailureTelemetry.Scope();
        using var client = new HttpClient(new FixedStatusHandler(HttpStatusCode.Unauthorized));

        foreach (var url in new[]
        {
            "https://first.example/v3/index.json",
            "https://second.example/v3/index.json"
        })
        {
            await HttpRetryHelper.GetStringWithRetryAsync(
                client,
                url,
                retryCount: 0,
                cancellationToken: TestContext.Current.CancellationToken,
                trafficKind: NetworkTrafficKind.PackageSourceDiscovery);
        }

        var failures = FeedFailureTelemetry.Current!.Failures;

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, f => f.Url.StartsWith("https://first.", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Url.StartsWith("https://second.", StringComparison.Ordinal));
        Assert.All(failures, f => Assert.Equal(FeedFailureKind.Authentication, f.Kind));
    }

    /// <summary>
    /// The HTTP helpers are called from paths that never open a scope, so recording must be a
    /// no-op there rather than throwing or leaking into a later scope.
    /// </summary>
    [Fact]
    public async Task RecordingOutsideAScopeIsHarmless()
    {
        Assert.Null(FeedFailureTelemetry.Current);

        using var client = new HttpClient(new FixedStatusHandler(HttpStatusCode.Unauthorized));
        var body = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            "https://unscoped.example/v3/index.json",
            retryCount: 0,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(body);
        Assert.Null(FeedFailureTelemetry.Current);
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(string.Empty)
            });
    }
}
