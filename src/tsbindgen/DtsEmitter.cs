using System.Text;
using System.Text.RegularExpressions;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

static partial class DtsEmitter
{
    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TsIdentifierRegex();

    public static string Emit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        TsBindGenDiagnostics? diagnostics = null)
    {
        ApiType[] declarationTypes =
            [.. surface.Records, .. surface.Enums];
        ValidateTypeNames(declarationTypes);
        ValidatePropertyNames(declarationTypes);

        var knownTypeNames = new HashSet<string>(
            surface.Records.Select(r => r.Name).Concat(surface.Enums.Select(e => e.Name)),
            StringComparer.Ordinal);

        var sb = new StringBuilder();

        foreach (ApiType enumType in surface.Enums.OrderBy(e => e.Name, StringComparer.Ordinal))
            EmitEnum(sb, enumType);

        foreach (ApiType record in surface.Records.OrderBy(r => r.Name, StringComparer.Ordinal))
            EmitRecord(sb, record, knownTypeNames, diagnostics);

        sb.Append(
            "export declare function initializeEngine(onStatus?: (status: string) => void): Promise<unknown>;\n");

        foreach (JsExportFunction function in surface.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
            EmitFunction(sb, function, knownTypeNames, diagnostics);

        return sb.ToString();
    }

    static void EmitEnum(StringBuilder sb, ApiType enumType)
    {
        if (!enumType.HasJsonStringEnumConverter)
        {
            sb.Append("export type ").Append(enumType.Name).Append(" = number;\n\n");
            return;
        }

        if (enumType.IsFlagsEnum)
        {
            sb.Append("export type ").Append(enumType.Name).Append(" = string;\n\n");
            return;
        }

        IEnumerable<string> memberNames = enumType.Members.Where(m => m.Kind == "field" && m.IsConst).Select(m => m.Name);
        string union = string.Join(" | ", memberNames.Select(n => $"\"{n}\""));
        sb.Append("export type ").Append(enumType.Name).Append(" = ").Append(union).Append(";\n\n");
    }

    static void EmitRecord(
        StringBuilder sb,
        ApiType record,
        IReadOnlySet<string> knownTypeNames,
        TsBindGenDiagnostics? diagnostics)
    {
        JsonWireNamingPolicy namingPolicy = record.JsonPropertyNamingPolicy ?? JsonWireNamingPolicy.None;
        if (namingPolicy == JsonWireNamingPolicy.Unsupported)
        {
            diagnostics?.ReportUnmappedType(
                $"{record.Name} JsonSerializerContext.PropertyNamingPolicy",
                "unsupported JsonKnownNamingPolicy");
            EmitBlockedRecord(sb, record);
            return;
        }

        var members = record.Members
            .Where(JsonWireMemberRules.IsSerialized)
            .Select(member => (
                Member: member,
                ResolvedName: member.JsonPropertyName ?? ApplyNamingPolicy(member.Name, namingPolicy)))
            .ToArray();

        sb.Append("export interface ").Append(record.Name).Append(" {\n");

        foreach ((ApiMember member, string resolvedName) in members)
        {
            string tsName = FormatPropertyKey(resolvedName);
            string propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType ?? "unknown";
            string tsType = TsTypeMapper.MapParameterType(propertyType, knownTypeNames, diagnostics, $"{record.Name}.{member.Name}");
            sb.Append("  ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void ValidatePropertyNames(IEnumerable<ApiType> types)
    {
        foreach (ApiType type in types)
        {
            foreach (ApiMember member in type.Members)
            {
                ValidatePropertyNameAttributes(
                    $"{type.Name}.{member.Name} [JsonPropertyName]",
                    member.JsonPropertyNameAttributeValues,
                    member.JsonPropertyName);
            }

            foreach (FilteredJsonPropertyNameFact fact
                in type.FilteredJsonPropertyNameFacts)
            {
                ValidatePropertyNameAttributes(
                    $"{type.Name}.{FormatFilteredPropertyNameLocation(fact)}",
                    fact.PropertyNames,
                    legacyPropertyName: null);
            }
        }
    }

    static void ValidateTypeNames(IEnumerable<ApiType> types)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ApiType type in types)
        {
            if (!TsIdentifierRegex().IsMatch(type.Name))
            {
                throw new UnsupportedWireContractException(
                    $"{type.FullName} type",
                    "TypeScript declaration names must be identifiers");
            }

            if (!names.Add(type.Name))
            {
                throw new UnsupportedWireContractException(
                    $"{type.Name} type",
                    "multiple JSON types project to the same TypeScript declaration name");
            }
        }
    }

    static void ValidatePropertyNameAttributes(
        string location,
        IReadOnlyList<string?> propertyNames,
        string? legacyPropertyName)
    {
        if (propertyNames.Count == 0)
        {
            if (legacyPropertyName is not null)
                ValidatePropertyName(location, legacyPropertyName);
            return;
        }

        if (propertyNames.Count != 1 || propertyNames[0] is not { } propertyName)
        {
            throw new UnsupportedWireContractException(
                location,
                "duplicate or malformed JsonPropertyName attributes are not supported");
        }

        ValidatePropertyName(location, propertyName);
    }

    static void ValidatePropertyName(string location, string propertyName)
    {
        if (propertyName.Any(char.IsControl))
        {
            throw new UnsupportedWireContractException(
                location,
                "control-character JSON property names are not supported");
        }
    }

    static string FormatFilteredPropertyNameLocation(
        FilteredJsonPropertyNameFact fact) =>
        fact.Kind switch
        {
            FilteredJsonPropertyNameKind.AutoPropertyBackingField
                or FilteredJsonPropertyNameKind.EventBackingField =>
                $"{fact.AssociatedMemberName} [field: JsonPropertyName]",
            FilteredJsonPropertyNameKind.CompilerNamedField =>
                $"field 0x{fact.MetadataToken:X8} [JsonPropertyName]",
            _ => throw new InvalidOperationException(
                $"Unknown filtered JSON property-name kind '{fact.Kind}'."),
        };

    static void EmitBlockedRecord(StringBuilder sb, ApiType record) =>
        sb.Append("export type ").Append(record.Name).Append(" = unknown;\n\n");

    static void EmitFunction(
        StringBuilder sb,
        JsExportFunction function,
        IReadOnlySet<string> knownTypeNames,
        TsBindGenDiagnostics? diagnostics)
    {
        string returnType = function.ReturnWireType is { } returnWireType
            ? TsTypeMapper.MapReturnEnvelope(function.ReturnType, returnWireType, knownTypeNames, diagnostics, $"{function.Name} return")
            : TsTypeMapper.MapReturnType(function.ReturnType, knownTypeNames, diagnostics, $"{function.Name} return");

        var parameters = function.Parameters.Select(p =>
            $"{CamelCase.FromPascalCase(p.Name)}: {TsTypeMapper.MapParameterType(p.Type, knownTypeNames, diagnostics, $"{function.Name}.{p.Name}")}");

        sb.Append("export declare function ")
          .Append(CamelCase.FromPascalCase(function.Name))
          .Append('(')
          .Append(string.Join(", ", parameters))
          .Append("): ")
          .Append(returnType)
          .Append(";\n");
    }

    static string ApplyNamingPolicy(string name, JsonWireNamingPolicy namingPolicy) => namingPolicy switch
    {
        JsonWireNamingPolicy.None => name,
        JsonWireNamingPolicy.CamelCase => CamelCase.FromPascalCase(name),
        JsonWireNamingPolicy.SnakeCaseLower => JsonNamingPolicies.SnakeCaseLower(name),
        JsonWireNamingPolicy.SnakeCaseUpper => JsonNamingPolicies.SnakeCaseUpper(name),
        JsonWireNamingPolicy.KebabCaseLower => JsonNamingPolicies.KebabCaseLower(name),
        JsonWireNamingPolicy.KebabCaseUpper => JsonNamingPolicies.KebabCaseUpper(name),
        _ => name,
    };

    static string FormatPropertyKey(string name) =>
        TsIdentifierRegex().IsMatch(name) ? name : $"\"{EscapeString(name)}\"";

    static string EscapeString(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
