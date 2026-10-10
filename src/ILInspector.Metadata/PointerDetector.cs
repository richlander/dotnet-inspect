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
            return DecodeMember(reader, member);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return MemorySafetyPointerEvidence.Unavailable;
        }
    }

    /// <param name="work">
    /// Receives the member signature and every TypeSpec blob decoded from it,
    /// when the caller receipts signature work.
    /// </param>
    internal static MemorySafetyPointerEvidence DecodeMember(
        MetadataReader reader,
        EntityHandle member,
        MemorySafetyMetadataWorkRecorder? work = null)
    {
        PointerDetection detection;
        bool degraded = false;
        switch (member.Kind)
        {
            case HandleKind.MethodDefinition:
                MethodDefinition methodDefinition =
                    reader.GetMethodDefinition((MethodDefinitionHandle)member);
                ObserveBlob(reader, methodDefinition.Signature, work);
                var method = GuardedProviderDecode.MethodResult(
                    reader,
                    methodDefinition,
                    Instance,
                    work,
                    PointerDetection.Degraded);
                detection = PointerDetection.Combine(
                    method.Value.ReturnType, method.Value.ParameterTypes);
                degraded = method.IsDegraded;
                break;
            case HandleKind.PropertyDefinition:
                PropertyDefinition propertyDefinition =
                    reader.GetPropertyDefinition(
                        (PropertyDefinitionHandle)member);
                ObserveBlob(reader, propertyDefinition.Signature, work);
                var property = GuardedProviderDecode.PropertyResult(
                    reader,
                    propertyDefinition,
                    Instance,
                    work,
                    PointerDetection.Degraded);
                detection = PointerDetection.Combine(
                    property.Value.ReturnType, property.Value.ParameterTypes);
                degraded = property.IsDegraded;
                break;
            case HandleKind.FieldDefinition:
                FieldDefinition fieldDefinition =
                    reader.GetFieldDefinition((FieldDefinitionHandle)member);
                ObserveBlob(reader, fieldDefinition.Signature, work);
                var field = GuardedProviderDecode.FieldResult(
                    reader,
                    fieldDefinition,
                    Instance,
                    work,
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
                        DecodeEventTypeSpec(
                            reader,
                            (TypeSpecificationHandle)eventType,
                            work),
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

    public PointerDetection GetPrimitiveType(PrimitiveTypeCode typeCode) => default;
    public PointerDetection GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => default;
    public PointerDetection GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => default;

    public PointerDetection GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            return PointerDetection.Degraded;
        using (scope)
        {
            TypeSpecification specification =
                reader.GetTypeSpecification(handle);
            ObserveBlob(
                reader,
                specification.Signature,
                context as MemorySafetyMetadataWorkRecorder);
            return specification.DecodeSignature(this, context);
        }
    }

    static PointerDetection DecodeEventTypeSpec(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        MemorySafetyMetadataWorkRecorder? work)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            return PointerDetection.Degraded;
        using (scope)
        {
            TypeSpecification specification =
                reader.GetTypeSpecification(handle);
            ObserveBlob(reader, specification.Signature, work);
            return specification.DecodeSignature(Instance, work);
        }
    }

    static void ObserveBlob(
        MetadataReader reader,
        BlobHandle blob,
        MemorySafetyMetadataWorkRecorder? work)
    {
        if (work is null)
            return;

        // An unreadable blob is left to the guarded decode, so receipting
        // never changes the evidence the decode reports.
        try
        {
            work.ObserveSignatureBytes(reader.GetBlobReader(blob).Length);
        }
        catch (BadImageFormatException)
        {
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
