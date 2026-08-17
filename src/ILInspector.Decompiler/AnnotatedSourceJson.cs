using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler.Annotations;
using ILInspector.Instructions;

namespace ILInspector.Decompiler;

/// <summary>
/// JSON contracts for <see cref="AnnotatedSourceDocument"/> and
/// <see cref="CSharpStructuralDiffDocument"/>.
/// </summary>
public static class AnnotatedSourceJson
{
    const string DocumentJsonContractError =
        "Annotated-source JSON violates the JSON contract.";
    const string StructuralDiffJsonContractError =
        "C# structural diff JSON violates the JSON contract.";

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
    /// Serializes one product-issued structural diff using the owned wire
    /// contract.
    /// </summary>
    public static string SerializeStructuralDiff(
        CSharpStructuralDiffDocument document,
        bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        return indented
            ? JsonSerializer.Serialize(
                document,
                AnnotatedSourceDocumentJsonContext.Default.CSharpStructuralDiffDocument)
            : JsonSerializer.Serialize(
                document,
                AnnotatedSourceDocumentCompactJsonContext.Default.CSharpStructuralDiffDocument);
    }

    /// <summary>
    /// Reads one product-issued structural diff from an untrusted JSON payload,
    /// then reissues correspondence from its exact embedded documents.
    /// </summary>
    public static CSharpStructuralDiffDocument DeserializeStructuralDiff(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Validate(
            json,
            ValidateStructuralDiff,
            "C# structural diff JSON is malformed.");
        try
        {
            return JsonSerializer.Deserialize(
                json,
                AnnotatedSourceStrictJsonContext.Default.CSharpStructuralDiffDocument)
                ?? throw new JsonException("C# structural diff document is null.");
        }
        catch (AnnotatedSourceContractJsonException error)
        {
            throw new JsonException(error.Message);
        }
        catch (JsonException)
        {
            throw new JsonException(StructuralDiffJsonContractError);
        }
        catch (ArgumentException)
        {
            throw new JsonException(
                "C# structural diff JSON violates the product-issued model contract.");
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
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new JsonException(malformedJsonError);
        }

        using (document)
        {
            try
            {
                validate(document.RootElement);
            }
            catch (InvalidOperationException)
            {
                throw new JsonException(malformedJsonError);
            }
        }
    }

    static void ValidateStructuralDiff(JsonElement root)
    {
        RequireProperties(
            root,
            "schema_version",
            "methodology_version",
            "correspondence",
            "before",
            "after",
            "rows");

        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("correspondence", out var correspondence))
            ValidateCorrespondence(correspondence);
        if (root.TryGetProperty("before", out var before))
            ValidateDocument(before, "before");
        if (root.TryGetProperty("after", out var after))
            ValidateDocument(after, "after");
        if (root.TryGetProperty("rows", out var rows))
            ValidateStructuralRows(rows);
        if (root.TryGetProperty("fidelity", out var fidelity)
            && fidelity.ValueKind != JsonValueKind.Null)
        {
            RequireProperties(fidelity, ["before", "after"], "fidelity");
        }

        static void ValidateStructuralRows(JsonElement rows)
        {
            ValidateObjectArray(rows, "rows", "change", "before_spans", "after_spans");
            if (rows.ValueKind != JsonValueKind.Array)
                return;

            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    continue;
                if (row.TryGetProperty("before_spans", out var beforeSpans))
                    ValidateObjectArray(beforeSpans, "rows.before_spans", "start", "length");
                if (row.TryGetProperty("after_spans", out var afterSpans))
                    ValidateObjectArray(afterSpans, "rows.after_spans", "start", "length");
            }
        }
    }

    static void ValidateCorrespondence(JsonElement correspondence)
    {
        RequireProperties(
            correspondence,
            [
                "subject",
                "before",
                "after",
                "before_revision",
                "after_revision",
                "matches",
                "unmatched_before",
                "unmatched_after",
            ],
            "correspondence");
        if (correspondence.ValueKind != JsonValueKind.Object)
            return;

        if (correspondence.TryGetProperty("before", out var before))
            ValidateDocument(before, "correspondence.before");
        if (correspondence.TryGetProperty("after", out var after))
            ValidateDocument(after, "correspondence.after");
        if (correspondence.TryGetProperty("before_revision", out var beforeRevision))
            RequireProperties(beforeRevision, ["sha256"], "correspondence.before_revision");
        if (correspondence.TryGetProperty("after_revision", out var afterRevision))
            RequireProperties(afterRevision, ["sha256"], "correspondence.after_revision");
        if (correspondence.TryGetProperty("matches", out var matches))
        {
            ValidateObjectArray(
                matches,
                "correspondence.matches",
                "before",
                "after",
                "provenance",
                "evidence",
                "moved");
            if (matches.ValueKind == JsonValueKind.Array)
            {
                foreach (var match in matches.EnumerateArray())
                {
                    if (match.ValueKind != JsonValueKind.Object)
                        continue;
                    if (match.TryGetProperty("before", out var matchBefore))
                        ValidateDocumentNodeIdentity(matchBefore, "correspondence.matches.before");
                    if (match.TryGetProperty("after", out var matchAfter))
                        ValidateDocumentNodeIdentity(matchAfter, "correspondence.matches.after");
                    if (match.TryGetProperty("evidence", out var evidence))
                        RequireProperties(evidence, ["il_offsets"], "correspondence.matches.evidence");
                }
            }
        }
        if (correspondence.TryGetProperty("unmatched_before", out var unmatchedBefore))
            ValidateUnmatched(unmatchedBefore, "correspondence.unmatched_before");
        if (correspondence.TryGetProperty("unmatched_after", out var unmatchedAfter))
            ValidateUnmatched(unmatchedAfter, "correspondence.unmatched_after");
    }

    static void ValidateUnmatched(JsonElement values, string name)
    {
        ValidateObjectArray(values, name, "node", "reason");
        if (values.ValueKind != JsonValueKind.Array)
            return;

        foreach (var unmatched in values.EnumerateArray())
        {
            if (unmatched.ValueKind != JsonValueKind.Object)
                continue;
            if (unmatched.TryGetProperty("node", out var node))
                ValidateDocumentNodeIdentity(node, $"{name}.node");
            if (unmatched.TryGetProperty("evidence", out var evidence)
                && evidence.ValueKind != JsonValueKind.Null)
            {
                RequireProperties(evidence, ["il_offsets"], $"{name}.evidence");
            }
        }
    }

    static void ValidateDocumentNodeIdentity(JsonElement identity, string name)
    {
        RequireProperties(identity, ["document", "node_id"], name);
        if (identity.ValueKind == JsonValueKind.Object
            && identity.TryGetProperty("document", out var revision))
        {
            RequireProperties(revision, ["sha256"], $"{name}.document");
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
            if (nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    if (node.ValueKind == JsonValueKind.Object
                        && node.TryGetProperty("provenance", out var provenance)
                        && provenance.ValueKind == JsonValueKind.Object)
                    {
                        RequireProperties(provenance, ["il_offsets"], $"{name}.nodes.provenance");
                    }
                }
            }
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
        if (document.TryGetProperty("source", out var source)
            && source.ValueKind == JsonValueKind.Object)
        {
            RequireProperties(
                source,
                [
                    "assembly_name",
                    "module_version_id",
                    "method_token",
                    "body_fingerprint",
                    "subject",
                ],
                $"{name}.source");
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
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new JsonException($"{name} must contain JSON objects.");

            RequireProperties(value, requiredProperties, name);
        }
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

        string[] nullProperties =
        [
            .. requiredProperties.Where(
                propertyName => value.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.Null)
        ];
        if (nullProperties.Length > 0)
        {
            throw new JsonException(
                $"{name} has null required properties: {string.Join(", ", nullProperties)}.");
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
        typeof(StrictCSharpNodeMatchProvenanceJsonConverter),
        typeof(StrictCSharpUnmatchedNodeReasonJsonConverter),
        typeof(StrictCSharpStructuralChangeKindJsonConverter),
    ],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AnnotatedSourceDocument))]
[JsonSerializable(typeof(CSharpStructuralDiffDocument))]
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
        => "C# structural diff JSON contains an unknown IL body-diff outcome.";

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

internal sealed class StrictCSharpNodeMatchProvenanceJsonConverter
    : StrictStringEnumJsonConverter<CSharpNodeMatchProvenance>
{
    protected override string ErrorMessage
        => "C# structural diff JSON contains an unknown node-match provenance.";

    protected override bool TryParse(string name, out CSharpNodeMatchProvenance value)
    {
        if (name == nameof(CSharpNodeMatchProvenance.IlOriginSet))
        {
            value = CSharpNodeMatchProvenance.IlOriginSet;
            return true;
        }

        value = default;
        return false;
    }

    protected override string? GetName(CSharpNodeMatchProvenance value)
        => value == CSharpNodeMatchProvenance.IlOriginSet
            ? nameof(CSharpNodeMatchProvenance.IlOriginSet)
            : null;
}

internal sealed class StrictCSharpUnmatchedNodeReasonJsonConverter
    : StrictStringEnumJsonConverter<CSharpUnmatchedNodeReason>
{
    protected override string ErrorMessage
        => "C# structural diff JSON contains an unknown unmatched-node reason.";

    protected override bool TryParse(string name, out CSharpUnmatchedNodeReason value)
    {
        value = name switch
        {
            nameof(CSharpUnmatchedNodeReason.Unsupported) => CSharpUnmatchedNodeReason.Unsupported,
            nameof(CSharpUnmatchedNodeReason.Ambiguous) => CSharpUnmatchedNodeReason.Ambiguous,
            nameof(CSharpUnmatchedNodeReason.NoCounterpart) => CSharpUnmatchedNodeReason.NoCounterpart,
            _ => default,
        };
        return name is nameof(CSharpUnmatchedNodeReason.Unsupported)
            or nameof(CSharpUnmatchedNodeReason.Ambiguous)
            or nameof(CSharpUnmatchedNodeReason.NoCounterpart);
    }

    protected override string? GetName(CSharpUnmatchedNodeReason value)
        => value switch
        {
            CSharpUnmatchedNodeReason.Unsupported => nameof(CSharpUnmatchedNodeReason.Unsupported),
            CSharpUnmatchedNodeReason.Ambiguous => nameof(CSharpUnmatchedNodeReason.Ambiguous),
            CSharpUnmatchedNodeReason.NoCounterpart => nameof(CSharpUnmatchedNodeReason.NoCounterpart),
            _ => null,
        };
}

internal sealed class StrictCSharpStructuralChangeKindJsonConverter
    : StrictStringEnumJsonConverter<CSharpStructuralChangeKind>
{
    protected override string ErrorMessage
        => "C# structural diff JSON contains an unknown structural change kind.";

    protected override bool TryParse(string name, out CSharpStructuralChangeKind value)
    {
        value = name switch
        {
            nameof(CSharpStructuralChangeKind.Added) => CSharpStructuralChangeKind.Added,
            nameof(CSharpStructuralChangeKind.Removed) => CSharpStructuralChangeKind.Removed,
            nameof(CSharpStructuralChangeKind.Changed) => CSharpStructuralChangeKind.Changed,
            nameof(CSharpStructuralChangeKind.Moved) => CSharpStructuralChangeKind.Moved,
            "Changed, Moved" =>
                CSharpStructuralChangeKind.Changed | CSharpStructuralChangeKind.Moved,
            _ => default,
        };
        return name is nameof(CSharpStructuralChangeKind.Added)
            or nameof(CSharpStructuralChangeKind.Removed)
            or nameof(CSharpStructuralChangeKind.Changed)
            or nameof(CSharpStructuralChangeKind.Moved)
            or "Changed, Moved";
    }

    protected override string? GetName(CSharpStructuralChangeKind value)
        => value switch
        {
            CSharpStructuralChangeKind.Added => nameof(CSharpStructuralChangeKind.Added),
            CSharpStructuralChangeKind.Removed => nameof(CSharpStructuralChangeKind.Removed),
            CSharpStructuralChangeKind.Changed => nameof(CSharpStructuralChangeKind.Changed),
            CSharpStructuralChangeKind.Moved => nameof(CSharpStructuralChangeKind.Moved),
            CSharpStructuralChangeKind.Changed | CSharpStructuralChangeKind.Moved =>
                "Changed, Moved",
            _ => null,
        };
}
