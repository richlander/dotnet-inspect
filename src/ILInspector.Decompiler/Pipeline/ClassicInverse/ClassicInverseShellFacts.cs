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
        ImmutableHashSet<int> awaiterLocals)
    {
        Machine = machine;
        StateLocal = stateLocal;
        AwaiterLocals = awaiterLocals;
    }

    /// <summary>The state-machine definition type that owns the execution body.</summary>
    internal TypeRef Machine { get; }

    /// <summary>The local slot the shell copies <c>&lt;&gt;1__state</c> into, or -1.</summary>
    internal int StateLocal { get; }

    /// <summary>Local slots proven to hold a compiler awaiter.</summary>
    internal ImmutableHashSet<int> AwaiterLocals { get; }

    internal static ClassicInverseShellFacts Derive(IrFunction execution)
    {
        TypeRef machine = ClassicInverseNodeFacts.Definition(execution.DeclaringType);
        int stateLocal = -1;
        var awaiters = ImmutableHashSet.CreateBuilder<int>();

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
                continue;
            }

            if (store.Value is LoadField { Field.Name: var awaiterField } cached
                && awaiterField.StartsWith("<>u__", StringComparison.Ordinal)
                && cached.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(cached.Field, machine))
            {
                awaiters.Add(store.Index);
            }
        }

        return new ClassicInverseShellFacts(machine, stateLocal, awaiters.ToImmutable());
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

    internal static bool IsAsyncMethodBuilder(TypeRef type)
    {
        TypeRef definition = Definition(type);
        return definition is
        {
            Namespace: "System.Runtime.CompilerServices",
            Name: "AsyncTaskMethodBuilder"
                or "AsyncTaskMethodBuilder`1"
                or "AsyncValueTaskMethodBuilder"
                or "AsyncValueTaskMethodBuilder`1",
        };
    }

    internal static bool IsBuilderAccess(IrExpression expression, TypeRef machine)
        => expression is LoadFieldAddress
        {
            Field: { Name: "<>t__builder" } field,
            Instance: LoadArgument { Index: 0 },
        }
            && IsMachineField(field, machine);

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
    internal static string? EffectSignature(IrNode node, TypeRef machine)
        => node switch
        {
            AwaitExpression => "await",
            Call call => $"call:{call.Callee.DeclaringType.ToDisplayString()}"
                + $".{call.Callee.Name}/{call.Callee.ParameterTypes.Length}"
                + $":{(call.IsVirtual ? "virt" : "direct")}",
            CallIndirect => "calli",
            LocalFunctionInvocation invocation =>
                $"localfn:{invocation.Name}",
            NewObject creation when !IsEffectFreeTuple(creation) =>
                $"newobj:{creation.Constructor.DeclaringType.ToDisplayString()}"
                + $"/{creation.Constructor.ParameterTypes.Length}",
            LoadProperty property => $"call:{property.PropertyName}",
            StoreProperty property => $"store:{property.PropertyName}",
            ArrayLength => "throw:array-length",
            LoadElement => "throw:element-access",
            LoadElementAddress => "throw:element-address",
            LoadIndirect => "throw:indirect-load",
            CastClass => "throw:cast",
            Unbox => "throw:unbox",
            UnboxAny => "throw:unbox-any",
            NewArray => "new:array",
            Convert { IsChecked: true } => "throw:checked-convert",
            Binary { IsChecked: true } binary =>
                $"throw:checked-{binary.Kind}",
            Binary
            {
                Kind: BinaryKind.Divide or BinaryKind.Remainder,
            } binary => $"throw:{binary.Kind}",
            LoadField load when !IsMachineRead(load, machine) =>
                $"read:{load.Field.DeclaringType.ToDisplayString()}.{load.Field.Name}",
            LoadFieldAddress load when !IsMachineRead(load, machine) =>
                $"readref:{load.Field.DeclaringType.ToDisplayString()}.{load.Field.Name}",
            StoreField store when !IsMachineField(store.Field, machine) =>
                $"store:{store.Field.DeclaringType.ToDisplayString()}.{store.Field.Name}",
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
