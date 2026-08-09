using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Builds a strict cross-module correspondence key for a metadata method. The
/// key is stronger than ECMA method lookup identity: it carries the declaring
/// chain's and method's generic-parameter constraints in addition to the full
/// ECMA signature, but does not fingerprint definition attributes or method
/// implementation.
/// </summary>
public static class MethodStructuralSignature
{
    /// <summary>
    /// Builds the key from the definition's metadata names.
    /// </summary>
    public static string Build(
        MetadataReader reader,
        MethodDefinition method)
        => new StructuralSignatureBuilder(reader).BuildMethod(method);

    /// <summary>
    /// Builds the key with name substitutions for correspondences whose source
    /// language gives generated definitions unstable names.
    /// </summary>
    public static string Build(
        MetadataReader reader,
        MethodDefinition method,
        string? methodName,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides)
        => new StructuralSignatureBuilder(reader, typeNameOverrides)
            .BuildMethod(method, methodName);
}

/// <summary>
/// Builds a strict cross-module definition key for a metadata type, including
/// every declaring segment and its generic-parameter constraints.
/// </summary>
public static class TypeStructuralSignature
{
    /// <summary>
    /// Builds the key. Optional substitutions replace only the names of the
    /// corresponding TypeDef handles; all other structural facts remain encoded.
    /// </summary>
    public static string Build(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides = null)
        => new StructuralSignatureBuilder(reader, typeNameOverrides)
            .BuildType(handle);

    internal static StructuralTypeKey BuildCore(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides,
        StructuralSignatureTypeProvider provider,
        StructuralSignatureWorkBudget workBudget,
        Dictionary<TypeDefinitionHandle, StructuralTypeKey> typeKeys,
        Dictionary<TypeDefinitionHandle, string> segmentKeys)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
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
                rejection?.Detail ?? "The type has an invalid declaring-type chain.");
        }

        var outer = reader.GetTypeDefinition(chain[0]);
        string @namespace = reader.GetString(outer.Namespace);
        StructuralTypeKey? parent = null;
        for (int i = 0; i < consumed; i++)
        {
            if (typeKeys.TryGetValue(chain[i], out var cached))
            {
                parent = cached;
                continue;
            }

            if (!segmentKeys.TryGetValue(chain[i], out string? segmentKey))
            {
                var segment = reader.GetTypeDefinition(chain[i]);
                string name = typeNameOverrides is not null
                    && typeNameOverrides.TryGetValue(chain[i], out var replacement)
                        ? replacement
                        : reader.GetString(segment.Name);
                var segmentBuilder = new StringBuilder();
                StructuralSignatureKey.AppendPart(segmentBuilder, name);
                StructuralSignatureKey.AppendGenericParameters(
                    segmentBuilder,
                    reader,
                    segment.GetGenericParameters(),
                    provider,
                    workBudget);
                workBudget.Charge(segmentBuilder.Length);
                segmentKey = segmentBuilder.ToString();
                segmentKeys.Add(chain[i], segmentKey);
            }

            parent = new StructuralTypeKey(@namespace, parent, segmentKey);
            typeKeys.Add(chain[i], parent);
        }

        return parent!;
    }
}

internal sealed class StructuralTypeKey : IEquatable<StructuralTypeKey>
{
    readonly int _hashCode;

    internal StructuralTypeKey(
        string @namespace,
        StructuralTypeKey? declaringType,
        string segment)
    {
        Namespace = @namespace;
        DeclaringType = declaringType;
        Segment = segment;
        Depth = declaringType is null ? 1 : declaringType.Depth + 1;
        SegmentTextLength =
            checked((declaringType?.SegmentTextLength ?? 0) + segment.Length);
        if (EncodedLength > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The structural type key exceeds the encoded-character budget.");
        }
        _hashCode = HashCode.Combine(
            declaringType?._hashCode
                ?? StringComparer.Ordinal.GetHashCode(@namespace),
            StringComparer.Ordinal.GetHashCode(@namespace),
            StringComparer.Ordinal.GetHashCode(segment));
    }

    internal string Namespace { get; }
    internal StructuralTypeKey? DeclaringType { get; }
    internal string Segment { get; }
    internal int Depth { get; }
    internal int SegmentTextLength { get; }

    internal int EncodedLength =>
        checked(
            1
            + StructuralSignatureKey.PartLength(Namespace)
            + StructuralSignatureKey.NumberLength(Depth)
            + SegmentTextLength);

    public bool Equals(StructuralTypeKey? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || _hashCode != other._hashCode
            || Depth != other.Depth
            || !StringComparer.Ordinal.Equals(Namespace, other.Namespace))
        {
            return false;
        }

        StructuralTypeKey? left = this;
        StructuralTypeKey? right = other;
        while (left is not null && right is not null)
        {
            if (!StringComparer.Ordinal.Equals(left.Segment, right.Segment))
                return false;
            left = left.DeclaringType;
            right = right.DeclaringType;
        }
        return left is null && right is null;
    }

    public override bool Equals(object? obj)
        => obj is StructuralTypeKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        var builder = new StringBuilder(EncodedLength);
        AppendEncoded(builder);
        return builder.ToString();
    }

    internal void AppendEncoded(StringBuilder builder)
    {
        builder.Append('T');
        StructuralSignatureKey.AppendPart(builder, Namespace);
        StructuralSignatureKey.AppendNumber(builder, Depth);
        AppendSegments(builder);
    }

    void AppendSegments(StringBuilder builder)
    {
        DeclaringType?.AppendSegments(builder);
        builder.Append(Segment);
    }
}

internal sealed class StructuralMethodKey : IEquatable<StructuralMethodKey>
{
    readonly int _hashCode;

    internal StructuralMethodKey(
        StructuralTypeKey declaringType,
        string methodName,
        string genericParameters,
        StructuralEncodedSignature signature)
    {
        DeclaringType = declaringType;
        Component = new StructuralMethodComponent(
            methodName,
            genericParameters,
            signature);
        if (EncodedLength > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The structural method key exceeds the encoded-character budget.");
        }
        _hashCode = HashCode.Combine(
            declaringType.GetHashCode(),
            Component.GetHashCode());
    }

    internal StructuralTypeKey DeclaringType { get; }
    internal StructuralMethodComponent Component { get; }

    int EncodedLength =>
        checked(
            1
            + StructuralSignatureKey.NumberLength(DeclaringType.EncodedLength)
            + DeclaringType.EncodedLength
            + Component.EncodedLength);

    public bool Equals(StructuralMethodKey? other)
        => ReferenceEquals(this, other)
            || other is not null
                && _hashCode == other._hashCode
                && DeclaringType.Equals(other.DeclaringType)
                && Component.Equals(other.Component);

    public override bool Equals(object? obj)
        => obj is StructuralMethodKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        int typeLength = DeclaringType.EncodedLength;
        var builder = new StringBuilder(EncodedLength);
        builder.Append('M');
        StructuralSignatureKey.AppendNumber(builder, typeLength);
        DeclaringType.AppendEncoded(builder);
        Component.AppendEncoded(builder);
        return builder.ToString();
    }
}

internal sealed class StructuralMethodComponent
    : IEquatable<StructuralMethodComponent>
{
    readonly int _hashCode;

    internal StructuralMethodComponent(
        string methodName,
        string genericParameters,
        StructuralEncodedSignature signature)
    {
        LocalKey = new StructuralMethodLocalKey(
            methodName,
            genericParameters);
        Signature = signature;
        _hashCode = HashCode.Combine(
            LocalKey.GetHashCode(),
            signature.GetHashCode());
    }

    internal StructuralMethodLocalKey LocalKey { get; }
    internal StructuralEncodedSignature Signature { get; }

    internal int EncodedLength =>
        checked(
            LocalKey.EncodedLength
            + Signature.Text.Length);

    public bool Equals(StructuralMethodComponent? other)
        => ReferenceEquals(this, other)
            || other is not null
                && _hashCode == other._hashCode
                && LocalKey.Equals(other.LocalKey)
                && Signature.Equals(other.Signature);

    public override bool Equals(object? obj)
        => obj is StructuralMethodComponent other && Equals(other);

    public override int GetHashCode() => _hashCode;

    internal void AppendEncoded(StringBuilder builder)
    {
        LocalKey.AppendEncoded(builder);
        builder.Append(Signature.Text);
    }
}

internal sealed class StructuralMethodLocalKey
    : IEquatable<StructuralMethodLocalKey>
{
    readonly int _hashCode;

    internal StructuralMethodLocalKey(
        string methodName,
        string genericParameters)
    {
        MethodName = methodName;
        GenericParameters = genericParameters;
        _hashCode = HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(methodName),
            StringComparer.Ordinal.GetHashCode(genericParameters));
    }

    internal string MethodName { get; }
    internal string GenericParameters { get; }

    internal int EncodedLength =>
        checked(
            StructuralSignatureKey.PartLength(MethodName)
            + GenericParameters.Length);

    public bool Equals(StructuralMethodLocalKey? other)
        => ReferenceEquals(this, other)
            || other is not null
                && _hashCode == other._hashCode
                && StringComparer.Ordinal.Equals(
                    MethodName,
                    other.MethodName)
                && StringComparer.Ordinal.Equals(
                    GenericParameters,
                    other.GenericParameters);

    public override bool Equals(object? obj)
        => obj is StructuralMethodLocalKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    internal void AppendEncoded(StringBuilder builder)
    {
        StructuralSignatureKey.AppendPart(builder, MethodName);
        builder.Append(GenericParameters);
    }
}

internal sealed class StructuralEncodedSignature
    : IEquatable<StructuralEncodedSignature>
{
    readonly int _hashCode;

    internal StructuralEncodedSignature(string text)
    {
        Text = text;
        _hashCode = StringComparer.Ordinal.GetHashCode(text);
    }

    internal string Text { get; }

    public bool Equals(StructuralEncodedSignature? other)
        => ReferenceEquals(this, other)
            || other is not null
                && _hashCode == other._hashCode
                && StringComparer.Ordinal.Equals(Text, other.Text);

    public override bool Equals(object? obj)
        => obj is StructuralEncodedSignature other && Equals(other);

    public override int GetHashCode() => _hashCode;
}

sealed class StructuralSignatureWorkBudget
{
    int _remaining = MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;
    bool _exhausted;

    internal void EnsureAvailable()
    {
        if (!_exhausted && _remaining > 0)
            return;

        _exhausted = true;
        throw new BadImageFormatException(
            "The structural signature exceeds the cumulative work budget.");
    }

    internal void Charge(int characters)
    {
        if (_exhausted || characters < 0 || characters > _remaining)
        {
            _exhausted = true;
            throw new BadImageFormatException(
                "The structural signature exceeds the cumulative work budget.");
        }
        _remaining -= characters;
    }
}

/// <summary>
/// Builds structural keys for one metadata module and one stable type-name
/// substitution policy.
/// </summary>
public sealed class StructuralSignatureBuilder
{
    readonly MetadataReader _reader;
    readonly IReadOnlyDictionary<TypeDefinitionHandle, string>? _typeNameOverrides;
    readonly StructuralSignatureWorkBudget _workBudget;
    readonly StructuralSignatureTypeProvider _provider;
    readonly Dictionary<TypeDefinitionHandle, StructuralTypeKey> _typeKeys = [];
    readonly Dictionary<TypeDefinitionHandle, string> _typeSegments = [];
    readonly Dictionary<BlobHandle, StructuralEncodedSignature> _methodSignatures = [];

    /// <summary>
    /// Creates a reusable builder. The override map must remain unchanged for
    /// the builder's lifetime.
    /// </summary>
    public StructuralSignatureBuilder(
        MetadataReader reader,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides = null)
        : this(
            reader,
            typeNameOverrides,
            new StructuralSignatureWorkBudget())
    {
    }

    internal StructuralSignatureBuilder(
        MetadataReader reader,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides,
        StructuralSignatureWorkBudget workBudget)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workBudget);
        _reader = reader;
        _typeNameOverrides = typeNameOverrides;
        _workBudget = workBudget;
        _provider = new StructuralSignatureTypeProvider(workBudget);
    }

    /// <summary>Builds a method key, optionally substituting its name.</summary>
    public string BuildMethod(
        MethodDefinition method,
        string? methodName = null)
        => BuildMethodKey(method, methodName).ToString();

    internal StructuralMethodKey BuildMethodKey(
        MethodDefinition method,
        string? methodName = null)
        => StructuralSignatureKey.Build(_reader, () =>
        {
            _workBudget.EnsureAvailable();
            string resolvedMethodName =
                methodName ?? _reader.GetString(method.Name);
            _workBudget.Charge(resolvedMethodName.Length);
            string genericParameters =
                StructuralSignatureKey.EncodeGenericParameters(
                _reader,
                method.GetGenericParameters(),
                _provider,
                _workBudget);
            return new StructuralMethodKey(
                BuildTypeCore(method.GetDeclaringType()),
                resolvedMethodName,
                genericParameters,
                BuildMethodSignature(method));
        });

    /// <summary>Builds a type key.</summary>
    public string BuildType(TypeDefinitionHandle handle)
        => BuildTypeKey(handle).ToString();

    internal StructuralTypeKey BuildTypeKey(TypeDefinitionHandle handle)
        => StructuralSignatureKey.Build(
            _reader,
            () =>
            {
                _workBudget.EnsureAvailable();
                return BuildTypeCore(handle);
            });

    StructuralTypeKey BuildTypeCore(TypeDefinitionHandle handle)
    {
        if (_typeKeys.TryGetValue(handle, out var key))
            return key;

        // A declaring segment may carry many constraint rows. Immutable parent
        // nodes let production keys share that segment instead of copying the
        // declaring chain into every method or nested type. Gated by
        // BuildMethodKey_SharesDeclaringTypeKeyAcrossMethods and
        // BuildTypeKey_SharesAncestorAcrossNestedTypes.
        key = TypeStructuralSignature.BuildCore(
            _reader,
            handle,
            _typeNameOverrides,
            _provider,
            _workBudget,
            _typeKeys,
            _typeSegments);
        return key;
    }

    StructuralEncodedSignature BuildMethodSignature(MethodDefinition method)
    {
        if (_methodSignatures.TryGetValue(method.Signature, out var signatureKey))
            return signatureKey;

        _workBudget.EnsureAvailable();
        if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            throw new BadImageFormatException(
                "The method signature exceeds the structural safety limit.");
        }

        MethodSignature<StructuralSignatureType> signature =
            method.DecodeSignature(_provider, null);
        int encodedLength =
            StructuralSignatureKey.MethodSignatureLength(signature);
        if (encodedLength > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The method signature exceeds the encoded-character budget.");
        }
        _workBudget.Charge(encodedLength);
        var builder = new StringBuilder(encodedLength);
        StructuralSignatureKey.AppendMethodSignature(builder, signature);
        signatureKey = new StructuralEncodedSignature(builder.ToString());
        _methodSignatures.Add(method.Signature, signatureKey);
        return signatureKey;
    }
}

static class StructuralSignatureKey
{
    internal static T Build<T>(MetadataReader reader, Func<T> build)
    {
        try
        {
            EnsureCollectionRangesFit(reader);
            return build();
        }
        catch (BadImageFormatException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            throw new BadImageFormatException(
                "The structural signature could not be read from malformed metadata.",
                ex);
        }
    }

    static void EnsureCollectionRangesFit(MetadataReader reader)
    {
        if (reader.GetTableRowCount(TableIndex.GenericParam) > ushort.MaxValue
            || reader.GetTableRowCount(TableIndex.GenericParamConstraint) > ushort.MaxValue)
        {
            throw new BadImageFormatException(
                "Generic parameter or constraint tables exceed the lossless "
                + "System.Reflection.Metadata collection range.");
        }
    }

    internal static void AppendMethodSignature(
        StringBuilder builder,
        MethodSignature<StructuralSignatureType> signature)
    {
        builder.Append('S');
        AppendNumber(builder, signature.Header.RawValue);
        AppendNumber(builder, signature.GenericParameterCount);
        AppendNumber(builder, signature.RequiredParameterCount);
        AppendNumber(builder, signature.ParameterTypes.Length);
        AppendPart(builder, signature.ReturnType);
        foreach (StructuralSignatureType parameter in signature.ParameterTypes)
            AppendPart(builder, parameter);
    }

    internal static int MethodSignatureLength(
        MethodSignature<StructuralSignatureType> signature)
    {
        int length = checked(
            1
            + NumberLength(signature.Header.RawValue)
            + NumberLength(signature.GenericParameterCount)
            + NumberLength(signature.RequiredParameterCount)
            + NumberLength(signature.ParameterTypes.Length)
            + PartLength(signature.ReturnType.EncodedLength));
        foreach (StructuralSignatureType parameter in signature.ParameterTypes)
            length = checked(length + PartLength(parameter.EncodedLength));
        return length;
    }

    internal static void AppendGenericParameters(
        StringBuilder builder,
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        StructuralSignatureTypeProvider provider,
        StructuralSignatureWorkBudget workBudget)
    {
        int count = handles.Count;
        AppendNumber(builder, count);
        if (count == 0)
            return;

        var parameters = new GenericParameterHandle[count];
        foreach (var handle in handles)
        {
            var parameter = reader.GetGenericParameter(handle);
            int index = parameter.Index;
            if ((uint)index >= (uint)count || !parameters[index].IsNil)
            {
                throw new BadImageFormatException(
                    "Generic parameter positions must be unique and contiguous.");
            }
            parameters[index] = handle;
        }

        for (int index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].IsNil)
            {
                throw new BadImageFormatException(
                    "Generic parameter positions must be unique and contiguous.");
            }

            var parameter = reader.GetGenericParameter(parameters[index]);
            AppendNumber(builder, index);
            AppendNumber(builder, (int)parameter.Attributes);

            List<string> constraints = [];
            int constraintPartsLength = 0;
            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                int nextCount = constraints.Count + 1;
                int available = checked(
                    MetadataSafetyPolicy.MaxStructuralSignatureChars
                    - builder.Length
                    - NumberLength(nextCount)
                    - constraintPartsLength);
                string constraintType = provider.GetConstraintType(
                    reader,
                    constraint.Type,
                    available);
                constraintPartsLength = checked(
                    constraintPartsLength + PartLength(constraintType));
                constraints.Add(constraintType);
            }
            constraints.Sort(StringComparer.Ordinal);
            builder.EnsureCapacity(
                checked(
                    builder.Length
                    + NumberLength(constraints.Count)
                    + constraintPartsLength));
            AppendNumber(builder, constraints.Count);
            foreach (string constraint in constraints)
                AppendPart(builder, constraint);
        }
    }

    internal static string EncodeGenericParameters(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        StructuralSignatureTypeProvider provider,
        StructuralSignatureWorkBudget workBudget)
    {
        if (handles.Count == 0)
            return "0;";

        var builder = new StringBuilder();
        AppendGenericParameters(builder, reader, handles, provider, workBudget);
        workBudget.Charge(builder.Length);
        return builder.ToString();
    }

    internal static string ReferenceScope(
        MetadataReader reader,
        EntityHandle scope)
    {
        var builder = new StringBuilder();
        switch (scope.Kind)
        {
            case HandleKind.AssemblyReference:
                var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
                AppendAssembly(
                    builder,
                    reader.GetString(assembly.Name),
                    assembly.Version,
                    assembly.Culture.IsNil ? "" : reader.GetString(assembly.Culture),
                    assembly.PublicKeyOrToken.IsNil
                        ? []
                        : reader.GetBlobBytes(assembly.PublicKeyOrToken),
                    (int)assembly.Flags);
                break;
            case HandleKind.ModuleReference:
                builder.Append('r');
                AppendPart(
                    builder,
                    reader.GetString(
                        reader.GetModuleReference((ModuleReferenceHandle)scope).Name));
                break;
            case HandleKind.ModuleDefinition:
                builder.Append('m');
                AppendPart(builder, reader.GetString(reader.GetModuleDefinition().Name));
                break;
            default:
                if (!scope.IsNil)
                {
                    throw new BadImageFormatException(
                        $"Unsupported TypeRef resolution scope {scope.Kind}.");
                }
                builder.Append('n');
                break;
        }
        return builder.ToString();
    }

    internal static void AppendPart(StringBuilder builder, string value)
    {
        AppendNumber(builder, value.Length);
        EnsureCanAppend(builder, value.Length);
        builder.Append(value);
    }

    internal static void AppendPart(
        StringBuilder builder,
        StructuralSignatureType value)
    {
        AppendNumber(builder, value.EncodedLength);
        EnsureCanAppend(builder, value.EncodedLength);
        value.AppendTo(builder);
    }

    internal static void AppendNumber(StringBuilder builder, int value)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        EnsureCanAppend(builder, text.Length + 1);
        builder.Append(text).Append(';');
    }

    internal static int NumberLength(int value)
        => value.ToString(CultureInfo.InvariantCulture).Length + 1;

    internal static int PartLength(string value)
        => PartLength(value.Length);

    internal static int PartLength(int valueLength)
        => checked(NumberLength(valueLength) + valueLength);

    static void EnsureCanAppend(StringBuilder builder, int additionalLength)
    {
        if (additionalLength < 0
            || builder.Length
                > MetadataSafetyPolicy.MaxStructuralSignatureChars
                    - additionalLength)
        {
            throw new BadImageFormatException(
                "The structural signature exceeds the encoded-character budget.");
        }
    }

    static void AppendAssembly(
        StringBuilder builder,
        string name,
        Version version,
        string culture,
        byte[] publicKeyOrToken,
        int flags)
    {
        builder.Append('a');
        AppendPart(builder, name);
        AppendPart(builder, version.ToString());
        AppendPart(builder, culture);
        AppendPart(builder, Convert.ToHexString(publicKeyOrToken));
        AppendNumber(builder, flags);
    }
}

abstract class StructuralSignatureType
{
    protected StructuralSignatureType(int encodedLength)
    {
        if (encodedLength < 0
            || encodedLength > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The structural type exceeds the encoded-character budget.");
        }
        EncodedLength = encodedLength;
    }

    internal int EncodedLength { get; }
    internal abstract void AppendTo(StringBuilder builder);
}

sealed class EncodedStructuralSignatureType(string encoded)
    : StructuralSignatureType(encoded.Length)
{
    internal override void AppendTo(StringBuilder builder)
        => builder.Append(encoded);
}

sealed class PartPrefixStructuralSignatureType
    : StructuralSignatureType
{
    readonly char _prefix;
    readonly StructuralSignatureType _value;

    internal PartPrefixStructuralSignatureType(
        char prefix,
        StructuralSignatureType value)
        : base(checked(
            1 + StructuralSignatureKey.PartLength(value.EncodedLength)))
    {
        _prefix = prefix;
        _value = value;
    }

    internal override void AppendTo(StringBuilder builder)
    {
        builder.Append(_prefix);
        StructuralSignatureKey.AppendPart(builder, _value);
    }
}

sealed class TypeUseStructuralSignatureType
    : StructuralSignatureType
{
    readonly char _kind;
    readonly byte _rawTypeKind;
    readonly string _typeName;

    internal TypeUseStructuralSignatureType(
        char kind,
        byte rawTypeKind,
        string typeName)
        : base(checked(
            1
            + StructuralSignatureKey.NumberLength(rawTypeKind)
            + StructuralSignatureKey.PartLength(typeName)))
    {
        _kind = kind;
        _rawTypeKind = rawTypeKind;
        _typeName = typeName;
    }

    internal override void AppendTo(StringBuilder builder)
    {
        builder.Append(_kind);
        StructuralSignatureKey.AppendNumber(builder, _rawTypeKind);
        StructuralSignatureKey.AppendPart(builder, _typeName);
    }
}

sealed class SpecificationUseStructuralSignatureType
    : StructuralSignatureType
{
    readonly byte _rawTypeKind;
    readonly StructuralSignatureType _type;

    internal SpecificationUseStructuralSignatureType(
        byte rawTypeKind,
        StructuralSignatureType type)
        : base(checked(
            1
            + StructuralSignatureKey.NumberLength(rawTypeKind)
            + StructuralSignatureKey.PartLength(type.EncodedLength)))
    {
        _rawTypeKind = rawTypeKind;
        _type = type;
    }

    internal override void AppendTo(StringBuilder builder)
    {
        builder.Append('s');
        StructuralSignatureKey.AppendNumber(builder, _rawTypeKind);
        StructuralSignatureKey.AppendPart(builder, _type);
    }
}

sealed class ArrayStructuralSignatureType
    : StructuralSignatureType
{
    readonly StructuralSignatureType _elementType;
    readonly ArrayShape _shape;

    internal ArrayStructuralSignatureType(
        StructuralSignatureType elementType,
        ArrayShape shape)
        : base(GetLength(elementType, shape))
    {
        _elementType = elementType;
        _shape = shape;
    }

    static int GetLength(
        StructuralSignatureType elementType,
        ArrayShape shape)
    {
        int length = checked(
            1
            + StructuralSignatureKey.NumberLength(shape.Rank)
            + StructuralSignatureKey.NumberLength(shape.Sizes.Length));
        foreach (int size in shape.Sizes)
            length = checked(length + StructuralSignatureKey.NumberLength(size));
        length = checked(
            length
            + StructuralSignatureKey.NumberLength(shape.LowerBounds.Length));
        foreach (int lowerBound in shape.LowerBounds)
            length = checked(
                length + StructuralSignatureKey.NumberLength(lowerBound));
        return checked(
            length
            + StructuralSignatureKey.PartLength(elementType.EncodedLength));
    }

    internal override void AppendTo(StringBuilder builder)
    {
        builder.Append('a');
        StructuralSignatureKey.AppendNumber(builder, _shape.Rank);
        StructuralSignatureKey.AppendNumber(builder, _shape.Sizes.Length);
        foreach (int size in _shape.Sizes)
            StructuralSignatureKey.AppendNumber(builder, size);
        StructuralSignatureKey.AppendNumber(
            builder,
            _shape.LowerBounds.Length);
        foreach (int lowerBound in _shape.LowerBounds)
            StructuralSignatureKey.AppendNumber(builder, lowerBound);
        StructuralSignatureKey.AppendPart(builder, _elementType);
    }
}

sealed class GenericInstantiationStructuralSignatureType
    : StructuralSignatureType
{
    readonly StructuralSignatureType _genericType;
    readonly ImmutableArray<StructuralSignatureType> _typeArguments;

    internal GenericInstantiationStructuralSignatureType(
        StructuralSignatureType genericType,
        ImmutableArray<StructuralSignatureType> typeArguments)
        : base(GetLength(genericType, typeArguments))
    {
        _genericType = genericType;
        _typeArguments = typeArguments;
    }

    static int GetLength(
        StructuralSignatureType genericType,
        ImmutableArray<StructuralSignatureType> typeArguments)
    {
        int length = checked(
            1
            + StructuralSignatureKey.PartLength(genericType.EncodedLength)
            + StructuralSignatureKey.NumberLength(typeArguments.Length));
        foreach (StructuralSignatureType argument in typeArguments)
            length = checked(
                length
                + StructuralSignatureKey.PartLength(argument.EncodedLength));
        return length;
    }

    internal override void AppendTo(StringBuilder builder)
    {
        builder.Append('g');
        StructuralSignatureKey.AppendPart(builder, _genericType);
        StructuralSignatureKey.AppendNumber(builder, _typeArguments.Length);
        foreach (StructuralSignatureType argument in _typeArguments)
            StructuralSignatureKey.AppendPart(builder, argument);
    }
}

sealed class FunctionPointerStructuralSignatureType
    : StructuralSignatureType
{
    readonly MethodSignature<StructuralSignatureType> _signature;

    internal FunctionPointerStructuralSignatureType(
        MethodSignature<StructuralSignatureType> signature)
        : base(checked(
            1 + StructuralSignatureKey.MethodSignatureLength(signature)))
    {
        _signature = signature;
    }

    internal override void AppendTo(StringBuilder builder)
    {
        builder.Append('f');
        StructuralSignatureKey.AppendMethodSignature(builder, _signature);
    }
}

sealed class ModifiedStructuralSignatureType
    : StructuralSignatureType
{
    readonly StructuralSignatureType _modifier;
    readonly StructuralSignatureType _unmodifiedType;
    readonly bool _isRequired;

    internal ModifiedStructuralSignatureType(
        StructuralSignatureType modifier,
        StructuralSignatureType unmodifiedType,
        bool isRequired)
        : base(checked(
            1
            + StructuralSignatureKey.PartLength(modifier.EncodedLength)
            + StructuralSignatureKey.PartLength(unmodifiedType.EncodedLength)))
    {
        _modifier = modifier;
        _unmodifiedType = unmodifiedType;
        _isRequired = isRequired;
    }

    internal override void AppendTo(StringBuilder builder)
    {
        builder.Append(_isRequired ? 'c' : 'o');
        StructuralSignatureKey.AppendPart(builder, _modifier);
        StructuralSignatureKey.AppendPart(builder, _unmodifiedType);
    }
}

sealed class StructuralSignatureTypeProvider
    : ISignatureTypeProvider<StructuralSignatureType, object?>
{
    readonly StructuralSignatureWorkBudget _workBudget;
    readonly Dictionary<EntityHandle, string> _constraintTypes = [];
    readonly Dictionary<BlobHandle, string> _constraintTypeSpecifications = [];
    readonly Dictionary<BlobHandle, StructuralSignatureType> _typeSpecifications = [];

    internal StructuralSignatureTypeProvider(
        StructuralSignatureWorkBudget workBudget)
        => _workBudget = workBudget;

    public StructuralSignatureType GetPrimitiveType(
        PrimitiveTypeCode typeCode)
        => Encoded(
            "p"
            + ((int)typeCode).ToString(CultureInfo.InvariantCulture)
            + ";");

    public StructuralSignatureType GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
        => NamedTypeUse(
            reader,
            'd',
            rawTypeKind,
            StructuralTypeName.OfDefinition(
                reader,
                handle,
                typeNameOverrides: null));

    public StructuralSignatureType GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
        => NamedTypeUse(
            reader,
            'r',
            rawTypeKind,
            StructuralTypeName.OfReference(reader, handle));

    public StructuralSignatureType GetTypeFromSpecification(
        MetadataReader reader,
        object? context,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
        => new SpecificationUseStructuralSignatureType(
            rawTypeKind,
            DecodeTypeSpecification(reader, handle, context));

    internal string GetConstraintType(
        MetadataReader reader,
        EntityHandle handle,
        int availableEncodedChars)
    {
        if (availableEncodedChars <= 0)
        {
            throw new BadImageFormatException(
                "The structural signature exceeds the encoded-character budget.");
        }

        if (_constraintTypes.TryGetValue(handle, out string? cached))
        {
            EnsureConstraintFits(cached, availableEncodedChars);
            return cached;
        }

        BlobHandle typeSpecificationBlob = default;
        if (handle.Kind == HandleKind.TypeSpecification)
        {
            typeSpecificationBlob = reader.GetTypeSpecification(
                (TypeSpecificationHandle)handle).Signature;
            if (_constraintTypeSpecifications.TryGetValue(
                    typeSpecificationBlob,
                    out cached))
            {
                EnsureConstraintFits(cached, availableEncodedChars);
                _constraintTypes.Add(handle, cached);
                return cached;
            }
        }

        StructuralSignatureType type = handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                new PartPrefixStructuralSignatureType(
                    'd',
                    NamedType(
                        StructuralTypeName.OfDefinition(
                            reader,
                            (TypeDefinitionHandle)handle,
                            typeNameOverrides: null))),
            HandleKind.TypeReference =>
                new PartPrefixStructuralSignatureType(
                    'r',
                    NamedType(
                        StructuralTypeName.OfReference(
                            reader,
                            (TypeReferenceHandle)handle))),
            HandleKind.TypeSpecification =>
                new PartPrefixStructuralSignatureType(
                    's',
                    DecodeTypeSpecification(
                        reader,
                        (TypeSpecificationHandle)handle,
                        context: null)),
            _ => throw new BadImageFormatException(
                $"Unsupported generic constraint type {handle.Kind}."),
        };

        if (StructuralSignatureKey.PartLength(type.EncodedLength)
            > availableEncodedChars)
        {
            throw new BadImageFormatException(
                "The structural signature exceeds the encoded-character budget.");
        }

        string encoded = Encode(type);
        _constraintTypes.Add(handle, encoded);
        if (!typeSpecificationBlob.IsNil)
            _constraintTypeSpecifications.TryAdd(typeSpecificationBlob, encoded);
        return encoded;
    }

    StructuralSignatureType DecodeTypeSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        object? context)
    {
        BlobHandle signature = reader.GetTypeSpecification(handle).Signature;
        if (_typeSpecifications.TryGetValue(signature, out var decoded))
            return decoded;

        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
        {
            throw new BadImageFormatException(
                "The TypeSpec exceeds the structural safety limit.");
        }
        using (scope)
        {
            decoded = reader.GetTypeSpecification(handle)
                .DecodeSignature(this, context);
        }
        _typeSpecifications.Add(signature, decoded);
        return decoded;
    }

    public StructuralSignatureType GetSZArrayType(
        StructuralSignatureType elementType)
        => new PartPrefixStructuralSignatureType('z', elementType);

    public StructuralSignatureType GetArrayType(
        StructuralSignatureType elementType,
        ArrayShape shape)
        => new ArrayStructuralSignatureType(elementType, shape);

    public StructuralSignatureType GetByReferenceType(
        StructuralSignatureType elementType)
        => new PartPrefixStructuralSignatureType('b', elementType);

    public StructuralSignatureType GetPointerType(
        StructuralSignatureType elementType)
        => new PartPrefixStructuralSignatureType('i', elementType);

    public StructuralSignatureType GetPinnedType(
        StructuralSignatureType elementType)
        => new PartPrefixStructuralSignatureType('q', elementType);

    public StructuralSignatureType GetGenericInstantiation(
        StructuralSignatureType genericType,
        ImmutableArray<StructuralSignatureType> typeArguments)
        => new GenericInstantiationStructuralSignatureType(
            genericType,
            typeArguments);

    public StructuralSignatureType GetGenericTypeParameter(
        object? context,
        int index)
        => Encoded(
            "t" + index.ToString(CultureInfo.InvariantCulture) + ";");

    public StructuralSignatureType GetGenericMethodParameter(
        object? context,
        int index)
        => Encoded(
            "m" + index.ToString(CultureInfo.InvariantCulture) + ";");

    public StructuralSignatureType GetFunctionPointerType(
        MethodSignature<StructuralSignatureType> signature)
        => new FunctionPointerStructuralSignatureType(signature);

    public StructuralSignatureType GetModifiedType(
        StructuralSignatureType modifier,
        StructuralSignatureType unmodifiedType,
        bool isRequired)
        => new ModifiedStructuralSignatureType(
            modifier,
            unmodifiedType,
            isRequired);

    StructuralSignatureType NamedTypeUse(
        MetadataReader reader,
        char kind,
        byte rawTypeKind,
        string typeName)
    {
        _ = reader;
        _workBudget.Charge(typeName.Length);
        return new TypeUseStructuralSignatureType(
            kind,
            rawTypeKind,
            typeName);
    }

    StructuralSignatureType NamedType(string typeName)
    {
        _workBudget.Charge(typeName.Length);
        return Encoded(typeName);
    }

    string Encode(StructuralSignatureType type)
    {
        _workBudget.Charge(type.EncodedLength);
        var builder = new StringBuilder(type.EncodedLength);
        type.AppendTo(builder);
        return builder.ToString();
    }

    static void EnsureConstraintFits(
        string encoded,
        int availableEncodedChars)
    {
        if (StructuralSignatureKey.PartLength(encoded)
            > availableEncodedChars)
        {
            throw new BadImageFormatException(
                "The structural signature exceeds the encoded-character budget.");
        }
    }

    static StructuralSignatureType Encoded(string encoded)
        => new EncodedStructuralSignatureType(encoded);
}

static class StructuralTypeName
{
    internal static string OfDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
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
                rejection?.Detail ?? "The type has an invalid declaring-type chain.");
        }

        var builder = new StringBuilder("D");
        var outer = reader.GetTypeDefinition(chain[0]);
        StructuralSignatureKey.AppendPart(builder, reader.GetString(outer.Namespace));
        StructuralSignatureKey.AppendNumber(builder, consumed);
        for (int i = 0; i < consumed; i++)
        {
            var definition = reader.GetTypeDefinition(chain[i]);
            string name = typeNameOverrides is not null
                && typeNameOverrides.TryGetValue(chain[i], out var replacement)
                    ? replacement
                    : reader.GetString(definition.Name);
            StructuralSignatureKey.AppendPart(builder, name);
        }
        return builder.ToString();
    }

    internal static string OfReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
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
                rejection?.Detail ?? "The type has an invalid resolution-scope chain.");
        }

        var builder = new StringBuilder("R");
        StructuralSignatureKey.AppendPart(
            builder,
            StructuralSignatureKey.ReferenceScope(reader, terminal));
        var outer = reader.GetTypeReference(chain[0]);
        StructuralSignatureKey.AppendPart(builder, reader.GetString(outer.Namespace));
        StructuralSignatureKey.AppendNumber(builder, consumed);
        for (int i = 0; i < consumed; i++)
        {
            StructuralSignatureKey.AppendPart(
                builder,
                reader.GetString(reader.GetTypeReference(chain[i]).Name));
        }
        return builder.ToString();
    }
}
