using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the narrow tuple deconstruction-declaration lowering:
/// <code>
/// S = tuple;
/// T1 a = S.Item1;
/// T2 b = S.Item2;
/// </code>
/// into <c>(T1 a, T2 b) = tuple;</c>. Scoped to local declarations from direct
/// <c>System.ValueTuple</c> fields, arities 2-7. Existing-local assignments,
/// nested/rest tuples, and user-defined <c>Deconstruct</c> calls are later slices.
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
                if (children[i] is not StoreStackSlot seed
                    || seed.Value.ResultType is not { } tupleType
                    || ValueTupleArity(tupleType) is not { } arity
                    || arity is < 2 or > 7
                    || i + arity >= children.Count)
                {
                    continue;
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
                    || !ReferencedOnlyWithin(function, seed.Slot, stores)
                    || stores.Any(store => !IsFirstLocalReference(function, store)))
                {
                    continue;
                }

                var source = (IrExpression)seed.DetachChildren()[0];
                var localIndices = stores.Select(store => store.Index).ToImmutableArray();
                var localTypes = stores.Select(store => store.Type).ToImmutableArray();
                var deconstruction = new DeconstructionAssignment(localIndices, localTypes, source);
                context.Stepper.StepOver("raise ValueTuple field stores to deconstruction", seed);
                seed.ReplaceWith(deconstruction);
                foreach (var store in stores)
                    store.Detach();
                return;
            }
        }
    }

    static int? ValueTupleArity(TypeRef type)
    {
        if (type is not { Kind: TypeRefKind.GenericInstance, ElementType: { } definition })
            return null;
        if (definition is not { Namespace: "System", Assembly: TypeRef.CoreLibrary or "System.Runtime" }
            || !definition.Name.StartsWith("ValueTuple`", StringComparison.Ordinal))
        {
            return null;
        }
        return type.TypeArguments.Length;
    }

    static bool ReferencedOnlyWithin(IrFunction function, int slot, IReadOnlyList<StoreLocal> stores)
    {
        foreach (var load in function.Descendants.OfType<LoadStackSlot>().Where(load => load.Slot == slot))
            if (!stores.Any(store => IsInside(load, store)))
                return false;
        return true;
    }

    static bool IsFirstLocalReference(IrFunction function, StoreLocal target)
    {
        foreach (var node in function.Descendants)
        {
            if (ReferenceEquals(node, target))
                return true;
            if (node is LoadLocal load && load.Index == target.Index
                || node is StoreLocal store && store.Index == target.Index
                || node is LoadLocalAddress address && address.Index == target.Index)
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
