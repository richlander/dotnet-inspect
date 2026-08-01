using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Rendering helpers for member access, calls, and call arguments.</summary>
public sealed partial class CSharpPrinter
{
    // The library-owned catalog descriptors for the four this.-qualification
    // knobs. Sourcing identity and config key from StyleOptionCatalog (#3160)
    // keeps the recorded decision's RuleId and prose in lockstep with the single
    // source of truth: a knob rename fails here at type init rather than drifting.
    static readonly StyleOptionDescriptor QualifyFieldOption = QualificationKnob("qualify-field-access");
    static readonly StyleOptionDescriptor QualifyPropertyOption = QualificationKnob("qualify-property-access");
    static readonly StyleOptionDescriptor QualifyMethodOption = QualificationKnob("qualify-method-access");
    static readonly StyleOptionDescriptor QualifyEventOption = QualificationKnob("qualify-event-access");

    static StyleOptionDescriptor QualificationKnob(string id)
        => StyleOptionCatalog.Options.Single(o => o.Id == id);

    // A this.-qualification KNOB (not mandatory shadow disambiguation) added the
    // this. qualifier, so record a byte-preserving taste decision the Applied
    // Taste surface can report. Only the knob-attributed path calls this (the knob
    // is enabled AND the bare name already binds); a mandatory disambiguation
    // this. — one that would appear with the knob off too — is never recorded as a
    // taste choice. AddDecision dedups on the full row (plus dedupDiscriminator, so
    // two same-named overloads stay distinct), so repeated accesses to one member
    // collapse to a single decision.
    void RecordThisQualificationDecision(StyleOptionDescriptor knob, string memberName, string bareName, string? dedupDiscriminator = null)
    {
        // A this. qualifier is only a byte-preserving taste choice when there is a
        // genuine instance receiver. A static or extension method whose first
        // parameter is spelled `this` (its IL parameter name is "this") reaches
        // these sites as a LoadArgument{Index:0, Name:"this"}, but that is an
        // explicit parameter, not an implicit receiver: qualifying it with this.
        // would be a compile error, never an opt-in spelling, so record nothing.
        if (!_function.Signature.HasThis)
            return;
        AddDecision(
            knob.Id,
            DecompilerDecisionCategories.Taste,
            memberName,
            $"Qualified instance member '{memberName}' with 'this.' ({knob.ConfigKey}). "
                + "Byte-preserving: the bare name already binds to the member, so the "
                + "qualifier is an opt-in spelling choice, not disambiguation.",
            oldValue: bareName,
            newValue: $"this.{bareName}",
            dedupDiscriminator: dedupDiscriminator);
    }

    // A method this. qualifier is a recordable byte-preserving taste choice only
    // when it is a genuine, user-authored opt-in — the method analogue of the
    // QualifyThisMember guard the field/property/event sites already apply:
    //  * the bare name must still bind to the method. IsStaticCallNameShadowed is
    //    the scope-aware shadow check (it counts the enclosing method's locals and
    //    parameters plus every nested lambda / local-function binder in scope), so
    //    when a same-named binder would capture the bare call the this. is
    //    mandatory disambiguation, not a choice, and records nothing;
    //  * the target must be a speakable source method. A compiler-generated lambda
    //    or local-function group target (an unspeakable <M>b__0-style name) is
    //    never user-authored, so its this. is never a taste choice.
    // The genuine-instance-receiver requirement is enforced by
    // RecordThisQualificationDecision. Overloads share a name, so the callee
    // parameter types disambiguate the dedup key.
    //  * displayName is the escaped C# spelling (post CSharpNaming.SourceMethodName)
    //    used for the shadow check and the recorded row;
    //  * rawName is the unsanitized IL metadata name, checked for unspeakability
    //    BEFORE SourceMethodName strips its <...> decoration (otherwise a lifted
    //    local function arrives already spelled as a plain identifier and slips
    //    past the check);
    //  * declaringType gates same-type membership: only a call whose callee is
    //    declared on the enclosing type is a `this.` opt-in (see below).
    void RecordMethodQualificationIfTaste(
        string displayName,
        string rawName,
        TypeRef declaringType,
        int genericArity,
        ImmutableArray<TypeRef> parameterTypes,
        ImmutableArray<TypeRef> definitionParameterTypes = default)
    {
        // Only a call to the enclosing type AT ITS OWN INSTANTIATION is a
        // byte-preserving `this.` taste choice. A callee reached here that is not
        // the exact self-type is one of:
        //  * an inherited base method (the non-virtual case already rendered
        //    base.M above; the virtual case would rebind under this./bare);
        //  * an explicit interface implementation invoked through this — which
        //    does not bind via `this.` at all (it requires a cast) and is only
        //    mis-rendered as this.M by a pre-existing emit gap;
        //  * a DIFFERENT instantiation of the enclosing generic type, e.g.
        //    ((I<object>)this).M() from within I<T> — bare/`this.` M() binds to
        //    I<T>::M, not I<object>::M, so the qualifier is not byte-preserving.
        // Definition-only equality (IsCrossType) is too loose for the last case,
        // so gate on the exact-instantiation test. This deliberately under-records
        // a legitimate this.BaseMethod(); a false-negative is safe, a
        // false-positive is not.
        if (!IsEnclosingTypeAtOwnInstantiation(declaringType))
            return;
        if (IsStaticCallNameShadowed(displayName) || IsUnspeakableName(rawName))
            return;
        // A generic method call's ParameterTypes are already substituted against
        // its MethodSpec (T -> int), so this.G<int>(x) and this.G<string>(x) of one
        // G<T>(T) would key apart and record TWO rows for a single source member.
        // Key on the DEFINITION signature (T left as !!0) so all instantiations of
        // one method collapse into one row, while distinct non-generic overloads
        // M(List<int>)/M(List<string>) still key apart on their concrete types.
        var keyParameterTypes = genericArity > 0 && !definitionParameterTypes.IsDefaultOrEmpty
            ? definitionParameterTypes
            : parameterTypes;
        RecordThisQualificationDecision(
            QualifyMethodOption, displayName, displayName, MethodOverloadDiscriminator(genericArity, keyParameterTypes));
    }

    // Compiler-generated members (captured-this lambdas, local-function group
    // targets) carry unspeakable names bracketed with angle brackets, which a
    // source identifier can never contain. Feed this the RAW metadata name: a
    // lifted local function's <Outer>g__Local|0_0 keeps its brackets there, but
    // CSharpNaming.SourceMethodName has already stripped them from the display
    // name.
    static bool IsUnspeakableName(string name)
        => name.IndexOf('<') >= 0 || name.IndexOf('>') >= 0;

    // A stable, structurally-complete per-overload key. Overloads share a display
    // name, so the decision subject alone would dedup two distinct methods into
    // one row. The key must distinguish every element the runtime treats as part
    // of an overload's signature — generic ARITY (M() vs M<T>() are distinct
    // overloads with identical empty parameter lists), generic instantiation
    // (List<int> vs List<string>), array element type and rank, by-ref/pointer
    // decoration, and generic-parameter slot — and must keep the full namespace +
    // assembly (ToDisplayString strips the namespace, so it would collapse
    // NsA.Widget and NsB.Widget). Under-distinguishing would merge distinct
    // overloads and HIDE a real taste application, so this errs toward more
    // distinctions, never fewer. For a generic method the caller passes the
    // DEFINITION parameter types (T as !!0), so the two instantiations
    // this.M<int>() and this.M<string>() key identically and stay one row (one
    // source member) even when a parameter mentions T.
    static string MethodOverloadDiscriminator(int genericArity, ImmutableArray<TypeRef> parameterTypes)
        => $"`{genericArity}`:"
            + (parameterTypes.IsDefaultOrEmpty ? "" : string.Join(",", parameterTypes.Select(TypeKey)));

    static string TypeKey(TypeRef type) => type.Kind switch
    {
        TypeRefKind.GenericInstance =>
            $"{TypeKey(type.ElementType!)}<{string.Join(",", type.TypeArguments.Select(TypeKey))}>",
        TypeRefKind.SzArray => $"{TypeKey(type.ElementType!)}[]",
        TypeRefKind.Array => $"{TypeKey(type.ElementType!)}[{type.Rank}]",
        TypeRefKind.ByRef => $"ref {TypeKey(type.ElementType!)}",
        TypeRefKind.Pointer => $"{TypeKey(type.ElementType!)}*",
        TypeRefKind.Pinned => $"pinned {TypeKey(type.ElementType!)}",
        TypeRefKind.GenericParameter => $"!{type.GenericParameterIndex}",
        TypeRefKind.MethodGenericParameter => $"!!{type.GenericParameterIndex}",
        TypeRefKind.FunctionPointer => FunctionPointerKey(type),
        _ => $"{type.Assembly}:{type.Namespace}.{type.Name}",
    };

    // A function-pointer type's identity includes its calling convention, return
    // type, and each parameter's ref-kind and type — all part of overload identity.
    // Keying on parameter types alone would collapse delegate*<int, void> vs
    // delegate*<int, int>, or a managed vs unmanaged pointer, into one row.
    static string FunctionPointerKey(TypeRef type)
    {
        var refKinds = type.FunctionPointerParameterRefKinds;
        string parameters = string.Join(
            ",",
            type.TypeArguments.Select((p, i) =>
                $"{(i < refKinds.Length ? refKinds[i].ToString() : "None")} {TypeKey(p)}"));
        return $"fnptr[{type.CallingConvention}]({parameters})->{TypeKey(type.ElementType!)}";
    }

    string FieldTarget(FieldRef field, IrExpression? instance)
    {
        if (PointerMemberReceiver(instance) is { } pointerReceiver)
        {
            var pointerMember = field.BackingPropertyName
                ?? CSharpNaming.PrimaryConstructorCaptureName(field.Name)
                ?? field.Name;
            return $"{pointerReceiver}->{CSharpNaming.ContainedIdentifier(pointerMember)}";
        }
        // An auto-property backing field, <Prop>k__BackingField, has no spellable
        // C# name; render it as the property it backs. `this.` qualifies the
        // instance form so a constructor assignment whose parameter shadows the
        // property still binds to it (and is legal even for a get-only property).
        if (field.BackingPropertyName is { } property)
            return instance switch
            {
                null => $"{TypeQualifierText(field.DeclaringType)}.{CSharpNaming.ContainedIdentifier(property)}",
                LoadArgument { Index: 0, Name: "this" } => $"this.{CSharpNaming.ContainedIdentifier(property)}",
                _ => $"{ReceiverText(instance)}.{CSharpNaming.ContainedIdentifier(property)}",
            };
        // A C# 12 primary-constructor capture field, <param>P, has no spellable C#
        // name; its source spelling is the primary-constructor parameter, which is
        // in scope across the whole type. Render it as an ordinary field named for
        // the parameter, with the same shadow qualification (the constructor's own
        // parameter shadows it, so `this.` reaches the field).
        if (CSharpNaming.PrimaryConstructorCaptureName(field.Name) is { } capture)
        {
            string captured = CSharpNaming.ContainedIdentifier(capture);
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
            LoadArgument { Index: 0, Name: "this" } => FieldThisTarget(field, fieldName),
            _ => $"{ReceiverText(instance)}.{fieldName}",
        };
    }

    // The this-receiver plain-field branch of FieldTarget. Emits this. when the
    // qualify-field knob is set OR a local shadows the field (mandatory), matching
    // the prior inline predicate exactly, and records a decision only on the
    // knob-attributed path (knob set AND no shadow forcing it).
    string FieldThisTarget(FieldRef field, string fieldName)
    {
        bool mandatory = QualifyThisMember(field.Name, field.Type);
        if (!_options.QualifyFieldAccess && !mandatory)
            return fieldName;
        // Record only a byte-preserving opt-in: the field must be declared on the
        // enclosing type at its own instantiation. A base-declared field that is
        // hidden by a `new` field of the same name reaches here spelled this.X (a
        // pre-existing emit gap — the load targets base.X), but this.X binds to the
        // DERIVED field, so recording it as byte-preserving would be a false
        // positive. Gating on the exact-instantiation test also under-records a
        // legitimate this. on a merely-inherited field; a false-negative is safe.
        if (_options.QualifyFieldAccess && !mandatory && IsEnclosingTypeAtOwnInstantiation(field.DeclaringType))
            RecordThisQualificationDecision(QualifyFieldOption, field.Name, fieldName);
        return $"this.{fieldName}";
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

    string PropertyTarget(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, string name, bool isVirtual = true, bool isEvent = false)
    {
        if (PointerMemberReceiver(instance) is { } pointerReceiver)
        {
            if (indexArguments.Count > 0)
                return $"(*{pointerReceiver})[{Arguments(indexArguments)}]";
            return $"{pointerReceiver}->{CSharpNaming.ContainedIdentifier(name)}";
        }

        bool thisQualifiedByKnob = false;
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
            // bare per the taste convention, matching FieldTarget. An event
            // subscription routes through here too, so it honors the separate
            // event qualification knob rather than the property one.
            LoadArgument { Index: 0, Name: "this" } => ThisPropertyReceiver(name, accessor, isEvent, out thisQualifiedByKnob),
            _ => ReceiverText(instance),
        };
        // An instance property accessor with index arguments IS an indexer,
        // whatever its metadata name (String's is Chars, not Item). An indexer
        // always renders this[...] (never this.Item), so the qualify knob makes no
        // textual difference there; this early return precedes the knob-attributed
        // decision so an indexer never records one.
        if (instance is not null && indexArguments.Count > 0)
            return $"{(receiver.Length == 0 ? "this" : receiver)}[{Arguments(indexArguments)}]";
        string escapedName = CSharpNaming.ContainedIdentifier(name);
        // Same byte-preserving gate as the field and method sites: a property or
        // event declared on a base type (hidden or inherited) reached through this.
        // is not a self-type opt-in, so it must not record. A virtual inherited
        // accessor binds identically under this./bare and would be safe, but the
        // exact-instantiation test uniformly under-records the cross-type cases; a
        // false-negative is safe, a false-positive is not.
        if (thisQualifiedByKnob && IsEnclosingTypeAtOwnInstantiation(accessor.DeclaringType))
            RecordThisQualificationDecision(isEvent ? QualifyEventOption : QualifyPropertyOption, name, escapedName);
        string dotted = receiver.Length == 0 ? escapedName : $"{receiver}.{escapedName}";
        return indexArguments.Count == 0 ? dotted : $"{dotted}[{Arguments(indexArguments)}]";
    }

    // The this-receiver property/event branch of PropertyTarget: returns the
    // receiver text ("this" when qualified, "" when bare) and reports through
    // <paramref name="qualifiedByKnob"/> whether the qualify KNOB (not mandatory
    // shadow disambiguation) selected the qualifier — the only case that is an
    // opt-in taste choice worth recording.
    string ThisPropertyReceiver(string name, MethodRef accessor, bool isEvent, out bool qualifiedByKnob)
    {
        bool knob = isEvent ? _options.QualifyEventAccess : _options.QualifyPropertyAccess;
        bool mandatory = QualifyThisMember(name, AccessorValueType(accessor));
        qualifiedByKnob = knob && !mandatory;
        return knob || mandatory ? "this" : "";
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
    /// True when a call target is the enclosing type at its OWN instantiation: the
    /// same non-generic type, or the enclosing generic type instantiated with its
    /// own generic parameters in order (<c>C&lt;T0, T1, …&gt;</c>). A different
    /// instantiation (<c>C&lt;string&gt;</c>) or a method-type-parameter
    /// instantiation (<c>C&lt;!!0&gt;</c>) is a DISTINCT type sharing only the
    /// definition. Two spelling paths depend on the exact-instantiation test: a
    /// static call must stay type-qualified (an unqualified call would rebind to
    /// the enclosing instantiation and change which method runs), and a
    /// this-qualification taste decision must not be recorded (bare/`this.` would
    /// rebind to the enclosing instantiation, so the qualifier is not
    /// byte-preserving). Definition equality alone (see <see cref="IsCrossType"/>)
    /// is too loose for both.
    /// </summary>
    bool IsEnclosingTypeAtOwnInstantiation(TypeRef calleeDeclaringType)
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

    /// <summary>
    /// True when a <c>this</c>-receiver access to a member of
    /// <paramref name="declaringType"/> is reached through an implicit
    /// class→interface (or variant interface) upcast that emits no IL, so the
    /// printer must re-insert the erased <c>((I)this)</c> cast. A default
    /// interface member — and an explicit interface implementation — is not a
    /// member of the implementing class, so bare <c>Member</c>/<c>this.Member</c>
    /// fails to bind (CS1061); the callee's declaring interface is the only
    /// receiver type that binds it. Requires a CONFIRMED interface declaring type
    /// (absent from <see cref="IrFunction.InterfaceTypes"/> — e.g. a
    /// cross-assembly type whose interface-ness cannot be proven — declines,
    /// never guesses) that is not the enclosing type at its own instantiation: an
    /// interface's own default member reached from inside that same interface
    /// instantiation stays bare, while a different instantiation
    /// (<c>I&lt;object&gt;</c> from within <c>I&lt;T&gt;</c>) still needs the cast
    /// so the call does not rebind to the enclosing instantiation.
    /// </summary>
    bool IsInterfaceCastThisReceiver(TypeRef declaringType)
        => _function.InterfaceTypes.Contains(NamedDefinition(declaringType))
            && !IsEnclosingTypeAtOwnInstantiation(declaringType);

    /// <summary>
    /// True when <paramref name="receiver"/> (or method-group target) is the
    /// enclosing value type's <c>this</c> boxed to reach a member of a type the
    /// struct was cast to. A struct must box to invoke a base
    /// (<c>System.ValueType</c>/<c>System.Object</c>) or interface member, so
    /// <c>((T)this).M()</c> / <c>base.M()</c> lowers to
    /// <c>ldarg.0; ldobj S; box S; call[virt] T::M</c> — the receiver is a
    /// <see cref="Box"/> over the dereferenced <c>this</c> pointer
    /// (<c>LoadIndirect</c> of <c>LoadArgument{0,"this"}</c>), not the bare
    /// <c>LoadArgument{0,"this"}</c> a reference-type upcast (no IL) leaves. Only a
    /// value type produces this shape (a class <c>this</c> is already a reference).
    /// The <see cref="Box"/> is itself the evidence of the cast, so no interface
    /// metadata is needed: callers re-spell it as <c>base.M</c> when the
    /// non-virtual callee is declared on one of the struct's base classes
    /// (<see cref="System.ValueType"/> or <see cref="System.Object"/>, where
    /// base-suppression reaches the base member and <c>base</c> is the only valid
    /// spelling for a <c>protected</c> member), and as the erased
    /// <c>((T)this).M</c> cast otherwise — a virtual call, or a non-virtual call to
    /// an interface member (#3201, #3213). See <see cref="IsStructBaseClass"/>.
    /// <para>
    /// Gated on the enclosing method having an implicit <c>this</c>: a
    /// <c>static</c> method (or extension method) whose first parameter is spelled
    /// <c>@this</c> emits the metadata name <c>"this"</c> at index 0, matching the
    /// shape, but <c>base</c>/<c>this</c> are illegal there (CS0026). Such a boxed
    /// <c>@this</c> parameter falls through to the ordinary receiver path, which
    /// spells the parameter by name (#3213 review).
    /// </para>
    /// </summary>
    bool IsBoxedThisReceiver(IrExpression receiver)
        => _function.Signature.HasThis
            && receiver is Box { Operand: LoadIndirect { Address: LoadArgument { Index: 0, Name: "this" } } };

    /// <summary>
    /// True when <paramref name="type"/> is one of a struct's two base classes,
    /// <c>System.ValueType</c> or <c>System.Object</c>. A non-virtual boxed-
    /// <c>this</c> call to a member declared on a base class re-spells as
    /// <c>base.M()</c>: base-suppression (<c>call</c>, no virtual dispatch) reaches
    /// the base member, and <c>base</c> is the ONLY spelling that stays valid for a
    /// <c>protected</c> base member like <c>object::MemberwiseClone</c> — an
    /// <c>((object)this).MemberwiseClone()</c> cast is CS1540 (protected access
    /// through a base-typed qualifier). An interface callee is excluded (a sealed
    /// default interface member emits a non-virtual <c>call I::M</c>, yet has no
    /// <c>base</c>: CS0117); it takes the erased <c>((I)this).M()</c> cast, which is
    /// opcode-faithful <c>ldobj; box; call I::M</c> (#3213 review).
    /// <para>
    /// Residual: a non-virtual <c>call</c> to an <em>overridable</em> base member
    /// that <c>ValueType</c> overrides (e.g. a hand-emitted <c>call
    /// object::GetHashCode</c>) has no faithful spelling either way — <c>base.</c>
    /// rebinds to <c>ValueType</c>'s override and a cast re-dispatches
    /// (<c>callvirt</c>). C# cannot express it, and csc never emits it; the
    /// <c>base.</c> form here (matching a real <c>base.GetType()</c> call) is a
    /// best-effort, valid-C# render of that synthetic shape.
    /// </para>
    /// </summary>
    static bool IsStructBaseClass(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "ValueType" or "Object" };

    /// <summary>
    /// True when <paramref name="receiver"/> (or method-group target) is a boxed
    /// NON-<c>this</c> value — a local, parameter, field, or <c>ref</c>/<c>in</c>
    /// place — reaching a CONFIRMED interface member of
    /// <paramref name="declaringType"/>; <paramref name="placeText"/> receives the
    /// unboxed place spelled as a cast operand. A value type must box to invoke an
    /// interface member (`<c>ldobj; box; call[virt] I::M</c>` — or `<c>ldftn</c>`
    /// for a group), so the member is not on the value type and the erased
    /// <c>((I)x)</c> upcast must be re-inserted: bare <c>(x).M()</c> is CS1061 for a
    /// default interface member or explicit implementation, and a silent rebind to
    /// the struct's own method for a normally-implemented one. The boxed
    /// <c>this</c> case (arg0) is owned by <see cref="IsBoxedThisReceiver"/> (with
    /// its <c>base</c>/cast split) and is excluded here. A <c>static</c> method's
    /// <c>@this</c> parameter shares the arg0 metadata name but is a genuine
    /// non-<c>this</c> place; it is also excluded because the printer still spells
    /// arg0-<c>"this"</c> with the <c>this</c> keyword (CS0026 in a static body) —
    /// a pre-existing spelling limitation (#3260) — so it stays on the ordinary
    /// path, unchanged from before this arm existed, rather than gaining an invalid
    /// <c>((I)this)</c> cast.
    /// <para>
    /// The place is spelled deref-aware: a <c>ref</c>/<c>in</c> managed reference
    /// reads back as the bare identifier/member (<c>s</c>) via
    /// <see cref="DerefLoad"/>, while any other <see cref="LoadIndirect"/> — an
    /// unmanaged pointer especially — is spelled through <see cref="Operand"/> so
    /// its dereference is PARENTHESIZED: bare <c>*p</c> after a cast reparses as
    /// multiplication (<c>(I)*p</c> is <c>(I) * p</c>, CS0119), whereas
    /// <c>(I)(*p)</c> binds as a cast. Unwrapping the address instead would drop
    /// the <c>*</c> entirely and emit <c>((I)p).M()</c> for a <c>T*</c> (CS0030).
    /// </para>
    /// <para>
    /// Gated on a CONFIRMED interface declaring type
    /// (<see cref="IrFunction.InterfaceTypes"/>; an unresolved or non-interface
    /// callee is absent, so a boxed value reaching a base-class member stays on the
    /// ordinary path — a separate fidelity concern). Unlike
    /// <see cref="IsInterfaceCastThisReceiver"/> it does NOT apply the
    /// enclosing-instantiation exclusion: that carve-out is sound only for a real
    /// <c>this</c> receiver (already typed as the interface), whereas a boxed
    /// struct place always needs the cast even when the callee interface is the
    /// enclosing type. The non-<c>this</c> sibling of #3201/#3213 (#3214).
    /// </para>
    /// </summary>
    bool TryBoxedNonThisInterfaceReceiver(IrExpression receiver, TypeRef declaringType, out string placeText)
    {
        placeText = "";
        if (receiver is not Box box)
            return false;
        var place = box.Operand is LoadIndirect { Address: { } address } ? address : box.Operand;
        if (place is LoadArgument { Index: 0, Name: "this" })
            return false;
        if (!_function.InterfaceTypes.Contains(NamedDefinition(declaringType)))
            return false;
        placeText = box.Operand is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef } byRefDeref
            ? DerefLoad(byRefDeref)
            : Operand(box.Operand);
        return true;
    }

    /// <summary>Member-access receivers: value-type receivers arrive by address in IL; C# spells the place itself, not its address.</summary>
    string ReceiverText(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress a => $"{LocalName(a.Index)}",
        LoadArgumentAddress a => a.Name,
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        FixedBufferElementAddress f => Deref(f),
        // A value-type array element accessed by address (ldelema; the receiver
        // of a field/property/method on the element) spells the element place
        // itself — C# auto-takes the address. The bare `ref pairs[0]` spelling
        // would be CS1525 in this value position (`(ref pairs[0]).A`).
        LoadElementAddress e => $"{Operand(e.Array)}[{ArrayIndexText(e.Index)}]",
        // An unbox yields a managed pointer into the box; a member access must
        // reach that in-box place, not a copy. Unlike a ref/out/write place, a
        // receiver is a value position where the cast `((T)o)` compiles, so it is
        // a safe fallback: `UnboxReceiverText` emits the faithful
        // `Unsafe.Unbox<T>(o)` intrinsic (a `ref T`; a mutating call or a member
        // assignment then acts on the boxed payload, matching `unbox; call` /
        // `unbox; stfld`) for a spellable non-nullable value type, and falls back
        // to the cast for Nullable<T>, a resolver-known reference type (malformed
        // IL), or an open generic parameter — where the cast silently drops a
        // mutation but at least compiles (`Unsafe.Unbox<T>` would be CS0453). The
        // intrinsic is a primary expression, so a trailing `.Member` binds
        // without extra parentheses, as does the parenthesized cast.
        Unbox u => UnboxReceiverText(u),
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
    string MethodGroupText(MethodRef method, IrExpression target, bool isVirtual)
    {
        string name = CSharpNaming.SourceMethodName(method.Name);
        if (target is Constant { Value: null })
            return $"{TypeQualifierText(method.DeclaringType)}.{name}";
        if (target is LoadArgument { Index: 0, Name: "this" })
        {
            // A default-interface-member (or explicit interface impl) method group
            // over this: the callee is declared on an interface `this` was
            // implicitly upcast to (no IL), so the member is not a member of the
            // implementing class. Re-insert the erased ((I)this) cast — bare/this.
            // would be CS1061. Takes precedence over the base arm below (a virtual
            // ldvirtftn over this would otherwise mis-read as base).
            if (IsInterfaceCastThisReceiver(method.DeclaringType))
                return $"(({TypeText(method.DeclaringType)})this).{name}";
            // A non-virtual (ldftn) instance method group over this to a
            // base-declared method is C#'s base.M — the ldftn deliberately
            // captures the base slot. Bare M or this.M would rebind to the derived
            // override and recompile to ldvirtftn (virtual dispatch), changing
            // behavior; so it stays base even under the qualify-method knob,
            // mirroring CallText. HasThis excludes closed static extension groups,
            // which share the `ldarg.0; ldftn` shape but bind the receiver as their
            // first argument — base.Ext would be CS0117 on the base type; they
            // stay this.Ext (or bare) like any other extension group.
            if (method.HasThis && !isVirtual && IsCrossType(method.DeclaringType))
                return $"base.{name}";
            if (_options.QualifyMethodAccess)
            {
                // A generic method GROUP (this.Make<int>) is deliberately not
                // recorded: MethodGroupText renders only the bare name, dropping
                // the type arguments (a pre-existing emit gap; AddressOfMethodText
                // and CallText append them, this path does not). The emitted
                // this.Make does not round-trip — delegate return-type inference
                // cannot recover the type argument (CS0411) — so recording it as a
                // byte-preserving opt-in would be a false positive. Suppressing is
                // a safe under-record; fixing the emit is out of scope here.
                if (method.TypeArguments.IsDefaultOrEmpty)
                    RecordMethodQualificationIfTaste(name, method.Name, method.DeclaringType, 0, method.ParameterTypes);
                return $"this.{name}";
            }
            return name;
        }
        // A method group through a boxed this to a type the struct was cast to
        // (base class or interface). A non-virtual (ldftn) group to a base-class
        // member (ValueType or Object) is base.M — base-suppression reaches the
        // base member, so `base.M` lowers to `ldobj; box; ldftn T::M`, and base. is
        // the only valid spelling for a protected base member like
        // object::MemberwiseClone. A virtual (ldvirtftn) group, and a non-virtual
        // group to a sealed interface member, are the erased ((T)this).M upcast —
        // `((T)this).M` lowers to `ldobj; box; [dup;] ld[virt]ftn T::M`,
        // opcode-faithful for each. Bare (this).M / this.M would be CS1061 or
        // rebind to the derived override; a base.M to an interface would be CS0117.
        // Subsumes the struct interface-cast case (#3201) and the base-class case
        // (#3213).
        // HasThis gates the whole branch: a closed static extension method group
        // (`((object)this).Ext`) shares the `ldobj; box; ldftn` shape but binds the
        // boxed this as its first argument, so its DeclaringType is the static
        // extension host — not a cast target (`((E)this).Ext` is CS0716). Those
        // fall through to the ordinary receiver path, which spells the boxed
        // receiver ((object)this) the extension already carries.
        if (method.HasThis && IsBoxedThisReceiver(target))
        {
            if (!isVirtual && IsCrossType(method.DeclaringType) && IsStructBaseClass(method.DeclaringType))
                return $"base.{name}";
            return $"(({TypeText(method.DeclaringType)})this).{name}";
        }
        // #3214: a boxed NON-this value (local, parameter, field, ref/in place)
        // reaching a confirmed interface member — the non-this sibling of the
        // boxed-this interface cast above. `((I)x).M` re-emits `ldobj; box;
        // ld[virt]ftn I::M`; bare `(x).M` would be CS1061 for a DIM/explicit impl.
        if (TryBoxedNonThisInterfaceReceiver(target, method.DeclaringType, out var boxedGroupPlace))
            return $"(({TypeText(method.DeclaringType)}){boxedGroupPlace}).{name}";
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
                    return $"{extensionReceiver}->{CSharpNaming.SourceMethodName(call.Callee)}{typeArguments}({extensionArgs})";
                if (arguments[0].ResultType is { Kind: TypeRefKind.Pointer })
                    return $"{TypeQualifierText(call.Callee.DeclaringType)}.{CSharpNaming.SourceMethodName(call.Callee)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
                return $"{ReceiverText(arguments[0])}.{CSharpNaming.SourceMethodName(call.Callee)}{typeArguments}({extensionArgs})";
            }
            // A static abstract/virtual interface member invoked through a type
            // parameter compiles to `constrained. T; call IInterface<…>::Method`.
            // C#'s spelling is the constrained type itself — `T.Method(args)` — not
            // the declaring interface (`INumberBase<T>.Method(args)` cannot invoke a
            // static abstract member: CS0119/CS0314). The constrained type is the
            // receiver, and the spelling recompiles to the same constrained call.
            if (call.ConstrainedTo is { } staticReceiver)
                return $"{TypeQualifierText(staticReceiver)}.{CSharpNaming.SourceMethodName(call.Callee)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
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
            string sourceName = CSharpNaming.SourceMethodName(call.Callee);
            string staticName = $"{sourceName}{typeArguments}";
            string staticArgs = Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds);
            return IsEnclosingTypeAtOwnInstantiation(call.Callee.DeclaringType) && !IsStaticCallNameShadowed(sourceName)
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
        if (IsBoxedThisReceiver(receiver))
        {
            string boxedMethodName = CSharpNaming.SourceMethodName(call.Callee);
            // A non-virtual call through a boxed this to a base-class member
            // (System.ValueType or System.Object) is base.M(): base-suppression
            // (`call`, no dispatch) reaches the base member and `base.M()` lowers to
            // `ldobj; box; call T::M`. Bare (this).M() here re-dispatches virtually
            // to the struct's own override — self-recursion or a wrong target (e.g.
            // GetHashCode calling itself) (#3213). base. is also the ONLY valid
            // spelling for a protected base member like object::MemberwiseClone —
            // ((object)this).MemberwiseClone() is CS1540. Stays base. even under the
            // qualify knob. A non-virtual call to a sealed interface member takes
            // the cast arm below, not base.: base would be CS0117 for an interface —
            // `((I)this).M()` re-emits the same non-virtual `call I::M` (#3213
            // review).
            if (!call.IsVirtual && IsCrossType(call.Callee.DeclaringType) && IsStructBaseClass(call.Callee.DeclaringType))
                return $"base.{boxedMethodName}{typeArguments}({rest})";
            // A call through a boxed this to an interface the struct was cast to:
            // ((I)this).M(). The explicit box is the source's upcast; ((I)this).M()
            // re-emits `ldobj; box; call[virt] I::M` — callvirt for a virtual
            // callee, call for a non-virtual one (a sealed DIM). A bare this.M()
            // would instead emit `constrained. callvirt` (no box) — not
            // opcode-faithful. Subsumes the struct interface-cast case (#3201).
            return $"(({TypeText(call.Callee.DeclaringType)})this).{boxedMethodName}{typeArguments}({rest})";
        }
        // #3214: a boxed NON-this value (local, parameter, field, ref/in place)
        // reaching a confirmed interface member — the non-this sibling of the
        // boxed-this interface cast above. `((I)x).M()` re-emits `ldobj; box;
        // call[virt] I::M`; bare `(x).M()` would be CS1061 for a DIM/explicit impl,
        // or a silent rebind to the value type's own method otherwise.
        if (TryBoxedNonThisInterfaceReceiver(receiver, call.Callee.DeclaringType, out var boxedNonThisPlace))
            return $"(({TypeText(call.Callee.DeclaringType)}){boxedNonThisPlace}).{CSharpNaming.SourceMethodName(call.Callee)}{typeArguments}({rest})";
        if (receiver is LoadArgument { Index: 0, Name: "this" })
        {
            string thisMethodName = CSharpNaming.SourceMethodName(call.Callee);
            // A default-interface-member (or explicit interface impl) call over
            // this: the callee is declared on an interface `this` was implicitly
            // upcast to (no IL), so the member is not a member of the implementing
            // class. Re-insert the erased ((I)this) cast — bare/this. would be
            // CS1061. Takes precedence over the base arm below.
            if (IsInterfaceCastThisReceiver(call.Callee.DeclaringType))
                return $"(({TypeText(call.Callee.DeclaringType)})this).{thisMethodName}{typeArguments}({rest})";
            // Non-virtual this-receiver call to a base-declared method is
            // C#'s base.M() — the call opcode deliberately skips dispatch, so it
            // stays base. even under the qualify-method knob (this.M() would
            // re-enable virtual dispatch and change behavior).
            if (!call.IsVirtual && IsCrossType(call.Callee.DeclaringType))
                return $"base.{thisMethodName}{typeArguments}({rest})";
            if (_options.QualifyMethodAccess)
            {
                RecordMethodQualificationIfTaste(
                    thisMethodName, call.Callee.Name, call.Callee.DeclaringType, call.Callee.TypeArguments.Length, call.Callee.ParameterTypes, call.Callee.DefinitionParameterTypes);
                return $"this.{thisMethodName}{typeArguments}({rest})";
            }
            return $"{thisMethodName}{typeArguments}({rest})";
        }
        if (PointerMethodReceiver(receiver) is { } pointerReceiver)
            return $"{pointerReceiver}->{CSharpNaming.SourceMethodName(call.Callee)}{typeArguments}({rest})";
        return $"{ReceiverText(receiver)}.{CSharpNaming.SourceMethodName(call.Callee)}{typeArguments}({rest})";
    }

    // Taste class 3 (no IL anchor): once a re-composed fluent chain is long, the
    // runtime style oracle (a wide .editorconfig) breaks each chained call onto
    // its own line under a continuation indent; a chain that still fits stays
    // inline. The threshold is the dotnet/runtime max line width; it is a pure
    // formatting tiebreaker, so it never changes which tokens are emitted.
    const int FluentChainWrapWidth = 120;
    const int FluentChainMinSegments = 2;

    /// <summary>
    /// True when <see cref="CallText"/> renders <paramref name="call"/> through
    /// the plain <c>{ReceiverText(receiver)}.Member(args)</c> tail — the only
    /// form whose text is guaranteed to be prefixed by its receiver's text, which
    /// the chain-splitting substring arithmetic relies on. Excludes operators,
    /// extension sugar, constrained/static calls, <c>this</c>/base and pointer
    /// receivers, constructor chains, and delegate <c>Invoke</c>.
    /// </summary>
    bool IsPlainInstanceChainSegment(Call call)
    {
        if (!call.Callee.HasThis || call.Arguments.Count < 1)
            return false;
        if (call.Callee.Name is ".ctor")
            return false;
        var receiver = call.Arguments[0];
        if (call.Callee.Name is "Invoke" && receiver is Lambda)
            return false;
        if (receiver is LoadArgument { Index: 0, Name: "this" })
            return false;
        return PointerMethodReceiver(receiver) is null;
    }

    /// <summary>
    /// Renders an instance-call chain rooted at <paramref name="root"/> as one
    /// call per line — the head receiver on the first line, each
    /// <c>.Member(args)</c> segment indented one continuation level beneath it —
    /// when the chain has at least <see cref="FluentChainMinSegments"/> chained
    /// segments and its single-line form would exceed
    /// <see cref="FluentChainWrapWidth"/>. Returns null (render inline) otherwise.
    /// Each line's text is spliced out of the single-line <see cref="CallText"/>
    /// by length arithmetic, so the broken form is token-identical to the inline
    /// form: only whitespace differs and the IL is unchanged.
    /// </summary>
    string? FluentChainLines(Call root, string prefix, string suffix, int indent)
    {
        var segments = new List<Call>();
        IrExpression current = root;
        while (current is Call call && IsPlainInstanceChainSegment(call))
        {
            segments.Add(call);
            current = call.Arguments[0];
        }
        if (segments.Count < FluentChainMinSegments)
            return null;
        segments.Reverse();

        string rootText = CallText(root);
        if (indent * 4 + prefix.Length + rootText.Length + suffix.Length <= FluentChainWrapWidth)
            return null;

        string pad = new(' ', indent * 4);
        string continuation = pad + "    ";
        var sb = new System.Text.StringBuilder();
        string headText = ReceiverText(segments[0].Arguments[0]);
        sb.Append(pad).Append(prefix).Append(headText);
        string previous = headText;
        foreach (var segment in segments)
        {
            string full = CallText(segment);
            // full always starts with `previous` (an outer plain instance call's
            // text is `{ReceiverText(receiver)}.Member(args)`, and the receiver's
            // text is exactly `previous`); the tail is this segment's `.Member(args)`.
            if (!full.StartsWith(previous, System.StringComparison.Ordinal))
                return null;
            sb.Append('\n').Append(continuation).Append(full, previous.Length, full.Length - previous.Length);
            previous = full;
        }
        sb.Append(suffix);
        return sb.ToString();
    }

    /// <summary>
    /// Renders a brace-bodied expression — an object/collection initializer, a
    /// record <c>with</c> expression, or an anonymous object — as a wrapped Allman
    /// block: <paramref name="head"/> on the first line, <c>{</c> and <c>}</c> on
    /// their own lines at the statement indent, and one entry per line one
    /// continuation level deeper. Returns null (render inline) when the body has
    /// fewer than <see cref="FluentChainMinSegments"/> entries or its single-line
    /// form fits within <see cref="FluentChainWrapWidth"/>. The wrapped form reuses
    /// the same <paramref name="head"/> and <paramref name="entryTexts"/> the inline
    /// renderer emits and carries no trailing comma, so it is token-identical to the
    /// inline form: only whitespace differs and the IL is unchanged. A defensive
    /// re-match against the inline <paramref name="flat"/> text keeps the statement
    /// inline if the reconstruction diverges rather than reshaping a token.
    /// </summary>
    string? BraceBodyLines(string head, IReadOnlyList<string> entryTexts, string flat, string prefix, string suffix, int indent)
    {
        if (entryTexts.Count < FluentChainMinSegments)
            return null;

        if (indent * 4 + prefix.Length + flat.Length + suffix.Length <= FluentChainWrapWidth)
            return null;

        // The wrapped form must be a pure whitespace variant of the inline text.
        // Reconstruct the inline body from the same head and entry texts and bail if
        // it does not match exactly, so a renderer quirk keeps the statement inline
        // rather than reshaping a token.
        if ($"{head} {{ {string.Join(", ", entryTexts)} }}" != flat)
            return null;

        string pad = new(' ', indent * 4);
        string continuation = pad + "    ";
        var sb = new System.Text.StringBuilder();
        sb.Append(pad).Append(prefix).Append(head);
        sb.Append('\n').Append(pad).Append('{');
        for (int i = 0; i < entryTexts.Count; i++)
        {
            sb.Append('\n').Append(continuation).Append(entryTexts[i]);
            if (i < entryTexts.Count - 1)
                sb.Append(',');
        }
        sb.Append('\n').Append(pad).Append('}');
        sb.Append(suffix);
        return sb.ToString();
    }

    /// <summary>
    /// Wraps an object/collection initializer (<c>new T(...) { ... }</c>) through
    /// <see cref="BraceBodyLines"/>, reusing the same head and entry texts the inline
    /// <see cref="ObjectInitializerText"/> renders.
    /// </summary>
    string? ObjectInitializerLines(ObjectInitializerExpression initializer, string prefix, string suffix, int indent)
    {
        var creation = initializer.Creation;
        string arguments = creation.Arguments.Count == 0
            ? string.Empty
            : $"({Arguments(creation.Arguments, creation.Constructor.ParameterTypes, creation.Constructor.ParameterRefKinds)})";
        string head = $"new {TypeText(creation.Constructor.DeclaringType)}{arguments}";
        var entryTexts = initializer.Entries
            .Select(entry => InitializerEntryText(initializer.IsCollection, entry))
            .ToList();
        return BraceBodyLines(head, entryTexts, ObjectInitializerText(initializer), prefix, suffix, indent);
    }

    /// <summary>
    /// Wraps a record <c>with</c> expression (<c>receiver with { ... }</c>) through
    /// <see cref="BraceBodyLines"/>, reusing the same head and entry texts the inline
    /// <see cref="WithExpressionText"/> renders.
    /// </summary>
    string? WithExpressionLines(WithExpression node, string prefix, string suffix, int indent)
    {
        string head = $"{Operand(node.Receiver)} with";
        var entryTexts = node.Entries.Select(WithExpressionEntryText).ToList();
        return BraceBodyLines(head, entryTexts, WithExpressionText(node), prefix, suffix, indent);
    }

    /// <summary>
    /// Wraps an anonymous object (<c>new { ... }</c>) through
    /// <see cref="BraceBodyLines"/>, reusing the same projection parts the inline
    /// <see cref="AnonymousObjectText"/> renders.
    /// </summary>
    string? AnonymousObjectLines(AnonymousObject node, string prefix, string suffix, int indent)
    {
        if (node.Values.Count == 0)
            return null;
        return BraceBodyLines("new", AnonymousObjectParts(node), AnonymousObjectText(node), prefix, suffix, indent);
    }

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
        // `out`/`ref` require a genuine assignable lvalue. ArgumentLvalue spells
        // every assignable form (including an unbox, as `Unsafe.Unbox<T>(o)`);
        // anything else is a bare value with no ref-place spelling, so leave it
        // to the default value spelling.
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
        LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or FixedBufferElementAddress or LoadElementAddress => Deref(argument),
        Unbox u => $"({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocal or LoadArgument or LoadStackSlot or LoadIndirect or Call or CallIndirect => Expression(argument),
        _ => null,
    };

    /// <summary>
    /// The subset of <see cref="ArgumentPlace"/> that is a genuine assignable
    /// lvalue — what <c>out</c>/<c>ref</c> demand. An <see cref="Unbox"/> is the
    /// managed pointer into a box; its assignable-place spelling is the
    /// <c>Unsafe.Unbox&lt;T&gt;(o)</c> intrinsic (see <see cref="UnsafeUnboxText"/>),
    /// valid as a <c>ref</c>/<c>out</c> target and as a ref-return. The bare cast
    /// form <c>(T)x</c> is an <c>unbox.any</c> value, not a place (<c>out (T)x</c>
    /// is CS0206, <c>ref (T)x</c> is CS0445), so it stays only in
    /// <see cref="ArgumentPlace"/> for the value-accepting <c>in</c> convention.
    /// </summary>
    string? ArgumentLvalue(IrExpression argument) => argument switch
    {
        LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or FixedBufferElementAddress or LoadElementAddress => Deref(argument),
        // `unbox T` is a managed pointer into the box; the `Unsafe.Unbox<T>(o)`
        // intrinsic is its assignable-place spelling — valid as a ref/out target
        // and as a ref-return (a bare `(T)x` cast is an unbox.any value, so
        // `ref (T)x` is CS0445 and `out (T)x` is CS0206).
        Unbox u => UnsafeUnboxText(u),
        // A ref-typed value already names a place: a ref local/parameter, a
        // ref-returning call, or a ref slot the importer spilled the managed
        // pointer into (a ref argument evaluated before a later side-effecting
        // argument). Each renders as a bare name the ref/out keyword prefixes.
        LoadLocal or LoadArgument or LoadStackSlot or LoadIndirect or Call or CallIndirect => Expression(argument),
        _ => null,
    };
}
