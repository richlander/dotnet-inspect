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
/// <see cref="PrimitiveTypeCode.Int32"/> so the width stays sound, unless a
/// caller-supplied resolver found the defining image first. A local TypeDef
/// still wins over that resolver. For a handle the decoder resolves the width
/// directly from the definition; for a blob-authored SerString it strips the
/// assembly qualification and restores reflection escapes before lookup. Both
/// then <see cref="Normalize"/> the returned code so an assembly-qualified
/// SerString or a non-fixed-width callback cannot select an unexpected width.
/// <c>CustomAttributeValueGuardTests</c>'s
/// <c>EscapedNamedEnum_MalformedAssemblySuffix_SeesOverlappingHostileCount</c>
/// and <c>EscapedNamedEnum_OverBudgetAssemblySuffix_SeesOverlappingHostileCount</c>
/// gate that resolution.
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
                    != "System.Enum"
                || definition.GetGenericParameters().Count != 0)
            {
                return false;
            }

            bool found = false;
            foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Static) != 0)
                {
                    // Every static field of an enum is one of its named
                    // constants, so it must be a literal. Anything else is a
                    // shape the CLI does not admit for an enum.
                    if ((field.Attributes & FieldAttributes.Literal) == 0)
                        return false;
                    continue;
                }
                const FieldAttributes RequiredAttributes =
                    FieldAttributes.SpecialName
                    | FieldAttributes.RTSpecialName;
                // `value__` holds the enum's value at runtime, so it is a real
                // instance slot. Literal implies static in the CLI, and a
                // literal instance field is a shape no valid enum can carry.
                if (found
                    || (field.Attributes & FieldAttributes.Literal) != 0
                    || (field.Attributes & RequiredAttributes)
                        != RequiredAttributes
                    || (field.Attributes & FieldAttributes.FieldAccessMask)
                        != FieldAttributes.Public
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
    /// Resolves a signature-named type handle to the definition it denotes in
    /// this reader, structurally: a definition handle denotes itself, and a
    /// reference is matched by name and resolution scope rather than by its
    /// rendered spelling. The guard and the decoder both ask this one question
    /// about the same handle, so neither can select a different definition --
    /// and therefore a different width -- than the other. A reference to a type
    /// this reader does not define has no local definition and resolves
    /// elsewhere.
    /// </summary>
    public static bool TryResolveDefinition(
        MetadataReader reader,
        EntityHandle handle,
        out TypeDefinitionHandle definition)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            definition = (TypeDefinitionHandle)handle;
            return true;
        }

        if (handle.Kind == HandleKind.TypeReference)
            return TryFindDefinition(reader, (TypeReferenceHandle)handle, out definition);

        definition = default;
        return false;
    }

    /// <summary>
    /// Projects a reflection-serialized name to the exact metadata index key:
    /// assembly qualification is removed, escaped metadata characters are
    /// restored, and nested segments use <c>.</c>.
    /// </summary>
    public static string NormalizeSerializedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TryParse(name, out TypeName? parsed)
            || !parsed.IsSimple)
        {
            return LegacyNormalize(name);
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        TypeName current = parsed;
        while (true)
        {
            if (!current.IsSimple)
                return LegacyNormalize(name);

            segments.Add(TypeName.Unescape(current.Name));
            if (!current.IsNested)
                break;
            current = current.DeclaringType;
        }

        var rootToLeaf = ImmutableArray.CreateBuilder<string>(segments.Count);
        for (int i = segments.Count - 1; i >= 0; i--)
            rootToLeaf.Add(segments[i]);

        string typeName = string.Join(".", rootToLeaf);
        string ns = TypeName.Unescape(current.Namespace);
        return ns.Length == 0 ? typeName : ns + "." + typeName;
    }

    public static string WithoutAssemblyQualification(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (TryParse(name, out TypeName? parsed))
            return parsed.FullName;

        int comma = name.IndexOf(',');
        return comma >= 0 ? name[..comma] : name;
    }

    static bool TryParse(
        string name,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out TypeName? parsed)
    {
        if (name.Length > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            parsed = null;
            return false;
        }

        return TypeName.TryParse(
            name,
            out parsed,
            new TypeNameParseOptions
            {
                MaxNodes = MetadataSafetyPolicy.MaxRelationshipNodes,
            });
    }

    static string LegacyNormalize(string name)
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
        // A blob-authored name is a reflection type name, so its escapes are
        // meaningful and must be resolved before lookup: `E\+Kind` names the
        // metadata type `E+Kind`, not one spelled with a backslash. Only
        // handle-derived names are matched by their exact metadata spelling,
        // and those never reach this method.
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

    /// <summary>
    /// Caps <see cref="Matches"/> recursion. Each step walks outward along two
    /// chains at once -- a reference's resolution scope and a definition's
    /// declaring type -- and metadata is untrusted, so neither chain is
    /// guaranteed to reach an end. A NestedClass table naming two types as each
    /// other's declaring type, paired with two references naming each other as
    /// resolution scope, otherwise recurses until the stack is exhausted, and a
    /// stack overflow cannot be caught. Real nesting is orders of magnitude
    /// shallower than this bound.
    /// </summary>
    const int MaxNestingDepth = 128;

    static bool Matches(
        MetadataReader reader,
        TypeReferenceHandle referenceHandle,
        TypeDefinitionHandle definitionHandle,
        int depth = 0)
    {
        if (depth > MaxNestingDepth)
            return false;

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
                    enclosing,
                    depth + 1);
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
