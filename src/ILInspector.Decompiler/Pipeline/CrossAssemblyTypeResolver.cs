using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers facts about a type defined in another assembly that the importing
/// assembly's metadata cannot state on its own. A bare type token (a
/// <c>newobj</c> target, a <c>box</c>/<c>sizeof</c> operand) carries no
/// <c>VALUETYPE</c>/<c>CLASS</c> byte, so <see cref="TypeRef.ValueTypeHint"/> is
/// <see cref="ValueTypeHint.Unknown"/> for cross-assembly references; this
/// resolver locates the defining assembly and reads the answer from its
/// metadata.
/// </summary>
/// <remarks>
/// Precision-preserving: a fact is returned only when the defining assembly is
/// located and its metadata confirms it. Anything unreachable (no locator hit,
/// a forwarder dead-end, a nested type, an I/O or format error) yields
/// <see cref="ValueTypeHint.Unknown"/> — never a guess. Security: a reference
/// whose public-key token is a trusted platform key is asserted
/// <see cref="AssemblyTrust.Platform"/> so the locator resolves it only from the
/// trusted framework, never a confusable local copy.
///
/// Reading is delegated to a shared <see cref="MetadataContext"/>: each defining
/// assembly is opened once and indexed for O(1) lookup, so resolving N tokens
/// from the same assembly costs one open and one type-table pass, not N.
/// </remarks>
internal sealed class CrossAssemblyTypeResolver
{
    static readonly string[] CoreLibCandidates =
        ["System.Private.CoreLib", "System.Runtime", "mscorlib", "netstandard"];

    readonly string _selfSimpleName;
    readonly MetadataReader _selfReader;
    readonly MetadataContext _context;
    readonly Dictionary<TypeRef, ValueTypeHint> _valueTypeCache = [];

    public CrossAssemblyTypeResolver(string selfSimpleName, MetadataReader selfReader, MetadataContext context)
    {
        _selfSimpleName = selfSimpleName;
        _selfReader = selfReader;
        _context = context;
    }

    /// <summary>
    /// Returns <paramref name="type"/> with its declared value-type-ness stamped
    /// when this resolver can confirm it from the defining assembly; returns the
    /// type unchanged when it already carries a hint, is not a cross-assembly
    /// named definition, or cannot be resolved.
    /// </summary>
    public TypeRef Upgrade(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition || type.ValueTypeHint != ValueTypeHint.Unknown)
            return type;
        if (string.IsNullOrEmpty(type.Assembly) || type.Name.Contains('+'))
            return type;
        // Same-assembly references are resolved by the importer's own shapes.
        if (type.Assembly == TypeRefDecoder.Canonical(_selfSimpleName))
            return type;

        var hint = ResolveValueTypeHint(type);
        return hint == ValueTypeHint.Unknown ? type : type.WithValueTypeHint(hint);
    }

    ValueTypeHint ResolveValueTypeHint(TypeRef type)
    {
        if (_valueTypeCache.TryGetValue(type, out var cached))
            return cached;

        var hint = ValueTypeHint.Unknown;
        try
        {
            if (Locate(type) is { } location)
                hint = ReadValueTypeHint(location);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }

        _valueTypeCache[type] = hint;
        return hint;
    }

    TypeLocation? Locate(TypeRef type)
    {
        string fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";

        if (type.Assembly == TypeRef.CoreLibrary)
        {
            foreach (var candidate in CoreLibCandidates)
            {
                if (_context.Locator(candidate, AssemblyTrust.Platform) is not { } start || !File.Exists(start))
                    continue;
                if (TypeForwardResolver.LocateType(start, fullName, _context.Locator, trust: AssemblyTrust.Platform) is { } located)
                    return located;
            }
            return null;
        }

        var trust = TrustFor(type.Assembly);
        if (_context.Locator(type.Assembly, trust) is not { } startPath || !File.Exists(startPath))
            return null;
        return TypeForwardResolver.LocateType(startPath, fullName, _context.Locator, trust: trust);
    }

    AssemblyTrust TrustFor(string simpleName)
    {
        foreach (var handle in _selfReader.AssemblyReferences)
        {
            var reference = _selfReader.GetAssemblyReference(handle);
            if (!string.Equals(_selfReader.GetString(reference.Name), simpleName, StringComparison.OrdinalIgnoreCase))
                continue;
            return PlatformKeys.IsPlatform(ToHex(_selfReader.GetBlobBytes(reference.PublicKeyOrToken)))
                ? AssemblyTrust.Platform
                : AssemblyTrust.Unspecified;
        }
        return AssemblyTrust.Unspecified;
    }

    ValueTypeHint ReadValueTypeHint(TypeLocation location)
    {
        if (_context.Open(location.AssemblyPath) is not { } assembly)
            return ValueTypeHint.Unknown;
        if (!assembly.TryGetType(location.FullTypeName, out var handle))
            return ValueTypeHint.Unknown;

        var typeDef = assembly.Reader.GetTypeDefinition(handle);
        // A struct's immediate base is System.ValueType (System.Enum for an
        // enum); anything else (or no base) is a reference type.
        string? baseName = BaseTypeName(assembly.Reader, typeDef.BaseType);
        return baseName is "System.ValueType" or "System.Enum"
            ? ValueTypeHint.ValueType
            : ValueTypeHint.ReferenceType;
    }

    static string? BaseTypeName(MetadataReader reader, EntityHandle baseType) => baseType.Kind switch
    {
        HandleKind.TypeReference => reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)baseType)),
        HandleKind.TypeDefinition => reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)baseType)),
        _ => null,
    };

    static string ToHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = "0123456789abcdef"[bytes[i] >> 4];
            chars[i * 2 + 1] = "0123456789abcdef"[bytes[i] & 0xF];
        }
        return new string(chars);
    }
}
