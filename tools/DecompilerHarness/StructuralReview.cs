using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;

namespace ILInspector.DecompilerHarness;

internal static class StructuralReview
{
    public static int Run(string path)
    {
        try
        {
            var input = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                StructuralReviewJsonContext.Default.CSharpStructuralComparisonInput)
                ?? throw new JsonException("Structural comparison input is null.");
            var comparison = CSharpBodyDiff.CompareStructure(input);
            Console.Write(RenderMarkdown(comparison));
            return 0;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            Console.Error.WriteLine($"Error: Could not render structural review '{path}': {ex.Message}");
            return 1;
        }
    }

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

        var output = new StringBuilder();
        output.AppendLine("# Structural review")
            .AppendLine()
            .Append("Target: ")
            .AppendLine(InlineCode(comparison.Subject))
            .AppendLine()
            .AppendLine("## Before")
            .AppendLine()
            .AppendLine(FencedCSharp(beforeBody))
            .AppendLine()
            .AppendLine("## After")
            .AppendLine()
            .AppendLine(FencedCSharp(afterBody))
            .AppendLine()
            .AppendLine("## Structural diff")
            .AppendLine();

        if (rows.IsEmpty)
        {
            output.AppendLine("No structural changes.");
            return output.ToString();
        }

        output.AppendLine("| Change | Structure | Region | Before spans | After spans | Fidelity |")
            .AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var row in rows)
        {
            output.Append("| ")
                .Append(TableCell(row.Change)).Append(" | ")
                .Append(TableCell(row.Structure)).Append(" | ")
                .Append(TableCell(row.Region)).Append(" | ")
                .Append(TableCell(row.BeforeSpans)).Append(" | ")
                .Append(TableCell(row.AfterSpans)).Append(" | ")
                .Append(TableCell(row.Fidelity)).AppendLine(" |");
        }

        return output.ToString();
    }

    static string FencedCSharp(string body)
    {
        string fence = new('`', Math.Max(3, LongestRun(body, '`') + 1));
        return $"{fence}csharp\n{body}\n{fence}";
    }

    static string InlineCode(string value)
    {
        string contained = value.ReplaceLineEndings(" ");
        string fence = new('`', Math.Max(1, LongestRun(contained, '`') + 1));
        return $"{fence} {contained} {fence}";
    }

    static string TableCell(string value)
        => WebUtility.HtmlEncode(value.ReplaceLineEndings(" "));

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

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CSharpStructuralComparisonInput))]
internal sealed partial class StructuralReviewJsonContext : JsonSerializerContext;
