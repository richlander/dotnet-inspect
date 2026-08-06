using System.Collections.Immutable;
using System.Collections.Concurrent;
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
/// located and its metadata confirms it. Anything unreachable (no resolver hit,
/// a forwarder dead-end, an I/O or format error) yields
/// <see cref="ValueTypeHint.Unknown"/> — never a guess. Security: a reference
/// whose public-key token is a trusted platform key is asserted
/// <see cref="AssemblyResolutionScope.Platform"/> so resolution is constrained to
/// platform/framework sources, never a confusable local copy.
///
/// Reading is delegated to a shared <see cref="MetadataContext"/>: each defining
/// assembly is opened once and indexed for O(1) lookup, so resolving N tokens
/// from the same assembly costs one open and one type-table pass, not N.
/// </remarks>
internal sealed class CrossAssemblyTypeResolver
{
    readonly ResolvedAssemblyReference _selfAssembly;
    readonly string _selfCanonical;
    readonly MetadataContext _context;
    readonly ConcurrentDictionary<TypeResolutionCoordinates, ValueTypeHint> _valueTypeCache = new();
    readonly ConcurrentDictionary<TypeResolutionCoordinates, MetadataFactState> _inlineArrayCache = new();
    readonly ConcurrentDictionary<TypeResolutionCoordinates, MetadataFactState> _byRefLikeCache = new();
    readonly ConcurrentDictionary<(MethodRef Method, TypeResolutionCoordinates Type), ResolvedMethodFacts?> _methodFactCache = new();
    readonly ConcurrentDictionary<(TypeRef Instance, TypeResolutionCoordinates Type, TypeRef Interface), MetadataFactState> _interfaceCache = new();

    public CrossAssemblyTypeResolver(
        MetadataReader selfReader,
        ResolvedAssemblyReference selfAssembly,
        MetadataContext context)
    {
        _selfAssembly = selfAssembly;
        // Same-assembly identity comparisons must use the same PKT-gated
        // canonicalization as GetTypeFromDefinition's own self path (issue
        // #3045): a plain name-only canonicalization would disagree
        // with a TypeRef.Assembly that GetTypeFromDefinition already refused to
        // canonicalize for an unsigned facade-named self, wrongly treating a
        // same-assembly type as cross-assembly (or vice versa).
        _selfCanonical = selfReader.IsAssembly ? TypeRefDecoder.CanonicalSelf(selfReader) : "";
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
        if (type.Assembly == _selfCanonical)
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

    public FieldRef Upgrade(FieldRef field)
    {
        if (field.DeclaringTypeCompilerGenerated == MetadataFactState.Unknown && field.DeclaringType is { Assembly: not null })
        {
            var dtType = NamedDefinition(field.DeclaringType);
            if (dtType is not null
                && dtType.Assembly != _selfCanonical
                && Locate(dtType) is { } definition
                && _context.Open(definition, out var handle) is { } assembly)
            {
                var typeDef = assembly.Reader.GetTypeDefinition(handle);
                field = field with { DeclaringTypeCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(assembly.Reader, typeDef.GetCustomAttributes()) ? MetadataFactState.Yes : MetadataFactState.No };
            }
        }
        if (field.BackingPropertyName is not null
            || CSharpNaming.BackingFieldProperty(field.Name) is null)
        {
            return field;
        }

        var type = NamedDefinition(field.DeclaringType);
        if (type is null || string.IsNullOrEmpty(type.Assembly))
            return field;
        if (type.Assembly == _selfCanonical)
            return field;

        return ResolveFieldBackingProperty(field, type) is { } property
            ? field with { BackingPropertyName = property }
            : field;
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
        if (type.Assembly == _selfCanonical)
            return callee;

        if (!TryCoordinates(type, out TypeResolutionCoordinates coordinates))
            return callee;
        var facts = _methodFactCache.GetOrAdd(
            (callee, coordinates),
            entry => ResolveMethodFacts(entry.Method, type));

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
        if (!TryCoordinates(type, out TypeResolutionCoordinates coordinates))
            return MetadataFactState.Unknown;
        var key = (type, coordinates, iface);
        if (_interfaceCache.TryGetValue(key, out var cached))
            return cached;

        var result = MetadataFactState.Unknown;
        try
        {
            if (NamedDefinition(type) is { } definition
                && !string.IsNullOrEmpty(definition.Assembly)
                && definition.Assembly != _selfCanonical
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
        var pending = new Stack<(TypeRef Type, ResolvedAssemblyReference? LocalAssembly)>();
        pending.Push((type, null));

        while (pending.Count > 0 && seen.Count < 256)
        {
            var (current, localAssembly) = pending.Pop();
            if (!seen.Add(current))
                continue;

            if (NamedDefinition(current) is not { } definition)
                continue;
            if (Locate(definition, localAssembly) is not { } resolved
                || _context.Open(resolved, out var handle) is not { } assembly)
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
                pending.Push((implemented, resolved.Assembly.Assembly));
            }

            if (DecodeBaseType(reader, typeDef, typeArguments) is { } baseType)
                pending.Push((baseType, resolved.Assembly.Assembly));
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
            if (Locate(type) is not { } definition
                || _context.Open(definition, out var handle) is not { } assembly)
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
                    || ScopeFor(type) == AssemblyResolutionScope.Platform;
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

    string? ResolveFieldBackingProperty(FieldRef field, TypeRef type)
    {
        try
        {
            if (Locate(type) is not { } definition
                || _context.Open(definition, out var handle) is not { } assembly)
                return null;

            var reader = assembly.Reader;
            var typeDef = reader.GetTypeDefinition(handle);
            var typeArguments = field.DeclaringType.Kind == TypeRefKind.GenericInstance
                ? field.DeclaringType.TypeArguments
                : [];
            var typeScope = new GenericScope(MethodDefinitionFacts.GenericParameterNames(reader, typeDef.GetGenericParameters()), []);
            bool allowCoreLibraryAliases = type.Assembly == TypeRef.CoreLibrary
                || ScopeFor(type) == AssemblyResolutionScope.Platform;

            foreach (var fieldHandle in typeDef.GetFields())
            {
                var candidate = reader.GetFieldDefinition(fieldHandle);
                if (!string.Equals(reader.GetString(candidate.Name), field.Name, StringComparison.Ordinal))
                    continue;

                var fieldType = GuardedDecode.FieldType(reader, candidate, typeScope).Instantiate(typeArguments, []);
                if (!SameSignatureType(fieldType, field.Type, allowCoreLibraryAliases))
                    continue;

                return BackingPropertyName(reader, typeDef, field.Name);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string? BackingPropertyName(MetadataReader reader, TypeDefinition declaringType, string fieldName)
    {
        if (CSharpNaming.BackingFieldProperty(fieldName) is not { } propertyName)
            return null;

        foreach (var propertyHandle in declaringType.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            if (string.Equals(reader.GetString(property.Name), propertyName, StringComparison.Ordinal))
                return propertyName;
        }

        return null;
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
        var signature = GuardedDecode.MethodSignature(reader, method, scope);
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

    /// <summary>
    /// The full <see cref="TypeShapeKind"/> of a cross-assembly type, read from
    /// its located definition (base type + interface flag). Mirrors
    /// <see cref="ReadValueTypeHint"/> but preserves the enum/delegate/interface
    /// distinctions the coarse <see cref="ValueTypeHint"/> folds away. Returns
    /// <see cref="TypeShapeKind.Unknown"/> when the definition cannot be located.
    /// </summary>
    public TypeShapeKind ClassifyShape(TypeRef type)
    {
        try
        {
            if (Locate(type) is not { } definition
                || _context.Open(definition, out var handle) is not { } assembly)
            {
                return TypeShapeKind.Unknown;
            }

            var typeDef = assembly.Reader.GetTypeDefinition(handle);
            if ((typeDef.Attributes & System.Reflection.TypeAttributes.Interface) != 0)
                return TypeShapeKind.Interface;

            // A struct's base is System.ValueType (System.Enum for an enum), a
            // delegate's is System.MulticastDelegate; a nil/TypeSpec base (object
            // or a generic class base) reads as null and is a reference class.
            return BaseTypeName(assembly.Reader, typeDef.BaseType) switch
            {
                "System.Enum" => TypeShapeKind.Enum,
                "System.ValueType" => TypeShapeKind.Struct,
                "System.MulticastDelegate" or "System.Delegate" => TypeShapeKind.Delegate,
                _ => TypeShapeKind.Class,
            };
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return TypeShapeKind.Unknown;
        }
    }

    ValueTypeHint ResolveValueTypeHint(TypeRef type)
    {
        if (!TryCoordinates(type, out TypeResolutionCoordinates key))
            return ValueTypeHint.Unknown;
        if (_valueTypeCache.TryGetValue(key, out var cached))
            return cached;

        var hint = ValueTypeHint.Unknown;
        try
        {
            if (Locate(type) is { } definition)
                hint = ReadValueTypeHint(definition);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }

        _valueTypeCache[key] = hint;
        return hint;
    }

    MetadataFactState ResolveInlineArrayFact(TypeRef type)
    {
        if (!TryCoordinates(type, out TypeResolutionCoordinates key))
            return MetadataFactState.Unknown;
        if (_inlineArrayCache.TryGetValue(key, out var cached))
            return cached;

        var fact = MetadataFactState.Unknown;
        try
        {
            if (Locate(type) is { } definition)
                fact = ReadInlineArrayFact(definition);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }

        _inlineArrayCache[key] = fact;
        return fact;
    }

    /// <summary>
    /// Whether a cross-assembly type carries <c>[IsByRefLike]</c> — a
    /// <c>ref struct</c> in a referenced assembly. Same-assembly ref structs are
    /// resolved directly from the inspected assembly's type definitions; this
    /// resolves the referenced-assembly case. <see cref="MetadataFactState.Unknown"/>
    /// when the defining assembly is outside the reference closure.
    /// </summary>
    public MetadataFactState IsByRefLike(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is null
            || !TryCoordinates(
                definition,
                out TypeResolutionCoordinates key))
            return MetadataFactState.Unknown;
        if (_byRefLikeCache.TryGetValue(key, out var cached))
            return cached;

        var fact = MetadataFactState.Unknown;
        try
        {
            if (Locate(definition) is { } resolved)
                fact = ReadByRefLikeFact(resolved);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }

        _byRefLikeCache[key] = fact;
        return fact;
    }

    ResolvedTypeDefinition? Locate(
        TypeRef type,
        ResolvedAssemblyReference? localAssembly = null)
    {
        MetadataTypeDefinitionName? definitionName = type.DefinitionName;
        AssemblyReferenceIdentity? resolutionAssembly = type.ResolutionAssembly;
        if (definitionName is null)
        {
            if (!TryResolutionIdentity(
                type,
                out definitionName,
                out resolutionAssembly))
            {
                return null;
            }
        }
        else if (type.Assembly != TypeRef.CoreLibrary
            && resolutionAssembly is null
            && localAssembly is null)
        {
            return null;
        }

        TypeResolutionRequest request;
        if (localAssembly is not null && resolutionAssembly is null)
        {
            request = TypeResolutionRequest.FromAssembly(
                localAssembly,
                ScopeFor(type),
                definitionName);
        }
        else if (type.Assembly == TypeRef.CoreLibrary)
        {
            return _context.ResolveCoreLibraryDefinition(
                _selfAssembly,
                definitionName);
        }
        else
        {
            if (resolutionAssembly is not { } identity)
                return null;
            request = TypeResolutionRequest.FromReference(
                identity,
                AssemblyBindingOrigin.FromAssembly(_selfAssembly),
                ScopeFor(type),
                definitionName);
        }

        TypeResolutionOutcome outcome =
            _context.Resolve(_selfAssembly, request);
        return outcome is TypeResolutionOutcome.Resolved resolved
            ? resolved.Definition
            : null;
    }

    static AssemblyResolutionScope ScopeFor(TypeRef type) =>
        type.ResolutionAssembly is { } identity
            && PlatformKeys.IsPlatform(identity.PublicKeyToken)
                ? AssemblyResolutionScope.Platform
                : AssemblyResolutionScope.Any;

    ValueTypeHint ReadValueTypeHint(ResolvedTypeDefinition definition)
    {
        if (_context.Open(definition, out var handle) is not { } assembly)
            return ValueTypeHint.Unknown;

        var typeDef = assembly.Reader.GetTypeDefinition(handle);
        // A struct's immediate base is System.ValueType (System.Enum for an
        // enum); anything else (or no base) is a reference type.
        string? baseName = BaseTypeName(assembly.Reader, typeDef.BaseType);
        return baseName is "System.ValueType" or "System.Enum"
            ? ValueTypeHint.ValueType
            : ValueTypeHint.ReferenceType;
    }

    MetadataFactState ReadInlineArrayFact(
        ResolvedTypeDefinition definition)
    {
        if (_context.Open(definition, out var handle) is not { } assembly)
            return MetadataFactState.Unknown;

        var typeDef = assembly.Reader.GetTypeDefinition(handle);
        return MethodDefinitionFacts.HasInlineArrayAttribute(assembly.Reader, typeDef)
            ? MetadataFactState.Yes
            : MetadataFactState.No;
    }

    MetadataFactState ReadByRefLikeFact(
        ResolvedTypeDefinition definition)
    {
        if (_context.Open(definition, out var handle) is not { } assembly)
            return MetadataFactState.Unknown;

        var typeDef = assembly.Reader.GetTypeDefinition(handle);
        return MethodDefinitionFacts.HasByRefLikeAttribute(assembly.Reader, typeDef)
            ? MetadataFactState.Yes
            : MetadataFactState.No;
    }

    static string? BaseTypeName(MetadataReader reader, EntityHandle baseType) => baseType.Kind switch
    {
        HandleKind.TypeReference => reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)baseType)),
        HandleKind.TypeDefinition => reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)baseType)),
        _ => null,
    };

    static TypeRef? NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;

    static bool TryCoordinates(
        TypeRef type,
        out TypeResolutionCoordinates coordinates)
    {
        if (NamedDefinition(type) is not { } definition
            || !TryResolutionIdentity(
                definition,
                out MetadataTypeDefinitionName definitionName,
                out AssemblyReferenceIdentity? resolutionAssembly))
        {
            coordinates = default;
            return false;
        }

        coordinates = new TypeResolutionCoordinates(
            definition.Assembly == TypeRef.CoreLibrary,
            resolutionAssembly,
            definitionName);
        return true;
    }

    static bool TryResolutionIdentity(
        TypeRef type,
        out MetadataTypeDefinitionName definitionName,
        out AssemblyReferenceIdentity? resolutionAssembly)
    {
        if (type.DefinitionName is { } structuredName)
        {
            definitionName = structuredName;
            resolutionAssembly = type.ResolutionAssembly;
            return type.Assembly == TypeRef.CoreLibrary || resolutionAssembly is not null;
        }

        // Publicly constructed TypeRefs predate structured resolution identity.
        // Preserve their top-level compatibility without parsing '+' into a
        // nesting relationship that the model cannot prove.
        if (type.Kind != TypeRefKind.Definition
            || string.IsNullOrEmpty(type.Assembly)
            || type.Name.Contains('+', StringComparison.Ordinal)
            || MetadataTypeDefinitionName.Create(type.Namespace, [type.Name])
                is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            definitionName = null!;
            resolutionAssembly = null;
            return false;
        }

        definitionName = valid.Name;
        resolutionAssembly = type.Assembly == TypeRef.CoreLibrary
            ? null
            : new AssemblyReferenceIdentity(
                type.Assembly,
                Version: null,
                Culture: null,
                PublicKeyToken: null);
        return true;
    }

    readonly record struct TypeResolutionCoordinates(
        bool IsCoreLibrary,
        AssemblyReferenceIdentity? Assembly,
        MetadataTypeDefinitionName Type);

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
