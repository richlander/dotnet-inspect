#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Services;

if (args.Length != 4
    || !int.TryParse(args[3], out int trials) || trials < 1)
{
    Console.Error.WriteLine(
        "Usage: dotnet run tools/PackageMetadataBenchmark.cs --"
        + " <revision-label> <id@version,...> <n,...> <trials>");
    return 1;
}

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string revision = args[0];
var coordinates = args[1].Split(',').Select(value =>
{
    string[] parts = value.Split('@');
    if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        throw new ArgumentException("Expected explicit id@version coordinates.");
    return (Id: parts[0], Version: parts[1]);
}).ToArray();
int[] counts = args[2].Split(',').Select(int.Parse).ToArray();
if (counts.Any(n => n < 1 || n > coordinates.Length))
    throw new ArgumentException("Each n must select a nonempty prefix of the coordinate list.");

const string Source = "https://api.nuget.org/v3/index.json";
var sourceOptions = new NuGetSourceOptions { Sources = [Source] };
DirectoryInfo cacheRoot = Directory.CreateTempSubdirectory("inspect-metadata-benchmark-");
using var capability = NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData);
Console.WriteLine(
    "timestampUtc\trevision\tos\tframework\ttrial\tcache\tn\treturned\tcompletion"
    + "\tfirstMs\tnthMs\tlastMs\tterminalMs\trequests\tdecodedBodyBytes\tprojectionSha256");
int failures = 0;
try
{
    for (int trial = 1; trial <= trials; trial++)
    {
        foreach (int n in counts)
        {
            CoreCache.Initialize(
                "inspect-metadata-benchmark",
                Path.Combine(cacheRoot.FullName, $"{trial}-{n}"));
            using var handler = new MeasuringHandler(
                HttpClientFactory.CreateCredentialFreeHandler());
            using var client = new HttpClient(handler)
            {
                Timeout = HttpClientFactoryOptions.BaselineTimeout,
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-inspect-metadata-benchmark");

            foreach (string cache in new[] { "cold-client", "warm-metadata", "warm-transport-refresh" })
            {
                int requestsBefore = handler.Requests;
                long bytesBefore = handler.Bytes;
                int returned = 0;
                double? first = null;
                double? last = null;
                var projection = new StringBuilder();
                string completion = "complete";
                long started = Stopwatch.GetTimestamp();
                foreach (var coordinate in coordinates.Take(n))
                {
                    PackageMetadata metadata = await PackageMetadataService.FetchAllMetadataAsync(
                        client, coordinate.Id, coordinate.Version,
                        message => Console.Error.WriteLine(message),
                        forceLatest: cache == "warm-transport-refresh",
                        sourceOptions: sourceOptions,
                        untrustedClient: client);
                    if (metadata.Published is null || metadata.Listed is null
                        || !metadata.DeprecationMetadataAvailable)
                    {
                        completion = "incomplete-metadata";
                        failures++;
                        break;
                    }

                    // Observe the production service result, not an HTTP response or UI paint.
                    last = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    first ??= last;
                    returned++;
                    projection.AppendLine(
                        $"{coordinate.Id}@{coordinate.Version}|{metadata.Published:O}"
                        + $"|{metadata.Listed}|{metadata.DeprecationMetadataAvailable}"
                        + $"|{metadata.Deprecation?.Summary}");
                }
                double terminal = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Console.WriteLine(string.Join('\t',
                    DateTimeOffset.UtcNow.ToString("O"), revision,
                    RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
                    trial, cache, n, returned, completion,
                    Format(first), Format(returned == n ? last : null), Format(last),
                    Format(terminal), handler.Requests - requestsBefore,
                    handler.Bytes - bytesBefore,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projection.ToString())))));
            }
        }
    }
}
finally
{
    // Only this invocation's private scratch cache is removed.
    Directory.Delete(cacheRoot.FullName, recursive: true);
}
return failures == 0 ? 0 : 1;

static string Format(double? value) => value?.ToString("F3") ?? "NA";

sealed class MeasuringHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public int Requests { get; private set; }
    public long Bytes { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests++;
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        response.Content = new MeasuringContent(response.Content, count => Bytes += count);
        return response;
    }
}

sealed class MeasuringContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly Action<int> _count;

    public MeasuringContent(HttpContent inner, Action<int> count)
    {
        _inner = inner;
        _count = count;
        foreach (var header in inner.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    protected override Task<Stream> CreateContentReadStreamAsync() =>
        CreateContentReadStreamAsync(CancellationToken.None);

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        new MeasuringStream(await _inner.ReadAsStreamAsync(cancellationToken), _count);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using Stream measured = await CreateContentReadStreamAsync(cancellationToken);
        await measured.CopyToAsync(stream, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _inner.Headers.ContentLength ?? 0;
        return _inner.Headers.ContentLength.HasValue;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}

sealed class MeasuringStream(Stream inner, Action<int> count) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int length)
    {
        int read = inner.Read(buffer, offset, length);
        count(read);
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        count(read);
        return read;
    }
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken);
        count(read);
        return read;
    }
    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int length, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, length), cancellationToken).AsTask();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int length) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
