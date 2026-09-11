using DotnetInspector.Core;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class InfoTrackerNetworkTests
{
    [Fact]
    public async Task CountsPermittedRequestsButNotPolicyRejectedRequests()
    {
        await ConsoleCapture.RunAsync(() =>
        {
            try
            {
                InfoTracker.ResetForTests();
                InfoTracker.Start();

                using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageDownload))
                {
                    NetworkTelemetry.RecordRequestStarting(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            "https://api.nuget.org/v3-flatcontainer/example/index.json"),
                        NetworkClientKinds.Shared);
                }

                using (NetworkTelemetry.Scope(NetworkTrafficKind.VulnerabilityData))
                {
                    NetworkTelemetry.RecordRequestStarting(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            "https://api.nuget.org/v3/vulnerabilities/index.json"),
                        NetworkClientKinds.Shared);
                }

                Assert.Equal(1, InfoTracker.HttpRequests);
            }
            finally
            {
                InfoTracker.ResetForTests();
            }
        });
    }

    [Fact]
    public async Task ResetDisposesNetworkSubscription()
    {
        await ConsoleCapture.RunAsync(() =>
        {
            try
            {
                InfoTracker.ResetForTests();
                InfoTracker.Start();
                InfoTracker.ResetForTests();

                using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageDownload))
                {
                    NetworkTelemetry.RecordRequestStarting(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            "https://api.nuget.org/v3-flatcontainer/example/index.json"),
                        NetworkClientKinds.Shared);
                }

                Assert.Equal(0, InfoTracker.HttpRequests);
            }
            finally
            {
                InfoTracker.ResetForTests();
            }
        });
    }
}
