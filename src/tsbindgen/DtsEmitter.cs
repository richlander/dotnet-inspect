using System.Text;
using CSharpText;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

static class DtsEmitter
{
    public static string Emit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        TsBindGenDiagnostics? diagnostics = null)
    {
        ApiType[] declarationTypes =
            [.. surface.Records, .. surface.Enums];
        ValidateTypeNames(declarationTypes);
        ValidateWireNames(declarationTypes);
        ValidateFunctionNames(surface.Functions);

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
        string union = string.Join(
            " | ",
            memberNames.Select(n => $"\"{EscapeString(n)}\""));
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
            string tsType = TsTypeMapper.MapParameterType(
                propertyType,
                knownTypeNames,
                diagnostics,
                $"{record.Name}.{member.Name}");
            sb.Append("  ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void ValidateWireNames(IEnumerable<ApiType> types)
    {
        foreach (ApiType type in types)
        {
            foreach (ApiMember member in type.Members)
            {
                ValidatePropertyNameAttributes(
                    $"{FormatMemberLocation(type, member)} [JsonPropertyName]",
                    member.JsonPropertyNameAttributeValues,
                    member.JsonPropertyName);
            }

            foreach (FilteredJsonPropertyNameFact fact
                in type.FilteredJsonPropertyNameFacts)
            {
                ValidatePropertyNameAttributes(
                    FormatFilteredPropertyNameLocation(fact),
                    fact.PropertyNames,
                    legacyPropertyName: null);
            }

            if (type.Kind == "enum")
            {
                foreach (ApiMember member in type.Members
                    .Where(member => member.Kind == "field" && member.IsConst))
                {
                    ValidatePropertyName(
                        FormatMemberLocation(type, member),
                        member.Name);
                }
                continue;
            }

            if (type.JsonPropertyNamingPolicy == JsonWireNamingPolicy.Unsupported)
                continue;

            JsonWireNamingPolicy namingPolicy =
                type.JsonPropertyNamingPolicy ?? JsonWireNamingPolicy.None;
            var resolvedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ApiMember member in type.Members
                .Where(JsonWireMemberRules.IsSerialized))
            {
                string resolvedName = member.JsonPropertyName
                    ?? ApplyNamingPolicy(member.Name, namingPolicy);
                string location = FormatMemberLocation(type, member);
                ValidatePropertyName(location, resolvedName);
                if (!resolvedNames.Add(resolvedName))
                {
                    throw new UnsupportedWireContractException(
                        location,
                        "multiple members resolve to the same JSON property name");
                }
            }
        }
    }

    static void ValidateTypeNames(IEnumerable<ApiType> types)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ApiType type in types)
        {
            if (!TypeScriptIdentifier.IsBindingIdentifier(type.Name))
            {
                throw new UnsupportedWireContractException(
                    FormatTypeLocation(type),
                    "TypeScript declaration names must be identifiers");
            }

            if (!TypeScriptIdentifier.IsTypeDeclarationIdentifier(type.Name))
            {
                throw new UnsupportedWireContractException(
                    FormatTypeLocation(type),
                    "declaration name conflicts with TypeScript or generated binding vocabulary");
            }

            if (!names.Add(type.Name))
            {
                throw new UnsupportedWireContractException(
                    FormatTypeLocation(type),
                    "multiple JSON types project to the same TypeScript declaration name");
            }
        }
    }

    static void ValidateFunctionNames(IEnumerable<JsExportFunction> functions)
    {
        var functionNames = new HashSet<string>(
            ["initializeEngine"],
            StringComparer.Ordinal);
        foreach (JsExportFunction function in functions)
        {
            string functionName = CamelCase.FromPascalCase(function.Name);
            if (!TypeScriptIdentifier.IsBindingIdentifier(functionName)
                || !IsComposedIdentifierName(function.DeclaringType)
                || !TypeScriptIdentifier.IsIdentifierName(function.Name))
            {
                throw new UnsupportedWireContractException(
                    "JS-export function",
                    "export names must be TypeScript identifiers");
            }

            if (!functionNames.Add(functionName))
            {
                throw new UnsupportedWireContractException(
                    "JS-export function",
                    "multiple exports resolve to the same TypeScript function name");
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ApiParameter parameter in function.Parameters)
            {
                string parameterName =
                    CamelCase.FromPascalCase(parameter.Name);
                if (!TypeScriptIdentifier.IsBindingIdentifier(parameterName))
                {
                    throw new UnsupportedWireContractException(
                        "JS-export parameter",
                        "parameter names must be TypeScript identifiers");
                }

                if (!parameterNames.Add(parameterName))
                {
                    throw new UnsupportedWireContractException(
                        "JS-export parameter",
                        "multiple parameters resolve to the same TypeScript name");
                }
            }
        }
    }

    static bool IsComposedIdentifierName(string name) =>
        name.Split('.').All(TypeScriptIdentifier.IsIdentifierName);

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
                $"field 0x{fact.MetadataToken:X8} [field: JsonPropertyName]",
            FilteredJsonPropertyNameKind.CompilerNamedField =>
                $"field 0x{fact.MetadataToken:X8} [JsonPropertyName]",
            _ => throw new InvalidOperationException(
                $"Unknown filtered JSON property-name kind '{fact.Kind}'."),
        };

    static string FormatTypeLocation(ApiType type) =>
        type.MetadataToken is { } token
            ? $"type 0x{token:X8}"
            : "JSON type";

    static string FormatMemberLocation(ApiType type, ApiMember member) =>
        member.MetadataToken is { } token
            ? $"member 0x{token:X8}"
            : $"{FormatTypeLocation(type)} member";

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
        TypeScriptIdentifier.IsIdentifierName(name)
            ? name
            : $"\"{EscapeString(name)}\"";

    static string EscapeString(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch)
                        || char.IsSurrogate(ch)
                        || ch is '\u2028' or '\u2029'
                        || CSharpIdentifier.IsRenderingHazard(ch))
                    {
                        builder.Append(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"\\u{(int)ch:X4}");
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }
        return builder.ToString();
    }
}
