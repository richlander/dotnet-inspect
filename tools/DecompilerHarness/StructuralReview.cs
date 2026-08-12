using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Instructions;

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
            Console.Error.WriteLine(CSharpText.CSharpIdentifier.ContainRenderedText(
                $"Error: Could not render structural review '{path}': {ex.Message}"));
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

        using var output = new StringWriter { NewLine = "\n" };
        output.WriteLine("# Structural review");
        output.WriteLine();
        output.Write("Target: ");
        output.WriteLine(InlineCode(comparison.Subject));
        output.WriteLine();
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
            output.WriteLine("No structural changes.");
            if (comparison.Fidelity is { } fidelity)
            {
                output.WriteLine();
                output.Write("Fidelity: ");
                output.WriteLine(InlineCode(Fidelity(fidelity)));
            }
            return output.ToString();
        }

        output.WriteLine("| Change | Structure | Region | Before spans | After spans | Fidelity |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var row in rows)
        {
            output.Write("| ");
            output.Write(TableCell(row.Change));
            output.Write(" | ");
            output.Write(TableCell(row.Structure));
            output.Write(" | ");
            output.Write(TableCell(row.Region));
            output.Write(" | ");
            output.Write(TableCell(row.BeforeSpans));
            output.Write(" | ");
            output.Write(TableCell(row.AfterSpans));
            output.Write(" | ");
            output.Write(TableCell(row.Fidelity));
            output.WriteLine(" |");
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

internal sealed class StrictStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    static readonly IReadOnlyDictionary<string, TEnum> s_values = Enum
        .GetNames<TEnum>()
        .ToDictionary(static name => name, static name => Enum.Parse<TEnum>(name), StringComparer.Ordinal);

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && reader.GetString() is { } name
            && s_values.TryGetValue(name, out var value))
        {
            return value;
        }

        throw new JsonException(ErrorMessage);
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        string? name = Enum.GetName(value);
        if (name is null)
            throw new JsonException(ErrorMessage);

        writer.WriteStringValue(name);
    }

    static string ErrorMessage => typeof(TEnum) == typeof(IlBodyDiffOutcome)
        ? "Structural fidelity contains an unknown IL body-diff outcome."
        : $"Structural review contains an unknown {typeof(TEnum).Name} value.";
}

[JsonSourceGenerationOptions(
    AllowDuplicateProperties = false,
    Converters =
    [
        typeof(StrictStringEnumJsonConverter<SourceLineKind>),
        typeof(StrictStringEnumJsonConverter<PrintedRegionRole>),
        typeof(StrictStringEnumJsonConverter<AnnotationConditionality>),
        typeof(StrictStringEnumJsonConverter<AnnotatedSourceFactOrigin>),
        typeof(StrictStringEnumJsonConverter<IlBodyDiffOutcome>),
    ],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CSharpStructuralComparisonInput))]
internal sealed partial class StructuralReviewJsonContext : JsonSerializerContext;
