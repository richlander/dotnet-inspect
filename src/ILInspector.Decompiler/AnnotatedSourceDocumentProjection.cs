using System.Collections.Immutable;
using System.Text;

namespace ILInspector.Decompiler;

/// <summary>Projects one medium from a portable annotated-source document.</summary>
public static class AnnotatedSourceDocumentProjection
{
    /// <summary>
    /// Removes IL lines and rebases every retained coordinate onto the exact C#
    /// text those lines surrounded.
    /// </summary>
    /// <remarks>
    /// <c>CompareProjectsInterleavedDocumentsToCSharp</c> gates coordinate and
    /// target rebasing; <c>Harness_ConsumesProductAnnotatedSourceDocumentJson</c>
    /// gates the direct producer-to-consumer path.
    /// </remarks>
    public static AnnotatedSourceDocument CSharpOnly(AnnotatedSourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var ilNodes = document.Nodes
            .Where(node => node.Medium == SourceLineKind.Il)
            .ToArray();
        if (ilNodes.Length == 0)
            return document;

        var textMap = new AnnotatedSourceTextMap(document.Text);
        var removedLines = new bool[textMap.Lines.Length];
        var ilCoverage = Enumerable.Range(0, textMap.Lines.Length)
            .Select(_ => new List<AnnotatedSourceLineSpan>())
            .ToArray();
        foreach (var node in ilNodes)
        {
            foreach (var span in node.Spans)
            {
                var pieces = textMap.Project(span);
                if (pieces.IsDefaultOrEmpty)
                {
                    throw new InvalidOperationException(
                        $"IL node {node.Id} does not select line text and cannot define a C# projection boundary.");
                }
                foreach (var piece in pieces)
                {
                    removedLines[piece.LineIndex] = true;
                    ilCoverage[piece.LineIndex].Add(piece);
                }
            }
        }
        foreach (var line in textMap.Lines.Where(line => removedLines[line.LineIndex]))
        {
            int coveredThrough = 0;
            foreach (var piece in ilCoverage[line.LineIndex].OrderBy(piece => piece.Column))
            {
                if (piece.Column > coveredThrough)
                {
                    throw new InvalidOperationException(
                        $"IL nodes leave characters [{coveredThrough}, {piece.Column}) "
                        + $"unclassified on line {line.LineIndex}.");
                }
                coveredThrough = Math.Max(coveredThrough, piece.Column + piece.Length);
            }
            if (coveredThrough < line.Text.Length)
            {
                throw new InvalidOperationException(
                    $"IL nodes leave characters [{coveredThrough}, {line.Text.Length}) "
                    + $"unclassified on line {line.LineIndex}.");
            }
        }

        AnnotatedSourceTextLine[] keptLines =
        [
            .. textMap.Lines.Where(line => !removedLines[line.LineIndex]),
        ];
        var kept = ImmutableArray.CreateBuilder<AnnotatedSourceSpan>(keptLines.Length);
        for (int index = 0; index < keptLines.Length; index++)
        {
            var line = keptLines[index];
            int length = line.Text.Length
                + (index + 1 < keptLines.Length ? line.TerminatorLength : 0);
            if (length > 0)
                kept.Add(new AnnotatedSourceSpan(line.Start, length));
        }

        var removed = ImmutableArray.CreateBuilder<AnnotatedSourceSpan>();
        int cursor = 0;
        foreach (var interval in kept)
        {
            if (cursor < interval.Start)
                removed.Add(new AnnotatedSourceSpan(cursor, interval.Start - cursor));
            cursor = interval.Start + interval.Length;
        }
        if (cursor < document.Text.Length)
            removed.Add(new AnnotatedSourceSpan(cursor, document.Text.Length - cursor));

        var text = new StringBuilder(document.Text.Length - removed.Sum(span => span.Length));
        foreach (var interval in kept)
        {
            text.Append(document.Text, interval.Start, interval.Length);
        }

        var nodeIds = new Dictionary<int, int>();
        var nodes = new List<AnnotatedSourceNode>();
        foreach (var node in document.Nodes.Where(node => node.Medium == SourceLineKind.CSharp))
        {
            int projectedId = nodes.Count;
            nodeIds.Add(node.Id, projectedId);
            nodes.Add(new AnnotatedSourceNode(
                projectedId,
                node.Kind,
                SourceLineKind.CSharp,
                Project(node.Spans)));
        }

        var regions = document.Regions
            .Select(region => new AnnotatedSourceRegion(region.Role, Project(region.Spans)))
            .ToArray();
        var facts = new List<AnnotatedSourceFact>(document.Facts.Count);
        var targets = new List<AnnotatedSourceTarget>();
        foreach (var fact in document.Facts)
        {
            AnnotatedSourceTarget[] originalTargets =
            [
                .. document.Targets.Where(target => target.FactId == fact.Id),
            ];
            AnnotatedSourceTarget[] retainedTargets =
            [
                .. originalTargets.Where(target => nodeIds.ContainsKey(target.NodeId)),
            ];
            if (originalTargets.Length > 0 && retainedTargets.Length == 0)
                continue;

            int projectedFactId = facts.Count;
            facts.Add(fact with { Id = projectedFactId });
            targets.AddRange(retainedTargets.Select(target =>
                new AnnotatedSourceTarget(projectedFactId, nodeIds[target.NodeId])));
        }

        return new AnnotatedSourceDocument(
            text.ToString(),
            nodes,
            regions,
            facts,
            targets);

        IReadOnlyList<AnnotatedSourceSpan> Project(IReadOnlyList<AnnotatedSourceSpan> spans)
        {
            var projected = new List<AnnotatedSourceSpan>(spans.Count);
            foreach (var sourceSpan in spans)
            {
                var span = ProjectSpan(sourceSpan);
                if (projected.Count > 0
                    && projected[^1].Start + projected[^1].Length == span.Start)
                {
                    projected[^1] = projected[^1] with
                    {
                        Length = projected[^1].Length + span.Length,
                    };
                }
                else
                {
                    projected.Add(span);
                }
            }
            return projected;
        }

        AnnotatedSourceSpan ProjectSpan(AnnotatedSourceSpan span)
        {
            int end = span.Start + span.Length;
            int removedBefore = 0;
            foreach (var interval in removed)
            {
                int intervalEnd = interval.Start + interval.Length;
                if (intervalEnd <= span.Start)
                {
                    removedBefore += interval.Length;
                    continue;
                }
                if (interval.Start >= end)
                    break;

                throw new InvalidOperationException(
                    $"A retained C# coordinate [{span.Start}, {end}) overlaps an IL line "
                    + $"[{interval.Start}, {intervalEnd}).");
            }
            return new AnnotatedSourceSpan(span.Start - removedBefore, span.Length);
        }
    }
}
