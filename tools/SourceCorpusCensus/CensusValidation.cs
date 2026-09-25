using ILInspector.SourceLink;

namespace DotnetInspector.SourceCorpusCensus;

static class CensusValidation
{
    public static async Task<int> RunAsync()
    {
        const string mixed =
            "A\r\n😀B\rC\nD\u0085E\u2028F\u2029G";
        DocumentMeasure measured = DocumentCensus.Measure(
            "https://example.test/mixed.cs",
            rawBytes: 0,
            mixed,
            SourceChecksumVerification.Exact);
        Require(measured.LineCount == 7, "mixed line count");
        Require(measured.DecodedUtf16 == 16, "UTF-16 length");
        Require(measured.MaximumLineUtf16 == 3, "surrogate-pair line length");

        DocumentMeasure finalEmpty = DocumentCensus.Measure(
            "https://example.test/final-empty.cs",
            rawBytes: 0,
            "A\n",
            SourceChecksumVerification.Exact);
        Require(finalEmpty.LineCount == 2, "final empty line");

        DocumentMeasure longLine = DocumentCensus.Measure(
            "https://example.test/long.cs",
            rawBytes: 0,
            new string('x', 9) + "\n",
            SourceChecksumVerification.Exact);
        SegmentCandidate segmented = DocumentCensus.Segment(
            [longLine],
            maximumLines: 256,
            maximumJsonUtf8: 8,
            maximumUtf16: 8);
        Require(segmented.TotalSegments == 2, "segment count");
        Require(segmented.OversizeLines == 1, "oversize line");
        Require(
            segmented.DocumentsWithOversizeLines == 1,
            "oversize document");

        MetricSummary metric = Statistics.Metric([1, 2, 3, 4, 100]);
        Require(metric.P50 == 3, "p50");
        Require(metric.P95 == 100, "p95");
        Require(metric.Max == 100, "maximum");

        byte[] bounded = await DocumentCensus.ReadBoundedAsync(
            new MemoryStream([1, 2, 3, 4]),
            maximumBytes: 4).ConfigureAwait(false);
        Require(
            bounded.AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "bounded source read");

        bool overLimitRejected = false;
        try
        {
            await DocumentCensus.ReadBoundedAsync(
                new MemoryStream([1, 2, 3, 4, 5]),
                maximumBytes: 4).ConfigureAwait(false);
        }
        catch (SourceSizeLimitException)
        {
            overLimitRejected = true;
        }
        Require(overLimitRejected, "over-limit source read");

        Console.Error.WriteLine("Source corpus census validation passed.");
        return 0;
    }

    static void Require(bool condition, string claim)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Source corpus census validation failed: {claim}.");
        }
    }
}
