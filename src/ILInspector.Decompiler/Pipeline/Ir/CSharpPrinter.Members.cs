using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Rendering helpers for member access, calls, and call arguments.</summary>
public sealed partial class CSharpPrinter
{
    string FieldTarget(FieldRef field, IrExpression? instance)
    {
        // An auto-property backing field, <Prop>k__BackingField, has no spellable
        // C# name; render it as the property it backs. `this.` qualifies the
        // instance form so a constructor assignment whose parameter shadows the
        // property still binds to it (and is legal even for a get-only property).
        if (CSharpNaming.BackingFieldProperty(field.Name) is { } property)
            return instance switch
            {
                null => $"{TypeText(field.DeclaringType)}.{property}",
                LoadArgument { Index: 0, Name: "this" } => $"this.{property}",
                _ => $"{ReceiverText(instance)}.{property}",
            };
        return instance switch
        {
            null => $"{TypeText(field.DeclaringType)}.{field.Name}",
            // A parameter or local with the same name shadows the field, so the
            // bare name binds to it, not the field (e.g. int Foo(int _x) =>
            // this._x + _x). Qualify with this. to reach the field; an
            // unshadowed instance field stays bare per the taste convention.
            LoadArgument { Index: 0, Name: "this" } => IsShadowedByLocal(field.Name) ? $"this.{field.Name}" : field.Name,
            _ => $"{ReceiverText(instance)}.{field.Name}",
        };
    }

    string PropertyTarget(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, string name, bool isVirtual = true)
    {
        string receiver = instance switch
        {
            // A NON-virtual this-receiver access to a base-declared member is
            // C#'s base. — the call opcode deliberately skips dispatch.
            LoadArgument { Index: 0, Name: "this" } when !isVirtual && IsCrossType(accessor.DeclaringType) => "base",
            null => TypeText(accessor.DeclaringType),
            LoadArgument { Index: 0, Name: "this" } => "",
            _ => ReceiverText(instance),
        };
        // An instance property accessor with index arguments IS an indexer,
        // whatever its metadata name (String's is Chars, not Item).
        if (instance is not null && indexArguments.Count > 0)
            return $"{(receiver.Length == 0 ? "this" : receiver)}[{Arguments(indexArguments)}]";
        string dotted = receiver.Length == 0 ? name : $"{receiver}.{name}";
        return indexArguments.Count == 0 ? dotted : $"{dotted}[{Arguments(indexArguments)}]";
    }

    /// <summary>True when the member's declaring DEFINITION differs from the function's — self-calls in generic types arrive as instantiations (List&lt;!0&gt;) and must not count as cross-type.</summary>
    bool IsCrossType(TypeRef memberDeclaringType)
    {
        static TypeRef Definition(TypeRef type)
            => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;
        return !Equals(Definition(memberDeclaringType), Definition(_function.DeclaringType));
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
        _ => Operand(receiver),
    };

    /// <summary>
    /// A method group for a delegate creation: a null target is a static
    /// method group (Type.Method); a this-receiver drops the qualifier to match
    /// instance-call spelling; any other receiver qualifies the name.
    /// </summary>
    string MethodGroupText(MethodRef method, IrExpression target)
    {
        string name = CSharpNaming.MethodName(method.Name);
        if (target is Constant { Value: null })
            return $"{TypeText(method.DeclaringType)}.{name}";
        if (target is LoadArgument { Index: 0, Name: "this" })
            return name;
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
        string name = $"{CSharpNaming.MethodName(method.Name)}{typeArguments}";
        return IsCrossType(method.DeclaringType)
            ? $"&{TypeText(method.DeclaringType)}.{name}"
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
                return $"{ReceiverText(arguments[0])}.{CSharpNaming.MethodName(call.Callee.Name)}{typeArguments}({extensionArgs})";
            }
            // A static abstract/virtual interface member invoked through a type
            // parameter compiles to `constrained. T; call IInterface<…>::Method`.
            // C#'s spelling is the constrained type itself — `T.Method(args)` — not
            // the declaring interface (`INumberBase<T>.Method(args)` cannot invoke a
            // static abstract member: CS0119/CS0314). The constrained type is the
            // receiver, and the spelling recompiles to the same constrained call.
            if (call.ConstrainedTo is { } staticReceiver)
                return $"{TypeText(staticReceiver)}.{CSharpNaming.MethodName(call.Callee.Name)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
            return $"{TypeText(call.Callee.DeclaringType)}.{CSharpNaming.MethodName(call.Callee.Name)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
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
        if (receiver is LoadArgument { Index: 0, Name: "this" })
        {
            // Non-virtual this-receiver call to a base-declared method is
            // C#'s base.M() — the call opcode deliberately skips dispatch.
            return !call.IsVirtual && IsCrossType(call.Callee.DeclaringType)
                ? $"base.{CSharpNaming.MethodName(call.Callee.Name)}{typeArguments}({rest})"
                : $"{CSharpNaming.MethodName(call.Callee.Name)}{typeArguments}({rest})";
        }
        return $"{ReceiverText(receiver)}.{CSharpNaming.MethodName(call.Callee.Name)}{typeArguments}({rest})";
    }

    string Arguments(IEnumerable<IrExpression> arguments)
        => string.Join(", ", arguments.Select(Expression));

    static ImmutableArray<ArgumentRefKind> CallIndirectRefKinds(CallIndirect call)
        => [.. call.ParameterTypes.Select(t => t.Kind == TypeRefKind.ByRef ? ArgumentRefKind.Ref : ArgumentRefKind.Value)];

    /// <summary>
    /// Arguments paired positionally with the callee's parameter types, casting
    /// each to its parameter type where C# needs it (CS0266) — the call-site
    /// counterpart of the return/store boundary casts. Callers pass arguments
    /// that already align 1:1 with the parameters (the receiver of an instance
    /// call is dropped first), so index i maps to parameterTypes[i].
    /// </summary>
    string Arguments(IEnumerable<IrExpression> arguments, IReadOnlyList<TypeRef> parameterTypes, ImmutableArray<ArgumentRefKind> refKinds)
    {
        var parts = new List<string>();
        int i = 0;
        foreach (var argument in arguments)
        {
            var parameter = i < parameterTypes.Count ? parameterTypes[i] : null;
            var refKind = i < refKinds.Length ? refKinds[i] : ArgumentRefKind.Value;
            parts.Add(RefArgument(argument, parameter, refKind)
                ?? (parameter is not null ? CastValue(argument, parameter) : Expression(argument)));
            i++;
        }
        return string.Join(", ", parts);
    }

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
    string? RefArgument(IrExpression argument, TypeRef? parameter, ArgumentRefKind refKind)
    {
        if (parameter is not { Kind: TypeRefKind.ByRef } || refKind == ArgumentRefKind.Value)
            return null;
        // `in` accepts a value argument (the compiler introduces a temporary), so
        // any place- or value-spelling works and the keyword stays implicit.
        if (refKind == ArgumentRefKind.In)
            return ArgumentPlace(argument);
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
