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
        string startName = JsonName(options, nameof(AnnotatedSourceSpan.Start));
        string lengthName = JsonName(options, nameof(AnnotatedSourceSpan.Length));

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName =
                AnnotatedSourceJsonConverterHelpers.ReadPropertyName(
                    ref reader,
                    nameof(AnnotatedSourceSpan));

            if (propertyName == startName)
                start = reader.GetInt32();
            else if (propertyName == lengthName)
                length = reader.GetInt32();
            else
                reader.Skip();
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

            if (propertyName == idName)
                id = reader.GetInt32();
            else if (propertyName == descriptorName)
                descriptor = reader.GetString()!;
            else if (propertyName == categoryName)
                category = reader.GetString()!;
            else if (propertyName == conditionalityName)
                conditionality = ReadConditionality(ref reader);
            else if (propertyName == detailName)
                detail = reader.GetString();
            else if (propertyName == sourceOffsetName)
                sourceOffset = reader.GetInt32();
            else if (propertyName == originName)
                origin = ReadOrigin(ref reader);
            else
                reader.Skip();
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

            if (propertyName == factIdName)
                factId = reader.GetInt32();
            else if (propertyName == nodeIdName)
                nodeId = reader.GetInt32();
            else
                reader.Skip();
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
}

static class AnnotatedSourceJsonConverterHelpers
{
    public static string JsonName(
        JsonSerializerOptions options,
        string name)
        => options.PropertyNamingPolicy?.ConvertName(name) ?? name;

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
