using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Analysis;

/// <summary>
/// Anchors offset-keyed annotations onto the statements of a raised tree — the
/// bridge that lets one annotation set drive the C# view co-equally with the IL
/// view. Anchoring is by IL-offset RANGE, not exact match, which is the crux:
/// an allocation can be erased by the raise (a box behind <c>object o = x;</c>
/// raises to <c>return x;</c>), so no surviving node carries its exact offset —
/// but the offset still falls within the enclosing statement's range, so the
/// fact lands on the right line. The tightest containing statement wins, so a
/// fact inside a loop body anchors to the inner statement, not the loop header.
/// </summary>
public static class AnnotationAnchor
{
    /// <summary>
    /// Maps each annotation to the raised statement it belongs on. Statements
    /// are block children at every nesting depth; an annotation whose offset no
    /// statement range covers binds to the nearest preceding statement, so it is
    /// never dropped (positive-only: a fact is always shown somewhere).
    /// </summary>
    public static IReadOnlyDictionary<IrNode, IReadOnlyList<Annotation>> Anchor(
        IrFunction raised, IReadOnlyList<Annotation> annotations)
    {
        var statements = new List<StatementSpan>();
        foreach (var block in raised.Descendants.OfType<Block>())
        {
            foreach (var statement in block.Children)
            {
                int min = int.MaxValue, max = int.MinValue;
                foreach (var node in Self(statement))
                {
                    if (node.SourceOffset < 0)
                        continue;
                    min = Math.Min(min, node.SourceOffset);
                    max = Math.Max(max, node.SourceOffset);
                }
                if (max >= 0)
                    statements.Add(new StatementSpan(statement, min, max));
            }
        }

        var map = new Dictionary<IrNode, List<Annotation>>();
        foreach (var annotation in annotations)
        {
            var owner = Best(statements, annotation.SourceOffset);
            if (owner is null)
                continue;
            if (!map.TryGetValue(owner, out var list))
                map[owner] = list = [];
            list.Add(annotation);
        }

        return map.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<Annotation>)[.. entry.Value.OrderBy(a => a.SourceOffset)]);
    }

    /// <summary>
    /// The tightest statement whose offset range contains <paramref name="offset"/>;
    /// failing containment, the statement with the greatest start at or before it
    /// (the one the offset falls into or just after); failing that, the earliest.
    /// </summary>
    static IrNode? Best(List<StatementSpan> statements, int offset)
    {
        StatementSpan? containing = null;
        StatementSpan? preceding = null;
        StatementSpan? earliest = null;
        foreach (var span in statements)
        {
            if (earliest is null || span.Min < earliest.Value.Min)
                earliest = span;
            if (span.Min <= offset && (preceding is null || span.Min > preceding.Value.Min))
                preceding = span;
            if (span.Min <= offset && offset <= span.Max)
            {
                int width = span.Max - span.Min;
                if (containing is null || width < containing.Value.Max - containing.Value.Min)
                    containing = span;
            }
        }
        return (containing ?? preceding ?? earliest)?.Statement;
    }

    static IEnumerable<IrNode> Self(IrNode node)
    {
        yield return node;
        foreach (var descendant in node.Descendants)
            yield return descendant;
    }

    readonly record struct StatementSpan(IrNode Statement, int Min, int Max);
}
