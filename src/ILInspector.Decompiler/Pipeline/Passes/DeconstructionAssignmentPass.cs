using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the narrow tuple deconstruction lowering:
/// <code>
/// S = tuple;
/// T1 a = S.Item1;
/// T2 b = S.Item2;
/// </code>
/// into <c>(T1 a, T2 b) = tuple;</c> when the targets are fresh locals declared
/// here, <c>(a, b) = tuple;</c> when they assign into pre-existing locals, or a
/// mix such as <c>(T1 a, b) = tuple;</c> — each target is classified independently
/// as a declaration or an assignment. Scoped to direct <c>System.ValueTuple</c>
/// fields, arities 2-7, with every target a local (parameter and field targets,
/// which the importer spells as <c>StoreArgument</c>/<c>StoreField</c> rather than
/// <c>StoreLocal</c>, fall outside the match). Nested/rest tuples and user-defined
/// <c>Deconstruct</c> calls are later slices.
/// </summary>
public sealed class DeconstructionAssignmentPass : IIrPass
{
    public string Name => "deconstruction-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (TryRaiseValueTuple(function, block, i, context)
                    || TryRaiseDeconstructMethod(function, children[i], context))
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The receiver-spill + <c>ItemN</c> store form: a <c>ValueTuple</c> value
    /// stored to a stack slot, then read field-by-field into the targets.
    /// </summary>
    static bool TryRaiseValueTuple(IrFunction function, Block block, int i, PassContext context)
    {
        var children = block.Children;
        if (children[i] is not StoreStackSlot seed
            || seed.Value.ResultType is not { } tupleType
            || !MemberIdentity.IsSupportedValueTupleType(tupleType, out var arity)
            || i + arity >= children.Count)
        {
            return false;
        }

        var stores = new List<StoreLocal>(arity);
        for (int j = 0; j < arity; j++)
        {
            if (children[i + 1 + j] is not StoreLocal
                {
                    Value: LoadField
                    {
                        Field.Name: var fieldName,
                        Instance: LoadStackSlot load,
                    },
                } store
                || load.Slot != seed.Slot
                || fieldName != $"Item{j + 1}")
            {
                stores.Clear();
                break;
            }
            stores.Add(store);
        }

        if (stores.Count != arity
            || !ReferencedOnlyWithin(function, seed.Slot, stores))
        {
            return false;
        }

        var targets = stores.Select(store => (store.Index, store.Type, (IrNode)store));
        if (ClassifyTargets(function, targets) is not { } resolved)
            return false;

        var source = (IrExpression)seed.DetachChildren()[0];
        var deconstruction = new DeconstructionAssignment(resolved.indices, resolved.types, source, resolved.isDeclared);
        context.Stepper.StepOver("raise ValueTuple field stores to deconstruction", seed);
        seed.ReplaceWith(deconstruction);
        foreach (var store in stores)
            store.Detach();
        return true;
    }

    /// <summary>
    /// The <c>Deconstruct</c>-method form: a single void <c>r.Deconstruct(out a,
    /// out b, ...)</c> call statement, the lowering of <c>(a, b) = r;</c> when
    /// <c>r</c>'s type supplies a <c>Deconstruct</c> method. Scoped to a
    /// side-effect-free local/parameter receiver (the only shape in the corpus —
    /// foreach <c>Current</c> and locals); the out-temp + copy form and other
    /// receivers are later slices.
    /// </summary>
    static bool TryRaiseDeconstructMethod(IrFunction function, IrNode statement, PassContext context)
    {
        var call = statement as Call ?? (statement as ExpressionStatement)?.Expression as Call;
        if (call is null
            || call.Callee is not { Name: "Deconstruct", HasThis: true, ReturnType: { Namespace: "System", Name: "Void" } }
            || call.Arguments.Count is < 3 or > 8)
        {
            return false;
        }

        int arity = call.Arguments.Count - 1;
        var receiver = call.Arguments[0];
        if (ReceiverValue(receiver) is not { } source)
            return false;

        var outArgs = call.Arguments.Skip(1).ToList();
        if (outArgs.Any(arg => arg is not LoadLocalAddress))
            return false;

        var targets = outArgs.Cast<LoadLocalAddress>()
            .Select(arg => (arg.Index, arg.Type, (IrNode)arg));
        // A target that aliases the receiver local would re-read the value it is
        // overwriting; `(a, b) = a` keeps the de-sugared call instead.
        if (receiver is LoadLocalAddress receiverLocal
            && outArgs.Cast<LoadLocalAddress>().Any(arg => arg.Index == receiverLocal.Index))
        {
            return false;
        }

        if (ClassifyTargets(function, targets) is not { } resolved)
            return false;

        var deconstruction = new DeconstructionAssignment(resolved.indices, resolved.types, source, resolved.isDeclared);
        context.Stepper.StepOver("raise Deconstruct-method call to deconstruction", statement);
        statement.ReplaceWith(deconstruction);
        return true;
    }

    /// <summary>The value form of a side-effect-free <c>Deconstruct</c> receiver, or null when the receiver is unsupported.</summary>
    static IrExpression? ReceiverValue(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress address => new LoadLocal(address.Index, address.Type),
        LoadArgumentAddress address => new LoadArgument(address.Index, address.Name, address.Type),
        LoadLocal local => new LoadLocal(local.Index, local.Type),
        LoadArgument argument => new LoadArgument(argument.Index, argument.Name, argument.Type),
        _ => null,
    };

    /// <summary>
    /// Resolves the deconstruction targets, classifying each independently as a
    /// fresh local introduced here (a declaration, its first reference is this
    /// store) or a pre-existing local (an assignment). A run may mix the two —
    /// <c>(int x, y) = …</c> — so the per-target flags are returned parallel to the
    /// indices. Distinct targets are required since <c>(a, a) = …</c> is not a
    /// valid deconstruction; the all-fresh form is inherently distinct. Returns
    /// null to decline.
    /// </summary>
    static (ImmutableArray<int> indices, ImmutableArray<TypeRef> types, ImmutableArray<bool> isDeclared)? ClassifyTargets(
        IrFunction function,
        IEnumerable<(int index, TypeRef type, IrNode reference)> targets)
    {
        var resolved = targets.ToList();
        if (resolved.Select(target => target.index).Distinct().Count() != resolved.Count)
            return null;

        var isDeclared = resolved.Select(target => IsFirstReference(function, target.reference, target.index)).ToImmutableArray();
        return ([.. resolved.Select(target => target.index)], [.. resolved.Select(target => target.type)], isDeclared);
    }

    static bool ReferencedOnlyWithin(IrFunction function, int slot, IReadOnlyList<StoreLocal> stores)
    {
        foreach (var load in function.Descendants.OfType<LoadStackSlot>().Where(load => load.Slot == slot))
            if (!stores.Any(store => IsInside(load, store)))
                return false;
        return true;
    }

    static bool IsFirstReference(IrFunction function, IrNode target, int index)
    {
        foreach (var node in function.Descendants)
        {
            if (ReferenceEquals(node, target))
                return true;
            if (node is LoadLocal load && load.Index == index
                || node is StoreLocal store && store.Index == index
                || node is LoadLocalAddress address && address.Index == index)
            {
                return false;
            }
        }
        return false;
    }

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }
}
