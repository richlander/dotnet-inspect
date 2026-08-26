using System.Collections.Immutable;

namespace ILInspector.Metadata;

/// <summary>
/// Projects signature-decoder trees into the public structured shape retained
/// for identity-sensitive metadata consumers.
/// </summary>
internal static class ApiTypeShapeFactory
{
    internal static ApiTypeShape? FromTypeNode(TypeNode type) =>
        FromTypeNode(type, depth: 0);

    static ApiTypeShape? FromTypeNode(TypeNode type, int depth)
    {
        if (depth >= MetadataSafetyPolicy.MaxRelationshipNodes)
            return null;

        return type switch
        {
            PrimitiveTypeNode primitive
                when TryGetPrimitive(
                    primitive.Name,
                    out ApiPrimitiveType primitiveType) =>
                ApiTypeShape.PrimitiveType(primitiveType),
            NamedTypeNode named
                when named.AssemblyIdentity is { } assembly =>
                ApiTypeShape.Named(new(
                    assembly,
                    named.Name,
                    StructuredName(named.MetadataName))),
            GenericTypeNode generic
                when generic.DefinitionAssemblyIdentity is { } assembly =>
                FromGeneric(generic, assembly, depth),
            SZArrayTypeNode array =>
                FromElement(
                    array.ElementType,
                    depth,
                    ApiTypeShape.SzArray),
            MDArrayTypeNode array =>
                FromElement(
                    array.ElementType,
                    depth,
                    element => ApiTypeShape.Array(
                        element,
                        array.Rank,
                        array.ArraySizes,
                        array.ArrayLowerBounds)),
            _ => null,
        };
    }

    static ApiTypeShape? FromGeneric(
        GenericTypeNode generic,
        ApiAssemblyIdentity assembly,
        int depth)
    {
        var arguments = ImmutableArray.CreateBuilder<ApiTypeShape>(
            generic.Arguments.Length);
        foreach (TypeNode argument in generic.Arguments)
        {
            ApiTypeShape? shape = FromTypeNode(argument, depth + 1);
            if (shape is null)
                return null;
            arguments.Add(shape);
        }

        return ApiTypeShape.GenericInstance(
            new(
                assembly,
                generic.DefinitionName,
                StructuredName(generic.MetadataName)),
            arguments.MoveToImmutable());
    }

    static ApiTypeShape? FromElement(
        TypeNode element,
        int depth,
        Func<ApiTypeShape, ApiTypeShape> wrap)
    {
        ApiTypeShape? shape = FromTypeNode(element, depth + 1);
        return shape is null ? null : wrap(shape);
    }

    internal static bool TryGetPrimitive(
        string name,
        out ApiPrimitiveType primitive) =>
        Enum.TryParse(name, ignoreCase: false, out primitive)
        || (primitive = name switch
        {
            "void" => ApiPrimitiveType.Void,
            "bool" => ApiPrimitiveType.Boolean,
            "char" => ApiPrimitiveType.Char,
            "sbyte" => ApiPrimitiveType.SByte,
            "byte" => ApiPrimitiveType.Byte,
            "short" => ApiPrimitiveType.Int16,
            "ushort" => ApiPrimitiveType.UInt16,
            "int" => ApiPrimitiveType.Int32,
            "uint" => ApiPrimitiveType.UInt32,
            "long" => ApiPrimitiveType.Int64,
            "ulong" => ApiPrimitiveType.UInt64,
            "float" => ApiPrimitiveType.Single,
            "double" => ApiPrimitiveType.Double,
            "decimal" => ApiPrimitiveType.Decimal,
            "string" => ApiPrimitiveType.String,
            "object" or "dynamic" => ApiPrimitiveType.Object,
            _ => (ApiPrimitiveType)(-1),
        }) != (ApiPrimitiveType)(-1);

    static MetadataTypeDefinitionName? StructuredName(
        MetadataTypeNameParts? parts) =>
        parts is not null
        && MetadataTypeDefinitionName.Create(
                parts.Namespace,
                [.. parts.Segments]) is
            MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : null;
}
