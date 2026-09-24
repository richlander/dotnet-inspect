using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed partial class PackageSourceClientTests
{
    private const string GallerySearch =
        "https://azuresearch-usnc.nuget.org/query";
    private const string GalleryVersions =
        "https://globalcdn.nuget.org/v3-flatcontainer/contoso/index.json";
    private const string GalleryManifest =
        "https://globalcdn.nuget.org/v3-flatcontainer/contoso/1.0.0/contoso.nuspec";
    private const string GalleryRegistration =
        "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/index.json";
    private const string GalleryRegistrationPage =
        "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/2.0.0.json";
    private const string GalleryPackage =
        "https://globalcdn.nuget.org/packages/contoso.1.0.0.nupkg";
    private const string GallerySymbols =
        "https://globalcdn.nuget.org/symbol-packages/contoso.1.0.0.snupkg";
    private const string ServiceIndex =
        "https://feed.example/v3/index.json";
    private const string SearchEndpoint =
        "https://feed.example/v3/query";
    private const string SearchRequest =
        SearchEndpoint
        + "?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
    private const string FlatContainer =
        "https://feed.example/v3/flat/";
    private const string NuGetOrgVersions =
        "https://api.nuget.org/v3-flatcontainer/contoso/index.json";
    private const string Versions =
        "https://feed.example/v3/flat/contoso/index.json";
    private const string Package =
        "https://feed.example/v3/flat/contoso/1.0.0/contoso.1.0.0.nupkg";
    private const string Manifest =
        "https://feed.example/v3/flat/contoso/1.0.0/contoso.nuspec";

    private static class PackageSourceClientFactory
    {
        public static IPackageSourceClient Create(
            PackageSource source,
            NuGetFetchOptions? options = null) =>
            NuGetFetch.PackageSourceClientFactory.Create(
                source,
                PackageSourceAssociation.Create(),
                options);

        public static IPackageSourceClient Create(
            PackageSource source,
            HttpMessageHandler transport,
            NuGetFetchOptions? options = null) =>
            NuGetFetch.PackageSourceClientFactory.Create(
                source,
                PackageSourceAssociation.Create(),
                transport,
                options);

        public static IPackageSourceClient Create(
            PackageSourceDescriptor descriptor,
            NuGetFetchOptions? options = null,
            PackageSourceCredential? credential = null) =>
            NuGetFetch.PackageSourceClientFactory.Create(
                descriptor,
                PackageSourceAssociation.Create(),
                options,
                credential);

        public static IPackageSourceClient Create(
            PackageSourceDescriptor descriptor,
            HttpMessageHandler transport,
            NuGetFetchOptions? options = null,
            PackageSourceCredential? credential = null) =>
            NuGetFetch.PackageSourceClientFactory.Create(
                descriptor,
                PackageSourceAssociation.Create(),
                transport,
                options,
                credential);

        public static IPackageSourceClient CreateGallery(
            NuGetFetchOptions? options = null) =>
            NuGetFetch.PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                options);

        public static IPackageSourceClient CreateGallery(
            HttpMessageHandler transport,
            NuGetFetchOptions? options = null) =>
            NuGetFetch.PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                transport,
                options);

        public static HttpClientHandler CreateGalleryTransportHandler(
            bool isBrowser) =>
            NuGetFetch.PackageSourceClientFactory
                .CreateGalleryTransportHandler(isBrowser);

        public static HttpMessageHandler CreateV3TransportHandler(
            Uri source,
            bool isBrowser) =>
            NuGetFetch.PackageSourceClientFactory
                .CreateV3TransportHandler(source, isBrowser);

        public static HttpClientHandler
            CreateCredentialFreeTransportHandler(bool isBrowser) =>
            NuGetFetch.PackageSourceClientFactory
                .CreateCredentialFreeTransportHandler(isBrowser);
    }

    private static string? DecodeBasic(string? parameter) =>
        parameter is null
            ? null
            : Encoding.UTF8.GetString(
                Convert.FromBase64String(parameter));

    private static async Task<string> ReadAsync(Stream stream)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        return await reader.ReadToEndAsync(
            TestContext.Current.CancellationToken);
    }

    private static T Succeeded<T>(
        PackageSourceOperationResult<T> result)
        where T : class
    {
        Assert.Null(result.Failure);
        return Assert.IsType<T>(result.Value);
    }

    private static PackageSourceFailure Failed<T>(
        PackageSourceOperationResult<T> result)
        where T : class
    {
        Assert.Null(result.Value);
        return Assert.IsType<PackageSourceFailure>(result.Failure);
    }

    private static PackageSourceResultFactory CreateResultFactory(
        PackageSourceDescriptor? descriptor = null,
        PackageSourceAssociation? association = null)
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            NuGetFetch.PackageSourceClientFactory.CreateCustom(
                descriptor ?? PackageSourceDescriptor.NuGetGallery,
                association ?? PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyPackageSourceClient(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private sealed class FactoryOnlyPackageSourceClient(
        PackageSourceResultIdentity source,
        PackageSourceCapabilities capabilities =
            PackageSourceCapabilities.None)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities { get; } =
            capabilities;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private static TaskCanceledException CreateCanceledTransportTimeout() =>
        new(
            "Simulated canceled transport timeout from "
            + "https://secret.example/package.",
            new TimeoutException("Simulated transport timeout."),
            CancellationToken.None);

    private sealed class DelayedList<T>(T value) : IReadOnlyList<T>
    {
        public int Count => 1;

        public T this[int index]
        {
            get
            {
                Assert.Equal(0, index);
                Thread.Sleep(100);
                return value;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            yield return this[0];
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static NuGetOperationDeadline CreateRegistrationParserOperation(
        CancellationToken cancellationToken) =>
        new(
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(5),
            },
            Timeout.InfiniteTimeSpan,
            cancellationToken);

    private static string RegistrationItems(int count) =>
        string.Join(
            ",",
            Enumerable.Range(1, count)
                .Select(version =>
                    $$"""
                      {
                        "catalogEntry": {
                          "version": "{{version}}.0.0"
                        }
                      }
                      """));

    private static async Task DeserializeRegistrationItemsAsync(
        string items,
        bool inline,
        IReadOnlySet<string> candidates,
        NuGetGalleryRegistrationBudget budget,
        NuGetOperationDeadline operation,
        CancellationToken cancellationToken)
    {
        string json = inline
            ? $$"""{"items":[{"items":[{{items}}]}]}"""
            : $$"""{"items":[{{items}}]}""";
        using var stream =
            new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (inline)
        {
            await NuGetGalleryRegistration.DeserializeIndexAsync(
                stream,
                candidates,
                budget,
                operation,
                cancellationToken);
        }
        else
        {
            await NuGetGalleryRegistration.DeserializePageAsync(
                stream,
                candidates,
                budget,
                operation,
                cancellationToken);
        }
    }

    private sealed class InterruptingReadOnlySet
        : HashSet<string>, IReadOnlySet<string>
    {
        private readonly Action _interrupt;
        private int _containsCalls;

        public InterruptingReadOnlySet(int count, Action interrupt)
            : base(
                Enumerable.Range(1, count)
                    .Select(version => $"{version}.0.0"),
                StringComparer.OrdinalIgnoreCase)
        {
            _interrupt = interrupt;
        }

        public int ContainsCalls => _containsCalls;

        bool IReadOnlySet<string>.Contains(string item)
        {
            if (Interlocked.Increment(ref _containsCalls) == 1)
                _interrupt();
            return Contains(item);
        }
    }

    private sealed class RedirectRecordingHandler(
        HttpStatusCode redirectStatus,
        string redirectTarget)
        : HttpMessageHandler
    {
        public List<string?> Authorization { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Authorization.Add(
                DecodeBasic(
                    request.Headers.Authorization?.Parameter));
            if (Authorization.Count == 1)
            {
                var redirect = new HttpResponseMessage(
                    redirectStatus);
                redirect.Headers.Location =
                    new Uri(redirectTarget);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class RedirectChainHandler(int redirects)
        : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_requests++ < redirects)
            {
                var redirect = new HttpResponseMessage(
                    HttpStatusCode.Found);
                redirect.Headers.Location =
                    new Uri($"/redirect-{_requests}", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class GalleryRedirectHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            if (Requests == 1)
            {
                var redirect =
                    new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location =
                    new Uri("/redirected", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3]),
                });
        }
    }

    private sealed class RawRedirectHandler(string location)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            var redirect = new HttpResponseMessage(
                HttpStatusCode.Found);
            redirect.Headers.TryAddWithoutValidation(
                "Location",
                location);
            return Task.FromResult(redirect);
        }
    }

    private static async Task<string> ServeHttpResponseAsync(
        TcpListener listener,
        string body,
        CancellationToken cancellationToken)
    {
        using TcpClient connection =
            await listener.AcceptTcpClientAsync(cancellationToken);
        await using NetworkStream stream = connection.GetStream();
        var request = new byte[4096];
        int requestLength = 0;
        while (requestLength < request.Length)
        {
            int read = await stream.ReadAsync(
                request.AsMemory(requestLength),
                cancellationToken);
            if (read == 0)
                break;

            requestLength += read;
            if (request.AsSpan(0, requestLength).IndexOf(
                    "\r\n\r\n"u8) >= 0)
            {
                break;
            }
        }

        string requestText =
            Encoding.ASCII.GetString(request, 0, requestLength);
        string requestLine = requestText.Split(
            "\r\n",
            StringSplitOptions.None)[0];
        byte[] content = Encoding.UTF8.GetBytes(body);
        byte[] headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Content-Type: application/json\r\n"
            + $"Content-Length: {content.Length}\r\n"
            + "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
        return requestLine;
    }

    private static async Task<IReadOnlyList<string>> ServeHttpResponsesAsync(
        TcpListener listener,
        IReadOnlyList<string> bodies,
        CancellationToken cancellationToken)
    {
        var requestLines = new List<string>(bodies.Count);
        foreach (string body in bodies)
        {
            requestLines.Add(
                await ServeHttpResponseAsync(
                    listener,
                    body,
                    cancellationToken));
        }

        return requestLines;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HttpStatusCode> _statuses =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            Func<HttpRequestMessage, HttpResponseMessage>> _responses =
            new(StringComparer.OrdinalIgnoreCase);

        public string this[string url] { set => _routes[url] = value; }

        public void SetStatus(string url, HttpStatusCode statusCode) =>
            _statuses[url] = statusCode;

        public void SetResponse(
            string url,
            Func<HttpRequestMessage, HttpResponseMessage> response) =>
            _responses[url] = response;

        public List<string> Requested { get; } = [];
        public List<string?> Authentication { get; } = [];
        public List<IReadOnlyDictionary<string, string[]>> Headers { get; } =
            [];
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            // Ranged batch reads send concurrently; recording and scripted
            // responses stay serialized so their order and counts hold.
            lock (Requested)
            {
                Requested.Add(url);
                Authentication.Add(
                    request.Headers.Authorization?.Parameter);
                Headers.Add(
                    request.Headers.ToDictionary(
                        header => header.Key,
                        header => header.Value.ToArray(),
                        StringComparer.OrdinalIgnoreCase));
                if (_responses.TryGetValue(
                        url,
                        out Func<HttpRequestMessage, HttpResponseMessage>? response))
                {
                    return Task.FromResult(response(request));
                }
            }

            string? route = _routes.Keys.FirstOrDefault(
                candidate => url.Equals(
                        candidate,
                        StringComparison.OrdinalIgnoreCase)
                    || candidate == GallerySearch
                        && url.StartsWith(
                            GallerySearch + "?",
                            StringComparison.OrdinalIgnoreCase));
            bool hasResponse = route is not null;
            HttpStatusCode status = _statuses.GetValueOrDefault(
                url,
                hasResponse
                    ? HttpStatusCode.OK
                    : HttpStatusCode.NotFound);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    hasResponse
                        ? _routes.GetValueOrDefault(route!)!
                        : ""),
                RequestMessage = request,
            });
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StallingHandler : HttpMessageHandler
    {
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CanceledSearchTransportTimeoutHandler
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == ServiceIndex)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""
                        {
                          "resources": [
                            {
                              "@id": "{{SearchEndpoint}}",
                              "@type": "SearchQueryService/3.5.0"
                            }
                          ]
                        }
                        """),
                };
            }

            await Task.Yield();
            throw CreateCanceledTransportTimeout();
        }
    }

    private sealed class StallingMetadataBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(
                        new StallingMetadataBodyStream()),
                });
    }

    private sealed class StallingMetadataBodyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingPayloadStream(Action? beforeThrow = null)
        : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            beforeThrow?.Invoke();
            throw new IOException(
                "Transport failed at https://secret.example/package.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            beforeThrow?.Invoke();
            return ValueTask.FromException<int>(
                new IOException(
                    "Transport failed at https://secret.example/package."));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class CanceledTimeoutPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw CreateCanceledTransportTimeout();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                CreateCanceledTransportTimeout());

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class CanceledTimeoutDisposePayloadStream
        : MemoryStream
    {
        public CanceledTimeoutDisposePayloadStream()
            : base([1], writable: false)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw CreateCanceledTransportTimeout();
        }
    }

    private sealed class LateObjectDisposedPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw new ObjectDisposedException(
                "https://secret.example/package");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class InvalidDataPayloadStream(bool delay) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw CreateFailure();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (delay)
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw CreateFailure();
        }

        private static InvalidDataException CreateFailure() =>
            new(
                "Simulated invalid payload from "
                + "https://secret.example/package.");

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class StallingPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingDisposePayloadStream
        : MemoryStream
    {
        public ThrowingDisposePayloadStream()
            : base([1], writable: false)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw new IOException(
                "Cleanup failed for https://secret.example/package.");
        }
    }

    private sealed class ThrowingDisposeStallingPayloadStream
        : Stream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await _disposed.Task;
            throw new ObjectDisposedException(
                nameof(ThrowingDisposeStallingPayloadStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.TrySetResult();
            base.Dispose(disposing);
            throw new IOException(
                "Cleanup failed for https://secret.example/package.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class DisposalUnblocksPayloadStream : Stream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _disposed.Task;
            throw new ObjectDisposedException(
                nameof(DisposalUnblocksPayloadStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class DisposalReturnsEofPayloadStream : Stream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            ReadStarted.TrySetResult();
            _disposed.Task.GetAwaiter().GetResult();
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _disposed.Task;
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ObjectDisposedPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new ObjectDisposedException(
                "https://secret.example/package");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new ObjectDisposedException(
                    "https://secret.example/package"));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingEofStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ConcurrentRegistrationHandler : HttpMessageHandler
    {
        private const int ExpectedBatchSize = 8;
        private readonly TaskCompletionSource _pageRequestsMayComplete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activePageRequests;
        private int _maxActivePageRequests;
        private int _pageRequests;

        public int MaxActivePageRequests => _maxActivePageRequests;
        public int PageRequests => _pageRequests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == GalleryVersions)
            {
                return Response(
                    """{"versions":["1.0.0","2.0.0","3.0.0","4.0.0","5.0.0","6.0.0","7.0.0","8.0.0","9.0.0"]}""");
            }

            if (url == GalleryRegistration)
            {
                string pages = string.Join(
                    ",",
                    Enumerable.Range(1, 9).Select(version =>
                        $$"""
                          {
                            "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/{{version}}.0.0/{{version}}.0.0.json"
                          }
                          """));
                return Response($$"""{"items":[{{pages}}]}""");
            }

            int active = Interlocked.Increment(
                ref _activePageRequests);
            UpdateMaximum(active);
            Interlocked.Increment(ref _pageRequests);
            if (active == 1)
            {
                _ = ReleasePageRequestsAsync(
                    TimeSpan.FromMilliseconds(200));
            }

            if (active == ExpectedBatchSize)
            {
                _ = ReleasePageRequestsAsync(
                    TimeSpan.FromMilliseconds(50));
            }

            try
            {
                await _pageRequestsMayComplete.Task.WaitAsync(
                    cancellationToken);
                string version =
                    request.RequestUri.Segments[^2].TrimEnd('/');
                return Response(
                    $$"""
                      {
                        "items": [
                          {
                            "catalogEntry": {
                              "version": "{{version}}"
                            }
                          }
                        ]
                      }
                      """);
            }
            finally
            {
                Interlocked.Decrement(ref _activePageRequests);
            }
        }

        private async Task ReleasePageRequestsAsync(TimeSpan delay)
        {
            await Task.Delay(delay);
            _pageRequestsMayComplete.TrySetResult();
        }

        private void UpdateMaximum(int active)
        {
            int observed;
            do
            {
                observed = _maxActivePageRequests;
                if (observed >= active)
                    return;
            }
            while (Interlocked.CompareExchange(
                ref _maxActivePageRequests,
                active,
                observed) != observed);
        }

        private static HttpResponseMessage Response(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
    }

    private sealed class LateMalformedRegistrationHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent content =
                request.RequestUri!.AbsoluteUri == GalleryVersions
                    ? new StringContent(
                        """{"versions":["1.0.0"]}""")
                    : new StreamContent(
                        new LateMalformedRegistrationStream());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
        }
    }

    private sealed class LateMalformedRegistrationStream : Stream
    {
        private bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_sent)
                return 0;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            buffer.Span[0] = (byte)'{';
            _sent = true;
            return 1;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class LateInvalidDataRegistrationHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent content =
                request.RequestUri!.AbsoluteUri == GalleryVersions
                    ? new StringContent(
                        """{"versions":["1.0.0"]}""")
                    : new StreamContent(
                        new LateInvalidDataRegistrationStream());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
        }
    }

    private sealed class LateInvalidDataRegistrationStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw new InvalidDataException(
                "Simulated late invalid registration data.");
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class LateStreamingTimeoutHandler(
        bool canceledTransportTimeout) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            if (canceledTransportTimeout)
                throw CreateCanceledTransportTimeout();

            throw new TimeoutException(
                "Simulated late streaming transport timeout.");
        }
    }

    private sealed class CancelableRegistrationHandler : HttpMessageHandler
    {
        public TaskCompletionSource RegistrationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == GalleryVersions)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content =
                        new StringContent("""{"versions":["1.0.0"]}"""),
                };
            }

            RegistrationStarted.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The registration stall completed without cancellation.");
        }
    }

    private sealed class FaultAndCancelRegistrationHandler
        : HttpMessageHandler
    {
        private int _pageRequests;

        public TaskCompletionSource BothPagesStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFault { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == GalleryVersions)
            {
                return Response(
                    """{"versions":["1.0.0","2.0.0"]}""");
            }

            if (url == GalleryRegistration)
            {
                return Response(
                    """
                    {
                      "items": [
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
                        },
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/2.0.0/2.0.0.json"
                        }
                      ]
                    }
                    """);
            }

            int page = Interlocked.Increment(ref _pageRequests);
            if (page == 2)
                BothPagesStarted.TrySetResult();
            if (page == 1)
            {
                await ReleaseFault.Task;
                throw new JsonException(
                    "Simulated registration page failure.");
            }

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The registration stall completed without cancellation.");
        }

        private static HttpResponseMessage Response(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
    }

    private sealed class FaultAndTimeoutRegistrationHandler(
        bool transportTimeout = false,
        bool canceledTransportTimeout = false)
        : HttpMessageHandler
    {
        public int FastTransportRequests;
        public int StallingRequests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == GalleryVersions)
            {
                return Response(
                    """{"versions":["1.0.0","2.0.0"]}""");
            }

            if (url == GalleryRegistration)
            {
                return Response(
                    """
                    {
                      "items": [
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
                        },
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/2.0.0/2.0.0.json"
                        }
                      ]
                    }
                    """);
            }

            if (url.EndsWith(
                    "/page/1.0.0/1.0.0.json",
                    StringComparison.Ordinal))
            {
                Interlocked.Increment(ref FastTransportRequests);
                throw new HttpRequestException(
                    "Simulated registration transport failure.");
            }

            Interlocked.Increment(ref StallingRequests);
            if (canceledTransportTimeout)
                throw CreateCanceledTransportTimeout();

            if (transportTimeout)
            {
                throw new TimeoutException(
                    "Simulated registration transport timeout.");
            }

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The registration stall completed without cancellation.");
        }

        private static HttpResponseMessage Response(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
    }

    private sealed class TransientGalleryHandler(
        bool statuslessFailure = false) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public int PrimaryRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            string url = request.RequestUri!.AbsoluteUri;
            if (url.StartsWith(
                    "https://globalcdn.nuget.org/v3/registration5-gz-semver2/",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (PrimaryRequests++ == 0)
            {
                if (statuslessFailure)
                {
                    throw new HttpRequestException(
                        "Browser fetch failed.",
                        new InvalidOperationException(
                            "JavaScript transport failure."));
                }

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.BadGateway));
            }

            HttpContent content = url.StartsWith(
                    GallerySearch,
                    StringComparison.Ordinal)
                ? new StringContent(
                    """{"data":[{"id":"Contoso","version":"1.0.0"}]}""")
                : url.Equals(GalleryVersions, StringComparison.Ordinal)
                    ? new StringContent(
                        """{"versions":["1.0.0"]}""")
                    : url.Equals(GalleryManifest, StringComparison.Ordinal)
                        ? new StringContent("<package />")
                        : new ByteArrayContent("package bytes"u8.ToArray());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
        }
    }

    private sealed class ImmediateReadFailureStream(Exception failure)
        : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw failure;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(failure);

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ReadThenFailureStream(byte[] content) : Stream
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < content.Length)
            {
                int copied = Math.Min(count, content.Length - _position);
                content.AsSpan(_position, copied).CopyTo(
                    buffer.AsSpan(offset, copied));
                _position += copied;
                return copied;
            }

            throw new IOException("The response body ended.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return ValueTask.FromResult(
                    Read(buffer.Span));
            }
            catch (Exception exception)
            {
                return ValueTask.FromException<int>(exception);
            }
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position < content.Length)
            {
                int copied = Math.Min(
                    buffer.Length,
                    content.Length - _position);
                content.AsSpan(_position, copied).CopyTo(buffer);
                _position += copied;
                return copied;
            }

            throw new IOException("The response body ended.");
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class LateEofStream(
        byte[] content,
        TimeSpan eofDelay) : Stream
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

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < content.Length)
            {
                int copied = Math.Min(
                    buffer.Length,
                    content.Length - _position);
                content.AsMemory(_position, copied).CopyTo(buffer);
                _position += copied;
                return copied;
            }

            await Task.Delay(eofDelay).ConfigureAwait(false);
            return 0;
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class CleanupFailureContent(
        byte[] content,
        bool responseCleanup) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await stream.WriteAsync(content);
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(
                responseCleanup
                    ? new MemoryStream(content, writable: false)
                    : new AsyncDisposeFailureStream(content));

        protected override bool TryComputeLength(out long length)
        {
            length = content.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && responseCleanup)
            {
                throw new IOException(
                    "The response cleanup failed.");
            }
        }
    }

    private sealed class AsyncDisposeFailureStream(byte[] content)
        : MemoryStream(content, writable: false)
    {
        public override ValueTask DisposeAsync() =>
            ValueTask.FromException(
                new IOException("The body cleanup failed."));
    }

    private sealed class LateOversizeStream(byte[] content) : Stream
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

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position == content.Length)
                return 0;
            if (_position == 0)
            {
                while (!cancellationToken.IsCancellationRequested)
                    await Task.Yield();
            }

            int count = Math.Min(
                content.Length - _position,
                buffer.Length);
            content.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            return count;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
