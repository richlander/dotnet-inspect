namespace ILInspector.Decompiler;

internal sealed record CSharpAnnotatedSourceProjection(
    AnnotatedSourceDocument Document,
    IReadOnlyDictionary<int, int> NodeIds)
{
    public static CSharpAnnotatedSourceProjection Create(AnnotatedSourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lines = SplitLines(source.Text);
        var ilLines = new HashSet<int>();
        foreach (var node in source.Nodes.Where(static node => node.Medium == SourceLineKind.Il))
        {
            if (node.Spans.Count != 1)
                throw new ArgumentException($"IL node {node.Id} is not one contiguous rendered line.", nameof(source));

            var span = node.Spans[0];
            int lineIndex = lines.FindIndex(line =>
                span.Start == line.Start
                && span.Length == line.ContentLength);
            if (lineIndex < 0)
            {
                throw new ArgumentException(
                    $"IL node {node.Id} does not cover one exact rendered line.",
                    nameof(source));
            }
            ilLines.Add(lineIndex);
        }

        var segments = new List<ProjectedSegment>(lines.Count - ilLines.Count);
        int projectedStart = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            if (ilLines.Contains(index))
                continue;

            var line = lines[index];
            segments.Add(new ProjectedSegment(
                line.Start,
                line.TotalLength,
                projectedStart));
            projectedStart += line.TotalLength;
        }

        string text = string.Concat(segments.Select(segment =>
            source.Text.Substring(segment.SourceStart, segment.Length)));
        var nodes = new List<AnnotatedSourceNode>();
        var nodeIds = new Dictionary<int, int>();
        foreach (var node in source.Nodes)
        {
            if (node.Medium != SourceLineKind.CSharp)
                continue;

            var spans = ProjectSpans(node.Spans, segments);
            if (spans.Count == 0)
            {
                throw new ArgumentException(
                    $"C# node {node.Id} has no characters after removing IL lines.",
                    nameof(source));
            }

            int id = nodes.Count;
            nodeIds.Add(node.Id, id);
            nodes.Add(new AnnotatedSourceNode(
                id,
                node.Kind,
                SourceLineKind.CSharp,
                spans,
                Provenance: node.Provenance));
        }

        var regions = new List<AnnotatedSourceRegion>();
        foreach (var region in source.Regions)
        {
            var spans = ProjectSpans(region.Spans, segments);
            if (spans.Count > 0)
                regions.Add(new AnnotatedSourceRegion(region.Role, spans));
        }

        return new CSharpAnnotatedSourceProjection(
            new AnnotatedSourceDocument(
                text,
                nodes,
                regions,
                Facts: [],
                Targets: [],
                source.Source),
            nodeIds);
    }

    static IReadOnlyList<AnnotatedSourceSpan> ProjectSpans(
        IReadOnlyList<AnnotatedSourceSpan> spans,
        IReadOnlyList<ProjectedSegment> segments)
    {
        var projected = new List<AnnotatedSourceSpan>();
        foreach (var span in spans)
        {
            int spanEnd = span.Start + span.Length;
            foreach (var segment in segments)
            {
                int segmentEnd = segment.SourceStart + segment.Length;
                int start = Math.Max(span.Start, segment.SourceStart);
                int end = Math.Min(spanEnd, segmentEnd);
                if (end <= start)
                    continue;

                int projectedSpanStart =
                    segment.ProjectedStart + start - segment.SourceStart;
                int length = end - start;
                if (projected.Count > 0
                    && projected[^1].Start + projected[^1].Length == projectedSpanStart)
                {
                    var previous = projected[^1];
                    projected[^1] = previous with { Length = previous.Length + length };
                }
                else
                {
                    projected.Add(new AnnotatedSourceSpan(projectedSpanStart, length));
                }
            }
        }
        return projected;
    }

    static List<SourceLineSegment> SplitLines(string text)
    {
        var lines = new List<SourceLineSegment>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\r' && text[index] != '\n')
                continue;

            int contentLength = index - start;
            int terminatorLength = text[index] == '\r'
                && index + 1 < text.Length
                && text[index + 1] == '\n'
                    ? 2
                    : 1;
            lines.Add(new SourceLineSegment(start, contentLength, contentLength + terminatorLength));
            index += terminatorLength - 1;
            start = index + 1;
        }

        lines.Add(new SourceLineSegment(start, text.Length - start, text.Length - start));
        return lines;
    }

    readonly record struct SourceLineSegment(
        int Start,
        int ContentLength,
        int TotalLength);

    readonly record struct ProjectedSegment(
        int SourceStart,
        int Length,
        int ProjectedStart);
}
