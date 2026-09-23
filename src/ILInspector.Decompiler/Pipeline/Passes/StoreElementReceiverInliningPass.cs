namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds the compiler's single-use address-receiver temporary for an array
/// element <c>ToString()</c> store before declaration planning:
/// <c>V = Call(); array[index] = V.ToString()</c> becomes
/// <c>array[index] = Call().ToString()</c>.
/// </summary>
public sealed class StoreElementReceiverInliningPass : IIrPass
{
    public string Name => "store-element-receiver-inlining";

    public void Run(IrFunction function, PassContext context)
    {
        var branchTargets =
            ReferenceOwnership.CollectBranchTargets(function);
        while (TryFoldOne(function, branchTargets, context))
        {
        }
    }

    static bool TryFoldOne(
        IrFunction function,
        IReadOnlySet<int> branchTargets,
        PassContext context)
    {
        foreach (var block in function
            .DescendantsOutsideNestedFunctions
            .OfType<Block>())
        {
            for (int index = 0;
                index + 1 < block.Children.Count;
                index++)
            {
                if (block.Children[index] is not StoreLocal store
                    || block.Children[index + 1]
                        is not StoreElement storeElement
                    || !CanFold(
                        function,
                        branchTargets,
                        store,
                        storeElement,
                        out var receiver))
                {
                    continue;
                }

                var value =
                    (IrExpression)store.DetachChildren()[0];
                store.Detach();
                context.Stepper.StepOver(
                    $"inline receiver temp {store.Index} into array-element ToString",
                    storeElement);
                receiver.ReplaceWith(value);
                storeElement.ReceiverTempInlined = true;
                return true;
            }
        }

        return false;
    }

    static bool CanFold(
        IrFunction function,
        IReadOnlySet<int> branchTargets,
        StoreLocal store,
        StoreElement storeElement,
        out LoadLocalAddress receiver)
    {
        receiver = null!;
        if (store.Type.Kind == TypeRefKind.ByRef
            || store.Value is not Call
            || store.Index < function.LocalNames.Length
                && function.LocalNames[store.Index] is not null
            || store.OwnsSourceLabel
                && store.SourceOffset >= 0
                && branchTargets.Contains(store.SourceOffset)
            || storeElement.Value is not Call
            {
                Callee:
                {
                    HasThis: true,
                    Name: "ToString",
                } callee,
                Arguments: [LoadLocalAddress candidate],
            } call
            || candidate.Index != store.Index
            || storeElement.ElementType is not null
                && !Equals(
                    callee.ReturnType,
                    storeElement.ElementType)
            || !CanEvaluateBeforeValue(
                storeElement.Array,
                store.Value)
            || !CanEvaluateBeforeValue(
                storeElement.Index,
                store.Value))
        {
            return false;
        }

        int stores = 0;
        int addressLoads = 0;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreLocal local
                    when local.Index == store.Index:
                    stores++;
                    if (!ReferenceEquals(local, store))
                        return false;
                    break;
                case LoadLocalAddress address
                    when address.Index == store.Index:
                    addressLoads++;
                    if (!ReferenceEquals(address, candidate))
                        return false;
                    break;
                case LoadLocal local
                    when local.Index == store.Index:
                    return false;
            }
        }

        if (stores != 1
            || addressLoads != 1
            || !ReferenceEquals(
                call.Arguments[0],
                candidate))
        {
            return false;
        }

        receiver = candidate;
        return true;
    }

    static bool CanEvaluateBeforeValue(
        IrExpression expression,
        IrExpression value)
        => expression switch
        {
            Constant => true,
            LoadArgument argument => !ReferencesArgument(
                value,
                argument.Index,
                argument.Parameter),
            LoadLocal local => !ReferencesLocal(
                value,
                local.Index),
            _ => false,
        };

    static bool ReferencesArgument(
        IrNode node,
        int index,
        Parameter? parameter)
        => IsArgumentReference(node, index, parameter)
            || node.Descendants.Any(
                descendant => IsArgumentReference(
                    descendant,
                    index,
                    parameter));

    static bool IsArgumentReference(
        IrNode node,
        int index,
        Parameter? parameter)
        => node is LoadArgument argument
                && PlaceIdentity.SameArgument(
                    argument.Index,
                    argument.Parameter,
                    index,
                    parameter)
            || node is LoadArgumentAddress address
                && PlaceIdentity.SameArgument(
                    address.Index,
                    address.Parameter,
                    index,
                    parameter);

    static bool ReferencesLocal(IrNode node, int index)
        => IsLocalReference(node, index)
            || node.DescendantsOutsideNestedFunctions.Any(
                descendant => IsLocalReference(
                    descendant,
                    index));

    static bool IsLocalReference(IrNode node, int index)
        => node switch
        {
            LoadLocal local => local.Index == index,
            LoadLocalAddress address => address.Index == index,
            StoreLocal store => store.Index == index,
            _ => false,
        };
}
