using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Queries;

internal abstract class TwoInt32PropertyJsonConverter<T> : JsonConverter<T>
{
    protected abstract string FirstPropertyName { get; }
    protected abstract string SecondPropertyName { get; }

    protected abstract T Create(int first, int second);
    protected abstract int FirstValue(T value);
    protected abstract int SecondValue(T value);

    public sealed override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"{typeof(T).Name} must be an object.");

        int first = 0;
        int second = 0;
        int seenProperties = 0;
        string firstName = JsonName(options, FirstPropertyName);
        string secondName = JsonName(options, SecondPropertyName);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException(
                    $"{typeof(T).Name} requires property names.");
            }

            string propertyName = reader.GetString()!;
            if (!reader.Read())
                throw new JsonException($"{typeof(T).Name} is incomplete.");

            if (PropertyMatches(propertyName, firstName, options))
            {
                ObserveProperty(ref seenProperties, 1 << 0, options);
                first = reader.GetInt32();
            }
            else if (PropertyMatches(propertyName, secondName, options))
            {
                ObserveProperty(ref seenProperties, 1 << 1, options);
                second = reader.GetInt32();
            }
            else if (options.UnmappedMemberHandling
                == JsonUnmappedMemberHandling.Disallow)
            {
                throw new JsonException(
                    $"{typeof(T).Name} contains an unknown property.");
            }
            else
            {
                reader.Skip();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException($"{typeof(T).Name} is incomplete.");

        return Create(first, second);
    }

    public sealed override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(
            JsonName(options, FirstPropertyName),
            FirstValue(value));
        writer.WriteNumber(
            JsonName(options, SecondPropertyName),
            SecondValue(value));
        writer.WriteEndObject();
    }

    private static string JsonName(
        JsonSerializerOptions options,
        string name) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    private static bool PropertyMatches(
        string actual,
        string expected,
        JsonSerializerOptions options) =>
        string.Equals(
            actual,
            expected,
            options.PropertyNameCaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static void ObserveProperty(
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
}
