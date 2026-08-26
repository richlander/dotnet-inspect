using CSharpText;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;
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

internal sealed record MethodAnchorDeclaringTypeContext(
    TypeDefinitionHandle Handle,
    string FullName,
    GenericContext GenericContext);

internal sealed record MethodCorrespondenceAnchorInfo(
    MethodAnchorInfo AnchorInfo,
    bool IsExtensionMethod,
    byte SignatureHeader,
    int GenericParameterCount,
    int RequiredParameterCount,
    int ParameterCount,
    ApiMemberIdentity.MethodTypeCorrespondence CorrespondenceReturnType,
    ImmutableArray<ApiMemberIdentity.MethodTypeCorrespondence>
        CorrespondenceParameterTypes);

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
    internal sealed class MethodCorrespondenceContext
    {
        readonly Dictionary<
            MetadataReader,
            IntrinsicCoreLibraryForwardedRootProjection>
            _intrinsicCoreLibraryForwardedRoots = [];
        readonly Dictionary<
            MetadataReader,
            AssemblyReferenceProjectionCache>
            _assemblyReferenceProjections = [];
        readonly Dictionary<
            MetadataReader,
            HashSet<AssemblyReferenceHandle>>
            _chargedAssemblyReferences = [];
        readonly Dictionary<
            MetadataReader,
            Dictionary<AssemblyReferenceHandle, ExceptionDispatchInfo>>
            _failedAssemblyReferenceProjections = [];

        internal IntrinsicCoreLibraryForwardedRootProjection
            GetOrAddIntrinsicCoreLibraryForwardedRoots(
            MetadataReader reader,
            Func<IntrinsicCoreLibraryForwardedRootProjection> create)
        {
            if (!_intrinsicCoreLibraryForwardedRoots.TryGetValue(
                    reader,
                    out IntrinsicCoreLibraryForwardedRootProjection? roots))
            {
                roots = create();
                _intrinsicCoreLibraryForwardedRoots.Add(reader, roots);
            }
            return roots;
        }

        internal AssemblyReferenceIdentity ProjectAssemblyReference(
            MetadataReader reader,
            AssemblyReferenceHandle handle,
            Action<int> charge)
        {
            if (!_assemblyReferenceProjections.TryGetValue(
                    reader,
                    out AssemblyReferenceProjectionCache? projection))
            {
                projection = new AssemblyReferenceProjectionCache(reader);
                _assemblyReferenceProjections.Add(reader, projection);
            }
            if (!_chargedAssemblyReferences.TryGetValue(
                    reader,
                    out HashSet<AssemblyReferenceHandle>? charged))
            {
                charged = [];
                _chargedAssemblyReferences.Add(reader, charged);
            }
            if (!_failedAssemblyReferenceProjections.TryGetValue(
                    reader,
                    out Dictionary<
                        AssemblyReferenceHandle,
                        ExceptionDispatchInfo>? failures))
            {
                failures = [];
                _failedAssemblyReferenceProjections.Add(
                    reader,
                    failures);
            }
            if (failures.TryGetValue(
                    handle,
                    out ExceptionDispatchInfo? failure))
            {
                failure.Throw();
            }

            bool added = charged.Add(handle);
            bool chargeCompleted = !added;
            bool succeeded = false;
            bool failureCached = false;
            try
            {
                if (added)
                {
                    int nameLength;
                    int cultureLength;
                    int keyLength;
                    try
                    {
                        System.Reflection.Metadata.AssemblyReference
                            reference =
                                reader.GetAssemblyReference(handle);
                        nameLength =
                            reader.GetBlobReader(reference.Name).Length;
                        cultureLength =
                            reference.Culture.IsNil
                                ? 0
                                : reader.GetBlobReader(
                                    reference.Culture).Length;
                        keyLength =
                            reference.PublicKeyOrToken.IsNil
                                ? 0
                                : reader.GetBlobReader(
                                    reference.PublicKeyOrToken).Length;
                    }
                    catch (Exception ex) when (
                        ex is BadImageFormatException
                            or ArgumentOutOfRangeException)
                    {
                        failures[handle] =
                            ExceptionDispatchInfo.Capture(ex);
                        failureCached = true;
                        throw;
                    }

                    charge(nameLength);
                    charge(cultureLength);
                    charge(keyLength);
                    chargeCompleted = true;
                }

                AssemblyReferenceIdentity identity =
                    AssemblyReferenceIdentity.From(
                        handle,
                        projection);
                succeeded = true;
                return identity;
            }
            catch (Exception ex) when (
                chargeCompleted
                && ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                failures[handle] =
                    ExceptionDispatchInfo.Capture(ex);
                failureCached = true;
                throw;
            }
            finally
            {
                if (added && !succeeded && !failureCached)
                    charged.Remove(handle);
            }
        }
    }

    internal sealed class IntrinsicCoreLibraryForwardedRootProjection
    {
        readonly Dictionary<
            (string Namespace, string Name),
            IntrinsicCoreLibraryForwardedRootEvidence> _roots = [];

        internal void Record(
            (string Namespace, string Name) root,
            bool authorized)
        {
            IntrinsicCoreLibraryForwardedRootEvidence evidence =
                authorized
                    ? IntrinsicCoreLibraryForwardedRootEvidence.Authorized
                    : IntrinsicCoreLibraryForwardedRootEvidence.Rejected;
            if (_roots.TryAdd(root, evidence))
                return;

            IntrinsicCoreLibraryForwardedRootEvidence current =
                _roots[root];
            if (current
                    == IntrinsicCoreLibraryForwardedRootEvidence.Malformed)
            {
                return;
            }
            if (current
                    == IntrinsicCoreLibraryForwardedRootEvidence.Authorized
                || authorized)
            {
                _roots[root] =
                    IntrinsicCoreLibraryForwardedRootEvidence.Conflicting;
            }
        }

        internal void RecordMalformed(
            (string Namespace, string Name) root) =>
            _roots[root] =
                IntrinsicCoreLibraryForwardedRootEvidence.Malformed;

        internal bool Authorizes(
            (string Namespace, string Name) root)
        {
            if (!_roots.TryGetValue(
                    root,
                    out IntrinsicCoreLibraryForwardedRootEvidence evidence))
            {
                return false;
            }
            if (evidence
                == IntrinsicCoreLibraryForwardedRootEvidence.Conflicting)
            {
                throw new BadImageFormatException(
                    "The intrinsic core-library type has conflicting exported-root evidence.");
            }
            if (evidence
                == IntrinsicCoreLibraryForwardedRootEvidence.Malformed)
            {
                throw new BadImageFormatException(
                    "The intrinsic core-library type has malformed exported-root evidence.");
            }
            return evidence
                == IntrinsicCoreLibraryForwardedRootEvidence.Authorized;
        }
    }

    enum IntrinsicCoreLibraryForwardedRootEvidence
    {
        Rejected,
        Authorized,
        Conflicting,
        Malformed,
    }

    internal sealed class MethodTypeCorrespondence
    {
        readonly AnchorSignatureType _type;

        internal MethodTypeCorrespondence(
            AnchorSignatureType type)
        {
            _type = type;
            PrepareCorrespondenceIdentities(
                type,
                includeOptionalModifiers: false);
        }

        internal bool CorrespondsTo(
            MethodTypeCorrespondence other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return CorrespondenceTypesEqual(
                _type,
                other._type,
                includeOptionalModifiers: false);
        }
    }

    enum NamedTypeScopeKind
    {
        Current,
        IntrinsicCoreLibrary,
        Assembly,
        Module,
    }

    sealed record NamedTypeCorrespondenceIdentity(
        NamedTypeScopeKind ScopeKind,
        AssemblyReferenceIdentity? Assembly,
        string? ModuleName,
        string Namespace,
        ImmutableArray<string> Segments,
        int GenericArity,
        byte RawTypeKind,
        bool AuthorizesCurrentToIntrinsicCoreLibrary)
    {
        internal bool CorrespondsTo(
            NamedTypeCorrespondenceIdentity other)
        {
            if (!Namespace.Equals(
                    other.Namespace,
                    StringComparison.Ordinal)
                || !Segments.SequenceEqual(
                    other.Segments,
                    StringComparer.Ordinal)
                || RawTypeKind != other.RawTypeKind)
            {
                return false;
            }

            if (ScopeKind == NamedTypeScopeKind.Current
                && other.ScopeKind
                    == NamedTypeScopeKind.IntrinsicCoreLibrary)
            {
                return other
                    .AuthorizesCurrentToIntrinsicCoreLibrary;
            }
            if (ScopeKind
                    == NamedTypeScopeKind.IntrinsicCoreLibrary
                && other.ScopeKind == NamedTypeScopeKind.Current)
            {
                return AuthorizesCurrentToIntrinsicCoreLibrary;
            }
            if (ScopeKind != other.ScopeKind)
                return false;

            return ScopeKind switch
            {
                NamedTypeScopeKind.Current => true,
                NamedTypeScopeKind.IntrinsicCoreLibrary => true,
                NamedTypeScopeKind.Assembly =>
                    Assembly is not null
                    && other.Assembly is not null
                    && Assembly.IsEquivalentIgnoringVersion(
                        other.Assembly),
                NamedTypeScopeKind.Module =>
                    string.Equals(
                        ModuleName,
                        other.ModuleName,
                        StringComparison.Ordinal),
                _ => false,
            };
        }
    }

    internal abstract class AnchorSignatureType
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

        // Conservative character-comparison work for the structural
        // correspondence projection. This is not a rendered identity length.
        internal virtual bool HasCorrespondenceDetails => false;
        internal virtual int CorrespondenceLength =>
            GetCorrespondenceLength(
                includeOptionalModifiers: false);
        internal virtual int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => Length;

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

    sealed class EncodedAnchorSignatureType
        : AnchorSignatureType
    {
        internal readonly string _text;
        internal readonly string? _correspondence;

        internal EncodedAnchorSignatureType(
            string text,
            string? correspondence = null)
            : base(text.Length)
        {
            _text = text;
            _correspondence = correspondence;
        }

        internal override void AppendTo(StringBuilder builder)
            => builder.Append(_text);

        internal override bool HasCorrespondenceDetails =>
            _correspondence is not null;

        internal override int CorrespondenceLength =>
            _correspondence?.Length ?? Length;

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => CorrespondenceLength;

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
        readonly Func<
            MetadataReader,
            TypeReferenceHandle,
            NamedTypeCorrespondenceIdentity>?
            _readCorrespondenceIdentity;
        readonly int _correspondenceLength;
        string? _text;
        NamedTypeCorrespondenceIdentity? _correspondenceIdentity;

        internal LazyTypeReferenceAnchorSignatureType(
            MetadataReader reader,
            TypeReferenceHandle handle,
            int estimatedLength,
            Func<MetadataReader, TypeReferenceHandle, string> format,
            int correspondenceLength,
            Func<
                MetadataReader,
                TypeReferenceHandle,
                NamedTypeCorrespondenceIdentity>?
                readCorrespondenceIdentity)
            : base(estimatedLength)
        {
            _reader = reader;
            _handle = handle;
            _format = format;
            _correspondenceLength = correspondenceLength;
            _readCorrespondenceIdentity =
                readCorrespondenceIdentity;
        }

        internal override void AppendTo(StringBuilder builder)
            => builder.Append(_text ??= _format(_reader, _handle));

        internal override bool HasCorrespondenceDetails =>
            _readCorrespondenceIdentity is not null;

        internal override int CorrespondenceLength =>
            HasCorrespondenceDetails
                ? _correspondenceLength
                : Length;

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => CorrespondenceLength;

        internal NamedTypeCorrespondenceIdentity
            GetCorrespondenceIdentity()
            => _correspondenceIdentity
                ?? throw new InvalidOperationException(
                    "The correspondence identity was not prepared.");

        internal void PrepareCorrespondenceIdentity()
        {
            if (_correspondenceIdentity is not null)
                return;
            _correspondenceIdentity =
                _readCorrespondenceIdentity is not null
                    ? _readCorrespondenceIdentity(
                        _reader,
                        _handle)
                    : throw new InvalidOperationException(
                        "The type has no correspondence identity.");
        }
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
        readonly Func<
            MetadataReader,
            TypeDefinitionHandle,
            NamedTypeCorrespondenceIdentity>?
            _readCorrespondenceIdentity;
        readonly int _correspondenceLength;
        string? _text;
        NamedTypeCorrespondenceIdentity? _correspondenceIdentity;

        internal LazyTypeDefinitionAnchorSignatureType(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            int estimatedLength,
            Func<MetadataReader, TypeDefinitionHandle, string> format,
            int correspondenceLength,
            Func<
                MetadataReader,
                TypeDefinitionHandle,
                NamedTypeCorrespondenceIdentity>?
                readCorrespondenceIdentity)
            : base(estimatedLength)
        {
            _reader = reader;
            _handle = handle;
            _format = format;
            _correspondenceLength = correspondenceLength;
            _readCorrespondenceIdentity =
                readCorrespondenceIdentity;
        }

        internal override void AppendTo(StringBuilder builder)
            => builder.Append(_text ??= _format(_reader, _handle));

        internal override bool HasCorrespondenceDetails =>
            _readCorrespondenceIdentity is not null;

        internal override int CorrespondenceLength =>
            HasCorrespondenceDetails
                ? _correspondenceLength
                : Length;

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => CorrespondenceLength;

        internal NamedTypeCorrespondenceIdentity
            GetCorrespondenceIdentity()
            => _correspondenceIdentity
                ?? throw new InvalidOperationException(
                    "The correspondence identity was not prepared.");

        internal void PrepareCorrespondenceIdentity()
        {
            if (_correspondenceIdentity is not null)
                return;
            _correspondenceIdentity =
                _readCorrespondenceIdentity is not null
                    ? _readCorrespondenceIdentity(
                        _reader,
                        _handle)
                    : throw new InvalidOperationException(
                        "The type has no correspondence identity.");
        }
    }

    sealed class WrappedAnchorSignatureType
        : AnchorSignatureType
    {
        internal readonly string _prefix;
        internal readonly AnchorSignatureType _value;
        internal readonly string _suffix;

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

        internal override bool HasCorrespondenceDetails =>
            _value.HasCorrespondenceDetails;

        internal override int CorrespondenceLength =>
            CheckedLength(
                _prefix.Length,
                _value.CorrespondenceLength,
                _suffix.Length);

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => CheckedLength(
                _prefix.Length,
                _value.GetCorrespondenceLength(
                    includeOptionalModifiers),
                _suffix.Length);

    }

    sealed class ArrayAnchorSignatureType
        : AnchorSignatureType
    {
        internal readonly AnchorSignatureType _elementType;
        internal readonly int _rank;
        internal readonly ImmutableArray<int> _sizes;
        internal readonly ImmutableArray<int> _lowerBounds;

        internal ArrayAnchorSignatureType(
            AnchorSignatureType elementType,
            int rank,
            ImmutableArray<int> sizes,
            ImmutableArray<int> lowerBounds)
            : base(
                CheckedLength(
                    elementType.Length,
                    rank <= 1
                        ? 3
                        : CheckedLength(rank, 1)))
        {
            _elementType = elementType;
            _rank = rank;
            _sizes = sizes;
            _lowerBounds = lowerBounds;
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

        internal override bool HasCorrespondenceDetails =>
            _elementType.HasCorrespondenceDetails;

        internal override int CorrespondenceLength =>
            CheckedLength(
                _elementType.CorrespondenceLength,
                _rank <= 1
                    ? 3
                    : CheckedLength(_rank, 1));

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => CheckedLength(
                _elementType.GetCorrespondenceLength(
                    includeOptionalModifiers),
                _rank <= 1
                    ? 3
                    : CheckedLength(_rank, 1));

    }

    sealed class JoinedAnchorSignatureType
        : AnchorSignatureType
    {
        internal readonly string _prefix;
        internal readonly ImmutableArray<AnchorSignatureType>
            _values;
        internal readonly string _separator;
        internal readonly string _suffix;

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

        internal override bool HasCorrespondenceDetails =>
            _values.Any(
                static value => value.HasCorrespondenceDetails);

        internal override int CorrespondenceLength =>
            GetCorrespondenceLength(
                _prefix,
                _values,
                _separator,
                _suffix,
                includeOptionalModifiers: false);

        static int GetCorrespondenceLength(
            string prefix,
            ImmutableArray<AnchorSignatureType> values,
            string separator,
            string suffix,
            bool includeOptionalModifiers)
        {
            int length = CheckedLength(
                prefix.Length,
                suffix.Length);
            if (values.Length > 1)
            {
                length = CheckedLength(
                    length,
                    CheckedProduct(
                        separator.Length,
                        values.Length - 1));
            }
            foreach (AnchorSignatureType value in values)
            {
                length = CheckedLength(
                    length,
                    value.GetCorrespondenceLength(
                        includeOptionalModifiers));
            }
            return length;
        }

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => GetCorrespondenceLength(
                _prefix,
                _values,
                _separator,
                _suffix,
                includeOptionalModifiers);

    }

    sealed class GenericAnchorSignatureType
        : AnchorSignatureType
    {
        internal readonly AnchorSignatureType _genericType;
        internal readonly ImmutableArray<AnchorSignatureType>
            _typeArguments;

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

        internal override bool HasCorrespondenceDetails =>
            _genericType.HasCorrespondenceDetails
            || _typeArguments.Any(
                static argument => argument.HasCorrespondenceDetails);

        internal override int CorrespondenceLength
        {
            get
            {
                int length = CheckedLength(
                    _genericType.CorrespondenceLength,
                    2);
                if (_typeArguments.Length > 1)
                {
                    length = CheckedLength(
                        length,
                        _typeArguments.Length - 1);
                }
                foreach (AnchorSignatureType argument in _typeArguments)
                {
                    length = CheckedLength(
                        length,
                        argument.CorrespondenceLength);
                }
                return length;
            }
        }

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
        {
            int length = CheckedLength(
                _genericType.GetCorrespondenceLength(
                    includeOptionalModifiers),
                2);
            if (_typeArguments.Length > 1)
            {
                length = CheckedLength(
                    length,
                    _typeArguments.Length - 1);
            }
            foreach (AnchorSignatureType argument in _typeArguments)
            {
                length = CheckedLength(
                    length,
                    argument.GetCorrespondenceLength(
                        includeOptionalModifiers));
            }
            return length;
        }

    }

    sealed class ModifiedAnchorSignatureType
        : AnchorSignatureType
    {
        const string RequiredPrefix = "modreq(";
        const string OptionalPrefix = "modopt(";
        const string ModifierSuffix = ")";

        internal readonly AnchorSignatureType _modifier;
        internal readonly AnchorSignatureType _unmodifiedType;
        internal readonly bool _isRequired;

        internal ModifiedAnchorSignatureType(
            AnchorSignatureType modifier,
            AnchorSignatureType unmodifiedType,
            bool isRequired)
            : base(unmodifiedType.Length)
        {
            _modifier = modifier;
            _unmodifiedType = unmodifiedType;
            _isRequired = isRequired;
        }

        internal override void AppendTo(StringBuilder builder)
            => _unmodifiedType.AppendTo(builder);

        internal override bool HasCorrespondenceDetails =>
            _isRequired
            || _unmodifiedType.HasCorrespondenceDetails;

        internal override int CorrespondenceLength =>
            GetCorrespondenceLength(
                includeOptionalModifiers: false);

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
        {
            if (!_isRequired && !includeOptionalModifiers)
                return _unmodifiedType.CorrespondenceLength;

            string prefix =
                _isRequired
                    ? RequiredPrefix
                    : OptionalPrefix;
            return CheckedLength(
                prefix.Length,
                _modifier.GetCorrespondenceLength(
                    includeOptionalModifiers),
                ModifierSuffix.Length,
                _unmodifiedType.GetCorrespondenceLength(
                    includeOptionalModifiers));
        }

    }

    sealed class FunctionPointerAnchorSignatureType
        : AnchorSignatureType
    {
        internal readonly MethodSignature<AnchorSignatureType>
            _signature;
        readonly JoinedAnchorSignatureType _display;
        readonly int _correspondenceHeaderLength;

        internal FunctionPointerAnchorSignatureType(
            MethodSignature<AnchorSignatureType> signature)
            : base(
                new JoinedAnchorSignatureType(
                    "delegate*<",
                    signature.ParameterTypes.Add(
                        signature.ReturnType),
                    ",",
                    ">").Length)
        {
            _signature = signature;
            _display = new JoinedAnchorSignatureType(
                "delegate*<",
                signature.ParameterTypes.Add(
                    signature.ReturnType),
                ",",
                ">");
            string correspondenceHeader =
                $"fnptr[{signature.Header.RawValue}:"
                + $"{signature.GenericParameterCount}:"
                + $"{signature.RequiredParameterCount}]<";
            _correspondenceHeaderLength =
                correspondenceHeader.Length;
        }

        internal override void AppendTo(StringBuilder builder)
            => _display.AppendTo(builder);

        internal override bool HasCorrespondenceDetails => true;

        internal override int CorrespondenceLength
        {
            get
            {
                int length = CheckedLength(
                    _correspondenceHeaderLength,
                    1);
                ImmutableArray<AnchorSignatureType> types =
                    _signature.ParameterTypes.Add(
                        _signature.ReturnType);
                if (types.Length > 1)
                    length = CheckedLength(length, types.Length - 1);
                foreach (AnchorSignatureType type in types)
                {
                    length = CheckedLength(
                        length,
                        type.GetCorrespondenceLength(
                            includeOptionalModifiers: true));
                }
                return length;
            }
        }

        internal override int GetCorrespondenceLength(
            bool includeOptionalModifiers)
            => CorrespondenceLength;

    }

    static bool CorrespondenceTypesEqual(
        AnchorSignatureType left,
        AnchorSignatureType right,
        bool includeOptionalModifiers)
    {
        while (left is ModifiedAnchorSignatureType
            {
                _isRequired: false,
            } leftOptional
            && !includeOptionalModifiers)
        {
            left = leftOptional._unmodifiedType;
        }
        while (right is ModifiedAnchorSignatureType
            {
                _isRequired: false,
            } rightOptional
            && !includeOptionalModifiers)
        {
            right = rightOptional._unmodifiedType;
        }

        return (left, right) switch
        {
            (EncodedAnchorSignatureType l,
                EncodedAnchorSignatureType r) =>
                string.Equals(
                    l._correspondence ?? l._text,
                    r._correspondence ?? r._text,
                    StringComparison.Ordinal),
            (LazyTypeReferenceAnchorSignatureType l,
                LazyTypeReferenceAnchorSignatureType r) =>
                l.GetCorrespondenceIdentity().CorrespondsTo(
                    r.GetCorrespondenceIdentity()),
            (LazyTypeReferenceAnchorSignatureType l,
                LazyTypeDefinitionAnchorSignatureType r) =>
                l.GetCorrespondenceIdentity().CorrespondsTo(
                    r.GetCorrespondenceIdentity()),
            (LazyTypeDefinitionAnchorSignatureType l,
                LazyTypeReferenceAnchorSignatureType r) =>
                l.GetCorrespondenceIdentity().CorrespondsTo(
                    r.GetCorrespondenceIdentity()),
            (LazyTypeDefinitionAnchorSignatureType l,
                LazyTypeDefinitionAnchorSignatureType r) =>
                l.GetCorrespondenceIdentity().CorrespondsTo(
                    r.GetCorrespondenceIdentity()),
            (WrappedAnchorSignatureType l,
                WrappedAnchorSignatureType r) =>
                l._prefix == r._prefix
                && l._suffix == r._suffix
                && CorrespondenceTypesEqual(
                    l._value,
                    r._value,
                    includeOptionalModifiers),
            (ArrayAnchorSignatureType l,
                ArrayAnchorSignatureType r) =>
                l._rank == r._rank
                && l._sizes.SequenceEqual(r._sizes)
                && l._lowerBounds.SequenceEqual(r._lowerBounds)
                && CorrespondenceTypesEqual(
                    l._elementType,
                    r._elementType,
                    includeOptionalModifiers),
            (JoinedAnchorSignatureType l,
                JoinedAnchorSignatureType r) =>
                l._prefix == r._prefix
                && l._separator == r._separator
                && l._suffix == r._suffix
                && CorrespondenceSequencesEqual(
                    l._values,
                    r._values,
                    includeOptionalModifiers),
            (GenericAnchorSignatureType l,
                GenericAnchorSignatureType r) =>
                CorrespondenceTypesEqual(
                    l._genericType,
                    r._genericType,
                    includeOptionalModifiers)
                && CorrespondenceSequencesEqual(
                    l._typeArguments,
                    r._typeArguments,
                    includeOptionalModifiers),
            (ModifiedAnchorSignatureType l,
                ModifiedAnchorSignatureType r) =>
                l._isRequired == r._isRequired
                && CorrespondenceTypesEqual(
                    l._modifier,
                    r._modifier,
                    includeOptionalModifiers)
                && CorrespondenceTypesEqual(
                    l._unmodifiedType,
                    r._unmodifiedType,
                    includeOptionalModifiers),
            (FunctionPointerAnchorSignatureType l,
                FunctionPointerAnchorSignatureType r) =>
                FunctionPointerCorrespondenceEquals(l, r),
            _ => false,
        };
    }

    static void PrepareCorrespondenceIdentities(
        AnchorSignatureType type,
        bool includeOptionalModifiers)
    {
        if (type is ModifiedAnchorSignatureType
            {
                _isRequired: false,
            } optional
            && !includeOptionalModifiers)
        {
            PrepareCorrespondenceIdentities(
                optional._unmodifiedType,
                includeOptionalModifiers);
            return;
        }

        switch (type)
        {
            case LazyTypeReferenceAnchorSignatureType reference:
                reference.PrepareCorrespondenceIdentity();
                break;
            case LazyTypeDefinitionAnchorSignatureType definition:
                definition.PrepareCorrespondenceIdentity();
                break;
            case WrappedAnchorSignatureType wrapped:
                PrepareCorrespondenceIdentities(
                    wrapped._value,
                    includeOptionalModifiers);
                break;
            case ArrayAnchorSignatureType array:
                PrepareCorrespondenceIdentities(
                    array._elementType,
                    includeOptionalModifiers);
                break;
            case JoinedAnchorSignatureType joined:
                foreach (AnchorSignatureType value in joined._values)
                {
                    PrepareCorrespondenceIdentities(
                        value,
                        includeOptionalModifiers);
                }
                break;
            case GenericAnchorSignatureType generic:
                PrepareCorrespondenceIdentities(
                    generic._genericType,
                    includeOptionalModifiers);
                foreach (AnchorSignatureType argument
                    in generic._typeArguments)
                {
                    PrepareCorrespondenceIdentities(
                        argument,
                        includeOptionalModifiers);
                }
                break;
            case ModifiedAnchorSignatureType modified:
                PrepareCorrespondenceIdentities(
                    modified._modifier,
                    includeOptionalModifiers);
                PrepareCorrespondenceIdentities(
                    modified._unmodifiedType,
                    includeOptionalModifiers);
                break;
            case FunctionPointerAnchorSignatureType functionPointer:
                PrepareCorrespondenceIdentities(
                    functionPointer._signature.ReturnType,
                    includeOptionalModifiers: true);
                foreach (AnchorSignatureType parameter
                    in functionPointer._signature.ParameterTypes)
                {
                    PrepareCorrespondenceIdentities(
                        parameter,
                        includeOptionalModifiers: true);
                }
                break;
        }
    }

    static bool FunctionPointerCorrespondenceEquals(
        FunctionPointerAnchorSignatureType left,
        FunctionPointerAnchorSignatureType right)
    {
        MethodSignature<AnchorSignatureType> leftSignature =
            left._signature;
        MethodSignature<AnchorSignatureType> rightSignature =
            right._signature;
        return leftSignature.Header.RawValue
                == rightSignature.Header.RawValue
            && leftSignature.GenericParameterCount
                == rightSignature.GenericParameterCount
            && leftSignature.RequiredParameterCount
                == rightSignature.RequiredParameterCount
            && CorrespondenceTypesEqual(
                leftSignature.ReturnType,
                rightSignature.ReturnType,
                includeOptionalModifiers: true)
            && CorrespondenceSequencesEqual(
                leftSignature.ParameterTypes,
                rightSignature.ParameterTypes,
                includeOptionalModifiers: true);
    }

    static bool CorrespondenceSequencesEqual(
        ImmutableArray<AnchorSignatureType> left,
        ImmutableArray<AnchorSignatureType> right,
        bool includeOptionalModifiers)
    {
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (!CorrespondenceTypesEqual(
                    left[i],
                    right[i],
                    includeOptionalModifiers))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Cumulative work budget for one member-anchor signature construction.
    /// Mirrors <c>StructuralSignatureWorkBudget</c>: charge every materialized
    /// type-name occurrence and every composite type node so repeated long
    /// names and nested compositions cannot amplify past
    /// <see cref="MetadataSafetyPolicy.MaxAnchorSignatureWorkChars"/> before
    /// rejection. Gated by
    /// <c>CreateMethodAnchor_RepeatedTypeNamesFailBeforeLargeAllocation</c>
    /// and
    /// <c>CreateMethodAnchor_NestedArrayModoptsFailBeforeLargeAllocation</c>.
    /// </summary>
    sealed class AnchorSignatureWorkBudget
    {
        int _remaining;
        bool _exhausted;

        internal AnchorSignatureWorkBudget()
            : this(MetadataSafetyPolicy.MaxAnchorSignatureWorkChars)
        {
        }

        internal AnchorSignatureWorkBudget(int remaining)
        {
            _remaining = remaining;
        }

        internal int Remaining => _exhausted ? 0 : _remaining;

        internal void Charge(int characters)
        {
            if (_exhausted || characters < 0 || characters > _remaining)
            {
                _exhausted = true;
                throw new BadImageFormatException(
                    "The member anchor signature exceeds the cumulative work budget.");
            }
            _remaining -= characters;
        }
    }

    sealed class AnchorSignatureTypeProvider
        : ISignatureTypeProvider<AnchorSignatureType, GenericContext?>
    {
        readonly AnchorSignatureWorkBudget _workBudget;
        readonly bool _includeCorrespondence;
        readonly MethodCorrespondenceContext?
            _correspondenceContext;
        Dictionary<
            (TypeDefinitionHandle Handle, byte RawTypeKind),
            AnchorSignatureType>? _definitionCache;
        Dictionary<
            (TypeReferenceHandle Handle, byte RawTypeKind),
            AnchorSignatureType>? _referenceCache;

        internal AnchorSignatureTypeProvider(
            AnchorSignatureWorkBudget workBudget,
            bool includeCorrespondence = false,
            MethodCorrespondenceContext? correspondenceContext = null)
        {
            _workBudget = workBudget;
            _includeCorrespondence = includeCorrespondence;
            _correspondenceContext = correspondenceContext;
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
            var cacheKey = (handle, rawTypeKind);
            if (_definitionCache.TryGetValue(
                    cacheKey,
                    out AnchorSignatureType? cached))
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
            int correspondenceLength =
                _includeCorrespondence
                    ? EstimateDefinitionCorrespondenceLength(
                        reader,
                        handle)
                    : estimatedLength;
            AnchorSignatureType encoded = new LazyTypeDefinitionAnchorSignatureType(
                reader,
                handle,
                estimatedLength,
                FormatDefinitionTypeName,
                correspondenceLength,
                _includeCorrespondence
                    ? (currentReader, currentHandle) =>
                        ReadDefinitionCorrespondenceIdentity(
                            currentReader,
                            currentHandle,
                            rawTypeKind)
                    : null);
            _definitionCache.Add(cacheKey, encoded);
            return encoded;
        }

        public AnchorSignatureType GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            _referenceCache ??= [];
            var cacheKey = (handle, rawTypeKind);
            if (_referenceCache.TryGetValue(
                    cacheKey,
                    out AnchorSignatureType? cached))
            {
                ChargeLeaf(cached.Length);
                return cached;
            }

            int estimatedLength = EstimateReferenceNameLength(reader, handle);
            ChargeLeaf(estimatedLength);
            int correspondenceLength =
                _includeCorrespondence
                    ? EstimateReferenceCorrespondenceLength(
                        reader,
                        handle)
                    : estimatedLength;
            AnchorSignatureType encoded = new LazyTypeReferenceAnchorSignatureType(
                reader,
                handle,
                estimatedLength,
                FormatReferenceTypeName,
                correspondenceLength,
                _includeCorrespondence
                    ? (currentReader, currentHandle) =>
                        ReadReferenceCorrespondenceIdentity(
                            currentReader,
                            currentHandle,
                            rawTypeKind)
                    : null);
            _referenceCache.Add(cacheKey, encoded);
            return encoded;
        }

        public AnchorSignatureType GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            {
                if (_includeCorrespondence)
                {
                    throw new BadImageFormatException(
                        "The signature contains an invalid or over-budget type specification.");
                }
                return Encoded("System.Object");
            }
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
        {
            if (_includeCorrespondence
                && (shape.Rank <= 0
                    || shape.Sizes.Length > shape.Rank
                    || shape.LowerBounds.Length > shape.Rank))
            {
                throw new BadImageFormatException(
                    "The signature contains an invalid array shape.");
            }
            return Composite(
                new ArrayAnchorSignatureType(
                    elementType,
                    shape.Rank,
                    shape.Sizes,
                    shape.LowerBounds));
        }

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
        {
            if (_includeCorrespondence)
            {
                NamedTypeCorrespondenceIdentity identity =
                    GetPreparedNamedTypeCorrespondenceIdentity(
                        genericType);
                if (identity.GenericArity != typeArguments.Length)
                {
                    throw new BadImageFormatException(
                        "The generic instantiation argument count does not match the named type's arity.");
                }
            }
            return Composite(
                new GenericAnchorSignatureType(genericType, typeArguments));
        }

        public AnchorSignatureType GetGenericTypeParameter(
            GenericContext? context,
            int index)
        {
            if (_includeCorrespondence
                && (context is null
                    || index < 0
                    || index >= context.TypeParameters.Count))
            {
                throw new BadImageFormatException(
                    "The signature references an out-of-range type generic parameter.");
            }
            string display =
                context is not null
                    && index >= 0
                    && index < context.TypeParameters.Count
                ? context.TypeParameters[index]
                : $"!{index}";
            return _includeCorrespondence
                ? Encoded(
                    display,
                    $"var[{index}]")
                : Encoded(display);
        }

        public AnchorSignatureType GetGenericMethodParameter(
            GenericContext? context,
            int index)
        {
            if (_includeCorrespondence
                && (context is null
                    || index < 0
                    || index >= context.MethodParameters.Count))
            {
                throw new BadImageFormatException(
                    "The signature references an out-of-range method generic parameter.");
            }
            string display =
                context is not null
                    && index >= 0
                    && index < context.MethodParameters.Count
                ? context.MethodParameters[index]
                : $"!!{index}";
            return _includeCorrespondence
                ? Encoded(
                    display,
                    $"mvar[{index}]")
                : Encoded(display);
        }

        public AnchorSignatureType GetFunctionPointerType(
            MethodSignature<AnchorSignatureType> signature)
            => Composite(
                new FunctionPointerAnchorSignatureType(signature));

        public AnchorSignatureType GetModifiedType(
            AnchorSignatureType modifier,
            AnchorSignatureType unmodifiedType,
            bool isRequired)
        {
            // The durable display anchor continues to erase custom modifiers.
            // API correspondence retains required modifiers everywhere and
            // optional modifiers inside function pointers, where C# encodes
            // extensible unmanaged calling conventions.
            _workBudget.Charge(1);
            return new ModifiedAnchorSignatureType(
                modifier,
                unmodifiedType,
                isRequired);
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

        int EstimateDefinitionCorrespondenceLength(
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

            int length = 64;
            var outer = reader.GetTypeDefinition(chain[0]);
            length = AddEncodedComponentEstimate(
                length,
                StructuralUtf8Length(reader, outer.Namespace));
            for (int i = 0; i < consumed; i++)
            {
                length = AddEncodedComponentEstimate(
                    length,
                    StructuralUtf8Length(
                        reader,
                        reader.GetTypeDefinition(chain[i]).Name));
            }
            return length;
        }

        int EstimateReferenceCorrespondenceLength(
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
                    out EntityHandle terminal,
                    out var rejection)
                || consumed == 0)
            {
                throw new BadImageFormatException(
                    rejection?.Detail
                        ?? "The type has an invalid resolution-scope chain.");
            }

            int length = 64;
            switch (terminal.Kind)
            {
                case HandleKind.AssemblyReference:
                {
                    var reference = reader.GetAssemblyReference(
                        (AssemblyReferenceHandle)terminal);
                    length = AddEncodedComponentEstimate(
                        length,
                        StructuralUtf8Length(reader, reference.Name));
                    length = AddEncodedComponentEstimate(
                        length,
                        StructuralUtf8Length(reader, reference.Culture));
                    length = AddEncodedComponentEstimate(
                        length,
                        reference.PublicKeyOrToken.IsNil ? 0 : 16);
                    break;
                }
                case HandleKind.ModuleReference:
                    length = AddEncodedComponentEstimate(
                        length,
                        StructuralUtf8Length(
                            reader,
                            reader.GetModuleReference(
                                (ModuleReferenceHandle)terminal).Name));
                    break;
                case HandleKind.ModuleDefinition:
                    ValidateCurrentModuleScope(terminal);
                    break;
                default:
                    if (!terminal.IsNil)
                    {
                        throw new BadImageFormatException(
                            "The type reference has an unsupported resolution scope.");
                    }
                    break;
            }

            var outer = reader.GetTypeReference(chain[0]);
            length = AddEncodedComponentEstimate(
                length,
                StructuralUtf8Length(reader, outer.Namespace));
            for (int i = 0; i < consumed; i++)
            {
                length = AddEncodedComponentEstimate(
                    length,
                    StructuralUtf8Length(
                        reader,
                        reader.GetTypeReference(chain[i]).Name));
            }
            return length;
        }

        static int AddEncodedComponentEstimate(
            int current,
            int utf8Length)
            => CheckedNameLength(
                current,
                CheckedNameLength(
                    DecimalDigitCount(utf8Length),
                    CheckedNameLength(1, utf8Length)));

        static int DecimalDigitCount(int value)
        {
            int digits = 1;
            while (value >= 10)
            {
                value /= 10;
                digits++;
            }
            return digits;
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

        NamedTypeCorrespondenceIdentity
            ReadDefinitionCorrespondenceIdentity(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            int genericArity =
                ValidateCorrespondenceTypeDefinitionGenericArity(
                    reader,
                    handle,
                    _workBudget);
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

            var segments =
                ImmutableArray.CreateBuilder<string>(consumed);
            for (int i = 0; i < consumed; i++)
            {
                segments.Add(
                    MetadataSafetyPolicy.ReadStructuralString(
                        reader,
                        reader.GetTypeDefinition(chain[i]).Name));
            }
            return new NamedTypeCorrespondenceIdentity(
                NamedTypeScopeKind.Current,
                Assembly: null,
                ModuleName: null,
                MetadataSafetyPolicy.ReadStructuralString(
                    reader,
                    reader.GetTypeDefinition(chain[0]).Namespace),
                segments.MoveToImmutable(),
                genericArity,
                rawTypeKind,
                AuthorizesCurrentToIntrinsicCoreLibrary: false);
        }

        NamedTypeCorrespondenceIdentity
            ReadReferenceCorrespondenceIdentity(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out int consumed,
                    out EntityHandle terminal,
                    out var rejection)
                || consumed == 0)
            {
                throw new BadImageFormatException(
                    rejection?.Detail
                        ?? "The type has an invalid resolution-scope chain.");
            }

            NamedTypeScopeKind scopeKind;
            AssemblyReferenceIdentity? assembly = null;
            string? moduleName = null;
            switch (terminal.Kind)
            {
                case HandleKind.AssemblyReference:
                    MethodCorrespondenceContext context =
                        _correspondenceContext
                        ?? throw new InvalidOperationException(
                            "Correspondence construction requires an operation context.");
                    assembly = context.ProjectAssemblyReference(
                        reader,
                        (AssemblyReferenceHandle)terminal,
                        _workBudget.Charge);
                    if (PlatformKeys.IsCoreLibraryFacadeReference(
                            assembly))
                    {
                        scopeKind =
                            NamedTypeScopeKind.IntrinsicCoreLibrary;
                        assembly = null;
                    }
                    else
                    {
                        scopeKind = NamedTypeScopeKind.Assembly;
                    }
                    break;
                case HandleKind.ModuleReference:
                    scopeKind = NamedTypeScopeKind.Module;
                    moduleName =
                        MetadataSafetyPolicy.ReadStructuralString(
                            reader,
                            reader.GetModuleReference(
                                (ModuleReferenceHandle)terminal).Name);
                    break;
                case HandleKind.ModuleDefinition:
                    ValidateCurrentModuleScope(terminal);
                    scopeKind = NamedTypeScopeKind.Current;
                    break;
                default:
                    if (!terminal.IsNil)
                    {
                        throw new BadImageFormatException(
                            "The type reference has an unsupported resolution scope.");
                    }
                    scopeKind = NamedTypeScopeKind.Current;
                    break;
            }

            var segments =
                ImmutableArray.CreateBuilder<string>(consumed);
            for (int i = 0; i < consumed; i++)
            {
                segments.Add(
                    MetadataSafetyPolicy.ReadStructuralString(
                        reader,
                        reader.GetTypeReference(chain[i]).Name));
            }
            ImmutableArray<string> segmentArray =
                segments.MoveToImmutable();
            string @namespace =
                MetadataSafetyPolicy.ReadStructuralString(
                    reader,
                    reader.GetTypeReference(chain[0]).Namespace);
            return new NamedTypeCorrespondenceIdentity(
                scopeKind,
                assembly,
                moduleName,
                @namespace,
                segmentArray,
                GetMetadataNameGenericArity(segmentArray),
                rawTypeKind,
                scopeKind
                    == NamedTypeScopeKind.IntrinsicCoreLibrary
                    && IsIntrinsicCoreLibraryForwardedRoot(
                        reader,
                        @namespace,
                        segmentArray[0]));
        }

        bool IsIntrinsicCoreLibraryForwardedRoot(
            MetadataReader reader,
            string @namespace,
            string name)
        {
            MethodCorrespondenceContext context =
                _correspondenceContext
                ?? throw new InvalidOperationException(
                    "Correspondence construction requires an operation context.");
            IntrinsicCoreLibraryForwardedRootProjection roots =
                context.GetOrAddIntrinsicCoreLibraryForwardedRoots(
                    reader,
                    ReadIntrinsicCoreLibraryForwardedRoots);
            return roots.Authorizes((@namespace, name));

            IntrinsicCoreLibraryForwardedRootProjection
                ReadIntrinsicCoreLibraryForwardedRoots()
            {
                var forwardedRoots =
                    new IntrinsicCoreLibraryForwardedRootProjection();
                foreach (ExportedTypeHandle handle in reader.ExportedTypes)
                {
                    _workBudget.Charge(LeafNodeWorkUnits);
                    ExportedType root;
                    try
                    {
                        root = reader.GetExportedType(handle);
                    }
                    catch (Exception ex) when (CanIgnoreMalformedRow(ex))
                    {
                        continue;
                    }

                    EntityHandle terminal;
                    try
                    {
                        terminal = root.Implementation;
                    }
                    catch (Exception ex) when (CanIgnoreMalformedRow(ex))
                    {
                        if (TryReadRootIdentity(
                                root,
                                out var malformedRootIdentity))
                        {
                            forwardedRoots.RecordMalformed(
                                malformedRootIdentity);
                        }
                        continue;
                    }
                    if (!IsValidDirectImplementation(
                            reader,
                            terminal))
                    {
                        if (TryReadRootIdentity(
                                root,
                                out var malformedRootIdentity))
                        {
                            forwardedRoots.RecordMalformed(
                                malformedRootIdentity);
                        }
                        continue;
                    }
                    if (terminal.Kind == HandleKind.ExportedType)
                        continue;
                    bool nestedVisibility =
                        HasNestedVisibility(root.Attributes);

                    if (!TryReadRootIdentity(
                            root,
                            out var rootIdentity))
                        continue;

                    bool authorized = false;
                    try
                    {
                        if (!nestedVisibility
                            && terminal.Kind
                                == HandleKind.AssemblyReference
                            && root.IsForwarder)
                        {
                            var targetHandle =
                                (AssemblyReferenceHandle)terminal;
                            AssemblyReferenceIdentity target =
                                context.ProjectAssemblyReference(
                                    reader,
                                    targetHandle,
                                    _workBudget.Charge);
                            authorized =
                                PlatformKeys
                                    .IsCoreLibraryFacadeReference(target);
                        }
                    }
                    catch (Exception ex) when (CanIgnoreMalformedRow(ex))
                    {
                        forwardedRoots.RecordMalformed(rootIdentity);
                        continue;
                    }
                    forwardedRoots.Record(rootIdentity, authorized);
                }
                return forwardedRoots;

                bool CanIgnoreMalformedRow(Exception ex) =>
                    _workBudget.Remaining > 0
                    && ex is BadImageFormatException
                        or ArgumentOutOfRangeException;

                static bool IsValidDirectImplementation(
                    MetadataReader reader,
                    EntityHandle implementation)
                {
                    int row = MetadataTokens.GetRowNumber(
                        implementation);
                    if (row <= 0)
                        return false;
                    return implementation.Kind switch
                    {
                        HandleKind.AssemblyReference =>
                            row <= reader.GetTableRowCount(
                                TableIndex.AssemblyRef),
                        HandleKind.AssemblyFile =>
                            row <= reader.GetTableRowCount(
                                TableIndex.File),
                        HandleKind.ExportedType =>
                            row <= reader.GetTableRowCount(
                                TableIndex.ExportedType),
                        _ => false,
                    };
                }

                bool TryReadRootIdentity(
                    ExportedType root,
                    out (string Namespace, string Name) rootIdentity)
                {
                    try
                    {
                        rootIdentity =
                            (
                                ReadStructuralString(
                                    reader,
                                    root.Namespace,
                                    _workBudget),
                                ReadStructuralString(
                                    reader,
                                    root.Name,
                                    _workBudget));
                        return true;
                    }
                    catch (Exception ex) when (CanIgnoreMalformedRow(ex))
                    {
                        rootIdentity = default;
                        return false;
                    }
                }

                static bool HasNestedVisibility(
                    TypeAttributes attributes) =>
                    (attributes & TypeAttributes.VisibilityMask) is
                        TypeAttributes.NestedPublic
                        or TypeAttributes.NestedPrivate
                        or TypeAttributes.NestedFamily
                        or TypeAttributes.NestedAssembly
                        or TypeAttributes.NestedFamANDAssem
                        or TypeAttributes.NestedFamORAssem;
            }
        }

        static int GetMetadataNameGenericArity(
            ImmutableArray<string> segments)
        {
            try
            {
                int arity = 0;
                foreach (string segment in segments)
                {
                    if (MetadataNameArity.TryReadSuffix(
                            segment,
                            out int segmentArity,
                            out _))
                    {
                        arity = checked(arity + segmentArity);
                    }
                }
                return arity;
            }
            catch (OverflowException ex)
            {
                throw new BadImageFormatException(
                    "The named type's generic arity exceeds the metadata limit.",
                    ex);
            }
        }

        static NamedTypeCorrespondenceIdentity
            GetPreparedNamedTypeCorrespondenceIdentity(
            AnchorSignatureType type)
        {
            switch (type)
            {
                case LazyTypeDefinitionAnchorSignatureType definition:
                    definition.PrepareCorrespondenceIdentity();
                    return definition.GetCorrespondenceIdentity();
                case LazyTypeReferenceAnchorSignatureType reference:
                    reference.PrepareCorrespondenceIdentity();
                    return reference.GetCorrespondenceIdentity();
                default:
                    throw new BadImageFormatException(
                        "A generic instantiation must reference a named type.");
            }
        }

        static void ValidateCurrentModuleScope(
            EntityHandle terminal)
        {
            if (MetadataTokens.GetRowNumber(terminal) != 1)
            {
                throw new BadImageFormatException(
                    "The type reference has an invalid current-module scope.");
            }
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

        AnchorSignatureType Encoded(
            string text,
            string? correspondence = null)
        {
            // Charge every occurrence, including the short-leaf floor. Gated by
            // CreateMethodAnchor_WideGenericModoptsFailBeforeLargeAllocation and
            // CreateMethodAnchor_WideTypeRefGenericModoptsFailBeforeLargeAllocation.
            ChargeLeaf(text.Length);
            return new EncodedAnchorSignatureType(
                text,
                correspondence);
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
        MethodAnchorShape shape =
            CreateMethodAnchorShape(
                reader,
                typeHandle,
                method,
                isExtensionMethod);
        return CreateMethodAnchorInfo(shape);
    }

    internal static MethodCorrespondenceAnchorInfo
        CreateMethodCorrespondenceAnchorInfo(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            MethodDefinition method,
            bool isExtensionMethod = false)
    {
        var correspondenceContext =
            new MethodCorrespondenceContext();
        return CreateMethodCorrespondenceAnchorInfo(
            CreateMethodAnchorShape(
                reader,
                typeHandle,
                method,
                isExtensionMethod,
                includeCorrespondence: true,
                correspondenceContext:
                    correspondenceContext));
    }

    /// <summary>
    /// Creates a method anchor while drawing from a caller-owned cumulative
    /// work remaining counter (classification scans). Each call is still capped
    /// by <see cref="MetadataSafetyPolicy.MaxAnchorSignatureWorkChars"/>, and
    /// spent units are subtracted from <paramref name="scanWorkRemaining"/>.
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

        var workBudget = new AnchorSignatureWorkBudget(anchorAllowance);
        try
        {
            MethodAnchorShape shape = CreateMethodAnchorShape(
                reader,
                typeHandle,
                method,
                isExtensionMethod,
                workBudget);
            int spent = anchorAllowance - workBudget.Remaining;
            scanWorkRemaining -= spent;
            if (scanWorkRemaining < 0)
                scanWorkRemaining = 0;
            return CreateMethodAnchorInfo(shape);
        }
        catch (BadImageFormatException)
        {
            // Budget may be exhausted mid-decode; do not allow retry with the
            // pre-call remaining on a later method.
            scanWorkRemaining = workBudget.Remaining;
            throw;
        }
    }

    internal static MethodAnchorDeclaringTypeContext
        CreateMethodAnchorDeclaringTypeContext(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            ref int scanWorkRemaining)
    {
        if (scanWorkRemaining <= 0)
        {
            throw new BadImageFormatException(
                "The assembly exceeds the classification scan work budget.");
        }

        int anchorAllowance = Math.Min(
            scanWorkRemaining,
            MetadataSafetyPolicy.MaxAnchorSignatureWorkChars);
        var workBudget =
            new AnchorSignatureWorkBudget(anchorAllowance);
        try
        {
            TypeDefinition type =
                reader.GetTypeDefinition(typeHandle);
            GenericContext genericContext =
                GenericContext.ForType(
                    reader,
                    type,
                    workBudget.Charge);
            string fullName =
                FormatDefinitionName(
                    reader,
                    typeHandle,
                    workBudget.Charge);
            workBudget.Charge(fullName.Length);
            UpdateScanWorkRemaining(
                ref scanWorkRemaining,
                anchorAllowance,
                workBudget);
            return new MethodAnchorDeclaringTypeContext(
                typeHandle,
                fullName,
                genericContext);
        }
        catch (BadImageFormatException)
        {
            scanWorkRemaining = workBudget.Remaining;
            throw;
        }
    }

    internal static MethodCorrespondenceAnchorInfo
        CreateMethodCorrespondenceAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        MethodAnchorDeclaringTypeContext declaringType,
        string metadataName,
        ref int scanWorkRemaining,
        bool isExtensionMethod = false,
        MethodCorrespondenceContext? correspondenceContext = null)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(metadataName);
        if (declaringType.Handle != typeHandle)
        {
            throw new ArgumentException(
                "The declaring-type context belongs to another type.",
                nameof(declaringType));
        }
        if (scanWorkRemaining <= 0)
        {
            throw new BadImageFormatException(
                "The assembly exceeds the classification scan work budget.");
        }

        int anchorAllowance = Math.Min(
            scanWorkRemaining,
            MetadataSafetyPolicy.MaxAnchorSignatureWorkChars);
        var workBudget =
            new AnchorSignatureWorkBudget(anchorAllowance);
        correspondenceContext ??=
            new MethodCorrespondenceContext();
        try
        {
            MethodAnchorShape shape =
                CreateMethodAnchorShape(
                    reader,
                    typeHandle,
                    method,
                    isExtensionMethod,
                    workBudget,
                    declaringType,
                    metadataName,
                    includeCorrespondence: true,
                    correspondenceContext:
                        correspondenceContext);
            UpdateScanWorkRemaining(
                ref scanWorkRemaining,
                anchorAllowance,
                workBudget);
            return CreateMethodCorrespondenceAnchorInfo(
                shape);
        }
        catch (BadImageFormatException)
        {
            scanWorkRemaining = workBudget.Remaining;
            throw;
        }
    }

    static void UpdateScanWorkRemaining(
        ref int scanWorkRemaining,
        int anchorAllowance,
        AnchorSignatureWorkBudget workBudget)
    {
        int spent =
            anchorAllowance - workBudget.Remaining;
        scanWorkRemaining -= spent;
        if (scanWorkRemaining < 0)
            scanWorkRemaining = 0;
    }

    static MethodAnchorInfo CreateMethodAnchorInfo(
        MethodAnchorShape shape)
        => new(shape.Anchor, shape.ReturnType);

    static MethodCorrespondenceAnchorInfo
        CreateMethodCorrespondenceAnchorInfo(
            MethodAnchorShape shape)
    {
        if (!shape.HasCorrespondenceProjection)
        {
            throw new InvalidOperationException(
                "The method anchor shape has no correspondence projection.");
        }
        return new(
            CreateMethodAnchorInfo(shape),
            shape.IsExtensionMethod,
            shape.SignatureHeader,
            shape.GenericParameterCount,
            shape.RequiredParameterCount,
            shape.ParameterTypes.Length,
            shape.CorrespondenceReturnType!,
            shape.CorrespondenceParameterTypes);
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

    readonly record struct MethodAnchorShape(
        MemberAnchor Anchor,
        string ReturnType,
        ImmutableArray<string> ParameterTypes,
        MethodTypeCorrespondence? CorrespondenceReturnType,
        ImmutableArray<MethodTypeCorrespondence>
            CorrespondenceParameterTypes,
        bool HasCorrespondenceProjection,
        bool IsExtensionMethod,
        byte SignatureHeader,
        int GenericParameterCount,
        int RequiredParameterCount);

    static MethodAnchorShape CreateMethodAnchorShape(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        bool isExtensionMethod,
        AnchorSignatureWorkBudget? workBudget = null,
        MethodAnchorDeclaringTypeContext? declaringType = null,
        string? knownMetadataName = null,
        bool includeCorrespondence = false,
        MethodCorrespondenceContext? correspondenceContext = null)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        workBudget ??= new AnchorSignatureWorkBudget();
        string methodName =
            knownMetadataName
            ?? ReadStructuralString(
                reader,
                method.Name,
                workBudget);
        GenericContext typeContext =
            declaringType?.GenericContext
            ?? GenericContext.ForType(
                reader,
                type,
                workBudget.Charge);
        GenericContext context =
            GenericContext.ForMethod(
                reader,
                typeContext,
                method,
                workBudget.Charge);
        if (includeCorrespondence)
        {
            ValidateCorrespondenceTypeDefinitionGenericArity(
                reader,
                typeHandle,
                workBudget);
        }
        var provider = new AnchorSignatureTypeProvider(
            workBudget,
            includeCorrespondence,
            correspondenceContext);
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
        if (includeCorrespondence)
        {
            EnsureCorrespondenceComparisonBudget(
                signature.ReturnType,
                signature.ParameterTypes);
        }
        string typeFullName =
            declaringType?.FullName
            ?? FormatDefinitionName(
                reader,
                typeHandle,
                workBudget.Charge);
        string selectorName =
            GetMemberSelectorName(
                methodName,
                isExtensionMethod);
        ChargeMethodIdentityWork(
            workBudget,
            typeFullName,
            methodName,
            context.MethodParameters,
            signature,
            selectorName,
            includeCorrespondence);
        string memberName = MethodMemberName(
            methodName,
            context.MethodParameters);
        string returnType = signature.ReturnType.Render();
        ImmutableArray<string> parameterTypes =
            Render(signature.ParameterTypes);
        MethodTypeCorrespondence? correspondenceReturnType =
            includeCorrespondence
                ? new MethodTypeCorrespondence(
                    signature.ReturnType)
                : null;
        ImmutableArray<MethodTypeCorrespondence>
            correspondenceParameterTypes =
            includeCorrespondence
                ? CreateCorrespondenceTypes(
                    signature.ParameterTypes)
                : [];
        // Route the SRM-direct producer through the single full-name grammar core so it
        // cannot drift from other producers. Conversion operators overload on return type,
        // so pass the return type for their disambiguation suffix only.
        string canonicalSignature = MemberCanonicalSignature.Build(
            "M",
            typeFullName,
            memberName,
            parameterTypes,
            IsConversionOperator(methodName) ? returnType : null);
        return new MethodAnchorShape(
            CreateAnchor(
                typeFullName,
                selectorName,
                memberName,
                canonicalSignature),
            returnType,
            parameterTypes,
            correspondenceReturnType,
            correspondenceParameterTypes,
            includeCorrespondence,
            isExtensionMethod,
            signature.Header.RawValue,
            signature.GenericParameterCount,
            signature.RequiredParameterCount);
    }

    static int ValidateCorrespondenceTypeDefinitionGenericArity(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        AnchorSignatureWorkBudget workBudget)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                typeHandle,
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

        int enclosingGenericCount = 0;
        for (int i = 0; i < consumed; i++)
        {
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
            string name = ReadStructuralString(
                reader,
                reader.GetTypeDefinition(chain[i]).Name,
                workBudget);
            bool hasDeclaredArity = MetadataNameArity.TryReadSuffix(
                name,
                out int declaredArity,
                out _);
            if ((hasDeclaredArity
                    && declaredArity != introducedGenericCount)
                || (!hasDeclaredArity
                    && introducedGenericCount != 0))
            {
                throw new BadImageFormatException(
                    "The type's metadata-name arity does not match its generic parameters.");
            }
            enclosingGenericCount = cumulativeGenericCount;
        }
        return enclosingGenericCount;
    }

    static string ReadStructuralString(
        MetadataReader reader,
        StringHandle handle,
        AnchorSignatureWorkBudget workBudget)
    {
        workBudget.Charge(
            reader.GetBlobReader(handle).Length);
        return MetadataSafetyPolicy.ReadStructuralString(
            reader,
            handle);
    }

    static ImmutableArray<string> Render(
        ImmutableArray<AnchorSignatureType> types)
    {
        var builder = ImmutableArray.CreateBuilder<string>(types.Length);
        foreach (AnchorSignatureType type in types)
            builder.Add(type.Render());
        return builder.MoveToImmutable();
    }

    static ImmutableArray<MethodTypeCorrespondence>
        CreateCorrespondenceTypes(
        ImmutableArray<AnchorSignatureType> types)
    {
        var builder =
            ImmutableArray.CreateBuilder<MethodTypeCorrespondence>(
                types.Length);
        foreach (AnchorSignatureType type in types)
            builder.Add(new MethodTypeCorrespondence(type));
        return builder.MoveToImmutable();
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

    static void EnsureCorrespondenceComparisonBudget(
        AnchorSignatureType returnType,
        ImmutableArray<AnchorSignatureType> parameterTypes)
    {
        int remaining =
            MetadataSafetyPolicy.MaxStructuralSignatureChars
            - returnType.CorrespondenceLength;
        if (remaining < 0)
            throw AnchorSignatureBudgetExceeded();
        foreach (AnchorSignatureType parameter in parameterTypes)
        {
            if (parameter.CorrespondenceLength > remaining)
                throw AnchorSignatureBudgetExceeded();
            remaining -= parameter.CorrespondenceLength;
        }
    }

    static void ChargeMethodIdentityWork(
        AnchorSignatureWorkBudget workBudget,
        string typeFullName,
        string methodName,
        IReadOnlyList<string> genericNames,
        MethodSignature<AnchorSignatureType> signature,
        string selectorName,
        bool includeCorrespondence)
    {
        int memberNameLength =
            GetMethodMemberNameLength(
                methodName,
                genericNames);
        int parameterLength = 0;
        foreach (AnchorSignatureType parameter
            in signature.ParameterTypes)
        {
            parameterLength = CheckedIdentityLength(
                parameterLength,
                parameter.Length);
        }
        if (signature.ParameterTypes.Length > 1)
        {
            parameterLength = CheckedIdentityLength(
                parameterLength,
                signature.ParameterTypes.Length - 1);
        }

        int canonicalLength = CheckedIdentityLength(
            2,
            typeFullName.Length,
            1,
            memberNameLength,
            2,
            parameterLength);
        if (IsConversionOperator(methodName))
        {
            canonicalLength = CheckedIdentityLength(
                canonicalLength,
                1,
                signature.ReturnType.Length);
        }

        workBudget.Charge(memberNameLength);
        workBudget.Charge(
            CheckedIdentityLength(
                signature.ReturnType.Length,
                parameterLength));
        workBudget.Charge(canonicalLength);
        workBudget.Charge(
            CheckedIdentityLength(
                MemberAnchor.FingerprintPrefix.Length,
                canonicalLength));
        workBudget.Charge(
            CheckedIdentityLength(
                MemberAnchor.FingerprintPrefix.Length,
                canonicalLength));
        workBudget.Charge(
            CheckedIdentityLength(
                selectorName.Length,
                11));

        if (includeCorrespondence)
        {
            int correspondenceLength =
                signature.ReturnType.HasCorrespondenceDetails
                    ? signature.ReturnType.CorrespondenceLength
                    : 0;
            foreach (AnchorSignatureType parameter
                in signature.ParameterTypes)
            {
                if (parameter.HasCorrespondenceDetails)
                {
                    correspondenceLength =
                        CheckedIdentityLength(
                            correspondenceLength,
                            parameter.CorrespondenceLength);
                }
            }
            workBudget.Charge(correspondenceLength);
        }
    }

    static int GetMethodMemberNameLength(
        string methodName,
        IReadOnlyList<string> genericNames)
    {
        if (methodName == ".ctor")
            return 5;
        if (genericNames.Count == 0)
            return methodName.Length;

        int length = CheckedIdentityLength(
            methodName.Length,
            2);
        if (genericNames.Count > 1)
        {
            length = CheckedIdentityLength(
                length,
                genericNames.Count - 1);
        }
        foreach (string name in genericNames)
        {
            length = CheckedIdentityLength(
                length,
                name.Length);
        }
        return length;
    }

    static int CheckedIdentityLength(
        params int[] parts)
    {
        try
        {
            int length = 0;
            foreach (int part in parts)
                length = checked(length + part);
            if (length
                > MetadataSafetyPolicy.MaxStructuralSignatureChars)
            {
                throw AnchorSignatureBudgetExceeded();
            }
            return length;
        }
        catch (OverflowException ex)
        {
            throw new BadImageFormatException(
                "The member anchor signature exceeds the encoded-character budget.",
                ex);
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
        string canonicalSignature)
    {
        var fingerprint = MemberAnchor.ComputeFingerprint(canonicalSignature);
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
        Action<int>? beforeMaterialize = null)
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
        beforeMaterialize?.Invoke(
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
                AppendAnchorName(builder, '+');

            if (remainingTypeNameCharacters == 0)
                throw TypeNameBudgetExceeded();
            remainingTypeNameCharacters--;

            var type = reader.GetTypeDefinition(chain[i]);
            beforeMaterialize?.Invoke(
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
            AppendEscapedAnchorName(
                builder,
                name,
                simpleNameLength,
                escapeDot: true);
            if (!hasDeclaredArity && introducedGenericCount > 0)
                AppendAnchorName(builder, ":0");

            if (introducedGenericCount == 0)
            {
                enclosingGenericCount = cumulativeGenericCount;
                continue;
            }

            AppendAnchorName(builder, "<");
            int index = 0;
            foreach (GenericParameterHandle parameter in
                genericParameters.Skip(enclosingGenericCount))
            {
                if (index++ > 0)
                    AppendAnchorName(builder, ",");
                StringHandle parameterNameHandle =
                    reader.GetGenericParameter(parameter).Name;
                beforeMaterialize?.Invoke(
                    reader.GetBlobReader(
                        parameterNameHandle).Length);
                string parameterName =
                    MetadataSafetyPolicy.ReadStructuralString(
                        reader,
                        parameterNameHandle);
                AppendEscapedAnchorName(
                    builder,
                    parameterName,
                    parameterName.Length,
                    escapeDot: true);
            }
            AppendAnchorName(builder, ">");
            enclosingGenericCount = cumulativeGenericCount;
        }

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
            if (char.IsLetterOrDigit(c)
                || c is '_' or '`'
                || c == '.' && !escapeDot)
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
        IReadOnlyList<string> genericNames)
    {
        if (methodName == ".ctor")
            return "#ctor";
        if (genericNames.Count == 0)
            return methodName;

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
        => memberName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";
}
