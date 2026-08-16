using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// The SRM-only decode half of custom-attribute reading: resolves an
/// attribute's type name and decodes its constructor blob to typed argument
/// values. Rendering those values to C# (or any other surface) is the caller's
/// concern — this layer is shared by anything that needs attribute data.
/// </summary>
public static class AttributeDecoder
{
    /// <summary>
    /// The fully qualified type name of an attribute, from its constructor handle.
    /// </summary>
    public static string? GetAttributeTypeName(
        MetadataReader reader,
        EntityHandle constructorHandle)
        => GetAttributeTypeName(
            reader,
            constructorHandle,
            beforeMaterialize: null);

    public static string? GetAttributeTypeName(
        MetadataReader reader,
        EntityHandle constructorHandle,
        Action<int>? beforeMaterialize)
    {
        if (constructorHandle.Kind == HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((MemberReferenceHandle)constructorHandle);
            if (!TryObserveTypeName(reader, memberRef.Parent, beforeMaterialize))
            {
                throw new BadImageFormatException(
                    "The attribute constructor parent is not a bounded named type.");
            }
            return TypeResolver.GetTypeName(reader, memberRef.Parent);
        }
        if (constructorHandle.Kind == HandleKind.MethodDefinition)
        {
            var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)constructorHandle);
            TypeDefinitionHandle declaringType = methodDef.GetDeclaringType();
            if (!TryObserveTypeName(reader, declaringType, beforeMaterialize))
            {
                throw new BadImageFormatException(
                    "The attribute constructor declaring type is not bounded.");
            }
            return TypeResolver.GetTypeNameFromDefinition(reader, declaringType);
        }
        return null;
    }

    static bool TryObserveTypeName(
        MetadataReader reader,
        EntityHandle handle,
        Action<int>? beforeMaterialize)
    {
        if (beforeMaterialize is null)
            return true;

        for (int count = 0;
             count < MetadataSafetyPolicy.MaxRelationshipNodes;
             count++)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeReference:
                    TypeReference typeReference =
                        reader.GetTypeReference((TypeReferenceHandle)handle);
                    beforeMaterialize(
                        reader.GetBlobReader(typeReference.Name).Length
                            + reader.GetBlobReader(typeReference.Namespace).Length);
                    if (typeReference.ResolutionScope.Kind != HandleKind.TypeReference)
                        return true;
                    handle = typeReference.ResolutionScope;
                    break;
                case HandleKind.TypeDefinition:
                    TypeDefinition typeDefinition =
                        reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    beforeMaterialize(
                        reader.GetBlobReader(typeDefinition.Name).Length
                            + reader.GetBlobReader(typeDefinition.Namespace).Length);
                    TypeDefinitionHandle declaringType =
                        typeDefinition.GetDeclaringType();
                    if (declaringType.IsNil)
                        return true;
                    handle = declaringType;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Decodes an attribute's fixed and named arguments to typed values, or null
    /// when the blob cannot be decoded. Argument <c>Type</c> strings are C#
    /// keywords for primitives, <c>System.Type</c> for typeof targets, and the
    /// full type name otherwise (enums, etc.).
    /// </summary>
    public static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false);

    /// <summary>
    /// Decodes an attribute while preserving the complete serialized names of
    /// <see cref="Type"/> fixed arguments, including nesting and assembly syntax.
    /// </summary>
    public static CustomAttributeValue<string>? TryDecodePreservingSerializedTypeNames(
        MetadataReader reader,
        CustomAttribute attribute)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: true);

    static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        bool preserveSerializedTypeNames)
    {
        try
        {
            return attribute.DecodeValue(
                new ArgTypeProvider(reader, preserveSerializedTypeNames));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Type provider for attribute-blob decoding: primitives as C# keywords, everything else as its full name (enums and typeof targets).</summary>
    sealed class ArgTypeProvider(
        MetadataReader reader,
        bool preserveSerializedTypeNames) : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            _ => "object",
        };

        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeResolver.GetTypeNameFromDefinition(r, handle);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeResolver.GetTypeName(r, handle) ?? "object";
        public string GetTypeFromSerializedName(string name)
        {
            if (preserveSerializedTypeNames)
                return name;
            int comma = name.IndexOf(',');
            return comma >= 0 ? name[..comma] : name;
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                var def = reader.GetTypeDefinition(handle);
                if (TypeResolver.GetTypeNameFromDefinition(reader, handle) != type)
                    continue;
                foreach (var fieldHandle in def.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.Static) != 0)
                        continue;

                    // SRM decodes this field signature on the native stack before the
                    // first provider callback, so an over-deep enum field blob would
                    // overflow uncatchably. Prescan and fail closed to Int32.
                    return SignatureBlobGuard.IsSafeToDecode(reader, field.Signature, SignatureBlobGuard.Kind.Field)
                        ? field.DecodeSignature(new PrimitiveCodeProvider(), null)
                        : PrimitiveTypeCode.Int32;
                }
            }
            return PrimitiveTypeCode.Int32;
        }
    }

    /// <summary>Minimal signature provider that reports an enum's underlying primitive type code.</summary>
    sealed class PrimitiveCodeProvider : ISignatureTypeProvider<PrimitiveTypeCode, object?>
    {
        public PrimitiveTypeCode GetPrimitiveType(PrimitiveTypeCode code) => code;
        public PrimitiveTypeCode GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetSZArrayType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetArrayType(PrimitiveTypeCode e, ArrayShape s) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetByReferenceType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetPointerType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericInstantiation(PrimitiveTypeCode g, ImmutableArray<PrimitiveTypeCode> a) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericMethodParameter(object? ctx, int i) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericTypeParameter(object? ctx, int i) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetModifiedType(PrimitiveTypeCode m, PrimitiveTypeCode u, bool r) => u;
        public PrimitiveTypeCode GetPinnedType(PrimitiveTypeCode e) => e;
        public PrimitiveTypeCode GetFunctionPointerType(MethodSignature<PrimitiveTypeCode> s) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromSpecification(MetadataReader r, object? ctx, TypeSpecificationHandle h, byte k) => PrimitiveTypeCode.Int32;
    }
}
