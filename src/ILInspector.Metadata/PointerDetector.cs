using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Allocation-free signature type provider that only detects pointer types.
/// Returns true when any type in the signature is a pointer.
/// </summary>
internal sealed class PointerDetector : ISignatureTypeProvider<bool, object?>
{
    public static PointerDetector Instance { get; } = new();

    public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
    public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
    public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => false;

    public bool GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        return typeSpec.DecodeSignature(this, context);
    }

    public bool GetSZArrayType(bool elementType) => elementType;
    public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
    public bool GetByReferenceType(bool elementType) => elementType;
    public bool GetPointerType(bool elementType) => true;
    public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
        => genericType || typeArguments.Any(static t => t);
    public bool GetGenericMethodParameter(object? context, int index) => false;
    public bool GetGenericTypeParameter(object? context, int index) => false;
    public bool GetFunctionPointerType(MethodSignature<bool> signature) => true;
    public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;
    public bool GetPinnedType(bool elementType) => elementType;
}
