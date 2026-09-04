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

    public static MemorySafetyPointerEvidence ReadMember(
        MetadataReader reader,
        EntityHandle member)
    {
        try
        {
            PointerDetection detection;
            bool degraded = false;
            switch (member.Kind)
            {
                case HandleKind.MethodDefinition:
                    var method = GuardedProviderDecode.MethodResult(
                        reader,
                        reader.GetMethodDefinition((MethodDefinitionHandle)member),
                        Instance,
                        (object?)null,
                        PointerDetection.Degraded);
                    detection = PointerDetection.Combine(
                        method.Value.ReturnType, method.Value.ParameterTypes);
                    degraded = method.IsDegraded;
                    break;
                case HandleKind.PropertyDefinition:
                    var property = GuardedProviderDecode.PropertyResult(
                        reader,
                        reader.GetPropertyDefinition((PropertyDefinitionHandle)member),
                        Instance,
                        (object?)null,
                        PointerDetection.Degraded);
                    detection = PointerDetection.Combine(
                        property.Value.ReturnType, property.Value.ParameterTypes);
                    degraded = property.IsDegraded;
                    break;
                case HandleKind.FieldDefinition:
                    var field = GuardedProviderDecode.FieldResult(
                        reader,
                        reader.GetFieldDefinition((FieldDefinitionHandle)member),
                        Instance,
                        (object?)null,
                        PointerDetection.Degraded);
                    detection = field.Value;
                    degraded = field.IsDegraded;
                    break;
                case HandleKind.EventDefinition:
                    EntityHandle eventType = reader.GetEventDefinition(
                        (EventDefinitionHandle)member).Type;
                    detection = eventType.Kind switch
                    {
                        HandleKind.TypeDefinition or HandleKind.TypeReference =>
                            default,
                        HandleKind.TypeSpecification =>
                            GuardedProviderDecode.TypeSpec(
                                reader,
                                (TypeSpecificationHandle)eventType,
                                Instance,
                                (object?)null,
                                PointerDetection.Degraded),
                        _ => PointerDetection.Degraded,
                    };
                    break;
                default:
                    return MemorySafetyPointerEvidence.Unavailable;
            }

            return detection.HasPointer
                ? MemorySafetyPointerEvidence.Present
                : degraded || detection.IsDegraded
                    ? MemorySafetyPointerEvidence.Unavailable
                    : MemorySafetyPointerEvidence.Absent;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return MemorySafetyPointerEvidence.Unavailable;
        }
    }

    public PointerDetection GetPrimitiveType(PrimitiveTypeCode typeCode) => default;
    public PointerDetection GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => default;
    public PointerDetection GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => default;

    public PointerDetection GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            return PointerDetection.Degraded;
        using (scope)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
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
