using System.Collections.Immutable;
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
/// a forwarder dead-end, an I/O or format error) yields
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
    readonly Dictionary<MethodRef, ResolvedMethodFacts?> _methodFactCache = [];

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
        if (string.IsNullOrEmpty(type.Assembly))
            return type;
        // Same-assembly references are resolved by the importer's own shapes.
        if (type.Assembly == TypeRefDecoder.Canonical(_selfSimpleName))
            return type;

        var hint = ResolveValueTypeHint(type);
        return hint == ValueTypeHint.Unknown ? type : type.WithValueTypeHint(hint);
    }

    /// <summary>
    /// Returns <paramref name="callee"/> with cross-assembly MethodDef facts
    /// stamped when the defining assembly can be resolved. Facts stay
    /// <see cref="MetadataFactState.Unknown"/> / <see cref="ParameterRefKindFacts.Unknown"/>
    /// when metadata is unreachable; absence of evidence is never reported as
    /// false.
    /// </summary>
    public MethodRef Upgrade(MethodRef callee, bool resolveRequiresUnsafe)
    {
        bool needsRefKinds = NeedsParameterRefKinds(callee);
        bool needsGenerated = NeedsGeneratedFacts(callee);
        bool needsUnsafe = resolveRequiresUnsafe && !callee.RequiresUnsafe;
        if (!needsRefKinds && !needsGenerated && !needsUnsafe)
            return callee;

        var type = NamedDefinition(callee.DeclaringType);
        if (type is null || string.IsNullOrEmpty(type.Assembly))
            return callee;
        // Same-assembly callees are stamped by the importer from their MethodDef.
        if (type.Assembly == TypeRefDecoder.Canonical(_selfSimpleName))
            return callee;

        if (!_methodFactCache.TryGetValue(callee, out var facts))
        {
            facts = ResolveMethodFacts(callee, type);
            _methodFactCache[callee] = facts;
        }

        if (facts is not { } resolved)
            return callee;

        return callee with
        {
            ParameterRefKinds = needsRefKinds && resolved.ParameterRefKinds.State != ParameterRefKindFacts.Unknown
                ? resolved.ParameterRefKinds.Kinds
                : callee.ParameterRefKinds,
            ParameterRefKindsFacts = needsRefKinds && resolved.ParameterRefKinds.State != ParameterRefKindFacts.Unknown
                ? resolved.ParameterRefKinds.State
                : callee.ParameterRefKindsFacts,
            RequiresUnsafe = callee.RequiresUnsafe || (needsUnsafe && resolved.RequiresUnsafe),
            CompilerGenerated = needsGenerated ? resolved.CompilerGenerated : callee.CompilerGenerated,
            DeclaringTypeCompilerGenerated = needsGenerated ? resolved.DeclaringTypeCompilerGenerated : callee.DeclaringTypeCompilerGenerated,
        };
    }

    ResolvedMethodFacts? ResolveMethodFacts(MethodRef callee, TypeRef type)
    {
        try
        {
            if (Locate(type) is not { } location)
                return null;
            if (_context.Open(location.AssemblyPath) is not { } assembly)
                return null;
            if (!assembly.TryGetType(location.FullTypeName, out var handle))
                return null;

            var reader = assembly.Reader;
            var typeDef = reader.GetTypeDefinition(handle);
            bool typeCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, typeDef.GetCustomAttributes());
            bool typeRequiresUnsafe = MethodDefinitionFacts.HasRequiresUnsafeAttribute(reader, typeDef);

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!string.Equals(reader.GetString(method.Name), callee.Name, StringComparison.Ordinal))
                    continue;
                if (!TryMatchMethod(reader, typeDef, method, callee, out var parameterRefKinds))
                    continue;

                bool methodCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, method.GetCustomAttributes());
                return new ResolvedMethodFacts(
                    parameterRefKinds,
                    typeRequiresUnsafe || MethodDefinitionFacts.HasRequiresUnsafeAttribute(reader, method),
                    FactState(methodCompilerGenerated),
                    FactState(typeCompilerGenerated));
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static bool TryMatchMethod(
        MetadataReader reader,
        TypeDefinition declaringType,
        MethodDefinition method,
        MethodRef callee,
        out ParameterRefKindResult parameterRefKinds)
    {
        parameterRefKinds = default;
        var scope = new GenericScope(
            MethodDefinitionFacts.GenericParameterNames(reader, declaringType.GetGenericParameters()),
            MethodDefinitionFacts.GenericParameterNames(reader, method.GetGenericParameters()));
        var signature = method.DecodeSignature(TypeRefDecoder.Instance, scope);
        if (signature.Header.IsInstance != callee.HasThis)
            return false;
        if (signature.GenericParameterCount != callee.TypeArguments.Length)
            return false;

        var typeArguments = callee.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? callee.DeclaringType.TypeArguments
            : [];
        var methodArguments = callee.TypeArguments;
        var returnType = signature.ReturnType.Instantiate(typeArguments, methodArguments);
        if (!returnType.Equals(callee.ReturnType))
            return false;

        if (signature.ParameterTypes.Length != callee.ParameterTypes.Length)
            return false;
        var parameters = ImmutableArray.CreateBuilder<TypeRef>(signature.ParameterTypes.Length);
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var parameter = signature.ParameterTypes[i].Instantiate(typeArguments, methodArguments);
            if (!parameter.Equals(callee.ParameterTypes[i]))
                return false;
            parameters.Add(parameter);
        }

        parameterRefKinds = MethodDefinitionFacts.ReadParameterRefKinds(reader, method, parameters.MoveToImmutable());
        return true;
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
        string name = type.Name.Replace('+', '.');
        string fullName = string.IsNullOrEmpty(type.Namespace) ? name : $"{type.Namespace}.{name}";

        if (type.Assembly == TypeRef.CoreLibrary)
        {
            foreach (var candidate in CoreLibCandidates)
            {
                if (_context.Locator(candidate, AssemblyTrust.Platform) is not { } start || !File.Exists(start))
                    continue;
                if (LocateFrom(start, fullName, AssemblyTrust.Platform) is { } located)
                    return located;
            }
            return null;
        }

        var trust = TrustFor(type.Assembly);
        if (_context.Locator(type.Assembly, trust) is not { } startPath || !File.Exists(startPath))
            return null;
        return LocateFrom(startPath, fullName, trust);
    }

    TypeLocation? LocateFrom(string startPath, string fullName, AssemblyTrust trust)
    {
        if (_context.Open(startPath) is { } assembly && assembly.TryGetType(fullName, out _))
            return new TypeLocation(startPath, fullName);
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

    static TypeRef? NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;

    static bool NeedsParameterRefKinds(MethodRef method)
    {
        if (method.ParameterRefKindsFacts != ParameterRefKindFacts.Unknown)
            return false;
        foreach (var parameter in method.ParameterTypes)
            if (parameter.Kind == TypeRefKind.ByRef)
                return true;
        return false;
    }

    static bool NeedsGeneratedFacts(MethodRef method)
        => (method.CompilerGenerated == MetadataFactState.Unknown
            || method.DeclaringTypeCompilerGenerated == MetadataFactState.Unknown)
            && LooksCompilerGenerated(method);

    static bool LooksCompilerGenerated(MethodRef method)
        => method.Name.Contains('<', StringComparison.Ordinal)
            || method.DeclaringType.Name.Contains('<', StringComparison.Ordinal)
            || method.DeclaringType.Name.Contains("__DisplayClass", StringComparison.Ordinal);

    static MetadataFactState FactState(bool value) => value ? MetadataFactState.Yes : MetadataFactState.No;

    readonly record struct ResolvedMethodFacts(
        ParameterRefKindResult ParameterRefKinds,
        bool RequiresUnsafe,
        MetadataFactState CompilerGenerated,
        MetadataFactState DeclaringTypeCompilerGenerated);
}
