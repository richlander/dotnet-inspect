using System.Collections.Immutable;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler;

/// <summary>Side of a structural comparison.</summary>
public enum CSharpStructuralSide
{
    /// <summary>The before document.</summary>
    Before,

    /// <summary>The after document.</summary>
    After,
}

/// <summary>Presentation-ready rich structural-diff row.</summary>
/// <param name="Change">Explicit structural outcome or outcomes.</param>
/// <param name="Structure">Stable-kind display transition.</param>
/// <param name="Region">Enclosing-region transition.</param>
/// <param name="BeforeSpans">Before absolute UTF-16 spans.</param>
/// <param name="AfterSpans">After absolute UTF-16 spans.</param>
/// <param name="Fidelity">Independent compile-back transition and optional note.</param>
public sealed record CSharpStructuralDiffDisplayRow(
    string Change,
    string Structure,
    string Region,
    string BeforeSpans,
    string AfterSpans,
    string Fidelity);

/// <summary>
/// Producer-owned display projection for structural C# body comparison.
/// </summary>
public static class CSharpStructuralDiffPrinter
{
    /// <summary>Projects typed structural rows without recomputing correspondence.</summary>
    public static ImmutableArray<CSharpStructuralDiffDisplayRow> ToDisplayRows(
        CSharpStructuralComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return
        [
            .. comparison.Rows.Select(row => new CSharpStructuralDiffDisplayRow(
                FormatChange(row.Change),
                FormatTransition(row.BeforeLabel, row.AfterLabel),
                FormatTransition(row.BeforeRegion?.ToString(), row.AfterRegion?.ToString()),
                FormatSpans(row.BeforeSpans),
                FormatSpans(row.AfterSpans),
                FormatFidelity(comparison.Fidelity)))
        ];
    }

    /// <summary>
    /// Renders one complete C# document with structural caret comments inserted
    /// directly below changed spans. Source lines remain unchanged and in order.
    /// </summary>
    public static string RenderAnnotatedBody(
        CSharpStructuralComparison comparison,
        CSharpStructuralSide side)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (!Enum.IsDefined(side))
            throw new ArgumentException($"Unknown structural comparison side: {side}.", nameof(side));

        var document = side == CSharpStructuralSide.Before
            ? comparison.Before
            : comparison.After;
        var lines = SplitLines(document.Text);
        var annotationsByLine = new Dictionary<int, List<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)>>();

        foreach (var row in comparison.Rows)
        {
            var spans = side == CSharpStructuralSide.Before ? row.BeforeSpans : row.AfterSpans;
            string? label = side == CSharpStructuralSide.Before ? row.BeforeLabel : row.AfterLabel;
            var region = side == CSharpStructuralSide.Before ? row.BeforeRegion : row.AfterRegion;
            if (label is null)
                continue;

            string annotationText = $"raise: {label}{RegionSuffix(region)}";
            foreach (var span in spans)
            {
                for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    var line = lines[lineIndex];
                    int start = Math.Max(span.Start, line.Start);
                    int end = Math.Min(span.Start + span.Length, line.Start + line.Text.Length);
                    if (end <= start)
                        continue;

                    var fact = new StructuralAnnotation(annotationText);
                    if (!annotationsByLine.TryGetValue(lineIndex, out var lineAnnotations))
                        annotationsByLine[lineIndex] = lineAnnotations = [];
                    lineAnnotations.Add((
                        fact,
                        new AnnotationAnchor.CaretExtent(start - line.Start, end - start)));
                }
            }
        }

        string memberIndent = AnnotationCaret.MemberIndent([.. lines.Select(static line => line.Text)]);
        var output = new List<string>(lines.Count + annotationsByLine.Count);
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            output.Add(line.Text);
            if (!annotationsByLine.TryGetValue(lineIndex, out var entries))
                continue;

            // A comment gutter cannot point into its own first three columns.
            // Keep only the suffix that clears the gutter so no caret shifts
            // right and claims characters beyond the selected span.
            int minimumColumn = memberIndent.Length + 3;
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                var entry = entries[index];
                if (entry.Extent.Column >= minimumColumn)
                    continue;

                int trim = minimumColumn - entry.Extent.Column;
                if (trim >= entry.Extent.Length)
                {
                    entries.RemoveAt(index);
                    continue;
                }
                entries[index] = (
                    entry.Fact,
                    new AnnotationAnchor.CaretExtent(
                        minimumColumn,
                        entry.Extent.Length - trim));
            }
            if (entries.Count == 0)
                continue;

            entries.Sort(static (left, right) =>
            {
                int result = left.Extent.Column.CompareTo(right.Extent.Column);
                return result != 0 ? result : right.Extent.Length.CompareTo(left.Extent.Length);
            });
            var facts = entries.Select(static entry => entry.Fact).ToArray();
            var extents = entries.ToDictionary(static entry => entry.Fact, static entry => entry.Extent);
            output.AddRange(AnnotationCaret.Render(line.Text, memberIndent, facts, extents: extents));
        }

        return string.Join('\n', output);
    }

    static string FormatChange(CSharpStructuralChangeKind change)
    {
        var values = new List<string>(2);
        if (change.HasFlag(CSharpStructuralChangeKind.Added)) values.Add(nameof(CSharpStructuralChangeKind.Added));
        if (change.HasFlag(CSharpStructuralChangeKind.Removed)) values.Add(nameof(CSharpStructuralChangeKind.Removed));
        if (change.HasFlag(CSharpStructuralChangeKind.Changed)) values.Add(nameof(CSharpStructuralChangeKind.Changed));
        if (change.HasFlag(CSharpStructuralChangeKind.Moved)) values.Add(nameof(CSharpStructuralChangeKind.Moved));
        return string.Join(", ", values);
    }

    static string FormatTransition(string? before, string? after)
        => (before, after) switch
        {
            (null, { } added) => $"+ {added}",
            ({ } removed, null) => $"- {removed}",
            ({ } oldValue, { } newValue) when oldValue == newValue => oldValue,
            ({ } oldValue, { } newValue) => $"{oldValue} -> {newValue}",
            _ => "",
        };

    static string FormatSpans(ImmutableArray<AnnotatedSourceSpan> spans)
        => string.Join(", ", spans.Select(static span => $"[{span.Start}..{span.Start + span.Length})"));

    static string FormatFidelity(CSharpStructuralFidelityEvidence? fidelity)
    {
        if (fidelity is null)
            return "";
        string transition = $"{fidelity.Before} -> {fidelity.After}";
        return fidelity.Note is { Length: > 0 } note
            ? $"{transition}; {note.ReplaceLineEndings(" ")}"
            : transition;
    }

    static string RegionSuffix(PrintedRegionRole? region)
        => region switch
        {
            PrintedRegionRole.Case => " case body",
            PrintedRegionRole.Body => " body",
            PrintedRegionRole.Else => " else clause",
            PrintedRegionRole.Catch => " catch clause",
            PrintedRegionRole.Finally => " finally clause",
            PrintedRegionRole.Header => " header",
            PrintedRegionRole.Construct => " construct",
            _ => "",
        };

    static IReadOnlyList<SourceTextLine> SplitLines(string text)
    {
        var lines = new List<SourceTextLine>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\r' && text[index] != '\n')
                continue;

            int length = index - start;
            lines.Add(new SourceTextLine(start, text.Substring(start, length)));
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                index++;
            start = index + 1;
        }

        lines.Add(new SourceTextLine(start, text[start..]));
        return lines;
    }

    private readonly record struct SourceTextLine(int Start, string Text);

    private sealed class StructuralAnnotation(string text) : IAnnotation
    {
        public AnnotationDescriptor Descriptor { get; } = new(
            text,
            AnnotationCategory.Semantics,
            "structural change");

        public int SourceOffset => -1;

        public AnnotationConditionality Conditionality => AnnotationConditionality.Always;

        public string? Detail => null;
    }
}
