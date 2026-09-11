using System.Net;
using DotnetInspector.Networking;
using NuGetFetch;

namespace DotnetInspector.Packages;

internal static class FeedFailureRecorder
{
    public static void Record(string url, HttpStatusCode? status) =>
        NuGetFetch.FeedFailureTelemetry.Record(
            url,
            status,
            ToFeedFailurePhase(NetworkTelemetry.CurrentTrafficKind));

    public static void Record(Uri url, HttpStatusCode? status) =>
        NuGetFetch.FeedFailureTelemetry.Record(
            url,
            status,
            ToFeedFailurePhase(NetworkTelemetry.CurrentTrafficKind));

    private static FeedFailurePhase ToFeedFailurePhase(
        NetworkTrafficKind trafficKind) =>
        trafficKind switch
        {
            NetworkTrafficKind.PackageDownload =>
                FeedFailurePhase.PackageDownload,
            NetworkTrafficKind.PackageManifest =>
                FeedFailurePhase.PackageManifest,
            NetworkTrafficKind.PackageMetadata =>
                FeedFailurePhase.PackageMetadata,
            NetworkTrafficKind.PackageSearch =>
                FeedFailurePhase.PackageSearch,
            NetworkTrafficKind.PackageSourceDiscovery =>
                FeedFailurePhase.PackageSourceDiscovery,
            NetworkTrafficKind.PackageVersionList =>
                FeedFailurePhase.PackageVersionList,
            _ => FeedFailurePhase.Unknown
        };
}
