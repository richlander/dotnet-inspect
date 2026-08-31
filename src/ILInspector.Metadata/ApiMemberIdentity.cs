using CSharpText;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public sealed record MethodAnchorInfo(
    MemberAnchor Anchor,
    string ReturnType)
{
    MemberAnchor _anchor = Anchor ?? throw new ArgumentNullException(nameof(Anchor));
    string _returnType = ReturnType ?? throw new ArgumentNullException(nameof(ReturnType));

    public MemberAnchor Anchor
    {
        get => _anchor;
        init => _anchor = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ReturnType
    {
        get => _returnType;
        init => _returnType = value ?? throw new ArgumentNullException(nameof(value));
    }
}

public sealed record ExtensionMemberAnchorInfo(
    MemberAnchor Anchor,
    string ReturnType,
    string ExtendedType)
{
    MemberAnchor _anchor = Anchor ?? throw new ArgumentNullException(nameof(Anchor));
    string _returnType = ReturnType ?? throw new ArgumentNullException(nameof(ReturnType));
    string _extendedType = ExtendedType ?? throw new ArgumentNullException(nameof(ExtendedType));

    public MemberAnchor Anchor
    {
        get => _anchor;
        init => _anchor = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ReturnType
    {
        get => _returnType;
        init => _returnType = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ExtendedType
    {
        get => _extendedType;
        init => _extendedType = value ?? throw new ArgumentNullException(nameof(value));
    }

    public MetadataNamedTypeReference? ReturnTypeReference { get; init; }

    public MetadataNamedTypeReference? ExtendedTypeReference { get; init; }
}

/// <summary>
/// Metadata-owned API identity helpers for durable member selectors. These
/// helpers compose identity strings from queryable metadata facts, not from C#
/// declaration text.
/// </summary>
public static class ApiMemberIdentity
{
    internal static ImmutableArray<string> ConversionOperatorNames { get; } =
    [
        "op_Implicit",
        "op_Explicit",
        "op_CheckedImplicit",
        "op_CheckedExplicit",
    ];

    abstract class AnchorSignatureType
    {
        protected AnchorSignatureType(int length)
        {
            if (length < 0
                || length > MetadataSafetyPolicy.MaxStructuralSignatureChars)
            {
                throw new BadImageFormatException(
                    "The member anchor type exceeds the encoded-character budget.");
            }
            Length = length;
        }

        internal int Length { get; }
        internal abstract void AppendTo(StringBuilder builder);

        internal string Render()
        {
            var builder = new StringBuilder(Length);
            AppendTo(builder);
            return builder.ToString();
        }

        internal static int CheckedLength(params int[] parts)
        {
            try
            {
                int length = 0;
                foreach (int part in parts)
                    length = checked(length + part);
                return length;
            }
            catch (OverflowException ex)
            {
                throw new BadImageFormatException(
                    "The member anchor type exceeds the encoded-character budget.",
                    ex);
            }
        }

        internal static int CheckedProduct(int left, int right)
        {
            try
            {
                return checked(left * right);
            }
            catch (OverflowException ex)
            {
                throw new BadImageFormatException(
                    "The member anchor type exceeds the encoded-character budget.",
                    ex);
            }
        }
    }

    sealed class EncodedAnchorSignatureType(string text)
        : AnchorSignatureType(text.Length)
    {
        internal override void AppendTo(StringBuilder builder)
            => builder.Append(text);
    }

    /// <summary>
    /// TypeRef leaf that charges work from UTF-8 storage length and defers
    /// UTF-16 materialization until the name is actually rendered into an
    /// anchor. Discarded modopt trees therefore cannot force large string
    /// allocations on cache miss.
    /// </summary>
    sealed class LazyTypeReferenceAnchorSignatureType : AnchorSignatureType
    {
        readonly MetadataReader _reader;
        readonly TypeReferenceHandle _handle;
        readonly Func<MetadataReader, TypeReferenceHandle, string> _format;
        string? _text;

        internal LazyTypeReferenceAnchorSignatureType(
            MetadataReader reader,
            TypeReferenceHandle handle,
            int estimatedLength,
            Func<MetadataReader, TypeReferenceHandle, string> format)
            : base(estimatedLength)
        {
            _reader = reader;
            _handle = handle;
            _format = format;
        }

        internal override void AppendTo(StringBuilder builder)
            => builder.Append(_text ??= _format(_reader, _handle));
    }

    /// <summary>
    /// TypeDef leaf counterpart of
    /// <see cref="LazyTypeReferenceAnchorSignatureType"/>.
    /// </summary>
    sealed class LazyTypeDefinitionAnchorSignatureType : AnchorSignatureType
    {
        readonly MetadataReader _reader;
        readonly TypeDefinitionHandle _handle;
        readonly Func<MetadataReader, TypeDefinitionHandle, string> _format;
        string? _text;

        internal LazyTypeDefinitionAnchorSignatureType(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            int estimatedLength,
            Func<MetadataReader, TypeDefinitionHandle, string> format)
            : base(estimatedLength)
        {
            _reader = reader;
            _handle = handle;
            _format = format;
        }

        internal override void AppendTo(StringBuilder builder)
            => builder.Append(_text ??= _format(_reader, _handle));
    }

    sealed class WrappedAnchorSignatureType
        : AnchorSignatureType
    {
        readonly string _prefix;
        readonly AnchorSignatureType _value;
        readonly string _suffix;

        internal WrappedAnchorSignatureType(
            string prefix,
            AnchorSignatureType value,
            string suffix)
            : base(CheckedLength(prefix.Length, value.Length, suffix.Length))
        {
            _prefix = prefix;
            _value = value;
            _suffix = suffix;
        }

        internal override void AppendTo(StringBuilder builder)
        {
            builder.Append(_prefix);
            _value.AppendTo(builder);
            builder.Append(_suffix);
        }
    }

    sealed class ArrayAnchorSignatureType
        : AnchorSignatureType
    {
        readonly AnchorSignatureType _elementType;
        readonly int _rank;

        internal ArrayAnchorSignatureType(
            AnchorSignatureType elementType,
            int rank)
            : base(
                CheckedLength(
                    elementType.Length,
                    rank <= 1
                        ? 3
                        : CheckedLength(rank, 1)))
        {
            _elementType = elementType;
            _rank = rank;
        }

        internal override void AppendTo(StringBuilder builder)
        {
            _elementType.AppendTo(builder);
            if (_rank <= 1)
            {
                builder.Append("[*]");
                return;
            }

            builder.Append('[');
            builder.Append(',', _rank - 1);
            builder.Append(']');
        }
    }

    sealed class JoinedAnchorSignatureType
        : AnchorSignatureType
    {
        readonly string _prefix;
        readonly ImmutableArray<AnchorSignatureType> _values;
        readonly string _separator;
        readonly string _suffix;

        internal JoinedAnchorSignatureType(
            string prefix,
            ImmutableArray<AnchorSignatureType> values,
            string separator,
            string suffix)
            : base(GetLength(prefix, values, separator, suffix))
        {
            _prefix = prefix;
            _values = values;
            _separator = separator;
            _suffix = suffix;
        }

        static int GetLength(
            string prefix,
            ImmutableArray<AnchorSignatureType> values,
            string separator,
            string suffix)
        {
            int length = CheckedLength(prefix.Length, suffix.Length);
            if (values.Length > 1)
            {
                length = CheckedLength(
                    length,
                    CheckedProduct(
                        separator.Length,
                        values.Length - 1));
            }
            foreach (AnchorSignatureType value in values)
                length = CheckedLength(length, value.Length);
            return length;
        }

        internal override void AppendTo(StringBuilder builder)
        {
            builder.Append(_prefix);
            for (int i = 0; i < _values.Length; i++)
            {
                if (i > 0)
                    builder.Append(_separator);
                _values[i].AppendTo(builder);
            }
            builder.Append(_suffix);
        }
    }

    sealed class GenericAnchorSignatureType
        : AnchorSignatureType
    {
        readonly AnchorSignatureType _genericType;
        readonly ImmutableArray<AnchorSignatureType> _typeArguments;

        internal GenericAnchorSignatureType(
            AnchorSignatureType genericType,
            ImmutableArray<AnchorSignatureType> typeArguments)
            : base(GetLength(genericType, typeArguments))
        {
            _genericType = genericType;
            _typeArguments = typeArguments;
        }

        static int GetLength(
            AnchorSignatureType genericType,
            ImmutableArray<AnchorSignatureType> typeArguments)
        {
            int length = CheckedLength(genericType.Length, 2);
            if (typeArguments.Length > 1)
                length = CheckedLength(length, typeArguments.Length - 1);
            foreach (AnchorSignatureType argument in typeArguments)
                length = CheckedLength(length, argument.Length);
            return length;
        }

        internal override void AppendTo(StringBuilder builder)
        {
            _genericType.AppendTo(builder);
            builder.Append('<');
            for (int i = 0; i < _typeArguments.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                _typeArguments[i].AppendTo(builder);
            }
            builder.Append('>');
        }
    }

    /// <summary>
    /// Cumulative work budget for one member-anchor construction. Signature
    /// providers always charge materialized type-name occurrences and composite
    /// nodes; the caller-owned overload additionally charges the complete
    /// projection through fingerprint and selector construction. Gated by
    /// <c>CreateMethodAnchor_RepeatedTypeNamesFailBeforeLargeAllocation</c>
    /// <c>CreateMethodAnchor_NestedArrayModoptsFailBeforeLargeAllocation</c>,
    /// <c>CreateMethodAnchorInfo_RepeatedLongNamesExhaustSharedProjectionBudget</c>,
    /// <c>CreateMethodAnchorInfo_BoundedProjectionPreservesIdentity</c>, and the
    /// three <c>CreateMethodAnchorInfo_*ProjectionHasANonVacuousBudgetGate</c>
    /// tests.
    /// </summary>
    sealed class AnchorSignatureWorkBudget
    {
        const int MinimumProjectionNodeWork = 64;
        int _remaining;
        bool _exhausted;
        readonly bool _chargeProjectionWork;

        internal AnchorSignatureWorkBudget()
            : this(MetadataSafetyPolicy.MaxAnchorSignatureWorkChars)
        {
        }

        internal AnchorSignatureWorkBudget(
            int remaining,
            bool chargeProjectionWork = false)
        {
            _remaining = remaining;
            _chargeProjectionWork = chargeProjectionWork;
        }

        internal int Remaining => _exhausted ? 0 : _remaining;

        internal void ChargeProjection(int work)
            => ChargeProjection((long)work);

        internal void ChargeProjectionNodes(long count)
            => ChargeProjection(count * MinimumProjectionNodeWork);

        internal void ChargeProjection(
            long work,
            string? stage = null)
        {
            if (_chargeProjectionWork)
                Charge(work, stage);
        }

        internal void Charge(
            long characters,
            string? stage = null)
        {
            if (_exhausted || characters < 0 || characters > _remaining)
            {
                _exhausted = true;
                throw new BadImageFormatException(
                    stage is null
                        ? "The member anchor signature exceeds the cumulative work budget."
                        : $"The member anchor {stage} exceeds the cumulative work budget.");
            }
            _remaining -= (int)characters;
        }
    }

    sealed class AnchorSignatureTypeProvider
        : ISignatureTypeProvider<AnchorSignatureType, GenericContext?>
    {
        readonly AnchorSignatureWorkBudget _workBudget;
        Dictionary<TypeDefinitionHandle, AnchorSignatureType>? _definitionCache;
        Dictionary<TypeReferenceHandle, AnchorSignatureType>? _referenceCache;

        internal AnchorSignatureTypeProvider(
            AnchorSignatureWorkBudget workBudget)
        {
            _workBudget = workBudget;
        }

        public AnchorSignatureType GetPrimitiveType(PrimitiveTypeCode typeCode)
            => Encoded(typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.Void => "System.Void",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                _ => typeCode.ToString(),
            });

        public AnchorSignatureType GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            _definitionCache ??= [];
            if (_definitionCache.TryGetValue(handle, out AnchorSignatureType? cached))
            {
                // Reuse the composed name, but charge the same leaf floor as
                // Encoded so wide GENERICINST/FNPTR trees of short TypeDef
                // names cannot bypass the budget on cache hits.
                ChargeLeaf(cached.Length);
                return cached;
            }

            // Charge from UTF-8 storage before any UTF-16 materialization so
            // discarded modopt TypeDefs cannot allocate full names on miss.
            // Gated by CreateMethodAnchor_UniqueLongTypeRefModoptsFailBeforeLargeAllocation.
            int estimatedLength = EstimateDefinitionNameLength(reader, handle);
            ChargeLeaf(estimatedLength);
            AnchorSignatureType encoded = new LazyTypeDefinitionAnchorSignatureType(
                reader,
                handle,
                estimatedLength,
                FormatDefinitionTypeName);
            _definitionCache.Add(handle, encoded);
            return encoded;
        }

        public AnchorSignatureType GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            _referenceCache ??= [];
            if (_referenceCache.TryGetValue(handle, out AnchorSignatureType? cached))
            {
                ChargeLeaf(cached.Length);
                return cached;
            }

            int estimatedLength = EstimateReferenceNameLength(reader, handle);
            ChargeLeaf(estimatedLength);
            AnchorSignatureType encoded = new LazyTypeReferenceAnchorSignatureType(
                reader,
                handle,
                estimatedLength,
                FormatReferenceTypeName);
            _referenceCache.Add(handle, encoded);
            return encoded;
        }

        public AnchorSignatureType GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return Encoded("System.Object");
            using (scope)
            {
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            }
        }

        public AnchorSignatureType GetSZArrayType(
            AnchorSignatureType elementType)
            => Composite(
                new WrappedAnchorSignatureType("", elementType, "[]"));

        public AnchorSignatureType GetArrayType(
            AnchorSignatureType elementType,
            ArrayShape shape)
            => Composite(
                new ArrayAnchorSignatureType(
                    elementType,
                    shape.Rank));

        public AnchorSignatureType GetByReferenceType(
            AnchorSignatureType elementType)
            => Composite(
                new WrappedAnchorSignatureType("", elementType, "&"));

        public AnchorSignatureType GetPointerType(
            AnchorSignatureType elementType)
            => Composite(
                new WrappedAnchorSignatureType("", elementType, "*"));

        public AnchorSignatureType GetPinnedType(
            AnchorSignatureType elementType)
            => Composite(
                new WrappedAnchorSignatureType("pinned ", elementType, ""));

        public AnchorSignatureType GetGenericInstantiation(
            AnchorSignatureType genericType,
            ImmutableArray<AnchorSignatureType> typeArguments)
            => Composite(
                new GenericAnchorSignatureType(genericType, typeArguments));

        public AnchorSignatureType GetGenericTypeParameter(
            GenericContext? context,
            int index)
            => Encoded(
                context is not null
                    && index >= 0
                    && index < context.TypeParameters.Count
                ? context.TypeParameters[index]
                : $"!{index}");

        public AnchorSignatureType GetGenericMethodParameter(
            GenericContext? context,
            int index)
            => Encoded(
                context is not null
                    && index >= 0
                    && index < context.MethodParameters.Count
                ? context.MethodParameters[index]
                : $"!!{index}");

        public AnchorSignatureType GetFunctionPointerType(
            MethodSignature<AnchorSignatureType> signature)
            => Composite(
                new JoinedAnchorSignatureType(
                    "delegate*<",
                    signature.ParameterTypes.Add(signature.ReturnType),
                    ",",
                    ">"));

        public AnchorSignatureType GetModifiedType(
            AnchorSignatureType modifier,
            AnchorSignatureType unmodifiedType,
            bool isRequired)
        {
            // Custom modifiers are dropped from the rendered anchor, but the
            // modifier subtree was already charged when it was constructed.
            // Charge a unit of work for the discarded edge so a tree of only
            // modifiers cannot be free.
            _ = modifier;
            _ = isRequired;
            _workBudget.Charge(1);
            return unmodifiedType;
        }

        int EstimateDefinitionNameLength(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                    reader,
                    handle,
                    chain,
                    out int consumed,
                    out EntityHandle terminal,
                    out var rejection)
                || consumed == 0
                || !terminal.IsNil)
            {
                throw new BadImageFormatException(
                    rejection?.Detail
                        ?? "The type has an invalid declaring-type chain.");
            }

            // UTF-8 storage length upper-bounds UTF-16 length for valid text, so
            // charging it before GetString keeps the work budget honest without
            // materializing discarded modifier names.
            var outer = reader.GetTypeDefinition(chain[0]);
            int length = StructuralUtf8Length(reader, outer.Namespace);
            for (int i = 0; i < consumed; i++)
            {
                if (length > 0)
                    length = CheckedNameLength(length, 1);
                length = CheckedNameLength(
                    length,
                    StructuralUtf8Length(
                        reader,
                        reader.GetTypeDefinition(chain[i]).Name));
            }
            return length;
        }

        int EstimateReferenceNameLength(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out int consumed,
                    out _,
                    out var rejection)
                || consumed == 0)
            {
                throw new BadImageFormatException(
                    rejection?.Detail
                        ?? "The type has an invalid resolution-scope chain.");
            }

            var outer = reader.GetTypeReference(chain[0]);
            int length = StructuralUtf8Length(reader, outer.Namespace);
            for (int i = 0; i < consumed; i++)
            {
                if (length > 0)
                    length = CheckedNameLength(length, 1);
                length = CheckedNameLength(
                    length,
                    StructuralUtf8Length(
                        reader,
                        reader.GetTypeReference(chain[i]).Name));
            }
            return length;
        }

        static int StructuralUtf8Length(
            MetadataReader reader,
            StringHandle handle)
        {
            int length = reader.GetBlobReader(handle).Length;
            if (length > MetadataSafetyPolicy.MaxStructuralSignatureChars)
            {
                throw new BadImageFormatException(
                    "The metadata string exceeds the structural-signature budget.");
            }
            return length;
        }

        static int CheckedNameLength(int left, int right)
        {
            try
            {
                int length = checked(left + right);
                if (length > MetadataSafetyPolicy.MaxStructuralSignatureChars)
                {
                    throw new BadImageFormatException(
                        "The member anchor type exceeds the encoded-character budget.");
                }
                return length;
            }
            catch (OverflowException ex)
            {
                throw new BadImageFormatException(
                    "The member anchor type exceeds the encoded-character budget.",
                    ex);
            }
        }

        string FormatDefinitionTypeName(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                    reader,
                    handle,
                    chain,
                    out int consumed,
                    out EntityHandle terminal,
                    out var rejection)
                || consumed == 0
                || !terminal.IsNil)
            {
                throw new BadImageFormatException(
                    rejection?.Detail
                        ?? "The type has an invalid declaring-type chain.");
            }

            var builder = new StringBuilder();
            var outer = reader.GetTypeDefinition(chain[0]);
            AppendSignatureTypeName(
                builder,
                MetadataSafetyPolicy.ReadStructuralString(
                    reader,
                    outer.Namespace));
            for (int i = 0; i < consumed; i++)
            {
                if (builder.Length > 0)
                    AppendSignatureTypeName(builder, ".");
                AppendSignatureTypeName(
                    builder,
                    MetadataSafetyPolicy.ReadStructuralString(
                        reader,
                        reader.GetTypeDefinition(chain[i]).Name));
            }
            return builder.ToString();
        }

        string FormatReferenceTypeName(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out int consumed,
                    out _,
                    out var rejection)
                || consumed == 0)
            {
                throw new BadImageFormatException(
                    rejection?.Detail
                        ?? "The type has an invalid resolution-scope chain.");
            }

            var builder = new StringBuilder();
            var outer = reader.GetTypeReference(chain[0]);
            AppendSignatureTypeName(
                builder,
                MetadataSafetyPolicy.ReadStructuralString(
                    reader,
                    outer.Namespace));
            for (int i = 0; i < consumed; i++)
            {
                if (builder.Length > 0)
                    AppendSignatureTypeName(builder, ".");
                AppendSignatureTypeName(
                    builder,
                    MetadataSafetyPolicy.ReadStructuralString(
                        reader,
                        reader.GetTypeReference(chain[i]).Name));
            }
            return builder.ToString();
        }

        static void AppendSignatureTypeName(
            StringBuilder builder,
            string value)
        {
            if (builder.Length
                > MetadataSafetyPolicy.MaxStructuralSignatureChars
                    - value.Length)
            {
                throw new BadImageFormatException(
                    "The member anchor signature exceeds the encoded-character budget.");
            }
            builder.Append(value);
        }

        AnchorSignatureType Encoded(string text)
        {
            // Charge every occurrence, including the short-leaf floor. Gated by
            // CreateMethodAnchor_WideGenericModoptsFailBeforeLargeAllocation and
            // CreateMethodAnchor_WideTypeRefGenericModoptsFailBeforeLargeAllocation.
            ChargeLeaf(text.Length);
            return new EncodedAnchorSignatureType(text);
        }

        void ChargeLeaf(int length)
        {
            // Short leaves (e.g. "!0", "N.T") still pay a floor so wide
            // GENERICINST/FNPTR modifier trees cannot mint tens of thousands of
            // near-free nodes under the name-length budget. Cache hits must use
            // the same floor as first-time Encoded; charging cached.Length alone
            // reopened the width axis on repeated TypeRef/TypeDef leaves.
            _workBudget.Charge(
                length > LeafNodeWorkUnits
                    ? length
                    : LeafNodeWorkUnits);
        }

        AnchorSignatureType Composite(AnchorSignatureType type)
        {
            // Charge a fixed per-node cost rather than type.Length. Charging the
            // full composed length at every nesting level is quadratic in depth
            // and rejects legitimate deep signatures (see
            // Resolve_DeepAcceptedSignatureDoesNotExpandAnchorQuadratically).
            // A constant still bounds O(params × depth) discarded modopt trees
            // because each composite allocates a node. Gated by
            // CreateMethodAnchor_NestedArrayModoptsFailBeforeLargeAllocation.
            _workBudget.Charge(CompositeNodeWorkUnits);
            return type;
        }

        // Work units charged per composite anchor node (array/pointer/generic/
        // fnptr) and as a floor for short leaf names. Sized so depth≈512 legal
        // signatures stay far under the 4 MiB budget while discarded modopt
        // trees that are deep or wide exhaust it before large allocation.
        const int CompositeNodeWorkUnits = 64;
        const int LeafNodeWorkUnits = 64;
    }

    public static string GetMemberDigest(string canonicalSignature)
        => MemberAnchor.ComputeFingerprint(canonicalSignature);

    public static string GetMemberSelectorName(ApiMember member) => member.Kind switch
    {
        "operator" => $"operator:{member.Name}",
        "explicit-interface-implementation" => $"explicit:{member.Name}",
        "extension-method" => $"extension:{member.Name}",
        _ => member.Name
    };

    public static string GetMemberSelectorName(string metadataMethodName, bool isExtensionMethod = false)
        => metadataMethodName switch
        {
            ".ctor" => ".ctor",
            _ when isExtensionMethod => $"extension:{metadataMethodName}",
            _ when metadataMethodName.StartsWith("op_", StringComparison.Ordinal) => $"operator:{metadataMethodName}",
            _ when metadataMethodName.Contains('.', StringComparison.Ordinal) => $"explicit:{metadataMethodName}",
            _ => metadataMethodName,
        };

    public static ApiMemberHandle CreateHandle(ApiType type, ApiMember member)
        => new(type, member, GetMemberAnchor(type, member));

    /// <summary>
    /// Persists <see cref="ApiMember.CanonicalSignature"/> when exact member
    /// identity cannot be reconstructed from serialized fields. Exact declaring
    /// type identity is already retained structurally on <see cref="ApiType"/>.
    /// Computed while the structural model is live so a round-tripped surface
    /// pairs with the same members read live.
    /// </summary>
    public static void PopulateCanonicalIdentities(
        ApiSurface surface,
        Action<string>? beforeRetain = null)
    {
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                if (member.SignatureModel is not { } signature
                    || !HasCanonicalDivergence(member, signature))
                {
                    continue;
                }

                string canonical = GetCanonicalSignature(type, member);
                beforeRetain?.Invoke(canonical);
                member.CanonicalSignature = canonical;
            }
        }
    }

    static bool HasCanonicalDivergence(ApiMember member, ApiSignature signature)
    {
        // Persist canonical identity for any member whose display signature carries C#
        // tuple syntax the text fallback cannot re-canonicalize after a JSON round-trip
        // (SignatureModel is not serialized). Two divergence sources both require it:
        //   * A tuple PARAMETER is part of the identity digest; its erased spelling and
        //     element names must not leak in and cannot be recovered from display text.
        //   * A tuple RETURN type is only part of the digest for conversion operators, but
        //     even for other members its '(...)' parentheses would derail the fallback's
        //     first-'(' parameter-list detection, corrupting the round-tripped identity.
        //     The persisted digest is computed live and correctly omits the non-conversion
        //     return type, so short-circuiting to it keeps live and round-trip in lockstep.
        if (signature.Parameters.Any(parameter =>
                !string.Equals(parameter.EffectiveCanonicalType, parameter.Type, StringComparison.Ordinal)))
        {
            return true;
        }

        //   * A member or type-parameter name carrying a rendering hazard is
        //     respelled in the display signature by containment (issue #3319)
        //     but kept raw in identity. The text fallback locates the member
        //     name by searching the display signature for the raw spelling, so
        //     a respelling makes that search miss and silently drops the
        //     generic arity -- a round-tripped `M<T>(int)` would pair as
        //     `M(int)`. Persisting the live identity keeps the two in lockstep.
        if (CarriesRenderingHazard(member.Name)
            || signature.TypeParameters.Any(parameter => CarriesRenderingHazard(parameter.Name)))
        {
            return true;
        }

        return !string.Equals(signature.EffectiveCanonicalReturnType, signature.ReturnType, StringComparison.Ordinal);
    }

    static bool CarriesRenderingHazard(string? name)
        => name is not null && CSharpIdentifierCore.RequiresContainment(name);

    public static string GetMemberSignatureSortKey(ApiMember member)
    {
        var signature = member.Signature ?? "";
        if (signature.Length == 0 || member.Name.Length == 0)
            return signature;

        var searchStart = 0;
        while (searchStart < signature.Length)
        {
            var nameIndex = signature.IndexOf(member.Name, searchStart, StringComparison.Ordinal);
            if (nameIndex < 0)
                return signature;

            var genericStart = nameIndex + member.Name.Length;
            if (genericStart < signature.Length && signature[genericStart] == '<')
            {
                var depth = 0;
                for (var i = genericStart; i < signature.Length; i++)
                {
                    if (signature[i] == '<')
                        depth++;
                    else if (signature[i] == '>')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            if (i + 1 < signature.Length && signature[i + 1] == '(')
                                return signature.Remove(genericStart, i - genericStart + 1);
                            break;
                        }
                    }
                }
            }

            searchStart = nameIndex + member.Name.Length;
        }

        return signature;
    }

    public static MemberAnchor GetMemberAnchor(ApiType type, ApiMember member)
        => CreateAnchor(type, member, GetCanonicalSignature(type, member));

    public static MemberAnchor CreateMethodAnchor(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        bool isExtensionMethod = false)
        => CreateMethodAnchorInfo(reader, typeHandle, method, isExtensionMethod).Anchor;

    public static MethodAnchorInfo CreateMethodAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        bool isExtensionMethod = false)
    {
        var shape = CreateMethodAnchorShape(reader, typeHandle, method, isExtensionMethod);
        return new MethodAnchorInfo(shape.Anchor, shape.ReturnType);
    }

    /// <summary>
    /// Creates a method anchor while drawing from a caller-owned cumulative
    /// work remaining counter. Metadata names, signature trees, rendered
    /// signatures, canonical identity, fingerprint input, and selector output
    /// all draw from the same counter. Each call is still capped by
    /// <see cref="MetadataSafetyPolicy.MaxAnchorSignatureWorkChars"/>, and spent
    /// units are subtracted from <paramref name="scanWorkRemaining"/>.
    /// </summary>
    public static MethodAnchorInfo CreateMethodAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        ref int scanWorkRemaining,
        bool isExtensionMethod = false)
    {
        if (scanWorkRemaining <= 0)
        {
            throw new BadImageFormatException(
                "The assembly exceeds the classification scan work budget.");
        }

        int anchorAllowance = scanWorkRemaining;
        if (anchorAllowance > MetadataSafetyPolicy.MaxAnchorSignatureWorkChars)
            anchorAllowance = MetadataSafetyPolicy.MaxAnchorSignatureWorkChars;

        var workBudget = new AnchorSignatureWorkBudget(
            anchorAllowance,
            chargeProjectionWork: true);
        try
        {
            var shape = CreateMethodAnchorShape(
                reader,
                typeHandle,
                method,
                isExtensionMethod,
                workBudget);
            int spent = anchorAllowance - workBudget.Remaining;
            scanWorkRemaining -= spent;
            if (scanWorkRemaining < 0)
                scanWorkRemaining = 0;
            return new MethodAnchorInfo(shape.Anchor, shape.ReturnType);
        }
        catch (BadImageFormatException)
        {
            // Budget may be exhausted mid-decode; do not allow retry with the
            // pre-call remaining on a later method.
            scanWorkRemaining = workBudget.Remaining;
            throw;
        }
    }

    public static ExtensionMemberAnchorInfo CreateExtensionMethodAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method)
    {
        var shape = CreateMethodAnchorShape(reader, typeHandle, method, isExtensionMethod: true);
        if (shape.ParameterTypes.Length == 0)
            throw new BadImageFormatException("An extension method must have a receiver parameter.");

        MethodSignature<MetadataNamedTypeReference?>? namedSignature =
            MetadataNamedTypeSignatureDecoder.DecodeMethod(
                reader,
                method,
                GenericContext.ForMethod(
                    reader,
                    reader.GetTypeDefinition(typeHandle),
                    method));
        return new ExtensionMemberAnchorInfo(
            shape.Anchor,
            shape.ReturnType,
            shape.ParameterTypes[0])
        {
            ReturnTypeReference = namedSignature?.ReturnType,
            ExtendedTypeReference =
                namedSignature is { ParameterTypes.Length: > 0 }
                    ? namedSignature.Value.ParameterTypes[0]
                    : null,
        };
    }

    internal static ExtensionMemberAnchorInfo CreateExtensionPropertyDeclarationAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle extensionClassHandle,
        TypeDefinition markerType,
        MethodDefinition markerMethod,
        PropertyDefinition property)
    {
        var context = GenericContext.ForType(reader, markerType);
        var workBudget = new AnchorSignatureWorkBudget();
        var provider = new AnchorSignatureTypeProvider(workBudget);
        var decodedMarker = GuardedProviderDecode.MethodResult(
            reader,
            markerMethod,
            provider,
            context,
            new EncodedAnchorSignatureType("System.Object"));
        if (decodedMarker.IsDegraded)
        {
            throw new BadImageFormatException(
                "The extension marker signature exceeds the metadata safety limit.");
        }
        MethodSignature<AnchorSignatureType> markerSignature =
            decodedMarker.Value;
        if (markerSignature.ParameterTypes.Length != 1)
            throw new BadImageFormatException("An extension marker must have exactly one receiver parameter.");

        var decodedProperty = GuardedProviderDecode.PropertyResult(
            reader,
            property,
            provider,
            context,
            new EncodedAnchorSignatureType("System.Object"));
        if (decodedProperty.IsDegraded)
        {
            throw new BadImageFormatException(
                "The extension property signature exceeds the metadata safety limit.");
        }
        MethodSignature<AnchorSignatureType> propertySignature =
            decodedProperty.Value;
        EnsureAnchorSignatureBudget(
            propertySignature.ReturnType,
            markerSignature.ParameterTypes,
            propertySignature.ParameterTypes);
        string propertyName =
            MetadataSafetyPolicy.ReadStructuralString(
                reader,
                property.Name);
        string typeFullName = FormatDefinitionName(reader, extensionClassHandle);
        string extendedType = markerSignature.ParameterTypes[0].Render();
        ImmutableArray<string> propertyParameterTypes =
            Render(markerSignature.ParameterTypes)
                .AddRange(Render(propertySignature.ParameterTypes));
        string canonicalSignature = MemberCanonicalSignature.BuildExtensionProperty(
            typeFullName,
            propertyName,
            propertyParameterTypes);
        return new ExtensionMemberAnchorInfo(
            CreateAnchor(
                typeFullName,
                $"extension:{propertyName}",
                propertyName,
                canonicalSignature),
            propertySignature.ReturnType.Render(),
            extendedType);
    }

    static (MemberAnchor Anchor, string ReturnType, ImmutableArray<string> ParameterTypes) CreateMethodAnchorShape(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        bool isExtensionMethod,
        AnchorSignatureWorkBudget? workBudget = null)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        string methodName = ReadProjectionString(
            reader,
            method.Name,
            workBudget);
        workBudget?.ChargeProjectionNodes(
            type.GetGenericParameters().Count
                + (long)method.GetGenericParameters().Count);
        Action<int>? beforeGenericNameMaterialize =
            workBudget is null
                ? null
                : workBudget.ChargeProjection;
        GenericContext typeContext =
            GenericContext.ForType(
                reader,
                type,
                beforeGenericNameMaterialize);
        GenericContext context =
            GenericContext.ForMethod(
                reader,
                typeContext,
                method,
                beforeGenericNameMaterialize);
        workBudget ??= new AnchorSignatureWorkBudget();
        var provider = new AnchorSignatureTypeProvider(workBudget);
        var decoded = GuardedProviderDecode.MethodResult(
            reader,
            method,
            provider,
            context,
            new EncodedAnchorSignatureType("System.Object"));
        if (decoded.IsDegraded)
        {
            throw new BadImageFormatException(
                "The method signature exceeds the metadata safety limit.");
        }
        MethodSignature<AnchorSignatureType> signature =
            decoded.Value;
        EnsureAnchorSignatureBudget(
            signature.ReturnType,
            signature.ParameterTypes);
        string typeFullName =
            FormatDefinitionName(reader, typeHandle, workBudget);
        string memberName = MethodMemberName(
            methodName,
            context.MethodParameters,
            workBudget);
        string returnType = Render(signature.ReturnType, workBudget);
        ImmutableArray<string> parameterTypes =
            Render(signature.ParameterTypes, workBudget);
        // Route the SRM-direct producer through the single full-name grammar core so it
        // cannot drift from other producers. Conversion operators overload on return type,
        // so pass the return type for their disambiguation suffix only.
        string? conversionReturnType =
            IsConversionOperator(methodName) ? returnType : null;
        ChargeCanonicalSignatureProjection(
            workBudget,
            typeFullName,
            memberName,
            parameterTypes,
            conversionReturnType);
        string canonicalSignature = MemberCanonicalSignature.Build(
            "M",
            typeFullName,
            memberName,
            parameterTypes,
            conversionReturnType);
        ChargeSelectorProjection(
            workBudget,
            methodName);
        string selectorName = GetMemberSelectorName(methodName, isExtensionMethod);
        return (
            CreateAnchor(
                typeFullName,
                selectorName,
                memberName,
                canonicalSignature,
                workBudget),
            returnType,
            parameterTypes);
    }

    static string ReadProjectionString(
        MetadataReader reader,
        StringHandle handle,
        AnchorSignatureWorkBudget? workBudget)
    {
        workBudget?.ChargeProjection(
            reader.GetBlobReader(handle).Length);
        return MetadataSafetyPolicy.ReadStructuralString(reader, handle);
    }

    static string Render(
        AnchorSignatureType type,
        AnchorSignatureWorkBudget workBudget)
    {
        workBudget.ChargeProjection(2L * type.Length);
        return type.Render();
    }

    static ImmutableArray<string> Render(
        ImmutableArray<AnchorSignatureType> types)
    {
        var builder = ImmutableArray.CreateBuilder<string>(types.Length);
        foreach (AnchorSignatureType type in types)
            builder.Add(type.Render());
        return builder.MoveToImmutable();
    }

    static ImmutableArray<string> Render(
        ImmutableArray<AnchorSignatureType> types,
        AnchorSignatureWorkBudget workBudget)
    {
        workBudget.ChargeProjection(types.Length);
        var builder = ImmutableArray.CreateBuilder<string>(types.Length);
        foreach (AnchorSignatureType type in types)
            builder.Add(Render(type, workBudget));
        return builder.MoveToImmutable();
    }

    static void ChargeCanonicalSignatureProjection(
        AnchorSignatureWorkBudget workBudget,
        string typeFullName,
        string memberName,
        ImmutableArray<string> parameterTypes,
        string? conversionReturnType)
    {
        long joinedParameterLength =
            Math.Max(0, parameterTypes.Length - 1);
        foreach (string parameterType in parameterTypes)
            joinedParameterLength += parameterType.Length;

        long canonicalLength =
            2L
            + typeFullName.Length
            + 1
            + memberName.Length
            + 2
            + joinedParameterLength;
        if (conversionReturnType is not null)
            canonicalLength += 1L + conversionReturnType.Length;

        workBudget.ChargeProjection(
            joinedParameterLength + canonicalLength);
    }

    static void ChargeSelectorProjection(
        AnchorSignatureWorkBudget workBudget,
        string methodName)
    {
        workBudget.ChargeProjection(
            "extension:".Length + (long)methodName.Length,
            "selector projection");
    }

    static void EnsureAnchorSignatureBudget(
        AnchorSignatureType returnType,
        params ImmutableArray<AnchorSignatureType>[] parameterGroups)
    {
        int remaining =
            MetadataSafetyPolicy.MaxStructuralSignatureChars
            - returnType.Length;
        if (remaining < 0)
            throw AnchorSignatureBudgetExceeded();

        foreach (ImmutableArray<AnchorSignatureType> parameters
            in parameterGroups)
        {
            foreach (AnchorSignatureType parameter in parameters)
            {
                if (parameter.Length > remaining)
                    throw AnchorSignatureBudgetExceeded();
                remaining -= parameter.Length;
            }
        }
    }

    static BadImageFormatException AnchorSignatureBudgetExceeded()
        => new(
            "The member anchor signature exceeds the encoded-character budget.");

    public static MemberAnchor CreateAnchor(ApiType type, ApiMember member, string canonicalSignature)
    {
        var fingerprint = MemberAnchor.ComputeFingerprint(
            canonicalSignature,
            member.SignatureDecodeStatus is SignatureDecodeStatus.Degraded);
        var stableSelector = $"{GetMemberSelectorName(member)}~{fingerprint}";
        return new MemberAnchor(
            stableSelector,
            canonicalSignature,
            fingerprint,
            FormatApiTypeAnchorName(type),
            member.Name);
    }

    static MemberAnchor CreateAnchor(
        string typeFullName,
        string selectorName,
        string memberName,
        string canonicalSignature,
        AnchorSignatureWorkBudget? workBudget = null)
    {
        int fingerprintInputByteCount =
            Encoding.UTF8.GetByteCount(MemberAnchor.FingerprintPrefix)
            + Encoding.UTF8.GetByteCount(canonicalSignature);
        workBudget?.ChargeProjection(
            MemberAnchor.FingerprintPrefix.Length
                + (long)canonicalSignature.Length
                + fingerprintInputByteCount
                + 32
                + 64
                + 64
                + 10,
            "fingerprint projection");
        var fingerprint = MemberAnchor.ComputeFingerprint(canonicalSignature);
        workBudget?.ChargeProjection(
            selectorName.Length + 1L + fingerprint.Length,
            "stable selector projection");
        return new MemberAnchor(
            $"{selectorName}~{fingerprint}",
            canonicalSignature,
            fingerprint,
            typeFullName,
            memberName);
    }

    static string FormatDefinitionName(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        AnchorSignatureWorkBudget? workBudget = null)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                chain,
                out int consumed,
                out EntityHandle terminal,
                out var rejection)
            || consumed == 0
            || !terminal.IsNil)
        {
            throw new BadImageFormatException(
                rejection?.Detail
                    ?? "The type has an invalid declaring-type chain.");
        }

        var builder = new StringBuilder();
        int remainingTypeNameCharacters =
            MetadataSafetyPolicy.MaxTypeNameCharacters;
        StringHandle namespaceHandle =
            reader.GetTypeDefinition(chain[0]).Namespace;
        workBudget?.ChargeProjection(
            reader.GetBlobReader(namespaceHandle).Length);
        if (!MetadataSafetyPolicy.TryReadTypeNameComponent(
                reader,
                namespaceHandle,
                ref remainingTypeNameCharacters,
                out string @namespace))
        {
            throw TypeNameBudgetExceeded();
        }
        if (!string.IsNullOrEmpty(@namespace))
        {
            workBudget?.ChargeProjection(
                EscapedAnchorNameLength(
                    @namespace,
                    @namespace.Length,
                    escapeDot: false)
                + 1L);
            AppendEscapedAnchorName(
                builder,
                @namespace,
                @namespace.Length,
                escapeDot: false);
            AppendAnchorName(builder, ".");
        }

        int enclosingGenericCount = 0;
        for (int i = 0; i < consumed; i++)
        {
            if (i > 0)
            {
                workBudget?.ChargeProjection(1);
                AppendAnchorName(builder, '+');
            }

            if (remainingTypeNameCharacters == 0)
                throw TypeNameBudgetExceeded();
            remainingTypeNameCharacters--;

            var type = reader.GetTypeDefinition(chain[i]);
            workBudget?.ChargeProjection(
                reader.GetBlobReader(type.Name).Length);
            if (!MetadataSafetyPolicy.TryReadTypeNameComponent(
                    reader,
                    type.Name,
                    ref remainingTypeNameCharacters,
                    out string name))
            {
                throw TypeNameBudgetExceeded();
            }
            var genericParameters = type.GetGenericParameters();
            if (!MetadataTypeDeclarationProbe.TryGetGenericParameterCount(
                    reader,
                    chain[i],
                    out int cumulativeGenericCount))
            {
                throw new BadImageFormatException(
                    "Generic parameter indices must be contiguous and ordered.");
            }
            int introducedGenericCount =
                MetadataDeclarationQuery.GetIntroducedTypeParameterCount(
                    cumulativeGenericCount,
                    enclosingGenericCount);
            // Only a canonical trailing `N is an arity suffix. Truncating at any
            // backtick would give a name whose backtick is literal (Widget`Literal)
            // the same anchor as the plain name (Widget). A suffix that disagrees
            // with the row's GenericParam count is also identity text: stripping
            // it would collapse malformed Widget`2<T> onto Widget`1<T>.
            bool hasDeclaredArity = MetadataNameArity.TryReadSuffix(
                name,
                out int declaredArity,
                out int simpleNameLength);
            if (hasDeclaredArity
                && declaredArity != introducedGenericCount)
            {
                simpleNameLength = name.Length;
            }
            workBudget?.ChargeProjection(
                EscapedAnchorNameLength(
                    name,
                    simpleNameLength,
                    escapeDot: true));
            AppendEscapedAnchorName(
                builder,
                name,
                simpleNameLength,
                escapeDot: true);
            if (!hasDeclaredArity && introducedGenericCount > 0)
            {
                workBudget?.ChargeProjection(2);
                AppendAnchorName(builder, ":0");
            }

            if (introducedGenericCount == 0)
            {
                enclosingGenericCount = cumulativeGenericCount;
                continue;
            }

            workBudget?.ChargeProjection(2);
            AppendAnchorName(builder, "<");
            int index = 0;
            foreach (GenericParameterHandle parameter in
                genericParameters.Skip(enclosingGenericCount))
            {
                if (index++ > 0)
                {
                    workBudget?.ChargeProjection(1);
                    AppendAnchorName(builder, ",");
                }
                string parameterName =
                    ReadProjectionString(
                        reader,
                        reader.GetGenericParameter(parameter).Name,
                        workBudget);
                workBudget?.ChargeProjection(
                    EscapedAnchorNameLength(
                        parameterName,
                        parameterName.Length,
                        escapeDot: true));
                AppendEscapedAnchorName(
                    builder,
                    parameterName,
                    parameterName.Length,
                    escapeDot: true);
            }
            AppendAnchorName(builder, ">");
            enclosingGenericCount = cumulativeGenericCount;
        }

        workBudget?.ChargeProjection(builder.Length);
        return builder.ToString();
    }

    static BadImageFormatException TypeNameBudgetExceeded()
        => new(
            $"The metadata type name exceeds "
                + $"{MetadataSafetyPolicy.MaxTypeNameCharacters} characters.");

    internal static string FormatTypeAnchorName(ApiType type) =>
        FormatApiTypeAnchorName(type);

    static string FormatApiTypeAnchorName(ApiType type)
    {
        if (type.DefinitionName is not { } exactName)
            return MetadataTypeNameFormatter.FormatFullName(type);

        var builder = new StringBuilder();
        if (exactName.Namespace.Length > 0)
        {
            AppendEscapedAnchorName(
                builder,
                exactName.Namespace,
                exactName.Namespace.Length,
                escapeDot: false);
            AppendAnchorName(builder, '.');
        }

        int parameterIndex = 0;
        for (int i = 0; i < exactName.Segments.Length; i++)
        {
            if (i > 0)
                AppendAnchorName(builder, '+');

            string segment = exactName.Segments[i];
            bool hasDeclaredArity = MetadataNameArity.TryReadSuffix(
                segment,
                out int declaredArity,
                out int simpleNameLength);
            int introducedGenericCount =
                HasExactTypeParameterCounts(type, exactName)
                    ? type.IntroducedTypeParameterCounts![i]
                    : InferIntroducedGenericCount(
                        type,
                        exactName,
                        i,
                        parameterIndex);
            if (hasDeclaredArity
                && declaredArity != introducedGenericCount)
            {
                simpleNameLength = segment.Length;
            }

            AppendEscapedAnchorName(
                builder,
                segment,
                simpleNameLength,
                escapeDot: true);
            if (!hasDeclaredArity && introducedGenericCount > 0)
                AppendAnchorName(builder, ":0");
            if (introducedGenericCount == 0)
                continue;

            AppendAnchorName(builder, '<');
            for (int j = 0; j < introducedGenericCount; j++)
            {
                if (j > 0)
                    AppendAnchorName(builder, ',');
                string parameterName =
                    type.TypeParameters[parameterIndex++].Name;
                AppendEscapedAnchorName(
                    builder,
                    parameterName,
                    parameterName.Length,
                    escapeDot: true);
            }
            AppendAnchorName(builder, '>');
        }

        return builder.ToString();
    }

    static bool HasExactTypeParameterCounts(
        ApiType type,
        MetadataTypeDefinitionName exactName) =>
        type.IntroducedTypeParameterCounts is { } counts
        && counts.Count == exactName.Segments.Length
        && counts.All(static count => count >= 0)
        && counts.Sum(static count => (long)count)
            == type.TypeParameters.Count;

    static int InferIntroducedGenericCount(
        ApiType type,
        MetadataTypeDefinitionName exactName,
        int segmentIndex,
        int parameterIndex)
    {
        int arityAfter = 0;
        for (int index = segmentIndex + 1;
            index < exactName.Segments.Length;
            index++)
        {
            arityAfter +=
                MetadataNameArity.OfSegment(exactName.Segments[index]);
        }
        return Math.Max(
            0,
            type.TypeParameters.Count - parameterIndex - arityAfter);
    }

    static void AppendEscapedAnchorName(
        StringBuilder builder,
        string value,
        int count,
        bool escapeDot)
    {
        for (int i = 0; i < count; i++)
        {
            char c = value[i];
            if (IsUnescapedAnchorNameCharacter(c, escapeDot))
            {
                AppendAnchorName(builder, c);
            }
            else
            {
                AppendAnchorName(builder, '\\');
                AppendAnchorName(builder, c);
            }
        }
    }

    static long EscapedAnchorNameLength(
        string value,
        int count,
        bool escapeDot)
    {
        long length = 0;
        for (int i = 0; i < count; i++)
        {
            char c = value[i];
            length += IsUnescapedAnchorNameCharacter(c, escapeDot)
                    ? 1
                    : 2;
        }
        return length;
    }

    static bool IsUnescapedAnchorNameCharacter(
        char value,
        bool escapeDot)
        => char.IsLetterOrDigit(value)
            || value is '_' or '`'
            || value == '.' && !escapeDot;

    static void AppendAnchorName(
        StringBuilder builder,
        char value)
    {
        if (builder.Length >= MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The member anchor name exceeds the encoded-character budget.");
        }
        builder.Append(value);
    }

    static void AppendAnchorName(
        StringBuilder builder,
        string value)
        => AppendAnchorName(builder, value, value.Length);

    static void AppendAnchorName(
        StringBuilder builder,
        string value,
        int count)
    {
        if (count < 0
            || count > value.Length
            || builder.Length
                > MetadataSafetyPolicy.MaxStructuralSignatureChars - count)
        {
            throw new BadImageFormatException(
                "The member anchor name exceeds the encoded-character budget.");
        }
        builder.Append(value, 0, count);
    }

    static string MethodMemberName(
        string methodName,
        IReadOnlyList<string> genericNames,
        AnchorSignatureWorkBudget? workBudget = null)
    {
        if (methodName == ".ctor")
            return "#ctor";
        if (genericNames.Count == 0)
            return methodName;

        long memberNameLength =
            methodName.Length + 2L + Math.Max(0, genericNames.Count - 1);
        foreach (string genericName in genericNames)
            memberNameLength += genericName.Length;
        workBudget?.ChargeProjection(2L * memberNameLength);
        var builder = new StringBuilder();
        AppendAnchorName(builder, methodName);
        AppendAnchorName(builder, "<");
        for (int i = 0; i < genericNames.Count; i++)
        {
            if (i > 0)
                AppendAnchorName(builder, ",");
            AppendAnchorName(builder, genericNames[i]);
        }
        AppendAnchorName(builder, ">");
        return builder.ToString();
    }

    public static string GetCanonicalSignature(ApiType type, ApiMember member)
    {
        // A persisted canonical identity (present for tuple-bearing members whose display
        // signature cannot be re-canonicalized from text after a JSON round-trip) is
        // authoritative: it was computed at extraction from the live structural model and
        // guarantees round-tripped surfaces pair with the same members read live.
        if (!string.IsNullOrEmpty(member.CanonicalSignature))
            return member.CanonicalSignature!;

        if (TryGetCanonicalSignature(type, member, out var canonicalSignature))
            return canonicalSignature;

        var declaringType = DeclaringTypeAnchorName(type, member);

        var kindCode = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.Kind is "field" or "event")
            return $"{kindCode}:{declaringType}.{member.Name}";

        // Note: TryGetCanonicalSignature above always succeeds for "property" (it has its
        // own SignatureModel-or-raw-signature fallback for indexer parameters), so this
        // method never actually reaches a "property" branch here. There is intentionally no
        // duplicate property-handling code below.

        var signature = member.Signature ?? member.ReturnType ?? member.Name;
        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : ExtractMemberNameWithGeneric(signature, member.Name);
        // Raw-signature fallback (used when SignatureModel is absent, e.g. after a JSON
        // round-trip where SignatureModel is [JsonIgnore]). member.Signature is the
        // display string and carries `dynamic`, so scrub it back to `object` for identity
        // exactly as the SignatureModel path does — otherwise a round-tripped member's
        // fingerprint diverges from the same member read live.
        var parameters = XmlDocumentationNotation.NormalizeDynamicToObject(
            ExtractCanonicalParameterList(signature));
        var canonical = $"{kindCode}:{declaringType}.{memberName}{parameters}";
        // Mirror the conversion-operator return-type disambiguation so member identity
        // is not dependent on whether SignatureModel was populated (the Try path above).
        if (IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(member.ReturnType))
            canonical += $"~{NormalizeCanonicalCommas(XmlDocumentationNotation.NormalizeDynamicToObject(member.ReturnType!))}";
        return canonical;
    }

    public static bool TryGetCanonicalSignature(ApiType type, ApiMember member, out string canonicalSignature)
    {
        // See GetCanonicalSignature: a persisted canonical identity is authoritative and
        // survives the JSON round-trip that discards SignatureModel.
        if (!string.IsNullOrEmpty(member.CanonicalSignature))
        {
            canonicalSignature = member.CanonicalSignature!;
            return true;
        }

        var declaringType = DeclaringTypeAnchorName(type, member);

        var kindCode = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.Kind is "field" or "event")
        {
            canonicalSignature = $"{kindCode}:{declaringType}.{member.Name}";
            return true;
        }

        if (member.Kind == "property")
        {
            // An indexer is a property with parameters -- include them in identity so
            // overloaded indexers (e.g. this[int] vs this[string]) don't collide on
            // "P:Type.Item" and get paired by declaration order instead of by their actual
            // parameter signature. Ordinary (parameterless) properties are unaffected: their
            // canonical signature format is unchanged from before this check existed.
            //
            // ApiSurface.SignatureModel is [JsonIgnore], so a JSON-round-tripped surface
            // (a supported, tested scenario -- see FallbackCanonicalSignature_* tests) has
            // no SignatureModel. Falling back to "" here would make a JSON-persisted
            // baseline's indexer canonical signature diverge from the same indexer read
            // live from the assembly, breaking pairing between the two. So when
            // SignatureModel is absent, parse the parameter list out of the raw
            // "this[...]" signature text instead, which IS preserved across JSON
            // round-trips.
            var indexerParameters = member.SignatureModel is { Parameters.Count: > 0 } propertySignature
                ? NormalizeCanonicalParameters(propertySignature.CanonicalParameterTypesSummary)
                : XmlDocumentationNotation.NormalizeDynamicToObject(
                    ExtractCanonicalIndexerParameterList(member.Signature));
            canonicalSignature = $"{kindCode}:{declaringType}.{member.Name}{indexerParameters}";
            return true;
        }

        if (member.SignatureModel is not { } signature)
        {
            canonicalSignature = "";
            return false;
        }

        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : LegacyCanonicalMemberName(member.Signature, member.Name)
              ?? (string.IsNullOrWhiteSpace(signature.MemberName)
                  ? member.Name
                  : signature.MemberName!);
        memberName = NormalizeCanonicalCommas(memberName);
        var canonical = $"{kindCode}:{declaringType}.{memberName}{NormalizeCanonicalParameters(signature.CanonicalParameterTypesSummary)}";
        // Conversion operators overload on return type, so the parameter list alone
        // is an ambiguous identity (every System.Decimal.op_Explicit(Decimal) collides).
        // Append a product-owned return-type suffix. It intentionally uses the
        // same "~ReturnType" delimiter as XML doc identity so conversion anchors
        // and XML lookups do not grow divergent spellings for the same fact.
        if (IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(signature.EffectiveCanonicalReturnType))
            canonical += $"~{NormalizeCanonicalCommas(XmlDocumentationNotation.NormalizeDynamicToObject(signature.EffectiveCanonicalReturnType!))}";
        canonicalSignature = canonical;
        return true;
    }

    static string DeclaringTypeAnchorName(ApiType type, ApiMember member)
    {
        if (!string.IsNullOrWhiteSpace(member.DeclaringTypeCanonicalName))
            return member.DeclaringTypeCanonicalName;
        if (string.IsNullOrWhiteSpace(member.DeclaringType))
            return FormatApiTypeAnchorName(type);
        if (type.DefinitionName is not null
            && string.Equals(
                member.DeclaringType,
                MetadataTypeNameFormatter.FormatFullName(type),
                StringComparison.Ordinal))
        {
            return FormatApiTypeAnchorName(type);
        }
        return member.DeclaringType;
    }

    internal static bool TryGetExtensionInstanceProjection(
        ApiType type,
        ApiMember member,
        out string identityKey,
        out string variant)
    {
        bool isExtension = member.Kind == "method" && member.IsExtension && member.IsStatic;
        bool isInstance = member.Kind == "method" && !member.IsExtension && !member.IsStatic;
        if ((!isExtension && !isInstance)
            || member.SignatureModel is not { ReturnType: not null } signature)
        {
            identityKey = "";
            variant = "";
            return false;
        }

        if (isExtension && signature.Parameters.Count == 0)
        {
            identityKey = "";
            variant = "";
            return false;
        }
        if (signature.Parameters.Any(parameter =>
                string.IsNullOrWhiteSpace(parameter.CanonicalTypeWithModifier)))
        {
            identityKey = "";
            variant = "";
            return false;
        }

        string receiver = isExtension
            ? signature.Parameters[0].CanonicalTypeWithModifier
            : type.FullName;
        if (string.IsNullOrWhiteSpace(receiver)
            || string.IsNullOrWhiteSpace(member.Name)
            || string.IsNullOrWhiteSpace(signature.EffectiveCanonicalReturnType))
        {
            identityKey = "";
            variant = "";
            return false;
        }

        var parameters = isExtension
            ? signature.Parameters.Skip(1)
            : signature.Parameters;
        var facets = new List<string>
        {
            NormalizeCorrespondenceType(receiver),
            member.Name,
            signature.TypeParameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeCorrespondenceType(signature.EffectiveCanonicalReturnType!),
        };
        facets.AddRange(parameters.Select(parameter =>
            NormalizeCorrespondenceType(parameter.CanonicalTypeWithModifier)));

        identityKey = string.Concat(facets.Select(facet => $"{facet.Length}:{facet}"));
        variant = isExtension ? "extension" : "instance";
        return true;
    }

    static string NormalizeCorrespondenceType(string type)
        => XmlDocumentationNotation.NormalizeDynamicToObject(type.Trim())
            .Replace("+", ".", StringComparison.Ordinal)
            .Replace(", ", ",", StringComparison.Ordinal);

    /// <summary>
    /// Locates the index of the parameter-list opening parenthesis in a member display
    /// signature, skipping a leading balanced parenthesized group that represents a C#
    /// tuple return type (e.g. <c>(int count, string name) Parse(...)</c>). A tuple return
    /// puts <c>(</c> at position 0, which is never the parameter list; returns -1 when no
    /// parameter-list parenthesis follows. Ordinary signatures (no leading tuple) resolve
    /// to the first <c>(</c> exactly as before, preserving existing digests.
    /// </summary>
    static int IndexOfParameterListParen(string signature)
    {
        var searchFrom = 0;
        if (signature.Length > 0 && signature[0] == '(')
        {
            var depth = 0;
            for (var i = 0; i < signature.Length; i++)
            {
                if (signature[i] == '(')
                {
                    depth++;
                }
                else if (signature[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        searchFrom = i + 1;
                        break;
                    }
                }
            }
        }

        return signature.IndexOf('(', searchFrom);
    }

    // Preserve the v1 Member Index digest contract for members that already have
    // compatibility signature text. The legacy parser had edge-case behavior
    // around method names inside generic parameter names, and published stable
    // selectors hash that exact canonical string.
    static string? LegacyCanonicalMemberName(string? signature, string memberName)
    {
        if (string.IsNullOrEmpty(signature))
            return null;

        var parenStart = IndexOfParameterListParen(signature);
        if (parenStart <= 0)
            return null;

        var nameIndex = signature.LastIndexOf(memberName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return null;

        var end = nameIndex + memberName.Length;
        if (end < parenStart && signature[end] == '<')
        {
            var depth = 0;
            for (var i = end; i < parenStart; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                        return signature[nameIndex..(i + 1)];
                }
            }
        }

        return memberName;
    }

    static string NormalizeCanonicalParameters(string parameterTypesSummary)
        => string.IsNullOrEmpty(parameterTypesSummary)
            ? "()"
            : NormalizeCanonicalCommas(
                XmlDocumentationNotation.NormalizeDynamicToObject(parameterTypesSummary));

    static string NormalizeCanonicalCommas(string value)
        => value.Replace(", ", ",", StringComparison.Ordinal).Trim();

    static string ExtractMemberNameWithGeneric(string signature, string memberName)
    {
        var parenStart = IndexOfParameterListParen(signature);
        if (parenStart <= 0)
            return memberName;

        var nameIndex = signature.LastIndexOf(memberName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return memberName;

        var end = nameIndex + memberName.Length;
        if (end < parenStart && signature[end] == '<')
        {
            var depth = 0;
            for (var i = end; i < parenStart; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                        return NormalizeCanonicalCommas(signature[nameIndex..(i + 1)]);
                }
            }
        }

        return memberName;
    }

    static string ExtractCanonicalParameterList(string signature)
    {
        var abbreviated = AbbreviateSignature(signature);
        var parenStart = abbreviated.IndexOf('(');
        var parenEnd = abbreviated.LastIndexOf(')');
        if (parenStart < 0 || parenEnd < parenStart)
            return "()";

        var parameters = abbreviated[parenStart..(parenEnd + 1)];
        return NormalizeCanonicalCommas(parameters);
    }

    /// <summary>
    /// Extracts the canonical, parenthesized parameter-type list from an indexer's raw
    /// signature text (e.g. "int this[string key] { get; }" -> "(string)"), or "" when the
    /// signature has no "this[...]" indexer parameter list (an ordinary, non-indexed
    /// property). Kept in sync with ApiSurfaceExtractor's raw indexer signature format.
    /// </summary>
    static string ExtractCanonicalIndexerParameterList(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        var indexerKeyword = signature.IndexOf("this[", StringComparison.Ordinal);
        if (indexerKeyword < 0)
            return "";

        var bracketStart = indexerKeyword + "this".Length;
        var depth = 0;
        var bracketEnd = -1;
        for (var i = bracketStart; i < signature.Length; i++)
        {
            if (signature[i] == '[')
                depth++;
            else if (signature[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    bracketEnd = i;
                    break;
                }
            }
        }

        if (bracketEnd < 0)
            return "";

        var parameterSection = signature[(bracketStart + 1)..bracketEnd];
        // Reuse the existing parenthesized-parameter-list machinery (type extraction,
        // default-value stripping, generic-depth-aware comma splitting) by round-tripping
        // the bracketed indexer parameters through the parenthesized form it expects.
        return ExtractCanonicalParameterList($"({parameterSection})");
    }

    static string AbbreviateSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return signature;

        int parenEnd = signature.LastIndexOf(')');
        if (parenEnd < parenStart + 1)
            return signature;

        string prefix = signature[..(parenStart + 1)];
        string suffix = signature[parenEnd..];
        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return signature;

        // The signature is a lossy display string. Parameter names (F# quoted
        // identifiers may contain spaces, '=', quotes, brackets, angle brackets,
        // parentheses, and commas), array-rank/type spellings, and default-value
        // literals can all contain characters that look structural, so no parser can
        // be fully robust here. Every deviation from main's splitter risks changing
        // the canonical signature for some compiler-emittable name (e.g. splitting
        // main's combined <(...)> depth into independent counters regresses an F#
        // name like ``x<)`` where '<' and ')' cross-cancel). This fallback therefore
        // reproduces main's splitter EXACTLY, adding only the one thing #2940 needs:
        // it skips a leading attribute list ("[Optional, DateTimeConstant(ticks)]
        // type name") so the comma inside it does not split the parameter list.
        //
        // Bracket nesting is tracked ONLY inside that leading attribute list, at the
        // very start of a parameter. Once the first real (non-space, non-'[') type
        // character is seen, tracking reverts to main's single combined depth counter
        // over '<'/'>'/'('/')' , so brackets in array types ("int[]") or in F#
        // quoted names ("x[") are ordinary text, and generic/tuple commas stay
        // protected — identical to main. String/char literal and default-value
        // tracking are deliberately omitted: any such heuristic is defeatable by a
        // quote or delimiter inside a name, which main treats as ordinary.
        List<string> paramTypes = [];
        int depth = 0;
        int attrBracketDepth = 0;
        int lastSplit = 0;
        bool inLeadingAttributes = true;
        for (int i = 0; i < paramSection.Length; i++)
        {
            char c = paramSection[i];

            if (inLeadingAttributes)
            {
                if (c == '[') { attrBracketDepth++; continue; }
                if (attrBracketDepth > 0)
                {
                    if (c == ']') attrBracketDepth--;
                    continue; // characters inside the attribute list (incl. commas) are skipped
                }
                if (c == ' ') continue; // still in the leading region, between/after attribute lists
                inLeadingAttributes = false; // first real type character; fall through to main's logic
            }

            if (c == '<' || c == '(') depth++;
            else if (c == '>' || c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                paramTypes.Add(ExtractParamType(paramSection[lastSplit..i].Trim()));
                lastSplit = i + 1;
                attrBracketDepth = 0;
                inLeadingAttributes = true;
            }
        }
        paramTypes.Add(ExtractParamType(paramSection[lastSplit..].Trim()));

        return prefix + string.Join(", ", paramTypes) + suffix;
    }

    static string ExtractParamType(string param)
        => XmlDocumentationNotation.ExtractSignatureParameterType(param);

    public static bool TryGetXmlDocMemberIdentity(ApiType type, ApiMember member, out XmlDocMemberIdentity identity)
    {
        var prefix = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.SignatureModel is not { } signature)
        {
            identity = new XmlDocMemberIdentity("", []);
            return false;
        }

        var conversionReturnType = IsConversionOperator(member.Name)
            && !string.IsNullOrWhiteSpace(signature.EffectiveCanonicalReturnType)
            ? signature.EffectiveCanonicalReturnType
            : null;
        identity = XmlDocumentationNotation.CreateMemberIdentity(
            prefix,
            type.FullName,
            member.Name,
            signature.Parameters.Select(parameter => parameter.CanonicalTypeWithModifier).ToList(),
            type.TypeParameters.Select(parameter => parameter.Name).ToList(),
            signature.MemberName,
            conversionReturnType);
        return true;
    }

    public static bool IsConversionOperator(string memberName)
        => ConversionOperatorNames.Contains(memberName, StringComparer.Ordinal);
}
