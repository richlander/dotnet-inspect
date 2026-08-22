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
            declarationTypes.SelectMany(
                type => new[] { type.Name, type.FullName, type.MetadataName }
                    .Where(identity => !string.IsNullOrEmpty(identity))
                    .Select(identity => identity!)),
            StringComparer.Ordinal);
        var knownTypeIdentities = surface.AssemblyIdentity is { } assembly
            ? new HashSet<ApiTypeReferenceIdentity>(
                declarationTypes.Select(type =>
                    new ApiTypeReferenceIdentity(
                        assembly,
                        type.FullName,
                        type.DefinitionName)))
            : [];

        var sb = new StringBuilder();

        foreach (ApiType enumType in surface.Enums.OrderBy(e => e.Name, StringComparer.Ordinal))
            EmitEnum(sb, enumType, diagnostics);

        foreach (ApiType record in surface.Records.OrderBy(r => r.Name, StringComparer.Ordinal))
            EmitRecord(
                sb,
                record,
                knownTypeNames,
                knownTypeIdentities,
                diagnostics);

        sb.Append(
            "export declare function initializeEngine(onStatus?: (status: string) => void): Promise<unknown>;\n");

        foreach (JsExportFunction function in surface.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
            EmitFunction(
                sb,
                function,
                knownTypeNames,
                knownTypeIdentities,
                diagnostics);

        return sb.ToString();
    }

    static void EmitEnum(
        StringBuilder sb,
        ApiType enumType,
        TsBindGenDiagnostics? diagnostics)
    {
        if (enumType.JsonPropertyNamingPolicy
            == JsonWireNamingPolicy.Unsupported)
        {
            ReportUnsupportedContextOptions(enumType, diagnostics);
            EmitBlockedType(sb, enumType);
            return;
        }
        if (HasUnsupportedJsonConverter(enumType))
        {
            ReportUnsupportedJsonConverter(enumType.Name, diagnostics);
            EmitBlockedType(sb, enumType);
            return;
        }
        if (enumType.HasUnsupportedJsonWireAttributes)
        {
            ReportUnsupportedJsonWireShape(enumType.Name, diagnostics);
            EmitBlockedType(sb, enumType);
            return;
        }

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

        IEnumerable<string> memberNames = enumType.Members
            .Where(member => member.Kind == "field" && member.IsConst)
            .Select(ResolvedEnumMemberName)
            .Distinct(StringComparer.Ordinal);
        string union = string.Join(
            " | ",
            memberNames.Select(n => $"\"{EscapeString(n)}\""));
        sb.Append("export type ").Append(enumType.Name).Append(" = ").Append(union).Append(";\n\n");
    }

    static void EmitRecord(
        StringBuilder sb,
        ApiType record,
        IReadOnlySet<string> knownTypeNames,
        IReadOnlySet<ApiTypeReferenceIdentity> knownTypeIdentities,
        TsBindGenDiagnostics? diagnostics)
    {
        JsonWireNamingPolicy namingPolicy = record.JsonPropertyNamingPolicy ?? JsonWireNamingPolicy.None;
        if (namingPolicy == JsonWireNamingPolicy.Unsupported)
        {
            ReportUnsupportedContextOptions(record, diagnostics);
            EmitBlockedType(sb, record);
            return;
        }
        if (HasUnsupportedJsonConverter(record))
        {
            ReportUnsupportedJsonConverter(record.Name, diagnostics);
            EmitBlockedType(sb, record);
            return;
        }
        if (HasUnsupportedRecordWireShape(record))
        {
            ReportUnsupportedJsonWireShape(record.Name, diagnostics);
            EmitBlockedType(sb, record);
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
            string location = $"{record.Name}.{member.Name}";
            string tsType;
            if (member.JsonConverterAttributeCount > 0)
            {
                ReportUnsupportedJsonConverter(location, diagnostics);
                tsType = "unknown";
            }
            else
            {
                tsType = TsTypeMapper.MapParameterType(
                    propertyType,
                    knownTypeNames,
                    diagnostics,
                    location,
                    BlockedAliases(
                        member.SignatureModel?.ReturnTypeReferences,
                        knownTypeNames,
                        knownTypeIdentities));
            }
            sb.Append("  ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void ValidateWireNames(IEnumerable<ApiType> types)
    {
        foreach (ApiType type in types)
        {
            bool converterControlled =
                HasUnsupportedJsonConverter(type);
            foreach (ApiMember member in type.Members)
            {
                ValidatePropertyNameAttributes(
                    $"{FormatMemberLocation(type, member)} [JsonPropertyName]",
                    member.JsonPropertyNameAttributeValues,
                    member.JsonPropertyName,
                    validateName: !converterControlled);
            }

            foreach (FilteredJsonPropertyNameFact fact
                in type.FilteredJsonPropertyNameFacts)
            {
                ValidatePropertyNameAttributes(
                    FormatFilteredPropertyNameLocation(fact),
                    fact.PropertyNames,
                    legacyPropertyName: null,
                    validateName: !converterControlled);
            }

            if (type.Kind == "enum")
            {
                ApiMember[] members =
                    [.. type.Members.Where(
                        member => member.Kind == "field" && member.IsConst)];
                foreach (ApiMember member in members)
                {
                    ValidateEnumMemberNameAttributes(
                        $"{FormatMemberLocation(type, member)} "
                            + "[JsonStringEnumMemberName]",
                        member.JsonStringEnumMemberNameAttributeValues);
                }
                if (converterControlled)
                    continue;

                foreach (ApiMember member in members)
                {
                    ValidatePropertyName(
                        FormatMemberLocation(type, member),
                        member.Name);
                }
                if (type.JsonPropertyNamingPolicy
                        != JsonWireNamingPolicy.Unsupported
                    && type.HasJsonStringEnumConverter
                    && !type.IsFlagsEnum
                    && members.Length == 0)
                {
                    throw new UnsupportedWireContractException(
                        FormatTypeLocation(type),
                        "string-converted enums must declare at least one member");
                }
                continue;
            }

            if (converterControlled)
                continue;

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
        var moduleBindings = new HashSet<string>(
            ["dotnet", "initializeEngine"],
            StringComparer.Ordinal);
        foreach (JsExportFunction function in functions)
        {
            string functionName = CamelCase.FromPascalCase(function.Name);
            string exportSlotName = functionName + "Export";
            if (!TypeScriptIdentifier.IsStrictModeBindingIdentifier(functionName)
                || !TypeScriptIdentifier.IsStrictModeBindingIdentifier(exportSlotName)
                || !IsComposedIdentifierName(function.DeclaringType)
                || !TypeScriptIdentifier.IsIdentifierName(function.Name))
            {
                throw new UnsupportedWireContractException(
                    "JS-export function",
                    "export names must be TypeScript identifiers");
            }

            if (!moduleBindings.Add(functionName)
                || !moduleBindings.Add(exportSlotName))
            {
                throw new UnsupportedWireContractException(
                    "JS-export function",
                    "exports collide with generated JavaScript module bindings");
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            bool reservesResult =
                function.ReturnWireType is not null
                && TsTypeMapper.IsJsonEnvelopeReturnType(function.ReturnType);
            foreach (ApiParameter parameter in function.Parameters)
            {
                string parameterName =
                    CamelCase.FromPascalCase(parameter.Name);
                if (!TypeScriptIdentifier.IsStrictModeBindingIdentifier(parameterName))
                {
                    throw new UnsupportedWireContractException(
                        "JS-export parameter",
                        "parameter names must be TypeScript identifiers");
                }

                if (!parameterNames.Add(parameterName)
                    || parameterName == exportSlotName
                    || reservesResult && parameterName == "result")
                {
                    throw new UnsupportedWireContractException(
                        "JS-export parameter",
                        "parameters collide with generated JavaScript bindings");
                }
            }
        }
    }

    static bool IsComposedIdentifierName(string name) =>
        name.Split('.').All(TypeScriptIdentifier.IsIdentifierName);

    static void ValidatePropertyNameAttributes(
        string location,
        IReadOnlyList<string?> propertyNames,
        string? legacyPropertyName,
        bool validateName = true)
    {
        if (propertyNames.Count == 0)
        {
            if (validateName && legacyPropertyName is not null)
                ValidatePropertyName(location, legacyPropertyName);
            return;
        }

        if (propertyNames.Count != 1 || propertyNames[0] is not { } propertyName)
        {
            throw new UnsupportedWireContractException(
                location,
                "duplicate or malformed JsonPropertyName attributes are not supported");
        }

        if (validateName)
            ValidatePropertyName(location, propertyName);
    }

    static void ValidateEnumMemberNameAttributes(
        string location,
        IReadOnlyList<string?> names)
    {
        if (names.Count == 0)
            return;

        if (names.Count != 1 || names[0] is null)
        {
            throw new UnsupportedWireContractException(
                location,
                "duplicate or malformed JsonStringEnumMemberName "
                    + "attributes are not supported");
        }
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
        (member.DeclarationMetadataToken ?? member.MetadataToken) is { } token
            ? $"member 0x{token:X8}"
            : $"{FormatTypeLocation(type)} member";

    static void ReportUnsupportedContextOptions(
        ApiType type,
        TsBindGenDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{type.Name} JsonSerializerContext options",
            "unsupported wire-shaping options");

    static bool HasUnsupportedJsonConverter(ApiType type) =>
        type.JsonConverterAttributeCount > 0
        && (type.Kind != "enum"
            || !type.HasJsonStringEnumConverter
            || type.JsonConverterAttributeCount != 1);

    static bool HasUnsupportedRecordWireShape(ApiType type)
    {
        if (type.HasUnsupportedJsonWireAttributes
            || type.Members.Any(member =>
                member.HasUnsupportedJsonWireAttributes
                && JsonWireMemberRules.IsSerialized(member)))
        {
            return true;
        }

        if (type.BaseType is null)
            return false;
        string expectedBaseType = type.Kind == "struct"
            ? "System.ValueType"
            : "System.Object";
        if (type.BaseType != expectedBaseType)
            return true;
        return type.BaseTypeReference is { } reference
            && !PlatformKeys.IsPlatform(
                reference.Assembly.PublicKeyToken);
    }

    static void ReportUnsupportedJsonWireShape(
        string location,
        TsBindGenDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{location} JSON wire shape",
            "unsupported wire-shaping attributes or inheritance");

    static void ReportUnsupportedJsonConverter(
        string location,
        TsBindGenDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            location,
            "unsupported custom JsonConverter");

    static string ResolvedEnumMemberName(ApiMember member) =>
        member.JsonStringEnumMemberName ?? member.Name;

    static void EmitBlockedType(StringBuilder sb, ApiType type) =>
        sb.Append("export type ").Append(type.Name).Append(" = unknown;\n\n");

    static void EmitFunction(
        StringBuilder sb,
        JsExportFunction function,
        IReadOnlySet<string> knownTypeNames,
        IReadOnlySet<ApiTypeReferenceIdentity> knownTypeIdentities,
        TsBindGenDiagnostics? diagnostics)
    {
        string returnType = function.ReturnWireType is { } returnWireType
            ? TsTypeMapper.MapReturnEnvelope(
                function.ReturnType,
                returnWireType,
                knownTypeNames,
                diagnostics,
                $"{function.Name} return",
                BlockedAliases(
                    function.ReturnWireTypeReferences
                        .Concat(function.ReturnTypeReferences)
                        .ToArray(),
                    knownTypeNames,
                    knownTypeIdentities))
            : TsTypeMapper.MapReturnType(
                function.ReturnType,
                knownTypeNames,
                diagnostics,
                $"{function.Name} return",
                BlockedAliases(
                    function.ReturnTypeReferences,
                    knownTypeNames,
                    knownTypeIdentities));

        var parameters = function.Parameters.Select(p =>
            $"{CamelCase.FromPascalCase(p.Name)}: {TsTypeMapper.MapParameterType(
                p.Type,
                knownTypeNames,
                diagnostics,
                $"{function.Name}.{p.Name}",
                BlockedAliases(
                    p.TypeReferences,
                    knownTypeNames,
                    knownTypeIdentities))}");

        sb.Append("export declare function ")
          .Append(CamelCase.FromPascalCase(function.Name))
          .Append('(')
          .Append(string.Join(", ", parameters))
          .Append("): ")
          .Append(returnType)
          .Append(";\n");
    }

    static IReadOnlySet<string>? BlockedAliases(
        IReadOnlyList<ApiTypeReferenceIdentity>? references,
        IReadOnlySet<string> knownTypeNames,
        IReadOnlySet<ApiTypeReferenceIdentity> knownTypeIdentities)
    {
        if (references is null || references.Count == 0)
            return null;

        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (ApiTypeReferenceIdentity reference in references)
        {
            string simpleName = LastSegment(reference.FullName);
            if (knownTypeIdentities.Count > 0
                && !knownTypeIdentities.Contains(reference))
            {
                if (knownTypeNames.Contains(reference.FullName))
                    blocked.Add(reference.FullName);
                if (knownTypeNames.Contains(simpleName))
                    blocked.Add(simpleName);
            }

            if (!IsAuthenticFrameworkMapping(reference))
            {
                AddFrameworkMappingAliases(blocked, reference.FullName);
            }
        }
        return blocked.Count == 0 ? null : blocked;
    }

    static bool IsAuthenticFrameworkMapping(
        ApiTypeReferenceIdentity reference)
    {
        if (!PlatformKeys.IsPlatform(
                reference.Assembly.PublicKeyToken))
        {
            return false;
        }

        string assembly = reference.Assembly.Name;
        return reference.FullName switch
        {
            "System.String"
                or "System.Char"
                or "System.Boolean"
                or "System.Byte"
                or "System.SByte"
                or "System.Int16"
                or "System.UInt16"
                or "System.Int32"
                or "System.UInt32"
                or "System.Int64"
                or "System.UInt64"
                or "System.Single"
                or "System.Double"
                or "System.Decimal"
                or "System.Void"
                or "System.Nullable`1"
                or "System.Threading.Tasks.Task`1"
                or "System.Threading.Tasks.Task"
                or "System.Threading.Tasks.ValueTask`1"
                or "System.Threading.Tasks.ValueTask" =>
                    IsCoreContractAssembly(assembly),
            "System.Collections.Generic.Dictionary`2"
                or "System.Collections.Generic.IReadOnlyDictionary`2" =>
                    IsCoreContractAssembly(assembly)
                    || assembly == "System.Collections",
            "System.Text.Json.JsonElement" =>
                assembly == "System.Text.Json",
            "String"
                or "Char"
                or "Boolean"
                or "Byte"
                or "SByte"
                or "Int16"
                or "UInt16"
                or "Int32"
                or "UInt32"
                or "Int64"
                or "UInt64"
                or "Single"
                or "Double"
                or "Decimal"
                or "Void"
                or "Nullable`1"
                or "Task`1"
                or "Task"
                or "ValueTask`1"
                or "ValueTask"
                or "Dictionary`2"
                or "IReadOnlyDictionary`2"
                or "JsonElement" => false,
            _ => true,
        };
    }

    static bool IsCoreContractAssembly(string assembly) =>
        assembly is "System.Private.CoreLib"
            or "System.Runtime"
            or "mscorlib"
            or "netstandard";

    static void AddFrameworkMappingAliases(
        HashSet<string> blocked,
        string fullName)
    {
        string? keyword = fullName switch
        {
            "System.String" or "String" => "string",
            "System.Char" or "Char" => "char",
            "System.Boolean" or "Boolean" => "bool",
            "System.Byte" or "Byte" => "byte",
            "System.SByte" or "SByte" => "sbyte",
            "System.Int16" or "Int16" => "short",
            "System.UInt16" or "UInt16" => "ushort",
            "System.Int32" or "Int32" => "int",
            "System.UInt32" or "UInt32" => "uint",
            "System.Int64" or "Int64" => "long",
            "System.UInt64" or "UInt64" => "ulong",
            "System.Single" or "Single" => "float",
            "System.Double" or "Double" => "double",
            "System.Decimal" or "Decimal" => "decimal",
            "System.Void" or "Void" => "void",
            _ => null,
        };
        if (keyword is not null)
        {
            blocked.Add(fullName);
            blocked.Add(keyword);
            return;
        }

        string? renderedDefinition = fullName switch
        {
            "System.Nullable`1" or "Nullable`1" =>
                fullName.StartsWith("System.", StringComparison.Ordinal)
                    ? "System.Nullable"
                    : "Nullable",
            "System.Threading.Tasks.Task`1" =>
                "System.Threading.Tasks.Task",
            "System.Threading.Tasks.Task" =>
                "System.Threading.Tasks.Task",
            "Task`1" or "Task" => "Task",
            "System.Threading.Tasks.ValueTask`1" =>
                "System.Threading.Tasks.ValueTask",
            "System.Threading.Tasks.ValueTask" =>
                "System.Threading.Tasks.ValueTask",
            "ValueTask`1" or "ValueTask" => "ValueTask",
            "System.Collections.Generic.Dictionary`2" =>
                "System.Collections.Generic.Dictionary",
            "Dictionary`2" => "Dictionary",
            "System.Collections.Generic.IReadOnlyDictionary`2" =>
                "System.Collections.Generic.IReadOnlyDictionary",
            "IReadOnlyDictionary`2" => "IReadOnlyDictionary",
            "System.Text.Json.JsonElement" =>
                "System.Text.Json.JsonElement",
            "JsonElement" => "JsonElement",
            _ => null,
        };
        if (renderedDefinition is null)
            return;

        blocked.Add(renderedDefinition);
        blocked.Add(LastSegment(renderedDefinition));
    }

    static string LastSegment(string typeName)
    {
        int dot = typeName.LastIndexOf('.');
        return dot >= 0 ? typeName[(dot + 1)..] : typeName;
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
