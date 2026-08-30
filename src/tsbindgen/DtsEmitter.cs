using System.Text;
using CSharpText;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

static class DtsEmitter
{
    static readonly HashSet<string> CoreContractFrameworkMappings =
    [
        "System.String",
        "System.Char",
        "System.Boolean",
        "System.Byte",
        "System.SByte",
        "System.Int16",
        "System.UInt16",
        "System.Int32",
        "System.UInt32",
        "System.Int64",
        "System.UInt64",
        "System.Single",
        "System.Double",
        "System.Decimal",
        "System.Void",
        "System.Nullable`1",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.ValueTask`1",
        "System.Threading.Tasks.ValueTask",
    ];

    static readonly HashSet<string> CollectionsFrameworkMappings =
    [
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.IReadOnlyDictionary`2",
    ];

    public static string Emit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        TsBindGenDiagnostics? diagnostics = null)
    {
        ApiType[] declarationTypes =
        [
            .. surface.Records
                .Concat(surface.Enums)
                .Where(type => ShouldEmit(surface, type)),
        ];
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

        foreach (ApiType enumType in surface.Enums
            .Where(type => ShouldEmit(surface, type))
            .OrderBy(e => e.Name, StringComparer.Ordinal))
            EmitEnum(sb, enumType, diagnostics);

        foreach (ApiType record in surface.Records
            .Where(type => ShouldEmit(surface, type))
            .OrderBy(r => r.Name, StringComparer.Ordinal))
            EmitRecord(
                sb,
                record,
                surface.WireDirections.TryGetValue(
                    record,
                    out JsonWireDirection recordDirections)
                    ? recordDirections
                    : JsonWireDirection.Both,
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

    static bool ShouldEmit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        ApiType type) =>
        !surface.WireDirections.TryGetValue(
            type,
            out JsonWireDirection directions)
        || directions != JsonWireDirection.None;

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
            sb.Append("export type ").Append(enumType.Name).Append(" = string | number;\n\n");
            return;
        }

        IEnumerable<string> memberNames = enumType.Members
            .Where(member => member.Kind == "field" && member.IsConst)
            .Select(ResolvedEnumMemberName)
            .Distinct(StringComparer.Ordinal);
        string union = string.Join(
            " | ",
            memberNames.Select(n => $"\"{EscapeString(n)}\""));
        sb.Append("export type ").Append(enumType.Name).Append(" = ").Append(union)
            .Append(" | number;\n\n");
    }

    /// <summary>
    /// Emits one record declaration for the <paramref name="directions"/> the
    /// type was actually reached in.
    /// </summary>
    /// <remarks>
    /// A type reached in both directions whose members disagree between them
    /// cannot be described by a single interface. Rather than silently picking
    /// one direction's shape, emission is blocked for that type: a
    /// direction-split declaration is a design change, not something to guess.
    /// Gated by
    /// <c>DtsEmitterTests.Emit_BlocksBidirectionalTypeWithDirectionSensitiveMember</c>.
    /// </remarks>
    static void EmitRecord(
        StringBuilder sb,
        ApiType record,
        JsonWireDirection directions,
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
        if ((directions & JsonWireDirection.Deserialize)
                != JsonWireDirection.None
            && record.Members.Any(
                member => JsonWireMemberRules
                    .RequiresConstructorBindingEvidence(
                        record,
                        member)))
        {
            ReportUnsupportedConstructorBinding(
                record.Name,
                diagnostics);
            EmitBlockedType(sb, record);
            return;
        }

        if (directions == JsonWireDirection.Both
            && record.Members.Any(JsonWireMemberRules.IsDirectionSensitive))
        {
            ReportDirectionSplitWireShape(record.Name, diagnostics);
            EmitBlockedType(sb, record);
            return;
        }

        var members = record.Members
            .Where(member => JsonWireMemberRules.IsSerialized(
                member,
                directions))
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
                tsType = TsTypeMapper.MapJsonWireType(
                    propertyType,
                    knownTypeNames,
                    diagnostics,
                    location,
                    BlockedAliases(
                        member.SignatureModel?.ReturnTypeReferences,
                        knownTypeNames,
                        knownTypeIdentities));
            }
            sb.Append("  readonly ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
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
                ValidateWireMemberAttributes(
                    FormatMemberLocation(type, member),
                    member);
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

                ValidateFlagsAttributeEvidence(type);

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

    /// <summary>
    /// Refuses to generate from authentic <c>[JsonIgnore]</c> or
    /// <c>[JsonInclude]</c> metadata that cannot be honored, using the same
    /// malformed-row marker convention as <c>[JsonPropertyName]</c>.
    /// </summary>
    /// <remarks>
    /// Validated even for converter-controlled types: the converter changes how
    /// a value is written, not whether an unreadable attribute row can be
    /// trusted. Gated by
    /// <c>DtsEmitterTests.Emit_RefusesMalformedOrDuplicateJsonIgnoreRows</c> and
    /// <c>DtsEmitterTests.Emit_RefusesMalformedJsonIncludeRows</c>.
    /// </remarks>
    static void ValidateWireMemberAttributes(
        string location,
        ApiMember member)
    {
        if (member.JsonIgnoreConditions.Contains(null))
        {
            throw new UnsupportedWireContractException(
                location,
                "[JsonIgnore] metadata could not be decoded");
        }
        if (member.JsonIgnoreConditions.Count > 1)
        {
            throw new UnsupportedWireContractException(
                location,
                "members must not declare multiple [JsonIgnore] attributes");
        }
        if (member.HasMalformedJsonInclude)
        {
            throw new UnsupportedWireContractException(
                location,
                "[JsonInclude] metadata could not be decoded");
        }
    }

    /// <summary>
    /// Refuses to project a string-converted enum from <c>[Flags]</c> metadata
    /// that cannot be honored.
    /// </summary>
    /// <remarks>
    /// The flags fact selects between two incompatible declarations: a flags
    /// enum is <c>string | number</c>, because STJ writes combinations as one
    /// comma-joined string that no member-name union contains, while a regular
    /// enum is that union plus <c>number</c>. Reading a malformed or duplicated
    /// authentic row as absence would therefore emit the narrower union for a
    /// contract that can carry combined values. Only string-converted enums are
    /// affected: a converterless enum is <c>number</c> either way, so its
    /// projection does not depend on the unreadable row. Gated by
    /// <c>DtsEmitterTests.Emit_RefusesMalformedOrDuplicateFlagsMetadata</c> and
    /// <c>Emit_AllowsMalformedFlagsMetadataOnConverterlessEnum</c>.
    /// </remarks>
    static void ValidateFlagsAttributeEvidence(ApiType type)
    {
        if (!type.HasJsonStringEnumConverter)
            return;
        if (type.HasMalformedFlagsAttribute)
        {
            throw new UnsupportedWireContractException(
                FormatTypeLocation(type),
                "[Flags] metadata could not be decoded");
        }
        if (type.FlagsAttributeCount > 1)
        {
            throw new UnsupportedWireContractException(
                FormatTypeLocation(type),
                "enums must not declare multiple [Flags] attributes");
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

    static void ReportDirectionSplitWireShape(
        string location,
        TsBindGenDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{location} JSON wire shape",
            "serialization and deserialization member sets differ on a bidirectional type");

    static void ReportUnsupportedConstructorBinding(
        string location,
        TsBindGenDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{location} JSON wire shape",
            "deserialization without a participating setter requires unmodeled constructor-binding evidence");

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

        var parameters = function.Parameters.Select((p, index) =>
            $"{CamelCase.FromPascalCase(p.Name)}: {TsTypeMapper.MapParameterType(
                p.Type,
                knownTypeNames,
                diagnostics,
                $"{function.Name}.{p.Name}",
                BlockedAliases(
                    p.TypeReferences,
                    knownTypeNames,
                    knownTypeIdentities),
                function.DelegateParameters.SingleOrDefault(
                    delegateParameter =>
                        delegateParameter.ParameterIndex == index))}");

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
        if (CoreContractFrameworkMappings.Contains(reference.FullName))
        {
            return IsCoreContractAssembly(assembly)
                && HasExpectedTopLevelDefinition(
                    reference,
                    reference.FullName);
        }
        if (CollectionsFrameworkMappings.Contains(reference.FullName))
        {
            return (IsCoreContractAssembly(assembly)
                    || assembly == "System.Collections")
                && HasExpectedTopLevelDefinition(
                    reference,
                    reference.FullName);
        }

        return reference.FullName switch
        {
            "System.Text.Json.JsonElement" =>
                assembly == "System.Text.Json"
                && HasExpectedTopLevelDefinition(
                    reference,
                    "System.Text.Json.JsonElement"),
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

    static bool HasExpectedTopLevelDefinition(
        ApiTypeReferenceIdentity reference,
        string expectedFullName)
    {
        if (reference.FullName != expectedFullName)
            return false;

        int separator = expectedFullName.LastIndexOf('.');
        string expectedNamespace = separator < 0
            ? ""
            : expectedFullName[..separator];
        string expectedName = separator < 0
            ? expectedFullName
            : expectedFullName[(separator + 1)..];
        return reference.DefinitionName is
        {
            Namespace: var @namespace,
            Segments: [var segment],
        }
            && @namespace == expectedNamespace
            && segment == expectedName;
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
