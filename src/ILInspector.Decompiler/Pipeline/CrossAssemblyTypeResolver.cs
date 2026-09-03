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
    readonly ConcurrentDictionary<TypeResolutionCoordinates, TypeShapeKind> _shapeCache = new();
    readonly ConcurrentDictionary<TypeResolutionCoordinates, MetadataFactState> _inlineArrayCache = new();
    readonly ConcurrentDictionary<TypeResolutionCoordinates, MetadataFactState> _byRefLikeCache = new();
    readonly ConcurrentDictionary<(FieldRef Field, TypeResolutionCoordinates Type), ResolvedFieldFacts?> _fieldFactCache = new();
    readonly ConcurrentDictionary<(MethodFactCacheIdentity Method, TypeResolutionCoordinates Type), ResolvedMethodFacts?> _methodFactCache = new();
    readonly ConcurrentDictionary<(TypeRef Instance, TypeResolutionCoordinates Type, TypeRef Interface, AssemblyReferenceIdentity? InterfaceAssembly), MetadataFactState> _interfaceCache = new();
    readonly ConcurrentDictionary<(TypeResolutionCoordinates Type, string MethodName), MetadataFactState> _operatorHierarchyCache = new();

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
        if (IsSelf(type))
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
                && !IsSelf(dtType)
                && Locate(dtType) is { } definition
                && _context.Open(definition, out var handle) is { } assembly)
            {
                var typeDef = assembly.Reader.GetTypeDefinition(handle);
                field = field with { DeclaringTypeCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(assembly.Reader, typeDef.GetCustomAttributes()) ? MetadataFactState.Yes : MetadataFactState.No };
            }
        }

        bool needsDynamic = field.DynamicFact == MetadataFactState.Unknown
            && IsSystemObject(MethodDefinitionFacts.DynamicValueType(field.Type));
        bool needsArrayElementDynamic = field.ArrayElementIsDynamic == MetadataFactState.Unknown
            && IsObjectArray(field.Type);
        bool needsBackingProperty = field.BackingPropertyName is null
            && CSharpNaming.BackingFieldProperty(field.Name) is not null;
        if (!needsDynamic && !needsArrayElementDynamic && !needsBackingProperty)
            return field;

        var type = NamedDefinition(field.DeclaringType);
        if (type is null || string.IsNullOrEmpty(type.Assembly))
            return field;
        if (IsSelf(type))
            return field;

        if (!TryCoordinates(type, out TypeResolutionCoordinates coordinates))
            return field;
        var facts = _fieldFactCache.GetOrAdd(
            (field, coordinates),
            entry => ResolveFieldFacts(entry.Field, type));
        if (facts is not { } resolved)
            return field;

        return field with
        {
            BackingPropertyName = needsBackingProperty && resolved.BackingPropertyName is { } property
                ? property
                : field.BackingPropertyName,
            IsDynamic = needsDynamic && resolved.DynamicFact == MetadataFactState.Yes
                || field.IsDynamic,
            DynamicFact = needsDynamic ? resolved.DynamicFact : field.DynamicFact,
            ArrayElementIsDynamic = needsArrayElementDynamic
                ? resolved.ArrayElementIsDynamic
                : field.ArrayElementIsDynamic,
        };
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
        bool needsUnsafe = resolveRequiresUnsafe
            && callee.RequiresUnsafeFact == MetadataFactState.Unknown
            && !callee.MemorySafetyContractUnavailable;
        bool needsMemorySafety =
            callee.MemorySafetyRulesState is null
            && !callee.MemorySafetyRulesUnavailable;
        bool needsExtension = NeedsExtensionFacts(callee);
        bool needsDelegate = NeedsDelegateFact(callee);
        bool needsOperator = NeedsOperatorFact(callee);
        bool needsAccessor = NeedsAccessorFact(callee);
        bool needsReturnDynamic = NeedsReturnDynamicFact(callee);
        bool needsReturnArrayElementDynamic = NeedsReturnArrayElementDynamicFact(callee);
        if (!needsRefKinds && !needsGenerated && !needsUnsafe && !needsMemorySafety
            && !needsExtension && !needsDelegate
            && !needsOperator && !needsAccessor && !needsReturnDynamic
            && !needsReturnArrayElementDynamic)
            return callee;

        var type = NamedDefinition(callee.DeclaringType);
        if (type is null || string.IsNullOrEmpty(type.Assembly))
            return callee;
        // Same-assembly callees are stamped by the importer from their MethodDef.
        if (IsSelf(type))
            return callee;

        if (!TryCoordinates(type, out TypeResolutionCoordinates coordinates))
            return callee;
        var facts = _methodFactCache.GetOrAdd(
            (new MethodFactCacheIdentity(callee), coordinates),
            entry => ResolveMethodFacts(entry.Method.Method, type));

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
            HasRefReadOnlyParameters = needsRefKinds && resolved.ParameterRefKinds.State != ParameterRefKindFacts.Unknown
                ? resolved.ParameterRefKinds.HasRefReadOnlyParameters
                : callee.HasRefReadOnlyParameters,
            RequiresUnsafe = callee.RequiresUnsafe
                || needsUnsafe && resolved.RequiresUnsafe,
            RequiresUnsafeFact = callee.RequiresUnsafeFact == MetadataFactState.Unknown
                && needsUnsafe
                    ? resolved.RequiresUnsafeFact
                    : callee.RequiresUnsafeFact,
            MemorySafetyRulesState = needsUnsafe || needsMemorySafety
                ? resolved.MemorySafetyRulesState
                : callee.MemorySafetyRulesState,
            MemorySafetyRulesUnavailable = needsUnsafe || needsMemorySafety
                ? resolved.MemorySafetyRulesUnavailable
                : callee.MemorySafetyRulesUnavailable,
            MemorySafetyContractUnavailable = needsUnsafe || needsMemorySafety
                ? resolved.MemorySafetyContractUnavailable
                : callee.MemorySafetyContractUnavailable,
            ReturnIsDynamic = needsReturnDynamic ? resolved.ReturnIsDynamic : callee.ReturnIsDynamic,
            ReturnArrayElementIsDynamic = needsReturnArrayElementDynamic
                ? resolved.ReturnArrayElementIsDynamic
                : callee.ReturnArrayElementIsDynamic,
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

    internal TypeRef UpgradeTypeReference(TypeRef type) => type.Kind switch
    {
        TypeRefKind.Definition => Upgrade(type),
        TypeRefKind.GenericInstance => type.WithComponents(
            UpgradeTypeReference(type.ElementType!),
            [.. type.TypeArguments.Select(UpgradeTypeReference)]),
        TypeRefKind.SzArray
            or TypeRefKind.Array
            or TypeRefKind.ByRef
            or TypeRefKind.Pointer
            or TypeRefKind.Pinned => type.WithComponents(
                UpgradeTypeReference(type.ElementType!)),
        TypeRefKind.FunctionPointer => type.WithComponents(
            UpgradeTypeReference(type.ElementType!),
            [.. type.TypeArguments.Select(UpgradeTypeReference)]),
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
        // Keyed on the definition's resolution assembly, not the presented
        // type's: a generic instance carries its provenance on the element
        // type, so keying on the instance records null for every version and
        // hands the first query's answer to the second.
        var key = (
            type,
            coordinates,
            iface,
            NamedDefinition(iface)?.ResolutionAssembly);
        if (_interfaceCache.TryGetValue(key, out var cached))
            return cached;

        var result = MetadataFactState.Unknown;
        try
        {
            if (NamedDefinition(type) is { } definition
                && !string.IsNullOrEmpty(definition.Assembly)
                && !IsSelf(definition)
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

    /// <summary>
    /// Whether the C# operator-binding hierarchy for a referenced type declares
    /// the requested special-name operator. Class types walk their base chain;
    /// interface types walk their base interfaces. Unreachable metadata returns
    /// <see cref="MetadataFactState.Unknown"/>.
    /// </summary>
    /// <remarks>
    /// Gated by <c>BoxedReferenceEqualityTests</c>' cross-assembly
    /// operator-bearing and operator-free hierarchy cases.
    /// </remarks>
    public MetadataFactState HasOperatorInBindingHierarchy(TypeRef type, string methodName)
    {
        var definition = NamedDefinition(type);
        if (definition is null
            || !TryCoordinates(definition, out TypeResolutionCoordinates coordinates))
        {
            return MetadataFactState.Unknown;
        }

        var key = (coordinates, methodName);
        if (_operatorHierarchyCache.TryGetValue(key, out var cached))
            return cached;

        var result = MetadataFactState.Unknown;
        try
        {
            if (!IsSelf(definition)
                && TryHasOperatorInBindingHierarchy(type, methodName, out bool hasOperator))
            {
                result = hasOperator ? MetadataFactState.Yes : MetadataFactState.No;
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            result = MetadataFactState.Unknown;
        }

        _operatorHierarchyCache[key] = result;
        return result;
    }

    /// <summary>
    /// Whether reducing an explicit static extension call to instance syntax
    /// would expose a same-named member in the receiver's binding hierarchy.
    /// Static spelling is retained for that conservative conflict; deciding
    /// whether C# overload resolution would select a particular method is
    /// intentionally outside this metadata query.
    /// </summary>
    /// <remarks>
    /// Gated by the method, property, generic, and platform-hierarchy conflict
    /// cases in <c>ExtensionMethodCallTests</c>.
    /// </remarks>
    public MetadataFactState ExtensionSyntaxConflict(
        TypeRef receiverType,
        MethodRef extension)
    {
        if (extension.IsExtension != MetadataFactState.Yes
            || extension.ParameterTypes.Length == 0)
        {
            return MetadataFactState.Unknown;
        }
        receiverType = ExtensionBindingReceiver(receiverType);
        if (receiverType.Kind is TypeRefKind.GenericParameter
            or TypeRefKind.MethodGenericParameter)
        {
            return MetadataFactState.Yes;
        }

        try
        {
            return TryFindConflictingMember(
                receiverType,
                extension.Name,
                out bool found)
                    ? found
                        ? MetadataFactState.Yes
                        : MetadataFactState.No
                    : MetadataFactState.Unknown;
        }
        catch (Exception ex) when (ex is IOException
            or BadImageFormatException
            or UnauthorizedAccessException)
        {
            return MetadataFactState.Unknown;
        }
    }

    bool TryFindConflictingMember(
        TypeRef receiverType,
        string memberName,
        out bool found)
    {
        found = false;
        bool unresolved = false;
        int remainingWork = OperatorHierarchyLimits.WorkItems;
        var seen = new HashSet<TypeDefinitionIdentity>();
        var pending =
            new Stack<(TypeRef Type, ResolvedAssemblyReference? LocalAssembly)>();
        pending.Push((
            receiverType,
            NamedDefinition(receiverType) is { } receiverDefinition
                && IsSelf(receiverDefinition)
                    ? _selfAssembly
                    : null));

        while (pending.Count > 0
            && seen.Count < OperatorHierarchyLimits.Types
            && remainingWork-- > 0)
        {
            var (current, localAssembly) = pending.Pop();
            if (NamedDefinition(current) is not { } definition
                || Locate(definition, localAssembly) is not { } resolved
                || _context.Open(resolved, out var handle) is not { } assembly)
            {
                unresolved = true;
                continue;
            }
            if (!seen.Add(ResolvedIdentity(definition, resolved)))
                continue;

            var reader = assembly.Reader;
            var typeDef = reader.GetTypeDefinition(handle);
            var typeArguments = current.Kind == TypeRefKind.GenericInstance
                ? current.TypeArguments
                : [];
            if (HasNamedMember(
                reader,
                typeDef,
                memberName,
                ref remainingWork,
                out bool budgetExhausted))
            {
                found = true;
                return true;
            }
            if (budgetExhausted)
            {
                unresolved = true;
                break;
            }

            bool isInterface = (typeDef.Attributes
                & System.Reflection.TypeAttributes.Interface) != 0;
            var interfaces = typeDef.GetInterfaceImplementations();
            var hierarchyScope = new GenericScope([], []);
            if ((!typeDef.BaseType.IsNil || interfaces.Count > 0)
                && !TryCreateHierarchyScope(
                    typeDef.GetGenericParameters(),
                    ref remainingWork,
                    out hierarchyScope))
            {
                unresolved = true;
                break;
            }
            if (!typeDef.BaseType.IsNil)
            {
                if (remainingWork-- <= 0)
                {
                    unresolved = true;
                    break;
                }
                if (DecodeType(
                    reader,
                    typeDef.BaseType,
                    hierarchyScope) is { } openBaseType)
                {
                    pending.Push((
                        openBaseType.Instantiate(typeArguments, []),
                        resolved.Assembly.Assembly));
                }
                else
                {
                    unresolved = true;
                }
            }
            if (isInterface)
            {
                pending.Push((
                    TypeRef.CoreLib("System", "Object"),
                    null));
                foreach (var implHandle in interfaces)
                {
                    if (remainingWork-- <= 0)
                    {
                        unresolved = true;
                        break;
                    }
                    var implementation =
                        reader.GetInterfaceImplementation(implHandle);
                    if (DecodeType(
                        reader,
                        implementation.Interface,
                        hierarchyScope) is not { } openInterface)
                    {
                        unresolved = true;
                        continue;
                    }
                    pending.Push((
                        openInterface.Instantiate(typeArguments, []),
                        resolved.Assembly.Assembly));
                }
            }
        }

        return !unresolved && pending.Count == 0;
    }

    static TypeRef ExtensionBindingReceiver(TypeRef type)
    {
        while (type is
            {
                    Kind: TypeRefKind.ByRef
                        or TypeRefKind.Pointer
                        or TypeRefKind.Pinned,
                    ElementType: { } element,
                })
        {
            type = element;
        }

        return type.Kind is TypeRefKind.SzArray or TypeRefKind.Array
            ? TypeRef.CoreLib("System", "Array")
            : type;
    }

    static bool HasNamedMember(
        MetadataReader reader,
        TypeDefinition type,
        string memberName,
        ref int remainingWork,
        out bool budgetExhausted)
    {
        budgetExhausted = false;

        foreach (var methodHandle in type.GetMethods())
        {
            if (remainingWork-- <= 0)
            {
                budgetExhausted = true;
                return false;
            }
            if (reader.StringComparer.Equals(
                reader.GetMethodDefinition(methodHandle).Name,
                memberName))
            {
                return true;
            }
        }
        foreach (var propertyHandle in type.GetProperties())
        {
            if (remainingWork-- <= 0)
            {
                budgetExhausted = true;
                return false;
            }
            if (reader.StringComparer.Equals(
                reader.GetPropertyDefinition(propertyHandle).Name,
                memberName))
            {
                return true;
            }
        }
        foreach (var fieldHandle in type.GetFields())
        {
            if (remainingWork-- <= 0)
            {
                budgetExhausted = true;
                return false;
            }
            if (reader.StringComparer.Equals(
                reader.GetFieldDefinition(fieldHandle).Name,
                memberName))
            {
                return true;
            }
        }
        foreach (var eventHandle in type.GetEvents())
        {
            if (remainingWork-- <= 0)
            {
                budgetExhausted = true;
                return false;
            }
            if (reader.StringComparer.Equals(
                reader.GetEventDefinition(eventHandle).Name,
                memberName))
            {
                return true;
            }
        }

        return false;
    }

    bool TryHasOperatorInBindingHierarchy(TypeRef type, string methodName, out bool hasOperator)
    {
        hasOperator = false;
        bool unresolved = false;
        int remainingWork = OperatorHierarchyLimits.WorkItems;
        var seen = new HashSet<TypeDefinitionIdentity>();
        var pending = new Stack<(TypeRef Type, ResolvedAssemblyReference? LocalAssembly)>();
        pending.Push((type, null));

        while (pending.Count > 0
            && seen.Count < OperatorHierarchyLimits.Types
            && remainingWork-- > 0)
        {
            var (current, localAssembly) = pending.Pop();
            if (NamedDefinition(current) is not { } definition)
            {
                unresolved = true;
                continue;
            }
            if (Locate(definition, localAssembly) is not { } resolved)
            {
                unresolved = true;
                continue;
            }
            if (!seen.Add(ResolvedIdentity(definition, resolved)))
                continue;
            if (_context.Open(resolved, out var handle) is not { } assembly)
            {
                unresolved = true;
                continue;
            }

            var reader = assembly.Reader;
            var typeDef = reader.GetTypeDefinition(handle);
            bool budgetExhausted = false;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                if (remainingWork-- <= 0)
                {
                    unresolved = true;
                    budgetExhausted = true;
                    break;
                }
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.StringComparer.Equals(method.Name, methodName)
                    && MethodDefinitionFacts.IsOperator(
                        method,
                        methodName,
                        hasThis: (method.Attributes & System.Reflection.MethodAttributes.Static) == 0))
                {
                    hasOperator = true;
                    return true;
                }
            }
            if (budgetExhausted)
                break;

            var typeArguments = current.Kind == TypeRefKind.GenericInstance ? current.TypeArguments : [];
            if (!TryCreateHierarchyScope(
                typeDef.GetGenericParameters(),
                ref remainingWork,
                out var scope))
            {
                unresolved = true;
                break;
            }
            if ((typeDef.Attributes & System.Reflection.TypeAttributes.Interface) != 0)
            {
                foreach (var implHandle in typeDef.GetInterfaceImplementations())
                {
                    if (remainingWork-- <= 0)
                    {
                        unresolved = true;
                        budgetExhausted = true;
                        break;
                    }
                    var implementation =
                        reader.GetInterfaceImplementation(implHandle);
                    if (DecodeType(
                        reader,
                        implementation.Interface,
                        scope) is not { } openInterface)
                    {
                        unresolved = true;
                        continue;
                    }
                    TypeRef baseInterface =
                        openInterface.Instantiate(typeArguments, []);
                    pending.Push((baseInterface, resolved.Assembly.Assembly));
                }
                if (budgetExhausted)
                    break;
            }
            else if (!typeDef.BaseType.IsNil)
            {
                if (remainingWork-- <= 0)
                {
                    unresolved = true;
                    break;
                }
                if (DecodeType(
                    reader,
                    typeDef.BaseType,
                    scope) is not { } openBaseType)
                {
                    unresolved = true;
                    continue;
                }
                TypeRef baseType =
                    openBaseType.Instantiate(typeArguments, []);
                if (!IsObject(baseType))
                    pending.Push((baseType, resolved.Assembly.Assembly));
            }
        }

        return !unresolved && pending.Count == 0;
    }

    static bool TryCreateHierarchyScope(
        GenericParameterHandleCollection parameters,
        ref int remainingWork,
        out GenericScope scope)
    {
        if (parameters.Count == 0)
        {
            scope = new GenericScope([], []);
            return true;
        }
        if (parameters.Count > remainingWork)
        {
            remainingWork = 0;
            scope = new GenericScope([], []);
            return false;
        }

        var names = ImmutableArray.CreateBuilder<string>(
            parameters.Count);
        remainingWork -= parameters.Count;
        foreach (var _ in parameters)
            names.Add("");
        scope = new GenericScope(names.MoveToImmutable(), []);
        return true;
    }

    static bool IsObject(TypeRef type)
        => type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            Namespace: "System",
            Name: "Object",
        };

    bool TryImplements(TypeRef type, TypeRef iface, out bool implements)
    {
        implements = false;
        bool unresolved = false;
        var seen = new HashSet<TypeDefinitionIdentity>();
        var pending = new Stack<(TypeRef Type, ResolvedAssemblyReference? LocalAssembly)>();
        pending.Push((type, null));

        while (pending.Count > 0 && seen.Count < 256)
        {
            var (current, localAssembly) = pending.Pop();
            if (NamedDefinition(current) is not { } definition)
            {
                unresolved = true;
                continue;
            }
            if (Locate(definition, localAssembly) is not { } resolved)
            {
                unresolved = true;
                continue;
            }
            if (!seen.Add(ResolvedIdentity(definition, resolved)))
                continue;
            if (_context.Open(resolved, out var handle) is not { } assembly)
            {
                unresolved = true;
                continue;
            }

            var reader = assembly.Reader;
            var typeDef = reader.GetTypeDefinition(handle);
            var typeArguments = current.Kind == TypeRefKind.GenericInstance ? current.TypeArguments : [];
            foreach (var implemented in DecodeInterfaces(reader, typeDef, typeArguments))
            {
                var identity = SameInterfaceIdentity(
                    implemented,
                    resolved.Assembly.Assembly,
                    iface);
                if (identity == MetadataFactState.Yes)
                {
                    implements = true;
                    return true;
                }
                if (identity == MetadataFactState.Unknown)
                    unresolved = true;
                pending.Push((implemented, resolved.Assembly.Assembly));
            }

            if (DecodeBaseType(reader, typeDef, typeArguments) is { } baseType)
                pending.Push((baseType, resolved.Assembly.Assembly));
        }

        return !unresolved;
    }

    /// <summary>
    /// Interface identity for a hierarchy answer. <see cref="TypeRef.Equals"/>
    /// is deliberately blind to which assembly a name resolves to and to the
    /// structured shape behind a flattened name, so the same interface
    /// presented by two versions of one library — or a nested and a top-level
    /// definition that flatten alike — compare equal. An <c>Implements</c>
    /// answer must not inherit that blindness, so both names are resolved and
    /// required to land on the same definition in the same physical assembly.
    /// A side that cannot be resolved yields <see cref="MetadataFactState.Unknown"/>:
    /// a resolution gap is not evidence of a match, and reporting the gap keeps
    /// the walk honest instead of turning an unknown into a confident Yes.
    /// Generic arguments are compared only by <see cref="TypeRef.Equals"/>, so
    /// two instances whose arguments differ solely by resolution provenance
    /// still compare equal; that residual is unverified.
    /// </summary>
    MetadataFactState SameInterfaceIdentity(
        TypeRef candidate,
        ResolvedAssemblyReference? candidateLocalAssembly,
        TypeRef iface)
    {
        if (!candidate.Equals(iface))
            return MetadataFactState.No;
        if (NamedDefinition(candidate) is not { } candidateDefinition
            || NamedDefinition(iface) is not { } ifaceDefinition)
        {
            return MetadataFactState.Unknown;
        }
        // The core library is the one assembly whose spellings are deliberately
        // aliased — facades, retargeting and the sentinel all name the same
        // definition — so a core-library name that matches is an identity
        // match, and demanding resolution there would turn the ordinary
        // IEnumerable question into Unknown for every cross-assembly type.
        if (candidateDefinition.Assembly == TypeRef.CoreLibrary
            && ifaceDefinition.Assembly == TypeRef.CoreLibrary)
        {
            return MetadataFactState.Yes;
        }
        if (Locate(candidateDefinition, candidateLocalAssembly) is not { } candidateResolved
            || Locate(ifaceDefinition) is not { } ifaceResolved)
        {
            return MetadataFactState.Unknown;
        }
        // Two dimensions, compared the way each is meant to be: the resolved
        // structured name exactly, so a nested and a top-level definition that
        // flatten alike stay distinct, and the assembly with IsEquivalentTo,
        // so an equivalent facade spelling of one core-library type is not
        // mistaken for a different definition.
        return candidateResolved.Type.Equals(ifaceResolved.Type)
                && candidateResolved.Assembly.Assembly.Identity.IsEquivalentTo(
                    ifaceResolved.Assembly.Assembly.Identity)
            ? MetadataFactState.Yes
            : MetadataFactState.No;
    }

    static IEnumerable<TypeRef> DecodeInterfaces(MetadataReader reader, TypeDefinition typeDef, ImmutableArray<TypeRef> typeArguments)    {
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

            // Multiple full-signature matches are malformed or ambiguous.
            // Returning no facts is safer than selecting by metadata order.
            ResolvedMethodFacts? match = null;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!string.Equals(reader.GetString(method.Name), callee.Name, StringComparison.Ordinal))
                    continue;
                bool allowCoreLibraryAliases = type.Assembly == TypeRef.CoreLibrary
                    || ScopeFor(type) == AssemblyResolutionScope.Platform;
                if (!TryMatchMethod(
                    reader,
                    typeDef,
                    method,
                    callee,
                    allowCoreLibraryAliases,
                    type.ResolutionAssembly,
                    definition.Assembly.Assembly,
                    out var parameterRefKinds,
                    out var declaredReturnType))
                    continue;

                if (match is not null)
                    return null;

                bool methodCompilerGenerated = MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, method.GetCustomAttributes());
                RequiresUnsafeContractResult requiresUnsafeContract =
                    MethodDefinitionFacts.RequiresUnsafeContract(
                        assembly.MemorySafety,
                        methodHandle);
                MetadataFactState requiresUnsafeFact =
                    requiresUnsafeContract.State;
                bool requiresUnsafe =
                    requiresUnsafeContract.IsExplicit;
                if (!requiresUnsafeContract.HasNormalizedContract
                    && MethodDefinitionFacts.HasRequiresUnsafeAttribute(
                        reader,
                        method))
                {
                    requiresUnsafeFact = MetadataFactState.Yes;
                    requiresUnsafe = true;
                }
                match = new ResolvedMethodFacts(
                    parameterRefKinds,
                    requiresUnsafe,
                    requiresUnsafeFact,
                    requiresUnsafeContract.RulesState,
                    requiresUnsafeContract.RulesUnavailable,
                    requiresUnsafeContract.ContractUnavailable,
                    MethodDefinitionFacts.ReturnDynamicFact(
                        reader,
                        method,
                        declaredReturnType,
                        callee.ReturnType),
                    MethodDefinitionFacts.ReturnArrayElementDynamicFact(
                        reader,
                        method,
                        declaredReturnType,
                        callee.ReturnType),
                    FactState(methodCompilerGenerated),
                    FactState(typeCompilerGenerated),
                    FactState(IsDelegateType(reader, typeDef)),
                    FactState(MethodDefinitionFacts.HasExtensionAttribute(reader, method)),
                    FactState(MethodDefinitionFacts.IsOperator(method, callee.Name, callee.HasThis)),
                    MethodDefinitionFacts.ReadAccessorKind(reader, typeDef, methodHandle));
            }

            return match;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    ResolvedFieldFacts? ResolveFieldFacts(FieldRef field, TypeRef type)
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

                var declaredFieldType = GuardedDecode.FieldType(reader, candidate, typeScope);
                var fieldType = declaredFieldType.Instantiate(typeArguments, []);
                if (!SameSignatureType(
                    fieldType,
                    field.Type,
                    allowCoreLibraryAliases,
                    TypeRefDecoder.CanonicalSelf(reader),
                    AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
                    type.ResolutionAssembly))
                    continue;

                return new ResolvedFieldFacts(
                    MethodDefinitionFacts.FieldDynamicFact(
                        reader,
                        candidate,
                        declaredFieldType,
                        fieldType),
                    MethodDefinitionFacts.FieldArrayElementDynamicFact(
                        reader,
                        candidate,
                        declaredFieldType,
                        fieldType),
                    BackingPropertyName(reader, typeDef, field.Name));
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

    static bool IsSystemObject(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Namespace: "System", Name: "Object" };

    static bool IsObjectArray(TypeRef type)
        => type is
        {
            Kind: TypeRefKind.SzArray or TypeRefKind.Array,
            ElementType:
            {
                Kind: TypeRefKind.Definition,
                Namespace: "System",
                Name: "Object",
            },
        };

    bool TryMatchMethod(
        MetadataReader reader,
        TypeDefinition declaringType,
        MethodDefinition method,
        MethodRef callee,
        bool allowCoreLibraryAliases,
        AssemblyReferenceIdentity? resolvedLocalBindingIdentity,
        ResolvedAssemblyReference resolvedAssembly,
        out ParameterRefKindResult parameterRefKinds,
        out TypeRef declaredReturnType)
    {
        parameterRefKinds = default;
        declaredReturnType = TypeRef.Unsupported("unmatched method return");
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
        string localAssembly = TypeRefDecoder.CanonicalSelf(reader);
        var localAssemblyIdentity = AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
        if (callee.DefinitionReturnType is { } definitionReturnType)
        {
            var candidateDefinitionReturn = signature.ReturnType.Instantiate(typeArguments, []);
            if (!SameSignatureType(
                candidateDefinitionReturn,
                definitionReturnType,
                allowCoreLibraryAliases,
                localAssembly,
                localAssemblyIdentity,
                resolvedLocalBindingIdentity,
                (resolved, expected) => SameBoundDefinition(
                    resolved,
                    expected,
                    resolvedAssembly)))
            {
                return false;
            }
        }

        var returnType = signature.ReturnType.Instantiate(typeArguments, methodArguments);
        if (!SameSignatureType(
            returnType,
            callee.ReturnType,
            allowCoreLibraryAliases,
            localAssembly,
            localAssemblyIdentity,
            resolvedLocalBindingIdentity,
            (resolved, expected) => SameBoundDefinition(
                resolved,
                expected,
                resolvedAssembly)))
            return false;
        declaredReturnType = signature.ReturnType;

        if (signature.ParameterTypes.Length != callee.ParameterTypes.Length)
            return false;
        if (!callee.DefinitionParameterTypes.IsEmpty)
        {
            if (signature.ParameterTypes.Length != callee.DefinitionParameterTypes.Length)
                return false;
            for (int i = 0; i < signature.ParameterTypes.Length; i++)
            {
                var definitionParameter = signature.ParameterTypes[i].Instantiate(typeArguments, []);
                if (!SameSignatureType(
                    definitionParameter,
                    callee.DefinitionParameterTypes[i],
                    allowCoreLibraryAliases,
                    localAssembly,
                    localAssemblyIdentity,
                    resolvedLocalBindingIdentity,
                    (resolved, expected) => SameBoundDefinition(
                        resolved,
                        expected,
                        resolvedAssembly)))
                {
                    return false;
                }
            }
        }
        var parameters = ImmutableArray.CreateBuilder<TypeRef>(signature.ParameterTypes.Length);
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var parameter = signature.ParameterTypes[i].Instantiate(typeArguments, methodArguments);
            if (!SameSignatureType(
                parameter,
                callee.ParameterTypes[i],
                allowCoreLibraryAliases,
                localAssembly,
                localAssemblyIdentity,
                resolvedLocalBindingIdentity,
                (resolved, expected) => SameBoundDefinition(
                    resolved,
                    expected,
                    resolvedAssembly)))
                return false;
            parameters.Add(parameter);
        }

        parameterRefKinds = MethodDefinitionFacts.ReadParameterRefKinds(reader, method, parameters.MoveToImmutable());
        return true;
    }

    bool SameBoundDefinition(
        TypeRef resolved,
        TypeRef expected,
        ResolvedAssemblyReference resolvedAssembly)
        => TryCreateReferenceResolutionRequest(
                resolved,
                resolvedAssembly,
                out ResolvedAssemblyReference resolvedRoot,
                out TypeResolutionRequest resolvedRequest)
            && TryCreateReferenceResolutionRequest(
                expected,
                localAssembly: null,
                out ResolvedAssemblyReference expectedRoot,
                out TypeResolutionRequest expectedRequest)
            && _context.ResolveToSameDefinition(
                resolvedRoot,
                resolvedRequest,
                expectedRoot,
                expectedRequest);

    internal static bool SameSignatureType(
        TypeRef resolved,
        TypeRef expected,
        bool allowCoreLibraryAliases,
        string? resolvedLocalAssembly = null,
        AssemblyReferenceIdentity? resolvedLocalAssemblyIdentity = null,
        AssemblyReferenceIdentity? resolvedLocalBindingIdentity = null,
        Func<TypeRef, TypeRef, bool>? sameBoundDefinition = null)
    {
        if (resolved.Kind != expected.Kind)
            return false;
        if (!SameCustomModifiers(
            resolved,
            expected,
            allowCoreLibraryAliases,
            resolvedLocalAssembly,
            resolvedLocalAssemblyIdentity,
            resolvedLocalBindingIdentity,
            sameBoundDefinition))
        {
            return false;
        }

        switch (resolved.Kind)
        {
            case TypeRefKind.Definition:
                if (resolved.Namespace != expected.Namespace
                    || resolved.Name != expected.Name
                    || !SameDefinitionName(resolved, expected))
                {
                    return false;
                }
                if (allowCoreLibraryAliases
                    && (resolved.Assembly == TypeRef.CoreLibrary
                        || expected.Assembly == TypeRef.CoreLibrary))
                {
                    return true;
                }
                if (resolved.Assembly != expected.Assembly)
                    return sameBoundDefinition?.Invoke(resolved, expected) == true;
                // Trusted platform assemblies are resolved version-agnostically,
                // and their facades share one canonical core-library identity.
                if (allowCoreLibraryAliases || resolved.Assembly == TypeRef.CoreLibrary)
                    return true;
                var resolvedAssembly = resolved.ResolutionAssembly;
                bool resolvedFromLocalAssembly = false;
                if (resolvedAssembly is null
                    && resolvedLocalAssemblyIdentity is not null
                    && resolved.Assembly == resolvedLocalAssembly)
                {
                    resolvedAssembly = resolvedLocalAssemblyIdentity;
                    resolvedFromLocalAssembly = true;
                }
                bool sameMetadataIdentity = (resolvedAssembly, expected.ResolutionAssembly) switch
                {
                    (null, null) => true,
                    ({ } actual, { } expectedAssembly)
                        => actual.IsEquivalentTo(expectedAssembly)
                            || resolvedFromLocalAssembly
                                && resolvedLocalBindingIdentity is { } bindingIdentity
                                && bindingIdentity.IsEquivalentTo(expectedAssembly),
                    _ => false,
                };
                return sameMetadataIdentity
                    || sameBoundDefinition?.Invoke(resolved, expected) == true;
            case TypeRefKind.GenericInstance:
                if (!SameSignatureType(
                        resolved.ElementType!,
                        expected.ElementType!,
                        allowCoreLibraryAliases,
                        resolvedLocalAssembly,
                        resolvedLocalAssemblyIdentity,
                        resolvedLocalBindingIdentity,
                        sameBoundDefinition)
                    || resolved.TypeArguments.Length != expected.TypeArguments.Length)
                    return false;
                for (int i = 0; i < resolved.TypeArguments.Length; i++)
                    if (!SameSignatureType(
                        resolved.TypeArguments[i],
                        expected.TypeArguments[i],
                        allowCoreLibraryAliases,
                        resolvedLocalAssembly,
                        resolvedLocalAssemblyIdentity,
                        resolvedLocalBindingIdentity,
                        sameBoundDefinition))
                        return false;
                return true;
            case TypeRefKind.SzArray or TypeRefKind.Pointer or TypeRefKind.Pinned or TypeRefKind.ByRef:
                return SameSignatureType(
                    resolved.ElementType!,
                    expected.ElementType!,
                    allowCoreLibraryAliases,
                    resolvedLocalAssembly,
                    resolvedLocalAssemblyIdentity,
                    resolvedLocalBindingIdentity,
                    sameBoundDefinition);
            case TypeRefKind.Array:
                return resolved.Rank == expected.Rank
                    && SameSignatureType(
                        resolved.ElementType!,
                        expected.ElementType!,
                        allowCoreLibraryAliases,
                        resolvedLocalAssembly,
                        resolvedLocalAssemblyIdentity,
                        resolvedLocalBindingIdentity,
                        sameBoundDefinition);
            case TypeRefKind.FunctionPointer:
                if (resolved.CallingConvention != expected.CallingConvention
                    || !SameSignatureType(
                        resolved.ElementType!,
                        expected.ElementType!,
                        allowCoreLibraryAliases,
                        resolvedLocalAssembly,
                        resolvedLocalAssemblyIdentity,
                        resolvedLocalBindingIdentity,
                        sameBoundDefinition)
                    || resolved.TypeArguments.Length != expected.TypeArguments.Length
                    || resolved.FunctionPointerParameterRefKinds.Length != expected.FunctionPointerParameterRefKinds.Length)
                    return false;
                for (int i = 0; i < resolved.TypeArguments.Length; i++)
                    if (!SameSignatureType(
                        resolved.TypeArguments[i],
                        expected.TypeArguments[i],
                        allowCoreLibraryAliases,
                        resolvedLocalAssembly,
                        resolvedLocalAssemblyIdentity,
                        resolvedLocalBindingIdentity,
                        sameBoundDefinition))
                        return false;
                for (int i = 0; i < resolved.FunctionPointerParameterRefKinds.Length; i++)
                    if (resolved.FunctionPointerParameterRefKinds[i] != expected.FunctionPointerParameterRefKinds[i])
                        return false;
                return true;
            default:
                return resolved.Equals(expected);
        }
    }

    // Decoded metadata compares the authoritative segment structure. Legacy
    // synthetic TypeRefs may fall back only for simple top-level names: a '+'
    // could be either a literal character or a nesting delimiter.
    static bool SameDefinitionName(TypeRef resolved, TypeRef expected)
        => (resolved.DefinitionName, expected.DefinitionName) switch
        {
            ({ } actual, { } target) => actual.Equals(target),
            (null, null) => !resolved.Name.Contains('+', StringComparison.Ordinal),
            _ => false,
        };

    static bool SameCustomModifiers(
        TypeRef resolved,
        TypeRef expected,
        bool allowCoreLibraryAliases,
        string? resolvedLocalAssembly,
        AssemblyReferenceIdentity? resolvedLocalAssemblyIdentity,
        AssemblyReferenceIdentity? resolvedLocalBindingIdentity,
        Func<TypeRef, TypeRef, bool>? sameBoundDefinition)
    {
        if (resolved.CustomModifiers.Length != expected.CustomModifiers.Length)
            return false;
        for (int i = 0; i < resolved.CustomModifiers.Length; i++)
        {
            var actual = resolved.CustomModifiers[i];
            var target = expected.CustomModifiers[i];
            if (actual.IsRequired != target.IsRequired
                || !SameSignatureType(
                    actual.Modifier,
                    target.Modifier,
                    allowCoreLibraryAliases,
                    resolvedLocalAssembly,
                    resolvedLocalAssemblyIdentity,
                    resolvedLocalBindingIdentity,
                    sameBoundDefinition))
            {
                return false;
            }
        }
        return true;
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
        if (!TryCoordinates(type, out TypeResolutionCoordinates coordinates))
            return TypeShapeKind.Unknown;
        if (_shapeCache.TryGetValue(coordinates, out var cached))
            return cached;
        var shape = ClassifyShapeCore(type);
        return _shapeCache.GetOrAdd(coordinates, shape);
    }

    TypeShapeKind ClassifyShapeCore(TypeRef type)
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

    bool IsSelf(TypeRef type)
        => TypeDefinitionIdentity.BelongsToAssembly(
            type,
            _selfCanonical,
            _selfAssembly.Identity);

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
        ResolvedAssemblyReference root =
            localAssembly ?? _selfAssembly;
        MetadataTypeDefinitionName? definitionName =
            type.DefinitionName;
        AssemblyReferenceIdentity? resolutionAssembly =
            type.ResolutionAssembly;
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
        if (localAssembly is not null
            && resolutionAssembly is null)
        {
            request = TypeResolutionRequest.FromAssembly(
                localAssembly,
                ScopeFor(type),
                definitionName);
        }
        else if (type.Assembly == TypeRef.CoreLibrary)
        {
            return _context.ResolveCoreLibraryDefinition(
                root,
                definitionName);
        }
        else
        {
            if (resolutionAssembly is not { } identity)
                return null;
            request = TypeResolutionRequest.FromReference(
                identity,
                AssemblyBindingOrigin.FromAssembly(root),
                ScopeFor(type),
                definitionName);
        }

        TypeResolutionOutcome outcome =
            _context.Resolve(root, request);
        return outcome is TypeResolutionOutcome.Resolved resolved
            ? resolved.Definition
            : null;
    }

    bool TryCreateReferenceResolutionRequest(
        TypeRef type,
        ResolvedAssemblyReference? localAssembly,
        out ResolvedAssemblyReference root,
        out TypeResolutionRequest request)
    {
        root = localAssembly ?? _selfAssembly;
        request = null!;
        if (type.Assembly == TypeRef.CoreLibrary)
            return false;

        MetadataTypeDefinitionName? definitionName = type.DefinitionName;
        AssemblyReferenceIdentity? resolutionAssembly =
            type.ResolutionAssembly;
        if (definitionName is null)
        {
            if (!TryResolutionIdentity(
                    type,
                    out definitionName,
                    out resolutionAssembly))
            {
                return false;
            }
        }
        else if (resolutionAssembly is null && localAssembly is null)
        {
            return false;
        }

        if (localAssembly is not null && resolutionAssembly is null)
        {
            request = TypeResolutionRequest.FromAssembly(
                localAssembly,
                ScopeFor(type),
                definitionName);
            return true;
        }

        if (resolutionAssembly is not { } identity)
            return false;

        request = TypeResolutionRequest.FromReference(
            identity,
            AssemblyBindingOrigin.FromAssembly(root),
            ScopeFor(type),
            definitionName);
        return true;
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

    // Resolution supplies the exact structured name and bound assembly identity
    // that legacy caller-constructed TypeRefs may not carry.
    static TypeDefinitionIdentity ResolvedIdentity(
        TypeRef definition,
        ResolvedTypeDefinition resolved)
        => new(
            definition,
            resolved.Type,
            resolved.Assembly.Assembly.Identity);

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

    sealed class MethodFactCacheIdentity
        : IEquatable<MethodFactCacheIdentity>
    {
        readonly int _hashCode;

        public MethodFactCacheIdentity(MethodRef method)
        {
            Method = method;
            var hash = new HashCode();
            hash.Add(method);
            AddResolutionIdentity(ref hash, method.DeclaringType);
            AddResolutionIdentity(ref hash, method.ReturnType);
            AddResolutionIdentities(ref hash, method.ParameterTypes);
            AddResolutionIdentities(ref hash, method.TypeArguments);
            AddResolutionIdentity(ref hash, method.DefinitionReturnType);
            AddResolutionIdentities(
                ref hash,
                method.DefinitionParameterTypes);
            _hashCode = hash.ToHashCode();
        }

        public MethodRef Method { get; }

        public bool Equals(MethodFactCacheIdentity? other) =>
            other is not null
            && Method.Equals(other.Method)
            && SameResolutionIdentity(
                Method.DeclaringType,
                other.Method.DeclaringType)
            && SameResolutionIdentity(
                Method.ReturnType,
                other.Method.ReturnType)
            && SameResolutionIdentities(
                Method.ParameterTypes,
                other.Method.ParameterTypes)
            && SameResolutionIdentities(
                Method.TypeArguments,
                other.Method.TypeArguments)
            && SameResolutionIdentity(
                Method.DefinitionReturnType,
                other.Method.DefinitionReturnType)
            && SameResolutionIdentities(
                Method.DefinitionParameterTypes,
                other.Method.DefinitionParameterTypes);

        public override bool Equals(object? obj) =>
            Equals(obj as MethodFactCacheIdentity);

        public override int GetHashCode() => _hashCode;

        static bool SameResolutionIdentities(
            ImmutableArray<TypeRef> left,
            ImmutableArray<TypeRef> right)
        {
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (!SameResolutionIdentity(left[i], right[i]))
                    return false;
            }

            return true;
        }

        static bool SameResolutionIdentity(
            TypeRef? left,
            TypeRef? right)
        {
            if (left is null || right is null)
                return left is null && right is null;
            if (left.ResolutionAssembly != right.ResolutionAssembly
                || left.CustomModifiers.Length
                    != right.CustomModifiers.Length
                || !SameResolutionIdentity(
                    left.ElementType,
                    right.ElementType)
                || !SameResolutionIdentities(
                    left.TypeArguments,
                    right.TypeArguments))
            {
                return false;
            }

            for (int i = 0; i < left.CustomModifiers.Length; i++)
            {
                TypeRefCustomModifier leftModifier =
                    left.CustomModifiers[i];
                TypeRefCustomModifier rightModifier =
                    right.CustomModifiers[i];
                if (leftModifier.IsRequired
                        != rightModifier.IsRequired
                    || !leftModifier.Modifier.Equals(
                        rightModifier.Modifier)
                    || !SameResolutionIdentity(
                        leftModifier.Modifier,
                        rightModifier.Modifier))
                {
                    return false;
                }
            }

            return true;
        }

        static void AddResolutionIdentities(
            ref HashCode hash,
            ImmutableArray<TypeRef> types)
        {
            hash.Add(types.Length);
            foreach (TypeRef type in types)
                AddResolutionIdentity(ref hash, type);
        }

        static void AddResolutionIdentity(
            ref HashCode hash,
            TypeRef? type)
        {
            if (type is null)
            {
                hash.Add(0);
                return;
            }

            hash.Add(type.ResolutionAssembly);
            AddResolutionIdentity(ref hash, type.ElementType);
            AddResolutionIdentities(ref hash, type.TypeArguments);
            hash.Add(type.CustomModifiers.Length);
            foreach (TypeRefCustomModifier modifier
                in type.CustomModifiers)
            {
                hash.Add(modifier.IsRequired);
                hash.Add(modifier.Modifier);
                AddResolutionIdentity(ref hash, modifier.Modifier);
            }
        }
    }

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

    static bool NeedsReturnDynamicFact(MethodRef method)
        => method.ReturnIsDynamic == MetadataFactState.Unknown
            && DynamicResultType(method.ReturnType) is
            {
                Kind: TypeRefKind.Definition,
                Namespace: "System",
                Name: "Object",
            };

    static bool NeedsReturnArrayElementDynamicFact(MethodRef method)
        => method.ReturnArrayElementIsDynamic == MetadataFactState.Unknown
            && IsObjectArray(method.ReturnType);

    static TypeRef DynamicResultType(TypeRef type)
        => type.Kind == TypeRefKind.ByRef && type.ElementType is { } element
            ? element
            : type;

    static bool IsDelegateType(MetadataReader reader, TypeDefinition typeDef)
    {
        try { return BaseTypeName(reader, typeDef.BaseType) is "System.MulticastDelegate"; }
        catch (BadImageFormatException) { return false; }
    }

    static MetadataFactState FactState(bool value) => value ? MetadataFactState.Yes : MetadataFactState.No;

    readonly record struct ResolvedMethodFacts(
        ParameterRefKindResult ParameterRefKinds,
        bool RequiresUnsafe,
        MetadataFactState RequiresUnsafeFact,
        MemorySafetyRulesState? MemorySafetyRulesState,
        bool MemorySafetyRulesUnavailable,
        bool MemorySafetyContractUnavailable,
        MetadataFactState ReturnIsDynamic,
        MetadataFactState ReturnArrayElementIsDynamic,
        MetadataFactState CompilerGenerated,
        MetadataFactState DeclaringTypeCompilerGenerated,
        MetadataFactState DeclaringTypeIsDelegate,
        MetadataFactState IsExtension,
        MetadataFactState IsOperator,
        AccessorKind AccessorKind);

    readonly record struct ResolvedFieldFacts(
        MetadataFactState DynamicFact,
        MetadataFactState ArrayElementIsDynamic,
        string? BackingPropertyName);
}
