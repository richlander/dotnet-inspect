using System.Collections.Immutable;
using System.Text;

namespace ILInspector.Decompiler;

/// <summary>
/// The C# plane of one annotated-source document and the explicit identity map
/// from retained source nodes to projected nodes.
/// </summary>
public sealed class CSharpAnnotatedSourceProjection
{
    CSharpAnnotatedSourceProjection(
        AnnotatedSourceDocument document,
        ImmutableDictionary<int, int> originalToProjectedNodeIds)
    {
        Document = document;
        OriginalToProjectedNodeIds = originalToProjectedNodeIds;
    }

    /// <summary>The projected C#-only document.</summary>
    public AnnotatedSourceDocument Document { get; }

    /// <summary>
    /// Maps every retained C# node id in the source document to its renumbered
    /// id in <see cref="Document"/>.
    /// </summary>
    public ImmutableDictionary<int, int> OriginalToProjectedNodeIds { get; }

    /// <summary>Projects one annotated-source document onto its C# plane.</summary>
    /// <exception cref="InvalidOperationException">
    /// IL node coverage does not prove complete-line ownership, or retained C#
    /// structure overlaps text that complete IL ownership removes.
    /// </exception>
    public static CSharpAnnotatedSourceProjection Create(AnnotatedSourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var csharpNodes = source.Nodes
            .Where(static node => node.Medium == SourceLineKind.CSharp)
            .ToArray();
        if (csharpNodes.Length == source.Nodes.Count)
        {
            return new(
                source,
                csharpNodes.ToImmutableDictionary(
                    static node => node.Id,
                    static node => node.Id));
        }

        var lines = SplitLines(source.Text);
        var ilCoverage = lines
            .Select(static _ => new List<AnnotatedSourceSpan>())
            .ToArray();
        foreach (var node in source.Nodes.Where(static node => node.Medium == SourceLineKind.Il))
        {
            foreach (var span in node.Spans)
            {
                int contentCharacters = 0;
                for (int index = 0; index < lines.Count; index++)
                {
                    var line = lines[index];
                    int start = Math.Max(span.Start, line.Start);
                    int end = Math.Min(span.Start + span.Length, line.Start + line.ContentLength);
                    if (end <= start)
                        continue;

                    ilCoverage[index].Add(new(start - line.Start, end - start));
                    contentCharacters += end - start;
                }

                if (contentCharacters != span.Length)
                {
                    throw new InvalidOperationException(
                        $"IL node {node.Id} selects a line terminator or text outside line content; "
                            + "only complete line content can establish IL ownership.");
                }
            }
        }

        var removedLines = new HashSet<int>();
        for (int index = 0; index < ilCoverage.Length; index++)
        {
            if (ilCoverage[index].Count == 0)
                continue;

            var line = lines[index];
            int coveredUntil = 0;
            foreach (var span in ilCoverage[index].OrderBy(static span => span.Start))
            {
                if (span.Start > coveredUntil)
                {
                    throw new InvalidOperationException(
                        $"IL node coverage on line {index} does not cover characters "
                            + $"{coveredUntil} through {span.Start}; partial or mixed line ownership cannot be projected.");
                }

                coveredUntil = Math.Max(coveredUntil, span.Start + span.Length);
            }

            if (coveredUntil != line.ContentLength)
            {
                throw new InvalidOperationException(
                    $"IL node coverage on line {index} ends at character {coveredUntil} "
                        + $"of {line.ContentLength}; partial or mixed line ownership cannot be projected.");
            }

            removedLines.Add(index);
        }

        var segments = new List<ProjectedSegment>(lines.Count - removedLines.Count);
        int projectedStart = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            if (removedLines.Contains(index))
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
        var nodes = new List<AnnotatedSourceNode>(csharpNodes.Length);
        var nodeIds = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var node in csharpNodes)
        {
            int id = nodes.Count;
            nodeIds.Add(node.Id, id);
            nodes.Add(new AnnotatedSourceNode(
                id,
                node.Kind,
                SourceLineKind.CSharp,
                ProjectSpans(node.Spans, segments, $"C# node {node.Id}"),
                Provenance: node.Provenance));
        }

        var regions = new List<AnnotatedSourceRegion>(source.Regions.Count);
        foreach (var region in source.Regions)
        {
            regions.Add(new AnnotatedSourceRegion(
                region.Role,
                ProjectSpans(region.Spans, segments, $"{region.Role} region")));
        }

        var targetCounts = new int[source.Facts.Count];
        var retainedTargetCounts = new int[source.Facts.Count];
        foreach (var target in source.Targets)
        {
            targetCounts[target.FactId]++;
            if (nodeIds.ContainsKey(target.NodeId))
                retainedTargetCounts[target.FactId]++;
        }

        var facts = new List<AnnotatedSourceFact>(source.Facts.Count);
        var factIds = new int[source.Facts.Count];
        Array.Fill(factIds, -1);
        foreach (var fact in source.Facts)
        {
            if (targetCounts[fact.Id] > 0 && retainedTargetCounts[fact.Id] == 0)
                continue;

            int id = facts.Count;
            factIds[fact.Id] = id;
            facts.Add(fact with { Id = id });
        }

        var targets = new List<AnnotatedSourceTarget>(source.Targets.Count);
        foreach (var target in source.Targets)
        {
            int factId = factIds[target.FactId];
            if (factId >= 0 && nodeIds.TryGetValue(target.NodeId, out int nodeId))
                targets.Add(new(factId, nodeId));
        }

        return new CSharpAnnotatedSourceProjection(
            new AnnotatedSourceDocument(
                text,
                nodes,
                regions,
                facts,
                targets,
                source.Source),
            nodeIds.ToImmutable());
    }

    static IReadOnlyList<AnnotatedSourceSpan> ProjectSpans(
        IReadOnlyList<AnnotatedSourceSpan> spans,
        IReadOnlyList<ProjectedSegment> segments,
        string owner)
    {
        var projected = new List<AnnotatedSourceSpan>();
        int retainedCharacters = 0;
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
                retainedCharacters += length;
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

        int sourceCharacters = spans.Sum(static span => span.Length);
        if (retainedCharacters != sourceCharacters)
        {
            throw new InvalidOperationException(
                $"{owner} overlaps text removed by complete IL line ownership; "
                    + "retained structure cannot be clipped during C# projection.");
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
