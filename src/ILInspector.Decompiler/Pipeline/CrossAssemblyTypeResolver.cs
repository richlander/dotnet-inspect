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
    readonly Dictionary<MethodRef, bool> _requiresUnsafeCache = [];

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

    /// <summary>
    /// Returns <paramref name="callee"/> with <see cref="MethodRef.RequiresUnsafe"/>
    /// stamped when the defining assembly confirms the method — or its declaring
    /// type — carries <c>RequiresUnsafeAttribute</c>. Same-assembly callees
    /// already carry the flag from their MethodDef, and a pointer-in-signature
    /// callee is caught by the printer's local heuristic; this closes the
    /// remaining case — a <em>pointerless</em> requires-unsafe method referenced
    /// across assemblies, whose attribute lives only on the defining MethodDef
    /// (or its type) and is invisible in the importing assembly's MemberRef.
    /// </summary>
    /// <remarks>
    /// Precision-preserving like <see cref="Upgrade(TypeRef)"/>: the flag is set
    /// only when the defining assembly is located and its metadata confirms the
    /// attribute. Anything unreachable yields the callee unchanged — never a
    /// guess. The caller should invoke this only for modules that use the
    /// updated memory-safety rules, since the flag is inert otherwise.
    /// </remarks>
    public MethodRef Upgrade(MethodRef callee)
    {
        if (callee.RequiresUnsafe)
            return callee;

        var type = callee.DeclaringType;
        if (type.Kind != TypeRefKind.Definition || string.IsNullOrEmpty(type.Assembly) || type.Name.Contains('+'))
            return callee;
        // Same-assembly callees are stamped by the importer from their MethodDef.
        if (type.Assembly == TypeRefDecoder.Canonical(_selfSimpleName))
            return callee;

        if (!_requiresUnsafeCache.TryGetValue(callee, out var requiresUnsafe))
        {
            requiresUnsafe = ResolveRequiresUnsafe(callee, type);
            _requiresUnsafeCache[callee] = requiresUnsafe;
        }

        return requiresUnsafe ? callee with { RequiresUnsafe = true } : callee;
    }

    bool ResolveRequiresUnsafe(MethodRef callee, TypeRef type)
    {
        try
        {
            if (Locate(type) is not { } location)
                return false;
            if (_context.Open(location.AssemblyPath) is not { } assembly)
                return false;
            if (!assembly.TryGetType(location.FullTypeName, out var handle))
                return false;

            var reader = assembly.Reader;
            var typeDef = reader.GetTypeDefinition(handle);
            // A type-level attribute marks every member requires-unsafe.
            if (AttributeReader.HasRequiresUnsafeAttribute(reader, typeDef.GetCustomAttributes()))
                return true;

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!string.Equals(reader.GetString(method.Name), callee.Name, StringComparison.Ordinal))
                    continue;
                // Disambiguate overloads by arity; the pointerless requires-unsafe
                // method we are after has a stable parameter count.
                var signature = method.DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty);
                if (signature.ParameterTypes.Length != callee.ParameterTypes.Length)
                    continue;
                if (AttributeReader.HasRequiresUnsafeAttribute(reader, method.GetCustomAttributes()))
                    return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
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
