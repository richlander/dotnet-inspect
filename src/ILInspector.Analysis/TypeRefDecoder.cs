using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Analysis;

internal sealed record GenericScope(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters)
{
    public static readonly GenericScope Empty = new([], []);
}

internal sealed class TypeRefDecoder : ISignatureTypeProvider<TypeRef, GenericScope>
{
    public static readonly TypeRefDecoder Instance = new();

    public TypeRef GetPrimitiveType(PrimitiveTypeCode typeCode)
        => TypeRef.CoreLib("System", typeCode switch
        {
            PrimitiveTypeCode.Boolean => "Boolean",
            PrimitiveTypeCode.Byte => "Byte",
            PrimitiveTypeCode.SByte => "SByte",
            PrimitiveTypeCode.Char => "Char",
            PrimitiveTypeCode.Int16 => "Int16",
            PrimitiveTypeCode.UInt16 => "UInt16",
            PrimitiveTypeCode.Int32 => "Int32",
            PrimitiveTypeCode.UInt32 => "UInt32",
            PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.UInt64 => "UInt64",
            PrimitiveTypeCode.Single => "Single",
            PrimitiveTypeCode.Double => "Double",
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.String => "String",
            PrimitiveTypeCode.Object => "Object",
            PrimitiveTypeCode.Void => "Void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => typeCode.ToString(),
        });

    public TypeRef GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        string name = reader.GetString(typeDef.Name);
        string ns = reader.GetString(typeDef.Namespace);
        if (typeDef.IsNested)
        {
            var declaring = GetTypeFromDefinition(reader, typeDef.GetDeclaringType(), 0);
            return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{name}", declaring.TrustedFrameworkAssembly);
        }
        string assembly = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : "";
        return TypeRef.Definition(assembly, ns, name, FrameworkAssemblyKeys.IsFrameworkDefinition(reader));
    }

    public TypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        string name = reader.GetString(typeRef.Name);
        string ns = reader.GetString(typeRef.Namespace);
        return typeRef.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => TypeRef.Definition(
                reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope).Name),
                ns,
                name,
                FrameworkAssemblyKeys.IsFrameworkReference(reader, (AssemblyReferenceHandle)typeRef.ResolutionScope)),
            HandleKind.TypeReference => NestedReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, name),
            _ => TypeRef.Definition("", ns, name, FrameworkAssemblyKeys.IsFrameworkDefinition(reader)),
        };
    }

    public TypeRef GetTypeFromSpecification(MetadataReader reader, GenericScope genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public TypeRef GetSZArrayType(TypeRef elementType) => TypeRef.SzArray(elementType);
    public TypeRef GetArrayType(TypeRef elementType, ArrayShape shape) => TypeRef.MdArray(elementType, shape.Rank);
    public TypeRef GetByReferenceType(TypeRef elementType) => TypeRef.ByRef(elementType);
    public TypeRef GetPointerType(TypeRef elementType) => TypeRef.Pointer(elementType);
    public TypeRef GetPinnedType(TypeRef elementType) => TypeRef.Pinned(elementType);
    public TypeRef GetGenericInstantiation(TypeRef genericType, ImmutableArray<TypeRef> typeArguments)
        => TypeRef.GenericInstance(genericType, typeArguments);
    public TypeRef GetGenericTypeParameter(GenericScope genericContext, int index)
        => TypeRef.GenericParameter(index, NameAt(genericContext.TypeParameters, index));
    public TypeRef GetGenericMethodParameter(GenericScope genericContext, int index)
        => TypeRef.MethodGenericParameter(index, NameAt(genericContext.MethodParameters, index));
    public TypeRef GetFunctionPointerType(MethodSignature<TypeRef> signature) => TypeRef.Unsupported("function pointer");
    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => TypeRef.Unsupported($"custom modifier ({(isRequired ? "modreq" : "modopt")} {modifier.ToDisplayString()})");

    static TypeRef NestedReference(MetadataReader reader, TypeReferenceHandle declaringHandle, string nestedName)
    {
        var declaring = Instance.GetTypeFromReference(reader, declaringHandle, 0);
        return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{nestedName}", declaring.TrustedFrameworkAssembly);
    }

    static string NameAt(ImmutableArray<string> names, int index)
        => index >= 0 && index < names.Length ? names[index] : "";
}
