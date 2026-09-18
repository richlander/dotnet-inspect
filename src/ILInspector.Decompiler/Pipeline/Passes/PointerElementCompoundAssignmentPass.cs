namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers a pointer-element compound update from its exclusive address spill.
/// See docs/design/pointer-element-compound-updates.md.
/// </summary>
public sealed class PointerElementCompoundAssignmentPass : IIrPass
{
    public string Name => "pointer-element-compound-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        var nodes = function.DescendantsOutsideNestedFunctions.ToArray();
        var loads = nodes.OfType<LoadStackSlot>().ToLookup(load => load.Slot);
        var stores = nodes.OfType<StoreStackSlot>().ToLookup(store => store.Slot);
        var branchTargets = ReferenceOwnership.CollectBranchTargets(function);

        foreach (var capture in nodes.OfType<StoreStackSlot>())
        {
            if (capture.Parent is not Block block
                || capture.ChildIndex + 1 >= block.Children.Count
                || stores[capture.Slot].Count() != 1
                || loads[capture.Slot].Count() != 2
                || branchTargets.Contains(capture.SourceOffset)
                || block.Children[capture.ChildIndex + 1] is not StoreIndirect
                {
                    IsVolatile: false,
                    Address: LoadStackSlot destination,
                    Value: Binary { IsChecked: false, Left: LoadIndirect { IsVolatile: false } read } operation,
                } update
                || branchTargets.Contains(update.SourceOffset)
                || destination.Slot != capture.Slot
                || read.Address is not LoadStackSlot source
                || source.Slot != capture.Slot
                || operation.Kind is not (BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply
                    or BinaryKind.And or BinaryKind.Or or BinaryKind.Xor)
                || capture.Value is not Binary
                {
                    Kind: BinaryKind.Add,
                    IsChecked: false,
                    Left.ResultType: { Kind: TypeRefKind.Pointer, ElementType: { } element },
                } address
                || !IsElementType(element)
                || !element.Equals(operation.Right.ResultType)
                || !MatchesStorageWidth(element, read.Type)
                || !MatchesStorageWidth(element, update.Type)
                || !TryIndex(address.Right, element, out var index))
            {
                continue;
            }

            var pointer = address.Left;
            var value = operation.Right;
            index.InheritSourceOffset(address.Right);
            context.Stepper.StepOver("raise exclusive pointer-element compound update", capture);
            pointer.Detach();
            if (index.Parent is not null)
                index.Detach();
            value.Detach();
            var assignment = new PointerElementCompoundAssignment(element, operation.Kind, pointer, index, value);
            assignment.InheritSourceOffset(update);
            capture.ReplaceWith(assignment);
            update.Detach();
        }
    }

    static bool IsElementType(TypeRef? type)
        => type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            Namespace: "System",
            Name: "Int32" or "UInt32" or "Int64" or "UInt64",
        };

    static bool MatchesStorageWidth(TypeRef element, TypeRef? storage)
        => storage is not null && IsElementType(storage)
            && PointerArithmetic.ByteSize(element) == PointerArithmetic.ByteSize(storage);

    static bool TryIndex(IrExpression offset, TypeRef element, out IrExpression index)
    {
        if (!PointerArithmetic.TryScaledPointerIndex(offset, element, out index)
            || index.ResultType is not
            {
                Kind: TypeRefKind.Definition,
                Assembly: TypeRef.CoreLibrary,
                Namespace: "System",
                Name: "Int32",
            })
        {
            return false;
        }

        if (offset is Constant { Value: int })
            return true;
        if (offset is not Binary { Kind: BinaryKind.Multiply, IsChecked: false } multiply)
            return false;

        return IsNativeIndex(multiply.Left, index) || IsNativeIndex(multiply.Right, index);
    }

    static bool IsNativeIndex(IrExpression expression, IrExpression index)
        => expression is Convert
        {
            IsChecked: false,
            IsUnsigned: false,
            Target: { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IntPtr" },
        } conversion && ReferenceEquals(conversion.Operand, index);
}
