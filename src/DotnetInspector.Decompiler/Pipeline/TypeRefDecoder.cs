using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>Generic parameter names in scope while decoding a signature.</summary>
internal sealed record GenericScope(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters)
{
    public static readonly GenericScope Empty = new([], []);
}

/// <summary>
/// Decodes metadata signatures into <see cref="TypeRef"/>s. Primitives and
/// corelib-resolved types canonicalize to <see cref="TypeRef.CoreLibrary"/>
/// so identity does not depend on which facade spelled the reference.
/// Shapes outside the supported core (function pointers, custom modifiers)
/// decode to <see cref="TypeRefKind.Unsupported"/> — honest, fidelity-lowering,
/// never a guess.
/// </summary>
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
            return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{name}");
        }
        string assembly = reader.IsAssembly
            ? Canonical(reader.GetString(reader.GetAssemblyDefinition().Name))
            : "";
        return TypeRef.Definition(assembly, ns, name);
    }

    public TypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        string name = reader.GetString(typeRef.Name);
        string ns = reader.GetString(typeRef.Namespace);
        switch (typeRef.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope);
                return TypeRef.Definition(Canonical(reader.GetString(assembly.Name)), ns, name);
            case HandleKind.TypeReference:
                var declaring = GetTypeFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, 0);
                return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{name}");
            default:
                return TypeRef.Definition("", ns, name);
        }
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

    public TypeRef GetFunctionPointerType(MethodSignature<TypeRef> signature)
        => TypeRef.Unsupported("function pointer");

    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => TypeRef.Unsupported($"custom modifier ({(isRequired ? "modreq" : "modopt")} {modifier.ToDisplayString()})");

    static string NameAt(ImmutableArray<string> names, int index)
        => index >= 0 && index < names.Length ? names[index] : "";

    /// <summary>Canonicalizes corelib spellings so facade choice never affects identity.</summary>
    internal static string Canonical(string assemblyName) => assemblyName is
        "System.Private.CoreLib" or "System.Runtime" or "mscorlib" or "netstandard" or "System.Runtime.Extensions"
        ? TypeRef.CoreLibrary
        : assemblyName;
}
