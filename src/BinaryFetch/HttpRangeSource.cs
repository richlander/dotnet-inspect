using System.Net;
using System.Net.Http.Headers;

namespace BinaryFetch;

/// <summary>
/// A random-access source over one HTTP representation using <c>Range</c>
/// requests. It owns the representation-level rules: a <c>200</c> to a ranged
/// request is <see cref="RangeFetchFailure.RangeIgnored"/>; every request after
/// the first carries <c>If-Range</c> with the validator the first response
/// supplied, and a changed representation is
/// <see cref="RangeFetchFailure.RepresentationChanged"/>; a response whose
/// visible headers disagree with its body, or with what earlier responses
/// established, is <see cref="RangeFetchFailure.InvalidResponse"/>.
/// </summary>
/// <remarks>
/// <para>
/// When <c>Content-Range</c> is not visible (a browser reading from a CDN that
/// does not expose it), the source validates by status and body length and
/// learns the total only through <see cref="ConfirmLength"/>: a tail body
/// shorter than requested is then the whole representation and must equal the
/// confirmed length.
/// </para>
/// <para>
/// The consumer supplies the client and a request-preparation callback for
/// credentials or host-specific request options; this type adds no identity
/// of its own. Transport exceptions the client raises propagate unwrapped.
/// </para>
/// </remarks>
public sealed class HttpRangeSource : RandomAccessSource
{
    private readonly RangeRequestSender _send;
    private readonly Uri _uri;

    private EntityTagHeaderValue? _entityTag;
    private DateTimeOffset? _lastModified;
    private bool _validatorObserved;
    private int? _shortTailLength;

    /// <summary>
    /// Creates a source whose every ranged request goes through
    /// <paramref name="send"/>. The consumer applies credentials, host
    /// request options, retry, and per-request deadlines inside that
    /// delegate; the source calls it once per request and passes its
    /// exceptions through unchanged.
    /// </summary>
    public HttpRangeSource(Uri uri, RangeRequestSender send)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(send);
        _uri = uri;
        _send = send;
    }

    /// <summary>Creates a source that sends each request directly with <paramref name="client"/>.</summary>
    public HttpRangeSource(HttpClient client, Uri uri)
        : this(
            uri,
            (request, cancellationToken) => client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(client);
    }

    /// <summary>Whether the first response supplied a validator usable for <c>If-Range</c>.</summary>
    public bool HasValidator => _entityTag is not null || _lastModified is not null;

    public override async ValueTask<ReadOnlyMemory<byte>> ReadTailAsync(
        int maxLength,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        using HttpRequestMessage request = CreateRequest(
            new RangeHeaderValue(null, maxLength));
        using HttpResponseMessage response = await _send(request, cancellationToken)
            .ConfigureAwait(false);
        RequirePartialContent(response, firstRequest: !_validatorObserved);
        byte[] body = await ReadBodyAsync(response, maxLength, cancellationToken)
            .ConfigureAwait(false);
        RequireContentLength(response, body.Length);
        CaptureValidator(response);

        ContentRangeHeaderValue? contentRange = response.Content.Headers.ContentRange;
        if (contentRange is not null)
        {
            RequireByteUnit(contentRange);
            if (contentRange.Length is { } total)
            {
                ConfirmObservedTotal(total);
                long expectedFrom = Math.Max(0, total - maxLength);
                if (contentRange.From != expectedFrom
                    || contentRange.To != total - 1
                    || body.Length != total - expectedFrom)
                {
                    throw Invalid("The tail response does not carry the requested range.");
                }
            }
            else if (contentRange.From is { } from && contentRange.To is { } to)
            {
                if (to - from + 1 != body.Length)
                    throw Invalid("The tail response does not carry the requested range.");
            }
        }
        else if (body.Length < maxLength)
        {
            // Without a visible Content-Range, a short tail is the whole
            // representation; the consumer's derived length must agree.
            _shortTailLength = body.Length;
        }

        return body;
    }

    public override async ValueTask ReadRangeAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (destination.Length == 0)
            return;
        if (Length is { } known && offset + destination.Length > known)
        {
            throw Invalid("The requested range lies past the end of the representation.");
        }

        long last = offset + destination.Length - 1;
        using HttpRequestMessage request = CreateRequest(
            new RangeHeaderValue(offset, last));
        bool conditional = ApplyIfRange(request);
        using HttpResponseMessage response = await _send(request, cancellationToken)
            .ConfigureAwait(false);
        RequirePartialContent(response, firstRequest: !conditional && !_validatorObserved);
        byte[] body = await ReadBodyAsync(response, destination.Length, cancellationToken)
            .ConfigureAwait(false);
        RequireContentLength(response, body.Length);
        if (body.Length != destination.Length)
            throw Invalid("The range response does not carry the requested length.");

        ContentRangeHeaderValue? contentRange = response.Content.Headers.ContentRange;
        if (contentRange is not null)
        {
            RequireByteUnit(contentRange);
            if (contentRange.From != offset || contentRange.To != last)
                throw Invalid("The range response does not carry the requested range.");
            if (contentRange.Length is { } total)
            {
                if (Length is { } established && established != total)
                {
                    throw new RangeFetchException(
                        RangeFetchFailure.RepresentationChanged,
                        "The representation's length changed between requests.");
                }

                Length ??= total;
            }
        }

        RequireSameValidator(response);
        body.CopyTo(destination);
    }

    public override void ConfirmLength(long length)
    {
        if (_shortTailLength is { } shortTail && shortTail != length)
        {
            throw Invalid(
                "The tail response was shorter than requested but is not the whole representation.");
        }

        base.ConfirmLength(length);
    }

    private HttpRequestMessage CreateRequest(RangeHeaderValue range)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Range = range;
        return request;
    }

    private bool ApplyIfRange(HttpRequestMessage request)
    {
        if (_entityTag is not null)
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(_entityTag);
            return true;
        }

        if (_lastModified is { } lastModified)
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(lastModified);
            return true;
        }

        return false;
    }

    private static void RequirePartialContent(HttpResponseMessage response, bool firstRequest)
    {
        if (response.StatusCode == HttpStatusCode.PartialContent)
            return;
        if (response.StatusCode == HttpStatusCode.OK)
        {
            throw new RangeFetchException(
                firstRequest
                    ? RangeFetchFailure.RangeIgnored
                    : RangeFetchFailure.RepresentationChanged,
                firstRequest
                    ? "The source answered a ranged request with the whole representation."
                    : "The source answered a conditional ranged request with the whole representation.",
                response.StatusCode);
        }

        throw new RangeFetchException(
            RangeFetchFailure.Transport,
            $"The source answered a ranged request with status {(int)response.StatusCode}.",
            response.StatusCode);
    }

    private static async Task<byte[]> ReadBodyAsync(
        HttpResponseMessage response,
        int maxLength,
        CancellationToken cancellationToken)
    {
        using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var buffer = new byte[maxLength];
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(filled),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            filled += read;
        }

        if (filled == buffer.Length)
        {
            var probe = new byte[1];
            int extra = await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false);
            if (extra != 0)
                throw Invalid("The range response carries more bytes than requested.");
        }

        return filled == buffer.Length ? buffer : buffer[..filled];
    }

    private static void RequireContentLength(HttpResponseMessage response, int bodyLength)
    {
        if (response.Content.Headers.ContentLength is { } contentLength
            && contentLength != bodyLength)
        {
            throw Invalid("The response's Content-Length does not match its body.");
        }
    }

    private static void RequireByteUnit(ContentRangeHeaderValue contentRange)
    {
        if (!string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase))
            throw Invalid("The range response uses a unit other than bytes.");
    }

    private void ConfirmObservedTotal(long total)
    {
        if (Length is { } established && established != total)
            throw Invalid("The response's total length disagrees with an earlier response.");
        Length = total;
    }

    private void CaptureValidator(HttpResponseMessage response)
    {
        if (_validatorObserved)
        {
            RequireSameValidator(response);
            return;
        }

        _validatorObserved = true;
        EntityTagHeaderValue? entityTag = response.Headers.ETag;
        if (entityTag is { IsWeak: false })
            _entityTag = entityTag;
        else if (response.Content.Headers.LastModified is { } lastModified)
            _lastModified = lastModified;
    }

    private void RequireSameValidator(HttpResponseMessage response)
    {
        if (_entityTag is not null)
        {
            EntityTagHeaderValue? entityTag = response.Headers.ETag;
            if (entityTag is not null && !_entityTag.Equals(entityTag))
            {
                throw new RangeFetchException(
                    RangeFetchFailure.RepresentationChanged,
                    "The representation's entity tag changed between requests.");
            }
        }
        else if (_lastModified is { } lastModified)
        {
            DateTimeOffset? observed = response.Content.Headers.LastModified;
            if (observed is not null && observed != lastModified)
            {
                throw new RangeFetchException(
                    RangeFetchFailure.RepresentationChanged,
                    "The representation's modification time changed between requests.");
            }
        }
    }

    private static RangeFetchException Invalid(string message) =>
        new(RangeFetchFailure.InvalidResponse, message);
}
