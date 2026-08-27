using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Single width oracle for enum-typed custom-attribute arguments.
///
/// SRM asks <c>ICustomAttributeTypeProvider.GetUnderlyingEnumType</c> for a
/// name and then consumes that many value bytes. The pre-decode guard has to
/// skip the same number of bytes or every later declared count is read from
/// the wrong offset. Both callers therefore resolve a handle or serialized
/// name to a local <see cref="TypeDefinition"/> the same way. A name that is
/// not a TypeDef in the current image falls back to
/// <see cref="PrimitiveTypeCode.Int32"/> so the skip stays aligned, unless a
/// caller-supplied resolver found the defining image first. A local TypeDef
/// still wins over that resolver. Guard and SRM both consult the resolver
/// with <see cref="NormalizeSerializedName"/> of the blob name and
/// <see cref="Normalize"/> the returned code so an assembly-qualified
/// SerString or a non-fixed-width callback cannot select a different skip
/// than SRM.
/// </summary>
static class EnumUnderlyingPrimitive
{
    public static int ByteSize(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte => 1,
        PrimitiveTypeCode.Char or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 => 2,
        PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Single => 4,
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 or PrimitiveTypeCode.Double => 8,
        _ => 4,
    };

    public static PrimitiveTypeCode FromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.Static) != 0)
                continue;

            PrimitiveTypeCode code = SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    field.Signature,
                    SignatureBlobGuard.Kind.Field)
                ? field.DecodeSignature(Provider.Instance, genericContext: null)
                : PrimitiveTypeCode.Int32;
            return Normalize(code);
        }

        return PrimitiveTypeCode.Int32;
    }

    public static bool TryFromEnumDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        out PrimitiveTypeCode code)
    {
        code = default;
        try
        {
            var definition = reader.GetTypeDefinition(handle);
            if ((definition.Attributes & TypeAttributes.Sealed) == 0
                || TypeResolver.GetTypeName(reader, definition.BaseType)
                    != "System.Enum")
            {
                return false;
            }

            bool found = false;
            foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Static) != 0)
                    continue;
                const FieldAttributes RequiredAttributes =
                    FieldAttributes.SpecialName
                    | FieldAttributes.RTSpecialName;
                if (found
                    || (field.Attributes & RequiredAttributes)
                        != RequiredAttributes
                    || reader.GetString(field.Name) != "value__")
                {
                    return false;
                }

                PrimitiveTypeCode? candidate =
                    SignatureBlobGuard.IsSafeToDecode(
                        reader,
                        field.Signature,
                        SignatureBlobGuard.Kind.Field)
                        ? field.DecodeSignature(
                            Provider.Instance,
                            genericContext: null)
                        : null;
                if (candidate is not { } underlyingType
                    || !IsEnumUnderlyingType(underlyingType))
                {
                    return false;
                }

                code = underlyingType;
                found = true;
            }

            return found;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            code = default;
            return false;
        }
    }

    static bool IsEnumUnderlyingType(PrimitiveTypeCode code) => code is
        PrimitiveTypeCode.SByte
        or PrimitiveTypeCode.Byte
        or PrimitiveTypeCode.Int16
        or PrimitiveTypeCode.UInt16
        or PrimitiveTypeCode.Int32
        or PrimitiveTypeCode.UInt32
        or PrimitiveTypeCode.Int64
        or PrimitiveTypeCode.UInt64;

    /// <summary>
    /// SRM casts the provider result to <c>SerializationTypeCode</c> and
    /// consumes a SerString for <see cref="PrimitiveTypeCode.String"/>.
    /// Only fixed-width enum primitives stay; everything else, including
    /// String, falls back to <see cref="PrimitiveTypeCode.Int32"/> so the
    /// guard and decoder skip the same four bytes.
    /// </summary>
    public static PrimitiveTypeCode Normalize(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte
            or PrimitiveTypeCode.Char or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16
            or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32
            or PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 => code,
        _ => PrimitiveTypeCode.Int32,
    };

    public static PrimitiveTypeCode FromHandle(
        MetadataReader reader,
        EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
            return FromDefinition(reader, (TypeDefinitionHandle)handle);
        if (handle.Kind == HandleKind.TypeReference
            && TryFindDefinition(reader, (TypeReferenceHandle)handle, out var definition))
            return FromDefinition(reader, definition);
        return PrimitiveTypeCode.Int32;
    }

    /// <summary>
    /// Matches <c>ArgTypeProvider.GetTypeFromSerializedName</c> (strip the
    /// assembly suffix) and the metadata index key (nested types use
    /// <c>.</c>, not the serialized <c>+</c>).
    /// </summary>
    public static string NormalizeSerializedName(string name)
    {
        int comma = name.IndexOf(',');
        if (comma >= 0)
            name = name[..comma];
        return name.Replace('+', '.');
    }

    public static PrimitiveTypeCode FromSerializedName(
        MetadataReader reader,
        string name)
        => TryFromSerializedName(reader, name, out PrimitiveTypeCode code)
            ? code
            : PrimitiveTypeCode.Int32;

    /// <summary>
    /// Resolves a serialized enum name to a local TypeDef's underlying
    /// primitive. Returns <see langword="false"/> when the name is not a
    /// TypeDef in <paramref name="reader"/>, including when it is defined
    /// only in a referenced assembly.
    /// </summary>
    public static bool TryFromSerializedName(
        MetadataReader reader,
        string name,
        out PrimitiveTypeCode code)
    {
        ReadOnlySpan<char> simple = NormalizeSerializedName(name).AsSpan();
        if (TryFindDefinition(reader, simple, out var definition))
        {
            code = FromDefinition(reader, definition);
            return true;
        }

        code = default;
        return false;
    }

    static bool TryFindDefinition(
        MetadataReader reader,
        TypeReferenceHandle handle,
        out TypeDefinitionHandle definition)
    {
        foreach (var candidate in reader.TypeDefinitions)
        {
            if (Matches(reader, handle, candidate))
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    static bool TryFindDefinition(
        MetadataReader reader,
        ReadOnlySpan<char> name,
        out TypeDefinitionHandle definition)
    {
        var comparer = reader.StringComparer;
        foreach (var candidate in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(candidate);
            if (!type.GetDeclaringType().IsNil)
                continue;
            if (!comparer.Equals(type.Name, LeafName(name).ToString()))
                continue;
            if (!comparer.Equals(type.Namespace, NamespaceName(name).ToString()))
                continue;
            definition = candidate;
            return true;
        }

        definition = default;
        return false;
    }

    static bool Matches(
        MetadataReader reader,
        TypeReferenceHandle referenceHandle,
        TypeDefinitionHandle definitionHandle)
    {
        var comparer = reader.StringComparer;
        var reference = reader.GetTypeReference(referenceHandle);
        var definition = reader.GetTypeDefinition(definitionHandle);
        if (!comparer.Equals(definition.Name, reader.GetString(reference.Name)))
            return false;

        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            TypeDefinitionHandle enclosing = definition.GetDeclaringType();
            return !enclosing.IsNil
                && Matches(
                    reader,
                    (TypeReferenceHandle)reference.ResolutionScope,
                    enclosing);
        }

        return definition.GetDeclaringType().IsNil
            && comparer.Equals(
                definition.Namespace,
                reader.GetString(reference.Namespace));
    }

    static ReadOnlySpan<char> LeafName(ReadOnlySpan<char> name)
    {
        int separator = name.LastIndexOfAny('.', '+');
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    static ReadOnlySpan<char> NamespaceName(ReadOnlySpan<char> name)
    {
        int separator = name.LastIndexOf('.');
        return separator >= 0 ? name[..separator] : [];
    }

    sealed class Provider : ISignatureTypeProvider<PrimitiveTypeCode, object?>
    {
        public static readonly Provider Instance = new();

        public PrimitiveTypeCode GetPrimitiveType(PrimitiveTypeCode code) => code;
        public PrimitiveTypeCode GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetSZArrayType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetArrayType(PrimitiveTypeCode e, ArrayShape s) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetByReferenceType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetPointerType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericInstantiation(
            PrimitiveTypeCode g,
            ImmutableArray<PrimitiveTypeCode> a)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericMethodParameter(object? ctx, int i) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericTypeParameter(object? ctx, int i) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetModifiedType(PrimitiveTypeCode m, PrimitiveTypeCode u, bool r) => u;
        public PrimitiveTypeCode GetPinnedType(PrimitiveTypeCode e) => e;
        public PrimitiveTypeCode GetFunctionPointerType(MethodSignature<PrimitiveTypeCode> s)
            => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromSpecification(
            MetadataReader r,
            object? ctx,
            TypeSpecificationHandle h,
            byte k)
            => PrimitiveTypeCode.Int32;
    }
}
