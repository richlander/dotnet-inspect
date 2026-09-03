using System.Collections.Immutable;
using System.Globalization;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Final-output C# spelling checks the printer relies on. Two kinds:
/// <list type="bullet">
/// <item>metadata names the printer would emit bare (honest-degradation
/// predicates, not rewrite gates: an unspeakable compiler-generated name that
/// survives raising makes the method no longer Full-fidelity C#), and</item>
/// <item>value-category spellability — whether the printer can spell an
/// expression as a plain value the compiler converts to <c>dynamic</c> — the
/// authoritative predicate passes consult before lifting an expression into a
/// value sink, so each pass asks one question instead of re-deriving the walk.</item>
/// </list>
/// </summary>
internal static class CSharpSpellability
{
    internal readonly record struct NameIssue(
        string Discriminator,
        string Reason,
        DecompilerFidelityLocation? Location = null);

    enum PlaceKind { Argument, Local, StackSlot }
    enum ExplicitTypeContext
    {
        Parameter,
        Element,
        GenericArgument,
        ArrayElement,
        PointerElement,
        FunctionPointerParameter,
        FunctionPointerReturn,
    }

    public static bool HasUnrepresentableMetadataName(IrNode node)
        => InspectUnrepresentableMetadataName(node) is not null;

    public static bool CanSpellExplicitParameterType(
        TypeRef type,
        IrFunction host,
        ArgumentRefKind refKind,
        bool isDynamic = false)
        => !type.ContainsUnsupported
            && (!isDynamic || IsDynamicParameterType(type, host))
            && type.ExplicitParameterModifiersAreExact(refKind)
            && HasExplicitParameterTypeShape(type, ExplicitTypeContext.Parameter, host)
            && TypeIssue(type) is null
            && !AnyDeclarationContextualNamePrintedBare(type)
            && !AnyConstituentLeadingSegmentShadowed(type, host, [type])
            && !AnyPrintedAliasInTypeShadowed(type, isDynamic, host, [type])
            && !AnyBareNameInTypeShadowed(type, host, [type]);

    internal static NameIssue? InspectUnrepresentableMetadataName(IrNode node)
    {
        foreach (var type in RenderedTypes(node))
        {
            if (TypeIssue(type) is { } issue)
                return issue;
        }

        if (node is IrExpression { ResultType: { } resultType }
            && TypeIssue(resultType) is { } expressionTypeIssue)
        {
            return expressionTypeIssue;
        }

        return node switch
        {
            IrFunction function => ParameterNamesIssue(
                function.Signature.Parameters,
                function.Signature.GenericParameterNames)
                ?? LocalNamesIssue(
                    function.LocalNames,
                    function.EliminatedLocalSlots,
                    function.Signature.Parameters
                        .Select(parameter => parameter.Name)
                        .Concat(function.Signature.GenericParameterNames)),
            Call call => MethodIssue(call.Callee),
            NewObject newObject => ConstructorIssue(newObject.Constructor),
            AddressOfMethod address => MethodGroupTargetIssue(address.Method),
            DelegateCreation creation => MethodGroupTargetIssue(creation.Method),
            LoadField load => FieldIssue(load.Field),
            StoreField store => FieldIssue(store.Field),
            LoadFieldAddress address => FieldIssue(address.Field),
            FixedBufferElementAddress address => FieldIssue(address.BufferField),
            LoadProperty load => PropertyIssue(load.PropertyName),
            StoreProperty store => PropertyIssue(store.PropertyName),
            NullCoalescingFieldAssignment assignment => FieldIssue(assignment.Field),
            NullCoalescingFieldAssignmentExpression assignment => FieldIssue(assignment.Field),
            NullCoalescingPropertyAssignment assignment => PropertyIssue(assignment.PropertyName),
            AnonymousObject anonymous => InitializerMembersIssue(anonymous.PropertyNames),
            ObjectInitializerExpression initializer => InitializerMembersIssue(initializer.Members),
            WithExpression withExpression => InitializerMembersIssue(withExpression.Members),
            InitializerBlock block => InitializerMembersIssue(block.Members),
            DeconstructionTarget target => DeconstructionTargetIssue(target),
            RecursivePropertyDeclarationPattern pattern => PropertyIssue(pattern.PropertyName),
            EventSubscription subscription => PropertyIssue(subscription.EventName),
            Lambda lambda => ParameterNamesIssue(lambda.Parameters)
                ?? NestedLocalNamesIssue(
                    lambda.LocalNames,
                    lambda.Parameters,
                    lambda.Body),
            LocalFunctionStatement statement => LocalFunctionIssue(statement.Name)
                ?? ParameterNamesIssue(statement.Parameters)
                ?? NestedLocalNamesIssue(
                    statement.LocalNames,
                    statement.Parameters,
                    statement.Body),
            LocalFunctionInvocation invocation => LocalFunctionIssue(invocation.Name),
            _ => null,
        };
    }

    static NameIssue? ParameterNamesIssue(
        ImmutableArray<Parameter> parameters,
        IEnumerable<string>? reservedNames = null)
    {
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string>? reserved = reservedNames is null
            ? null
            : new HashSet<string>(reservedNames, StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (!HasLosslessBodyIdentifierSpelling(parameter.Name))
            {
                return Issue(
                    DecompilerFidelityDiscriminators.UnspellableParameterName,
                    $"parameter name '{parameter.Name}' has no lossless C# spelling");
            }
            if (!parameterNames.Add(parameter.Name))
            {
                return Issue(
                    DecompilerFidelityDiscriminators.UnspellableParameterName,
                    $"duplicate parameter name '{parameter.Name}' has no lossless C# binding");
            }
            if (reserved?.Contains(parameter.Name) == true)
            {
                return Issue(
                    DecompilerFidelityDiscriminators.UnspellableParameterName,
                    $"parameter name '{parameter.Name}' conflicts with a method generic parameter");
            }
        }

        return null;
    }

    static NameIssue? LocalNamesIssue(
        ImmutableArray<string?> localNames,
        IReadOnlySet<int>? eliminatedLocalSlots = null,
        IEnumerable<string>? reservedNames = null,
        IrNode? retainedScope = null)
    {
        HashSet<string>? reserved = reservedNames is null
            ? null
            : new HashSet<string>(reservedNames, StringComparer.Ordinal);
        for (var index = 0; index < localNames.Length; index++)
        {
            string? name = localNames[index];
            if (name is null || eliminatedLocalSlots?.Contains(index) == true)
                continue;
            if (retainedScope is not null
                && !IrFunction.LocalSlotReferencesInScope(retainedScope, index).Any())
            {
                continue;
            }
            if (!HasLosslessBodyIdentifierSpelling(name))
            {
                return new NameIssue(
                    DecompilerFidelityDiscriminators.UnspellableLocalName,
                    $"local name '{name}' has no lossless C# spelling",
                    DecompilerFidelityLocation.AtLocal(index));
            }
            if (reserved?.Contains(name) == true)
            {
                return new NameIssue(
                    DecompilerFidelityDiscriminators.UnspellableLocalName,
                    $"local name '{name}' conflicts with a parameter or method generic parameter",
                    DecompilerFidelityLocation.AtLocal(index));
            }
        }

        return null;
    }

    static NameIssue? NestedLocalNamesIssue(
        ImmutableArray<string?> localNames,
        ImmutableArray<Parameter> parameters,
        BlockContainer body)
        => LocalNamesIssue(
            localNames,
            reservedNames: parameters
                .Select(parameter => parameter.Name)
                .Concat(ExternalArgumentNamesInScope(body, parameters)),
            retainedScope: body);

    internal static IEnumerable<string> ExternalArgumentNamesInScope(
        IrNode scope,
        ImmutableArray<Parameter> parameters)
    {
        var parameterNames = new HashSet<string>(
            parameters.Select(parameter => parameter.Name),
            StringComparer.Ordinal);
        foreach (var node in scope.DescendantsOutsideNestedFunctions.Prepend(scope))
        {
            string? name = node switch
            {
                LoadArgument argument => argument.Name,
                LoadArgumentAddress address => address.Name,
                StoreArgument store => store.Name,
                _ => null,
            };
            if (name is not null && !parameterNames.Contains(name))
                yield return name;
        }
    }

    static bool HasLosslessBodyIdentifierSpelling(string name)
        => CSharpIdentifier.IsIdentifierLike(name)
            && !name.Any(static character =>
                char.IsSurrogate(character)
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format);

    /// <summary>
    /// Whether the printer can spell <paramref name="expression"/> as a value the
    /// compiler implicitly converts to <c>dynamic</c> (<c>(dynamic)expr</c>).
    /// Follows transparent conversions, value-merges (both conditional / switch
    /// arms), and body-local reaching definitions to their leaf value-producers;
    /// the expression is spellable only when every reachable node is itself a
    /// printer-spellable dynamic-castable value. Deleting a cache guard can make a
    /// preceding spill adjacent to the raised use, so the walk accounts for values
    /// a later inliner would expose.
    /// <para>
    /// The printer renders such a value through <c>Operand</c> / <c>Expression</c>
    /// (for example the <c>DynamicGetMember</c> receiver). In that switch the
    /// address / <c>unbox</c> place leaves render with a leading <c>ref</c> and
    /// <c>LoadFunctionPointer</c> renders as an <c>ldftn</c> comment placeholder;
    /// none is a value convertible to <c>dynamic</c>.
    /// </para>
    /// </summary>
    public static bool IsDynamicCastableValue(IrFunction function, IrExpression expression)
    {
        var pending = new Stack<IrExpression>();
        var seenPlaces = new HashSet<(PlaceKind Kind, int Index)>();
        var bodyNodes = function.DescendantsOutsideNestedFunctions.ToList();
        pending.Push(expression);

        while (pending.Count > 0)
        {
            var expr = pending.Pop();
            if (!HasDynamicCastableValueCategory(expr))
                return false;

            switch (expr)
            {
                case Coerce coerce:
                    pending.Push(coerce.Operand);
                    break;
                case Convert convert:
                    pending.Push(convert.Operand);
                    break;
                case CastClass cast:
                    pending.Push(cast.Operand);
                    break;
                case Box box:
                    pending.Push(box.Operand);
                    break;
                case IsInstance isInstance:
                    pending.Push(isInstance.Operand);
                    break;
                case UnboxAny unbox:
                    pending.Push(unbox.Operand);
                    break;
                case Conditional conditional:
                    pending.Push(conditional.Condition);
                    // Both arms render through the same Operand/Expression path
                    // (ConditionalText), which carries no ByRef special case, so
                    // both must be spellable regardless of the conditional's
                    // result type. A `ref`-typed merge whose arm is an address /
                    // `unbox` place would otherwise render with a leading `ref`
                    // the compiler rejects (a `ref` of a cast, or a doubled
                    // keyword), so its arms are inspected here rather than skipped.
                    pending.Push(conditional.WhenTrue);
                    pending.Push(conditional.WhenFalse);
                    break;
                case Coalesce coalesce:
                    pending.Push(coalesce.Left);
                    pending.Push(coalesce.Right);
                    break;
                case SwitchExpression switchExpression:
                    pending.Push(switchExpression.Value);
                    foreach (var arm in switchExpression.Arms)
                        pending.Push(arm.Value);
                    break;
                case TupleSwitchExpression tupleSwitch:
                    foreach (var component in tupleSwitch.Components)
                        pending.Push(component);
                    foreach (var arm in tupleSwitch.Arms)
                        pending.Push(arm.Value);
                    break;
                case UnionSwitchExpression unionSwitch:
                    pending.Push(unionSwitch.Value);
                    foreach (var arm in unionSwitch.Arms)
                    {
                        if (arm.Guard is { } guard)
                            pending.Push(guard);
                        pending.Push(arm.Value);
                    }
                    if (unionSwitch.NullValue is { } nullValue)
                        pending.Push(nullValue);
                    if (unionSwitch.DefaultValue is { } defaultValue)
                        pending.Push(defaultValue);
                    break;
                case PatternSwitchExpression patternSwitch:
                    pending.Push(patternSwitch.Value);
                    foreach (var arm in patternSwitch.Arms)
                    {
                        if (arm.Guard is { } guard)
                            pending.Push(guard);
                        pending.Push(arm.Value);
                    }
                    if (patternSwitch.DefaultValue is { } patternDefault)
                        pending.Push(patternDefault);
                    break;
                case LoadArgument load
                    when seenPlaces.Add((PlaceKind.Argument, load.Index)):
                    foreach (var store in bodyNodes.OfType<StoreArgument>())
                        if (store.Index == load.Index)
                            pending.Push(store.Value);
                    break;
                case LoadLocal load
                    when seenPlaces.Add((PlaceKind.Local, load.Index)):
                    foreach (var store in bodyNodes.OfType<StoreLocal>())
                        if (store.Index == load.Index)
                            pending.Push(store.Value);
                    break;
                case LoadStackSlot load
                    when seenPlaces.Add((PlaceKind.StackSlot, load.Slot)):
                    foreach (var store in bodyNodes.OfType<StoreStackSlot>())
                        if (store.Slot == load.Slot)
                            pending.Push(store.Value);
                    break;
            }
        }

        return true;
    }

    // A single node's value category, mirroring the printer's value-expression
    // arms. The printer spells an address / `unbox` place with a leading `ref`
    // (`ref x`, `ref buf[i]`, `ref (T)o`) and a raw function-pointer load as an
    // `ldftn` comment, so none is a value convertible to `dynamic`. A `ByRef`
    // receiver is implicitly dereferenced, so it stays spellable when its element
    // is a value/reference type but not when the element is itself a pointer /
    // function pointer (`ref int*` still yields `int*`, CS0030); peel `ByRef` and
    // local-signature `Pinned` wrappers over the finite result-type tree before
    // the pointer check so no crafted shape slips a pointer through.
    static bool HasDynamicCastableValueCategory(IrExpression expr)
    {
        if (expr is LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress
            or LoadElementAddress or FixedBufferElementAddress
            or LoadFunctionPointer or Unbox)
            return false;

        var type = expr.ResultType;
        while (type?.Kind is TypeRefKind.ByRef or TypeRefKind.Pinned)
            type = type.ElementType;
        return type?.Kind is not (TypeRefKind.Pointer or TypeRefKind.FunctionPointer);
    }

    static IEnumerable<TypeRef> RenderedTypes(IrNode node)
        => node is IrFunction function
            ? FunctionRenderedTypes(function)
            : node.DirectTypes;

    static IEnumerable<TypeRef> FunctionRenderedTypes(IrFunction function)
    {
        yield return function.Signature.ReturnType;
        foreach (var parameter in function.Signature.Parameters)
            yield return parameter.Type;
        for (int slot = 0; slot < function.Locals.Length; slot++)
        {
            // A slot a raising pass proved dead renders nowhere (the printer skips
            // it), so its type — often an unspellable synthesized buffer such as
            // <>y__InlineArrayN — must not degrade fidelity for output it never
            // appears in.
            if (function.EliminatedLocalSlots.Contains(slot))
                continue;
            yield return function.Locals[slot];
        }
        foreach (var region in function.Regions)
            if (region.CatchType is { } catchType)
                yield return catchType;
    }

    static NameIssue? ConstructorIssue(MethodRef constructor)
    {
        foreach (var argument in constructor.TypeArguments)
            if (TypeIssue(argument) is { } issue)
                return issue;
        return null;
    }

    static NameIssue? MethodIssue(MethodRef method)
        => MethodIssue(method, isMethodGroupTarget: false);

    /// <summary>
    /// The spellability of a method used as a delegate/method-group target
    /// (<c>ldftn</c>). Unlike a constructor <em>call</em> — which renders as a
    /// <c>base(...)</c>/<c>this(...)</c> initializer or <c>new T(...)</c> and so
    /// exempts <c>.ctor</c> — a constructor has no C# method-group spelling, so a
    /// <c>.ctor</c> target must degrade. Otherwise the name sanitizer's legal
    /// <c>__ctor</c> fallback would be presented as Full fidelity and could
    /// silently bind an unrelated real <c>__ctor</c> member (#3129).
    /// </summary>
    static NameIssue? MethodGroupTargetIssue(MethodRef method)
        => MethodIssue(method, isMethodGroupTarget: true);

    static NameIssue? MethodIssue(MethodRef method, bool isMethodGroupTarget)
    {
        foreach (var argument in method.TypeArguments)
            if (TypeIssue(argument) is { } issue)
                return issue;

        if (method.Name is ".ctor" && !isMethodGroupTarget)
            return null;

        if (IsUnverifiedAccessorLikeMethod(method))
        {
            return Issue(
                DecompilerFidelityDiscriminators.AccessorMetadataUnavailable,
                $"method '{method.Name}' looks like a property/event accessor but accessor metadata was unavailable; explicit accessor calls have no C# spelling");
        }

        // Check the metadata name as it stands, not a >g__ decode of it, ONLY when the
        // raising pass stamped this callee as declined. Decoding a declined call would
        // rate it Full on the strength of a source spelling that appears nowhere in the
        // output, because no declaration of it is emitted (#3631). The stamp — not the
        // name shape — is the discriminator: before LocalFunctionRaisingPass runs, a
        // local function that WILL be raised carries the identical mangled name, so
        // judging by name alone degrades methods whose output is perfectly valid.
        string name = method.LocalFunctionRaise == LocalFunctionRaiseState.Declined
            ? method.Name
            : CSharpNaming.MethodName(method.Name);
        return CSharpNaming.IsEscapableIdentifier(name)
            ? null
            : Issue(
                MethodNameDiscriminator(method.Name),
                $"method name '{method.Name}' has no C# spelling");
    }

    static NameIssue? FieldIssue(FieldRef field)
    {
        string name = field.BackingPropertyName ?? field.Name;
        if (CSharpNaming.IsEscapableIdentifier(name))
            return null;
        return Issue(
            GeneratedCodeIdentity.IsGeneratedFieldName(name)
                ? DecompilerFidelityDiscriminators.GeneratedFieldName
                : DecompilerFidelityDiscriminators.UnspellableFieldName,
            $"field name '{field.Name}' has no C# spelling");
    }

    static NameIssue? PropertyIssue(string name)
    {
        if (CSharpNaming.IsUsableIdentifier(name))
            return null;
        if (CSharpNaming.IsEscapableIdentifier(name))
        {
            return Issue(
                DecompilerFidelityDiscriminators.EscapablePropertyName,
                $"property name '{name}' requires C# @ escaping");
        }
        return Issue(
            IsGeneratedNameShape(name)
                ? DecompilerFidelityDiscriminators.GeneratedPropertyName
                : DecompilerFidelityDiscriminators.UnspellablePropertyName,
            $"property name '{name}' has no C# spelling");
    }

    static bool IsUnverifiedAccessorLikeMethod(MethodRef method)
        => method.AccessorKind == AccessorKind.Unknown
            && method.IsSpecialName
            && !method.IsSpecialNameInferred
            && (method.Name.StartsWith("get_", StringComparison.Ordinal)
                || method.Name.StartsWith("set_", StringComparison.Ordinal)
                || method.Name.StartsWith("add_", StringComparison.Ordinal)
                || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    // A local function name is emitted through CSharpNaming.EscapeIdentifier, so a
    // reserved keyword (e.g. return -> @return) is spellable; only a name with
    // invalid identifier characters (e.g. a hand-written `bad-name`) has no C#
    // spelling. Use the keyword-tolerant predicate to avoid degrading the
    // keyword-escaped local functions #1465 made Full.
    static NameIssue? LocalFunctionIssue(string name)
        => CSharpNaming.IsEscapableIdentifier(name)
            ? null
            : Issue(
                DecompilerFidelityDiscriminators.UnspellableLocalFunctionName,
                $"local function name '{name}' has no C# spelling");

    static NameIssue? InitializerMembersIssue(IEnumerable<string?> members)
    {
        foreach (var member in members)
        {
            if (member is null)
                continue;
            if (!CSharpNaming.IsUsableIdentifier(member))
            {
                if (CSharpNaming.IsEscapableIdentifier(member))
                    continue;
                return Issue(
                    IsGeneratedNameShape(member)
                        ? DecompilerFidelityDiscriminators.GeneratedInitializerMemberName
                        : DecompilerFidelityDiscriminators.UnspellableInitializerMemberName,
                    $"initializer member name '{member}' has no C# spelling");
            }
        }
        return null;
    }

    static NameIssue? DeconstructionTargetIssue(DeconstructionTarget target)
        => target.Kind switch
        {
            DeconstructionTargetKind.Field when target.Field is { } field => FieldIssue(field),
            DeconstructionTargetKind.Property => PropertyIssue(target.PropertyName),
            _ => null,
        };

    static NameIssue? TypeIssue(TypeRef type)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (IsCoreLibPrimitive(type))
                    return null;
                if (type.HasDefinitionArityMismatch)
                {
                    return Issue(
                        "generic-arity-mismatch",
                        $"generic type '{type}' has a metadata/ownership arity mismatch");
                }
                // Validate every nested segment, not just the leaf: a foreign
                // nested type is spelled through its declaring chain
                // (`Outer.Inner`), so an unspellable outer segment also has no
                // valid C# spelling even when the innermost name is fine. Keyword
                // segments are spellable via @ escaping, so use the keyword-tolerant
                // identifier predicate.
                foreach (string segment in type.MetadataNameSegments())
                {
                    string simpleSegment = StripArity(segment);
                    if (!CSharpNaming.IsEscapableIdentifier(simpleSegment))
                    {
                        return Issue(
                            TypeNameDiscriminator(simpleSegment),
                            $"type name '{simpleSegment}' has no C# spelling");
                    }
                }
                return null;

            case TypeRefKind.GenericInstance:
                if (type.HasUnrenderableGenericArity)
                {
                    return Issue(
                        "generic-arity-mismatch",
                        $"generic type '{type.ElementType}' has an argument-count mismatch");
                }
                if (type.ElementType is { } definition && TypeIssue(definition) is { } definitionIssue)
                    return definitionIssue;
                foreach (var argument in type.TypeArguments)
                    if (TypeIssue(argument) is { } argumentIssue)
                        return argumentIssue;
                return null;

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element ? TypeIssue(element) : null;

            case TypeRefKind.GenericParameter:
            case TypeRefKind.MethodGenericParameter:
                return type.GenericParameterName.Length == 0 || CSharpNaming.IsEscapableIdentifier(type.GenericParameterName)
                    ? null
                    : Issue(
                        IsGeneratedNameShape(type.GenericParameterName)
                            ? DecompilerFidelityDiscriminators.GeneratedGenericParameterName
                            : DecompilerFidelityDiscriminators.UnspellableGenericParameterName,
                        $"generic parameter name '{type.GenericParameterName}' has no C# spelling");

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType && TypeIssue(returnType) is { } returnIssue)
                    return returnIssue;
                foreach (var parameter in type.TypeArguments)
                    if (TypeIssue(parameter) is { } parameterIssue)
                        return parameterIssue;
                return null;

            default:
                return null;
        }
    }

    static bool HasExplicitParameterTypeShape(
        TypeRef type,
        ExplicitTypeContext context,
        IrFunction host)
    {
        if (context is ExplicitTypeContext.ArrayElement
                or ExplicitTypeContext.GenericArgument
            && IsByRefLikeType(type, host))
        {
            return false;
        }

        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                return TryGetTotalGenericArity(type.Name, out int definitionArity)
                    && definitionArity == 0
                    && (AllowsVoid(context) || !IsCoreLibVoid(type))
                    && (AllowsRestrictedSpecialType(context)
                        || !IsRestrictedSpecialType(type))
                    && !CollidesWithInScopeName(type, host);

            case TypeRefKind.GenericInstance:
                return type.ElementType is
                    {
                        Kind: TypeRefKind.Definition,
                        Name: var definitionName,
                    } definition
                    && TryGetTotalGenericArity(definitionName, out int genericArity)
                    && genericArity > 0
                    && type.TypeArguments.Length == genericArity
                    && TypeIssue(definition) is null
                    && !CollidesWithInScopeName(type, host)
                    && type.TypeArguments.All(
                        argument => HasExplicitParameterTypeShape(
                            argument,
                            ExplicitTypeContext.GenericArgument,
                            host));

            case TypeRefKind.SzArray:
                return type.ElementType is { } arrayElement
                    && HasExplicitParameterTypeShape(
                        arrayElement,
                        ExplicitTypeContext.ArrayElement,
                        host);

            case TypeRefKind.Array:
                return type.ArrayShapeIsExact
                    && type.Rank is >= 2 and <= 32
                    && type.ElementType is { } mdArrayElement
                    && HasExplicitParameterTypeShape(
                        mdArrayElement,
                        ExplicitTypeContext.ArrayElement,
                        host);

            case TypeRefKind.ByRef:
                return AllowsByRef(context)
                    && type.ElementType is { } byRefElement
                    && HasExplicitParameterTypeShape(
                        byRefElement,
                        ExplicitTypeContext.Element,
                        host);

            case TypeRefKind.Pointer:
                return context != ExplicitTypeContext.GenericArgument
                    && type.ElementType is { } pointerElement
                    && HasExplicitParameterTypeShape(
                        pointerElement,
                        ExplicitTypeContext.PointerElement,
                        host);

            case TypeRefKind.Pinned:
            case TypeRefKind.Unsupported:
                return false;

            case TypeRefKind.GenericParameter:
                return GenericParameterIsInScope(
                    type,
                    host.DeclaringTypeGenericParameterNames,
                    host.Signature.GenericParameterNames);

            case TypeRefKind.MethodGenericParameter:
                return GenericParameterIsInScope(
                    type,
                    host.Signature.GenericParameterNames);

            case TypeRefKind.FunctionPointer:
                return context != ExplicitTypeContext.GenericArgument
                    && type.FunctionPointerSignatureIsExact
                    && IsSpellableFunctionPointerCallingConvention(type.CallingConvention)
                    && type.ElementType is { } returnType
                    && HasExplicitParameterTypeShape(
                        returnType,
                        ExplicitTypeContext.FunctionPointerReturn,
                        host)
                    && FunctionPointerParametersHaveExactShapes(type, host);

            default:
                return false;
        }
    }

    static bool FunctionPointerParametersHaveExactShapes(TypeRef type, IrFunction host)
    {
        if (type.FunctionPointerParameterRefKinds.Length != type.TypeArguments.Length)
            return false;
        for (int i = 0; i < type.TypeArguments.Length; i++)
        {
            var parameter = type.TypeArguments[i];
            var refKind = type.FunctionPointerParameterRefKinds[i];
            if ((parameter.Kind == TypeRefKind.ByRef) != (refKind != ArgumentRefKind.Value)
                || !HasExplicitParameterTypeShape(
                    parameter,
                    ExplicitTypeContext.FunctionPointerParameter,
                    host))
            {
                return false;
            }
        }
        return true;
    }

    static bool AllowsByRef(ExplicitTypeContext context)
        => context is ExplicitTypeContext.Parameter
            or ExplicitTypeContext.FunctionPointerParameter
            or ExplicitTypeContext.FunctionPointerReturn;

    static bool AllowsVoid(ExplicitTypeContext context)
        => context is ExplicitTypeContext.PointerElement
            or ExplicitTypeContext.FunctionPointerReturn;

    static bool AllowsRestrictedSpecialType(ExplicitTypeContext context)
        => context is ExplicitTypeContext.Parameter
            or ExplicitTypeContext.FunctionPointerParameter
            or ExplicitTypeContext.PointerElement;

    static bool GenericParameterIsInScope(
        TypeRef type,
        ImmutableArray<string> names,
        ImmutableArray<string> shadowingNames = default)
        => type.GenericParameterIndex >= 0
            && type.GenericParameterIndex < names.Length
            && type.GenericParameterName.Length > 0
            && names.Count(name => string.Equals(
                name,
                type.GenericParameterName,
                StringComparison.Ordinal)) == 1
            && (shadowingNames.IsDefault
                || !shadowingNames.Contains(
                    type.GenericParameterName,
                    StringComparer.Ordinal))
            && string.Equals(
                type.GenericParameterName,
                names[type.GenericParameterIndex],
                StringComparison.Ordinal);

    static bool IsSpellableFunctionPointerCallingConvention(string convention)
    {
        if (convention.Length == 0 || convention == "unmanaged")
            return true;
        const string prefix = "unmanaged[";
        if (!convention.StartsWith(prefix, StringComparison.Ordinal)
            || !convention.EndsWith(']'))
        {
            return false;
        }

        var parts = convention[prefix.Length..^1]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 3)
            return false;
        bool sawConvention = false;
        bool sawSuppressGcTransition = false;
        bool sawMemberFunction = false;
        foreach (var part in parts)
        {
            if (part == "SuppressGCTransition")
            {
                if (sawSuppressGcTransition)
                    return false;
                sawSuppressGcTransition = true;
                continue;
            }
            if (part == "MemberFunction")
            {
                if (sawMemberFunction)
                    return false;
                sawMemberFunction = true;
                continue;
            }
            if (part is not ("Cdecl" or "Stdcall" or "Thiscall" or "Fastcall")
                || sawConvention)
            {
                return false;
            }
            sawConvention = true;
        }
        return true;
    }

    static bool TryGetTotalGenericArity(string metadataName, out int total)
    {
        total = 0;
        foreach (var segment in metadataName.Split('+'))
        {
            int tick = segment.IndexOf('`');
            if (tick < 0)
                continue;
            if (tick == segment.Length - 1
                || segment.IndexOf('`', tick + 1) >= 0
                || !TryParseCanonicalArity(
                    segment.AsSpan(tick + 1),
                    out int arity)
                || total > int.MaxValue - arity)
            {
                total = 0;
                return false;
            }
            total += arity;
        }
        return true;
    }

    static bool TryParseCanonicalArity(
        ReadOnlySpan<char> text,
        out int arity)
    {
        arity = 0;
        if (text.IsEmpty || text[0] is < '1' or > '9')
            return false;
        foreach (char character in text)
        {
            if (character is < '0' or > '9'
                || arity > (int.MaxValue - (character - '0')) / 10)
            {
                arity = 0;
                return false;
            }
            arity = arity * 10 + character - '0';
        }
        return true;
    }

    static string MethodNameDiscriminator(string name)
    {
        if (GeneratedCodeIdentity.IsSynthesizedLambdaMethodName(name))
            return DecompilerFidelityDiscriminators.LambdaMethodName;
        if (GeneratedCodeIdentity.IsSynthesizedLocalFunctionName(name))
            return DecompilerFidelityDiscriminators.LocalFunctionMethodName;
        return IsGeneratedNameShape(name)
            ? DecompilerFidelityDiscriminators.GeneratedMethodName
            : DecompilerFidelityDiscriminators.UnspellableMethodName;
    }

    static string TypeNameDiscriminator(string name)
    {
        if (name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
            return DecompilerFidelityDiscriminators.DisplayClassTypeName;
        if (name == "<>c")
            return DecompilerFidelityDiscriminators.LambdaHolderTypeName;
        if (name.StartsWith("<", StringComparison.Ordinal)
            && name.Contains(">d__", StringComparison.Ordinal))
        {
            return DecompilerFidelityDiscriminators.StateMachineTypeName;
        }
        return IsGeneratedNameShape(name)
            ? DecompilerFidelityDiscriminators.GeneratedTypeName
            : DecompilerFidelityDiscriminators.UnspellableTypeName;
    }

    static bool IsGeneratedNameShape(string name)
        => name.StartsWith("<", StringComparison.Ordinal);

    static NameIssue Issue(string discriminator, string reason)
        => new(discriminator, reason);

    static bool IsCoreLibPrimitive(TypeRef type)
        => type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && s_coreLibPrimitiveNames.Contains(type.Name);

    // Only a canonical trailing `N is an arity suffix, so a segment whose backtick is
    // literal keeps it — and is then correctly reported as having no C# spelling
    // rather than silently truncated to a spellable name. See MetadataNameArity.
    static bool IsCoreLibVoid(TypeRef type)
        => type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && type.Name == "Void";

    static bool IsRestrictedSpecialType(TypeRef type)
        => type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && type.Name is "TypedReference" or "ArgIterator" or "RuntimeArgumentHandle";

    static bool IsCoreLibObject(TypeRef type)
        => type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && type.Name == "Object";

    static bool IsDynamicParameterType(TypeRef type, IrFunction host)
        => !IsInScopeName("dynamic", host)
            && IsCoreLibObject(
                type.Kind == TypeRefKind.ByRef && type.ElementType is { } element
                    ? element
                    : type);

    static bool CollidesWithInScopeName(TypeRef type, IrFunction host)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is not { Kind: TypeRefKind.Definition })
            return false;

        bool genericInstance = type.Kind == TypeRefKind.GenericInstance;
        bool nested = definition.Name.Contains('+');
        bool qualified = nested && type.PrintsAsQualifiedNestedName(host.DeclaringType);

        if (nested)
        {
            string first = StripArity(definition.Name.Split('+')[0]);
            if (IsInScopeGenericParameterName(first, host))
                return true;
        }

        if (!qualified
            && !(genericInstance && !nested)
            && IsInScopeGenericParameterName(SimpleMetadataName(definition.Name), host))
        {
            return true;
        }

        if (!qualified
            && !genericInstance
            && definition.Assembly == TypeRef.CoreLibrary
            && definition.Namespace == "System"
            && PrimitiveTypeNames.TryToKeywordForSystemType(definition.Name, out string? keyword)
            && IsContextualTypeKeyword(keyword)
            && IsInScopeGenericParameterName(keyword, host))
        {
            return true;
        }

        return CollidesWithDeclaringTypeSimpleName(definition, host, qualified)
            || CollidesWithVisibleNestedName(type, host, [type]);
    }

    internal static bool AnyLeadingSegmentShadowedByKnownTypes(
        IReadOnlyList<TypeRef> parameterTypes,
        IrFunction host)
    {
        for (int i = 0; i < parameterTypes.Count; i++)
        {
            if (AnyConstituentLeadingSegmentShadowed(parameterTypes[i], host, parameterTypes))
                return true;
        }

        return false;
    }

    internal static bool AnyPrintedAliasShadowedByKnownTypes(
        IReadOnlyList<TypeRef> parameterTypes,
        IReadOnlyList<bool> isDynamic,
        IrFunction host)
    {
        for (int i = 0; i < parameterTypes.Count; i++)
        {
            bool parameterIsDynamic = i < isDynamic.Count && isDynamic[i];
            if (AnyPrintedAliasInTypeShadowed(parameterTypes[i], parameterIsDynamic, host, parameterTypes))
                return true;
        }

        return false;
    }

    internal static bool AnyBareNameShadowedByKnownTypes(
        IReadOnlyList<TypeRef> parameterTypes,
        IrFunction host)
    {
        for (int i = 0; i < parameterTypes.Count; i++)
        {
            if (AnyBareNameInTypeShadowed(parameterTypes[i], host, parameterTypes))
                return true;
        }

        return false;
    }

    // TypeNameSegment uses body escape, so declaration-contextual keywords
    // (scoped, file, record, required, init) print bare. In an explicit
    // lambda parameter list those tokens parse as modifiers (CS0748/CS9048
    // for scoped). Decline rather than emit the invalid spelling. Reserved
    // keywords already print as @name and stay accepted.
    static bool AnyDeclarationContextualNamePrintedBare(TypeRef type)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (IsCoreLibPrimitive(type))
                    return false;
                foreach (var segment in type.Name.Split('+'))
                {
                    if (PrintsBareDeclarationContextualName(segment))
                        return true;
                }

                return false;

            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition
                    && AnyDeclarationContextualNamePrintedBare(definition))
                {
                    return true;
                }

                return AnyDeclarationContextualNamePrintedBareList(type.TypeArguments);

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element
                    && AnyDeclarationContextualNamePrintedBare(element);

            case TypeRefKind.GenericParameter:
            case TypeRefKind.MethodGenericParameter:
                return PrintsBareDeclarationContextualName(type.GenericParameterName);

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType
                    && AnyDeclarationContextualNamePrintedBare(returnType))
                {
                    return true;
                }

                return AnyDeclarationContextualNamePrintedBareList(type.TypeArguments);

            default:
                return false;
        }
    }

    static bool AnyDeclarationContextualNamePrintedBareList(IReadOnlyList<TypeRef> types)
    {
        for (int i = 0; i < types.Count; i++)
        {
            if (AnyDeclarationContextualNamePrintedBare(types[i]))
                return true;
        }

        return false;
    }

    static bool PrintsBareDeclarationContextualName(string metadataName)
    {
        if (metadataName.Length == 0)
            return false;

        string simple = StripArity(metadataName);
        string printed = CSharpNaming.TypeNameSegment(simple);
        return printed == simple
            && CSharpIdentifier.ContainIdentifierForDeclaration(simple) != printed;
    }

    internal static bool AnyPrintedNameIdentityCollision(
        IReadOnlyList<TypeRef> parameterTypes,
        IReadOnlyList<bool> isDynamic,
        IrFunction host)
    {
        var seen = new List<(string Name, int Arity, TypeRef Identity)>();
        for (int i = 0; i < parameterTypes.Count; i++)
        {
            if (WalkPrintedNameIdentities(
                    parameterTypes[i],
                    i < isDynamic.Count && isDynamic[i],
                    host,
                    seen))
            {
                return true;
            }
        }

        return false;
    }

    static bool WalkPrintedNameIdentities(
        TypeRef type,
        bool isDynamic,
        IrFunction host,
        List<(string Name, int Arity, TypeRef Identity)> seen)
    {
        if (TryGetPrintedContextualAlias(type, isDynamic, out string? alias)
            && alias is not null
            && RecordPrintedName(seen, alias, arity: 0, PrintedAliasIdentity(type)))
        {
            return true;
        }

        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is { Kind: TypeRefKind.Definition }
            && !IsCoreLibPrimitive(definition)
            && RecordDefinitionPrintedName(definition, type, host, seen))
        {
            return true;
        }

        switch (type.Kind)
        {
            case TypeRefKind.GenericInstance:
                return WalkPrintedNameIdentityList(type.TypeArguments, host, seen);

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element
                    && WalkPrintedNameIdentities(element, isDynamic: false, host, seen);

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType
                    && WalkPrintedNameIdentities(returnType, isDynamic: false, host, seen))
                {
                    return true;
                }

                return WalkPrintedNameIdentityList(type.TypeArguments, host, seen);

            default:
                return false;
        }
    }

    static bool WalkPrintedNameIdentityList(
        IReadOnlyList<TypeRef> types,
        IrFunction host,
        List<(string Name, int Arity, TypeRef Identity)> seen)
    {
        for (int i = 0; i < types.Count; i++)
        {
            if (WalkPrintedNameIdentities(types[i], isDynamic: false, host, seen))
                return true;
        }

        return false;
    }

    static bool RecordDefinitionPrintedName(
        TypeRef definition,
        TypeRef type,
        IrFunction host,
        List<(string Name, int Arity, TypeRef Identity)> seen)
    {
        if (definition.Name.Contains('+')
            && type.PrintsAsQualifiedNestedName(host.DeclaringType))
        {
            string first = definition.Name.Split('+')[0];
            return RecordPrintedName(
                seen,
                StripArity(first),
                ArityOf(first),
                TypeRef.Definition(definition.Assembly, definition.Namespace, first));
        }

        string last = definition.Name.Contains('+')
            ? definition.Name[(definition.Name.LastIndexOf('+') + 1)..]
            : definition.Name;
        return RecordPrintedName(seen, StripArity(last), ArityOf(last), definition);
    }

    static TypeRef PrintedAliasIdentity(TypeRef type)
    {
        var underlying = type.Kind == TypeRefKind.ByRef && type.ElementType is { } byRefElement
            ? byRefElement
            : type;
        return underlying.Kind == TypeRefKind.GenericInstance && underlying.ElementType is { } definition
            ? definition
            : underlying;
    }

    static bool RecordPrintedName(
        List<(string Name, int Arity, TypeRef Identity)> seen,
        string name,
        int arity,
        TypeRef identity)
    {
        for (int i = 0; i < seen.Count; i++)
        {
            if (seen[i].Name != name || seen[i].Arity != arity)
                continue;
            return !IsExactType(seen[i].Identity, identity);
        }

        seen.Add((name, arity, identity));
        return false;
    }

    static bool AnyBareNameInTypeShadowed(
        TypeRef type,
        IrFunction host,
        IReadOnlyList<TypeRef> knownTypes)
    {
        if (BarePrintedNameShadowed(type, host, knownTypes))
            return true;

        switch (type.Kind)
        {
            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition
                    && AnyBareNameInTypeShadowed(definition, host, knownTypes))
                {
                    return true;
                }

                return AnyBareNameListShadowed(type.TypeArguments, host, knownTypes);

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element
                    && AnyBareNameInTypeShadowed(element, host, knownTypes);

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType
                    && AnyBareNameInTypeShadowed(returnType, host, knownTypes))
                {
                    return true;
                }

                return AnyBareNameListShadowed(type.TypeArguments, host, knownTypes);

            default:
                return false;
        }
    }

    static bool AnyBareNameListShadowed(
        IReadOnlyList<TypeRef> types,
        IrFunction host,
        IReadOnlyList<TypeRef> knownTypes)
    {
        for (int i = 0; i < types.Count; i++)
        {
            if (AnyBareNameInTypeShadowed(types[i], host, knownTypes))
                return true;
        }

        return false;
    }

    static bool BarePrintedNameShadowed(
        TypeRef type,
        IrFunction host,
        IReadOnlyList<TypeRef> knownTypes)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is not { Kind: TypeRefKind.Definition } || IsCoreLibPrimitive(definition))
            return false;
        if (definition.Name.Contains('+') && type.PrintsAsQualifiedNestedName(host.DeclaringType))
            return false;

        var hostDefinition = HostDefinition(host);
        if (hostDefinition is null)
            return false;

        string last = definition.Name.Contains('+')
            ? definition.Name[(definition.Name.LastIndexOf('+') + 1)..]
            : definition.Name;
        return KnownTypesProveVisibleNestedName(
            knownTypes,
            hostDefinition,
            StripArity(last),
            ArityOf(last),
            excludeExact: definition);
    }

    static bool CollidesWithDeclaringTypeSimpleName(
        TypeRef definition,
        IrFunction host,
        bool qualified)
    {
        if (IsCoreLibPrimitive(definition))
            return CollidesWithPrintedKeyword(definition, host);

        var hostDefinition = host.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? host.DeclaringType.ElementType
            : host.DeclaringType;
        if (hostDefinition is not { Kind: TypeRefKind.Definition })
            return false;

        var hostSegments = hostDefinition.Name.Split('+');
        if (qualified)
        {
            var siblingSegments = definition.Name.Split('+');
            string first = siblingSegments[0];
            string firstSimple = StripArity(first);
            int firstArity = ArityOf(first);

            // A later sibling-chain segment with the same simple name and
            // arity is a nested type of some prefix. When that prefix is the
            // host or an ancestor, C# binds the leading identifier to the
            // nested type (Outer.Mid.Outer.Deep inside Outer.Mid).
            for (int k = 1; k < siblingSegments.Length; k++)
            {
                if (StripArity(siblingSegments[k]) != firstSimple
                    || ArityOf(siblingSegments[k]) != firstArity)
                {
                    continue;
                }

                string prefix = string.Join("+", siblingSegments, 0, k);
                if (definition.Assembly == hostDefinition.Assembly
                    && definition.Namespace == hostDefinition.Namespace
                    && (hostDefinition.Name == prefix
                        || hostDefinition.Name.StartsWith(prefix + "+", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            for (int i = hostSegments.Length - 1; i >= 0; i--)
            {
                if (StripArity(hostSegments[i]) != firstSimple
                    || ArityOf(hostSegments[i]) != firstArity)
                {
                    continue;
                }

                // Arity-aware lookup binds the leading segment to this host
                // chain entry. That is the sibling's own outermost type only
                // when it is the host's outermost type.
                bool sameIdentity = i == 0
                    && definition.Assembly == hostDefinition.Assembly
                    && definition.Namespace == hostDefinition.Namespace
                    && first == hostSegments[0];
                return !sameIdentity;
            }

            return false;
        }

        string last = definition.Name.Contains('+')
            ? definition.Name[(definition.Name.LastIndexOf('+') + 1)..]
            : definition.Name;
        string printed = StripArity(last);
        int printedArity = ArityOf(last);
        if (printed.Length == 0)
            return false;

        for (int i = hostSegments.Length - 1; i >= 0; i--)
        {
            if (StripArity(hostSegments[i]) != printed
                || ArityOf(hostSegments[i]) != printedArity)
                continue;

            string hostPrefix = string.Join("+", hostSegments, 0, i + 1);
            bool sameIdentity = definition.Assembly == hostDefinition.Assembly
                && definition.Namespace == hostDefinition.Namespace
                && definition.Name == hostPrefix;
            if (!sameIdentity)
                return true;
            return false;
        }

        return false;
    }

    static string SimpleMetadataName(string metadataName)
    {
        int nested = metadataName.LastIndexOf('+');
        return StripArity(nested < 0 ? metadataName : metadataName[(nested + 1)..]);
    }

    static int ArityOf(string metadataName)
    {
        int tick = metadataName.IndexOf('`');
        return tick >= 0 && int.TryParse(metadataName[(tick + 1)..], out int arity) ? arity : 0;
    }

    static bool CollidesWithPrintedKeyword(TypeRef definition, IrFunction host)
        => definition.Assembly == TypeRef.CoreLibrary
            && definition.Namespace == "System"
            && PrimitiveTypeNames.TryToKeywordForSystemType(definition.Name, out string? keyword)
            && IsContextualTypeKeyword(keyword)
            && IsInScopeName(keyword, host);

    static bool IsContextualTypeKeyword(string keyword)
        => keyword is "nint" or "nuint";

    static bool AnyConstituentLeadingSegmentShadowed(
        TypeRef type,
        IrFunction host,
        IReadOnlyList<TypeRef> knownTypes)
    {
        if (CollidesWithVisibleNestedName(type, host, knownTypes))
            return true;

        switch (type.Kind)
        {
            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition
                    && AnyConstituentLeadingSegmentShadowed(definition, host, knownTypes))
                {
                    return true;
                }

                return AnyTypeListLeadingSegmentShadowed(type.TypeArguments, host, knownTypes);

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element
                    && AnyConstituentLeadingSegmentShadowed(element, host, knownTypes);

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType
                    && AnyConstituentLeadingSegmentShadowed(returnType, host, knownTypes))
                {
                    return true;
                }

                return AnyTypeListLeadingSegmentShadowed(type.TypeArguments, host, knownTypes);

            default:
                return false;
        }
    }

    static bool AnyTypeListLeadingSegmentShadowed(
        IReadOnlyList<TypeRef> types,
        IrFunction host,
        IReadOnlyList<TypeRef> knownTypes)
    {
        for (int i = 0; i < types.Count; i++)
        {
            if (AnyConstituentLeadingSegmentShadowed(types[i], host, knownTypes))
                return true;
        }

        return false;
    }

    static bool AnyPrintedAliasInTypeShadowed(
        TypeRef type,
        bool isDynamic,
        IrFunction host,
        IReadOnlyList<TypeRef> knownTypes)
    {
        var hostDefinition = HostDefinition(host);
        return hostDefinition is not null
            && WalkPrintedAlias(type, isDynamic, hostDefinition, knownTypes);
    }

    static bool WalkPrintedAlias(
        TypeRef type,
        bool isDynamic,
        TypeRef hostDefinition,
        IReadOnlyList<TypeRef> knownTypes)
    {
        if (TryGetPrintedContextualAlias(type, isDynamic, out string? alias)
            && alias is not null
            && KnownTypesProveVisibleNestedName(knownTypes, hostDefinition, alias, arity: 0))
        {
            return true;
        }

        switch (type.Kind)
        {
            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition
                    && WalkPrintedAlias(definition, isDynamic: false, hostDefinition, knownTypes))
                {
                    return true;
                }

                return WalkPrintedAliasList(type.TypeArguments, hostDefinition, knownTypes);

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element
                    && WalkPrintedAlias(element, isDynamic: false, hostDefinition, knownTypes);

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType
                    && WalkPrintedAlias(returnType, isDynamic: false, hostDefinition, knownTypes))
                {
                    return true;
                }

                return WalkPrintedAliasList(type.TypeArguments, hostDefinition, knownTypes);

            default:
                return false;
        }
    }

    static bool WalkPrintedAliasList(
        IReadOnlyList<TypeRef> types,
        TypeRef hostDefinition,
        IReadOnlyList<TypeRef> knownTypes)
    {
        for (int i = 0; i < types.Count; i++)
        {
            if (WalkPrintedAlias(types[i], isDynamic: false, hostDefinition, knownTypes))
                return true;
        }

        return false;
    }

    static bool TryGetPrintedContextualAlias(TypeRef type, bool isDynamic, out string? alias)
    {
        var underlying = type.Kind == TypeRefKind.ByRef && type.ElementType is { } byRefElement
            ? byRefElement
            : type;
        if (isDynamic && IsCoreLibObject(underlying))
        {
            alias = "dynamic";
            return true;
        }

        var definition = underlying.Kind == TypeRefKind.GenericInstance
            ? underlying.ElementType
            : underlying;
        if (definition is { Kind: TypeRefKind.Definition }
            && definition.Assembly == TypeRef.CoreLibrary
            && definition.Namespace == "System"
            && PrimitiveTypeNames.TryToKeywordForSystemType(definition.Name, out string? keyword)
            && IsContextualTypeKeyword(keyword))
        {
            alias = keyword;
            return true;
        }

        alias = null;
        return false;
    }

    static bool KnownTypesProveVisibleNestedName(
        IReadOnlyList<TypeRef> knownTypes,
        TypeRef hostDefinition,
        string simpleName,
        int arity,
        TypeRef? excludeLeading = null,
        TypeRef? excludeExact = null)
    {
        for (int i = 0; i < knownTypes.Count; i++)
        {
            if (TypeTreeProvesVisibleNestedName(
                    knownTypes[i],
                    hostDefinition,
                    simpleName,
                    arity,
                    excludeLeading,
                    excludeExact))
            {
                return true;
            }
        }

        return false;
    }

    static bool CollidesWithVisibleNestedName(
        TypeRef type,
        IrFunction host,
        IReadOnlyList<TypeRef> knownTypes)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is not { Kind: TypeRefKind.Definition } || !definition.Name.Contains('+'))
            return false;
        if (!type.PrintsAsQualifiedNestedName(host.DeclaringType))
            return false;

        var hostDefinition = HostDefinition(host);
        if (hostDefinition is null)
            return false;

        string first = definition.Name.Split('+')[0];
        string firstSimple = StripArity(first);
        int firstArity = ArityOf(first);
        for (int i = 0; i < knownTypes.Count; i++)
        {
            if (TypeTreeProvesVisibleNestedName(
                    knownTypes[i],
                    hostDefinition,
                    firstSimple,
                    firstArity,
                    excludeLeading: definition))
            {
                return true;
            }
        }

        return false;
    }

    static bool TypeTreeProvesVisibleNestedName(
        TypeRef type,
        TypeRef hostDefinition,
        string simpleName,
        int arity,
        TypeRef? excludeLeading = null,
        TypeRef? excludeExact = null)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (excludeExact is { } exact
                    && (IsExactType(type, exact) || IsChildOfTopLevel(type, exact)))
                {
                    return false;
                }
                if (excludeLeading is { } candidate && IsOwnLeadingType(type, candidate))
                    return false;
                return TopLevelProvesVisibleName(type, hostDefinition, simpleName, arity)
                    || ChainProvesVisibleNestedName(type, hostDefinition, simpleName, arity);

            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition
                    && TypeTreeProvesVisibleNestedName(
                        definition,
                        hostDefinition,
                        simpleName,
                        arity,
                        excludeLeading,
                        excludeExact))
                {
                    return true;
                }

                for (int i = 0; i < type.TypeArguments.Length; i++)
                {
                    if (TypeTreeProvesVisibleNestedName(
                            type.TypeArguments[i],
                            hostDefinition,
                            simpleName,
                            arity,
                            excludeLeading,
                            excludeExact))
                    {
                        return true;
                    }
                }

                return false;

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                return type.ElementType is { } element
                    && TypeTreeProvesVisibleNestedName(
                        element,
                        hostDefinition,
                        simpleName,
                        arity,
                        excludeLeading,
                        excludeExact);

            case TypeRefKind.FunctionPointer:
                if (type.ElementType is { } returnType
                    && TypeTreeProvesVisibleNestedName(
                        returnType,
                        hostDefinition,
                        simpleName,
                        arity,
                        excludeLeading,
                        excludeExact))
                {
                    return true;
                }

                for (int i = 0; i < type.TypeArguments.Length; i++)
                {
                    if (TypeTreeProvesVisibleNestedName(
                            type.TypeArguments[i],
                            hostDefinition,
                            simpleName,
                            arity,
                            excludeLeading,
                            excludeExact))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    static bool TopLevelProvesVisibleName(
        TypeRef named,
        TypeRef hostDefinition,
        string simpleName,
        int arity)
    {
        if (named.Kind != TypeRefKind.Definition
            || named.Namespace != hostDefinition.Namespace)
        {
            return false;
        }

        string first = named.Name.Split('+')[0];
        return StripArity(first) == simpleName && ArityOf(first) == arity;
    }

    static bool IsOwnLeadingType(TypeRef named, TypeRef candidate)
        => named.Kind == TypeRefKind.Definition
            && named.Assembly == candidate.Assembly
            && named.Namespace == candidate.Namespace
            && named.Name.Split('+')[0] == candidate.Name.Split('+')[0];

    static bool IsExactType(TypeRef named, TypeRef candidate)
        => named.Kind == TypeRefKind.Definition
            && named.Assembly == candidate.Assembly
            && named.Namespace == candidate.Namespace
            && named.Name == candidate.Name;

    static bool IsChildOfTopLevel(TypeRef named, TypeRef topLevel)
        => named.Kind == TypeRefKind.Definition
            && topLevel.Kind == TypeRefKind.Definition
            && !topLevel.Name.Contains('+')
            && named.Assembly == topLevel.Assembly
            && named.Namespace == topLevel.Namespace
            && named.Name.StartsWith(topLevel.Name + "+", StringComparison.Ordinal);

    static bool ChainProvesVisibleNestedName(
        TypeRef named,
        TypeRef hostDefinition,
        string simpleName,
        int arity)
    {
        if (named.Kind != TypeRefKind.Definition
            || named.Assembly != hostDefinition.Assembly
            || named.Namespace != hostDefinition.Namespace
            || !named.Name.Contains('+'))
        {
            return false;
        }

        var segments = named.Name.Split('+');
        for (int k = 1; k < segments.Length; k++)
        {
            if (StripArity(segments[k]) != simpleName || ArityOf(segments[k]) != arity)
                continue;

            string prefix = string.Join("+", segments, 0, k);
            if (hostDefinition.Name == prefix
                || hostDefinition.Name.StartsWith(prefix + "+", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    static TypeRef? HostDefinition(IrFunction host)
    {
        var hostDefinition = host.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? host.DeclaringType.ElementType
            : host.DeclaringType;
        return hostDefinition is { Kind: TypeRefKind.Definition } ? hostDefinition : null;
    }

    static bool IsInScopeName(string name, IrFunction host)
        => IsInScopeGenericParameterName(name, host)
            || HostDeclaringChainHasArityZeroName(host, name);

    static bool HostDeclaringChainHasArityZeroName(IrFunction host, string name)
    {
        var hostDefinition = HostDefinition(host);
        if (hostDefinition is null)
            return false;

        foreach (var segment in hostDefinition.Name.Split('+'))
        {
            if (ArityOf(segment) == 0 && StripArity(segment) == name)
                return true;
        }

        return false;
    }

    static bool IsInScopeGenericParameterName(string name, IrFunction host)
        => host.DeclaringTypeGenericParameterNames.Contains(name, StringComparer.Ordinal)
            || host.Signature.GenericParameterNames.Contains(name, StringComparer.Ordinal);

    static bool IsByRefLikeType(TypeRef type, IrFunction host)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is null)
            return false;
        if (host.ByRefLikeTypes.Contains(definition))
            return true;
        if (definition.Namespace != "System")
            return false;
        int tick = definition.Name.IndexOf('`');
        string simple = tick < 0 ? definition.Name : definition.Name[..tick];
        return simple is "Span" or "ReadOnlySpan" or "TypedReference"
            or "ArgIterator" or "RuntimeArgumentHandle";
    }

    static string StripArity(string name)
        => MetadataNameArity.StripFromSegment(name);

    static readonly HashSet<string> s_coreLibPrimitiveNames = new(StringComparer.Ordinal)
    {
        "Boolean", "Byte", "SByte", "Char", "Int16", "UInt16", "Int32", "UInt32",
        "Int64", "UInt64", "Single", "Double", "Decimal", "IntPtr", "UIntPtr",
        "String", "Object", "Void",
    };
}
