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
                    throw new CallerCallbackException(
                        _typeDefinitionsByNameFailure);
                }
                _typeDefinitionsByNameFailure.Throw();
            }

            try
            {
                return _typeDefinitionsByName = create();
            }
            catch (CallerCallbackException ex)
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
    /// <paramref name="reader"/>. The resolver receives the decoder's exact
    /// metadata-name projection from the blob: the assembly suffix removed,
    /// reflection escapes restored, and nested segments joined with <c>.</c>.
    /// The owned decoder uses that projection when it selects the width during
    /// its single walk.
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
        => DecodeCore(
            reader,
            attribute,
            preserveSerializedTypeNames,
            beforeMaterialize,
            LegacyResolver(enumUnderlyingType));

    /// <summary>
    /// Decodes an attribute and additionally reports, per top-level fixed and
    /// named argument, whether any enum width within that argument defaulted to
    /// <see cref="PrimitiveTypeCode.Int32"/> because no structural, local,
    /// trusted, or caller path resolved it. This is the opt-in surface for
    /// D2's "visibly" clause; the existing <see cref="TryDecode(MetadataReader,
    /// CustomAttribute)"/> overloads report only the value.
    /// <paramref name="enumUnderlyingType"/> may report a name unresolved (by
    /// returning <see langword="false"/>), which a legacy
    /// <c>Func&lt;string, PrimitiveTypeCode&gt;</c> cannot; an unresolved name
    /// defaults and is reported set.
    /// </summary>
    public static DetailedCustomAttributeValue? TryDecodeDetailed(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize = null,
        EnumWidthResolver? enumUnderlyingType = null,
        bool preserveSerializedTypeNames = false)
    {
        try
        {
            if (!CustomAttributeValueDecoder.TryDecode(
                    reader,
                    attribute,
                    preserveSerializedTypeNames,
                    captureDefaultedWidths: true,
                    beforeMaterialize,
                    enumUnderlyingType,
                    out CustomAttributeValue<string> value,
                    out System.Collections.Immutable.ImmutableArray<bool> fixedDefaulted,
                    out System.Collections.Immutable.ImmutableArray<bool> namedDefaulted))
            {
                return null;
            }

            return new DetailedCustomAttributeValue(
                value,
                fixedDefaulted,
                namedDefaulted);
        }
        catch (CallerCallbackException ex)
        {
            ex.Rethrow();
            throw;
        }
    }

    static CustomAttributeValue<string>? DecodeCore(
        MetadataReader reader,
        CustomAttribute attribute,
        bool preserveSerializedTypeNames,
        Action<int>? beforeMaterialize,
        EnumWidthResolver? enumUnderlyingType)
    {
        try
        {
            return CustomAttributeValueDecoder.TryDecode(
                    reader,
                    attribute,
                    preserveSerializedTypeNames,
                    captureDefaultedWidths: false,
                    beforeMaterialize,
                    enumUnderlyingType,
                    out CustomAttributeValue<string> value,
                    out _,
                    out _)
                ? value
                : null;
        }
        catch (CallerCallbackException ex)
        {
            ex.Rethrow();
            throw;
        }
    }

    /// <summary>
    /// Adapts a legacy <c>Func&lt;string, PrimitiveTypeCode&gt;</c> to the
    /// resolver shape. A legacy answer is authoritative: it always reports
    /// resolved, so a defaulted-width signal is never set on its behalf.
    /// </summary>
    internal static EnumWidthResolver? LegacyResolver(
        Func<string, PrimitiveTypeCode>? enumUnderlyingType)
        => enumUnderlyingType is null
            ? null
            : (string name, out PrimitiveTypeCode width) =>
            {
                width = enumUnderlyingType(name);
                return true;
            };

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

    internal sealed class TypeDefinitionIndexException(MetadataTypeNameFailure failure)
        : BadImageFormatException(failure.Detail)
    {
        public MetadataTypeNameFailure Failure { get; } = failure;
    }

    /// <summary>
    /// Private sentinel that carries a caller callback's original exception —
    /// from the <c>beforeMaterialize</c> observer or the enum-width resolver —
    /// unchanged past the decoder's malformed-input catches so the public edge
    /// can rethrow it. Because it is not a
    /// <see cref="BadImageFormatException"/> or
    /// <see cref="ArgumentOutOfRangeException"/>, a caller callback raising
    /// either of those is never misclassified as a malformed blob (#5085,
    /// #5759).
    /// </summary>
    internal sealed class CallerCallbackException(ExceptionDispatchInfo failure)
        : Exception(null, failure.SourceException)
    {
        public ExceptionDispatchInfo Failure { get; } = failure;

        public void Rethrow() =>
            Failure.Throw();
    }

    /// <summary>
    /// Resolves a serialized enum type name to its underlying width, reporting
    /// through the return value whether it could. A <see langword="false"/>
    /// return means the width is unresolved and the decoder defaults it to
    /// <see cref="PrimitiveTypeCode.Int32"/> and reports it defaulted, which a
    /// legacy <c>Func&lt;string, PrimitiveTypeCode&gt;</c> cannot express.
    /// </summary>
    public delegate bool EnumWidthResolver(
        string enumTypeName,
        out PrimitiveTypeCode underlyingType);

    /// <summary>
    /// A decoded attribute value with the additive defaulted-width signal
    /// (#5288 D2, #5742). Each flag is <see langword="true"/> when any enum
    /// width within the corresponding top-level fixed or named argument
    /// defaulted to <see cref="PrimitiveTypeCode.Int32"/>.
    /// </summary>
    public readonly struct DetailedCustomAttributeValue(
        CustomAttributeValue<string> value,
        System.Collections.Immutable.ImmutableArray<bool> fixedArgumentEnumWidthDefaulted,
        System.Collections.Immutable.ImmutableArray<bool> namedArgumentEnumWidthDefaulted)
    {
        public CustomAttributeValue<string> Value { get; } = value;

        public System.Collections.Immutable.ImmutableArray<bool>
            FixedArgumentEnumWidthDefaulted { get; } = fixedArgumentEnumWidthDefaulted;

        public System.Collections.Immutable.ImmutableArray<bool>
            NamedArgumentEnumWidthDefaulted { get; } = namedArgumentEnumWidthDefaulted;
    }

}
