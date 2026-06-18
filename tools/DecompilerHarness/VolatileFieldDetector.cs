using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Detects whether a field signature carries <c>modreq(IsVolatile)</c> — the
/// metadata encoding of a <c>volatile</c> field. The compile-back skeleton must
/// re-declare such fields <c>volatile</c>: a read of a volatile field emits a
/// <c>volatile.</c> prefix before the <c>ldsfld</c>/<c>ldfld</c>, so a skeleton
/// that drops the modifier recompiles to a bare load (an opcode diff).
/// </summary>
internal sealed class VolatileFieldDetector : ISignatureTypeProvider<bool, object?>
{
    public static VolatileFieldDetector Instance { get; } = new();

    public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
    public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;

    public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        return reader.GetString(typeRef.Name) == "IsVolatile"
            && reader.GetString(typeRef.Namespace) == "System.Runtime.CompilerServices";
    }

    public bool GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, context);

    public bool GetSZArrayType(bool elementType) => elementType;
    public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
    public bool GetByReferenceType(bool elementType) => elementType;
    public bool GetPointerType(bool elementType) => elementType;
    public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
        => genericType || typeArguments.Any(static t => t);
    public bool GetGenericMethodParameter(object? context, int index) => false;
    public bool GetGenericTypeParameter(object? context, int index) => false;
    public bool GetFunctionPointerType(MethodSignature<bool> signature) => false;
    public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired)
        => (isRequired && modifier) || unmodifiedType;
    public bool GetPinnedType(bool elementType) => elementType;
}
