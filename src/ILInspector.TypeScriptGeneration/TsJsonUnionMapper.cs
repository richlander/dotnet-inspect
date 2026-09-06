using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.TypeScriptGeneration;

sealed record TsJsonUnionMappingContext(
    ApiAssemblyIdentity? Assembly,
    IReadOnlyDictionary<ApiTypeReferenceIdentity, string> Names,
    IReadOnlyDictionary<ApiTypeReferenceIdentity, int> GenericArities,
    TsDelegateMappingContext LocalTypes);

static class TsJsonUnionMapper
{
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
                return $"{name}<{string.Join(", ", type.TypeArguments.Select(
                    argument => MapClosedCase(argument, context, location)))}>";
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
        string location)
    {
        if (shape.Kind == ApiTypeShapeKind.Primitive
            && shape.Primitive is { } primitive
            && primitive != ApiPrimitiveType.Void
            && TsTypeMapper.MapPrimitive(primitive) is { } primitiveName)
            return primitiveName;

        if (shape.Kind == ApiTypeShapeKind.SzArray && shape.ElementType is { } element)
            return element is { Kind: ApiTypeShapeKind.Primitive, Primitive: ApiPrimitiveType.Byte }
                ? "string"
                : $"ReadonlyArray<{MapCollectionShape(element, context, location)}>";

        if (shape.Definition is { } identity)
        {
            if (context.Names.TryGetValue(identity, out string? name))
            {
                if (shape.Kind == ApiTypeShapeKind.Named
                    && !context.GenericArities.ContainsKey(identity))
                    return name;
                if (shape.Kind == ApiTypeShapeKind.GenericInstance
                    && context.GenericArities.TryGetValue(identity, out int arity)
                    && arity == shape.TypeArguments.Length)
                {
                    return $"{name}<{string.Join(", ", shape.TypeArguments.Select(
                        argument => MapClosedShape(argument, context, location)))}>";
                }
            }

            if (DtsEmitter.IsAuthenticFrameworkMapping(identity))
            {
                if (shape.Kind == ApiTypeShapeKind.Named
                    && identity.FullName == "System.Text.Json.JsonElement")
                    return "unknown";
                if (shape.Kind == ApiTypeShapeKind.Named
                    && identity.FullName == "System.Decimal")
                    return "number";
                if (shape.Kind == ApiTypeShapeKind.GenericInstance
                    && identity.FullName == "System.Nullable`1"
                    && shape.TypeArguments is [var nullable])
                    return WithNull(MapClosedShape(nullable, context, location));
                if (shape.Kind == ApiTypeShapeKind.GenericInstance
                    && identity.FullName is "System.Collections.Generic.Dictionary`2"
                        or "System.Collections.Generic.IReadOnlyDictionary`2"
                    && shape.TypeArguments is [var key, var value]
                    && key is { Kind: ApiTypeShapeKind.Primitive, Primitive: ApiPrimitiveType.String })
                {
                    return $"Readonly<Record<string, {MapCollectionShape(value, context, location)}>>";
                }
            }
        }
        throw Unsupported(location, "unsupported closed generic union argument");
    }

    internal static string WithNull(string type) =>
        $"{type} | null";

    // Signature-only case trees do not retain nested nullable-reference annotations.
    static string MapCollectionCase(TypeRef type, TsJsonUnionMappingContext context, string location)
    {
        string mapped = MapClosedCase(type, context, location);
        return TsTypeMapper.ClassifyAuthenticatedType(type, context.LocalTypes) == TsLocalTypeKind.Reference
            ? WithNull(mapped)
            : mapped;
    }

    static string MapCollectionShape(ApiTypeShape shape, TsJsonUnionMappingContext context, string location)
    {
        string mapped = MapClosedShape(shape, context, location);
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
