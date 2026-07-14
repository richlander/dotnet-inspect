using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Rendering helpers for member access, calls, and call arguments.</summary>
public sealed partial class CSharpPrinter
{
    string FieldTarget(FieldRef field, IrExpression? instance)
    {
        if (PointerMemberReceiver(instance) is { } pointerReceiver)
        {
            var pointerMember = field.BackingPropertyName
                ?? CSharpNaming.PrimaryConstructorCaptureName(field.Name)
                ?? field.Name;
            return $"{pointerReceiver}->{CSharpNaming.EscapeIdentifier(pointerMember)}";
        }
        // An auto-property backing field, <Prop>k__BackingField, has no spellable
        // C# name; render it as the property it backs. `this.` qualifies the
        // instance form so a constructor assignment whose parameter shadows the
        // property still binds to it (and is legal even for a get-only property).
        if (field.BackingPropertyName is { } property)
            return instance switch
            {
                null => $"{TypeQualifierText(field.DeclaringType)}.{CSharpNaming.EscapeIdentifier(property)}",
                LoadArgument { Index: 0, Name: "this" } => $"this.{CSharpNaming.EscapeIdentifier(property)}",
                _ => $"{ReceiverText(instance)}.{CSharpNaming.EscapeIdentifier(property)}",
            };
        // A C# 12 primary-constructor capture field, <param>P, has no spellable C#
        // name; its source spelling is the primary-constructor parameter, which is
        // in scope across the whole type. Render it as an ordinary field named for
        // the parameter, with the same shadow qualification (the constructor's own
        // parameter shadows it, so `this.` reaches the field).
        if (CSharpNaming.PrimaryConstructorCaptureName(field.Name) is { } capture)
        {
            string captured = CSharpNaming.EscapeIdentifier(capture);
            return instance switch
            {
                null => $"{TypeQualifierText(field.DeclaringType)}.{captured}",
                LoadArgument { Index: 0, Name: "this" } => IsShadowedByLocal(capture) ? $"this.{captured}" : captured,
                _ => $"{ReceiverText(instance)}.{captured}",
            };
        }
        string fieldName = CSharpNaming.SafeIdentifier(field.Name);
        return instance switch
        {
            null => $"{TypeQualifierText(field.DeclaringType)}.{fieldName}",
            // A parameter or local with the same name shadows the field, so the
            // bare name binds to it, not the field (e.g. int Foo(int _x) =>
            // this._x + _x). Qualify with this. to reach the field; an
            // unshadowed instance field stays bare per the taste convention.
            LoadArgument { Index: 0, Name: "this" } => QualifyThisMember(field.Name, field.Type) ? $"this.{fieldName}" : fieldName,
            _ => $"{ReceiverText(instance)}.{fieldName}",
        };
    }

    string? PointerMemberReceiver(IrExpression? instance)
        => PointerReceiver(instance);

    string? PointerMethodReceiver(IrExpression? instance)
        => PointerReceiver(instance);

    string? PointerReceiver(IrExpression? instance)
    {
        return instance?.ResultType is { Kind: TypeRefKind.Pointer }
            ? PointerReceiverText(instance)
            : null;
    }

    string PointerReceiverText(IrExpression instance)
        => new Rendered(Expression(instance), CSharpPrecedence.Of(instance)).At(Precedence.Primary);

    string? PointerRefExtensionReceiver(MethodRef method, IrExpression? instance)
    {
        if (instance?.ResultType is not { Kind: TypeRefKind.Pointer, ElementType: { } pointee })
            return null;
        return method.ParameterTypes is [{ Kind: TypeRefKind.ByRef, ElementType: { } byRefTarget }, ..]
            && pointee.Equals(byRefTarget)
            ? PointerReceiverText(instance)
            : null;
    }

    string PropertyTarget(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, string name, bool isVirtual = true)
    {
        if (PointerMemberReceiver(instance) is { } pointerReceiver)
        {
            if (indexArguments.Count > 0)
                return $"(*{pointerReceiver})[{Arguments(indexArguments)}]";
            return $"{pointerReceiver}->{CSharpNaming.EscapeIdentifier(name)}";
        }

        string receiver = instance switch
        {
            // A NON-virtual this-receiver access to a base-declared member is
            // C#'s base. — the call opcode deliberately skips dispatch.
            LoadArgument { Index: 0, Name: "this" } when !isVirtual && IsCrossType(accessor.DeclaringType) => "base",
            null => TypeQualifierText(accessor.DeclaringType),
            // A parameter or local with the same name shadows the property, so a
            // bare read binds to it, not the property (e.g. the synthesized record
            // Deconstruct(out int X, ...) whose body reads this.X). Qualify with
            // this. to reach the property; an unshadowed instance property stays
            // bare per the taste convention, matching FieldTarget.
            LoadArgument { Index: 0, Name: "this" } => QualifyThisMember(name, AccessorValueType(accessor)) ? "this" : "",
            _ => ReceiverText(instance),
        };
        // An instance property accessor with index arguments IS an indexer,
        // whatever its metadata name (String's is Chars, not Item).
        if (instance is not null && indexArguments.Count > 0)
            return $"{(receiver.Length == 0 ? "this" : receiver)}[{Arguments(indexArguments)}]";
        string escapedName = CSharpNaming.EscapeIdentifier(name);
        string dotted = receiver.Length == 0 ? escapedName : $"{receiver}.{escapedName}";
        return indexArguments.Count == 0 ? dotted : $"{dotted}[{Arguments(indexArguments)}]";
    }

    bool QualifyThisMember(string memberName, TypeRef? valueType)
        => IsShadowedByLocal(memberName) || MemberNameCollidesWithTypeName(memberName, valueType);

    static bool MemberNameCollidesWithTypeName(string memberName, TypeRef? valueType)
        => valueType is not null
            && (CSharpNaming.EscapeIdentifier(memberName) == SimpleTypeName(valueType)
                || IsKnownBaseTypeCollision(memberName, valueType));

    static string? SimpleTypeName(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is not { Kind: TypeRefKind.Definition })
            return null;
        int nested = definition.Name.LastIndexOf('+');
        string innermost = nested < 0 ? definition.Name : definition.Name[(nested + 1)..];
        return CSharpNaming.TypeNameSegment(innermost);
    }

    static bool IsKnownBaseTypeCollision(string memberName, TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        // The product path does not load inspected assemblies to walk inheritance.
        // Keep this to well-known framework bases whose derived names frequently
        // appear as same-named instance properties, e.g. MethodInfo MemberInfo.
        return memberName == "MemberInfo"
            && definition is
            {
                Kind: TypeRefKind.Definition,
                Namespace: "System" or "System.Reflection",
                Name: "Type" or "TypeInfo" or "MemberInfo" or "MethodBase" or "MethodInfo" or "ConstructorInfo" or "PropertyInfo" or "FieldInfo" or "EventInfo",
            };
    }

    static TypeRef? AccessorValueType(MethodRef accessor)
        => accessor.Name.StartsWith("get_", StringComparison.Ordinal) ? accessor.ReturnType
            : accessor.ParameterTypes.Length > 0 ? accessor.ParameterTypes[^1]
            : null;

    /// <summary>True when the member's declaring DEFINITION differs from the function's — self-calls in generic types arrive as instantiations (List&lt;!0&gt;) and must not count as cross-type.</summary>
    bool IsCrossType(TypeRef memberDeclaringType)
    {
        static TypeRef Definition(TypeRef type)
            => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;
        return !Equals(Definition(memberDeclaringType), Definition(_function.DeclaringType));
    }

    /// <summary>
    /// True when a static-call target is the current type at its own
    /// instantiation: the same non-generic type, or the enclosing generic type
    /// instantiated with its own generic parameters in order
    /// (<c>C&lt;T0, T1, …&gt;</c>). A different instantiation (<c>C&lt;string&gt;</c>)
    /// or a method-type-parameter instantiation (<c>C&lt;!!0&gt;</c>) is a distinct
    /// type whose static member must stay qualified — an unqualified call would
    /// rebind to the enclosing instantiation and change which method runs.
    /// </summary>
    bool IsCurrentStaticScope(TypeRef calleeDeclaringType)
    {
        var scope = _function.DeclaringType;
        var scopeDefinition = scope is { Kind: TypeRefKind.GenericInstance, ElementType: { } sd } ? sd : scope;
        if (Equals(calleeDeclaringType, scope) || Equals(calleeDeclaringType, scopeDefinition))
            return true;
        if (calleeDeclaringType is not { Kind: TypeRefKind.GenericInstance, ElementType: { } definition }
            || !Equals(definition, scopeDefinition))
            return false;
        var arguments = calleeDeclaringType.TypeArguments;
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].Kind != TypeRefKind.GenericParameter || arguments[i].GenericParameterIndex != i)
                return false;
        }
        return true;
    }

    /// <summary>Member-access receivers: value-type receivers arrive by address in IL; C# spells the place itself, not its address.</summary>
    string ReceiverText(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress a => $"{LocalName(a.Index)}",
        LoadArgumentAddress a => a.Name,
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        // A value-type array element accessed by address (ldelema; the receiver
        // of a field/property/method on the element) spells the element place
        // itself — C# auto-takes the address. The bare `ref pairs[0]` spelling
        // would be CS1525 in this value position (`(ref pairs[0]).A`).
        LoadElementAddress e => $"{Operand(e.Array)}[{Expression(e.Index)}]",
        // An unbox yields a managed pointer to the value inside the box; a
        // member access on it spells the cast itself ((T)x), since C# auto-takes
        // the address. The `ref (T)x` form (the by-ref argument spelling) is
        // CS1525 "Invalid expression term 'ref'" in this value position.
        Unbox u => $"(({TypeText(u.Type)}){Operand(u.Operand)})",
        // A bare negative constant misbinds as a member-access receiver:
        // `-1.ToString()` parses as `-(1.ToString())` (CS0023). Operand treats a
        // Constant as an atom, so a receiver whose literal leads with a unary
        // `-`/`+` is parenthesized here (a string/char literal receiver is fine
        // bare, so it is left alone) — mirroring the cast receiver handling and
        // NeedsCastOperandParentheses (#2151).
        Constant when Operand(receiver) is [('-' or '+'), ..] literal => $"({literal})",
        // These nodes render as operator-like surface forms, not primary
        // member receivers. Operand leaves them bare in ordinary operand
        // positions, but member access would bind to a child or fail to parse:
        // `^1.M()`, `1..2.M()`, `++x.M()`, `r with { ... }.M()`.
        IndexFromEnd or RangeExpression or IncrementDecrement or WithExpression => $"({Expression(receiver)})",
        _ => Operand(receiver),
    };

    /// <summary>
    /// A method group for a delegate creation: a null target is a static
    /// method group (Type.Method); a this-receiver drops the qualifier to match
    /// instance-call spelling; any other receiver qualifies the name.
    /// </summary>
    string MethodGroupText(MethodRef method, IrExpression target)
    {
        string name = CSharpNaming.SourceMethodName(method.Name);
        if (target is Constant { Value: null })
            return $"{TypeQualifierText(method.DeclaringType)}.{name}";
        if (target is LoadArgument { Index: 0, Name: "this" })
            return name;
        if (PointerMethodReceiver(target) is { } pointerReceiver)
            return $"{pointerReceiver}->{name}";
        return $"{ReceiverText(target)}.{name}";
    }

    /// <summary>
    /// <c>&amp;Method</c> for a static method group. A same-type target — every
    /// local function, and members of the function's own type — needs no
    /// qualifier; a cross-type static method is qualified by its declaring type.
    /// Generic methods carry their type arguments (<c>&amp;Method&lt;int&gt;</c>).
    /// </summary>
    string AddressOfMethodText(AddressOfMethod node)
    {
        var method = node.Method;
        string typeArguments = method.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", method.TypeArguments.Select(TypeText))}>";
        string name = $"{CSharpNaming.SourceMethodName(method.Name)}{typeArguments}";
        return IsCrossType(method.DeclaringType)
            ? $"&{TypeQualifierText(method.DeclaringType)}.{name}"
            : $"&{name}";
    }

    string CallText(Call call)
    {
        var arguments = call.Arguments;
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        if (!call.Callee.HasThis)
        {
            // C# compiles user-defined operators TO these calls; the
            // operator spelling is the faithful inverse.
            if (IsOperatorCall(call))
                return OperatorSpelling(call)!;
            // An extension method's static call C.M(receiver, args) renders as the
            // instance form receiver.M(args) the source used. No IL anchor chooses
            // between the two forms (taste rule case 3), and the runtime writes the
            // instance form; only sugar on confirmed [Extension] evidence, and drop
            // the receiver from the parameter pairing (it is parameter 0).
            if (call.Callee.IsExtension == MetadataFactState.Yes && arguments.Count >= 1)
            {
                IReadOnlyList<TypeRef> restTypes = [.. call.Callee.ParameterTypes.Skip(1)];
                var restRefKinds = call.Callee.ParameterRefKinds.IsDefaultOrEmpty
                    ? call.Callee.ParameterRefKinds
                    : [.. call.Callee.ParameterRefKinds.Skip(1)];
                string extensionArgs = Arguments(arguments.Skip(1), restTypes, restRefKinds);
                if (PointerRefExtensionReceiver(call.Callee, arguments[0]) is { } extensionReceiver)
                    return $"{extensionReceiver}->{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({extensionArgs})";
                if (arguments[0].ResultType is { Kind: TypeRefKind.Pointer })
                    return $"{TypeQualifierText(call.Callee.DeclaringType)}.{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
                return $"{ReceiverText(arguments[0])}.{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({extensionArgs})";
            }
            // A static abstract/virtual interface member invoked through a type
            // parameter compiles to `constrained. T; call IInterface<…>::Method`.
            // C#'s spelling is the constrained type itself — `T.Method(args)` — not
            // the declaring interface (`INumberBase<T>.Method(args)` cannot invoke a
            // static abstract member: CS0119/CS0314). The constrained type is the
            // receiver, and the spelling recompiles to the same constrained call.
            if (call.ConstrainedTo is { } staticReceiver)
                return $"{TypeQualifierText(staticReceiver)}.{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
            // A static call to a member of the current type needs no type
            // qualifier — `M(args)`, not `SelfType.M(args)` — just as a this-
            // receiver instance call drops `this.` and a same-type static method
            // group drops its qualifier (see AddressOfMethodText). Cross-type
            // (incl. base-declared/inherited) static calls stay qualified. The
            // comparison is exact (including generic instantiation): a call to a
            // different instantiation of the current open type — e.g. `C<string>.M`
            // from within `C<T>` — must stay qualified so it does not silently
            // rebind to `C<T>.M`.
            // A static call to a member of the current type needs no type
            // qualifier — `M(args)`, not `SelfType.M(args)` — just as a this-
            // receiver instance call drops `this.` and a same-type static method
            // group drops its qualifier (see AddressOfMethodText). It stays
            // qualified when the target is a different type (incl. base-declared/
            // inherited members and a different instantiation of the enclosing
            // generic type), or when a local/parameter shadows the name — the type
            // qualifier is the only disambiguator for a static call (there is no
            // `this.`), so an unqualified call would bind to the local.
            string sourceName = CSharpNaming.SourceMethodName(call.Callee.Name);
            string staticName = $"{sourceName}{typeArguments}";
            string staticArgs = Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds);
            return IsCurrentStaticScope(call.Callee.DeclaringType) && !IsStaticCallNameShadowed(sourceName)
                ? $"{staticName}({staticArgs})"
                : $"{TypeQualifierText(call.Callee.DeclaringType)}.{staticName}({staticArgs})";
        }
        var receiver = arguments[0];
        string rest = Arguments(arguments.Skip(1), call.Callee.ParameterTypes, call.Callee.ParameterRefKinds);
        if (call.Callee.Name == ".ctor")
        {
            // A call (not newobj) to a constructor is only ever a this(...)/base(...)
            // chain — IL exposes no other way to invoke .ctor — so the receiver is
            // always `this`, however the import spelled it (a copied-this temp
            // included). Spell the chain keyword and drop the receiver; the
            // `receiver..ctor(...)` fallback would never be valid C#.
            string keyword = Equals(call.Callee.DeclaringType, _function.DeclaringType) ? "this" : "base";
            return $"{keyword}({rest})";
        }
        if (call.Callee.Name == "Invoke" && receiver is Lambda lambda)
            return $"(({TypeText(lambda.DelegateType)}){Operand(lambda)}).Invoke({rest})";
        if (receiver is LoadArgument { Index: 0, Name: "this" })
        {
            // Non-virtual this-receiver call to a base-declared method is
            // C#'s base.M() — the call opcode deliberately skips dispatch.
            return !call.IsVirtual && IsCrossType(call.Callee.DeclaringType)
                ? $"base.{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({rest})"
                : $"{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({rest})";
        }
        if (PointerMethodReceiver(receiver) is { } pointerReceiver)
            return $"{pointerReceiver}->{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({rest})";
        return $"{ReceiverText(receiver)}.{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({rest})";
    }

    /// <summary>
    /// A rectangular (multi-dimensional) array element/creation pseudo-member.
    /// The CLR models <c>int[,]</c> element get/set/address and construction as
    /// calls to runtime-generated members named <c>Get</c>/<c>Set</c>/<c>Address</c>
    /// and a rank-shaped <c>.ctor</c> — none of which are spellable C#. The
    /// receiver/target type being <see cref="TypeRefKind.Array"/> with
    /// <see cref="TypeRef.Rank"/> &gt;= 2 is the discriminator: a user type can
    /// never declare these, so a user-declared <c>Get</c>/<c>Set</c> on an
    /// ordinary type is left untouched.
    /// </summary>
    static bool IsMultiDimArrayType(TypeRef type) => type.Kind == TypeRefKind.Array && type.Rank >= 2;

    /// <summary>
    /// Lowers a rectangular-array <c>Get</c>/<c>Set</c>/<c>Address</c> pseudo-call
    /// to C# indexer syntax (<c>a[i, j]</c>, <c>a[i, j] = v</c>); null when the
    /// call is not one. <c>Get</c> and <c>Address</c> both spell as the place
    /// <c>a[i, j]</c> — a managed-ref <c>Address</c> reads back as its place
    /// exactly like a ref-returning call, so the enclosing ref/return context
    /// supplies the <c>ref</c> keyword (mirroring <see cref="LoadElementAddress"/>
    /// for single-dimensional arrays).
    /// </summary>
    string? MultiDimArrayAccessText(Call call)
    {
        var arguments = call.Arguments;
        if (!call.Callee.HasThis || arguments.Count == 0 || !IsMultiDimArrayType(call.Callee.DeclaringType))
            return null;
        int rank = call.Callee.DeclaringType.Rank;
        string receiver = Operand(arguments[0]);
        switch (call.Callee.Name)
        {
            case "Get" or "Address" when arguments.Count == rank + 1:
            {
                var indexArguments = arguments.Skip(1).Take(rank).ToArray();
                var indices = indexArguments.Select(Expression).ToArray();
                if (HasRepeatedStackSlot(indexArguments) || HasRepeatedGeneratedTempName(indices))
                    return null;
                return $"{receiver}[{string.Join(", ", indices)}]";
            }

            case "Set" when arguments.Count == rank + 2:
            {
                var indexArguments = arguments.Skip(1).Take(rank).ToArray();
                var indexTexts = indexArguments.Select(Expression).ToArray();
                if (HasRepeatedStackSlot(indexArguments) || HasRepeatedGeneratedTempName(indexTexts))
                    return null;
                string indices = string.Join(", ", indexTexts);
                // The Set signature is (i0, .., iN-1, value); its last parameter
                // is the element type, so cast the value exactly as a single-dim
                // StoreElement does.
                TypeRef? elementType = call.Callee.ParameterTypes.Length > rank ? call.Callee.ParameterTypes[rank] : null;
                string value = elementType is not null ? CoerceText(arguments[^1], elementType) : Expression(arguments[^1]);
                return $"{receiver}[{indices}] = {value}";
            }
            default:
                return null;
        }
    }

    string? MultiDimArrayElementText(LoadElement element)
    {
        if (element.Array.ResultType is not { } arrayType || !IsMultiDimArrayType(arrayType) || element.Index is not TupleExpression tuple)
            return null;
        return MultiDimArrayPlaceText(element.Array, tuple.Elements, "Get");
    }

    string? MultiDimArrayElementAddressText(LoadElementAddress element)
    {
        if (element.Array.ResultType is not { } arrayType || !IsMultiDimArrayType(arrayType) || element.Index is not TupleExpression tuple)
            return null;
        return MultiDimArrayPlaceText(element.Array, tuple.Elements, "Address");
    }

    string MultiDimArrayPlaceText(IrExpression array, IReadOnlyList<IrExpression> indices, string pseudoMember)
    {
        var indexTexts = indices.Select(Expression).ToArray();
        return HasRepeatedStackSlot(indices) || HasRepeatedGeneratedTempName(indexTexts)
            ? $"{Operand(array)}.{pseudoMember}({string.Join(", ", indexTexts)})"
            : $"{Operand(array)}[{string.Join(", ", indexTexts)}]";
    }

    /// <summary>
    /// Lowers the rank-length rectangular-array constructor (<c>new int[,](n0, n1)</c>)
    /// to C# array-creation syntax (<c>new int[n0, n1]</c>); null when the
    /// <c>newobj</c> is not one. Only the rank-length <c>.ctor</c> is spellable;
    /// the lower-bound <c>.ctor</c> (2*rank arguments, for a non-zero-based array)
    /// has no C# syntax, so it is left to honest degradation.
    /// </summary>
    string? MultiDimArrayCreationText(NewObject node)
    {
        if (!IsMultiDimArrayType(node.Constructor.DeclaringType) || node.Arguments.Count != node.Constructor.DeclaringType.Rank)
            return null;
        return ArrayCreationText(node.Constructor.DeclaringType.ElementType!, node.Arguments);
    }

    static bool HasRepeatedStackSlot(IEnumerable<IrExpression> expressions)
    {
        var seen = new HashSet<int>();
        foreach (var expression in expressions)
        {
            if (expression is LoadStackSlot load && !seen.Add(load.Slot))
                return true;
            foreach (var descendant in expression.Descendants.OfType<LoadStackSlot>())
                if (!seen.Add(descendant.Slot))
                    return true;
        }
        return false;
    }

    static bool HasRepeatedGeneratedTempName(IReadOnlyList<string> rendered)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string text in rendered)
        {
            if (text is ['V' or 'S', '_', ..] && !seen.Add(text))
                return true;
        }
        return false;
    }

    string ArrayCreationElementText(TypeRef type)
    {
        var current = type;
        while (current.Kind is TypeRefKind.SzArray or TypeRefKind.Array && current.ElementType is { } element)
            current = element;
        return TypeText(current);
    }

    string ArrayCreationText(TypeRef elementType, IEnumerable<IrExpression> lengths)
        => $"new {ArrayCreationElementText(elementType)}[{string.Join(", ", lengths.Select(Expression))}]{ArrayCreationSuffix(elementType)}";

    string ArrayCreationSuffix(TypeRef type)
    {
        var suffixes = new List<string>();
        var current = type;
        while (current.Kind is TypeRefKind.SzArray or TypeRefKind.Array && current.ElementType is { } element)
        {
            suffixes.Add(current.Kind == TypeRefKind.Array
                ? $"[{new string(',', Math.Max(0, current.Rank - 1))}]"
                : "[]");
            current = element;
        }
        return string.Concat(suffixes);
    }

    string Arguments(IEnumerable<IrExpression> arguments)
        => string.Join(", ", arguments.Select(Expression));

    static ImmutableArray<ArgumentRefKind> CallIndirectRefKinds(CallIndirect call)
        => call.ParameterRefKinds.IsDefaultOrEmpty
            ? [.. call.ParameterTypes.Select(t => t.Kind == TypeRefKind.ByRef ? ArgumentRefKind.Ref : ArgumentRefKind.Value)]
            : call.ParameterRefKinds;

    /// <summary>
    /// Arguments paired positionally with the callee's parameter types, casting
    /// each to its parameter type where C# needs it (CS0266) — the call-site
    /// counterpart of the return/store boundary casts. Callers pass arguments
    /// that already align 1:1 with the parameters (the receiver of an instance
    /// call is dropped first), so index i maps to parameterTypes[i].
    /// </summary>
    string Arguments(
        IEnumerable<IrExpression> arguments,
        IReadOnlyList<TypeRef> parameterTypes,
        ImmutableArray<ArgumentRefKind> refKinds,
        bool explicitIn = false,
        bool chainFidelityCasts = false)
    {
        var parts = new List<string>();
        int i = 0;
        foreach (var argument in arguments)
        {
            var parameter = i < parameterTypes.Count ? parameterTypes[i] : null;
            var refKind = i < refKinds.Length ? refKinds[i] : ArgumentRefKind.Value;
            if (RefArgument(argument, parameter, refKind, explicitIn) is { } refSpelling)
                parts.Add(refSpelling);
            else if (chainFidelityCasts && parameter is not null && refKind == ArgumentRefKind.Value
                && ChainFidelityCast(argument, parameter) is { } fidelityCast)
                parts.Add(fidelityCast);
            else
                parts.Add(parameter is not null ? CoerceText(argument, parameter) : Expression(argument));
            i++;
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// A constructor-chain argument spelled at its exact parameter type. C#
    /// overload resolution re-runs on the recompiled <c>: this(args)</c> /
    /// <c>: base(args)</c> initializer, so an argument whose natural spelling is a
    /// subtype of — or type-less against — its parameter (a <c>null</c> literal,
    /// an array flowing into a covariant interface, any reference upcast) can
    /// silently rebind to a narrower same-arity sibling overload. Spelling
    /// <c>(Param)arg</c> pins the parameter type the IL selected. IL verification
    /// guarantees the argument is already assignable to the parameter, so the cast
    /// is a widening reference conversion that emits no opcode — the recompiled
    /// body stays byte-identical. Only concrete reference-like parameters get the
    /// cast (value/numeric/enum/pointer/type-parameter parameters keep the
    /// coercion spelling). Null when no fidelity cast is needed: an identity
    /// argument, or one whose type is unknown.
    /// </summary>
    string? ChainFidelityCast(IrExpression argument, TypeRef parameter)
    {
        if (!IsConcreteReferenceParameter(parameter))
            return null;
        string paramText = TypeText(parameter);
        // A type-less null literal re-resolves against every sibling overload, so
        // it always needs its parameter type pinned.
        if (argument is Constant { Value: null })
            return $"({paramText})null";
        var argumentType = EffectiveType(argument);
        if (argumentType is null || argumentType.Equals(parameter))
            return null;
        return $"({paramText}){Operand(argument)}";
    }

    /// <summary>
    /// A parameter whose spelled type is a concrete, nameable reference type (a
    /// class, interface, delegate, or array) — the targets for which a widening
    /// reference conversion is a no-op the recompiled IL reproduces exactly.
    /// Excludes by-ref/pointer/function-pointer, value/enum/numeric, and bare
    /// type-parameter parameters (where a <c>(T)</c> cast can box or unbox).
    /// </summary>
    bool IsConcreteReferenceParameter(TypeRef parameter)
        => parameter.Kind is TypeRefKind.Definition or TypeRefKind.GenericInstance
            or TypeRefKind.SzArray or TypeRefKind.Array
            && IsReferenceLike(parameter);

    /// <summary>
    /// Spells a by-ref argument with the keyword its parameter demands:
    /// <c>out</c>, <c>in</c> (no keyword — the readonly ref is implicit), or
    /// <c>ref</c>. A managed pointer forwarded to a <c>ref</c>/<c>out</c>
    /// parameter needs the keyword at the call site (CS1620); spelling it on an
    /// <c>in</c> parameter is the inverse error (CS1615), so the address-of
    /// node's own <c>ref</c> is dropped there. Null when the kind is unknown (a
    /// cross-assembly MemberRef carries no parameter rows) or the argument is not
    /// a simple place — both leave the existing spelling untouched.
    /// </summary>
    string? RefArgument(IrExpression argument, TypeRef? parameter, ArgumentRefKind refKind, bool explicitIn)
    {
        if (parameter is not { Kind: TypeRefKind.ByRef } || refKind == ArgumentRefKind.Value)
            return null;
        // `in` accepts a value argument (the compiler introduces a temporary), so
        // any place- or value-spelling works and the keyword stays implicit.
        if (refKind == ArgumentRefKind.In)
            return (explicitIn ? ArgumentLvalue(argument) : ArgumentPlace(argument)) is { } inPlace
                ? explicitIn ? $"in {inPlace}" : inPlace
                : null;
        // `out`/`ref` require a genuine assignable lvalue; a cast (unbox) is not
        // one (`out (T)x` is CS0206), so leave those to the default spelling.
        if (ArgumentLvalue(argument) is not { } place)
            return null;
        return refKind == ArgumentRefKind.Out ? $"out {place}" : $"ref {place}";
    }

    /// <summary>
    /// The bare place of a by-ref argument — without any <c>ref</c> the keyword
    /// renderer adds itself. Address-of nodes read back as their place; a by-ref
    /// value (ref local/parameter, ref-returning call) already renders as a bare
    /// place. Null for forms that are not a single place (a ref ternary binds
    /// <c>ref</c> per arm), leaving them to the default spelling.
    /// </summary>
    string? ArgumentPlace(IrExpression argument) => argument switch
    {
        LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress => Deref(argument),
        Unbox u => $"({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocal or LoadArgument or LoadStackSlot or LoadIndirect or Call or CallIndirect => Expression(argument),
        _ => null,
    };

    /// <summary>
    /// The subset of <see cref="ArgumentPlace"/> that is a genuine assignable
    /// lvalue — what <c>out</c>/<c>ref</c> demand. Excludes the <see cref="Unbox"/>
    /// cast form (an lvalue only `in` can accept, as a value).
    /// </summary>
    string? ArgumentLvalue(IrExpression argument) => argument switch
    {
        LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress => Deref(argument),
        // A ref-typed value already names a place: a ref local/parameter, a
        // ref-returning call, or a ref slot the importer spilled the managed
        // pointer into (a ref argument evaluated before a later side-effecting
        // argument). Each renders as a bare name the ref/out keyword prefixes.
        LoadLocal or LoadArgument or LoadStackSlot or LoadIndirect or Call or CallIndirect => Expression(argument),
        _ => null,
    };
}
