using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Queries;

public sealed class AuthoredProjectTargetFrameworkIdentityJsonConverter :
    JsonConverter<AuthoredProjectTargetFrameworkIdentity>
{
    public override AuthoredProjectTargetFrameworkIdentity Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "An authored target-framework identity must be an object.");
        }

        string kindProperty = PropertyName(options, "Kind");
        string canonicalFrameworkProperty =
            PropertyName(options, "CanonicalFramework");
        string comparisonIdentityProperty =
            PropertyName(options, "ComparisonIdentity");
        AuthoredProjectTargetFrameworkKind kind = default;
        bool hasKind = false;
        string? canonicalFramework = null;
        string? comparisonIdentity = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException(
                    "Expected an authored target-framework identity property.");
            }

            string propertyName = reader.GetString()
                ?? throw new JsonException(
                    "An authored target-framework identity property name is required.");
            if (!reader.Read())
            {
                throw new JsonException(
                    "An authored target-framework identity value is required.");
            }

            if (propertyName == kindProperty)
            {
                if (hasKind)
                {
                    throw new JsonException(
                        "Duplicate authored target-framework kind.");
                }
                kind = ReadKind(ref reader);
                hasKind = true;
            }
            else if (propertyName == canonicalFrameworkProperty)
            {
                canonicalFramework = ReadUniqueString(
                    ref reader,
                    canonicalFramework,
                    "canonical authored target framework");
            }
            else if (propertyName == comparisonIdentityProperty)
            {
                comparisonIdentity = ReadUniqueString(
                    ref reader,
                    comparisonIdentity,
                    "authored target-framework comparison identity");
            }
            else
            {
                reader.Skip();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException(
                "The authored target-framework identity is incomplete.");
        }
        if (!hasKind)
        {
            throw new JsonException(
                "An authored target-framework kind is required.");
        }

        try
        {
            return kind switch
            {
                AuthoredProjectTargetFrameworkKind.Exact
                    when canonicalFramework is not null
                        && comparisonIdentity is null =>
                    AuthoredProjectTargetFrameworkIdentity.Exact(
                        canonicalFramework),
                AuthoredProjectTargetFrameworkKind.Unrecognized
                    when canonicalFramework is null
                        && comparisonIdentity is not null =>
                    AuthoredProjectTargetFrameworkIdentity.FromOpaqueIdentity(
                        kind,
                        comparisonIdentity),
                AuthoredProjectTargetFrameworkKind.Unresolved
                    when canonicalFramework is null
                        && comparisonIdentity is not null =>
                    AuthoredProjectTargetFrameworkIdentity.FromOpaqueIdentity(
                        kind,
                        comparisonIdentity),
                _ => throw new JsonException(
                    "The authored target-framework fields do not match its kind."),
            };
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(
                "The authored target-framework identity is invalid.",
                exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        AuthoredProjectTargetFrameworkIdentity value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(
            PropertyName(options, "Kind"),
            KindName(value.Kind));
        if (value.CanonicalFramework is not null)
        {
            writer.WriteString(
                PropertyName(options, "CanonicalFramework"),
                value.CanonicalFramework);
        }
        else
        {
            writer.WriteString(
                PropertyName(options, "ComparisonIdentity"),
                value.ComparisonIdentity);
        }
        writer.WriteEndObject();
    }

    private static string ReadUniqueString(
        ref Utf8JsonReader reader,
        string? current,
        string description)
    {
        if (current is not null)
            throw new JsonException($"Duplicate {description}.");

        return reader.GetString()
            ?? throw new JsonException($"A {description} is required.");
    }

    private static AuthoredProjectTargetFrameworkKind ReadKind(
        ref Utf8JsonReader reader) =>
        reader.GetString() switch
        {
            nameof(AuthoredProjectTargetFrameworkKind.Exact) =>
                AuthoredProjectTargetFrameworkKind.Exact,
            nameof(AuthoredProjectTargetFrameworkKind.Unrecognized) =>
                AuthoredProjectTargetFrameworkKind.Unrecognized,
            nameof(AuthoredProjectTargetFrameworkKind.Unresolved) =>
                AuthoredProjectTargetFrameworkKind.Unresolved,
            _ => throw new JsonException(
                "Unknown authored target-framework kind."),
        };

    private static string KindName(AuthoredProjectTargetFrameworkKind kind) =>
        kind switch
        {
            AuthoredProjectTargetFrameworkKind.Exact =>
                nameof(AuthoredProjectTargetFrameworkKind.Exact),
            AuthoredProjectTargetFrameworkKind.Unrecognized =>
                nameof(AuthoredProjectTargetFrameworkKind.Unrecognized),
            AuthoredProjectTargetFrameworkKind.Unresolved =>
                nameof(AuthoredProjectTargetFrameworkKind.Unresolved),
            _ => throw new JsonException(
                "Unknown authored target-framework kind."),
        };

    private static string PropertyName(
        JsonSerializerOptions options,
        string name) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}
