using System.Net;
using System.Text.Json;
using ILInspector.Decompiler;

namespace ILInspector.DecompilerHarness;

internal static class StructuralReview
{
    const int MaximumCorrespondenceGapExamples = 5;

    public static int Run(string path, string? afterPath, bool json)
    {
        try
        {
            var document = afterPath is null
                ? AnnotatedSourceJson.DeserializeStructuralDiff(File.ReadAllText(path))
                : CSharpStructuralDiffDocument.Create(
                    ReadDocument(path),
                    ReadDocument(afterPath));
            Console.Write(json
                ? AnnotatedSourceJson.SerializeStructuralDiff(document)
                : RenderMarkdown(document.ToComparison()));
            return 0;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            Console.Error.WriteLine(CSharpText.CSharpIdentifier.ContainRenderedText(
                afterPath is null
                    ? $"Error: Could not render structural diff '{path}': {ex.Message}"
                    : $"Error: Could not render structural review '{path}' and '{afterPath}': {ex.Message}"));
            return 1;
        }
    }

    static AnnotatedSourceDocument ReadDocument(string path)
        => AnnotatedSourceJson.DeserializeDocument(File.ReadAllText(path));

    internal static string RenderMarkdown(CSharpStructuralComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        string beforeBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.Before);
        string afterBody = CSharpStructuralDiffPrinter.RenderAnnotatedBody(
            comparison,
            CSharpStructuralSide.After);
        var rows = CSharpStructuralDiffPrinter.ToDisplayRows(comparison);
        var correspondenceGaps = GetCorrespondenceGaps(comparison.Correspondence);

        using var output = new StringWriter { NewLine = "\n" };
        output.WriteLine("# Structural review");
        output.WriteLine();
        output.Write("Target: ");
        output.WriteLine(InlineCode(comparison.Subject));
        output.WriteLine();
        if (correspondenceGaps.Length > 0)
        {
            output.WriteLine(
                $"Structural review status: **Partial** - {correspondenceGaps.Length} unsupported or ambiguous " +
                "nodes were excluded. Matched rows do not establish changes represented only by the gaps below.");
            output.WriteLine();
        }
        output.WriteLine("## Before");
        output.WriteLine();
        output.WriteLine(FencedCSharp(beforeBody));
        output.WriteLine();
        output.WriteLine("## After");
        output.WriteLine();
        output.WriteLine(FencedCSharp(afterBody));
        output.WriteLine();
        output.WriteLine("## Structural diff");
        output.WriteLine();

        if (rows.IsEmpty)
        {
            output.WriteLine(comparison.IsCorrespondenceComplete
                ? "No structural changes."
                : "No supported structural changes; correspondence is incomplete.");
            if (comparison.Fidelity is { } fidelity)
            {
                output.WriteLine();
                output.Write("Fidelity: ");
                output.WriteLine(InlineCode(Fidelity(fidelity)));
            }
        }
        else
        {
            bool includeFidelity = rows.Any(static row => row.Fidelity.Length > 0);
            output.WriteLine(includeFidelity
                ? "| Change | Structure | Region | Fidelity |"
                : "| Change | Structure | Region |");
            output.WriteLine(includeFidelity
                ? "| --- | --- | --- | --- |"
                : "| --- | --- | --- |");
            foreach (var row in rows)
            {
                output.Write("| ");
                output.Write(TableCell(row.Change));
                output.Write(" | ");
                output.Write(TableCell(row.Structure));
                output.Write(" | ");
                output.Write(TableCell(row.Region));
                if (includeFidelity)
                {
                    output.Write(" | ");
                    output.Write(TableCell(row.Fidelity));
                }
                output.WriteLine(" |");
            }
        }

        WriteCorrespondenceGaps(output, correspondenceGaps);
        return output.ToString();
    }

    static (CSharpStructuralSide Side, CSharpUnmatchedNode Node)[] GetCorrespondenceGaps(
        CSharpNodeCorrespondenceResult? correspondence)
    {
        if (correspondence is null)
            return [];

        return
        [
            .. correspondence.UnmatchedBefore
            .Where(static node => node.Reason != CSharpUnmatchedNodeReason.NoCounterpart)
            .Select(static node => (CSharpStructuralSide.Before, Node: node))
            .Concat(correspondence.UnmatchedAfter
                .Where(static node => node.Reason != CSharpUnmatchedNodeReason.NoCounterpart)
                .Select(static node => (CSharpStructuralSide.After, Node: node)))
        ];
    }

    static void WriteCorrespondenceGaps(
        StringWriter output,
        (CSharpStructuralSide Side, CSharpUnmatchedNode Node)[] gaps)
    {
        if (gaps.Length == 0)
            return;

        output.WriteLine();
        output.WriteLine("## Correspondence gaps");
        output.WriteLine();
        output.WriteLine("| Side | Reason | Count | Example nodes |");
        output.WriteLine("| --- | --- | ---: | --- |");
        foreach (var group in gaps
                     .GroupBy(static gap => (gap.Side, gap.Node.Reason))
                     .OrderBy(static group => group.Key.Side)
                     .ThenBy(static group => group.Key.Reason))
        {
            int[] nodes = [.. group.Select(static gap => gap.Node.Node.NodeId).Order()];
            string examples = string.Join(", ", nodes.Take(MaximumCorrespondenceGapExamples));
            if (nodes.Length > MaximumCorrespondenceGapExamples)
                examples += $" (+{nodes.Length - MaximumCorrespondenceGapExamples} more)";

            output.Write("| ");
            output.Write(group.Key.Side);
            output.Write(" | ");
            output.Write(group.Key.Reason);
            output.Write(" | ");
            output.Write(nodes.Length);
            output.Write(" | ");
            output.Write(examples);
            output.WriteLine(" |");
        }
    }

    static string FencedCSharp(string body)
    {
        string fence = new('`', Math.Max(3, LongestRun(body, '`') + 1));
        return $"{fence}csharp\n{body}\n{fence}";
    }

    static string InlineCode(string value)
    {
        string contained = CSharpText.CSharpIdentifier.ContainRenderedText(value);
        string fence = new('`', Math.Max(1, LongestRun(contained, '`') + 1));
        return $"{fence} {contained} {fence}";
    }

    static string TableCell(string value)
        => WebUtility.HtmlEncode(value)
            .Replace("|", "&#124;", StringComparison.Ordinal)
            .Replace("!", "&#33;", StringComparison.Ordinal)
            .Replace("[", "&#91;", StringComparison.Ordinal)
            .Replace("]", "&#93;", StringComparison.Ordinal);

    static string Fidelity(CSharpStructuralFidelityEvidence fidelity)
    {
        string transition = $"{fidelity.Before} -> {fidelity.After}";
        return fidelity.Note is { Length: > 0 } note
            ? $"{transition}; {note}"
            : transition;
    }

    static int LongestRun(string value, char character)
    {
        int longest = 0;
        int current = 0;
        foreach (char candidate in value)
        {
            if (candidate == character)
            {
                longest = Math.Max(longest, ++current);
            }
            else
            {
                current = 0;
            }
        }
        return longest;
    }
}
