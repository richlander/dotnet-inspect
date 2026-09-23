using System.Text;
using CSharpText;
using ILInspector.Analysis;
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
        "System.Guid",
        "System.Version",
        "System.DateTimeOffset",
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
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.List`1",
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
                    TypeInventory(
                        surface,
                        declarationTypes));
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
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null,
        string? allocatedInertStringName = null,
        string? allocatedInertStringBrandName = null,
        string? allocatedDateTimeOffsetName = null,
        string? allocatedDateTimeOffsetBrandName = null,
        string? allocatedJsonTextName = null,
        string? allocatedJsonTextBrandName = null)
    {
        ApiType[] declarationTypes = GetDeclarationTypes(surface);
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity =
                DeclaredTypesByScopedIdentity(
                    surface,
                    TypeInventory(
                        surface,
                        declarationTypes));
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
            allocatedTypeNames,
            allocatedInertStringName,
            allocatedInertStringBrandName,
            allocatedDateTimeOffsetName,
            allocatedDateTimeOffsetBrandName,
            allocatedJsonTextName,
            allocatedJsonTextBrandName);
        return sb.ToString();
    }

    internal static TypeScriptFunctionSignature GetFunctionSignature(
        ILInspector.JsExportSurface.JsExportSurface surface,
        JsExportFunction function,
        TypeScriptGenerationDiagnostics? diagnostics = null,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null,
        string? allocatedInertStringName = null,
        string? allocatedDateTimeOffsetName = null,
        string? allocatedJsonTextName = null,
        bool includeRawReturnType = true) =>
        GetFunctionSignature(
            surface,
            GetDeclarationTypes(surface),
            function,
            diagnostics,
            allocatedTypeNames,
            allocatedInertStringName,
            allocatedDateTimeOffsetName,
            allocatedJsonTextName,
            includeRawReturnType);

    static ApiType[] GetDeclarationTypes(
        ILInspector.JsExportSurface.JsExportSurface surface)
    {
        foreach (JsExportUnion union in surface.Unions.Where(
            union => ShouldEmit(surface, union.Definition)))
        {
            JsonWireDirection directions = surface.WireDirections.GetValueOrDefault(
                union.Definition, JsonWireDirection.Both);
            string? reason = (directions & JsonWireDirection.Deserialize) != 0
                ? union.DeserializationUnsupportedReason
                : union.SerializationUnsupportedReason;
            if (reason is not null || union.IncludesNull is null || union.CaseTypes.Count == 0)
            {
                throw new UnsupportedWireContractException(
                    union.Definition.FullName,
                    reason ?? "union case or null evidence is unavailable");
            }
        }
        foreach (JsExportPolymorphicUnion union
            in surface.PolymorphicUnions.Where(
                union => ShouldEmit(surface, union.Definition)))
        {
            string? reason = union.UnsupportedReason;
            if (reason is not null
                || union.TypeDiscriminatorPropertyName is null
                || union.Cases.Count == 0)
            {
                throw new UnsupportedWireContractException(
                    $"{union.Definition.FullName} JSON polymorphism",
                    reason
                        ?? "discriminator property or case evidence is unavailable");
            }

            JsonWireDirection directions =
                surface.WireDirections.GetValueOrDefault(
                    union.Definition,
                    JsonWireDirection.Both);
            if ((directions & JsonWireDirection.Deserialize)
                != JsonWireDirection.None)
            {
                throw new UnsupportedWireContractException(
                    $"{union.Definition.FullName} JSON polymorphism",
                    "polymorphic deserialization is unsupported");
            }
        }
        ValidateUnionCycles(surface);
        return [
            .. surface.Records
                .Concat(surface.Enums)
                .Concat(surface.Unions.Select(union => union.Definition))
                .Concat(surface.PolymorphicUnions.Select(
                    union => union.Definition))
                .Concat(surface.PolymorphicUnions.SelectMany(
                    union => union.Cases.Select(
                        @case => @case.Definition)))
                .Where(type => ShouldEmit(surface, type)),
        ];
    }

    static void ValidateUnionCycles(ILInspector.JsExportSurface.JsExportSurface surface)
    {
        var unions = surface.Unions
            .Where(union => ShouldEmit(surface, union.Definition) && union.Definition.DefinitionName is not null)
            .ToDictionary(union => union.Definition.DefinitionName!, union => union);
        var completed = new HashSet<JsExportUnion>();
        var active = new HashSet<JsExportUnion>();
        var pending = new Stack<(JsExportUnion Union, bool Exit)>();
        foreach (JsExportUnion root in unions.Values)
        {
            pending.Push((root, false));
            while (pending.TryPop(out var next))
            {
                if (next.Exit)
                {
                    active.Remove(next.Union);
                    completed.Add(next.Union);
                    continue;
                }
                if (completed.Contains(next.Union))
                    continue;
                if (!active.Add(next.Union))
                {
                    throw new UnsupportedWireContractException(
                        next.Union.Definition.FullName,
                        "recursive union case aliases are unsupported");
                }
                pending.Push((next.Union, true));
                var types = new Stack<TypeRef>(next.Union.CaseTypes);
                while (types.TryPop(out var type))
                {
                    if (TsTypeMapper.MatchesContainingAssembly(type, surface.AssemblyIdentity)
                        && type.Resolution?.Type is { } definition
                        && unions.TryGetValue(definition, out JsExportUnion? referenced))
                        pending.Push((referenced, false));
                    if (type.ElementType is { } element)
                        types.Push(element);
                    foreach (var argument in type.TypeArguments)
                        types.Push(argument);
                }
            }
        }
    }

    static IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
        DeclaredTypesByScopedIdentity(
            ILInspector.JsExportSurface.JsExportSurface surface,
            IEnumerable<ApiType> types) =>
        types
            .SelectMany(type =>
                surface.ReferencedTypeDefinitions
                    .Where(candidate =>
                        ReferenceEquals(candidate.Value, type))
                    .Select(candidate => (
                        Identity: candidate.Key,
                        Type: type))
                    .Concat(
                        surface.AssemblyIdentity is { } assembly
                            ? [
                                (
                                    Identity: new ApiTypeReferenceIdentity(
                                        assembly,
                                        type.FullName,
                                        type.DefinitionName),
                                    Type: type),
                            ]
                            : []))
                .GroupBy(candidate => candidate.Identity)
                .Where(group => group
                    .Select(candidate => candidate.Type)
                    .Distinct()
                    .Count() == 1)
                .ToDictionary(
                group => group.Key,
                group => group.First().Type);

    static IEnumerable<ApiType> TypeInventory(
        ILInspector.JsExportSurface.JsExportSurface surface,
        ApiType[] declarationTypes) =>
        (surface.AllTypes.Count > 0
            ? surface.AllTypes
            : declarationTypes)
        .Concat(surface.ReferencedTypeDefinitions.Values)
        .Distinct();

    static void EmitWireDeclarations(
        StringBuilder sb,
        ILInspector.JsExportSurface.JsExportSurface surface,
        ApiType[] declarationTypes,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity,
        TypeScriptGenerationDiagnostics? diagnostics,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null,
        string? allocatedInertStringName = null,
        string? allocatedInertStringBrandName = null,
        string? allocatedDateTimeOffsetName = null,
        string? allocatedDateTimeOffsetBrandName = null,
        string? allocatedJsonTextName = null,
        string? allocatedJsonTextBrandName = null)
    {
        TypeMappingEnvironment typeEnvironment =
            CreateKnownTypes(
                surface,
                declarationTypes,
                allocatedTypeNames,
                allocatedInertStringName,
                allocatedDateTimeOffsetName);

        if (FindInertStringIdentity(surface) is { } inertStringIdentity)
        {
            string inertStringName =
                allocatedInertStringName ?? "InertString";
            string inertStringBrandName =
                allocatedInertStringBrandName ?? "inertStringBrand";
            if (declarationTypes.Any(type =>
                AllocatedTypeName(type, allocatedTypeNames)
                    == inertStringName))
            {
                throw new UnsupportedWireContractException(
                    inertStringIdentity.FullName,
                    "the inert-string TypeScript brand collides with another type");
            }
            if (allocatedInertStringBrandName is null
                && (declarationTypes.Any(type =>
                        AllocatedTypeName(type, allocatedTypeNames)
                            == inertStringBrandName)
                    || surface.Functions.Any(function =>
                        CamelCase.FromPascalCase(function.Name)
                            == inertStringBrandName)))
            {
                throw new UnsupportedWireContractException(
                    inertStringIdentity.FullName,
                    "the inert-string TypeScript brand binding collides with another declaration");
            }

            sb.Append("declare const ")
                .Append(inertStringBrandName)
                .Append(": unique symbol;\n\n")
                .Append("export type ")
                .Append(inertStringName)
                .Append(" = string & {\n  readonly [")
                .Append(inertStringBrandName)
                .Append("]: \"InertString\";\n};\n\n");
        }

        if (UsesDateTimeOffset(surface))
        {
            string dateTimeOffsetName =
                allocatedDateTimeOffsetName
                    ?? TsTypeMapper.DateTimeOffsetJsonStringName;
            string dateTimeOffsetBrandName =
                allocatedDateTimeOffsetBrandName
                    ?? "dateTimeOffsetStringBrand";
            if (declarationTypes.Any(type =>
                AllocatedTypeName(type, allocatedTypeNames)
                    == dateTimeOffsetName))
            {
                throw new UnsupportedWireContractException(
                    TsTypeMapper.DateTimeOffsetFullName,
                    "the DateTimeOffset TypeScript brand collides with another type");
            }
            if (allocatedDateTimeOffsetBrandName is null
                && (declarationTypes.Any(type =>
                        AllocatedTypeName(type, allocatedTypeNames)
                            == dateTimeOffsetBrandName)
                    || surface.Functions.Any(function =>
                        CamelCase.FromPascalCase(function.Name)
                            == dateTimeOffsetBrandName)))
            {
                throw new UnsupportedWireContractException(
                    TsTypeMapper.DateTimeOffsetFullName,
                    "the DateTimeOffset TypeScript brand binding collides with another declaration");
            }

            sb.Append("declare const ")
                .Append(dateTimeOffsetBrandName)
                .Append(": unique symbol;\n\n")
                .Append("export type ")
                .Append(dateTimeOffsetName)
                .Append(" = string & {\n  readonly [")
                .Append(dateTimeOffsetBrandName)
                .Append("]: \"DateTimeOffsetString\";\n};\n\n");
        }

        if (UsesJsonText(surface))
        {
            string jsonTextName = allocatedJsonTextName ?? "JsonText";
            string jsonTextBrandName =
                allocatedJsonTextBrandName ?? "jsonTextBrand";
            if (declarationTypes.Any(type =>
                AllocatedTypeName(type, allocatedTypeNames)
                    == jsonTextName))
            {
                throw new UnsupportedWireContractException(
                    jsonTextName,
                    "the JSON-text TypeScript brand collides with another type");
            }
            if (allocatedJsonTextBrandName is null
                && (declarationTypes.Any(type =>
                        AllocatedTypeName(type, allocatedTypeNames)
                            == jsonTextBrandName)
                    || surface.Functions.Any(function =>
                        CamelCase.FromPascalCase(function.Name)
                            == jsonTextBrandName)))
            {
                throw new UnsupportedWireContractException(
                    jsonTextName,
                    "the JSON-text TypeScript brand binding collides with another declaration");
            }

            sb.Append("declare const ")
                .Append(jsonTextBrandName)
                .Append(": unique symbol;\n\n")
                .Append("export type ")
                .Append(jsonTextName)
                .Append("<T> = string & {\n  readonly [")
                .Append(jsonTextBrandName)
                .Append("]: T;\n};\n\n");
        }

        if (UsesJsonValue(surface))
        {
            const string jsonValueName = "JsonValue";
            if (declarationTypes.Any(type =>
                AllocatedTypeName(type, allocatedTypeNames)
                    == jsonValueName))
            {
                throw new UnsupportedWireContractException(
                    "System.Text.Json.JsonElement",
                    "the JSON-value TypeScript alias collides with another type");
            }

            sb.Append(
                """
                export type JsonValue =
                  | null
                  | boolean
                  | number
                  | string
                  | readonly JsonValue[]
                  | { readonly [key: string]: JsonValue };

                """);
        }

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
                surface.Unions,
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

        foreach (JsExportPolymorphicUnion union
            in surface.PolymorphicUnions
                .Where(union => ShouldEmit(surface, union.Definition))
                .OrderBy(
                    union => AllocatedTypeName(
                        union.Definition,
                        allocatedTypeNames),
                    StringComparer.Ordinal))
        {
            EmitPolymorphicUnion(
                sb,
                union,
                surface.Unions,
                surface.AssemblyIdentity,
                declaredTypesByScopedIdentity,
                typeEnvironment,
                allocatedTypeNames,
                diagnostics);
        }

        foreach (JsExportUnion union in surface.Unions
            .Where(union => ShouldEmit(surface, union.Definition))
            .OrderBy(
                union => AllocatedTypeName(union.Definition, allocatedTypeNames),
                StringComparer.Ordinal))
        {
            EmitUnion(sb, union, typeEnvironment,
                AllocatedTypeName(union.Definition, allocatedTypeNames));
        }
    }

    static void EmitUnion(
        StringBuilder sb,
        JsExportUnion union,
        TypeMappingEnvironment environment,
        string name)
    {
        var usedNames = new HashSet<string>(environment.IdentityNames.Values, StringComparer.Ordinal);
        string[] parameters = new string[union.Definition.TypeParameters.Count];
        for (int index = 0; index < parameters.Length; index++)
        {
            string parameter = $"T{index}";
            while (!usedNames.Add(parameter))
                parameter += "_";
            parameters[index] = parameter;
        }

        string[] mapped = [.. union.CaseTypes.SelectMany(type =>
            TsJsonUnionMapper.MapCase(type, parameters, environment.UnionContext, union.Definition.FullName))];
        IEnumerable<string> alternatives = mapped.Where(type => type != "null");
        if (union.IncludesNull == true || mapped.Contains("null", StringComparer.Ordinal))
            alternatives = alternatives.Append("null");

        sb.Append("export type ").Append(name);
        if (parameters.Length > 0)
            sb.Append('<').AppendJoin(", ", parameters).Append('>');
        sb.Append(" = ").AppendJoin(" | ", alternatives.Distinct(StringComparer.Ordinal))
            .Append(";\n\n");
    }

    static void EmitPolymorphicUnion(
        StringBuilder sb,
        JsExportPolymorphicUnion union,
        IReadOnlyList<JsExportUnion> unions,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity,
        TypeMappingEnvironment typeEnvironment,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames,
        TypeScriptGenerationDiagnostics? diagnostics)
    {
        ApiType root = union.Definition;
        string discriminatorPropertyName =
            union.TypeDiscriminatorPropertyName!;
        JsonWireNamingPolicy namingPolicy =
            root.JsonPropertyNamingPolicy
                ?? JsonWireNamingPolicy.None;

        foreach (JsExportPolymorphicCase @case in union.Cases.OrderBy(
            @case => AllocatedTypeName(
                @case.Definition,
                allocatedTypeNames),
            StringComparer.Ordinal))
        {
            ApiType caseType = @case.Definition;
            IReadOnlyList<(ApiMember Member, string ResolvedName)> members =
                GetPolymorphicCaseMembers(
                    root,
                    caseType,
                    discriminatorPropertyName,
                    namingPolicy,
                    assemblyIdentity,
                    declaredTypesByScopedIdentity);

            string declarationName =
                AllocatedTypeName(caseType, allocatedTypeNames);
            sb.Append("export interface ")
                .Append(declarationName)
                .Append(" {\n  readonly ")
                .Append(FormatPropertyKey(discriminatorPropertyName))
                .Append(": \"")
                .Append(EscapeString(@case.TypeDiscriminator))
                .Append("\";\n");

            foreach ((ApiMember member, string resolvedName) in members)
            {
                string location =
                    $"{caseType.FullName}.{member.Name}";
                JsonWireMemberPresence presence =
                    GetEffectiveMemberPresence(
                        caseType,
                        member,
                        JsonWireDirection.Serialize,
                        assemblyIdentity,
                        declaredTypesByScopedIdentity);
                ValidateMemberTypeMapping(
                    caseType,
                    member,
                    presence,
                    unions,
                    declaredTypesByScopedIdentity);
                string propertyType =
                    member.SignatureModel?.ReturnType
                        ?? member.ReturnType
                        ?? "unknown";
                string tsType;
                if (member.JsonConverterAttributeCount > 0
                    && !HasApprovedInertStringConverter(member))
                {
                    ReportUnsupportedJsonConverter(
                        location,
                        diagnostics);
                    tsType = "unknown";
                }
                else
                {
                    IReadOnlySet<string>? blockedAliases =
                        BlockedAliases(
                            member.SignatureModel?.ReturnTypeReferences,
                            typeEnvironment.KnownTypeNames,
                            typeEnvironment.KnownTypeIdentities);
                    IReadOnlyDictionary<string, string> mappedTypeNames =
                        MappedTypeNames(
                            typeEnvironment,
                            member.SignatureModel?.ReturnTypeReferences
                                ?? []);
                    tsType = presence == JsonWireMemberPresence.Conditional
                        ? TsTypeMapper.MapJsonWirePresentValueType(
                            propertyType,
                            typeEnvironment.KnownTypeNames,
                            diagnostics,
                            location,
                            blockedAliases,
                            mappedTypeNames,
                            member.SignatureModel?.ReturnTypeShape,
                            typeEnvironment.IdentityNames,
                            typeEnvironment.UnionContext)
                        : TsTypeMapper.MapJsonWireType(
                            propertyType,
                            typeEnvironment.KnownTypeNames,
                            diagnostics,
                            location,
                            blockedAliases,
                            mappedTypeNames,
                            member.SignatureModel?.ReturnTypeShape,
                            typeEnvironment.IdentityNames,
                            typeEnvironment.UnionContext);
                }

                sb.Append("  readonly ")
                    .Append(FormatPropertyKey(resolvedName));
                if (presence == JsonWireMemberPresence.Conditional)
                    sb.Append('?');
                sb.Append(": ")
                    .Append(tsType)
                    .Append(";\n");
            }

            sb.Append("}\n\n");
        }

        sb.Append("export type ")
            .Append(AllocatedTypeName(root, allocatedTypeNames))
            .Append(" = ")
            .AppendJoin(
                " | ",
                union.Cases.Select(@case =>
                    AllocatedTypeName(
                        @case.Definition,
                        allocatedTypeNames)))
            .Append(";\n\n");
    }

    static IReadOnlyList<(ApiMember Member, string ResolvedName)>
        GetPolymorphicCaseMembers(
            ApiType root,
            ApiType caseType,
            string discriminatorPropertyName,
            JsonWireNamingPolicy namingPolicy,
            ApiAssemblyIdentity? assemblyIdentity,
            IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
                declaredTypesByScopedIdentity)
    {
        if (caseType.JsonPropertyNamingPolicy != root.JsonPropertyNamingPolicy)
        {
            throw new UnsupportedWireContractException(
                caseType.FullName,
                "polymorphic root and case naming policies differ");
        }
        if (caseType.JsonDefaultIgnoreCondition
                != root.JsonDefaultIgnoreCondition
            || caseType.JsonUseStringEnumConverter
                != root.JsonUseStringEnumConverter)
        {
            throw new UnsupportedWireContractException(
                caseType.FullName,
                "polymorphic root and case serializer options differ");
        }

        var members = new List<(ApiMember Member, string ResolvedName)>();
        var resolvedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            discriminatorPropertyName,
        };
        foreach (ApiType declaringType in new[] { root, caseType })
        {
            foreach (ApiMember member in declaringType.Members.Where(
                member => JsonWireMemberRules.ParticipatesInWireContract(
                    member,
                    JsonWireDirection.Serialize,
                    assemblyIdentity,
                    declaredTypesByScopedIdentity)))
            {
                int overriddenIndex = -1;
                if (ReferenceEquals(declaringType, caseType)
                    && member.IsOverride)
                {
                    overriddenIndex = members.FindIndex(
                        candidate => candidate.Member.Name.Equals(
                            member.Name,
                            StringComparison.Ordinal));
                    if (overriddenIndex >= 0)
                    {
                        resolvedNames.Remove(
                            members[overriddenIndex].ResolvedName);
                        members.RemoveAt(overriddenIndex);
                    }
                }

                string resolvedName = member.JsonPropertyName
                    ?? ApplyNamingPolicy(member.Name, namingPolicy);
                string location =
                    $"{declaringType.FullName}.{member.Name}";
                ValidatePropertyName(location, resolvedName);
                if (!resolvedNames.Add(resolvedName))
                {
                    throw new UnsupportedWireContractException(
                        location,
                        resolvedName == discriminatorPropertyName
                            ? "serialized member collides with the "
                                + "polymorphic discriminator property"
                            : "inherited and declared members resolve "
                                + "to the same JSON property name");
                }
                if (overriddenIndex >= 0)
                    members.Insert(overriddenIndex, (member, resolvedName));
                else
                    members.Add((member, resolvedName));
            }
        }

        return members;
    }

    static TypeScriptFunctionSignature GetFunctionSignature(
        ILInspector.JsExportSurface.JsExportSurface surface,
        ApiType[] declarationTypes,
        JsExportFunction function,
        TypeScriptGenerationDiagnostics? diagnostics,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null,
        string? allocatedInertStringName = null,
        string? allocatedDateTimeOffsetName = null,
        string? allocatedJsonTextName = null,
        bool includeRawReturnType = true)
    {
        var effectiveDiagnostics =
            diagnostics ?? new TypeScriptGenerationDiagnostics();
        TypeMappingEnvironment typeEnvironment =
            CreateKnownTypes(
                surface,
                declarationTypes,
                allocatedTypeNames,
                allocatedInertStringName,
                allocatedDateTimeOffsetName);
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
        bool validParameterWireBindings = TryIndexParameterWireBindings(
            function,
            out IReadOnlyDictionary<int, JsExportParameterWireBinding>
                parameterWireBindings);
        if (!validParameterWireBindings)
        {
            effectiveDiagnostics.ReportUnmappedType(
                $"{function.Name} JSON input parameters",
                "invalid JSON input parameter association");
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
                    typeEnvironment.KnownTypeIdentities),
                typeEnvironment.UnionContext)
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
        bool isAsync =
            TsTypeMapper.IsAsyncReturnType(function.ReturnType);
        bool returnsJsonText =
            function.ReturnWireMode == JsExportJsonOutputMode.JsonText;
        if (returnsJsonText)
        {
            string jsonTextName = allocatedJsonTextName ?? "JsonText";
            publicReturnType = isAsync
                ? $"Promise<{jsonTextName}<{UnwrapPromise(publicReturnType)}>>"
                : $"{jsonTextName}<{publicReturnType}>";
        }
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
            validDelegateAssociations && validParameterWireBindings
                ?
                [
                    .. function.Parameters.Select((parameter, index) =>
                    {
                        string rawType = TsTypeMapper.MapParameterType(
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
                            typeEnvironment.DelegateMappingContext);
                        JsExportParameterWireBinding? wireBinding =
                            parameterWireBindings.GetValueOrDefault(index);
                        string publicType = wireBinding is null
                            ? rawType
                            : TsTypeMapper.MapJsonWireType(
                                wireBinding.WireType,
                                typeEnvironment.KnownTypeNames,
                                effectiveDiagnostics,
                                $"{function.Name}.{parameter.Name}",
                                BlockedAliases(
                                    wireBinding.WireTypeReferences,
                                    typeEnvironment.KnownTypeNames,
                                    typeEnvironment.KnownTypeIdentities),
                                MappedTypeNames(
                                    typeEnvironment,
                                    wireBinding.WireTypeReferences),
                                wireBinding.WireTypeShape,
                                typeEnvironment.IdentityNames,
                                typeEnvironment.UnionContext);
                        return new TypeScriptParameterSignature(
                            CamelCase.FromPascalCase(parameter.Name),
                            rawType,
                            publicType,
                            wireBinding is not null);
                    }),
                ]
                :
                [
                    .. function.Parameters.Select(parameter =>
                        new TypeScriptParameterSignature(
                            CamelCase.FromPascalCase(parameter.Name),
                            "unknown",
                            "unknown",
                            false)),
                ];

        return new TypeScriptFunctionSignature(
            CamelCase.FromPascalCase(function.Name),
            parameters,
            rawReturnType,
            publicReturnType,
            hasMappedReturn && isAsync,
            hasMappedReturn
                && function.ReturnWireType is not null
                && !returnsJsonText
                && TsTypeMapper.IsJsonEnvelopeReturnType(function.ReturnType),
            hasMappedReturn
                && function.ReturnWireType is not null
                && returnsJsonText
                && TsTypeMapper.IsJsonEnvelopeReturnType(function.ReturnType),
            hasMappedReturn
                && function.ReturnWireType is not null
                && TsTypeMapper.IsNullableJsonEnvelopeReturnType(
                    function.ReturnType));
    }

    static string UnwrapPromise(string type) =>
        type.StartsWith("Promise<", StringComparison.Ordinal)
            && type.EndsWith('>')
            ? type[8..^1]
            : throw new InvalidOperationException(
                $"Expected Promise return type, found '{type}'.");

    static TypeMappingEnvironment
        CreateKnownTypes(
            ILInspector.JsExportSurface.JsExportSurface surface,
            ApiType[] declarationTypes,
            IReadOnlyDictionary<ApiType, string>? allocatedTypeNames = null,
            string? allocatedInertStringName = null,
            string? allocatedDateTimeOffsetName = null)
    {
        (ApiTypeReferenceIdentity Identity, ApiType Type)[] typeIdentities =
            TypeIdentities(surface, declarationTypes);
        var knownTypeNames = new HashSet<string>(
            declarationTypes.SelectMany(
                type => new[] { type.Name, type.FullName, type.MetadataName }
                    .Where(identity => !string.IsNullOrEmpty(identity))
                    .Select(identity => identity!)),
            StringComparer.Ordinal);
        var knownTypeIdentities = typeIdentities
            .Select(item => item.Identity)
            .ToHashSet();
        ApiTypeReferenceIdentity? inertStringIdentity =
            FindInertStringIdentity(surface);
        if (inertStringIdentity is not null)
            knownTypeIdentities.Add(inertStringIdentity);
        IReadOnlyList<ApiTypeReferenceIdentity> dateTimeOffsetIdentities =
            FindDateTimeOffsetIdentities(surface);
        knownTypeIdentities.UnionWith(dateTimeOffsetIdentities);
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
        foreach ((ApiTypeReferenceIdentity identity, ApiType type)
            in typeIdentities)
        {
            identityNames.Add(
                identity,
                AllocatedTypeName(type, allocatedTypeNames));
        }
        if (inertStringIdentity is not null)
        {
            identityNames.Add(
                inertStringIdentity,
                allocatedInertStringName ?? "InertString");
        }
        foreach (ApiTypeReferenceIdentity identity
            in dateTimeOffsetIdentities)
        {
            identityNames.Add(
                identity,
                allocatedDateTimeOffsetName
                    ?? TsTypeMapper.DateTimeOffsetJsonStringName);
        }

        return new TypeMappingEnvironment(
            knownTypeNames,
            knownTypeIdentities,
            aliases,
            identityNames,
            delegateMappingContext,
            new TsJsonUnionMappingContext(
                surface.AssemblyIdentity,
                identityNames,
                surface.AssemblyIdentity is { } unionAssembly
                    ? typeIdentities
                        .Where(item => item.Type.TypeParameters.Count > 0)
                        .ToDictionary(
                            item => item.Identity,
                            item => item.Type.TypeParameters.Count)
                    : typeIdentities
                        .Where(item => item.Type.TypeParameters.Count > 0)
                        .ToDictionary(
                            item => item.Identity,
                            item => item.Type.TypeParameters.Count),
                GenericTypeNames(declarationTypes, allocatedTypeNames),
                GenericTypeNameArities(declarationTypes, allocatedTypeNames),
                delegateMappingContext,
                typeIdentities
                    .Where(item =>
                        item.Type.TypeParameters.Count > 0
                        && surface.Records.Any(record =>
                            ReferenceEquals(record, item.Type)))
                    .Select(item => item.Identity)
                    .ToHashSet(),
                UsesDateTimeOffset(surface)
                    ? allocatedDateTimeOffsetName
                        ?? TsTypeMapper.DateTimeOffsetJsonStringName
                    : null));
    }

    static (ApiTypeReferenceIdentity Identity, ApiType Type)[] TypeIdentities(
        ILInspector.JsExportSurface.JsExportSurface surface,
        IEnumerable<ApiType> types)
    {
        var identities = new List<(ApiTypeReferenceIdentity, ApiType)>();
        foreach (ApiType type in types)
        {
            foreach ((ApiTypeReferenceIdentity identity, ApiType definition)
                in surface.ReferencedTypeDefinitions
                    .Where(candidate => ReferenceEquals(candidate.Value, type))
                    .Select(candidate => (candidate.Key, type)))
            {
                identities.Add((identity, definition));
            }

            if (surface.AssemblyIdentity is { } assembly
                && !surface.ReferencedTypeDefinitions.Values
                    .Any(candidate => ReferenceEquals(candidate, type)))
            {
                identities.Add((
                    new ApiTypeReferenceIdentity(
                        assembly,
                        type.FullName,
                        type.DefinitionName),
                    type));
            }
        }

        return identities
            .GroupBy(item => item.Item1)
            .Where(group => group
                .Select(item => item.Item2)
                .Distinct()
                .Count() == 1)
            .Select(group => (group.Key, group.First().Item2))
            .ToArray();
    }

    static string[] GenericParameterNames(
        ApiType type,
        TypeMappingEnvironment environment)
    {
        if (type.TypeParameters.Count == 0)
            return [];

        var usedNames = new HashSet<string>(
            environment.IdentityNames.Values,
            StringComparer.Ordinal);
        var names = new string[type.TypeParameters.Count];
        for (int index = 0; index < names.Length; index++)
        {
            string name = $"T{index}";
            while (!usedNames.Add(name))
                name += "_";
            names[index] = name;
        }
        return names;
    }

    static Dictionary<string, string> GenericTypeNames(
        IEnumerable<ApiType> types,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ApiType type in types.Where(type => type.TypeParameters.Count > 0))
        {
            string allocatedName = AllocatedTypeName(type, allocatedTypeNames);
            foreach (string? alias in new string?[]
            {
                type.Name,
                type.FullName,
                type.MetadataName,
                allocatedName,
            })
            {
                if (!string.IsNullOrEmpty(alias))
                {
                    names.TryAdd(alias, allocatedName);
                    names.TryAdd(RemoveMetadataArity(alias), allocatedName);
                }
            }
        }
        return names;
    }

    static Dictionary<string, int> GenericTypeNameArities(
        IEnumerable<ApiType> types,
        IReadOnlyDictionary<ApiType, string>? allocatedTypeNames)
    {
        var arities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ApiType type in types.Where(type => type.TypeParameters.Count > 0))
        {
            foreach (string? alias in new string?[]
            {
                type.Name,
                type.FullName,
                type.MetadataName,
                AllocatedTypeName(type, allocatedTypeNames),
            })
            {
                if (!string.IsNullOrEmpty(alias))
                {
                    arities.TryAdd(alias, type.TypeParameters.Count);
                    arities.TryAdd(RemoveMetadataArity(alias), type.TypeParameters.Count);
                }
            }
        }
        return arities;
    }

    static string RemoveMetadataArity(string name)
    {
        string segment = name;
        int lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
            segment = name[(lastDot + 1)..];

        int arity = segment.LastIndexOf('`');
        if (arity < 0)
            return name;

        return name[..(lastDot + 1)] + segment[..arity];
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
                : PreferredTypeName(type);

    internal static string PreferredTypeName(ApiType type)
    {
        if (type.TypeParameters.Count > 0
            && type.DefinitionName is { } definition)
        {
            string segment = definition.Segments[^1];
            int arity = segment.LastIndexOf('`');
            return arity >= 0 ? segment[..arity] : segment;
        }
        return type.Name;
    }

    static bool ShouldEmit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        ApiType type) =>
        type.FullName is not TsTypeMapper.InertStringFullName
        and not "System.Collections.Immutable.ImmutableArray`1"
        and not "System.Text.Json.JsonElement"
        && (!surface.WireDirections.TryGetValue(
            type,
            out JsonWireDirection directions)
        || directions != JsonWireDirection.None);

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

        if (!UsesStringEnumConverter(enumType))
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
        IReadOnlyList<JsExportUnion> unions,
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

        string[] genericParameters =
            GenericParameterNames(record, typeEnvironment);
        IReadOnlyDictionary<string, string> recordTypeNames =
            MappedTypeNames(typeEnvironment, []);
        if (record.TypeParameters.Count > 0)
        {
            var names = new Dictionary<string, string>(
                recordTypeNames,
                StringComparer.Ordinal);
            for (int index = 0; index < record.TypeParameters.Count; index++)
            {
                names[record.TypeParameters[index].Name] =
                    genericParameters[index];
            }
            recordTypeNames = names;
        }

        JsonWireDirection declarationDirection =
            (directions & JsonWireDirection.Serialize)
                != JsonWireDirection.None
                ? JsonWireDirection.Serialize
                : JsonWireDirection.Deserialize;
        var members = record.Members
            .Select(member => (
                Member: member,
                Presence: GetEffectiveMemberPresence(
                    record,
                    member,
                    declarationDirection,
                    assemblyIdentity,
                    declaredTypesByScopedIdentity),
                ResolvedName: member.JsonPropertyName
                    ?? ApplyNamingPolicy(member.Name, namingPolicy)))
            // Unsupported presence is not absence. Keep the member required
            // until its owner can authenticate conditionality.
            .Where(item =>
                item.Presence != JsonWireMemberPresence.Absent)
            .ToArray();

        foreach ((
            ApiMember member,
            JsonWireMemberPresence presence,
            _) in members)
        {
            ValidateMemberTypeMapping(
                record,
                member,
                presence,
                unions,
                declaredTypesByScopedIdentity);
        }

        sb.Append("export interface ").Append(declarationName);
        if (genericParameters.Length > 0)
            sb.Append('<').AppendJoin(", ", genericParameters).Append('>');
        sb.Append(" {\n");

        foreach ((
            ApiMember member,
            JsonWireMemberPresence presence,
            string resolvedName) in members)
        {
            string tsName = FormatPropertyKey(resolvedName);
            string propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType ?? "unknown";
            string location = $"{record.Name}.{member.Name}";
            string tsType;
            if (member.JsonConverterAttributeCount > 0
                && !HasApprovedInertStringConverter(member))
            {
                ReportUnsupportedJsonConverter(location, diagnostics);
                tsType = "unknown";
            }
            else
            {
                IReadOnlySet<string>? blockedAliases = BlockedAliases(
                    member.SignatureModel?.ReturnTypeReferences,
                    typeEnvironment.KnownTypeNames,
                    typeEnvironment.KnownTypeIdentities);
                IReadOnlyDictionary<string, string> mappedTypeNames =
                    MappedTypeNames(
                        typeEnvironment,
                        member.SignatureModel?.ReturnTypeReferences
                            ?? [])
                    .Concat(recordTypeNames)
                    .GroupBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last().Value,
                        StringComparer.Ordinal);
                tsType = presence == JsonWireMemberPresence.Conditional
                    ? TsTypeMapper.MapJsonWirePresentValueType(
                        propertyType,
                        typeEnvironment.KnownTypeNames,
                        diagnostics,
                        location,
                        blockedAliases,
                        mappedTypeNames,
                        member.SignatureModel?.ReturnTypeShape,
                        typeEnvironment.IdentityNames,
                        typeEnvironment.UnionContext)
                    : TsTypeMapper.MapJsonWireType(
                        propertyType,
                        typeEnvironment.KnownTypeNames,
                        diagnostics,
                        location,
                        blockedAliases,
                        mappedTypeNames,
                        member.SignatureModel?.ReturnTypeShape,
                        typeEnvironment.IdentityNames,
                        typeEnvironment.UnionContext);
            }
            sb.Append("  readonly ").Append(tsName);
            sb.Append(
                presence == JsonWireMemberPresence.Conditional
                    ? "?: "
                    : ": ");
            sb.Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void ValidateMemberTypeMapping(
        ApiType record,
        ApiMember member,
        JsonWireMemberPresence presence,
        IReadOnlyList<JsExportUnion> unions,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
    {
        if (presence == JsonWireMemberPresence.Conditional
            && TryGetConditionalParameter(
                member.SignatureModel,
                record.TypeParameters,
                out string? conditionalParameter))
        {
            throw new UnsupportedWireContractException(
                $"{record.Name}.{member.Name}",
                $"conditional generic record parameter "
                    + $"'{conditionalParameter}' has no exact "
                    + "present-value mapping");
        }
        if (presence == JsonWireMemberPresence.Conditional
            && TsJsonUnionMapper.CanCollapseToUnknown(
                member.SignatureModel?.ReturnTypeShape,
                unions,
                declaredTypesByScopedIdentity))
        {
            throw new UnsupportedWireContractException(
                $"{record.Name}.{member.Name}",
                "conditional union present-value type can collapse "
                    + "to unknown");
        }
        if (TryGetArrayParameter(
                member.SignatureModel,
                record.TypeParameters,
                out string? parameter))
        {
            throw new UnsupportedWireContractException(
                $"{record.Name}.{member.Name}",
                $"generic record parameter '{parameter}' is embedded "
                    + "in an array whose JSON mapping is not parametric");
        }
    }

    static bool TryGetConditionalParameter(
        ApiSignature? signature,
        IReadOnlyList<TypeParameter> parameters,
        out string? parameterName)
    {
        ApiTypeShape? type = UnwrapNullableShape(
            signature?.ReturnTypeShape);

        if (type is
            {
                Kind: ApiTypeShapeKind.GenericParameter,
                IsMethodGenericParameter: false,
                GenericParameterIndex: var parameterIndex,
            }
            && parameterIndex >= 0
            && parameterIndex < parameters.Count)
        {
            parameterName = parameters[parameterIndex].Name;
            return true;
        }

        parameterName = null;
        return false;
    }

    static ApiTypeShape? UnwrapNullableShape(ApiTypeShape? type)
    {
        return type is
        {
            Kind: ApiTypeShapeKind.GenericInstance,
            Definition: { } definition,
            TypeArguments: [var nullable],
        }
            && definition.FullName == "System.Nullable`1"
            && IsAuthenticFrameworkMapping(definition)
            ? nullable
            : type;
    }

    static bool TryGetArrayParameter(
        ApiSignature? signature,
        IReadOnlyList<TypeParameter> parameters,
        out string? parameterName)
    {
        if (signature?.ReturnTypeShape is not { } returnType)
        {
            parameterName = null;
            return false;
        }

        var pending = new Stack<ApiTypeShape>();
        pending.Push(returnType);
        while (pending.Count > 0)
        {
            ApiTypeShape current = pending.Pop();
            if (current.Kind == ApiTypeShapeKind.SzArray
                && current.ElementType is
                {
                    Kind: ApiTypeShapeKind.GenericParameter,
                    IsMethodGenericParameter: false,
                    GenericParameterIndex: var parameterIndex,
                }
                && parameterIndex >= 0
                && parameterIndex < parameters.Count)
            {
                parameterName = parameters[parameterIndex].Name;
                return true;
            }

            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            for (int index = current.TypeArguments.Length - 1;
                index >= 0;
                index--)
            {
                pending.Push(current.TypeArguments[index]);
            }
        }

        parameterName = null;
        return false;
    }

    static void ValidateWireNames(
        ApiAssemblyIdentity? assemblyIdentity,
        IEnumerable<ApiType> types,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
    {
        foreach (ApiType type in types)
        {
            if (type.HasUnionAttribute == true)
                continue;
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
                    member,
                    assemblyIdentity,
                    declaredTypesByScopedIdentity);
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
                    && UsesStringEnumConverter(type)
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
                .Where(member =>
                    JsonWireMemberRules.ParticipatesInWireContract(
                        member,
                        JsonWireDirection.Both,
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
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
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
        if (JsonWireMemberRules.GetPresence(
                member,
                JsonWireDirection.Serialize,
                assemblyIdentity,
                declaredTypesByScopedIdentity)
                == JsonWireMemberPresence.Unsupported
            || JsonWireMemberRules.GetPresence(
                member,
                JsonWireDirection.Deserialize,
                assemblyIdentity,
                declaredTypesByScopedIdentity)
                == JsonWireMemberPresence.Unsupported)
        {
            throw new UnsupportedWireContractException(
                location,
                "[JsonIgnore] condition is invalid for the member type, or "
                + "the member type's null capability could not be "
                + "authenticated");
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
        if (!UsesStringEnumConverter(type))
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
            || !UsesStringEnumConverter(type)
            || type.JsonConverterAttributeCount != 1);

    static bool UsesStringEnumConverter(ApiType type) =>
        type.HasJsonStringEnumConverter
        || type.JsonUseStringEnumConverter;

    static bool HasApprovedInertStringConverter(ApiMember member) =>
        member.JsonConverterAttributeCount == 1
        && InertStringIdentities(member.SignatureModel?.ReturnTypeShape).Any();

    static IEnumerable<ApiTypeReferenceIdentity> InertStringIdentities(
        ApiTypeShape? type)
    {
        if (type is null)
            yield break;

        var pending = new Stack<ApiTypeShape>();
        pending.Push(type);
        while (pending.TryPop(out ApiTypeShape? current))
        {
            if (current.Definition is { } identity
                && IsInertStringIdentity(identity))
            {
                yield return identity;
            }
            if (current.ElementType is { } element)
                pending.Push(element);
            foreach (ApiTypeShape argument in current.TypeArguments)
                pending.Push(argument);
        }
    }

    internal static ApiTypeReferenceIdentity? FindInertStringIdentity(
        ILInspector.JsExportSurface.JsExportSurface surface)
    {
        ApiTypeReferenceIdentity[] identities =
        [
            .. surface.Records
                .Concat(surface.PolymorphicUnions.SelectMany(
                    union => new[] { union.Definition }
                        .Concat(union.Cases.Select(
                            @case => @case.Definition))))
                .SelectMany(type =>
                {
                    JsonWireDirection directions =
                        surface.WireDirections.GetValueOrDefault(
                            type,
                            JsonWireDirection.Both);
                    return type.Members.Where(member =>
                        JsonWireMemberRules.ParticipatesInWireContract(
                            member,
                            directions));
                })
                .Where(HasApprovedInertStringConverter)
                .SelectMany(member => InertStringIdentities(
                    member.SignatureModel?.ReturnTypeShape))
                .Distinct(),
        ];
        return identities.Length switch
        {
            0 => null,
            1 => identities[0],
            _ => throw new UnsupportedWireContractException(
                TsTypeMapper.InertStringFullName,
                "multiple inert-string assembly identities are unsupported"),
        };
    }

    internal static IReadOnlyList<ApiTypeReferenceIdentity>
        FindDateTimeOffsetIdentities(
            ILInspector.JsExportSurface.JsExportSurface surface) =>
        [
            .. JsonWireShapes(surface)
                .SelectMany(ShapeIdentities)
                .Where(identity =>
                    identity.FullName
                        == TsTypeMapper.DateTimeOffsetFullName
                    && IsAuthenticFrameworkMapping(identity))
                .Distinct()
                .OrderBy(
                    identity => identity.Assembly.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    identity => identity.Assembly.Version)
                .ThenBy(
                    identity => identity.FullName,
                    StringComparer.Ordinal),
        ];

    internal static bool UsesDateTimeOffset(
        ILInspector.JsExportSurface.JsExportSurface surface) =>
        FindDateTimeOffsetIdentities(surface).Count > 0
        || surface.Unions
            .Where(union => ShouldEmit(surface, union.Definition))
            .SelectMany(union => union.CaseTypes)
            .Any(ContainsDateTimeOffset);

    static bool ContainsDateTimeOffset(TypeRef root)
    {
        var pending = new Stack<TypeRef>();
        pending.Push(root);
        while (pending.TryPop(out TypeRef? current))
        {
            if (current is
                {
                    Kind: TypeRefKind.Definition,
                    Namespace: "System",
                    Name: "DateTimeOffset",
                }
                && TsTypeMapper.IsAuthenticFrameworkMapping(current))
            {
                return true;
            }
            if (current.ElementType is { } element)
                pending.Push(element);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
        }
        return false;
    }

    static IEnumerable<ApiTypeShape> JsonWireShapes(
        ILInspector.JsExportSurface.JsExportSurface surface)
    {
        foreach (JsExportFunction function in surface.Functions)
        {
            if (function.ReturnWireTypeShape is { } returnShape)
                yield return returnShape;
            foreach (JsExportParameterWireBinding binding
                in function.ParameterWireBindings)
            {
                if (binding.WireTypeShape is { } parameterShape)
                    yield return parameterShape;
            }
        }

        foreach (ApiType type in surface.Records
            .Concat(surface.PolymorphicUnions.SelectMany(
                union => new[] { union.Definition }
                    .Concat(union.Cases.Select(
                        @case => @case.Definition)))))
        {
            JsonWireDirection directions =
                surface.WireDirections.GetValueOrDefault(
                    type,
                    JsonWireDirection.Both);
            foreach (ApiMember member in type.Members.Where(member =>
                JsonWireMemberRules.ParticipatesInWireContract(
                    member,
                    directions)))
            {
                if (member.SignatureModel?.ReturnTypeShape is { } memberShape)
                    yield return memberShape;
            }
        }
    }

    static IEnumerable<ApiTypeReferenceIdentity> ShapeIdentities(
        ApiTypeShape root)
    {
        var pending = new Stack<ApiTypeShape>();
        pending.Push(root);
        while (pending.TryPop(out ApiTypeShape? current))
        {
            if (current.Definition is { } identity)
                yield return identity;
            if (current.ElementType is { } element)
                pending.Push(element);
            foreach (ApiTypeShape argument in current.TypeArguments)
                pending.Push(argument);
        }
    }

    internal static bool UsesJsonText(
        ILInspector.JsExportSurface.JsExportSurface surface) =>
        surface.Functions.Any(function =>
            function.ReturnWireMode == JsExportJsonOutputMode.JsonText);

    internal static bool UsesJsonValue(
        ILInspector.JsExportSurface.JsExportSurface surface)
    {
        ApiType[] declarationTypes = GetDeclarationTypes(surface);
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity =
                DeclaredTypesByScopedIdentity(
                    surface,
                    TypeInventory(
                        surface,
                        declarationTypes));
        if (surface.Records
            .Where(type => ShouldEmit(surface, type))
            .Any(type =>
        {
            JsonWireDirection directions =
                surface.WireDirections.GetValueOrDefault(
                    type,
                    JsonWireDirection.Both);
            if (type.JsonPropertyNamingPolicy
                    == JsonWireNamingPolicy.Unsupported
                || HasUnsupportedJsonConverter(type)
                || HasUnsupportedRecordWireShape(
                    type,
                    surface.AssemblyIdentity,
                    declaredTypesByScopedIdentity)
                || ((directions & JsonWireDirection.Deserialize)
                        != JsonWireDirection.None
                    && type.Members.Any(member =>
                        JsonWireMemberRules
                            .RequiresConstructorBindingEvidence(
                                type,
                                member,
                                surface.AssemblyIdentity,
                                declaredTypesByScopedIdentity)))
                || (directions == JsonWireDirection.Both
                    && type.Members.Any(member =>
                        JsonWireMemberRules.IsDirectionSensitive(
                            member,
                            surface.AssemblyIdentity,
                            declaredTypesByScopedIdentity))))
            {
                return false;
            }

            JsonWireDirection declarationDirection =
                (directions & JsonWireDirection.Serialize)
                    != JsonWireDirection.None
                    ? JsonWireDirection.Serialize
                    : JsonWireDirection.Deserialize;
            return type.Members
                .Any(member => MemberUsesJsonValue(
                    type,
                    member,
                    declarationDirection,
                    surface.AssemblyIdentity,
                    declaredTypesByScopedIdentity));
        }))
        {
            return true;
        }

        return surface.PolymorphicUnions
            .Where(union => ShouldEmit(surface, union.Definition))
            .Any(union =>
            {
                ApiType root = union.Definition;
                string discriminatorPropertyName =
                    union.TypeDiscriminatorPropertyName!;
                JsonWireNamingPolicy namingPolicy =
                    root.JsonPropertyNamingPolicy
                        ?? JsonWireNamingPolicy.None;
                return union.Cases.Any(@case =>
                    GetPolymorphicCaseMembers(
                        root,
                        @case.Definition,
                        discriminatorPropertyName,
                        namingPolicy,
                        surface.AssemblyIdentity,
                        declaredTypesByScopedIdentity)
                    .Any(item => MemberUsesJsonValue(
                        @case.Definition,
                        item.Member,
                        JsonWireDirection.Serialize,
                        surface.AssemblyIdentity,
                        declaredTypesByScopedIdentity)));
            });
    }

    static bool MemberUsesJsonValue(
        ApiType declaringType,
        ApiMember member,
        JsonWireDirection direction,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity) =>
        GetEffectiveMemberPresence(
            declaringType,
            member,
            direction,
            assemblyIdentity,
            declaredTypesByScopedIdentity)
            == JsonWireMemberPresence.Conditional
        && (member.JsonConverterAttributeCount == 0
            || HasApprovedInertStringConverter(member))
        && !TryGetConditionalParameter(
            member.SignatureModel,
            declaringType.TypeParameters,
            out _)
        && IsJsonElementPresentValue(
            member.SignatureModel?.ReturnTypeShape);

    static JsonWireMemberPresence GetEffectiveMemberPresence(
        ApiType declaringType,
        ApiMember member,
        JsonWireDirection direction,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity) =>
        JsonWireMemberRules.GetPresence(
            member,
            direction,
            assemblyIdentity,
            declaredTypesByScopedIdentity,
            declaringType.JsonDefaultIgnoreCondition);

    static bool IsJsonElementPresentValue(ApiTypeShape? type) =>
        UnwrapNullableShape(type)?.Definition is { } identity
        && identity.FullName == "System.Text.Json.JsonElement"
        && IsAuthenticFrameworkMapping(identity);

    static bool IsInertStringIdentity(
        ApiTypeReferenceIdentity identity) =>
        identity.FullName == TsTypeMapper.InertStringFullName
        && identity.Assembly.Name == "InertText";

    static bool HasUnsupportedRecordWireShape(
        ApiType type,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
    {
        if (type.HasUnsupportedJsonWireAttributes
            || type.Members.Any(member =>
                member.HasUnsupportedJsonWireAttributes
                && !HasApprovedInertStringConverter(member)
                && JsonWireMemberRules.ParticipatesInWireContract(
                    member,
                    JsonWireDirection.Both,
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
        TsDelegateMappingContext DelegateMappingContext,
        TsJsonUnionMappingContext UnionContext);

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
                  parameter =>
                      $"{parameter.Name}: {parameter.PublicType}")))
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

    static bool TryIndexParameterWireBindings(
        JsExportFunction function,
        out IReadOnlyDictionary<int, JsExportParameterWireBinding>
            parameterWireBindings)
    {
        var indexed =
            new Dictionary<int, JsExportParameterWireBinding>();
        foreach (JsExportParameterWireBinding binding
            in function.ParameterWireBindings)
        {
            if (binding is null
                || binding.ParameterIndex < 0
                || binding.ParameterIndex >= function.Parameters.Count
                || !indexed.TryAdd(binding.ParameterIndex, binding))
            {
                parameterWireBindings =
                    new Dictionary<int, JsExportParameterWireBinding>();
                return false;
            }
        }

        parameterWireBindings = indexed;
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

    internal static bool IsAuthenticFrameworkMapping(
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
