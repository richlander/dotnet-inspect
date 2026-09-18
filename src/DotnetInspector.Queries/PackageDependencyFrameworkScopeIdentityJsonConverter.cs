using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;

namespace DotnetInspector.Queries;

public sealed class PackageDependencyFrameworkScopeIdentityJsonConverter :
    JsonConverter<PackageDependencyFrameworkScopeIdentity>
{
    public override PackageDependencyFrameworkScopeIdentity Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A framework scope must be an object.");

        string kindProperty = PropertyName(options, "Kind");
        string canonicalFrameworkProperty =
            PropertyName(options, "CanonicalFramework");
        string opaqueIdentityProperty = PropertyName(options, "OpaqueIdentity");
        string sourceSpellingProperty = PropertyName(options, "SourceSpelling");
        PackageDependencyFrameworkScopeKind kind = default;
        bool hasKind = false;
        string? canonicalFramework = null;
        string? opaqueIdentity = null;
        string? sourceSpelling = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a framework-scope property.");

            string propertyName = reader.GetString()
                ?? throw new JsonException(
                    "A framework-scope property name is required.");
            if (!reader.Read())
                throw new JsonException("A framework-scope value is required.");

            if (propertyName == kindProperty)
            {
                if (hasKind)
                    throw new JsonException("Duplicate framework-scope kind.");
                kind = ReadKind(ref reader);
                hasKind = true;
            }
            else if (propertyName == canonicalFrameworkProperty)
            {
                canonicalFramework = ReadUniqueString(
                    ref reader,
                    canonicalFramework,
                    "canonical framework");
            }
            else if (propertyName == opaqueIdentityProperty)
            {
                opaqueIdentity = ReadUniqueString(
                    ref reader,
                    opaqueIdentity,
                    "opaque framework identity");
            }
            else if (propertyName == sourceSpellingProperty)
            {
                sourceSpelling = ReadUniqueString(
                    ref reader,
                    sourceSpelling,
                    "framework source spelling");
            }
            else
            {
                reader.Skip();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException("The framework scope is incomplete.");
        if (!hasKind)
            throw new JsonException("A framework-scope kind is required.");

        var inertSourceSpelling = InertString.FromEncoded(
            TextPolicy.Field,
            sourceSpelling
                ?? throw new JsonException(
                    "A framework source spelling is required."));
        return kind switch
        {
            PackageDependencyFrameworkScopeKind.AnyFramework
                when canonicalFramework is null && opaqueIdentity is null =>
                PackageDependencyFrameworkScopeIdentity.Any(
                    inertSourceSpelling),
            PackageDependencyFrameworkScopeKind.ExactFramework
                when canonicalFramework is not null
                    && opaqueIdentity is null =>
                Exact(
                    canonicalFramework,
                    inertSourceSpelling),
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework
                when canonicalFramework is null
                    && opaqueIdentity is not null =>
                Unrecognized(
                    opaqueIdentity,
                    inertSourceSpelling),
            PackageDependencyFrameworkScopeKind.UnresolvedFramework
                when canonicalFramework is null
                    && opaqueIdentity is not null =>
                Unresolved(
                    opaqueIdentity,
                    inertSourceSpelling),
            _ => throw new JsonException(
                "The framework-scope fields do not match its kind."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        PackageDependencyFrameworkScopeIdentity value,
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
        if (value.OpaqueIdentity is not null)
        {
            writer.WriteString(
                PropertyName(options, "OpaqueIdentity"),
                value.OpaqueIdentity);
        }
        writer.WriteString(
            PropertyName(options, "SourceSpelling"),
            value.SourceSpelling.ToString());
        writer.WriteEndObject();
    }

    private static PackageDependencyFrameworkScopeIdentity Exact(
        string framework,
        InertString sourceSpelling)
    {
        if (!NuGetTargetFrameworkIdentity.TryNormalize(
                framework,
                out string canonical)
            || !string.Equals(
                framework,
                canonical,
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "An exact framework scope requires canonical framework text.");
        }

        return PackageDependencyFrameworkScopeIdentity.Exact(
            framework,
            sourceSpelling);
    }

    private static PackageDependencyFrameworkScopeIdentity Unrecognized(
        string opaqueIdentity,
        InertString sourceSpelling)
    {
        RequireOpaqueIdentity(opaqueIdentity);
        return PackageDependencyFrameworkScopeIdentity.Unrecognized(
            opaqueIdentity,
            sourceSpelling);
    }

    private static PackageDependencyFrameworkScopeIdentity Unresolved(
        string opaqueIdentity,
        InertString sourceSpelling)
    {
        RequireOpaqueIdentity(opaqueIdentity);
        return PackageDependencyFrameworkScopeIdentity.Unresolved(
            opaqueIdentity,
            sourceSpelling);
    }

    private static void RequireOpaqueIdentity(string opaqueIdentity)
    {
        if (!RestoredProjectIdentityText.IsOpaque(opaqueIdentity))
        {
            throw new JsonException(
                "An opaque framework scope requires a sha256-prefixed lowercase SHA-256 identity.");
        }
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

    private static string PropertyName(
        JsonSerializerOptions options,
        string name) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    private static PackageDependencyFrameworkScopeKind ReadKind(
        ref Utf8JsonReader reader) =>
        reader.GetString() switch
        {
            nameof(PackageDependencyFrameworkScopeKind.AnyFramework) =>
                PackageDependencyFrameworkScopeKind.AnyFramework,
            nameof(PackageDependencyFrameworkScopeKind.ExactFramework) =>
                PackageDependencyFrameworkScopeKind.ExactFramework,
            nameof(PackageDependencyFrameworkScopeKind.UnrecognizedFramework) =>
                PackageDependencyFrameworkScopeKind.UnrecognizedFramework,
            nameof(PackageDependencyFrameworkScopeKind.UnresolvedFramework) =>
                PackageDependencyFrameworkScopeKind.UnresolvedFramework,
            _ => throw new JsonException("Unknown framework-scope kind."),
        };

    private static string KindName(
        PackageDependencyFrameworkScopeKind kind) =>
        kind switch
        {
            PackageDependencyFrameworkScopeKind.AnyFramework =>
                nameof(PackageDependencyFrameworkScopeKind.AnyFramework),
            PackageDependencyFrameworkScopeKind.ExactFramework =>
                nameof(PackageDependencyFrameworkScopeKind.ExactFramework),
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework =>
                nameof(PackageDependencyFrameworkScopeKind
                    .UnrecognizedFramework),
            PackageDependencyFrameworkScopeKind.UnresolvedFramework =>
                nameof(PackageDependencyFrameworkScopeKind.UnresolvedFramework),
            _ => throw new JsonException("Unknown framework-scope kind."),
        };
}
