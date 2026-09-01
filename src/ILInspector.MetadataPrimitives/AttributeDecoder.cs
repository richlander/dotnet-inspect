using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.ExceptionServices;

namespace ILInspector.Metadata;

/// <summary>
/// The SRM-only decode half of custom-attribute reading: resolves an
/// attribute's type name and decodes its constructor blob to typed argument
/// values. Rendering those values to C# (or any other surface) is the caller's
/// concern — this layer is shared by anything that needs attribute data.
/// </summary>
public static class AttributeDecoder
{
    internal sealed class MaterializationContext(Action<int> observe)
    {
        Dictionary<string, TypeDefinitionHandle>? _typeDefinitionsByName;
        ExceptionDispatchInfo? _typeDefinitionsByNameFailure;
        MetadataTypeNameFailure? _typeDefinitionsByNameFailureModel;
        bool _typeDefinitionsByNameObserverFailure;

        public void Observe(int characters) => observe(characters);

        public bool TryGetCachedIndexFailure(
            [NotNullWhen(true)] out MetadataTypeNameFailure? failure)
        {
            if (_typeDefinitionsByNameFailureModel is not null
                && !_typeDefinitionsByNameObserverFailure)
            {
                failure = _typeDefinitionsByNameFailureModel;
                return true;
            }

            failure = null;
            return false;
        }

        public Dictionary<string, TypeDefinitionHandle> GetOrCreateTypeDefinitionsByName(
            Func<Dictionary<string, TypeDefinitionHandle>> create)
        {
            if (_typeDefinitionsByName is not null)
                return _typeDefinitionsByName;
            if (_typeDefinitionsByNameFailure is not null)
            {
                if (_typeDefinitionsByNameObserverFailure)
                {
                    throw new MaterializationObserverException(
                        _typeDefinitionsByNameFailure);
                }
                _typeDefinitionsByNameFailure.Throw();
            }

            try
            {
                return _typeDefinitionsByName = create();
            }
            catch (MaterializationObserverException ex)
            {
                _typeDefinitionsByNameFailure = ex.Failure;
                _typeDefinitionsByNameObserverFailure = true;
                throw;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                _typeDefinitionsByNameFailure =
                    ExceptionDispatchInfo.Capture(ex);
                _typeDefinitionsByNameFailureModel =
                    ex is TypeDefinitionIndexException index
                        ? index.Failure
                        : MetadataTypeNameFailure.Malformed(default, ex.Message);
                throw;
            }
        }
    }

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
            return TypeResolver.GetTypeName(
                reader,
                memberRef.Parent,
                context: null,
                beforeMaterialize);
        }
        if (constructorHandle.Kind == HandleKind.MethodDefinition)
        {
            var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)constructorHandle);
            TypeDefinitionHandle declaringType = methodDef.GetDeclaringType();
            return TypeResolver.GetTypeNameFromDefinition(
                reader,
                declaringType,
                beforeMaterialize);
        }
        return null;
    }

    internal static bool TryGetAttributeTypeAssemblyReference(
        MetadataReader reader,
        EntityHandle constructorHandle,
        string fullTypeName,
        out AssemblyReferenceHandle assemblyReference,
        Action<int>? beforeMaterialize = null)
    {
        assemblyReference = default;
        if (GetAttributeTypeName(
                reader,
                constructorHandle,
                beforeMaterialize)
            != fullTypeName)
        {
            return false;
        }

        EntityHandle declaringType = constructorHandle.Kind switch
        {
            HandleKind.MemberReference =>
                reader.GetMemberReference(
                    (MemberReferenceHandle)constructorHandle).Parent,
            HandleKind.MethodDefinition =>
                reader.GetMethodDefinition(
                    (MethodDefinitionHandle)constructorHandle)
                    .GetDeclaringType(),
            _ => default,
        };
        if (declaringType.IsNil)
            return false;

        if (declaringType.Kind != HandleKind.TypeReference)
            return false;

        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        bool resolved = MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    (TypeReferenceHandle)declaringType,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _)
            && terminal.Kind == HandleKind.AssemblyReference;
        if (resolved)
            assemblyReference = (AssemblyReferenceHandle)terminal;
        return resolved;
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
            preserveSerializedTypeNames: false,
            beforeMaterialize: null,
            enumUnderlyingType: null);

    public static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            beforeMaterialize,
            enumUnderlyingType: null);

    /// <summary>
    /// Decodes an attribute, consulting <paramref name="enumUnderlyingType"/>
    /// for serialized enum names that are not TypeDefs in
    /// <paramref name="reader"/>. The resolver receives the exact metadata
    /// name SRM's provider derives from the blob: the assembly suffix removed,
    /// reflection escapes restored, and nested segments joined with <c>.</c>.
    /// The pre-decode guard asks with that same projection, so a guard skip
    /// and this decode never select different widths.
    /// </summary>
    public static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            beforeMaterialize,
            enumUnderlyingType);

    internal static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize,
        IReadOnlyDictionary<string, PrimitiveTypeCode>
            trustedExternalEnumUnderlyingTypes)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            beforeMaterialize,
            TrustedResolver(trustedExternalEnumUnderlyingTypes));

    /// <summary>
    /// Decodes an attribute while preserving the complete serialized names of
    /// <see cref="Type"/> fixed arguments, including nesting and assembly syntax.
    /// </summary>
    public static CustomAttributeValue<string>? TryDecodePreservingSerializedTypeNames(
        MetadataReader reader,
        CustomAttribute attribute)
        => TryDecodePreservingSerializedTypeNames(
            reader,
            attribute,
            beforeMaterialize: null);

    public static CustomAttributeValue<string>? TryDecodePreservingSerializedTypeNames(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: true,
            beforeMaterialize,
            enumUnderlyingType: null);

    internal static CustomAttributeValue<string>?
        TryDecodePreservingSerializedTypeNames(
            MetadataReader reader,
            CustomAttribute attribute,
            Action<int>? beforeMaterialize,
            IReadOnlyDictionary<string, PrimitiveTypeCode>
                trustedExternalEnumUnderlyingTypes)
        => TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: true,
            beforeMaterialize,
            TrustedResolver(trustedExternalEnumUnderlyingTypes));

    static CustomAttributeValue<string>? TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        bool preserveSerializedTypeNames,
        Action<int>? beforeMaterialize,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType)
    {
        var provider = new ArgTypeProvider(
            reader,
            preserveSerializedTypeNames,
            beforeMaterialize,
            enumUnderlyingType);
        try
        {
            if (!CustomAttributeValueGuard.IsSafeToDecode(
                    reader,
                    attribute,
                    beforeMaterialize,
                    provider.GetUnderlyingEnumType))
                return null;
        }
        catch (MaterializationObserverException ex)
        {
            ex.Rethrow();
            throw;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return null;
        }

        try
        {
            return attribute.DecodeValue(provider);
        }
        catch (MaterializationObserverException ex)
        {
            ex.Rethrow();
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Adapts a trusted, closed set of external enum widths to the resolver
    /// shape. Names outside the set resolve to
    /// <see cref="PrimitiveTypeCode.Int32"/>, the same default an absent
    /// resolver produces, so an unrecognized cross-assembly enum is never
    /// given an attacker-chosen width.
    /// </summary>
    static Func<string, PrimitiveTypeCode> TrustedResolver(
        IReadOnlyDictionary<string, PrimitiveTypeCode> trusted)
        => name => trusted.TryGetValue(name, out PrimitiveTypeCode width)
            ? width
            : PrimitiveTypeCode.Int32;

    /// <summary>
    /// Binds a caller enum-width resolver to the same local-TypeDef-first,
    /// <see cref="EnumUnderlyingPrimitive.Normalize"/> oracle
    /// <see cref="ArgTypeProvider.GetUnderlyingEnumType"/> uses so a direct
    /// <c>IsSafeToDecode(..., resolver)</c> skip cannot diverge from
    /// <c>DecodeValue</c>.
    /// </summary>
    internal static Func<string, PrimitiveTypeCode> BindEnumWidthResolver(
        MetadataReader reader,
        Action<int>? beforeMaterialize,
        Func<string, PrimitiveTypeCode> enumUnderlyingType)
        => enumUnderlyingType.Target is ArgTypeProvider
            ? enumUnderlyingType
            : new ArgTypeProvider(
                reader,
                preserveSerializedTypeNames: false,
                beforeMaterialize,
                enumUnderlyingType).GetUnderlyingEnumType;

    /// <summary>
    /// Applies the same projection SRM applies to a blob-authored serialized
    /// enum name before it calls <c>GetUnderlyingEnumType</c>. SRM resolves the
    /// SerString through <see cref="ArgTypeProvider.GetTypeFromSerializedName"/>
    /// first, so a guard that consults the width oracle with the raw name asks a
    /// different question whenever those two spellings normalize differently —
    /// including names that only parse after the assembly suffix is removed.
    /// Composing the same two steps keeps the guard skip and
    /// <c>DecodeValue</c> on one width by construction rather than by relying on
    /// two normalizations agreeing.
    /// </summary>
    internal static string ProjectSerializedEnumName(
        Func<string, PrimitiveTypeCode>? enumUnderlyingType,
        string name)
        => enumUnderlyingType?.Target is ArgTypeProvider provider
            ? provider.GetTypeFromSerializedName(name)
            : EnumUnderlyingPrimitive.WithoutAssemblyQualification(name);

    /// <summary>Type provider for attribute-blob decoding: primitives as C# keywords, everything else as its full name (enums and typeof targets).</summary>
    internal sealed class ArgTypeProvider(
        MetadataReader reader,
        bool preserveSerializedTypeNames,
        Action<int>? beforeMaterialize,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType) : ICustomAttributeTypeProvider<string>
    {
        Dictionary<string, TypeDefinitionHandle>? _typeDefinitionsByName;
        bool _lastNameFromBlob;
        TypeDefinitionHandle _pendingDefinition;
        TypeReferenceHandle _pendingReference;
        MetadataReader? _pendingReader;
        readonly MaterializationContext? _materializationContext =
            beforeMaterialize?.Target as MaterializationContext;

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

        public string GetSystemType() => SystemTypeArgumentName.Rendered;
        public bool IsSystemType(string type) => SystemTypeArgumentName.Matches(type);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            _lastNameFromBlob = false;
            // Remember the definition itself, not just its rendered name, and
            // the reader it belongs to. The pre-decode guard resolves a
            // definition-typed enum straight from this handle, so resolving the
            // width from the same handle here is what keeps the two sides on
            // one width; a rendered name cannot carry that identity, because
            // distinct definitions can render to the same string.
            _pendingDefinition = handle;
            _pendingReference = default;
            _pendingReader = r;
            return TypeResolver.GetTypeNameFromDefinition(r, handle, ObserveBeforeMaterialize);
        }

        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind)
        {
            _lastNameFromBlob = false;
            // A reference carries a resolution scope that its flattened
            // spelling discards, so remember the reference and let the enum
            // lookup resolve it structurally, exactly as the guard does. The
            // handle is recorded rather than resolved here because most
            // references in an attribute blob name a typeof target rather than
            // an enum, and resolving one costs a scan of the definition table.
            _pendingDefinition = default;
            _pendingReference = handle;
            _pendingReader = r;
            return TypeResolver.GetTypeName(
                r,
                handle,
                context: null,
                beforeMaterialize: ObserveBeforeMaterialize) ?? "object";
        }

        public string GetTypeFromSerializedName(string name)
        {
            // Record that the name produced most recently came from the blob.
            // SRM asks for a type name and then immediately asks for that
            // name's underlying enum type, so this tracks the provenance of the
            // pending lookup rather than remembering spellings. A set would
            // accumulate, and a spelling that is legitimately handle-derived
            // later in the same blob would then be resolved as blob syntax.
            _lastNameFromBlob = true;
            _pendingDefinition = default;
            _pendingReference = default;
            _pendingReader = null;
            return preserveSerializedTypeNames
                ? name
                : EnumUnderlyingPrimitive.WithoutAssemblyQualification(name);
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            // A handle-typed argument is resolved from the definition the
            // signature named, never from its rendered name. Nested types join
            // their declaring type with '.', the same separator used between a
            // namespace and a type name, so distinct definitions can render to
            // one string and a name index must drop one of them; a reference
            // also carries a resolution scope that its spelling discards. The
            // guard resolves the same argument from the same handle through the
            // same function, so taking the handle here keeps both sides on one
            // width by construction.
            //
            // A blob-authored name is reflection syntax and is normalized
            // first; a handle-derived name that has no pending definition is an
            // exact metadata spelling and is matched verbatim before being
            // normalized.
            bool fromBlob = _lastNameFromBlob;
            TypeDefinitionHandle pending = _pendingDefinition;
            TypeReferenceHandle pendingReference = _pendingReference;
            MetadataReader? pendingReader = _pendingReader;
            _lastNameFromBlob = false;
            _pendingDefinition = default;
            _pendingReference = default;
            _pendingReader = null;

            if (pendingReader is not null)
            {
                if (!pending.IsNil)
                    return EnumUnderlyingPrimitive.FromDefinition(pendingReader, pending);
                if (!pendingReference.IsNil
                    && EnumUnderlyingPrimitive.TryResolveDefinition(
                        pendingReader,
                        pendingReference,
                        out TypeDefinitionHandle referenced))
                {
                    return EnumUnderlyingPrimitive.FromDefinition(pendingReader, referenced);
                }
            }

            if (!fromBlob && TypeDefinitionsByName.TryGetValue(type, out var exact))
                return EnumUnderlyingPrimitive.FromDefinition(reader, exact);

            string normalized = EnumUnderlyingPrimitive.NormalizeSerializedName(type);
            if (TypeDefinitionsByName.TryGetValue(normalized, out var handle))
                return EnumUnderlyingPrimitive.FromDefinition(reader, handle);

            return enumUnderlyingType is not null
                ? EnumUnderlyingPrimitive.Normalize(enumUnderlyingType(normalized))
                : PrimitiveTypeCode.Int32;
        }

        Dictionary<string, TypeDefinitionHandle> TypeDefinitionsByName =>
            _materializationContext?.GetOrCreateTypeDefinitionsByName(
                BuildTypeDefinitionIndex)
            ?? (_typeDefinitionsByName ??= BuildTypeDefinitionIndex());

        Dictionary<string, TypeDefinitionHandle> BuildTypeDefinitionIndex()
        {
            ObserveBeforeMaterialize(reader.TypeDefinitions.Count);
            var result = new Dictionary<string, TypeDefinitionHandle>(
                reader.TypeDefinitions.Count,
                StringComparer.Ordinal);
            foreach (var handle in reader.TypeDefinitions)
            {
                string name;
                try
                {
                    name = TypeResolver.GetTypeNameFromDefinition(
                        reader,
                        handle,
                        ObserveBeforeMaterialize);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException or ArgumentOutOfRangeException)
                {
                    throw new TypeDefinitionIndexException(
                        MetadataTypeNameFailure.Malformed(handle, ex.Message));
                }

                result.TryAdd(name, handle);
            }
            return result;
        }

        void ObserveBeforeMaterialize(int characters)
        {
            try
            {
                beforeMaterialize?.Invoke(characters);
            }
            catch (Exception ex)
            {
                throw new MaterializationObserverException(
                    ExceptionDispatchInfo.Capture(ex));
            }
        }
    }

    internal sealed class TypeDefinitionIndexException(MetadataTypeNameFailure failure)
        : BadImageFormatException(failure.Detail)
    {
        public MetadataTypeNameFailure Failure { get; } = failure;
    }

    sealed class MaterializationObserverException(ExceptionDispatchInfo failure)
        : Exception(null, failure.SourceException)
    {
        public ExceptionDispatchInfo Failure { get; } = failure;

        public void Rethrow() =>
            Failure.Throw();
    }

}
