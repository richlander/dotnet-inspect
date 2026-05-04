// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Regression tests for issue #310: a local folder source from NuGet.Config
/// must not crash version resolution / package download with
/// `System.NotSupportedException: net_http_unsupported_requesturi_scheme, file`.
/// </summary>
public class LocalFolderSourceTests : IDisposable
{
    private const string VersionCacheCategory = "versions";

    public LocalFolderSourceTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        CoreCache.Clear(VersionCacheCategory);
    }

    public void Dispose()
    {
        CoreCache.Clear(VersionCacheCategory);
    }

    public static TheoryData<string> LocalFolderUrls() => new()
    {
        @"D:\some\local\packages",
        "/var/local/packages",
        "file:///var/packages",
        @"C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\",
        // Also unparseable / non-absolute should be skipped, not crash:
        "local-packages",
    };

    [Theory]
    [MemberData(nameof(LocalFolderUrls))]
    public async Task GetLatestVersion_LocalFolderSource_DoesNotThrowAndReturnsNull(string folderUrl)
    {
        // FailingClient throws on any HTTP request — proves we never tried to
        // hand the local folder URL to HttpClient.
        using var client = new HttpClient(new FailingHandler());
        var localSource = new NuGetSource("local", folderUrl);

        var result = await PackageExtractor.GetLatestVersionAsync(
            client, "TestPackage", [localSource], log: null);

        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(LocalFolderUrls))]
    public async Task GetVersions_LocalFolderSource_DoesNotThrowAndReturnsNull(string folderUrl)
    {
        using var client = new HttpClient(new FailingHandler());
        var sourceOptions = new NuGetSourceOptions { Sources = [folderUrl] };

        var result = await PackageExtractor.GetVersionsAsync(
            client, "TestPackage", includePrerelease: false, limit: null, log: null,
            sourceOptions: sourceOptions);

        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(LocalFolderUrls))]
    public async Task GetPackageDownloadUrl_LocalFolderSource_DoesNotThrowAndReturnsNull(string folderUrl)
    {
        using var client = new HttpClient(new FailingHandler());
        var localSource = new NuGetSource("local", folderUrl);

        var url = await PackageExtractor.GetPackageDownloadUrlAsync(
            client, localSource, "testpackage", "1.0.0", log: null);

        Assert.Null(url);
    }

    [Fact]
    public async Task GetLatestVersion_FallsBackPastLocalFolderSourceToNuGetOrg()
    {
        // The exact reproducer from issue #310: a local folder source listed
        // before a working HTTP feed must not prevent the HTTP feed from
        // resolving the package.
        var handler = new StubHandler();
        handler.Add(
            "azuresearch-usnc.nuget.org/query",
            """{"data":[{"id":"TestPackage","version":"4.2.1"}]}""");

        using var client = new HttpClient(handler);

        var localSource = new NuGetSource("local", @"D:\some\local\packages");
        var nugetOrg = NuGetSource.NuGetOrg;

        var version = await PackageExtractor.GetLatestVersionAsync(
            client, "TestPackage", [localSource, nugetOrg], log: null, skipCache: true);

        Assert.Equal("4.2.1", version);
    }

    [Fact]
    public async Task GetLatestVersion_LogsSkippedNonHttpSource()
    {
        using var client = new HttpClient(new FailingHandler());
        var localSource = new NuGetSource("MyLocalFeed", @"D:\some\local\packages");
        var logs = new List<string>();

        await PackageExtractor.GetLatestVersionAsync(
            client, "TestPackage", [localSource], log: logs.Add);

        Assert.Contains(logs, l => l.Contains("MyLocalFeed", StringComparison.Ordinal)
            && l.Contains("non-HTTP", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// HTTP handler that throws on any request — proves no network call was attempted.
    /// </summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException($"Unexpected network access in test: {request.RequestUri}");
        }
    }

    /// <summary>
    /// HTTP handler that returns canned JSON when the request URL contains a registered
    /// substring, and 404s otherwise.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<(string match, string body)> _routes = [];

        public void Add(string urlSubstring, string body) => _routes.Add((urlSubstring, body));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            foreach (var (match, body) in _routes)
            {
                if (url.Contains(match, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    });
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
