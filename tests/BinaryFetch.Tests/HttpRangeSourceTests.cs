using System.Net;
using System.Net.Http.Headers;

namespace BinaryFetch.Tests;

public sealed class HttpRangeSourceTests
{
    private static byte[] Representation(int length)
    {
        var bytes = new byte[length];
        for (int index = 0; index < length; index++)
            bytes[index] = (byte)(index * 31 + 7);
        return bytes;
    }

    private static HttpRangeSource Source(RangeHandler handler) =>
        new(new HttpClient(handler), new Uri("https://range.example/archive.bin"));

    [Fact]
    public async Task TailRead_WithVisibleHeaders_ReturnsTheSuffixAndTheTotal()
    {
        byte[] representation = Representation(100_000);
        var handler = new RangeHandler(representation) { ETag = "\"v1\"" };
        await using HttpRangeSource source = Source(handler);

        ReadOnlyMemory<byte> tail = await source.ReadTailAsync(1_000, TestContext.Current.CancellationToken);

        Assert.Equal(representation[^1_000..], tail.ToArray());
        Assert.Equal(100_000, source.Length);
        Assert.True(source.HasValidator);
        Assert.Equal("bytes=-1000", handler.Requests[0].Headers.Range!.ToString());
    }

    [Fact]
    public async Task TailRead_ShorterRepresentation_ReturnsTheWholeAndTheTotal()
    {
        byte[] representation = Representation(300);
        var handler = new RangeHandler(representation);
        await using HttpRangeSource source = Source(handler);

        ReadOnlyMemory<byte> tail = await source.ReadTailAsync(1_000, TestContext.Current.CancellationToken);

        Assert.Equal(representation, tail.ToArray());
        Assert.Equal(300, source.Length);
        source.ConfirmLength(300);
    }

    [Fact]
    public async Task TailRead_HiddenHeaders_ShortBody_IsTheWholeOnlyWhenTheConsumerConfirmsIt()
    {
        byte[] representation = Representation(300);
        var handler = new RangeHandler(representation) { HideContentRange = true };
        await using HttpRangeSource source = Source(handler);

        ReadOnlyMemory<byte> tail = await source.ReadTailAsync(1_000, TestContext.Current.CancellationToken);

        Assert.Equal(300, tail.Length);
        Assert.Null(source.Length);
        RangeFetchException refused = Assert.Throws<RangeFetchException>(() => source.ConfirmLength(301));
        Assert.Equal(RangeFetchFailure.InvalidResponse, refused.Failure);
        source.ConfirmLength(300);
        Assert.Equal(300, source.Length);
    }

    [Fact]
    public async Task TailRead_HiddenHeaders_FullBody_LeavesTheLengthToTheConsumer()
    {
        byte[] representation = Representation(100_000);
        var handler = new RangeHandler(representation) { HideContentRange = true };
        await using HttpRangeSource source = Source(handler);

        ReadOnlyMemory<byte> tail = await source.ReadTailAsync(1_000, TestContext.Current.CancellationToken);

        Assert.Equal(representation[^1_000..], tail.ToArray());
        Assert.Null(source.Length);
        source.ConfirmLength(100_000);
        var slice = new byte[10];
        await source.ReadRangeAsync(500, slice, TestContext.Current.CancellationToken);
        Assert.Equal(representation[500..510], slice);
    }

    [Fact]
    public async Task RangeRead_ReturnsExactlyTheRequestedBytes_AndSendsIfRange()
    {
        byte[] representation = Representation(50_000);
        var handler = new RangeHandler(representation) { ETag = "\"v1\"" };
        await using HttpRangeSource source = Source(handler);
        await source.ReadTailAsync(100, TestContext.Current.CancellationToken);

        var slice = new byte[1_234];
        await source.ReadRangeAsync(10_000, slice, TestContext.Current.CancellationToken);

        Assert.Equal(representation[10_000..11_234], slice);
        Assert.Equal("bytes=10000-11233", handler.Requests[1].Headers.Range!.ToString());
        Assert.Equal("\"v1\"", handler.Requests[1].Headers.IfRange!.EntityTag!.ToString());
    }

    [Fact]
    public async Task RangeIgnored_WhenTheSourceAnswersTheFirstRequestWith200()
    {
        var handler = new RangeHandler(Representation(5_000)) { IgnoreRange = true };
        await using HttpRangeSource source = Source(handler);

        RangeFetchException refused = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadTailAsync(100, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(RangeFetchFailure.RangeIgnored, refused.Failure);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
    }

    [Fact]
    public async Task RepresentationChanged_WhenAConditionalRequestGets200_OrTheValidatorChanges()
    {
        byte[] representation = Representation(50_000);
        var handler = new RangeHandler(representation) { ETag = "\"v1\"" };
        await using HttpRangeSource source = Source(handler);
        await source.ReadTailAsync(100, TestContext.Current.CancellationToken);

        handler.IgnoreRange = true;
        RangeFetchException replaced = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadRangeAsync(0, new byte[10], TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(RangeFetchFailure.RepresentationChanged, replaced.Failure);

        handler.IgnoreRange = false;
        handler.ETag = "\"v2\"";
        RangeFetchException retagged = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadRangeAsync(0, new byte[10], TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(RangeFetchFailure.RepresentationChanged, retagged.Failure);
    }

    [Fact]
    public async Task RepresentationChanged_WhenTheTotalDiffersOnALaterRequest()
    {
        var handler = new RangeHandler(Representation(50_000));
        await using HttpRangeSource source = Source(handler);
        await source.ReadTailAsync(100, TestContext.Current.CancellationToken);

        handler.ReportedTotal = 60_000;
        RangeFetchException changed = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadRangeAsync(0, new byte[10], TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(RangeFetchFailure.RepresentationChanged, changed.Failure);
    }

    [Theory]
    [InlineData("content-length")]
    [InlineData("range")]
    [InlineData("long-body")]
    public async Task InvalidResponse_WhenTheResponseDoesNotDescribeItsBytes(string defect)
    {
        var handler = new RangeHandler(Representation(50_000)) { Defect = defect };
        await using HttpRangeSource source = Source(handler);

        RangeFetchException refused = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadTailAsync(100, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(RangeFetchFailure.InvalidResponse, refused.Failure);
    }

    [Fact]
    public async Task Transport_CarriesTheStatus_AndClientExceptionsPassThrough()
    {
        var handler = new RangeHandler(Representation(10)) { Status = HttpStatusCode.NotFound };
        await using HttpRangeSource source = Source(handler);
        RangeFetchException missing = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadTailAsync(100, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(RangeFetchFailure.Transport, missing.Failure);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var throwing = new HttpRangeSource(
            new Uri("https://range.example/archive.bin"),
            (_, _) => throw new HttpRequestException("connection reset"));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => throwing.ReadTailAsync(100, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task SendDelegate_IsCalledOncePerRequest()
    {
        byte[] representation = Representation(20_000);
        var handler = new RangeHandler(representation);
        var client = new HttpClient(handler);
        int sends = 0;
        await using var source = new HttpRangeSource(
            new Uri("https://range.example/archive.bin"),
            (request, token) =>
            {
                sends++;
                return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            });

        await source.ReadTailAsync(100, TestContext.Current.CancellationToken);
        await source.ReadRangeAsync(0, new byte[50], TestContext.Current.CancellationToken);
        await source.ReadRangeAsync(0, Memory<byte>.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(2, sends);
    }

    [Fact]
    public async Task HiddenHeaders_FullTail_RefusesADerivedLengthShorterThanTheBytesServed()
    {
        var handler = new RangeHandler(Representation(100_000)) { HideContentRange = true };
        await using HttpRangeSource source = Source(handler);
        await source.ReadTailAsync(1_000, TestContext.Current.CancellationToken);

        // Prepended garbage would make the record's derived total smaller
        // than the tail already served; that is not a shorter representation.
        RangeFetchException refused = Assert.Throws<RangeFetchException>(() => source.ConfirmLength(117));
        Assert.Equal(RangeFetchFailure.InvalidResponse, refused.Failure);
    }

    [Fact]
    public async Task WithoutAValidator_ALater200_IsRangeIgnored_NotRepresentationChanged()
    {
        var handler = new RangeHandler(Representation(50_000)); // no ETag, no Last-Modified
        await using HttpRangeSource source = Source(handler);
        await source.ReadTailAsync(100, TestContext.Current.CancellationToken);
        Assert.False(source.HasValidator);

        handler.IgnoreRange = true;
        RangeFetchException ignored = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadRangeAsync(0, new byte[10], TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(RangeFetchFailure.RangeIgnored, ignored.Failure);
        Assert.Null(handler.Requests[1].Headers.IfRange);
    }

    [Fact]
    public async Task ARangeReadAsTheFirstRequest_CapturesTheValidator()
    {
        var handler = new RangeHandler(Representation(50_000)) { ETag = "\"v1\"" };
        await using HttpRangeSource source = Source(handler);

        await source.ReadRangeAsync(0, new byte[10], TestContext.Current.CancellationToken);
        Assert.True(source.HasValidator);
        await source.ReadRangeAsync(10, new byte[10], TestContext.Current.CancellationToken);

        Assert.Null(handler.Requests[0].Headers.IfRange);
        Assert.Equal("\"v1\"", handler.Requests[1].Headers.IfRange!.EntityTag!.ToString());
    }

    [Fact]
    public async Task TailWithAnUnknownTotal_ShortBody_IsTheWholeOnlyWhenConfirmed()
    {
        var handler = new RangeHandler(Representation(300)) { UnknownTotal = true };
        await using HttpRangeSource source = Source(handler);

        ReadOnlyMemory<byte> tail = await source.ReadTailAsync(1_000, TestContext.Current.CancellationToken);

        Assert.Equal(300, tail.Length);
        Assert.Null(source.Length);
        Assert.Throws<RangeFetchException>(() => source.ConfirmLength(400));
        source.ConfirmLength(300);
    }

    [Fact]
    public async Task StreamSource_ServesTailAndRanges_AndRefusesReadsPastTheEnd()
    {
        byte[] representation = Representation(4_000);
        await using var source = new StreamRandomAccessSource(new MemoryStream(representation));

        Assert.Equal(4_000, source.Length);
        ReadOnlyMemory<byte> tail = await source.ReadTailAsync(10_000, TestContext.Current.CancellationToken);
        Assert.Equal(representation, tail.ToArray());
        var slice = new byte[100];
        await source.ReadRangeAsync(3_900, slice, TestContext.Current.CancellationToken);
        Assert.Equal(representation[3_900..], slice);

        RangeFetchException past = await Assert.ThrowsAsync<RangeFetchException>(
            () => source.ReadRangeAsync(3_950, new byte[100], TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(RangeFetchFailure.InvalidResponse, past.Failure);
        Assert.Throws<RangeFetchException>(() => source.ConfirmLength(4_001));
    }

    /// <summary>
    /// A source serving one representation by range, with switches for the
    /// ways a real source can deviate: ignoring the range, hiding
    /// <c>Content-Range</c>, changing its validator or total, or describing
    /// its bytes wrongly.
    /// </summary>
    internal sealed class RangeHandler(byte[] representation) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public bool IgnoreRange { get; set; }
        public bool HideContentRange { get; set; }
        public bool UnknownTotal { get; set; }
        public string? ETag { get; set; }
        public long? ReportedTotal { get; set; }
        public string? Defect { get; set; }
        public HttpStatusCode? Status { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Status is { } status)
                return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent([]) });

            long total = ReportedTotal ?? representation.Length;
            RangeItemHeaderValue? range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (IgnoreRange || range is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(representation),
                });
            }

            long from;
            long to;
            if (range.From is null)
            {
                long suffix = range.To!.Value;
                from = Math.Max(0, representation.Length - suffix);
                to = representation.Length - 1;
            }
            else
            {
                from = range.From.Value;
                to = Math.Min(range.To ?? representation.Length - 1, representation.Length - 1);
            }

            byte[] slice = representation[(int)from..(int)(to + 1)];
            if (Defect == "long-body")
                slice = [.. slice, 0xAA];
            var content = new ByteArrayContent(slice);
            if (Defect == "content-length")
                content.Headers.ContentLength = slice.Length + 1;
            if (!HideContentRange)
            {
                content.Headers.ContentRange = Defect == "range"
                    ? new ContentRangeHeaderValue(from + 1, to + 1, total)
                    : UnknownTotal
                        ? new ContentRangeHeaderValue(from, to)
                        : new ContentRangeHeaderValue(from, to, total);
            }

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            if (ETag is { } etag)
                response.Headers.ETag = new EntityTagHeaderValue(etag);
            return Task.FromResult(response);
        }
    }
}
