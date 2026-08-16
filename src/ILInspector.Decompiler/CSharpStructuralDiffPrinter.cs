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
    const int MaximumInlineTransitionLength = 120;

    /// <summary>Projects typed structural rows without recomputing correspondence.</summary>
    public static ImmutableArray<CSharpStructuralDiffDisplayRow> ToDisplayRows(
        CSharpStructuralComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return
        [
            .. comparison.Rows.Select(row => new CSharpStructuralDiffDisplayRow(
                FormatChange(row.Change),
                FormatTransition(Contain(row.BeforeLabel), Contain(row.AfterLabel)),
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
        EnsureDisplaySafe(lines, side);
        var annotationsByLine = new Dictionary<int, List<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)>>();

        foreach (var row in comparison.Rows)
        {
            var spans = side == CSharpStructuralSide.Before ? row.BeforeSpans : row.AfterSpans;
            string? label = side == CSharpStructuralSide.Before ? row.BeforeLabel : row.AfterLabel;
            var region = side == CSharpStructuralSide.Before ? row.BeforeRegion : row.AfterRegion;
            if (label is null)
                continue;

            string annotationText =
                $"raise: {Contain(label)}{RegionSuffix(region)}"
                + TextTransitionSuffix(comparison, row, side);
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

            entries.Sort(static (left, right) =>
            {
                int result = left.Extent.Column.CompareTo(right.Extent.Column);
                return result != 0 ? result : right.Extent.Length.CompareTo(left.Extent.Length);
            });
            if (!CanRenderInCommentGutter(entries, memberIndent.Length))
            {
                output.AddRange(RenderExactFallback(entries));
                continue;
            }

            var facts = entries.Select(static entry => entry.Fact).ToArray();
            var extents = entries.ToDictionary(static entry => entry.Fact, static entry => entry.Extent);
            var rendered = AnnotationCaret.Render(line.Text, memberIndent, facts, extents: extents);
            output.AddRange(rendered.Count > 0 ? rendered : RenderExactFallback(entries));
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
            ? $"{transition}; {Contain(note)}"
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

    static string TextTransitionSuffix(
        CSharpStructuralComparison comparison,
        CSharpStructuralDiffRow row,
        CSharpStructuralSide side)
    {
        if (!row.Change.HasFlag(CSharpStructuralChangeKind.Changed)
            || CSharpBodyDiff.SelectedTextEqual(
                comparison.Before,
                row.BeforeSpans,
                comparison.After,
                row.AfterSpans))
            return "";

        if (row.BeforeSpans.Length != 1 || row.AfterSpans.Length != 1)
            return "; text changed";

        string beforeText = SelectText(comparison.Before, row.BeforeSpans[0]);
        string afterText = SelectText(comparison.After, row.AfterSpans[0]);
        string counterpart = side == CSharpStructuralSide.Before
            ? afterText
            : beforeText;
        if (counterpart.IndexOfAny(['\r', '\n']) >= 0
            || counterpart.Length > MaximumInlineTransitionLength)
        {
            return "; text changed";
        }

        return side == CSharpStructuralSide.Before
            ? $"; changed to {Contain(counterpart)}"
            : $"; changed from {Contain(counterpart)}";
    }

    static string SelectText(
        AnnotatedSourceDocument document,
        AnnotatedSourceSpan span)
        => document.Text.Substring(span.Start, span.Length);

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

    static bool CanRenderInCommentGutter(
        IReadOnlyList<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)> entries,
        int commentColumn)
    {
        var extents = entries
            .Select(static entry => entry.Extent)
            .Distinct()
            .ToArray();
        if (extents.Length == 1)
            return extents[0].Column >= commentColumn + 3;

        for (int index = 0; index < extents.Length; index++)
        {
            int labelLength = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1;
            if (extents[index].Column - labelLength < commentColumn + 2)
                return false;
        }
        return true;
    }

    static IReadOnlyList<string> RenderExactFallback(
        IReadOnlyList<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)> entries)
    {
        var lines = new List<string>();
        foreach (var group in entries.GroupBy(static entry => entry.Extent))
        {
            bool first = true;
            foreach (var entry in group)
            {
                lines.Add(
                    new string(' ', entry.Extent.Column)
                    + (first ? new string('^', entry.Extent.Length) : new string(' ', entry.Extent.Length))
                    + " "
                    + AnnotationText.Format(entry.Fact));
                first = false;
            }
        }
        return lines;
    }

    static void EnsureDisplaySafe(
        IReadOnlyList<SourceTextLine> lines,
        CSharpStructuralSide side)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].Text;
            if (!string.Equals(line, Contain(line), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{side} document line {index + 1} contains terminal or invisible control text.");
            }
        }
    }

    static string? Contain(string? value)
        => value is null ? null : CSharpText.CSharpIdentifier.ContainRenderedText(value);

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
