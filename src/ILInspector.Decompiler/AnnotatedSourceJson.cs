using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler.Annotations;
using ILInspector.Instructions;

namespace ILInspector.Decompiler;

/// <summary>
/// JSON contracts for <see cref="AnnotatedSourceDocument"/> and structural
/// comparison input.
/// </summary>
public static class AnnotatedSourceJson
{
    const string DocumentJsonContractError =
        "Annotated-source JSON violates the JSON contract.";
    const string StructuralComparisonJsonContractError =
        "Structural comparison JSON violates the JSON contract.";

    /// <summary>
    /// Reads one annotated-source document from an untrusted JSON payload.
    /// Unknown or duplicate properties, missing required fields, non-exact enum
    /// names, and invalid document topology are rejected.
    /// </summary>
    public static AnnotatedSourceDocument DeserializeDocument(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Validate(
            json,
            static root => ValidateDocument(root, "document"),
            "Annotated-source JSON is malformed.");
        try
        {
            return JsonSerializer.Deserialize(
                json,
                AnnotatedSourceStrictJsonContext.Default.AnnotatedSourceDocument)
                ?? throw new JsonException("Annotated-source document is null.");
        }
        catch (AnnotatedSourceContractJsonException error)
        {
            throw new JsonException(error.Message);
        }
        catch (JsonException)
        {
            throw new JsonException(DocumentJsonContractError);
        }
        catch (ArgumentException)
        {
            throw new JsonException("Annotated-source JSON violates the document model contract.");
        }
    }

    /// <summary>
    /// Reads one owner-issued structural comparison from an untrusted JSON
    /// payload under the same strict annotated-source contract.
    /// </summary>
    public static CSharpStructuralComparisonInput DeserializeStructuralComparison(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Validate(
            json,
            ValidateStructuralComparison,
            "Structural comparison JSON is malformed.");
        try
        {
            return JsonSerializer.Deserialize(
                json,
                AnnotatedSourceStrictJsonContext.Default.CSharpStructuralComparisonInput)
                ?? throw new JsonException("Structural comparison input is null.");
        }
        catch (AnnotatedSourceContractJsonException error)
        {
            throw new JsonException(error.Message);
        }
        catch (JsonException)
        {
            throw new JsonException(StructuralComparisonJsonContractError);
        }
        catch (ArgumentException)
        {
            throw new JsonException("Structural comparison JSON violates the owned model contract.");
        }
    }

    static void Validate(
        string json,
        Action<JsonElement> validate,
        string malformedJsonError)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new JsonException(malformedJsonError);
        }

        using (document)
        {
            validate(document.RootElement);
        }
    }

    static void ValidateStructuralComparison(JsonElement root)
    {
        RequireProperties(
            root,
            "subject",
            "before",
            "after",
            "before_node_ids",
            "after_node_ids",
            "correspondences");

        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("before", out var before))
            ValidateDocument(before, "before");
        if (root.TryGetProperty("after", out var after))
            ValidateDocument(after, "after");
        if (root.TryGetProperty("correspondences", out var correspondences))
        {
            ValidateObjectArray(
                correspondences,
                "correspondences",
                "before_node_id",
                "after_node_id");
        }
        if (root.TryGetProperty("fidelity", out var fidelity)
            && fidelity.ValueKind != JsonValueKind.Null)
        {
            RequireProperties(fidelity, "before", "after");
        }
    }

    static void ValidateDocument(JsonElement document, string name)
    {
        RequireProperties(document, "text", "nodes", "regions", "facts", "targets");
        if (document.ValueKind != JsonValueKind.Object)
            return;

        if (document.TryGetProperty("nodes", out var nodes))
        {
            ValidateObjectArray(nodes, $"{name}.nodes", "id", "kind", "medium", "spans");
            ValidateSpans(nodes, $"{name}.nodes");
        }
        if (document.TryGetProperty("regions", out var regions))
        {
            ValidateObjectArray(regions, $"{name}.regions", "role", "spans");
            ValidateSpans(regions, $"{name}.regions");
        }
        if (document.TryGetProperty("facts", out var facts))
        {
            ValidateObjectArray(
                facts,
                $"{name}.facts",
                "id",
                "descriptor",
                "category",
                "conditionality",
                "source_offset",
                "origin");
        }
        if (document.TryGetProperty("targets", out var targets))
        {
            ValidateObjectArray(targets, $"{name}.targets", "fact_id", "node_id");
        }
    }

    static void ValidateSpans(JsonElement owners, string name)
    {
        if (owners.ValueKind != JsonValueKind.Array)
            return;

        foreach (var owner in owners.EnumerateArray())
        {
            if (owner.ValueKind != JsonValueKind.Object
                || !owner.TryGetProperty("spans", out var spans))
            {
                continue;
            }

            ValidateObjectArray(spans, $"{name}.spans", "start", "length");
        }
    }

    static void ValidateObjectArray(
        JsonElement values,
        string name,
        params string[] requiredProperties)
    {
        if (values.ValueKind != JsonValueKind.Array)
            return;

        foreach (var value in values.EnumerateArray())
            RequireProperties(value, requiredProperties, name);
    }

    static void RequireProperties(
        JsonElement value,
        params string[] requiredProperties)
        => RequireProperties(value, requiredProperties, "object");

    static void RequireProperties(
        JsonElement value,
        string[] requiredProperties,
        string name)
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
                $"{name} is missing required properties: {string.Join(", ", missing)}.");
        }
    }
}

[JsonSourceGenerationOptions(
    AllowDuplicateProperties = false,
    Converters =
    [
        typeof(StrictSourceLineKindJsonConverter),
        typeof(StrictPrintedRegionRoleJsonConverter),
        typeof(StrictAnnotationConditionalityJsonConverter),
        typeof(StrictAnnotatedSourceFactOriginJsonConverter),
        typeof(StrictIlBodyDiffOutcomeJsonConverter),
    ],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AnnotatedSourceDocument))]
[JsonSerializable(typeof(CSharpStructuralComparisonInput))]
internal sealed partial class AnnotatedSourceStrictJsonContext : JsonSerializerContext;

internal sealed class AnnotatedSourceContractJsonException(string message)
    : JsonException(message);

internal abstract class StrictStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    protected abstract string ErrorMessage { get; }

    protected abstract bool TryParse(string name, out TEnum value);

    protected abstract string? GetName(TEnum value);

    public sealed override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && reader.GetString() is { } name
            && TryParse(name, out var value))
        {
            return value;
        }

        throw new AnnotatedSourceContractJsonException(ErrorMessage);
    }

    public sealed override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        string? name = GetName(value);
        if (name is null)
            throw new AnnotatedSourceContractJsonException(ErrorMessage);

        writer.WriteStringValue(name);
    }
}

internal sealed class StrictSourceLineKindJsonConverter
    : StrictStringEnumJsonConverter<SourceLineKind>
{
    protected override string ErrorMessage
        => "Annotated-source JSON contains an unknown SourceLineKind value.";

    protected override bool TryParse(string name, out SourceLineKind value)
    {
        if (name == nameof(SourceLineKind.CSharp))
        {
            value = SourceLineKind.CSharp;
            return true;
        }
        if (name == nameof(SourceLineKind.Il))
        {
            value = SourceLineKind.Il;
            return true;
        }

        value = default;
        return false;
    }

    protected override string? GetName(SourceLineKind value)
        => value switch
        {
            SourceLineKind.CSharp => nameof(SourceLineKind.CSharp),
            SourceLineKind.Il => nameof(SourceLineKind.Il),
            _ => null,
        };
}

internal sealed class StrictPrintedRegionRoleJsonConverter
    : StrictStringEnumJsonConverter<PrintedRegionRole>
{
    protected override string ErrorMessage
        => "Annotated-source JSON contains an unknown PrintedRegionRole value.";

    protected override bool TryParse(string name, out PrintedRegionRole value)
    {
        value = name switch
        {
            nameof(PrintedRegionRole.Construct) => PrintedRegionRole.Construct,
            nameof(PrintedRegionRole.Header) => PrintedRegionRole.Header,
            nameof(PrintedRegionRole.Body) => PrintedRegionRole.Body,
            nameof(PrintedRegionRole.Else) => PrintedRegionRole.Else,
            nameof(PrintedRegionRole.Catch) => PrintedRegionRole.Catch,
            nameof(PrintedRegionRole.Finally) => PrintedRegionRole.Finally,
            nameof(PrintedRegionRole.Case) => PrintedRegionRole.Case,
            _ => default,
        };
        return name is nameof(PrintedRegionRole.Construct)
            or nameof(PrintedRegionRole.Header)
            or nameof(PrintedRegionRole.Body)
            or nameof(PrintedRegionRole.Else)
            or nameof(PrintedRegionRole.Catch)
            or nameof(PrintedRegionRole.Finally)
            or nameof(PrintedRegionRole.Case);
    }

    protected override string? GetName(PrintedRegionRole value)
        => value switch
        {
            PrintedRegionRole.Construct => nameof(PrintedRegionRole.Construct),
            PrintedRegionRole.Header => nameof(PrintedRegionRole.Header),
            PrintedRegionRole.Body => nameof(PrintedRegionRole.Body),
            PrintedRegionRole.Else => nameof(PrintedRegionRole.Else),
            PrintedRegionRole.Catch => nameof(PrintedRegionRole.Catch),
            PrintedRegionRole.Finally => nameof(PrintedRegionRole.Finally),
            PrintedRegionRole.Case => nameof(PrintedRegionRole.Case),
            _ => null,
        };
}

internal sealed class StrictAnnotationConditionalityJsonConverter
    : StrictStringEnumJsonConverter<AnnotationConditionality>
{
    protected override string ErrorMessage
        => "Annotated-source JSON contains an unknown AnnotationConditionality value.";

    protected override bool TryParse(string name, out AnnotationConditionality value)
    {
        value = name switch
        {
            nameof(AnnotationConditionality.Always) => AnnotationConditionality.Always,
            nameof(AnnotationConditionality.CachedOnce) => AnnotationConditionality.CachedOnce,
            nameof(AnnotationConditionality.PerIteration) => AnnotationConditionality.PerIteration,
            _ => default,
        };
        return name is nameof(AnnotationConditionality.Always)
            or nameof(AnnotationConditionality.CachedOnce)
            or nameof(AnnotationConditionality.PerIteration);
    }

    protected override string? GetName(AnnotationConditionality value)
        => value switch
        {
            AnnotationConditionality.Always => nameof(AnnotationConditionality.Always),
            AnnotationConditionality.CachedOnce => nameof(AnnotationConditionality.CachedOnce),
            AnnotationConditionality.PerIteration => nameof(AnnotationConditionality.PerIteration),
            _ => null,
        };
}

internal sealed class StrictAnnotatedSourceFactOriginJsonConverter
    : StrictStringEnumJsonConverter<AnnotatedSourceFactOrigin>
{
    protected override string ErrorMessage
        => "Annotated-source JSON contains an unknown AnnotatedSourceFactOrigin value.";

    protected override bool TryParse(string name, out AnnotatedSourceFactOrigin value)
    {
        if (name == nameof(AnnotatedSourceFactOrigin.Body))
        {
            value = AnnotatedSourceFactOrigin.Body;
            return true;
        }
        if (name == nameof(AnnotatedSourceFactOrigin.MemberHeader))
        {
            value = AnnotatedSourceFactOrigin.MemberHeader;
            return true;
        }

        value = default;
        return false;
    }

    protected override string? GetName(AnnotatedSourceFactOrigin value)
        => value switch
        {
            AnnotatedSourceFactOrigin.Body => nameof(AnnotatedSourceFactOrigin.Body),
            AnnotatedSourceFactOrigin.MemberHeader => nameof(AnnotatedSourceFactOrigin.MemberHeader),
            _ => null,
        };
}

internal sealed class StrictIlBodyDiffOutcomeJsonConverter
    : StrictStringEnumJsonConverter<IlBodyDiffOutcome>
{
    protected override string ErrorMessage
        => "Structural comparison JSON contains an unknown IL body-diff outcome.";

    protected override bool TryParse(string name, out IlBodyDiffOutcome value)
    {
        value = name switch
        {
            nameof(IlBodyDiffOutcome.Unavailable) => IlBodyDiffOutcome.Unavailable,
            nameof(IlBodyDiffOutcome.Exact) => IlBodyDiffOutcome.Exact,
            nameof(IlBodyDiffOutcome.OperandDiff) => IlBodyDiffOutcome.OperandDiff,
            nameof(IlBodyDiffOutcome.OpcodeDiff) => IlBodyDiffOutcome.OpcodeDiff,
            _ => default,
        };
        return name is nameof(IlBodyDiffOutcome.Unavailable)
            or nameof(IlBodyDiffOutcome.Exact)
            or nameof(IlBodyDiffOutcome.OperandDiff)
            or nameof(IlBodyDiffOutcome.OpcodeDiff);
    }

    protected override string? GetName(IlBodyDiffOutcome value)
        => value switch
        {
            IlBodyDiffOutcome.Unavailable => nameof(IlBodyDiffOutcome.Unavailable),
            IlBodyDiffOutcome.Exact => nameof(IlBodyDiffOutcome.Exact),
            IlBodyDiffOutcome.OperandDiff => nameof(IlBodyDiffOutcome.OperandDiff),
            IlBodyDiffOutcome.OpcodeDiff => nameof(IlBodyDiffOutcome.OpcodeDiff),
            _ => null,
        };
}
