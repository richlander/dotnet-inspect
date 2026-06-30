using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>A decoded signature type carried as its evaluation-stack lattice form, keeping <c>void</c> distinct from <see cref="StackType.Unknown"/>.</summary>
public readonly record struct SigType(StackType Stack, bool IsVoid)
{
    public static readonly SigType Void = new(StackType.Unknown, true);
    public static SigType Of(StackType stack) => new(stack, false);
}

/// <summary>
/// Maps SRM signature elements to the coarse <see cref="StackType"/> lattice. Stays metadata-light:
/// it never resolves across assemblies. Value-type vs object-reference is decided from a
/// <see cref="TypeDefinition"/>'s base type when available; cross-assembly references default to
/// object-reference (the honest coarse answer for the receiver-type demonstration).
/// </summary>
internal sealed class StackTypeSignatureProvider : ISignatureTypeProvider<SigType, object?>
{
    readonly MetadataReader _reader;

    public StackTypeSignatureProvider(MetadataReader reader) => _reader = reader;

    public SigType GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Char
            or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte
            or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16
            or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 => SigType.Of(StackType.Int32),
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 => SigType.Of(StackType.Int64),
        PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr => SigType.Of(StackType.NativeInt),
        PrimitiveTypeCode.Single or PrimitiveTypeCode.Double => SigType.Of(StackType.Float),
        PrimitiveTypeCode.String or PrimitiveTypeCode.Object => SigType.Of(StackType.ObjectReference),
        PrimitiveTypeCode.TypedReference => SigType.Of(StackType.TypedReference),
        PrimitiveTypeCode.Void => SigType.Void,
        _ => SigType.Of(StackType.Unknown),
    };

    public SigType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => SigType.Of(ClassifyDefinition(handle));

    public SigType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => SigType.Of(StackType.ObjectReference);

    public SigType GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public SigType GetSZArrayType(SigType elementType) => SigType.Of(StackType.ObjectReference);
    public SigType GetArrayType(SigType elementType, ArrayShape shape) => SigType.Of(StackType.ObjectReference);
    public SigType GetPointerType(SigType elementType) => SigType.Of(StackType.UnmanagedPointer);
    public SigType GetByReferenceType(SigType elementType) => SigType.Of(StackType.ManagedPointer);
    public SigType GetPinnedType(SigType elementType) => elementType;
    public SigType GetFunctionPointerType(MethodSignature<SigType> signature) => SigType.Of(StackType.NativeInt);
    public SigType GetGenericInstantiation(SigType genericType, ImmutableArray<SigType> typeArguments) => genericType;
    public SigType GetGenericMethodParameter(object? genericContext, int index) => SigType.Of(StackType.Unknown);
    public SigType GetGenericTypeParameter(object? genericContext, int index) => SigType.Of(StackType.Unknown);
    public SigType GetModifiedType(SigType modifier, SigType unmodifiedType, bool isRequired) => unmodifiedType;

    /// <summary>Classifies a type token (TypeDef/Ref/Spec) to its stack lattice form.</summary>
    public StackType Classify(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => ClassifyDefinition((TypeDefinitionHandle)handle),
        HandleKind.TypeReference => StackType.ObjectReference,
        HandleKind.TypeSpecification => _reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null).Stack,
        _ => StackType.Unknown,
    };

    StackType ClassifyDefinition(TypeDefinitionHandle handle)
    {
        var definition = _reader.GetTypeDefinition(handle);
        if (definition.BaseType.IsNil)
            return StackType.ObjectReference;
        var (ns, name) = BaseTypeName(definition.BaseType);
        return ns == "System" && name is "ValueType" or "Enum"
            ? StackType.ValueType
            : StackType.ObjectReference;
    }

    (string Namespace, string Name) BaseTypeName(EntityHandle baseType) => baseType.Kind switch
    {
        HandleKind.TypeReference => Names(_reader.GetTypeReference((TypeReferenceHandle)baseType).Namespace,
            _reader.GetTypeReference((TypeReferenceHandle)baseType).Name),
        HandleKind.TypeDefinition => Names(_reader.GetTypeDefinition((TypeDefinitionHandle)baseType).Namespace,
            _reader.GetTypeDefinition((TypeDefinitionHandle)baseType).Name),
        _ => ("", ""),
    };

    (string, string) Names(StringHandle ns, StringHandle name)
        => (_reader.GetString(ns), _reader.GetString(name));
}
