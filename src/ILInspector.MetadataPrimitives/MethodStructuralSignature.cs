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

    internal static string BuildCore(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides,
        StructuralSignatureTypeProvider provider,
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

        var builder = new StringBuilder("T");
        var outer = reader.GetTypeDefinition(chain[0]);
        StructuralSignatureKey.AppendPart(builder, reader.GetString(outer.Namespace));
        StructuralSignatureKey.AppendNumber(builder, consumed);
        for (int i = 0; i < consumed; i++)
        {
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
            builder.Append(segmentKey);
        }

        return builder.ToString();
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
    readonly Dictionary<TypeDefinitionHandle, string> _typeKeys = [];
    readonly Dictionary<TypeDefinitionHandle, string> _typeSegments = [];

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
        => StructuralSignatureKey.Build(_reader, () =>
        {
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
            var builder = new StringBuilder("M");
            StructuralSignatureKey.AppendPart(
                builder,
                BuildTypeCore(method.GetDeclaringType()));
            StructuralSignatureKey.AppendPart(
                builder,
                methodName ?? _reader.GetString(method.Name));
            StructuralSignatureKey.AppendGenericParameters(
                builder,
                _reader,
                method.GetGenericParameters(),
                _provider);
            StructuralSignatureKey.AppendMethodSignature(builder, signature);
            return builder.ToString();
        });

    /// <summary>Builds a type key.</summary>
    public string BuildType(TypeDefinitionHandle handle)
        => StructuralSignatureKey.Build(
            _reader,
            () => BuildTypeCore(handle));

    string BuildTypeCore(TypeDefinitionHandle handle)
    {
        if (_typeKeys.TryGetValue(handle, out string? key))
            return key;

        // A declaring chain may carry many constraint rows. Reuse its encoded
        // key across every method on that type so artifact-controlled method and
        // constraint counts cannot multiply. Gated by
        // BuildMethod_ReusesDeclaringTypeKeyAcrossMethods.
        key = TypeStructuralSignature.BuildCore(
            _reader,
            handle,
            _typeNameOverrides,
            _provider,
            _typeSegments);
        _typeKeys.Add(handle, key);
        return key;
    }
}

static class StructuralSignatureKey
{
    internal static string Build(MetadataReader reader, Func<string> build)
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
        builder.Append(value);
    }

    internal static void AppendNumber(StringBuilder builder, int value)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');
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
        => handle.Kind switch
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
