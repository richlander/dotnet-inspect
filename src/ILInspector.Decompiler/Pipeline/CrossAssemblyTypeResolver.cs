using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers facts about a type defined in another assembly that the importing
/// assembly's metadata cannot state on its own. A bare type token (a
/// <c>newobj</c> target, a <c>box</c>/<c>sizeof</c> operand) carries no
/// <c>VALUETYPE</c>/<c>CLASS</c> byte, so <see cref="TypeRef.ValueTypeHint"/> is
/// <see cref="ValueTypeHint.Unknown"/> for cross-assembly references; direct
/// inline-array span conversion raising also needs <c>[InlineArray]</c> evidence,
/// and collection initializer raising needs interface evidence from the defining
/// assembly. This resolver locates that assembly and reads the answer from its
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
    readonly Dictionary<TypeRef, MetadataFactState> _inlineArrayCache = [];
    readonly Dictionary<MethodRef, ResolvedMethodFacts?> _methodFactCache = [];
    readonly Dictionary<(TypeRef Type, TypeRef Interface), MetadataFactState> _interfaceCache = [];

    public CrossAssemblyTypeResolver(string selfSimpleName, MetadataReader selfReader, MetadataContext context)
    {
        _selfSimpleName = selfSimpleName;
        _selfReader = selfReader;
        _context = context;
    }

    /// <summary>
    /// Returns <paramref name="type"/> with cross-assembly type facts stamped
    /// when this resolver can confirm them from the defining assembly; returns the
    /// type unchanged when it already carries the needed facts, is not a
    /// cross-assembly named definition, or cannot be resolved.
    /// </summary>
    public TypeRef Upgrade(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
            return type;
        if (string.IsNullOrEmpty(type.Assembly))
            return type;
        // Same-assembly references are resolved by the importer's own shapes.
        if (type.Assembly == TypeRefDecoder.Canonical(_selfSimpleName))
            return type;

        var result = type;
        if (result.ValueTypeHint == ValueTypeHint.Unknown)
        {
            var hint = ResolveValueTypeHint(type);
            if (hint != ValueTypeHint.Unknown)
                result = result.WithValueTypeHint(hint);
        }
        if (result.InlineArray == MetadataFactState.Unknown)
        {
            var inlineArray = ResolveInlineArrayFact(type);
            if (inlineArray != MetadataFactState.Unknown)
                result = result.WithInlineArrayFact(inlineArray);
        }
        return result;
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
        callee = UpgradeTypeReferences(callee);
        bool needsRefKinds = NeedsParameterRefKinds(callee);
        bool needsGenerated = NeedsGeneratedFacts(callee);
        bool needsUnsafe = resolveRequiresUnsafe && !callee.RequiresUnsafe;
        bool needsExtension = NeedsExtensionFacts(callee);
        bool needsDelegate = NeedsDelegateFact(callee);
        bool needsOperator = NeedsOperatorFact(callee);
        bool needsAccessor = NeedsAccessorFact(callee);
        if (!needsRefKinds && !needsGenerated && !needsUnsafe && !needsExtension && !needsDelegate && !needsOperator && !needsAccessor)
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
            DeclaringTypeIsDelegate = needsDelegate ? resolved.DeclaringTypeIsDelegate : callee.DeclaringTypeIsDelegate,
            IsExtension = needsExtension ? resolved.IsExtension : callee.IsExtension,
            IsOperator = needsOperator ? resolved.IsOperator : callee.IsOperator,
            AccessorKind = needsAccessor ? resolved.AccessorKind : callee.AccessorKind,
        };
    }

    MethodRef UpgradeTypeReferences(MethodRef method)
        => method with
        {
            DeclaringType = UpgradeTypeReference(method.DeclaringType),
            ReturnType = UpgradeTypeReference(method.ReturnType),
            ParameterTypes = [.. method.ParameterTypes.Select(UpgradeTypeReference)],
            TypeArguments = [.. method.TypeArguments.Select(UpgradeTypeReference)],
        };

    TypeRef UpgradeTypeReference(TypeRef type) => type.Kind switch
    {
        TypeRefKind.Definition => Upgrade(type),
        TypeRefKind.GenericInstance => TypeRef.GenericInstance(
            UpgradeTypeReference(type.ElementType!),
            [.. type.TypeArguments.Select(UpgradeTypeReference)]),
        TypeRefKind.SzArray => TypeRef.SzArray(UpgradeTypeReference(type.ElementType!)),
        TypeRefKind.Array => TypeRef.MdArray(UpgradeTypeReference(type.ElementType!), type.Rank),
        TypeRefKind.ByRef => TypeRef.ByRef(UpgradeTypeReference(type.ElementType!)),
        TypeRefKind.Pointer => TypeRef.Pointer(UpgradeTypeReference(type.ElementType!)),
        TypeRefKind.Pinned => TypeRef.Pinned(UpgradeTypeReference(type.ElementType!)),
        TypeRefKind.FunctionPointer => TypeRef.FunctionPointer(
            UpgradeTypeReference(type.ElementType!),
            [.. type.TypeArguments.Select(UpgradeTypeReference)],
            type.CallingConvention),
        _ => type,
    };

    /// <summary>
    /// Returns whether a cross-assembly type implements an interface, resolving
    /// base classes and base interfaces through the shared metadata context.
    /// Unreachable metadata returns <see cref="MetadataFactState.Unknown"/>;
    /// absence is reported as <see cref="MetadataFactState.No"/> only after the
    /// reachable hierarchy has been walked.
    /// </summary>
    public MetadataFactState Implements(TypeRef type, TypeRef iface)
    {
        var key = (type, iface);
        if (_interfaceCache.TryGetValue(key, out var cached))
            return cached;

        var result = MetadataFactState.Unknown;
        try
        {
            if (NamedDefinition(type) is { } definition
                && !string.IsNullOrEmpty(definition.Assembly)
                && definition.Assembly != TypeRefDecoder.Canonical(_selfSimpleName)
                && TryImplements(type, iface, out var implements))
            {
                result = implements ? MetadataFactState.Yes : MetadataFactState.No;
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            result = MetadataFactState.Unknown;
        }

        _interfaceCache[key] = result;
        return result;
    }

    bool TryImplements(TypeRef type, TypeRef iface, out bool implements)
    {
        implements = false;
        bool unresolved = false;
        var seen = new HashSet<TypeRef>();
        var pending = new Stack<TypeRef>();
        pending.Push(type);

        while (pending.Count > 0 && seen.Count < 256)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;

            if (NamedDefinition(current) is not { } definition)
                continue;
            if (Locate(definition) is not { } location
                || _context.Open(location.AssemblyPath) is not { } assembly
                || !assembly.TryGetType(location.FullTypeName, out var handle))
            {
                unresolved = true;
                continue;
            }

            var reader = assembly.Reader;
            var typeDef = reader.GetTypeDefinition(handle);
            var typeArguments = current.Kind == TypeRefKind.GenericInstance ? current.TypeArguments : [];
            foreach (var implemented in DecodeInterfaces(reader, typeDef, typeArguments))
            {
                if (implemented.Equals(iface))
                {
                    implements = true;
                    return true;
                }
                pending.Push(implemented);
            }

            if (DecodeBaseType(reader, typeDef, typeArguments) is { } baseType)
                pending.Push(baseType);
        }

        return !unresolved;
    }

    static IEnumerable<TypeRef> DecodeInterfaces(MetadataReader reader, TypeDefinition typeDef, ImmutableArray<TypeRef> typeArguments)
    {
        var scope = new GenericScope(MethodDefinitionFacts.GenericParameterNames(reader, typeDef.GetGenericParameters()), []);
        foreach (var implHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(implHandle).Interface;
            if (DecodeType(reader, iface, scope) is { } decoded)
                yield return decoded.Instantiate(typeArguments, []);
        }
    }

    static TypeRef? DecodeBaseType(MetadataReader reader, TypeDefinition typeDef, ImmutableArray<TypeRef> typeArguments)
    {
        var scope = new GenericScope(MethodDefinitionFacts.GenericParameterNames(reader, typeDef.GetGenericParameters()), []);
        return DecodeType(reader, typeDef.BaseType, scope)?.Instantiate(typeArguments, []);
    }

    static TypeRef? DecodeType(MetadataReader reader, EntityHandle handle, GenericScope scope)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, scope, (TypeSpecificationHandle)handle, 0),
            _ => null,
        };

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
                bool allowCoreLibraryAliases = type.Assembly == TypeRef.CoreLibrary
                    || TrustFor(type.Assembly) == AssemblyTrust.Platform;
                if (!TryMatchMethod(reader, typeDef, method, callee, allowCoreLibraryAliases, out var parameterRefKinds))
                    continue;

                bool methodCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, method.GetCustomAttributes());
                return new ResolvedMethodFacts(
                    parameterRefKinds,
                    typeRequiresUnsafe || MethodDefinitionFacts.HasRequiresUnsafeAttribute(reader, method),
                    FactState(methodCompilerGenerated),
                    FactState(typeCompilerGenerated),
                    FactState(IsDelegateType(reader, typeDef)),
                    FactState(MethodDefinitionFacts.HasExtensionAttribute(reader, method)),
                    FactState(MethodDefinitionFacts.IsOperator(method, callee.Name, callee.HasThis)),
                    MethodDefinitionFacts.ReadAccessorKind(reader, typeDef, methodHandle));
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
        bool allowCoreLibraryAliases,
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
        if (!SameSignatureType(returnType, callee.ReturnType, allowCoreLibraryAliases))
            return false;

        if (signature.ParameterTypes.Length != callee.ParameterTypes.Length)
            return false;
        var parameters = ImmutableArray.CreateBuilder<TypeRef>(signature.ParameterTypes.Length);
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var parameter = signature.ParameterTypes[i].Instantiate(typeArguments, methodArguments);
            if (!SameSignatureType(parameter, callee.ParameterTypes[i], allowCoreLibraryAliases))
                return false;
            parameters.Add(parameter);
        }

        parameterRefKinds = MethodDefinitionFacts.ReadParameterRefKinds(reader, method, parameters.MoveToImmutable());
        return true;
    }

    static bool SameSignatureType(TypeRef resolved, TypeRef expected, bool allowCoreLibraryAliases)
    {
        if (resolved.Equals(expected))
            return true;
        if (resolved.Kind != expected.Kind)
            return false;

        switch (resolved.Kind)
        {
            case TypeRefKind.Definition:
                return resolved.Namespace == expected.Namespace
                    && resolved.Name == expected.Name
                    && (resolved.Assembly == expected.Assembly
                        || (allowCoreLibraryAliases
                            && (resolved.Assembly == TypeRef.CoreLibrary
                                || expected.Assembly == TypeRef.CoreLibrary)));
            case TypeRefKind.GenericInstance:
                if (!SameSignatureType(resolved.ElementType!, expected.ElementType!, allowCoreLibraryAliases)
                    || resolved.TypeArguments.Length != expected.TypeArguments.Length)
                    return false;
                for (int i = 0; i < resolved.TypeArguments.Length; i++)
                    if (!SameSignatureType(resolved.TypeArguments[i], expected.TypeArguments[i], allowCoreLibraryAliases))
                        return false;
                return true;
            case TypeRefKind.SzArray or TypeRefKind.Pointer or TypeRefKind.Pinned or TypeRefKind.ByRef:
                return SameSignatureType(resolved.ElementType!, expected.ElementType!, allowCoreLibraryAliases);
            case TypeRefKind.Array:
                return resolved.Rank == expected.Rank
                    && SameSignatureType(resolved.ElementType!, expected.ElementType!, allowCoreLibraryAliases);
            case TypeRefKind.FunctionPointer:
                if (resolved.CallingConvention != expected.CallingConvention
                    || !SameSignatureType(resolved.ElementType!, expected.ElementType!, allowCoreLibraryAliases)
                    || resolved.TypeArguments.Length != expected.TypeArguments.Length)
                    return false;
                for (int i = 0; i < resolved.TypeArguments.Length; i++)
                    if (!SameSignatureType(resolved.TypeArguments[i], expected.TypeArguments[i], allowCoreLibraryAliases))
                        return false;
                return true;
            default:
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

    MetadataFactState ResolveInlineArrayFact(TypeRef type)
    {
        if (_inlineArrayCache.TryGetValue(type, out var cached))
            return cached;

        var fact = MetadataFactState.Unknown;
        try
        {
            if (Locate(type) is { } location)
                fact = ReadInlineArrayFact(location);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }

        _inlineArrayCache[type] = fact;
        return fact;
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

    MetadataFactState ReadInlineArrayFact(TypeLocation location)
    {
        if (_context.Open(location.AssemblyPath) is not { } assembly)
            return MetadataFactState.Unknown;
        if (!assembly.TryGetType(location.FullTypeName, out var handle))
            return MetadataFactState.Unknown;

        var typeDef = assembly.Reader.GetTypeDefinition(handle);
        return MethodDefinitionFacts.HasInlineArrayAttribute(assembly.Reader, typeDef)
            ? MetadataFactState.Yes
            : MetadataFactState.No;
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

    // An extension method is always static with at least one parameter (the
    // receiver). That structural pre-filter keeps the cross-assembly resolution
    // off instance calls and parameterless statics, the bulk of call sites.
    static bool NeedsExtensionFacts(MethodRef method)
        => method.IsExtension == MetadataFactState.Unknown
            && !method.HasThis
            && method.ParameterTypes.Length >= 1;

    static bool NeedsDelegateFact(MethodRef method)
        => method.DeclaringTypeIsDelegate == MetadataFactState.Unknown
            && method.Name == ".ctor"
            && method.HasThis
            && method.ParameterTypes.Length == 2
            && method.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Object"))
            && method.ParameterTypes[1].Equals(TypeRef.CoreLib("System", "IntPtr"));

    static bool NeedsOperatorFact(MethodRef method)
        => method.IsOperator == MetadataFactState.Unknown
            && !method.HasThis
            && method.Name.StartsWith("op_", StringComparison.Ordinal);

    static bool NeedsAccessorFact(MethodRef method)
        => method.AccessorKind == AccessorKind.Unknown
            && (method.Name.StartsWith("get_", StringComparison.Ordinal)
                || method.Name.StartsWith("set_", StringComparison.Ordinal)
                || method.Name.StartsWith("add_", StringComparison.Ordinal)
                || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    static bool IsDelegateType(MetadataReader reader, TypeDefinition typeDef)
    {
        try { return BaseTypeName(reader, typeDef.BaseType) is "System.MulticastDelegate"; }
        catch (BadImageFormatException) { return false; }
    }

    static MetadataFactState FactState(bool value) => value ? MetadataFactState.Yes : MetadataFactState.No;

    readonly record struct ResolvedMethodFacts(
        ParameterRefKindResult ParameterRefKinds,
        bool RequiresUnsafe,
        MetadataFactState CompilerGenerated,
        MetadataFactState DeclaringTypeCompilerGenerated,
        MetadataFactState DeclaringTypeIsDelegate,
        MetadataFactState IsExtension,
        MetadataFactState IsOperator,
        AccessorKind AccessorKind);
}
