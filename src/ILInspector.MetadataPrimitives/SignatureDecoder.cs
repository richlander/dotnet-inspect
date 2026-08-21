using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

namespace ILInspector.Metadata;

/// <summary>
/// Decodes type signatures from metadata into human-readable C# type names.
/// Handles primitives, generics, arrays, pointers, and ref types.
/// </summary>
public class SignatureDecoder : ISignatureTypeProvider<string, GenericContext?>
{
    const string Unresolved = "object";
    internal const int MaxAcceptedNameCacheEntries =
        MetadataSafetyPolicy.MaxRelationshipNodes * 16;
    internal const int MaxAcceptedNameCacheCharacters =
        MetadataSafetyPolicy.MaxStructuralSignatureChars;
    // SRM's string provider callback erases segment boundaries before
    // GetGenericInstantiation runs. Every TypeDef/TypeRef head keeps exact parts:
    // display-decoration characters are legal metadata-name text, so a flat
    // prefilter cannot prove that later generic parsing is safe.
    readonly ConditionalWeakTable<string, MetadataTypeNameParts> structuredNames = new();
    readonly ConditionalWeakTable<MetadataReader, ReaderNameCache> readerNames = new();
    readonly Action<int>? _beforeMaterialize;
    readonly bool _enforceCharacterBudget;

    [ThreadStatic]
    static SignatureDecodeRejection? s_rejection;

    /// <summary>
    /// Shared instance for common use cases.
    /// </summary>
    public static SignatureDecoder Instance { get; } = new();

    public SignatureDecoder()
        : this(beforeMaterialize: null, enforceCharacterBudget: true)
    {
    }

    internal SignatureDecoder(Action<int> beforeMaterialize)
        : this(beforeMaterialize, enforceCharacterBudget: true)
    {
        ArgumentNullException.ThrowIfNull(beforeMaterialize);
    }

    internal SignatureDecoder(Action<int>? beforeMaterialize, bool enforceCharacterBudget)
    {
        _beforeMaterialize = beforeMaterialize;
        _enforceCharacterBudget = enforceCharacterBudget;
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "void",
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
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => typeCode.ToString()
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        if (TryGetCached(reader, handle, out string? cached))
            return cached;
        int materializationWork = 0;
        Action<int>? observe = _beforeMaterialize is null
            ? null
            : amount =>
            {
                materializationWork = SaturatingAdd(
                    materializationWork,
                    amount);
                _beforeMaterialize(amount);
            };
        return Retain(
            reader,
            handle,
            TypeResolver.TryGetTypeNameFromDefinition(
                reader,
                handle,
                observe,
                out string? name,
                out RelationshipTraversalRejection? rejection,
                _enforceCharacterBudget),
            name,
            rejection,
            () => TypeResolver.ResolveTypeNamePartsFromDefinition(
                reader,
                handle,
                _enforceCharacterBudget),
            materializationWork);
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        if (TryGetCached(reader, handle, out string? cached))
            return cached;
        int materializationWork = 0;
        Action<int>? observe = _beforeMaterialize is null
            ? null
            : amount =>
            {
                materializationWork = SaturatingAdd(
                    materializationWork,
                    amount);
                _beforeMaterialize(amount);
            };
        return Retain(
            reader,
            handle,
            TypeResolver.TryGetTypeNameFromReference(
                reader,
                handle,
                observe,
                out string? name,
                out RelationshipTraversalRejection? rejection,
                _enforceCharacterBudget),
            name,
            rejection,
            () => TypeResolver.ResolveTypeNamePartsFromReference(
                reader,
                handle,
                _enforceCharacterBudget),
            materializationWork);
    }

    public string GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        _beforeMaterialize?.Invoke(
            reader.GetBlobReader(
                reader.GetTypeSpecification(handle).Signature).Length);
        if (!TypeSpecGuard.TryEnter(
            reader,
            handle,
            out var scope,
            out var rejectionKind))
        {
            Reject(
                new SignatureDecodeRejection(
                    rejectionKind,
                    rejectionKind == SignatureDecodeRejectionKind.UnsafeStructure
                        ? "A nested TypeSpec exceeds the structural safety limit."
                        : "A nested TypeSpec exceeds the re-entry depth or cumulative-byte budget."));
            return Unresolved;
        }
        using (scope)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        }
    }

    internal static SignatureDecodeResult<T> Decode<T>(
        Func<T> decode,
        Func<SignatureDecodeRejection?>? preflight = null)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(decode);
        var previousRejection = s_rejection;
        s_rejection = null;
        try
        {
            if (preflight?.Invoke() is { } preflightRejection)
                return new SignatureDecodeResult<T>.Rejected(preflightRejection);

            T value = decode();
            return s_rejection is null
                ? new SignatureDecodeResult<T>.Decoded(value)
                : new SignatureDecodeResult<T>.Rejected(s_rejection);
        }
        catch (BadImageFormatException ex)
        {
            return RecordedOrMalformed<T>(ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return RecordedOrMalformed<T>(ex);
        }
        finally
        {
            s_rejection = previousRejection;
        }
    }

    internal static void Reject(SignatureDecodeRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        s_rejection ??= rejection;
    }

    static string ReadNameOrContinue(
        bool resolved,
        string? name,
        RelationshipTraversalRejection? rejection)
    {
        if (resolved)
            return name!;

        ArgumentNullException.ThrowIfNull(rejection);
        if (rejection.Kind == RelationshipTraversalRejectionKind.NameBudget)
        {
            Reject(
                new SignatureDecodeRejection(
                    SignatureDecodeRejectionKind.NameBudget,
                    rejection.Detail));
            return Unresolved;
        }

        throw new BadImageFormatException(
            $"Metadata relationship traversal rejected ({rejection.Kind}): "
            + rejection.Detail);
    }

    static SignatureDecodeResult<T> RecordedOrMalformed<T>(Exception exception)
        where T : notnull
        => s_rejection is { } recorded
            ? new SignatureDecodeResult<T>.Rejected(recorded)
            : Malformed<T>(exception);

    static SignatureDecodeResult<T> Malformed<T>(Exception exception)
        where T : notnull
        => new SignatureDecodeResult<T>.Rejected(
            new SignatureDecodeRejection(
                SignatureDecodeRejectionKind.MalformedMetadata,
                exception.Message));

    public string GetSZArrayType(string elementType)
    {
        ObserveMaterialization(elementType.Length + 2L);
        return $"{elementType}[]";
    }

    public string GetArrayType(string elementType, ArrayShape shape)
    {
        ObserveMaterialization(elementType.Length + Math.Max(shape.Rank, 0L) + 1L);
        return $"{elementType}[{new string(',', shape.Rank - 1)}]";
    }

    public string GetByReferenceType(string elementType)
    {
        ObserveMaterialization(elementType.Length + 4L);
        return $"ref {elementType}";
    }

    public string GetPointerType(string elementType)
    {
        ObserveMaterialization(elementType.Length + 1L);
        return $"{elementType}*";
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        long estimatedLength = genericType.Length * 2L + 16L;
        foreach (string argument in typeArguments)
            estimatedLength += argument.Length + 2L;
        ObserveMaterialization(estimatedLength);
        if (!structuredNames.TryGetValue(genericType, out MetadataTypeNameParts? structured))
            return TypeResolver.ApplyGenericArguments(genericType, typeArguments);

        string typeName = TypeResolver.ApplyGenericArguments(
            structured.Segments,
            typeArguments);
        return structured.Namespace.Length == 0
            ? typeName
            : $"{structured.Namespace}.{typeName}";
    }

    string Retain(
        MetadataReader reader,
        EntityHandle handle,
        bool resolved,
        string? projectedName,
        RelationshipTraversalRejection? rejection,
        Func<RelationshipTraversalResult<MetadataTypeNameParts>> create,
        int materializationWork)
    {
        if (!resolved)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            ReaderNameCache rejectedCache = readerNames.GetValue(
                reader,
                static _ => new ReaderNameCache());
            lock (rejectedCache.Names)
            {
                if (!rejectedCache.Rejections.ContainsKey(handle)
                    && rejectedCache.TryReserve(
                        rejection.Detail.Length))
                {
                    rejectedCache.Rejections.Add(
                        handle,
                        new(rejection, materializationWork));
                }
            }
            return ReadNameOrContinue(false, projectedName, rejection);
        }

        ReaderNameCache cache = readerNames.GetValue(
            reader,
            static _ => new ReaderNameCache());
        lock (cache.Names)
        {
            if (cache.Names.TryGetValue(
                    handle,
                    out CachedName? retained))
            {
                return retained.Name;
            }

            RelationshipTraversalResult<MetadataTypeNameParts> result = create();
            MetadataTypeNameParts structured = result.GetValueOrThrow();
            string value = structured.ToDottedName();
            if (!string.Equals(
                    projectedName,
                    value,
                    StringComparison.Ordinal))
            {
                throw new BadImageFormatException(
                    "Structured and projected metadata type names disagree.");
            }
            if (value.Length == 0)
            {
                if (cache.TryReserve(
                        RetainedCharacters(value, structured)))
                {
                    cache.Names.Add(
                        handle,
                        new(value, materializationWork));
                }
                return value;
            }

            string name = string.Create(
                value.Length,
                value,
                static (destination, source) =>
                    source.AsSpan().CopyTo(destination));
            structuredNames.Add(name, structured);
            if (cache.TryReserve(
                    RetainedCharacters(name, structured)))
            {
                cache.Names.Add(
                    handle,
                    new(name, materializationWork));
            }
            return name;
        }
    }

    static int SaturatingAdd(int left, int right)
        => (int)Math.Min(int.MaxValue, (long)left + right);

    bool TryGetCached(
        MetadataReader reader,
        EntityHandle handle,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
    {
        if (!readerNames.TryGetValue(reader, out ReaderNameCache? cache))
        {
            value = null;
            return false;
        }

        lock (cache.Names)
        {
            if (cache.Names.TryGetValue(
                    handle,
                    out CachedName? cached))
            {
                ReplayMaterializationWork(cached.MaterializationWork);
                value = cached.Name;
                return true;
            }
            if (cache.Rejections.TryGetValue(
                    handle,
                    out CachedRejection? cachedRejection))
            {
                ReplayMaterializationWork(
                    cachedRejection.MaterializationWork);
                value = ReadNameOrContinue(
                    resolved: false,
                    name: null,
                    cachedRejection.Rejection);
                return true;
            }
        }

        value = null;
        return false;
    }

    void ReplayMaterializationWork(int amount)
    {
        if (amount > 0)
            _beforeMaterialize?.Invoke(amount);
    }

    internal int GetCachedEntryCount(MetadataReader reader)
    {
        if (!readerNames.TryGetValue(
                reader,
                out ReaderNameCache? cache))
        {
            return 0;
        }

        lock (cache.Names)
            return cache.EntryCount;
    }

    static long RetainedCharacters(
        string name,
        MetadataTypeNameParts structured)
    {
        long characters =
            name.Length + structured.Namespace.Length;
        foreach (string segment in structured.Segments)
            characters += segment.Length;
        return characters;
    }

    sealed class ReaderNameCache
    {
        internal Dictionary<EntityHandle, CachedName> Names { get; } = [];
        internal Dictionary<EntityHandle, CachedRejection> Rejections { get; } = [];

        long _retainedCharacters;

        internal int EntryCount =>
            Names.Count + Rejections.Count;

        internal bool TryReserve(long characters)
        {
            if (EntryCount >= MaxAcceptedNameCacheEntries
                || characters
                    > MaxAcceptedNameCacheCharacters
                        - _retainedCharacters)
            {
                return false;
            }

            _retainedCharacters += characters;
            return true;
        }
    }

    sealed record CachedName(
        string Name,
        int MaterializationWork);

    sealed record CachedRejection(
        RelationshipTraversalRejection Rejection,
        int MaterializationWork);

    public string GetGenericMethodParameter(GenericContext? context, int index)
    {
        if (context is not null && index < context.MethodParameters.Count)
            return context.MethodParameters[index];
        return $"TM{index}";
    }

    public string GetGenericTypeParameter(GenericContext? context, int index)
    {
        if (context is not null && index < context.TypeParameters.Count)
            return context.TypeParameters[index];
        return $"T{index}";
    }

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        long estimatedLength = signature.ReturnType.Length + 32L;
        foreach (string parameterType in signature.ParameterTypes)
            estimatedLength += parameterType.Length + 2L;
        ObserveMaterialization(
            estimatedLength + 16L + signature.ParameterTypes.Length * 4L);
        var types = signature.ParameterTypes.Add(signature.ReturnType);
        string arguments = string.Join(", ", types);
        string convention = ConventionText(signature.Header.CallingConvention);
        return convention.Length == 0
            ? $"delegate*<{arguments}>"
            : $"delegate* {convention}<{arguments}>";
    }

    static string ConventionText(SignatureCallingConvention convention) => convention switch
    {
        SignatureCallingConvention.Default => "",
        SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
        SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
        SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
        SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
        _ => "unmanaged",
    };

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    void ObserveMaterialization(long units)
        => _beforeMaterialize?.Invoke((int)Math.Min(units, int.MaxValue));
}
