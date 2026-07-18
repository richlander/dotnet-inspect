namespace ILInspector.Decompiler.Pipeline;

/// <summary>Shared structural proof for raising a protected-region <see cref="Leave"/>.</summary>
public static class ProtectedRegionControlFlow
{
    public static bool CanRaiseLeave(Leave leave)
        => (HasAncestor<TryFinally>(leave) || HasAncestor<TryCatch>(leave))
            && !IsInsideFinallyBody(leave);

    public static bool CanRaiseLeave(Leave leave, IrNode exclusiveBoundary)
        => !IsInsideFinallyBody(leave)
            && HasProtectedAncestorBelow(leave, exclusiveBoundary);

    static bool HasAncestor<T>(IrNode node) where T : IrNode
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is T)
                return true;
        return false;
    }

    static bool IsInsideFinallyBody(Leave leave)
    {
        for (var current = leave.Parent; current is not null; current = current.Parent)
        {
            if (current.Parent is TryFinally tryFinally && ReferenceEquals(current, tryFinally.FinallyBody))
                return true;
        }
        return false;
    }

    static bool HasProtectedAncestorBelow(Leave leave, IrNode exclusiveBoundary)
    {
        bool foundProtectedRegion = false;
        for (var current = leave.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, exclusiveBoundary))
                return foundProtectedRegion;
            if (current is TryFinally or TryCatch)
                foundProtectedRegion = true;
        }
        return false;
    }
}
