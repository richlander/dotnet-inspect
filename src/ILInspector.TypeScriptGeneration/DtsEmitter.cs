using System.Text;
using CSharpText;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace ILInspector.TypeScriptGeneration;

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
        "System.IntPtr",
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
        TypeScriptGenerationDiagnostics? diagnostics = null)
    {
        ApiType[] declarationTypes = GetDeclarationTypes(surface);
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity =
                DeclaredTypesByScopedIdentity(
                    surface,
                    declarationTypes);
        ValidateTypeNames(declarationTypes);
        ValidateWireNames(
            surface.AssemblyIdentity,
            declarationTypes,
            declaredTypesByScopedIdentity);
        ValidateFunctionNames(surface.Functions);

        var sb = new StringBuilder();
        EmitWireDeclarations(
            sb,
            surface,
            declarationTypes,
            declaredTypesByScopedIdentity,
            diagnostics);

        foreach (JsExportFunction function in surface.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
            EmitFunction(sb, GetFunctionSignature(
                surface,
                declarationTypes,
                function,
                diagnostics,
                includeRawReturnType: false));

        return sb.ToString();
    }

    internal static string EmitWireDeclarations(
        ILInspector.JsExportSurface.JsExportSurface surface,
        TypeScriptGenerationDiagnostics? diagnostics = null,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null)
    {
        ApiType[] declarationTypes = GetDeclarationTypes(surface);
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity =
                DeclaredTypesByScopedIdentity(
                    surface,
                    declarationTypes);
        ValidateTypeNames(declarationTypes, allocatedTypeNames);
        ValidateWireNames(
            surface.AssemblyIdentity,
            declarationTypes,
            declaredTypesByScopedIdentity);

        var sb = new StringBuilder();
        EmitWireDeclarations(
            sb,
            surface,
            declarationTypes,
            declaredTypesByScopedIdentity,
            diagnostics,
            allocatedTypeNames);
        return sb.ToString();
    }

    internal static TypeScriptFunctionSignature GetFunctionSignature(
        ILInspector.JsExportSurface.JsExportSurface surface,
        JsExportFunction function,
        TypeScriptGenerationDiagnostics? diagnostics = null,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null,
        bool includeRawReturnType = true) =>
        GetFunctionSignature(
            surface,
            GetDeclarationTypes(surface),
            function,
            diagnostics,
            allocatedTypeNames,
            includeRawReturnType);

    static ApiType[] GetDeclarationTypes(
        ILInspector.JsExportSurface.JsExportSurface surface) =>
        [
            .. surface.Records
                .Concat(surface.Enums)
                .Where(type => ShouldEmit(surface, type)),
        ];

    static IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
        DeclaredTypesByScopedIdentity(
            ILInspector.JsExportSurface.JsExportSurface surface,
            IEnumerable<ApiType> declarationTypes) =>
        surface.AssemblyIdentity is { } assembly
            ? declarationTypes
                .Select(type => (
                    Identity: new ApiTypeReferenceIdentity(
                        assembly,
                        type.FullName,
                        type.DefinitionName),
                    Type: type))
                .GroupBy(candidate => candidate.Identity)
                .Where(group => group
                    .Select(candidate => candidate.Type)
                    .Distinct()
                    .Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Type)
            : new Dictionary<ApiTypeReferenceIdentity, ApiType>();

    static void EmitWireDeclarations(
        StringBuilder sb,
        ILInspector.JsExportSurface.JsExportSurface surface,
        ApiType[] declarationTypes,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity,
        TypeScriptGenerationDiagnostics? diagnostics,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null)
    {
        TypeMappingEnvironment typeEnvironment =
            CreateKnownTypes(
                surface,
                declarationTypes,
                allocatedTypeNames);

        foreach (ApiType enumType in surface.Enums
            .Where(type => ShouldEmit(surface, type))
            .OrderBy(
                type => AllocatedTypeName(type, allocatedTypeNames),
                StringComparer.Ordinal))
            EmitEnum(
                sb,
                enumType,
                AllocatedTypeName(enumType, allocatedTypeNames),
                diagnostics);

        foreach (ApiType record in surface.Records
            .Where(type => ShouldEmit(surface, type))
            .OrderBy(
                type => AllocatedTypeName(type, allocatedTypeNames),
                StringComparer.Ordinal))
            EmitRecord(
                sb,
                record,
                surface.WireDirections.TryGetValue(
                    record,
                    out JsonWireDirection recordDirections)
                    ? recordDirections
                    : JsonWireDirection.Both,
                surface.AssemblyIdentity,
                declaredTypesByScopedIdentity,
                AllocatedTypeName(record, allocatedTypeNames),
                typeEnvironment,
                diagnostics);
    }

    static TypeScriptFunctionSignature GetFunctionSignature(
        ILInspector.JsExportSurface.JsExportSurface surface,
        ApiType[] declarationTypes,
        JsExportFunction function,
        TypeScriptGenerationDiagnostics? diagnostics,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null,
        bool includeRawReturnType = true)
    {
        var effectiveDiagnostics =
            diagnostics ?? new TypeScriptGenerationDiagnostics();
        TypeMappingEnvironment typeEnvironment =
            CreateKnownTypes(
                surface,
                declarationTypes,
                allocatedTypeNames);
        bool validDelegateAssociations = TryIndexDelegateParameters(
            function,
            out IReadOnlyDictionary<int, JsExportDelegateParameter>
                delegateParameters);
        if (!validDelegateAssociations)
        {
            effectiveDiagnostics.ReportUnmappedType(
                $"{function.Name} delegate parameters",
                "invalid delegate parameter association");
        }
        IReadOnlyDictionary<string, string> publicReturnTypeNames =
            MappedTypeNames(
                typeEnvironment,
                function.ReturnWireType is not null
                    ? function.ReturnWireTypeReferences
                    : function.ReturnTypeReferences);
        IReadOnlyDictionary<string, string> rawReturnTypeNames =
            MappedTypeNames(
                typeEnvironment,
                function.ReturnTypeReferences);
        int returnDiagnosticsBefore =
            effectiveDiagnostics.UnmappedTypes.Count;

        string publicReturnType = function.ReturnWireType is { } returnWireType
            ? TsTypeMapper.MapReturnEnvelope(
                function.ReturnType,
                returnWireType,
                typeEnvironment.KnownTypeNames,
                effectiveDiagnostics,
                $"{function.Name} return",
                BlockedAliases(
                    function.ReturnWireTypeReferences,
                    typeEnvironment.KnownTypeNames,
                    typeEnvironment.KnownTypeIdentities),
                publicReturnTypeNames,
                function.ReturnWireTypeShape,
                typeEnvironment.IdentityNames,
                BlockedAliases(
                    function.ReturnTypeReferences,
                    typeEnvironment.KnownTypeNames,
                    typeEnvironment.KnownTypeIdentities))
            : TsTypeMapper.MapReturnType(
                function.ReturnType,
                typeEnvironment.KnownTypeNames,
                effectiveDiagnostics,
                $"{function.Name} return",
                BlockedAliases(
                    function.ReturnTypeReferences,
                    typeEnvironment.KnownTypeNames,
                    typeEnvironment.KnownTypeIdentities),
                publicReturnTypeNames);
        string rawReturnType = includeRawReturnType
            && function.ReturnWireType is not null
            ? TsTypeMapper.MapReturnType(
                function.ReturnType,
                typeEnvironment.KnownTypeNames,
                effectiveDiagnostics,
                $"{function.Name} raw return",
                BlockedAliases(
                    function.ReturnTypeReferences,
                    typeEnvironment.KnownTypeNames,
                    typeEnvironment.KnownTypeIdentities),
                rawReturnTypeNames)
            : publicReturnType;
        bool hasMappedReturn =
            effectiveDiagnostics.UnmappedTypes.Count
                == returnDiagnosticsBefore;
        TypeScriptParameterSignature[] parameters =
            validDelegateAssociations
                ?
                [
                    .. function.Parameters.Select((parameter, index) =>
                        new TypeScriptParameterSignature(
                            CamelCase.FromPascalCase(parameter.Name),
                            TsTypeMapper.MapParameterType(
                                parameter.Type,
                                typeEnvironment.KnownTypeNames,
                                effectiveDiagnostics,
                                $"{function.Name}.{parameter.Name}",
                                BlockedAliases(
                                    parameter.TypeReferences,
                                    typeEnvironment.KnownTypeNames,
                                    typeEnvironment.KnownTypeIdentities),
                                MappedTypeNames(
                                    typeEnvironment,
                                    parameter.TypeReferences),
                                delegateParameters.GetValueOrDefault(index),
                                typeEnvironment.DelegateMappingContext))),
                ]
                :
                [
                    .. function.Parameters.Select(parameter =>
                        new TypeScriptParameterSignature(
                            CamelCase.FromPascalCase(parameter.Name),
                            "unknown")),
                ];

        return new TypeScriptFunctionSignature(
            CamelCase.FromPascalCase(function.Name),
            parameters,
            rawReturnType,
            publicReturnType,
            hasMappedReturn
                && TsTypeMapper.IsAsyncReturnType(function.ReturnType),
            hasMappedReturn
                && function.ReturnWireType is not null
                && TsTypeMapper.IsJsonEnvelopeReturnType(function.ReturnType),
            hasMappedReturn
                && function.ReturnWireType is not null
                && TsTypeMapper.IsNullableJsonEnvelopeReturnType(
                    function.ReturnType));
    }

    static TypeMappingEnvironment
        CreateKnownTypes(
            ILInspector.JsExportSurface.JsExportSurface surface,
            ApiType[] declarationTypes,
            IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null)
    {
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
        var localTypeKinds = declarationTypes
            .Select(type => (
                type.DefinitionName,
                Kind: type.Kind switch
                {
                    "class" or "interface" or "delegate" =>
                        TsLocalTypeKind.Reference,
                    "struct" or "enum" =>
                        TsLocalTypeKind.Value,
                    _ => (TsLocalTypeKind?)null,
                }))
            .Where(item =>
                item.DefinitionName is not null
                && item.Kind is not null)
            .ToDictionary(
                item => item.DefinitionName!,
                item => item.Kind!.Value,
                EqualityComparer<
                    MetadataTypeDefinitionName>.Default);
        var delegateMappingContext = new TsDelegateMappingContext(
            knownTypeNames,
            localTypeKinds,
            surface.AssemblyIdentity,
            declarationTypes
                .Where(type => type.DefinitionName is not null)
                .ToDictionary(
                    type => type.DefinitionName!,
                    type => AllocatedTypeName(
                        type,
                        allocatedTypeNames),
                    EqualityComparer<
                        MetadataTypeDefinitionName>.Default));
        var aliases = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (IGrouping<string, ApiType> group in declarationTypes
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1))
        {
            ApiType type = group.Single();
            aliases.Add(
                group.Key,
                AllocatedTypeName(type, allocatedTypeNames));
        }
        foreach (ApiType type in declarationTypes)
        {
            string allocatedName =
                AllocatedTypeName(type, allocatedTypeNames);
            aliases[type.FullName] = allocatedName;
            if (!string.IsNullOrEmpty(type.MetadataName))
                aliases[type.MetadataName] = allocatedName;
        }

        var identityNames =
            new Dictionary<ApiTypeReferenceIdentity, string>();
        if (surface.AssemblyIdentity is { } identityAssembly)
        {
            foreach (ApiType type in declarationTypes)
            {
                identityNames.Add(
                    new ApiTypeReferenceIdentity(
                        identityAssembly,
                        type.FullName,
                        type.DefinitionName),
                    AllocatedTypeName(type, allocatedTypeNames));
            }
        }

        return new TypeMappingEnvironment(
            knownTypeNames,
            knownTypeIdentities,
            aliases,
            identityNames,
            delegateMappingContext);
    }

    static IReadOnlyDictionary<string, string> MappedTypeNames(
        TypeMappingEnvironment environment,
        IEnumerable<ApiTypeReferenceIdentity> references)
    {
        var aliases = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach ((string alias, string allocatedName) in environment.Aliases)
        {
            if (!TsTypeMapper.IsIntrinsicTypeSpelling(alias))
                aliases.Add(alias, allocatedName);
        }
        foreach (ApiTypeReferenceIdentity reference in references)
        {
            if (!environment.IdentityNames.TryGetValue(
                    reference,
                    out string? allocatedName))
            {
                continue;
            }

            aliases[reference.FullName] = allocatedName;
            aliases[LastSegment(reference.FullName)] = allocatedName;
        }
        return aliases;
    }

    static string AllocatedTypeName(
        ApiType type,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames) =>
        allocatedTypeNames is not null
            && allocatedTypeNames.TryGetValue(type, out string? name)
                ? name
                : type.Name;

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
        string declarationName,
        TypeScriptGenerationDiagnostics? diagnostics)
    {
        if (enumType.JsonPropertyNamingPolicy
            == JsonWireNamingPolicy.Unsupported)
        {
            ReportUnsupportedContextOptions(enumType, diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }
        if (HasUnsupportedJsonConverter(enumType))
        {
            ReportUnsupportedJsonConverter(enumType.Name, diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }
        if (enumType.HasUnsupportedJsonWireAttributes)
        {
            ReportUnsupportedJsonWireShape(enumType.Name, diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }

        if (!enumType.HasJsonStringEnumConverter)
        {
            sb.Append("export type ").Append(declarationName).Append(" = number;\n\n");
            return;
        }

        if (enumType.IsFlagsEnum)
        {
            sb.Append("export type ").Append(declarationName).Append(" = string | number;\n\n");
            return;
        }

        IEnumerable<string> memberNames = enumType.Members
            .Where(member => member.Kind == "field" && member.IsConst)
            .Select(ResolvedEnumMemberName)
            .Distinct(StringComparer.Ordinal);
        string union = string.Join(
            " | ",
            memberNames.Select(n => $"\"{EscapeString(n)}\""));
        sb.Append("export type ").Append(declarationName).Append(" = ").Append(union)
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
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity,
        string declarationName,
        TypeMappingEnvironment typeEnvironment,
        TypeScriptGenerationDiagnostics? diagnostics)
    {
        JsonWireNamingPolicy namingPolicy = record.JsonPropertyNamingPolicy ?? JsonWireNamingPolicy.None;
        if (namingPolicy == JsonWireNamingPolicy.Unsupported)
        {
            ReportUnsupportedContextOptions(record, diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }
        if (HasUnsupportedJsonConverter(record))
        {
            ReportUnsupportedJsonConverter(record.Name, diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }
        if (HasUnsupportedRecordWireShape(
                record,
                assemblyIdentity,
                declaredTypesByScopedIdentity))
        {
            ReportUnsupportedJsonWireShape(record.Name, diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }
        if ((directions & JsonWireDirection.Deserialize)
                != JsonWireDirection.None
            && record.Members.Any(
                member => JsonWireMemberRules
                    .RequiresConstructorBindingEvidence(
                        record,
                        member,
                        assemblyIdentity,
                        declaredTypesByScopedIdentity)))
        {
            ReportUnsupportedConstructorBinding(
                record.Name,
                diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }

        if (directions == JsonWireDirection.Both
            && record.Members.Any(member =>
                JsonWireMemberRules.IsDirectionSensitive(
                    member,
                    assemblyIdentity,
                    declaredTypesByScopedIdentity)))
        {
            ReportDirectionSplitWireShape(record.Name, diagnostics);
            EmitBlockedType(sb, declarationName);
            return;
        }

        var members = record.Members
            .Where(member => JsonWireMemberRules.IsSerialized(
                member,
                directions,
                assemblyIdentity,
                declaredTypesByScopedIdentity))
            .Select(member => (
                Member: member,
                ResolvedName: member.JsonPropertyName ?? ApplyNamingPolicy(member.Name, namingPolicy)))
            .ToArray();

        sb.Append("export interface ").Append(declarationName).Append(" {\n");

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
                    typeEnvironment.KnownTypeNames,
                    diagnostics,
                    location,
                    BlockedAliases(
                        member.SignatureModel?.ReturnTypeReferences,
                        typeEnvironment.KnownTypeNames,
                        typeEnvironment.KnownTypeIdentities),
                    MappedTypeNames(
                        typeEnvironment,
                        member.SignatureModel?.ReturnTypeReferences
                            ?? []),
                    member.SignatureModel?.ReturnTypeShape,
                    typeEnvironment.IdentityNames);
            }
            sb.Append("  readonly ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void ValidateWireNames(
        ApiAssemblyIdentity? assemblyIdentity,
        IEnumerable<ApiType> types,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
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
                .Where(member => JsonWireMemberRules.IsSerialized(
                    member,
                    assemblyIdentity,
                    declaredTypesByScopedIdentity)))
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

    static void ValidateTypeNames(
        IEnumerable<ApiType> types,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ApiType type in types)
        {
            string typeName =
                AllocatedTypeName(type, allocatedTypeNames);
            if (!TypeScriptIdentifier.IsBindingIdentifier(typeName))
            {
                throw new UnsupportedWireContractException(
                    FormatTypeLocation(type),
                    "TypeScript declaration names must be identifiers");
            }

            if (!TypeScriptIdentifier.IsTypeDeclarationIdentifier(typeName))
            {
                throw new UnsupportedWireContractException(
                    FormatTypeLocation(type),
                    "declaration name conflicts with TypeScript or generated binding vocabulary");
            }

            if (!names.Add(typeName))
            {
                throw new UnsupportedWireContractException(
                    FormatTypeLocation(type),
                    "multiple JSON types project to the same TypeScript declaration name");
            }
        }
    }

    static void ValidateFunctionNames(IEnumerable<JsExportFunction> functions)
    {
        var moduleBindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsExportFunction function in functions)
        {
            string functionName = CamelCase.FromPascalCase(function.Name);
            if (!TypeScriptIdentifier.IsStrictModeBindingIdentifier(functionName)
                || !IsComposedIdentifierName(function.DeclaringType)
                || !TypeScriptIdentifier.IsIdentifierName(function.Name))
            {
                throw new UnsupportedWireContractException(
                    "JS-export function",
                    "export names must be TypeScript identifiers");
            }

            if (!moduleBindings.Add(functionName))
            {
                throw new UnsupportedWireContractException(
                    "JS-export function",
                    "exports collide with generated JavaScript module bindings");
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
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

                if (!parameterNames.Add(parameterName))
                {
                    throw new UnsupportedWireContractException(
                        "JS-export parameter",
                        "parameters collide in the TypeScript declaration");
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
        TypeScriptGenerationDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{type.Name} JsonSerializerContext options",
            "unsupported wire-shaping options");

    static bool HasUnsupportedJsonConverter(ApiType type) =>
        type.JsonConverterAttributeCount > 0
        && (type.Kind != "enum"
            || !type.HasJsonStringEnumConverter
            || type.JsonConverterAttributeCount != 1);

    static bool HasUnsupportedRecordWireShape(
        ApiType type,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
    {
        if (type.HasUnsupportedJsonWireAttributes
            || type.Members.Any(member =>
                member.HasUnsupportedJsonWireAttributes
                && JsonWireMemberRules.IsSerialized(
                    member,
                    assemblyIdentity,
                    declaredTypesByScopedIdentity)))
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
        TypeScriptGenerationDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{location} JSON wire shape",
            "unsupported wire-shaping attributes or inheritance");

    static void ReportDirectionSplitWireShape(
        string location,
        TypeScriptGenerationDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{location} JSON wire shape",
            "serialization and deserialization member sets differ on a bidirectional type");

    static void ReportUnsupportedConstructorBinding(
        string location,
        TypeScriptGenerationDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            $"{location} JSON wire shape",
            "deserialization without a participating setter requires unmodeled constructor-binding evidence");

    static void ReportUnsupportedJsonConverter(
        string location,
        TypeScriptGenerationDiagnostics? diagnostics) =>
        diagnostics?.ReportUnmappedType(
            location,
            "unsupported custom JsonConverter");

    static string ResolvedEnumMemberName(ApiMember member) =>
        member.JsonStringEnumMemberName ?? member.Name;

    static void EmitBlockedType(StringBuilder sb, string declarationName) =>
        sb.Append("export type ").Append(declarationName).Append(" = unknown;\n\n");

    private sealed record TypeMappingEnvironment(
        HashSet<string> KnownTypeNames,
        HashSet<ApiTypeReferenceIdentity> KnownTypeIdentities,
        Dictionary<string, string> Aliases,
        Dictionary<ApiTypeReferenceIdentity, string> IdentityNames,
        TsDelegateMappingContext DelegateMappingContext);

    static void EmitFunction(
        StringBuilder sb,
        TypeScriptFunctionSignature signature)
    {
        sb.Append("export declare function ")
          .Append(signature.Name)
          .Append('(')
          .Append(string.Join(
              ", ",
              signature.Parameters.Select(
                  parameter => $"{parameter.Name}: {parameter.Type}")))
          .Append("): ")
          .Append(signature.PublicReturnType)
          .Append(";\n");
    }

    static bool TryIndexDelegateParameters(
        JsExportFunction function,
        out IReadOnlyDictionary<int, JsExportDelegateParameter>
            delegateParameters)
    {
        var indexed = new Dictionary<int, JsExportDelegateParameter>();
        foreach (JsExportDelegateParameter parameter
            in function.DelegateParameters)
        {
            if (parameter is null
                || parameter.ParameterIndex < 0
                || parameter.ParameterIndex >= function.Parameters.Count
                || !indexed.TryAdd(
                    parameter.ParameterIndex,
                    parameter))
            {
                delegateParameters =
                    new Dictionary<int, JsExportDelegateParameter>();
                return false;
            }
        }

        delegateParameters = indexed;
        return true;
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
                or "IntPtr"
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
            "System.IntPtr" or "IntPtr" => "nint",
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
