using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler;

sealed class AnnotatedSourceSpanJsonConverter
    : JsonConverter<AnnotatedSourceSpan>
{
    public override AnnotatedSourceSpan Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        AnnotatedSourceJsonConverterHelpers.RequireObject(
            reader,
            nameof(AnnotatedSourceSpan));

        int start = 0;
        int length = 0;
        int seenProperties = 0;
        string startName = JsonName(options, nameof(AnnotatedSourceSpan.Start));
        string lengthName = JsonName(options, nameof(AnnotatedSourceSpan.Length));

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName =
                AnnotatedSourceJsonConverterHelpers.ReadPropertyName(
                    ref reader,
                    nameof(AnnotatedSourceSpan));

            if (PropertyMatches(propertyName, startName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 0,
                    options,
                    nameof(AnnotatedSourceSpan));
                start = reader.GetInt32();
            }
            else if (PropertyMatches(propertyName, lengthName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 1,
                    options,
                    nameof(AnnotatedSourceSpan));
                length = reader.GetInt32();
            }
            else
            {
                SkipUnknown(
                    ref reader,
                    options,
                    nameof(AnnotatedSourceSpan));
            }
        }

        AnnotatedSourceJsonConverterHelpers.RequireEndObject(
            reader,
            nameof(AnnotatedSourceSpan));
        return new(start, length);
    }

    public override void Write(
        Utf8JsonWriter writer,
        AnnotatedSourceSpan value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(JsonName(options, nameof(value.Start)), value.Start);
        writer.WriteNumber(JsonName(options, nameof(value.Length)), value.Length);
        writer.WriteEndObject();
    }

    static string JsonName(JsonSerializerOptions options, string name)
        => AnnotatedSourceJsonConverterHelpers.JsonName(options, name);

    static bool PropertyMatches(
        string actual,
        string expected,
        JsonSerializerOptions options)
        => AnnotatedSourceJsonConverterHelpers.PropertyMatches(
            actual,
            expected,
            options);

    static void ObserveProperty(
        ref int seenProperties,
        int property,
        JsonSerializerOptions options,
        string typeName)
        => AnnotatedSourceJsonConverterHelpers.ObserveProperty(
            ref seenProperties,
            property,
            options,
            typeName);

    static void SkipUnknown(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        string typeName)
        => AnnotatedSourceJsonConverterHelpers.SkipUnknown(
            ref reader,
            options,
            typeName);
}

sealed class AnnotatedSourceFactJsonConverter
    : JsonConverter<AnnotatedSourceFact>
{
    public override AnnotatedSourceFact Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        AnnotatedSourceJsonConverterHelpers.RequireObject(
            reader,
            nameof(AnnotatedSourceFact));

        int id = 0;
        string descriptor = null!;
        string category = null!;
        var conditionality = default(AnnotationConditionality);
        string? detail = null;
        int sourceOffset = 0;
        var origin = default(AnnotatedSourceFactOrigin);
        int seenProperties = 0;
        string idName = JsonName(options, nameof(AnnotatedSourceFact.Id));
        string descriptorName =
            JsonName(options, nameof(AnnotatedSourceFact.Descriptor));
        string categoryName =
            JsonName(options, nameof(AnnotatedSourceFact.Category));
        string conditionalityName =
            JsonName(options, nameof(AnnotatedSourceFact.Conditionality));
        string detailName =
            JsonName(options, nameof(AnnotatedSourceFact.Detail));
        string sourceOffsetName =
            JsonName(options, nameof(AnnotatedSourceFact.SourceOffset));
        string originName =
            JsonName(options, nameof(AnnotatedSourceFact.Origin));

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName =
                AnnotatedSourceJsonConverterHelpers.ReadPropertyName(
                    ref reader,
                    nameof(AnnotatedSourceFact));

            if (PropertyMatches(propertyName, idName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 0,
                    options,
                    nameof(AnnotatedSourceFact));
                id = reader.GetInt32();
            }
            else if (PropertyMatches(propertyName, descriptorName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 1,
                    options,
                    nameof(AnnotatedSourceFact));
                descriptor = reader.GetString()!;
            }
            else if (PropertyMatches(propertyName, categoryName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 2,
                    options,
                    nameof(AnnotatedSourceFact));
                category = reader.GetString()!;
            }
            else if (PropertyMatches(
                propertyName,
                conditionalityName,
                options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 3,
                    options,
                    nameof(AnnotatedSourceFact));
                conditionality = ReadConditionality(ref reader);
            }
            else if (PropertyMatches(propertyName, detailName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 4,
                    options,
                    nameof(AnnotatedSourceFact));
                detail = reader.GetString();
            }
            else if (PropertyMatches(
                propertyName,
                sourceOffsetName,
                options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 5,
                    options,
                    nameof(AnnotatedSourceFact));
                sourceOffset = reader.GetInt32();
            }
            else if (PropertyMatches(propertyName, originName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 6,
                    options,
                    nameof(AnnotatedSourceFact));
                origin = ReadOrigin(ref reader);
            }
            else
            {
                SkipUnknown(
                    ref reader,
                    options,
                    nameof(AnnotatedSourceFact));
            }
        }

        AnnotatedSourceJsonConverterHelpers.RequireEndObject(
            reader,
            nameof(AnnotatedSourceFact));
        return new(
            id,
            descriptor,
            category,
            conditionality,
            detail,
            sourceOffset,
            origin);
    }

    public override void Write(
        Utf8JsonWriter writer,
        AnnotatedSourceFact value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(JsonName(options, nameof(value.Id)), value.Id);
        writer.WriteString(
            JsonName(options, nameof(value.Descriptor)),
            value.Descriptor);
        writer.WriteString(
            JsonName(options, nameof(value.Category)),
            value.Category);
        writer.WriteString(
            JsonName(options, nameof(value.Conditionality)),
            ConditionalityName(value.Conditionality));
        if (value.Detail is not null
            || options.DefaultIgnoreCondition
                is not JsonIgnoreCondition.WhenWritingNull
                    and not JsonIgnoreCondition.WhenWritingDefault)
        {
            writer.WriteString(
                JsonName(options, nameof(value.Detail)),
                value.Detail);
        }
        writer.WriteNumber(
            JsonName(options, nameof(value.SourceOffset)),
            value.SourceOffset);
        writer.WriteString(
            JsonName(options, nameof(value.Origin)),
            OriginName(value.Origin));
        writer.WriteEndObject();
    }

    static AnnotationConditionality ReadConditionality(
        ref Utf8JsonReader reader)
    {
        string? name = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : null;
        return name switch
        {
            nameof(AnnotationConditionality.Always) =>
                AnnotationConditionality.Always,
            nameof(AnnotationConditionality.CachedOnce) =>
                AnnotationConditionality.CachedOnce,
            nameof(AnnotationConditionality.PerIteration) =>
                AnnotationConditionality.PerIteration,
            _ => throw new AnnotatedSourceContractJsonException(
                "Annotated-source JSON contains an unknown "
                + "AnnotationConditionality value."),
        };
    }

    static AnnotatedSourceFactOrigin ReadOrigin(ref Utf8JsonReader reader)
    {
        string? name = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : null;
        return name switch
        {
            nameof(AnnotatedSourceFactOrigin.Body) =>
                AnnotatedSourceFactOrigin.Body,
            nameof(AnnotatedSourceFactOrigin.MemberHeader) =>
                AnnotatedSourceFactOrigin.MemberHeader,
            _ => throw new AnnotatedSourceContractJsonException(
                "Annotated-source JSON contains an unknown "
                + "AnnotatedSourceFactOrigin value."),
        };
    }

    static string ConditionalityName(AnnotationConditionality value)
        => value switch
        {
            AnnotationConditionality.Always =>
                nameof(AnnotationConditionality.Always),
            AnnotationConditionality.CachedOnce =>
                nameof(AnnotationConditionality.CachedOnce),
            AnnotationConditionality.PerIteration =>
                nameof(AnnotationConditionality.PerIteration),
            _ => throw new AnnotatedSourceContractJsonException(
                "Annotated-source JSON contains an unknown "
                + "AnnotationConditionality value."),
        };

    static string OriginName(AnnotatedSourceFactOrigin value)
        => value switch
        {
            AnnotatedSourceFactOrigin.Body =>
                nameof(AnnotatedSourceFactOrigin.Body),
            AnnotatedSourceFactOrigin.MemberHeader =>
                nameof(AnnotatedSourceFactOrigin.MemberHeader),
            _ => throw new AnnotatedSourceContractJsonException(
                "Annotated-source JSON contains an unknown "
                + "AnnotatedSourceFactOrigin value."),
        };

    static string JsonName(JsonSerializerOptions options, string name)
        => AnnotatedSourceJsonConverterHelpers.JsonName(options, name);

    static bool PropertyMatches(
        string actual,
        string expected,
        JsonSerializerOptions options)
        => AnnotatedSourceJsonConverterHelpers.PropertyMatches(
            actual,
            expected,
            options);

    static void ObserveProperty(
        ref int seenProperties,
        int property,
        JsonSerializerOptions options,
        string typeName)
        => AnnotatedSourceJsonConverterHelpers.ObserveProperty(
            ref seenProperties,
            property,
            options,
            typeName);

    static void SkipUnknown(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        string typeName)
        => AnnotatedSourceJsonConverterHelpers.SkipUnknown(
            ref reader,
            options,
            typeName);
}

sealed class AnnotatedSourceTargetJsonConverter
    : JsonConverter<AnnotatedSourceTarget>
{
    public override AnnotatedSourceTarget Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        AnnotatedSourceJsonConverterHelpers.RequireObject(
            reader,
            nameof(AnnotatedSourceTarget));

        int factId = 0;
        int nodeId = 0;
        int seenProperties = 0;
        string factIdName =
            JsonName(options, nameof(AnnotatedSourceTarget.FactId));
        string nodeIdName =
            JsonName(options, nameof(AnnotatedSourceTarget.NodeId));

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName =
                AnnotatedSourceJsonConverterHelpers.ReadPropertyName(
                    ref reader,
                    nameof(AnnotatedSourceTarget));

            if (PropertyMatches(propertyName, factIdName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 0,
                    options,
                    nameof(AnnotatedSourceTarget));
                factId = reader.GetInt32();
            }
            else if (PropertyMatches(propertyName, nodeIdName, options))
            {
                ObserveProperty(
                    ref seenProperties,
                    1 << 1,
                    options,
                    nameof(AnnotatedSourceTarget));
                nodeId = reader.GetInt32();
            }
            else
            {
                SkipUnknown(
                    ref reader,
                    options,
                    nameof(AnnotatedSourceTarget));
            }
        }

        AnnotatedSourceJsonConverterHelpers.RequireEndObject(
            reader,
            nameof(AnnotatedSourceTarget));
        return new(factId, nodeId);
    }

    public override void Write(
        Utf8JsonWriter writer,
        AnnotatedSourceTarget value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(
            JsonName(options, nameof(value.FactId)),
            value.FactId);
        writer.WriteNumber(
            JsonName(options, nameof(value.NodeId)),
            value.NodeId);
        writer.WriteEndObject();
    }

    static string JsonName(JsonSerializerOptions options, string name)
        => AnnotatedSourceJsonConverterHelpers.JsonName(options, name);

    static bool PropertyMatches(
        string actual,
        string expected,
        JsonSerializerOptions options)
        => AnnotatedSourceJsonConverterHelpers.PropertyMatches(
            actual,
            expected,
            options);

    static void ObserveProperty(
        ref int seenProperties,
        int property,
        JsonSerializerOptions options,
        string typeName)
        => AnnotatedSourceJsonConverterHelpers.ObserveProperty(
            ref seenProperties,
            property,
            options,
            typeName);

    static void SkipUnknown(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        string typeName)
        => AnnotatedSourceJsonConverterHelpers.SkipUnknown(
            ref reader,
            options,
            typeName);
}

static class AnnotatedSourceJsonConverterHelpers
{
    public static string JsonName(
        JsonSerializerOptions options,
        string name)
        => options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    public static bool PropertyMatches(
        string actual,
        string expected,
        JsonSerializerOptions options)
        => string.Equals(
            actual,
            expected,
            options.PropertyNameCaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    public static void ObserveProperty(
        ref int seenProperties,
        int property,
        JsonSerializerOptions options,
        string typeName)
    {
        if (!options.AllowDuplicateProperties
            && (seenProperties & property) != 0)
        {
            throw new JsonException(
                $"{typeName} contains a duplicate property.");
        }

        seenProperties |= property;
    }

    public static void SkipUnknown(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        string typeName)
    {
        if (options.UnmappedMemberHandling
            == JsonUnmappedMemberHandling.Disallow)
        {
            throw new JsonException(
                $"{typeName} contains an unknown property.");
        }

        reader.Skip();
    }

    public static void RequireObject(
        Utf8JsonReader reader,
        string typeName)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"{typeName} must be an object.");
    }

    public static string ReadPropertyName(
        ref Utf8JsonReader reader,
        string typeName)
    {
        if (reader.TokenType != JsonTokenType.PropertyName)
            throw new JsonException($"{typeName} requires property names.");

        string? propertyName = reader.GetString();
        if (!reader.Read())
            throw new JsonException($"{typeName} is incomplete.");

        return propertyName!;
    }

    public static void RequireEndObject(
        Utf8JsonReader reader,
        string typeName)
    {
        if (reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException($"{typeName} is incomplete.");
    }
}
