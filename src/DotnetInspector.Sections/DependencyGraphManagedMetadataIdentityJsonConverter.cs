using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed class DependencyGraphManagedMetadataIdentityJsonConverter :
    JsonConverter<ManagedMetadataIdentity>
{
    public override ManagedMetadataIdentity Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "A dependency-graph managed metadata identity must be an object.");
        }

        string kindProperty = PropertyName(options, "Kind");
        string nameProperty = PropertyName(options, "Name");
        string versionProperty = PropertyName(options, "Version");
        string cultureProperty = PropertyName(options, "Culture");
        string publicKeyTokenProperty =
            PropertyName(options, "PublicKeyToken");
        string moduleVersionIdProperty =
            PropertyName(options, "ModuleVersionId");
        string? kind = null;
        string? name = null;
        string? version = null;
        string? culture = null;
        string? publicKeyToken = null;
        string? moduleVersionId = null;
        bool hasKind = false;
        bool hasName = false;
        bool hasVersion = false;
        bool hasCulture = false;
        bool hasPublicKeyToken = false;
        bool hasModuleVersionId = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException(
                    "Expected a dependency-graph managed metadata identity property.");
            }

            string propertyName = reader.GetString()
                ?? throw new JsonException(
                    "A dependency-graph managed metadata identity property name is required.");
            if (!reader.Read())
            {
                throw new JsonException(
                    "A dependency-graph managed metadata identity value is required.");
            }

            if (propertyName == kindProperty)
            {
                kind = ReadUniqueString(
                    ref reader,
                    ref hasKind,
                    "managed metadata identity kind");
            }
            else if (propertyName == nameProperty)
            {
                name = ReadUniqueString(
                    ref reader,
                    ref hasName,
                    "managed metadata identity name");
            }
            else if (propertyName == versionProperty)
            {
                version = ReadUniqueString(
                    ref reader,
                    ref hasVersion,
                    "assembly version");
            }
            else if (propertyName == cultureProperty)
            {
                culture = ReadUniqueString(
                    ref reader,
                    ref hasCulture,
                    "assembly culture");
            }
            else if (propertyName == publicKeyTokenProperty)
            {
                publicKeyToken = ReadUniqueString(
                    ref reader,
                    ref hasPublicKeyToken,
                    "assembly public-key token");
            }
            else if (propertyName == moduleVersionIdProperty)
            {
                moduleVersionId = ReadUniqueString(
                    ref reader,
                    ref hasModuleVersionId,
                    "module version ID");
            }
            else
            {
                reader.Skip();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException(
                "The dependency-graph managed metadata identity is incomplete.");
        }
        if (!hasKind || !hasName)
        {
            throw new JsonException(
                "A dependency-graph managed metadata identity requires kind and name.");
        }

        return kind switch
        {
            "assembly"
                when !hasModuleVersionId =>
                new ManagedMetadataIdentity.Assembly(
                    new AssemblyReferenceIdentity(
                        name!,
                        ParseVersion(version, hasVersion),
                        culture,
                        publicKeyToken)),
            "module"
                when hasModuleVersionId
                    && !hasVersion
                    && !hasCulture
                    && !hasPublicKeyToken =>
                new ManagedMetadataIdentity.Module(
                    name!,
                    ParseModuleVersionId(moduleVersionId!)),
            "assembly" or "module" => throw new JsonException(
                "The managed metadata identity fields do not match its kind."),
            _ => throw new JsonException(
                "Unknown managed metadata identity kind."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ManagedMetadataIdentity value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case ManagedMetadataIdentity.Assembly assembly:
                writer.WriteString(PropertyName(options, "Kind"), "assembly");
                writer.WriteString(
                    PropertyName(options, "Name"),
                    assembly.Identity.Name);
                WriteOptionalString(
                    writer,
                    PropertyName(options, "Version"),
                    assembly.Identity.Version?.ToString());
                WriteOptionalString(
                    writer,
                    PropertyName(options, "Culture"),
                    assembly.Identity.Culture);
                WriteOptionalString(
                    writer,
                    PropertyName(options, "PublicKeyToken"),
                    assembly.Identity.PublicKeyToken);
                break;
            case ManagedMetadataIdentity.Module module:
                writer.WriteString(PropertyName(options, "Kind"), "module");
                writer.WriteString(
                    PropertyName(options, "Name"),
                    module.ModuleName);
                writer.WriteString(
                    PropertyName(options, "ModuleVersionId"),
                    module.ModuleVersionId);
                break;
            default:
                throw new JsonException(
                    "Unknown managed metadata identity kind.");
        }
        writer.WriteEndObject();
    }

    private static Version? ParseVersion(string? value, bool present)
    {
        if (!present)
            return null;
        if (!Version.TryParse(value, out Version? version)
            || !string.Equals(
                value,
                version.ToString(),
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "An assembly version must use canonical version text.");
        }

        return version;
    }

    private static Guid ParseModuleVersionId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out Guid moduleVersionId)
            || !string.Equals(
                value,
                moduleVersionId.ToString("D"),
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "A module version ID must use canonical GUID text.");
        }

        return moduleVersionId;
    }

    private static string ReadUniqueString(
        ref Utf8JsonReader reader,
        ref bool present,
        string description)
    {
        if (present)
            throw new JsonException($"Duplicate {description}.");
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"A {description} is required.");

        present = true;
        return reader.GetString()!;
    }

    private static void WriteOptionalString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is not null)
            writer.WriteString(propertyName, value);
    }

    private static string PropertyName(
        JsonSerializerOptions options,
        string name) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}
