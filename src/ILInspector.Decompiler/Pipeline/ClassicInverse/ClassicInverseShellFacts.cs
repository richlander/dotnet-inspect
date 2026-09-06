using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The exact lowering-shell facts the classic inverse proves once per request
/// and then reuses. Every fact is positive: an absent fact is never assumed.
/// </summary>
internal sealed class ClassicInverseShellFacts
{
    ClassicInverseShellFacts(
        TypeRef machine,
        int stateLocal,
        ImmutableHashSet<int> awaiterLocals,
        ImmutableDictionary<int, TypeRef> awaiterTypes,
        ClassicInverseLoweringProof protocol)
    {
        Machine = machine;
        StateLocal = stateLocal;
        AwaiterLocals = awaiterLocals;
        AwaiterTypes = awaiterTypes;
        Protocol = protocol;
    }

    /// <summary>The state-machine definition type that owns the execution body.</summary>
    internal TypeRef Machine { get; }

    /// <summary>The local slot the shell copies <c>&lt;&gt;1__state</c> into, or -1.</summary>
    internal int StateLocal { get; }

    /// <summary>Local slots proven to hold a compiler awaiter.</summary>
    internal ImmutableHashSet<int> AwaiterLocals { get; }

    /// <summary>
    /// The exact awaiter type each proven awaiter slot carries. A slot appears
    /// only when every binding the body contains — the local's own declared
    /// type, the <c>GetAwaiter</c> return type that produced it, and the
    /// <c>&lt;&gt;u__N</c> cache field it is restored from — names one and the
    /// same type. The awaiter family is not enumerated: a compiler-produced
    /// custom awaiter is admitted on the same terms as a core-library one, and
    /// a slot whose bindings disagree carries no awaiter type at all.
    /// </summary>
    internal ImmutableDictionary<int, TypeRef> AwaiterTypes { get; }

    /// <summary>
    /// The completion-callback, completion-catch, and resume-state protocol
    /// proven over both the raw import and the planning view. When it carries a
    /// <see cref="ClassicInverseLoweringProof.Failure"/>, no node has a protocol
    /// role and the accountant declines.
    /// </summary>
    internal ClassicInverseLoweringProof Protocol { get; }

    /// <summary>
    /// Derives the shell facts from the planning view and the unmodified import
    /// snapshot it was derived from. Both bodies are required: the import owns
    /// the exception-region facts the planning view consumes into structure, and
    /// the two must describe the same protocol before any node is scaffolding.
    /// </summary>
    internal static ClassicInverseShellFacts Derive(
        IrFunction execution,
        IrFunction rawExecution,
        ClassicInverseBudget budget)
    {
        TypeRef machine = ClassicInverseNodeFacts.Definition(execution.DeclaringType);
        int stateLocal = -1;
        var awaiters = ImmutableHashSet.CreateBuilder<int>();
        var awaiterTypes = new Dictionary<int, TypeRef>();
        var conflicted = new HashSet<int>();

        void ObserveAwaiterType(int slot, TypeRef? type)
        {
            if (type is null)
            {
                conflicted.Add(slot);
                awaiterTypes.Remove(slot);
                return;
            }
            if (conflicted.Contains(slot))
                return;
            if (!awaiterTypes.TryGetValue(slot, out TypeRef? existing))
            {
                awaiterTypes[slot] = type;
                return;
            }
            if (!existing.Equals(type))
            {
                conflicted.Add(slot);
                awaiterTypes.Remove(slot);
            }
        }

        foreach (IrNode node in execution.Body.Descendants)
        {
            if (node is not StoreLocal store)
                continue;

            if (store.Value is LoadField { Field.Name: "<>1__state" } state
                && state.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(state.Field, machine))
            {
                stateLocal = stateLocal < 0 || stateLocal == store.Index
                    ? store.Index
                    : -1;
                continue;
            }

            if (store.Value is Call { Callee.Name: "GetAwaiter" } getAwaiter
                && getAwaiter.Arguments.Count == 1)
            {
                awaiters.Add(store.Index);
                ObserveAwaiterType(store.Index, store.Type);
                ObserveAwaiterType(store.Index, getAwaiter.Callee.ReturnType);
                continue;
            }

            if (store.Value is LoadField { Field.Name: var awaiterField } cached
                && awaiterField.StartsWith("<>u__", StringComparison.Ordinal)
                && cached.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(cached.Field, machine))
            {
                awaiters.Add(store.Index);
                ObserveAwaiterType(store.Index, store.Type);
                ObserveAwaiterType(store.Index, cached.Field.Type);
            }
        }

        return new ClassicInverseShellFacts(
            machine,
            stateLocal,
            awaiters.ToImmutable(),
            awaiterTypes.ToImmutableDictionary(),
            ClassicInverseLoweringProof.Derive(
                execution,
                rawExecution,
                machine,
                stateLocal,
                awaiters.ToImmutable(),
                budget));
    }
}

/// <summary>Shared, purely structural predicates over imported classic-async IR.</summary>
internal static class ClassicInverseNodeFacts
{
    internal static TypeRef Definition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } element
            ? element
            : type;

    internal static bool IsMachineField(FieldRef field, TypeRef machine)
    {
        TypeRef declaring = Definition(field.DeclaringType);
        machine = Definition(machine);
        return !declaring.DefinitionHandle.IsNil
            && declaring.DefinitionHandle == machine.DefinitionHandle
            && declaring.DefinitionModuleVersionId is { } declaringMvid
            && machine.DefinitionModuleVersionId is { } machineMvid
            && declaringMvid == machineMvid;
    }

    /// <summary>A read of state-machine storage through <c>this</c>.</summary>
    internal static bool IsMachineRead(IrNode node, TypeRef machine)
        => node switch
        {
            LoadField { Instance: LoadArgument { Index: 0 } } load =>
                IsMachineField(load.Field, machine),
            LoadFieldAddress { Instance: LoadArgument { Index: 0 } } load =>
                IsMachineField(load.Field, machine),
            _ => false,
        };

    /// <summary>
    /// One of the four core-library async method builders, by exact core-library
    /// identity rather than namespace and name. A same-named type in another
    /// assembly is a lookalike, not the builder whose callbacks the lowering
    /// protocol models.
    /// </summary>
    internal static bool IsAsyncMethodBuilder(TypeRef type)
    {
        TypeRef definition = Definition(type);
        return definition.Name is
                "AsyncTaskMethodBuilder"
                or "AsyncTaskMethodBuilder`1"
                or "AsyncValueTaskMethodBuilder"
                or "AsyncValueTaskMethodBuilder`1"
            && MemberIdentity.IsCoreLibraryType(
                definition,
                "System.Runtime.CompilerServices",
                definition.Name);
    }

    internal static bool IsBuilderAccess(IrExpression expression, TypeRef machine)
        => BuilderField(expression, machine) is not null;

    /// <summary>
    /// The machine's own <c>&lt;&gt;t__builder</c> field behind a builder
    /// callback receiver, or <c>null</c>. The field's declared type is the only
    /// authority for which builder a callback may be declared on.
    /// </summary>
    internal static FieldRef? BuilderField(IrExpression expression, TypeRef machine)
        => expression is LoadFieldAddress
        {
            Field: { Name: "<>t__builder" } field,
            Instance: LoadArgument { Index: 0 },
        }
            && IsMachineField(field, machine)
            ? field
            : null;

    /// <summary>The kickoff's builder access: <c>ldflda &lt;&gt;t__builder</c> on the state-machine local.</summary>
    internal static bool IsBuilderAccessOnLocal(
        IrExpression expression,
        TypeRef machine,
        int stateMachineLocal)
        => expression is LoadFieldAddress
        {
            Field: { Name: "<>t__builder" } field,
            Instance: LoadLocalAddress local,
        }
            && local.Index == stateMachineLocal
            && IsMachineField(field, machine);

    internal static bool IsHoistedLocalField(string name)
        => name.StartsWith("<", StringComparison.Ordinal)
            && !name.StartsWith("<>", StringComparison.Ordinal)
            && name.Contains(">5__", StringComparison.Ordinal);

    internal static bool IsCompilerHousekeepingField(string name)
        => name is "<>1__state" or "<>t__builder" or "<>4__this"
            || name.StartsWith("<>u__", StringComparison.Ordinal);

    /// <summary>
    /// The observable-effect signature of one node, or <c>null</c> when the node
    /// contributes no observable behavior of its own. Classification is positive:
    /// an unrecognized node form that could carry an effect is reported through
    /// <see cref="IsUnknownEffectForm"/> instead of being silently inert.
    /// </summary>
    internal static string? EffectSignature(
        IrNode node,
        TypeRef machine,
        ClassicInverseTypeBinding? binding = null,
        ClassicInverseBudget? budget = null)
    {
        string Type(TypeRef type) => ClassicInverseTypedIdentity.Type(
            binding is null ? type : binding.Type(type, budget!));
        string Method(MethodRef method) => ClassicInverseTypedIdentity.Method(
            binding is null ? method : binding.Method(method, budget!));
        string Field(FieldRef field) => ClassicInverseTypedIdentity.Field(
            binding is null ? field : binding.Field(field, budget!));
        return node switch
        {
            AwaitExpression => "await",
            TypeOf typeOf => $"typeof:{Type(typeOf.Type)}",
            DefaultValue value => $"default:{Type(value.Type)}",
            Call call => $"call:{Method(call.Callee)}"
                + $":{(call.IsVirtual ? "virt" : "direct")}"
                + (call.ConstrainedTo is { } constrained
                    ? $":constrained({Type(constrained)})"
                    : ""),
            CallIndirect indirect =>
                $"calli:{Type(indirect.ReturnType)}"
                + $"({string.Join(
                    ",",
                    indirect.ParameterTypes.Select(
                        Type))})"
                + $":{indirect.CallingConvention}"
                + $":{string.Join(",", indirect.ParameterRefKinds)}"
                + $":{(indirect.IsInstance ? "instance" : "static")}",
            LocalFunctionInvocation invocation =>
                $"localfn:{invocation.Name}",
            NewObject creation when !IsEffectFreeTuple(creation) =>
                $"newobj:{Method(creation.Constructor)}",
            LoadProperty property =>
                $"call:{Method(property.Accessor)}"
                + $":{(property.IsVirtual ? "virt" : "direct")}",
            StoreProperty property =>
                $"store:{Method(property.Accessor)}"
                + $":{(property.IsVirtual ? "virt" : "direct")}",
            ArrayLength => "throw:array-length",
            LoadElement => "throw:element-access",
            LoadElementAddress => "throw:element-address",
            LoadIndirect => "throw:indirect-load",
            CastClass => "throw:cast",
            Unbox => "throw:unbox",
            UnboxAny => "throw:unbox-any",
            NewArray => "new:array",
            StackAllocate => "alloc:stack",
            Convert { IsChecked: true } => "throw:checked-convert",
            Binary { IsChecked: true } binary =>
                $"throw:checked-{binary.Kind}",
            Binary
            {
                Kind: BinaryKind.Divide or BinaryKind.Remainder,
            } binary => $"throw:{binary.Kind}",
            LoadField load when !IsMachineRead(load, machine) =>
                $"read:{Field(load.Field)}",
            LoadFieldAddress load when !IsMachineRead(load, machine) =>
                $"readref:{Field(load.Field)}",
            StoreField store when !IsMachineField(store.Field, machine) =>
                $"store:{Field(store.Field)}",
            StoreElement => "store:element",
            StoreIndirect => "store:indirect",
            StoreArgument => "store:argument",
            CopyBlock => "store:block",
            InitObject init when !IsMachineInitTarget(init.Address, machine) =>
                "store:init",
            EventSubscription => "store:event",
            NullCoalescingAssignment
                or NullCoalescingFieldAssignment
                or NullCoalescingFieldAssignmentExpression
                or NullCoalescingPropertyAssignment => "store:coalesce",
            ChainedAssignment => "store:chained",
            DeconstructionAssignment => "store:deconstruct",
            IncrementDecrement => "store:incdec",
            Throw => "throw",
            DynamicGetMember member => $"dynamic:{member.PropertyName}",
            UnsupportedNode unsupported => $"unsupported:{unsupported.Opcode}",
            _ => null,
        };
    }

    internal static bool IsEffectFreeTuple(NewObject creation)
        => creation.Constructor.ConstructorEffectFree
            || IsValueTupleConstruction(creation);

    internal static bool IsValueTupleConstruction(NewObject creation)
    {
        TypeRef type = Definition(creation.Constructor.DeclaringType);
        return MemberIdentity.IsCoreLibraryType(type, "System", "ValueTuple")
            || Enumerable.Range(1, 8).Any(arity =>
                MemberIdentity.IsCoreLibraryType(
                    type,
                    "System",
                    $"ValueTuple`{arity}"));
    }

    static bool IsMachineInitTarget(IrExpression address, TypeRef machine)
        => address is LoadFieldAddress { Instance: LoadArgument { Index: 0 } } field
            && IsMachineField(field.Field, machine);

    /// <summary>
    /// Node forms the effect inventory cannot classify. The accountant declines
    /// rather than assuming inertness, so a new IR form never silently escapes
    /// the semantic ledger.
    /// </summary>
    internal static bool IsUnknownEffectForm(IrNode node)
        => node is Lambda
            or LocalFunctionStatement
            or Switch
            or SwitchExpression
            or SwitchBranch
            or Lock
            or UsingStatement
            or DeconstructionTarget;
}
