using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotnetInspector.Queries;

internal abstract class OnePropertyJsonConverter<T, TFirst> : JsonConverter<T>
{
    protected abstract string FirstPropertyName { get; }

    protected abstract T Create(TFirst first);
    protected abstract TFirst FirstValue(T value);

    public sealed override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        JsonObjectValueConverterHelpers.RequireObject<T>(reader);

        TFirst first = default!;
        int seenProperties = 0;
        string firstName = JsonObjectValueConverterHelpers.JsonName(
            options,
            FirstPropertyName);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName =
                JsonObjectValueConverterHelpers.ReadPropertyName<T>(
                    ref reader);
            if (JsonObjectValueConverterHelpers.PropertyMatches(
                propertyName,
                firstName,
                options))
            {
                JsonObjectValueConverterHelpers.ObserveProperty<T>(
                    ref seenProperties,
                    1 << 0,
                    options);
                first = JsonObjectValueConverterHelpers.ReadValue<TFirst>(
                    ref reader,
                    options);
            }
            else
            {
                JsonObjectValueConverterHelpers.SkipUnknown<T>(
                    ref reader,
                    options);
            }
        }

        JsonObjectValueConverterHelpers.RequireEndObject<T>(reader);
        return JsonObjectValueConverterHelpers.Create(
            () => Create(first));
    }

    public sealed override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(
            JsonObjectValueConverterHelpers.JsonName(
                options,
                FirstPropertyName));
        JsonObjectValueConverterHelpers.WriteValue(
            writer,
            FirstValue(value),
            options);
        writer.WriteEndObject();
    }
}

internal abstract class TwoPropertyJsonConverter<T, TFirst, TSecond> :
    JsonConverter<T>
{
    protected abstract string FirstPropertyName { get; }
    protected abstract string SecondPropertyName { get; }

    protected abstract T Create(TFirst first, TSecond second);
    protected abstract TFirst FirstValue(T value);
    protected abstract TSecond SecondValue(T value);

    public sealed override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        JsonObjectValueConverterHelpers.RequireObject<T>(reader);

        TFirst first = default!;
        TSecond second = default!;
        int seenProperties = 0;
        string firstName = JsonObjectValueConverterHelpers.JsonName(
            options,
            FirstPropertyName);
        string secondName = JsonObjectValueConverterHelpers.JsonName(
            options,
            SecondPropertyName);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName =
                JsonObjectValueConverterHelpers.ReadPropertyName<T>(
                    ref reader);
            if (JsonObjectValueConverterHelpers.PropertyMatches(
                propertyName,
                firstName,
                options))
            {
                JsonObjectValueConverterHelpers.ObserveProperty<T>(
                    ref seenProperties,
                    1 << 0,
                    options);
                first = JsonObjectValueConverterHelpers.ReadValue<TFirst>(
                    ref reader,
                    options);
            }
            else if (JsonObjectValueConverterHelpers.PropertyMatches(
                propertyName,
                secondName,
                options))
            {
                JsonObjectValueConverterHelpers.ObserveProperty<T>(
                    ref seenProperties,
                    1 << 1,
                    options);
                second = JsonObjectValueConverterHelpers.ReadValue<TSecond>(
                    ref reader,
                    options);
            }
            else
            {
                JsonObjectValueConverterHelpers.SkipUnknown<T>(
                    ref reader,
                    options);
            }
        }

        JsonObjectValueConverterHelpers.RequireEndObject<T>(reader);
        return JsonObjectValueConverterHelpers.Create(
            () => Create(first, second));
    }

    public sealed override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(
            JsonObjectValueConverterHelpers.JsonName(
                options,
                FirstPropertyName));
        JsonObjectValueConverterHelpers.WriteValue(
            writer,
            FirstValue(value),
            options);
        writer.WritePropertyName(
            JsonObjectValueConverterHelpers.JsonName(
                options,
                SecondPropertyName));
        JsonObjectValueConverterHelpers.WriteValue(
            writer,
            SecondValue(value),
            options);
        writer.WriteEndObject();
    }
}

internal static class JsonObjectValueConverterHelpers
{
    internal static T Create<T>(Func<T> create)
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(
                $"{typeof(T).Name} contains invalid values.",
                exception);
        }
    }

    internal static string JsonName(
        JsonSerializerOptions options,
        string name) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    internal static bool PropertyMatches(
        string actual,
        string expected,
        JsonSerializerOptions options) =>
        string.Equals(
            actual,
            expected,
            options.PropertyNameCaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    internal static void ObserveProperty<T>(
        ref int seenProperties,
        int property,
        JsonSerializerOptions options)
    {
        if (!options.AllowDuplicateProperties
            && (seenProperties & property) != 0)
        {
            throw new JsonException(
                $"{typeof(T).Name} contains a duplicate property.");
        }

        seenProperties |= property;
    }

    internal static void SkipUnknown<T>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options)
    {
        if (options.UnmappedMemberHandling
            == JsonUnmappedMemberHandling.Disallow)
        {
            throw new JsonException(
                $"{typeof(T).Name} contains an unknown property.");
        }

        reader.Skip();
    }

    internal static void RequireObject<T>(Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"{typeof(T).Name} must be an object.");
    }

    internal static string ReadPropertyName<T>(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            throw new JsonException(
                $"{typeof(T).Name} requires property names.");
        }

        string? propertyName = reader.GetString();
        if (!reader.Read())
            throw new JsonException($"{typeof(T).Name} is incomplete.");

        return propertyName!;
    }

    internal static void RequireEndObject<T>(Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException($"{typeof(T).Name} is incomplete.");
    }

    internal static TValue ReadValue<TValue>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options)
    {
        var typeInfo = (JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue));
        return JsonSerializer.Deserialize(ref reader, typeInfo)!;
    }

    internal static void WriteValue<TValue>(
        Utf8JsonWriter writer,
        TValue value,
        JsonSerializerOptions options)
    {
        var typeInfo = (JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue));
        JsonSerializer.Serialize(writer, value, typeInfo);
    }
}
