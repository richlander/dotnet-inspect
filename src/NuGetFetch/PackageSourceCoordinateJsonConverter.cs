using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuGetFetch;

public sealed class PackageSourceCoordinateJsonConverter :
    JsonConverter<PackageSourceCoordinate>
{
    public override PackageSourceCoordinate Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A package coordinate must be an object.");

        string packageIdProperty = PropertyName(
            options,
            nameof(PackageSourceCoordinate.PackageId));
        string versionProperty = PropertyName(
            options,
            nameof(PackageSourceCoordinate.Version));
        string? packageId = null;
        string? version = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a package coordinate property.");

            string propertyName = reader.GetString()
                ?? throw new JsonException(
                    "A package coordinate property name is required.");
            if (!reader.Read())
                throw new JsonException("A package coordinate value is required.");

            if (propertyName == packageIdProperty)
            {
                if (packageId is not null)
                    throw new JsonException("Duplicate package ID.");
                packageId = reader.GetString();
            }
            else if (propertyName == versionProperty)
            {
                if (version is not null)
                    throw new JsonException("Duplicate package version.");
                version = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException("The package coordinate is incomplete.");

        try
        {
            return PackageSourceCoordinate.Create(
                packageId
                    ?? throw new JsonException("A package ID is required."),
                version
                    ?? throw new JsonException(
                        "A package version is required."));
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(
                "The package coordinate is invalid.",
                exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        PackageSourceCoordinate value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(
            PropertyName(options, nameof(PackageSourceCoordinate.PackageId)),
            value.PackageId);
        writer.WriteString(
            PropertyName(options, nameof(PackageSourceCoordinate.Version)),
            value.Version);
        writer.WriteEndObject();
    }

    private static string PropertyName(
        JsonSerializerOptions options,
        string name) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}
