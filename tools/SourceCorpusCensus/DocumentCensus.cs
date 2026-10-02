using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

using ILInspector.SourceLink;

namespace DotnetInspector.SourceCorpusCensus;

sealed record CorpusRow(
    string? SourceUrl,
    string? ChecksumAlgorithm,
    string? Checksum);

readonly record struct DocumentKey(
    string Url,
    string Algorithm,
    string Checksum);

sealed record LineMeasure(
    int ContentUtf16,
    int ContentUtf8,
    int RowJsonUtf8,
    int RowUtf16);

sealed record DocumentMeasure(
    string Url,
    SourceChecksumVerification Verification,
    int RawBytes,
    int DecodedUtf16,
    int LineCount,
    int MaximumLineUtf16,
    int MaximumLineJsonUtf8,
    LineMeasure[] Lines);

sealed class SourceSizeLimitException(string message) : Exception(message);

static class DocumentCensus
{
    const int SchemaVersion = 1;
    const int MaximumSourceBytes = 64 * 1024 * 1024;

    public static async Task<int> RunAsync(IReadOnlyList<string> corpusPaths)
    {
        var documents = new HashSet<DocumentKey>();
        int rowCount = 0;
        foreach (string candidate in corpusPaths)
        {
            string corpusPath = Path.GetFullPath(candidate);
            foreach (string line in File.ReadLines(corpusPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                CorpusRow row = JsonSerializer.Deserialize(
                    line,
                    CensusJsonContext.Default.CorpusRow)
                    ?? throw new InvalidDataException(
                        $"A row in '{corpusPath}' decoded as null.");
                rowCount++;
                if (row.SourceUrl is not { Length: > 0 }
                    || row.ChecksumAlgorithm is not { Length: > 0 }
                    || row.Checksum is not { Length: > 0 })
                {
                    throw new InvalidDataException(
                        $"Corpus row {rowCount} lacks a SourceLink identity.");
                }
                if (!Uri.TryCreate(
                        row.SourceUrl,
                        UriKind.Absolute,
                        out Uri? uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidDataException(
                        $"Corpus row {rowCount} does not name an HTTPS SourceLink URL.");
                }

                documents.Add(
                    new DocumentKey(
                        row.SourceUrl,
                        row.ChecksumAlgorithm,
                        row.Checksum));
            }
        }

        DocumentKey[] orderedDocuments =
        [
            .. documents.OrderBy(
                static document => document.Url,
                StringComparer.Ordinal),
        ];
        Console.Error.WriteLine(
            $"Rows {rowCount:n0}; unique documents "
            + $"{orderedDocuments.Length:n0}.");

        using var client = new HttpClient
        {
            MaxResponseContentBufferSize = MaximumSourceBytes,
            Timeout = TimeSpan.FromMinutes(2),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "dotnet-inspect-source-corpus-census/1.0");
        var measurements = new ConcurrentBag<DocumentMeasure>();
        var failures = new ConcurrentBag<DocumentFailure>();
        using var gate = new SemaphoreSlim(8);
        int completed = 0;

        await Task.WhenAll(orderedDocuments.Select(async document =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                byte[] content = await FetchAsync(
                    client,
                    document.Url).ConfigureAwait(false);
                byte[] checksum;
                try
                {
                    checksum = Convert.FromHexString(document.Checksum);
                }
                catch (FormatException exception)
                {
                    failures.Add(
                        new(
                            document.Url,
                            "invalid-checksum",
                            $"Invalid checksum: {exception.Message}"));
                    return;
                }

                VerifiedSourceTextResult verified =
                    SourceLinkService.VerifySourceContent(
                        document.Algorithm,
                        checksum,
                        content);
                if (!verified.IsVerified || verified.Text is null)
                {
                    failures.Add(
                        new(
                            document.Url,
                            "verification",
                            $"{verified.ChecksumVerification}: "
                                + verified.Failure));
                    return;
                }

                measurements.Add(
                    Measure(
                        document.Url,
                        content.Length,
                        verified.Text,
                        verified.ChecksumVerification));
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or IOException
                    or TaskCanceledException
                    or SourceSizeLimitException)
            {
                failures.Add(
                    new(
                        document.Url,
                        exception is SourceSizeLimitException
                            ? "size-limit"
                            : "retrieval",
                        $"{exception.GetType().Name}: {exception.Message}"));
            }
            finally
            {
                int count = Interlocked.Increment(ref completed);
                if (count % 100 == 0 || count == orderedDocuments.Length)
                {
                    Console.Error.WriteLine(
                        $"Fetched {count:n0}/{orderedDocuments.Length:n0}.");
                }
                gate.Release();
            }
        })).ConfigureAwait(false);

        DocumentMeasure[] measured =
        [
            .. measurements.OrderBy(
                static document => document.Url,
                StringComparer.Ordinal),
        ];
        DocumentFailure[] orderedFailures =
        [
            .. failures.OrderBy(
                static failure => failure.Url,
                StringComparer.Ordinal),
        ];
        DocumentCorpusReport report = BuildReport(
            rowCount,
            orderedDocuments.Length,
            measured,
            orderedFailures);
        CensusOutput.Write(report);
        return orderedFailures.Length == 0 ? 0 : 1;
    }

    internal static DocumentMeasure Measure(
        string url,
        int rawBytes,
        string text,
        SourceChecksumVerification verification)
    {
        var lines = new List<LineMeasure>();
        int lineStart = 0;
        for (int index = 0; index <= text.Length; index++)
        {
            int terminatorLength = index < text.Length
                ? TerminatorLength(text, index)
                : 0;
            if (index < text.Length && terminatorLength == 0)
                continue;

            string rowText = text[lineStart..(index + terminatorLength)];
            string content = text[lineStart..index];
            lines.Add(
                new(
                    content.Length,
                    Encoding.UTF8.GetByteCount(content),
                    JsonEncodedText.Encode(rowText).EncodedUtf8Bytes.Length,
                    rowText.Length));
            if (index == text.Length)
                break;

            index += terminatorLength - 1;
            lineStart = index + 1;
        }

        return new(
            url,
            verification,
            rawBytes,
            text.Length,
            lines.Count,
            lines.Max(static line => line.ContentUtf16),
            lines.Max(static line => line.RowJsonUtf8),
            [.. lines]);
    }

    internal static SegmentCandidate Segment(
        IReadOnlyList<DocumentMeasure> documents,
        int maximumLines,
        int maximumJsonUtf8,
        int maximumUtf16)
    {
        var segmentsPerDocument = new List<int>(documents.Count);
        int oversizeLines = 0;
        int documentsWithOversizeLines = 0;
        foreach (DocumentMeasure document in documents)
        {
            int segments = 0;
            int currentLines = 0;
            int currentJson = 0;
            int currentUtf16 = 0;
            bool documentOversize = false;
            foreach (LineMeasure line in document.Lines)
            {
                bool oversize =
                    line.RowJsonUtf8 > maximumJsonUtf8
                    || line.RowUtf16 > maximumUtf16;
                if (oversize)
                {
                    oversizeLines++;
                    documentOversize = true;
                }

                if (currentLines > 0
                    && (currentLines == maximumLines
                        || currentJson + line.RowJsonUtf8 > maximumJsonUtf8
                        || currentUtf16 + line.RowUtf16 > maximumUtf16))
                {
                    segments++;
                    currentLines = 0;
                    currentJson = 0;
                    currentUtf16 = 0;
                }

                currentLines++;
                currentJson += line.RowJsonUtf8;
                currentUtf16 += line.RowUtf16;
            }
            if (currentLines > 0)
                segments++;
            if (documentOversize)
                documentsWithOversizeLines++;
            segmentsPerDocument.Add(segments);
        }

        return new(
            maximumLines,
            maximumJsonUtf8,
            maximumUtf16,
            segmentsPerDocument.Sum(),
            segmentsPerDocument.Count(static count => count > 1),
            Statistics.Metric(segmentsPerDocument),
            oversizeLines,
            documentsWithOversizeLines);
    }

    static DocumentCorpusReport BuildReport(
        int rowCount,
        int uniqueDocumentCount,
        DocumentMeasure[] documents,
        DocumentFailure[] failures)
    {
        int[] lineUtf16 = documents
            .SelectMany(static document => document.Lines)
            .Select(static line => line.ContentUtf16)
            .ToArray();
        int[] lineUtf8 = documents
            .SelectMany(static document => document.Lines)
            .Select(static line => line.ContentUtf8)
            .ToArray();
        int[] lineJsonUtf8 = documents
            .SelectMany(static document => document.Lines)
            .Select(static line => line.RowJsonUtf8)
            .ToArray();

        return new(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            rowCount,
            uniqueDocumentCount,
            documents.Length,
            documents.Count(static document =>
                document.Verification == SourceChecksumVerification.Exact),
            documents.Count(static document =>
                document.Verification
                    == SourceChecksumVerification.LineEndingNormalized),
            documents.Sum(static document => document.LineCount),
            failures,
            Statistics.Metric(documents.Select(static document =>
                document.RawBytes)),
            Statistics.Metric(documents.Select(static document =>
                document.DecodedUtf16)),
            Statistics.Metric(documents.Select(static document =>
                document.LineCount)),
            Statistics.Metric(documents.Select(static document =>
                document.MaximumLineUtf16)),
            Statistics.Metric(lineUtf16),
            Statistics.Metric(lineUtf8),
            Statistics.Metric(lineJsonUtf8),
            Thresholds(lineUtf16),
            Thresholds(lineJsonUtf8),
            [
                Segment(documents, 256, 16 * 1024, 8 * 1024),
                Segment(documents, 256, 32 * 1024, 16 * 1024),
                Segment(documents, 256, 64 * 1024, 32 * 1024),
                Segment(documents, 256, 128 * 1024, 64 * 1024),
            ],
            [
                .. documents
                    .OrderByDescending(static document => document.RawBytes)
                    .ThenBy(
                        static document => document.Url,
                        StringComparer.Ordinal)
                    .Take(20)
                    .Select(Example),
            ],
            [
                .. documents
                    .OrderByDescending(static document =>
                        document.MaximumLineJsonUtf8)
                    .ThenBy(
                        static document => document.Url,
                        StringComparer.Ordinal)
                    .Take(20)
                    .Select(Example),
            ]);
    }

    static DocumentExample Example(DocumentMeasure document) =>
        new(
            document.Url,
            document.RawBytes,
            document.LineCount,
            document.MaximumLineUtf16,
            document.MaximumLineJsonUtf8);

    static ThresholdCount[] Thresholds(int[] values)
    {
        int[] thresholds = [256, 1024, 4096, 8192, 16384, 32768, 65536];
        return
        [
            .. thresholds.Select(threshold =>
                new ThresholdCount(
                    threshold,
                    values.Count(value => value > threshold))),
        ];
    }

    static int TerminatorLength(string text, int index) =>
        text[index] switch
        {
            '\r' when index + 1 < text.Length
                && text[index + 1] == '\n' => 2,
            '\r' or '\n' or '\u0085' or '\u2028' or '\u2029' => 1,
            _ => 0,
        };

    static async Task<byte[]> FetchAsync(HttpClient client, string url)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromMinutes(2));
                using HttpResponseMessage response = await client.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength
                    is > MaximumSourceBytes)
                {
                    throw new SourceSizeLimitException(
                        $"Source exceeds the {MaximumSourceBytes:n0}-byte limit.");
                }

                await using Stream source = await response.Content
                    .ReadAsStreamAsync(timeout.Token)
                    .ConfigureAwait(false);
                return await ReadBoundedAsync(
                    source,
                    MaximumSourceBytes,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                attempt < 3
                && exception is HttpRequestException
                    or IOException
                    or TaskCanceledException)
            {
                last = exception;
                await Task.Delay(
                    TimeSpan.FromMilliseconds(250 * attempt))
                    .ConfigureAwait(false);
            }
        }

        throw last
            ?? new InvalidOperationException("Source fetch did not run.");
    }

    internal static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);

        using var destination = new MemoryStream(
            Math.Min(maximumBytes, 64 * 1024));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                long remaining = maximumBytes - destination.Length;
                int requested = (int)Math.Min(
                    buffer.Length,
                    remaining + 1);
                int read = await source.ReadAsync(
                    buffer.AsMemory(0, requested),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return destination.ToArray();
                if (read > remaining)
                {
                    throw new SourceSizeLimitException(
                        $"Source exceeds the {maximumBytes:n0}-byte limit.");
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
