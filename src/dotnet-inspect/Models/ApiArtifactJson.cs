using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ILInspector.CSharp;
using ILInspector.Metadata;
using InertText;
using DotnetInspector.Output;

namespace DotnetInspector.Models;

/// <summary>
/// JSON write projections that visually encode every semantic API string
/// before the structural serializer writes it.
/// </summary>
/// <remarks>
/// <c>SemanticTypeOutputContainmentTests</c> gates decoded values and schema
/// neutrality; the metadata-confusion case in <c>PackageFixtureTests</c> is the
/// real-artifact gate.
/// </remarks>
internal static class ApiArtifactJson
{
    private static readonly ConditionalWeakTable<ApiMember, PreparedMember> PreparedMembers =
        new();
    private static readonly ConditionalWeakTable<TypeParameter, PreparedTypeParameter>
        PreparedTypeParameters = new();

    public static JsonTypeInfo<ApiSurface> Surface { get; } =
        CreateTypeInfo<ApiSurface>(ApiJsonContext.Default.Options);

    public static JsonTypeInfo<ApiType> Type { get; } =
        CreateTypeInfo<ApiType>(ApiTypeJsonContext.Default.Options);

    public static JsonTypeInfo<ApiType> CompactType { get; } =
        CreateTypeInfo<ApiType>(
            ApiTypeCompactJsonContext.Default.Options);

    public static void Prepare(ApiSurface surface)
    {
        foreach (ApiType type in surface.Types)
            Prepare(type);
    }

    public static void Prepare(ApiType type)
    {
        string[] typeParameterNames =
            [.. type.TypeParameters.Select(parameter => parameter.Name)];
        foreach (TypeParameter parameter in type.TypeParameters)
        {
            PreparedTypeParameters.Remove(parameter);
            PreparedTypeParameters.Add(
                parameter,
                new PreparedTypeParameter(typeParameterNames));
        }

        foreach (ApiMember member in type.Members)
        {
            PreparedMembers.Remove(member);
            if (member.Signature is null)
                continue;

            if (member.SignatureModel is null)
            {
                PreparedMembers.Add(
                    member,
                    new PreparedMember(
                        CSharpFormatter.ContainOpaqueCompatibilitySignature(
                            member.Signature),
                        SignatureDecodeStatus.Degraded));
                continue;
            }

            if (!RequiresStructuredPreparation(type, member))
                continue;

            var formatter = new CSharpFormatter();
            string signature = formatter.FormatCompatibilityMemberSignature(
                type,
                member);
            PreparedMembers.Add(
                member,
                new PreparedMember(
                    signature,
                    member.SignatureDecodeStatus));
        }
    }

    private static bool RequiresStructuredPreparation(
        ApiType type,
        ApiMember member)
    {
        // Whole signatures already contain C# literal escapes, so re-importing
        // them as raw text would double valid syntax. Recompose only when a raw
        // metadata slot carries a literal backslash that must be distinguished
        // from a generated visual escape; benign signatures stay byte-neutral.
        if (member.Name.Contains('\\'))
            return true;

        if (member.Kind is "constructor" or "finalizer"
            && (type.DefinitionName?.Segments.Any(
                    static segment => segment.Contains('\\'))
                ?? type.Name.Contains('\\')))
        {
            return true;
        }

        if (member.SignatureModel is not { } signature)
            return false;

        return RequiresKeywordGenericPreparation(type, signature)
            || ContainsLiteralBackslash(signature.ReturnType)
            || signature.Parameters.Any(
                static parameter =>
                    ContainsLiteralBackslash(parameter.Type)
                    || ContainsLiteralBackslash(parameter.Name))
            || signature.TypeParameters.Any(
                static parameter =>
                    ContainsLiteralBackslash(parameter.Name)
                    || parameter.Constraints.Any(
                        ContainsLiteralBackslash));

        static bool ContainsLiteralBackslash(string? value)
            => value?.Contains('\\') == true;
    }

    private static bool RequiresKeywordGenericPreparation(
        ApiType type,
        ApiSignature signature)
    {
        string[] parameterNames =
        [
            .. type.TypeParameters.Select(parameter => parameter.Name),
            .. signature.TypeParameters.Select(parameter => parameter.Name),
        ];
        if (!parameterNames.Any(
                name => CSharpFormatter.EscapeIdentifier(name) != name))
        {
            return false;
        }

        if (signature.TypeParameters.Any(
                parameter =>
                    CSharpFormatter.EscapeIdentifier(parameter.Name)
                        != parameter.Name))
        {
            return true;
        }

        return RawTypeRequiresEscape(signature.ReturnType)
            || signature.Parameters.Any(
                parameter => RawTypeRequiresEscape(parameter.Type))
            || signature.TypeParameters.Any(
                parameter => parameter.Constraints.Any(
                    RawTypeRequiresEscape));

        bool RawTypeRequiresEscape(string? value)
            => value is not null
                && CSharpFormatter.RawTypeRequiresKnownIdentifierEscape(
                    value,
                    parameterNames);
    }

    private static JsonTypeInfo<T> CreateTypeInfo<T>(
        JsonSerializerOptions baseline)
    {
        JsonSerializerOptions options = CreateOptions(baseline);
        return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }

    private static JsonSerializerOptions CreateOptions(
        JsonSerializerOptions baseline)
    {
        var options = new JsonSerializerOptions(baseline);
        options.Converters.Insert(
            0,
            ApiArtifactMetadataTypeNameJsonConverter.Instance);
        options.Converters.Insert(0, ApiArtifactStringJsonConverter.Instance);
        options.TypeInfoResolver = options.TypeInfoResolver!
            .WithAddedModifier(ConfigurePropertyConverters);
        return options;
    }

    private static void ConfigurePropertyConverters(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(DocComment))
        {
            SetEncodedStrings(typeInfo, "summary", "remarks", "returns");
        }
        else if (typeInfo.Type == typeof(TypeParameter))
        {
            RemoveProperty(typeInfo, "structured_constraints");
            RemoveProperty(typeInfo, "type_kind");
            SetCSharpStrings(
                typeInfo,
                "display_name");
            SetPreparedTypeParameterConstraints(typeInfo);
        }
        else if (typeInfo.Type == typeof(ApiType))
        {
            SetRawTypeStrings(
                typeInfo,
                "enum_underlying_type",
                "base_type");
            SetCSharpStringLists(typeInfo, "attributes");
            SetRawTypeStringLists(
                typeInfo,
                "interfaces",
                "derived_types");
        }
        else if (typeInfo.Type == typeof(ApiMember))
        {
            RemoveProperty(typeInfo, "signature_model");
            SetRawTypeStrings(
                typeInfo,
                "return_type",
                "extended_type",
                "declaring_type");
            SetPreparedMemberSignature(typeInfo);
            SetPreparedMemberDecodeStatus(typeInfo);
            SetCSharpStringLists(typeInfo, "attributes");
        }
        else if (typeInfo.Type == typeof(ApiSignature))
        {
            SetRawTypeStrings(
                typeInfo,
                "return_type",
                "canonical_return_type",
                "parameter_types_summary",
                "canonical_parameter_types_summary",
                "effective_canonical_return_type",
                "public_accessors_summary");
            SetCSharpStringLists(typeInfo, "return_attributes");
        }
        else if (typeInfo.Type == typeof(ApiParameter))
        {
            SetRawTypeStrings(
                typeInfo,
                "type",
                "canonical_type",
                "type_with_modifier",
                "effective_canonical_type",
                "canonical_type_with_modifier");
            SetCSharpStrings(typeInfo, "default_value_text");
            SetCSharpStringLists(typeInfo, "attributes");
        }
        else if (typeInfo.Type == typeof(ApiAccessor))
        {
            SetCSharpStringLists(typeInfo, "return_attributes");
        }
    }

    private static void RemoveProperty(
        JsonTypeInfo typeInfo,
        string propertyName)
    {
        JsonPropertyInfo property = typeInfo.Properties.Single(
            property => WireNamesEqual(
                property.Name,
                propertyName));
        typeInfo.Properties.Remove(property);
    }

    private static void SetCSharpStrings(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactCSharpStringJsonConverter.Instance);
        }
    }

    private static void SetRawTypeStrings(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactRawTypeStringJsonConverter.Instance);
        }
    }

    private static void SetEncodedStrings(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactEncodedStringJsonConverter.Instance);
        }
    }

    private static void SetCSharpStringLists(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactCSharpStringListJsonConverter.Instance);
        }
    }

    private static void SetRawTypeStringLists(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactRawTypeStringListJsonConverter.Instance);
        }
    }

    private static void SetPreparedMemberSignature(JsonTypeInfo typeInfo)
    {
        JsonPropertyInfo property = typeInfo.Properties.Single(
            property => WireNamesEqual(
                property.Name,
                "signature"));
        property.Get = value =>
        {
            var member = (ApiMember)value;
            return PreparedMembers.TryGetValue(
                member,
                out PreparedMember? prepared)
                    ? prepared.Signature
                    : member.Signature;
        };
        property.CustomConverter =
            ApiArtifactCSharpStringJsonConverter.Instance;
    }

    private static void SetPreparedMemberDecodeStatus(JsonTypeInfo typeInfo)
    {
        JsonPropertyInfo property = typeInfo.Properties.Single(
            property => WireNamesEqual(
                property.Name,
                "signature_decode_status"));
        property.Get = value =>
        {
            var member = (ApiMember)value;
            return PreparedMembers.TryGetValue(
                member,
                out PreparedMember? prepared)
                    ? prepared.DecodeStatus
                    : member.SignatureDecodeStatus;
        };
    }

    private static void SetPreparedTypeParameterConstraints(
        JsonTypeInfo typeInfo)
    {
        JsonPropertyInfo constraints = typeInfo.Properties.Single(
            property => WireNamesEqual(
                property.Name,
                "constraints"));
        constraints.Get = value =>
            PreparedConstraintEntries((TypeParameter)value).ToList();
        constraints.CustomConverter =
            ApiArtifactCSharpStringListJsonConverter.Instance;

        JsonPropertyInfo summary = typeInfo.Properties.Single(
            property => WireNamesEqual(
                property.Name,
                "constraints_summary"));
        summary.Get = value =>
        {
            IReadOnlyList<string> entries =
                PreparedConstraintEntries((TypeParameter)value);
            return entries.Count == 0
                ? null
                : string.Join(", ", entries);
        };
        summary.CustomConverter =
            ApiArtifactCSharpStringJsonConverter.Instance;
    }

    private static IReadOnlyList<string> PreparedConstraintEntries(
        TypeParameter parameter) =>
        CSharpFormatter.FormatTypeParameterConstraintEntries(
            parameter,
            PreparedTypeParameters.TryGetValue(
                parameter,
                out PreparedTypeParameter? prepared)
                    ? prepared.ParameterNames
                    : []);

    private static void SetConverter(
        JsonTypeInfo typeInfo,
        string propertyName,
        JsonConverter converter)
    {
        JsonPropertyInfo property = typeInfo.Properties.Single(
            property => WireNamesEqual(
                property.Name,
                propertyName));
        property.CustomConverter = converter;
    }

    private static bool WireNamesEqual(
        string left,
        string right)
    {
        int leftIndex = 0;
        int rightIndex = 0;
        while (true)
        {
            while (leftIndex < left.Length && left[leftIndex] == '_')
                leftIndex++;
            while (rightIndex < right.Length && right[rightIndex] == '_')
                rightIndex++;

            if (leftIndex == left.Length || rightIndex == right.Length)
            {
                return leftIndex == left.Length
                    && rightIndex == right.Length;
            }

            if (char.ToUpperInvariant(left[leftIndex])
                != char.ToUpperInvariant(right[rightIndex]))
            {
                return false;
            }

            leftIndex++;
            rightIndex++;
        }
    }

    private sealed record PreparedMember(
        string Signature,
        SignatureDecodeStatus? DecodeStatus);

    private sealed record PreparedTypeParameter(
        IReadOnlyList<string> ParameterNames);
}

/// <summary>
/// Output-only conversion from untreated API model strings to inert JSON
/// string values.
/// </summary>
internal sealed class ApiArtifactStringJsonConverter : JsonConverter<string>
{
    public static ApiArtifactStringJsonConverter Instance { get; } = new();

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(
            new InertString(TextPolicy.Field, value).ToString());
}

internal sealed class ApiArtifactCSharpStringJsonConverter
    : JsonConverter<string>
{
    public static ApiArtifactCSharpStringJsonConverter Instance { get; } =
        new();

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(Contain(value));

    internal static string Contain(string value)
        => ApiPresentationText.CSharpField(value).ToString();
}

internal sealed class ApiArtifactRawTypeStringJsonConverter
    : JsonConverter<string>
{
    public static ApiArtifactRawTypeStringJsonConverter Instance { get; } =
        new();

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(Contain(value));

    internal static string Contain(string value)
        => ApiPresentationText.RawTypeField(value).ToString();
}

internal sealed class ApiArtifactEncodedStringJsonConverter
    : JsonConverter<string>
{
    public static ApiArtifactEncodedStringJsonConverter Instance { get; } =
        new();

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(
            ApiPresentationText.EncodedField(value).ToString());
}

internal sealed class ApiArtifactCSharpStringListJsonConverter
    : JsonConverter<List<string>>
{
    public static ApiArtifactCSharpStringListJsonConverter Instance { get; } =
        new();

    public override List<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string? item in value)
        {
            if (item is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(
                    ApiArtifactCSharpStringJsonConverter.Contain(item));
        }
        writer.WriteEndArray();
    }
}

internal sealed class ApiArtifactRawTypeStringListJsonConverter
    : JsonConverter<List<string>>
{
    public static ApiArtifactRawTypeStringListJsonConverter Instance { get; } =
        new();

    public override List<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string? item in value)
        {
            if (item is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(
                    ApiArtifactRawTypeStringJsonConverter.Contain(item));
        }
        writer.WriteEndArray();
    }
}

internal sealed class ApiArtifactMetadataTypeNameJsonConverter
    : JsonConverter<MetadataTypeDefinitionName>
{
    public static ApiArtifactMetadataTypeNameJsonConverter Instance { get; } =
        new();

    public override MetadataTypeDefinitionName Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        MetadataTypeDefinitionName value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "namespace",
            new InertString(TextPolicy.Field, value.Namespace).ToString());
        writer.WriteStartArray("segments");
        foreach (string segment in value.Segments)
        {
            writer.WriteStringValue(
                new InertString(TextPolicy.Field, segment).ToString());
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
