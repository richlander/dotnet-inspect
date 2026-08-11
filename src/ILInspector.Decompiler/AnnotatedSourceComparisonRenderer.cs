using System.Collections.Immutable;
using System.Text;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler;

/// <summary>Renders full-body caret overlays and a structural diff from one comparison result.</summary>
public static class AnnotatedSourceComparisonRenderer
{
    public static string RenderMarkdown(AnnotatedSourceComparisonResult comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        string before = RenderBefore(comparison);
        string after = RenderAfter(comparison);
        string fence = Fence(before, after);
        var output = new StringBuilder()
            .AppendLine("## Before")
            .AppendLine()
            .Append(fence).AppendLine("csharp")
            .AppendLine(before)
            .AppendLine(fence)
            .AppendLine()
            .AppendLine("## After")
            .AppendLine()
            .Append(fence).AppendLine("csharp")
            .AppendLine(after)
            .AppendLine(fence)
            .AppendLine()
            .AppendLine("## Structural diff")
            .AppendLine()
            .Append(RenderRichDiff(comparison));
        return output.ToString().TrimEnd('\r', '\n');
    }

    public static string RenderBefore(AnnotatedSourceComparisonResult comparison)
        => RenderSide(comparison, before: true);

    public static string RenderAfter(AnnotatedSourceComparisonResult comparison)
        => RenderSide(comparison, before: false);

    public static string RenderRichDiff(AnnotatedSourceComparisonResult comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (comparison.Changes.IsEmpty)
            return "No structural changes.";

        var beforeMap = new AnnotatedSourceTextMap(comparison.Before.Text);
        var afterMap = new AnnotatedSourceTextMap(comparison.After.Text);
        var output = new StringBuilder()
            .AppendLine("| Change | Before | After | Context | Before span | After span | Evidence |")
            .AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var change in comparison.Changes)
        {
            string context = Context(change);
            string evidence = change.Evidence.IsDefaultOrEmpty
                ? "-"
                : string.Join("; ", change.Evidence.Select(item => $"{item.Kind}: {item.Summary}"));
            output
                .Append("| ").Append(change.Kind)
                .Append(" | ").Append(Cell(change.Before?.Kind))
                .Append(" | ").Append(Cell(change.After?.Kind))
                .Append(" | ").Append(Cell(context))
                .Append(" | ").Append(Cell(Spans(beforeMap, change.Before)))
                .Append(" | ").Append(Cell(Spans(afterMap, change.After)))
                .Append(" | ").Append(Cell(evidence))
                .AppendLine(" |");
        }

        return output.ToString().TrimEnd();
    }

    static string RenderSide(AnnotatedSourceComparisonResult comparison, bool before)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var document = before ? comparison.Before : comparison.After;
        var map = new AnnotatedSourceTextMap(document.Text);
        var labels = new Dictionary<int, List<CaretLabel>>();

        foreach (var change in comparison.Changes)
        {
            var node = before ? change.Before : change.After;
            if (node is null)
                continue;

            string text = Label(change);
            foreach (var span in node.Spans)
            {
                foreach (var piece in map.Project(span))
                {
                    if (!labels.TryGetValue(piece.LineIndex, out var lineLabels))
                        labels[piece.LineIndex] = lineLabels = [];
                    lineLabels.Add(new CaretLabel(
                        text,
                        new AnnotationAnchor.CaretExtent(piece.Column, piece.Length)));
                }
            }
        }

        string memberIndent = AnnotationCaret.MemberIndent([.. map.Lines.Select(line => line.Text)]);
        var output = new StringBuilder();
        foreach (var line in map.Lines)
        {
            output.AppendLine(line.Text);
            if (!labels.TryGetValue(line.LineIndex, out var lineLabels))
                continue;

            lineLabels.Sort((left, right) =>
            {
                int column = left.Extent!.Value.Column.CompareTo(right.Extent!.Value.Column);
                return column != 0
                    ? column
                    : right.Extent.Value.Length.CompareTo(left.Extent.Value.Length);
            });
            foreach (string caret in AnnotationCaret.RenderLabels(line.Text, memberIndent, lineLabels))
                output.AppendLine(caret);
        }
        return output.ToString().TrimEnd('\r', '\n');
    }

    static string Label(AnnotatedSourceNodeChange change)
    {
        string label = change.Kind switch
        {
            AnnotatedSourceChangeKind.Added => $"added {change.After!.Kind}",
            AnnotatedSourceChangeKind.Removed => $"removed {change.Before!.Kind}",
            AnnotatedSourceChangeKind.Changed => $"{change.Before!.Kind} -> {change.After!.Kind}",
            AnnotatedSourceChangeKind.Moved => $"moved {change.Before!.Kind}",
            _ => change.Kind.ToString(),
        };
        string context = Context(change);
        return context.Length == 0 ? label : $"{label} [{context}]";
    }

    static string Context(AnnotatedSourceNodeChange change)
    {
        string before = change.Before?.RegionPath ?? "";
        string after = change.After?.RegionPath ?? "";
        if (string.Equals(before, after, StringComparison.Ordinal))
            return before;
        if (before.Length == 0)
            return after;
        if (after.Length == 0)
            return before;
        return $"{before} -> {after}";
    }

    static string Spans(
        AnnotatedSourceTextMap map,
        AnnotatedSourceNodeSnapshot? node)
        => node is null
            ? "-"
            : string.Join(
                ", ",
                node.Spans
                    .SelectMany(span => map.Project(span))
                    .Select(span => $"L{span.LineIndex + 1}:C{span.Column + 1}+{span.Length}"));

    static string Cell(string? value)
        => string.IsNullOrEmpty(value)
            ? "-"
            : value.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);

    static string Fence(params string[] values)
    {
        int longest = values
            .Select(LongestBacktickRun)
            .DefaultIfEmpty(0)
            .Max();
        return new string('`', Math.Max(3, longest + 1));
    }

    static int LongestBacktickRun(string value)
    {
        int longest = 0;
        int current = 0;
        foreach (char character in value)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }
}
