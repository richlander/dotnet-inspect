using ILInspector.Analysis;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace ILInspector.TypeScriptGeneration;

sealed record TsJsonUnionMappingContext(
    ApiAssemblyIdentity? Assembly,
    IReadOnlyDictionary<ApiTypeReferenceIdentity, string> Names,
    IReadOnlyDictionary<ApiTypeReferenceIdentity, int> GenericArities,
    IReadOnlyDictionary<string, string> GenericNames,
    IReadOnlyDictionary<string, int> GenericNameArities,
    TsDelegateMappingContext LocalTypes,
    IReadOnlySet<ApiTypeReferenceIdentity> GenericRecords,
    string? DateTimeOffsetName,
    bool ConservativeReferenceArguments = false);

static class TsJsonUnionMapper
{
    internal static bool CanCollapseToUnknown(
        ApiTypeShape? type,
        IReadOnlyList<JsExportUnion> unions,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
    {
        ApiTypeShape? presentType = UnwrapNullableShape(type);
        if (presentType?.Definition is not { } identity
            || !declaredTypesByScopedIdentity.TryGetValue(
                identity,
                out ApiType? definition))
        {
            return false;
        }

        Dictionary<ApiType, JsExportUnion> unionsByDefinition =
            unions.ToDictionary(union => union.Definition);
        if (!unionsByDefinition.TryGetValue(
                definition,
                out JsExportUnion? union))
        {
            return false;
        }

        Dictionary<MetadataTypeDefinitionName, JsExportUnion>
            unionsByDefinitionName = unions
                .Where(union =>
                    union.Definition.DefinitionName is not null)
                .ToDictionary(
                    union => union.Definition.DefinitionName!,
                    union => union);
        Dictionary<ApiTypeReferenceIdentity, JsExportUnion> unionsByIdentity =
            declaredTypesByScopedIdentity
                .Where(candidate =>
                    unionsByDefinition.ContainsKey(candidate.Value))
                .ToDictionary(
                    candidate => candidate.Key,
                    candidate => unionsByDefinition[candidate.Value]);

        return UnionCanMapToUnknown(
            union,
            (parameterIndex, active) =>
                parameterIndex >= 0
                && parameterIndex < presentType.TypeArguments.Length
                && ShapeCanMapToUnknown(
                    presentType.TypeArguments[parameterIndex],
                    unionsByIdentity,
                    unionsByDefinitionName,
                    active),
            unionsByIdentity,
            unionsByDefinitionName,
            []);
    }

    static bool UnionCanMapToUnknown(
        JsExportUnion union,
        Func<int, HashSet<JsExportUnion>, bool>
            parameterCanMapToUnknown,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, JsExportUnion>
            unionsByIdentity,
        IReadOnlyDictionary<MetadataTypeDefinitionName, JsExportUnion>
            unionsByDefinitionName,
        HashSet<JsExportUnion> active)
    {
        if (!active.Add(union))
            return false;

        bool result = union.CaseTypes.Any(caseType =>
            UnionCaseCanMapToUnknown(
                caseType,
                parameterCanMapToUnknown,
                unionsByIdentity,
                unionsByDefinitionName,
                active));
        active.Remove(union);
        return result;
    }

    static bool UnionCaseCanMapToUnknown(
        TypeRef type,
        Func<int, HashSet<JsExportUnion>, bool>
            parameterCanMapToUnknown,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, JsExportUnion>
            unionsByIdentity,
        IReadOnlyDictionary<MetadataTypeDefinitionName, JsExportUnion>
            unionsByDefinitionName,
        HashSet<JsExportUnion> active)
    {
        if (type.Kind == TypeRefKind.GenericParameter)
        {
            // A supplied argument is a finite type tree, not a recursive case
            // edge, even when it instantiates the same union definition.
            return parameterCanMapToUnknown(
                type.GenericParameterIndex,
                []);
        }

        if (type is
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType: { } nullableDefinition,
                TypeArguments: [var nullable],
            }
            && IsCoreType(nullableDefinition, "Nullable`1"))
        {
            return UnionCaseCanMapToUnknown(
                nullable,
                parameterCanMapToUnknown,
                unionsByIdentity,
                unionsByDefinitionName,
                active);
        }

        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (definition.Namespace == "System.Text.Json"
            && definition.Name == "JsonElement"
            && TsTypeMapper.IsAuthenticFrameworkMapping(definition))
        {
            return true;
        }

        if (definition.Resolution?.Type is not { } resolved
            || !unionsByDefinitionName.TryGetValue(
                resolved,
                out JsExportUnion? nested))
        {
            return false;
        }

        return UnionCanMapToUnknown(
            nested,
            (parameterIndex, nestedActive) =>
                parameterIndex >= 0
                && parameterIndex < type.TypeArguments.Length
                && UnionCaseCanMapToUnknown(
                    type.TypeArguments[parameterIndex],
                    parameterCanMapToUnknown,
                    unionsByIdentity,
                    unionsByDefinitionName,
                    nestedActive),
            unionsByIdentity,
            unionsByDefinitionName,
            active);
    }

    static bool ShapeCanMapToUnknown(
        ApiTypeShape type,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, JsExportUnion>
            unionsByIdentity,
        IReadOnlyDictionary<MetadataTypeDefinitionName, JsExportUnion>
            unionsByDefinitionName,
        HashSet<JsExportUnion> active)
    {
        ApiTypeShape presentType = UnwrapNullableShape(type) ?? type;
        if (presentType.Kind == ApiTypeShapeKind.GenericParameter)
            return true;

        if (presentType.Definition is { } identity
            && identity.FullName == "System.Text.Json.JsonElement"
            && DtsEmitter.IsAuthenticFrameworkMapping(identity))
        {
            return true;
        }

        return presentType.Definition is { } unionIdentity
            && unionsByIdentity.TryGetValue(
                unionIdentity,
                out JsExportUnion? union)
            && UnionCanMapToUnknown(
                union,
                (parameterIndex, nestedActive) =>
                    parameterIndex >= 0
                    && parameterIndex < presentType.TypeArguments.Length
                    && ShapeCanMapToUnknown(
                        presentType.TypeArguments[parameterIndex],
                        unionsByIdentity,
                        unionsByDefinitionName,
                        nestedActive),
                unionsByIdentity,
                unionsByDefinitionName,
                active);
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
            && DtsEmitter.IsAuthenticFrameworkMapping(definition)
            ? nullable
            : type;
    }

    internal static IEnumerable<string> MapCase(
        TypeRef type,
        IReadOnlyList<string> parameters,
        TsJsonUnionMappingContext context,
        string location)
    {
        if (type.Kind == TypeRefKind.GenericParameter)
        {
            if (type.GenericParameterIndex >= 0
                && type.GenericParameterIndex < parameters.Count)
                return [parameters[type.GenericParameterIndex]];
            throw Unsupported(location, "union case generic parameter is unavailable");
        }

        var pending = new Stack<TypeRef>();
        pending.Push(type);
        while (pending.TryPop(out TypeRef? component))
        {
            if (component.Kind == TypeRefKind.GenericParameter)
                throw Unsupported(location, "generic parameters embedded in union case signatures are unsupported");
            if (component.ElementType is { } element)
                pending.Push(element);
            foreach (TypeRef argument in component.TypeArguments)
                pending.Push(argument);
        }
        return type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition, TypeArguments: [var nullable] }
            && IsCoreType(definition, "Nullable`1")
                ? [MapClosedCase(nullable, context, location), "null"]
                : [MapClosedCase(type, context, location)];
    }

    static string MapClosedCase(
        TypeRef type,
        TsJsonUnionMappingContext context,
        string location)
    {
        if (type.Kind == TypeRefKind.SzArray && type.ElementType is { } element)
            return IsCoreType(element, "Byte")
                ? "string"
                : $"ReadonlyArray<{MapCollectionCase(element, context, location)}>";

        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (definition.Kind != TypeRefKind.Definition)
            throw Unsupported(location, "unsupported union case signature");

        if (definition.Namespace == "System"
            && definition.Name == "DateTimeOffset"
            && TsTypeMapper.IsAuthenticFrameworkMapping(definition))
        {
            return context.DateTimeOffsetName
                ?? TsTypeMapper.DateTimeOffsetJsonStringName;
        }

        if (type.Kind == TypeRefKind.GenericInstance)
        {
            if (IsCoreType(definition, "Nullable`1")
                && type.TypeArguments is [var nullable])
                return WithNull(MapClosedCase(nullable, context, location));

            if (TsTypeMapper.IsAuthenticFrameworkMapping(definition)
                && definition.Namespace == "System.Collections.Generic"
                && definition.Name is "Dictionary`2" or "IReadOnlyDictionary`2"
                && type.TypeArguments is [var key, var value]
                && IsCoreType(key, "String"))
            {
                return $"Readonly<Record<string, {MapCollectionCase(value, context, location)}>>";
            }

            if (LocalIdentity(definition, context) is { } identity
                && context.GenericArities.TryGetValue(identity, out int arity)
                && arity == type.TypeArguments.Length
                && context.Names.TryGetValue(identity, out string? name))
            {
                bool conservativeArguments =
                    context.GenericRecords.Contains(identity);
                return $"{name}<{string.Join(", ", type.TypeArguments.Select(
                    argument => conservativeArguments
                        ? MapCollectionCase(argument, context, location)
                        : MapClosedCase(argument, context, location)))}>";
            }
            throw Unsupported(location, "unsupported generic union case type");
        }

        if (definition.Namespace == "System"
            && TsTypeMapper.IsAuthenticFrameworkMapping(definition)
            && Enum.TryParse(definition.Name, out ApiPrimitiveType primitive)
            && primitive != ApiPrimitiveType.Void
            && TsTypeMapper.MapPrimitive(primitive) is { } primitiveName)
            return primitiveName;

        if (definition.Namespace == "System.Text.Json"
            && definition.Name == "JsonElement"
            && TsTypeMapper.IsAuthenticFrameworkMapping(definition))
            return "unknown";

        if (LocalIdentity(definition, context) is { } local
            && !context.GenericArities.ContainsKey(local)
            && context.Names.TryGetValue(local, out string? localName))
            return localName;

        throw Unsupported(location, $"unsupported union case type '{type.ToDisplayString()}'");
    }

    internal static string MapClosedShape(
        ApiTypeShape shape,
        TsJsonUnionMappingContext context,
        string location,
        string? preciseDisplayType = null)
    {
        string? displayType = preciseDisplayType?.Trim();
        bool nullableDisplay =
            displayType?.EndsWith("?", StringComparison.Ordinal) == true;
        if (nullableDisplay)
            displayType = displayType![..^1].TrimEnd();

        if (shape.Kind == ApiTypeShapeKind.Primitive
            && shape.Primitive is { } primitive
            && primitive != ApiPrimitiveType.Void
            && TsTypeMapper.MapPrimitive(primitive) is { } primitiveName)
        {
            return nullableDisplay
                ? WithNull(primitiveName)
                : primitiveName;
        }

        if (shape.Kind == ApiTypeShapeKind.SzArray && shape.ElementType is { } element)
        {
            string? elementDisplay =
                displayType?.EndsWith("[]", StringComparison.Ordinal) == true
                    ? displayType[..^2]
                    : null;
            string array = element is
                {
                    Kind: ApiTypeShapeKind.Primitive,
                    Primitive: ApiPrimitiveType.Byte,
                }
                ? "string"
                : $"ReadonlyArray<{MapCollectionShape(
                    element,
                    context,
                    location,
                    elementDisplay)}>";
            return nullableDisplay ? WithNull(array) : array;
        }

        if (shape.Definition is { } identity)
        {
            if (context.Names.TryGetValue(identity, out string? name))
            {
                if (shape.Kind == ApiTypeShapeKind.Named
                    && !context.GenericArities.ContainsKey(identity))
                {
                    return nullableDisplay ? WithNull(name) : name;
                }
                if (shape.Kind == ApiTypeShapeKind.GenericInstance
                    && context.GenericArities.TryGetValue(identity, out int arity)
                    && arity == shape.TypeArguments.Length)
                {
                    bool conservativeArguments =
                        context.ConservativeReferenceArguments
                        && context.GenericRecords.Contains(identity);
                    IReadOnlyList<string>? displayArguments = null;
                    if (!conservativeArguments
                        && displayType is not null
                        && TsTypeMapper.TryParseGenericType(
                            displayType,
                            out _,
                            out IReadOnlyList<string> parsedArguments)
                        && parsedArguments.Count
                            == shape.TypeArguments.Length)
                    {
                        displayArguments = parsedArguments;
                    }

                    string mapped = $"{name}<{string.Join(
                        ", ",
                        shape.TypeArguments.Select((argument, index) =>
                            conservativeArguments
                                ? MapCollectionShape(
                                    argument,
                                    context,
                                    location)
                                : MapClosedShape(
                                    argument,
                                    context,
                                    location,
                                    displayArguments?[index])))}>";
                    return nullableDisplay ? WithNull(mapped) : mapped;
                }
            }

            if (DtsEmitter.IsAuthenticFrameworkMapping(identity))
            {
                if (shape.Kind == ApiTypeShapeKind.Named
                    && identity.FullName == "System.Text.Json.JsonElement")
                    return "unknown";
                if (shape.Kind == ApiTypeShapeKind.Named
                    && identity.FullName == "System.Decimal")
                    return nullableDisplay ? "number | null" : "number";
                if (shape.Kind == ApiTypeShapeKind.GenericInstance
                    && identity.FullName == "System.Nullable`1"
                    && shape.TypeArguments is [var nullable])
                    return WithNull(MapClosedShape(
                        nullable,
                        context,
                        location,
                        NullableArgumentDisplay(
                            displayType,
                            nullableDisplay)));
                if (shape.Kind == ApiTypeShapeKind.GenericInstance
                    && identity.FullName is "System.Collections.Generic.Dictionary`2"
                        or "System.Collections.Generic.IReadOnlyDictionary`2"
                    && shape.TypeArguments is [var key, var value]
                    && key is { Kind: ApiTypeShapeKind.Primitive, Primitive: ApiPrimitiveType.String })
                {
                    string? valueDisplay = null;
                    if (displayType is not null
                        && TsTypeMapper.TryParseGenericType(
                            displayType,
                            out _,
                            out IReadOnlyList<string> dictionaryArguments)
                        && dictionaryArguments.Count == 2)
                    {
                        valueDisplay = dictionaryArguments[1];
                    }

                    string dictionary =
                        $"Readonly<Record<string, "
                        + $"{MapCollectionShape(
                            value,
                            context,
                            location,
                            valueDisplay)}>>";
                    return nullableDisplay
                        ? WithNull(dictionary)
                        : dictionary;
                }
            }
        }
        throw Unsupported(location, "unsupported closed generic union argument");
    }

    internal static string WithNull(string type) =>
        type is "null" or "unknown"
            || type.EndsWith(" | null", StringComparison.Ordinal)
                ? type
                : $"{type} | null";

    static string? NullableArgumentDisplay(
        string? displayType,
        bool nullableSuffix)
    {
        if (nullableSuffix)
            return displayType;
        if (displayType is not null
            && TsTypeMapper.TryParseGenericType(
                displayType,
                out string? definition,
                out IReadOnlyList<string> arguments)
            && definition is "Nullable" or "System.Nullable"
            && arguments is [var argument])
        {
            return argument;
        }
        return null;
    }

    // Signature-only case trees do not retain nested nullable-reference annotations.
    static string MapCollectionCase(TypeRef type, TsJsonUnionMappingContext context, string location)
    {
        string mapped = MapClosedCase(type, context, location);
        return TsTypeMapper.ClassifyAuthenticatedType(type, context.LocalTypes) == TsLocalTypeKind.Reference
            ? WithNull(mapped)
            : mapped;
    }

    static string MapCollectionShape(
        ApiTypeShape shape,
        TsJsonUnionMappingContext context,
        string location,
        string? preciseDisplayType = null)
    {
        string mapped = MapClosedShape(
            shape,
            context,
            location,
            preciseDisplayType);
        bool reference = shape.Kind == ApiTypeShapeKind.SzArray
            || shape is { Kind: ApiTypeShapeKind.Primitive, Primitive: ApiPrimitiveType.String };
        if (shape.Definition is { } identity)
        {
            reference |= identity.DefinitionName is { } definition
                && context.LocalTypes.LocalTypeKinds.TryGetValue(definition, out var kind)
                && kind == TsLocalTypeKind.Reference
                && context.Names.ContainsKey(identity);
            reference |= identity.FullName is "System.Collections.Generic.Dictionary`2"
                    or "System.Collections.Generic.IReadOnlyDictionary`2"
                && DtsEmitter.IsAuthenticFrameworkMapping(identity);
        }
        return reference ? WithNull(mapped) : mapped;
    }

    static ApiTypeReferenceIdentity? LocalIdentity(
        TypeRef type,
        TsJsonUnionMappingContext context) =>
        context.Assembly is { } assembly
        && TsTypeMapper.MatchesContainingAssembly(type, assembly)
        && type.Resolution?.Type is { } definition
            ? new(assembly, definition.ToMetadataFullName(), definition)
            : null;

    static bool IsCoreType(TypeRef type, string name) =>
        type.Kind == TypeRefKind.Definition
        && type.Namespace == "System"
        && type.Name == name
        && type.Assembly == TypeRef.CoreLibrary
        && type.TrustedFrameworkAssembly;

    static UnsupportedWireContractException Unsupported(string location, string reason) =>
        new(location, reason);
}
