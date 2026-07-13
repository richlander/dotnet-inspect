using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Allocation-free signature type provider that only detects pointer types.
/// Preserves degraded-decode evidence separately from a definite pointer match.
/// </summary>
internal sealed class PointerDetector : ISignatureTypeProvider<PointerDetection, object?>
{
    public static PointerDetector Instance { get; } = new();

    public PointerDetection GetPrimitiveType(PrimitiveTypeCode typeCode) => default;
    public PointerDetection GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => default;
    public PointerDetection GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => default;

    public PointerDetection GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out int blobLength))
            return PointerDetection.Degraded;
        try
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        }
        finally
        {
            TypeSpecGuard.Exit(blobLength);
        }
    }

    public PointerDetection GetSZArrayType(PointerDetection elementType) => elementType;
    public PointerDetection GetArrayType(PointerDetection elementType, ArrayShape shape) => elementType;
    public PointerDetection GetByReferenceType(PointerDetection elementType) => elementType;
    public PointerDetection GetPointerType(PointerDetection elementType)
        => new(HasPointer: true, elementType.IsDegraded);
    public PointerDetection GetGenericInstantiation(
        PointerDetection genericType,
        ImmutableArray<PointerDetection> typeArguments)
        => PointerDetection.Combine(genericType, typeArguments);
    public PointerDetection GetGenericMethodParameter(object? context, int index) => default;
    public PointerDetection GetGenericTypeParameter(object? context, int index) => default;
    public PointerDetection GetFunctionPointerType(MethodSignature<PointerDetection> signature)
        => new(
            HasPointer: true,
            signature.ReturnType.IsDegraded
                || signature.ParameterTypes.Any(static type => type.IsDegraded));
    public PointerDetection GetModifiedType(
        PointerDetection modifier,
        PointerDetection unmodifiedType,
        bool isRequired)
        => new(
            modifier.HasPointer || unmodifiedType.HasPointer,
            modifier.IsDegraded || unmodifiedType.IsDegraded);
    public PointerDetection GetPinnedType(PointerDetection elementType) => elementType;
}

internal readonly record struct PointerDetection(bool HasPointer, bool IsDegraded)
{
    public static PointerDetection Degraded => new(HasPointer: false, IsDegraded: true);

    public static PointerDetection Combine(
        PointerDetection first,
        ImmutableArray<PointerDetection> rest)
        => new(
            first.HasPointer || rest.Any(static type => type.HasPointer),
            first.IsDegraded || rest.Any(static type => type.IsDegraded));
}
