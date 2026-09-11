using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MsdlProxy;

/// <summary>
/// Fetches a PDB from MSDL server-side (where CORS does not apply) and
/// returns it through the Static Web App's same-origin managed API. The MSDL
/// host is a compile-time constant; only the two validated path segments are
/// ever taken from the request.
/// </summary>
internal static class MsdlClient
{
    public const string Name = "msdl";

    private const string MsdlHost = "https://msdl.microsoft.com";

    // The browser host independently rejects portable PDBs above 8 MiB.
    // Enforcing that same cap here bounds managed-Function buffering and
    // prevents spending proxy bandwidth on content the only consumer rejects.
    private const long MaxSymbolBytes = 8L * 1024 * 1024;

    public static async Task<IActionResult> ProxySymbolAsync(
        HttpClient client,
        string pdbFileName,
        string symbolKey,
        CancellationToken cancellationToken)
    {
        string pdbSegment = Uri.EscapeDataString(pdbFileName);
        string url =
            $"{MsdlHost}/download/symbols/{pdbSegment}/{symbolKey}/{pdbSegment}";

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new StatusCodeResult(StatusCodes.Status502BadGateway);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            return new NotFoundResult();
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return new StatusCodeResult(StatusCodes.Status502BadGateway);
        }

        if (response.Content.Headers.ContentLength is { } declaredLength
            && declaredLength > MaxSymbolBytes)
        {
            response.Dispose();
            return new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
        }

        var upstream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        // BoundedStream still owns response disposal: even a truthful, small
        // Content-Length header does not guarantee the body matches it, so
        // this is the actual enforcement point rather than the header check
        // above (which only lets an obviously-oversized response fail fast).
        var bounded = new BoundedStream(upstream, response, MaxSymbolBytes);
        return new FileStreamResult(bounded, "application/octet-stream");
    }

    /// <summary>
    /// Wraps an upstream response stream, disposing the owning
    /// <see cref="HttpResponseMessage"/> when the stream is disposed, and
    /// throwing if more than <paramref name="maxBytes"/> is ever read --
    /// the enforcement backstop behind the declared Content-Length check.
    /// </summary>
    private sealed class BoundedStream(Stream inner, HttpResponseMessage response, long maxBytes) : Stream
    {
        private long _totalRead;

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
            => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .ConfigureAwait(false);
            _totalRead += read;
            if (_totalRead > maxBytes)
                throw new InvalidOperationException("MSDL response exceeded the configured size limit.");
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
