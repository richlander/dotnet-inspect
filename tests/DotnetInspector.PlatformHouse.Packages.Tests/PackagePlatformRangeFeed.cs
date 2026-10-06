using System.Net;
using System.Net.Http.Headers;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

internal sealed class PackagePlatformRangeFeed : HttpMessageHandler
{
    private readonly IReadOnlyDictionary<string, byte[]> _packages;
    private int _fullRequests;
    private int _rangedRequests;

    internal PackagePlatformRangeFeed(
        string packageId,
        string version,
        byte[] archive)
        : this([(packageId, version, archive)])
    {
    }

    internal PackagePlatformRangeFeed(
        params (string PackageId, string Version, byte[] Archive)[] packages)
    {
        _packages = packages.ToDictionary(
            static package =>
                $"https://globalcdn.nuget.org/packages/"
                + $"{package.PackageId}.{package.Version}.nupkg",
            static package => package.Archive,
            StringComparer.OrdinalIgnoreCase);
    }

    internal int FullRequests => Volatile.Read(ref _fullRequests);
    internal int RangedRequests => Volatile.Read(ref _rangedRequests);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_packages.TryGetValue(
                request.RequestUri!.AbsoluteUri,
                out byte[]? archive))
        {
            return Task.FromResult(
                Response(
                    HttpStatusCode.NotFound,
                    new ByteArrayContent([])));
        }

        RangeItemHeaderValue? range =
            request.Headers.Range?.Ranges.SingleOrDefault();
        if (range is null)
        {
            Interlocked.Increment(ref _fullRequests);
            return Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    new ByteArrayContent(archive)));
        }

        long start;
        long end;
        if (range.From is null)
        {
            long suffix = Math.Min(range.To!.Value, archive.LongLength);
            start = archive.LongLength - suffix;
            end = archive.LongLength - 1;
        }
        else
        {
            start = range.From.Value;
            end = Math.Min(
                range.To ?? archive.LongLength - 1,
                archive.LongLength - 1);
        }

        Interlocked.Increment(ref _rangedRequests);
        var content = new ByteArrayContent(
            archive,
            checked((int)start),
            checked((int)(end - start + 1)));
        content.Headers.ContentRange =
            new ContentRangeHeaderValue(
                start,
                end,
                archive.LongLength);
        return Task.FromResult(
            Response(HttpStatusCode.PartialContent, content));
    }

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        HttpContent content) =>
        new(status)
        {
            Content = content,
        };
}
