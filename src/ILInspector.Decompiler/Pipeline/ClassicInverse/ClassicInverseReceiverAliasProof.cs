namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Closed correspondence for the entry-block copy of a captured reference
/// receiver. Both coordinate spaces must carry the same unique initialization;
/// every read remains a read of the exact kickoff-transferred machine field.
/// </summary>
internal sealed class ClassicInverseReceiverAliasProof
{
    readonly Dictionary<LoadLocal, FieldRef> _reads = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<StoreLocal> _stores = new(ReferenceEqualityComparer.Instance);

    internal bool IsStore(StoreLocal store) => _stores.Contains(store);

    internal bool IsCapture(LoadField field)
        => field.Parent is StoreLocal store && IsStore(store) && ReferenceEquals(store.Value, field);

    internal bool TryField(LoadLocal read, out FieldRef field)
        => _reads.TryGetValue(read, out field!);

    internal static ClassicInverseReceiverAliasProof Derive(
        IrFunction planning, IrFunction raw, TypeRef machine, ClassicInverseBudget budget)
    {
        var proof = new ClassicInverseReceiverAliasProof();
        var first = Observe(planning, machine, budget);
        var second = Observe(raw, machine, budget);
        if (first is null || second is null
            || first.Value.Store.Index != second.Value.Store.Index
            || first.Value.Store.SourceOffset != second.Value.Store.SourceOffset
            || !Equals(first.Value.Field, second.Value.Field)
            || !Equals(first.Value.Store.Type, second.Value.Store.Type))
            return proof;

        proof._stores.Add(first.Value.Store);
        proof._stores.Add(second.Value.Store);
        foreach (var read in first.Value.Reads.Concat(second.Value.Reads))
        {
            if (!budget.Charge())
                return new ClassicInverseReceiverAliasProof();
            proof._reads[read] = first.Value.Field;
        }
        return proof;
    }

    static (StoreLocal Store, FieldRef Field, List<LoadLocal> Reads)? Observe(
        IrFunction body, TypeRef machine, ClassicInverseBudget budget)
    {
        if (body.Body.Blocks.Count == 0 || body.Body.Blocks[0].StartOffset != 0)
            return null;
        Block root = body.Body.Blocks[0];
        StoreLocal? alias = null;
        FieldRef? field = null;
        foreach (var statement in root.Children)
        {
            if (!budget.Charge())
                return null;
            if (statement is StoreLocal
                {
                    Value: LoadField { Field.Name: "<>4__this", Instance: LoadArgument { Index: 0 } } capture,
                } store && ClassicInverseNodeFacts.IsMachineField(capture.Field, machine))
            {
                alias = store;
                field = capture.Field;
                break;
            }
            // Only the compiler's initial state read may precede the alias.
            if (statement is not StoreLocal
                {
                    Value: LoadField { Field.Name: "<>1__state", Instance: LoadArgument { Index: 0 } } state,
                } || !ClassicInverseNodeFacts.IsMachineField(state.Field, machine))
                return null;
        }
        if (alias is null || field is null || alias.SourceOffset < 0
            || !Equals(alias.Type, field.Type)
            || field.Type.DeclaredValueTypeHint != ValueTypeHint.ReferenceType
                && body.TypeShapes.GetValueOrDefault(CoercionRendering.NamedDefinition(field.Type)) != TypeShape.Reference
            || alias.Index < 0
            || alias.Index < body.LocalNames.Length && body.LocalNames[alias.Index] is not null)
            return null;

        var reads = new List<LoadLocal>();
        bool initialized = false;
        foreach (var node in body.Body.Descendants)
        {
            if (!budget.Charge())
                return null;
            switch (node)
            {
                case StoreLocal store when store.Index == alias.Index:
                    if (!ReferenceEquals(store, alias) || initialized)
                        return null;
                    initialized = true;
                    break;
                case LoadLocalAddress address when address.Index == alias.Index:
                    return null;
                case LoadLocal read when read.Index == alias.Index:
                    if (!initialized || !Equals(read.Type, field.Type))
                        return null;
                    reads.Add(read);
                    break;
            }
        }
        return reads.Count == 0 ? null : (alias, field, reads);
    }
}
