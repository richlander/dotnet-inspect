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
                    provider);
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
        string signature)
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
        string signature)
    {
        MethodName = methodName;
        GenericParameters = genericParameters;
        Signature = signature;
        _hashCode = HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(methodName),
            StringComparer.Ordinal.GetHashCode(genericParameters),
            StringComparer.Ordinal.GetHashCode(signature));
    }

    internal string MethodName { get; }
    internal string GenericParameters { get; }
    internal string Signature { get; }

    internal int EncodedLength =>
        checked(
            StructuralSignatureKey.PartLength(MethodName)
            + GenericParameters.Length
            + Signature.Length);

    public bool Equals(StructuralMethodComponent? other)
        => ReferenceEquals(this, other)
            || other is not null
                && _hashCode == other._hashCode
                && StringComparer.Ordinal.Equals(
                    MethodName,
                    other.MethodName)
                && StringComparer.Ordinal.Equals(
                    GenericParameters,
                    other.GenericParameters)
                && StringComparer.Ordinal.Equals(
                    Signature,
                    other.Signature);

    public override bool Equals(object? obj)
        => obj is StructuralMethodComponent other && Equals(other);

    public override int GetHashCode() => _hashCode;

    internal void AppendEncoded(StringBuilder builder)
    {
        StructuralSignatureKey.AppendPart(builder, MethodName);
        builder.Append(GenericParameters);
        builder.Append(Signature);
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
    readonly StructuralSignatureTypeProvider _provider = new();
    readonly Dictionary<TypeDefinitionHandle, StructuralTypeKey> _typeKeys = [];
    readonly Dictionary<TypeDefinitionHandle, string> _typeSegments = [];
    readonly Dictionary<BlobHandle, string> _methodSignatures = [];

    /// <summary>
    /// Creates a reusable builder. The override map must remain unchanged for
    /// the builder's lifetime.
    /// </summary>
    public StructuralSignatureBuilder(
        MetadataReader reader,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _typeNameOverrides = typeNameOverrides;
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
            string genericParameters =
                StructuralSignatureKey.EncodeGenericParameters(
                _reader,
                method.GetGenericParameters(),
                _provider);
            return new StructuralMethodKey(
                BuildTypeCore(method.GetDeclaringType()),
                methodName ?? _reader.GetString(method.Name),
                genericParameters,
                BuildMethodSignature(method));
        });

    /// <summary>Builds a type key.</summary>
    public string BuildType(TypeDefinitionHandle handle)
        => BuildTypeKey(handle).ToString();

    internal StructuralTypeKey BuildTypeKey(TypeDefinitionHandle handle)
        => StructuralSignatureKey.Build(
            _reader,
            () => BuildTypeCore(handle));

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
            _typeKeys,
            _typeSegments);
        return key;
    }

    string BuildMethodSignature(MethodDefinition method)
    {
        if (_methodSignatures.TryGetValue(method.Signature, out string? signatureKey))
            return signatureKey;

        if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            throw new BadImageFormatException(
                "The method signature exceeds the structural safety limit.");
        }

        MethodSignature<string> signature =
            method.DecodeSignature(_provider, null);
        var builder = new StringBuilder();
        StructuralSignatureKey.AppendMethodSignature(builder, signature);
        signatureKey = builder.ToString();
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
        MethodSignature<string> signature)
    {
        builder.Append('S');
        AppendNumber(builder, signature.Header.RawValue);
        AppendNumber(builder, signature.GenericParameterCount);
        AppendNumber(builder, signature.RequiredParameterCount);
        AppendNumber(builder, signature.ParameterTypes.Length);
        AppendPart(builder, signature.ReturnType);
        foreach (string parameter in signature.ParameterTypes)
            AppendPart(builder, parameter);
    }

    internal static void AppendGenericParameters(
        StringBuilder builder,
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        StructuralSignatureTypeProvider provider)
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
            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                constraints.Add(provider.GetConstraintType(reader, constraint.Type));
            }
            constraints.Sort(StringComparer.Ordinal);
            AppendNumber(builder, constraints.Count);
            foreach (string constraint in constraints)
                AppendPart(builder, constraint);
        }
    }

    internal static string EncodeGenericParameters(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        StructuralSignatureTypeProvider provider)
    {
        if (handles.Count == 0)
            return "0;";

        var builder = new StringBuilder();
        AppendGenericParameters(builder, reader, handles, provider);
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

    internal static void AppendNumber(StringBuilder builder, int value)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        EnsureCanAppend(builder, text.Length + 1);
        builder.Append(text).Append(';');
    }

    internal static int NumberLength(int value)
        => value.ToString(CultureInfo.InvariantCulture).Length + 1;

    internal static int PartLength(string value)
        => checked(NumberLength(value.Length) + value.Length);

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

sealed class StructuralSignatureTypeProvider
    : ISignatureTypeProvider<string, object?>
{
    readonly Dictionary<EntityHandle, string> _constraintTypes = [];

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        => "p" + ((int)typeCode).ToString(CultureInfo.InvariantCulture) + ";";

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
        => TypeUse(
            'd',
            rawTypeKind,
            StructuralTypeName.OfDefinition(reader, handle, typeNameOverrides: null));

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
        => TypeUse(
            'r',
            rawTypeKind,
            StructuralTypeName.OfReference(reader, handle));

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? context,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
        {
            throw new BadImageFormatException(
                "The TypeSpec exceeds the structural safety limit.");
        }
        using (scope)
        {
            string decoded =
                reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            return TypeUse('s', rawTypeKind, decoded);
        }
    }

    internal string GetConstraintType(MetadataReader reader, EntityHandle handle)
    {
        if (_constraintTypes.TryGetValue(handle, out string? type))
            return type;

        type = handle.Kind switch
        {
            HandleKind.TypeDefinition => "d" + Part(
                StructuralTypeName.OfDefinition(
                    reader,
                    (TypeDefinitionHandle)handle,
                    typeNameOverrides: null)),
            HandleKind.TypeReference => "r" + Part(
                StructuralTypeName.OfReference(
                    reader,
                    (TypeReferenceHandle)handle)),
            HandleKind.TypeSpecification => "s" + Part(
                DecodeConstraintTypeSpecification(
                    reader,
                    (TypeSpecificationHandle)handle)),
            _ => throw new BadImageFormatException(
                $"Unsupported generic constraint type {handle.Kind}."),
        };
        _constraintTypes.Add(handle, type);
        return type;
    }

    string DecodeConstraintTypeSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
        {
            throw new BadImageFormatException(
                "The constraint TypeSpec exceeds the structural safety limit.");
        }
        using (scope)
        {
            return reader.GetTypeSpecification(handle)
                .DecodeSignature(this, null);
        }
    }

    public string GetSZArrayType(string elementType)
        => "z" + Part(elementType);

    public string GetArrayType(string elementType, ArrayShape shape)
    {
        var builder = new StringBuilder("a");
        StructuralSignatureKey.AppendNumber(builder, shape.Rank);
        StructuralSignatureKey.AppendNumber(builder, shape.Sizes.Length);
        foreach (int size in shape.Sizes)
            StructuralSignatureKey.AppendNumber(builder, size);
        StructuralSignatureKey.AppendNumber(builder, shape.LowerBounds.Length);
        foreach (int lowerBound in shape.LowerBounds)
            StructuralSignatureKey.AppendNumber(builder, lowerBound);
        StructuralSignatureKey.AppendPart(builder, elementType);
        return builder.ToString();
    }

    public string GetByReferenceType(string elementType)
        => "b" + Part(elementType);

    public string GetPointerType(string elementType)
        => "i" + Part(elementType);

    public string GetPinnedType(string elementType)
        => "q" + Part(elementType);

    public string GetGenericInstantiation(
        string genericType,
        ImmutableArray<string> typeArguments)
    {
        var builder = new StringBuilder("g");
        StructuralSignatureKey.AppendPart(builder, genericType);
        StructuralSignatureKey.AppendNumber(builder, typeArguments.Length);
        foreach (string argument in typeArguments)
            StructuralSignatureKey.AppendPart(builder, argument);
        return builder.ToString();
    }

    public string GetGenericTypeParameter(object? context, int index)
        => "t" + index.ToString(CultureInfo.InvariantCulture) + ";";

    public string GetGenericMethodParameter(object? context, int index)
        => "m" + index.ToString(CultureInfo.InvariantCulture) + ";";

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        var builder = new StringBuilder("f");
        StructuralSignatureKey.AppendMethodSignature(builder, signature);
        return builder.ToString();
    }

    public string GetModifiedType(
        string modifier,
        string unmodifiedType,
        bool isRequired)
        => (isRequired ? "c" : "o") + Part(modifier) + Part(unmodifiedType);

    static string TypeUse(char kind, byte rawTypeKind, string type)
    {
        var builder = new StringBuilder();
        builder.Append(kind);
        StructuralSignatureKey.AppendNumber(builder, rawTypeKind);
        StructuralSignatureKey.AppendPart(builder, type);
        return builder.ToString();
    }

    static string Part(string value)
    {
        var builder = new StringBuilder();
        StructuralSignatureKey.AppendPart(builder, value);
        return builder.ToString();
    }
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
