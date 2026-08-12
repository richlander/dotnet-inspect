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
            string json = File.ReadAllText(path);
            ValidateRequiredValueTypeProperties(json);
            var input = JsonSerializer.Deserialize(
                json,
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

    static void ValidateRequiredValueTypeProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return;

        ValidateDocument(document.RootElement, "before");
        ValidateDocument(document.RootElement, "after");
    }

    static void ValidateDocument(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var document)
            || document.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ValidateSpans(document, "nodes");
        ValidateSpans(document, "regions");
        ValidateObjectArray(
            document,
            "facts",
            "id",
            "descriptor",
            "category",
            "conditionality",
            "detail",
            "source_offset",
            "origin");
        ValidateObjectArray(document, "targets", "fact_id", "node_id");
    }

    static void ValidateSpans(JsonElement document, string propertyName)
    {
        if (!document.TryGetProperty(propertyName, out var owners)
            || owners.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var owner in owners.EnumerateArray())
        {
            if (owner.ValueKind != JsonValueKind.Object
                || !owner.TryGetProperty("spans", out var spans)
                || spans.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var span in spans.EnumerateArray())
                RequireProperties(span, "start", "length");
        }
    }

    static void ValidateObjectArray(
        JsonElement document,
        string propertyName,
        params string[] requiredProperties)
    {
        if (!document.TryGetProperty(propertyName, out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var value in values.EnumerateArray())
            RequireProperties(value, requiredProperties);
    }

    static void RequireProperties(JsonElement value, params string[] requiredProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return;

        string[] missing =
        [
            .. requiredProperties.Where(propertyName => !value.TryGetProperty(propertyName, out _))
        ];
        if (missing.Length > 0)
        {
            throw new JsonException(
                $"JSON object is missing required properties: {string.Join(", ", missing)}.");
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
