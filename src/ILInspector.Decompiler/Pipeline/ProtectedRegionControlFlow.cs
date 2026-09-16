using System.Reflection.Metadata;
using ILInspector.Instructions;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Decompiler policy for raising a protected-region <see cref="Leave"/>.
/// Metadata-backed bodies consume Instructions normal-transfer facts; synthetic
/// Layer 0 trees retain the structural compatibility path.
/// </summary>
public static class ProtectedRegionControlFlow
{
    public static bool CanRaiseLeave(Leave leave)
    {
        if (TryGetSharedTransfer(
                leave,
                out IrFunction? function,
                out InstructionExceptionFlowFacts facts,
                out InstructionNormalTransfer transfer))
        {
            HashSet<InstructionExceptionRegionId> raisable =
                RaisableRegionsLeft(facts, transfer);
            return raisable.Count > 0
                && HasAssociatedRegionBelow(
                    leave,
                    function!,
                    function!,
                    facts,
                    transfer,
                    raisable);
        }

        return UsesSyntheticCompatibility(function)
            && CanRaiseSyntheticLeave(leave);
    }

    /// <summary>
    /// Additionally correlates a region left by the transfer with an exact
    /// structured association below the candidate construct.
    /// </summary>
    public static bool CanRaiseLeave(Leave leave, IrNode exclusiveBoundary)
    {
        if (TryGetSharedTransfer(
                leave,
                out IrFunction? function,
                out InstructionExceptionFlowFacts facts,
                out InstructionNormalTransfer transfer))
        {
            HashSet<InstructionExceptionRegionId> raisable =
                RaisableRegionsLeft(facts, transfer);
            return raisable.Count > 0
                && HasAssociatedRegionBelow(
                    leave,
                    exclusiveBoundary,
                    function!,
                    facts,
                    transfer,
                    raisable);
        }

        return UsesSyntheticCompatibility(function)
            && CanRaiseSyntheticLeave(leave, exclusiveBoundary);
    }

    static bool TryGetSharedTransfer(
        Leave leave,
        out IrFunction? function,
        out InstructionExceptionFlowFacts facts,
        out InstructionNormalTransfer transfer)
    {
        function = OwningFunction(leave);
        facts = null!;
        transfer = null!;

        switch (function?.ExceptionFlow)
        {
            case InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Available available:
                facts = available.Value;
                break;
            case InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable unavailable:
                RecordFailure(
                    function,
                    $"Instructions exception-flow evidence is unavailable "
                    + $"({unavailable.Reason}): {unavailable.Detail}");
                return false;
            case InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Ambiguous ambiguous:
                RecordFailure(
                    function,
                    "Instructions exception-flow evidence is ambiguous: "
                    + ambiguous.Detail);
                return false;
            case null when function?.IsMetadataBacked == true:
                RecordFailure(
                    function,
                    "Metadata-backed protected-region control flow has no "
                    + "correlated Instructions evidence.");
                return false;
            case null:
                return false;
            default:
                throw new InvalidOperationException(
                    "Unknown Instructions exception-flow result.");
        }

        switch (facts.NormalTransferAt(
                    leave.SourceOffset,
                    leave.TargetOffset))
        {
            case InstructionExceptionFlowResult<
                InstructionNormalTransfer>.Available available
                when available.Value.Kind
                    == InstructionNormalTransferKind.Leave:
                transfer = available.Value;
                return !OriginatesInCleanupHandler(facts, transfer);
            case InstructionExceptionFlowResult<
                InstructionNormalTransfer>.Available available:
                RecordFailure(
                    function!,
                    $"Instructions normal-transfer evidence at "
                    + $"IL_{leave.SourceOffset:X4} reports "
                    + $"{available.Value.Kind}, not Leave.");
                return false;
            case InstructionExceptionFlowResult<
                InstructionNormalTransfer>.Unavailable unavailable:
                RecordFailure(
                    function!,
                    $"Instructions normal-transfer evidence at "
                    + $"IL_{leave.SourceOffset:X4} is unavailable "
                    + $"({unavailable.Reason}): {unavailable.Detail}");
                return false;
            case InstructionExceptionFlowResult<
                InstructionNormalTransfer>.Ambiguous ambiguous:
                RecordFailure(
                    function!,
                    $"Instructions normal-transfer evidence at "
                    + $"IL_{leave.SourceOffset:X4} is ambiguous: "
                    + ambiguous.Detail);
                return false;
            default:
                throw new InvalidOperationException(
                    "Unknown Instructions normal-transfer result.");
        }
    }

    static HashSet<InstructionExceptionRegionId> RaisableRegionsLeft(
        InstructionExceptionFlowFacts facts,
        InstructionNormalTransfer transfer)
        => transfer.RegionsLeft
            .Where(region => IsRaisableRegion(facts, region))
            .Select(region => region.Id)
            .ToHashSet();

    static bool IsRaisableRegion(
        InstructionExceptionFlowFacts facts,
        InstructionExceptionRegion region)
        => region.Id.Role switch
        {
            InstructionExceptionRegionRole.Protected => true,
            InstructionExceptionRegionRole.Handler =>
                facts.Clauses.Any(clause =>
                    clause.HandlerRegion == region.Id
                    && clause.Kind is ExceptionRegionKind.Catch
                        or ExceptionRegionKind.Filter),
            _ => false,
        };

    static bool OriginatesInCleanupHandler(
        InstructionExceptionFlowFacts facts,
        InstructionNormalTransfer transfer)
        => transfer.SourceContext.Any(region =>
            region.Id.Role == InstructionExceptionRegionRole.Handler
            && facts.Clauses.Any(clause =>
                clause.HandlerRegion == region.Id
                && clause.Kind is ExceptionRegionKind.Finally
                    or ExceptionRegionKind.Fault));

    static bool HasAssociatedRegionBelow(
        Leave leave,
        IrNode exclusiveBoundary,
        IrFunction function,
        InstructionExceptionFlowFacts facts,
        InstructionNormalTransfer transfer,
        HashSet<InstructionExceptionRegionId> raisable)
    {
        var sourceRegions = transfer.SourceContext
            .Select(region => region.Id)
            .ToHashSet();
        bool matched = false;
        bool invalidAssociation = false;
        IrNode child = leave;
        for (IrNode? current = leave.Parent;
             current is not null;
             child = current, current = current.Parent)
        {
            if (ReferenceEquals(current, exclusiveBoundary))
                return matched && !invalidAssociation;

            InstructionExceptionRegionId? association = current switch
            {
                TryCatch tryCatch when ReferenceEquals(
                    child,
                    tryCatch.TryBody) =>
                    tryCatch.ExceptionProtectedRegion,
                TryCatch when child is CatchClause clause =>
                    clause.ExceptionClause?.HandlerRegion,
                TryFinally tryFinally when ReferenceEquals(
                    child,
                    tryFinally.TryBody) =>
                    tryFinally.ExceptionClause?.ProtectedRegion,
                _ => null,
            };

            if (current is not (TryCatch or TryFinally))
                continue;

            if (association is not { } region)
            {
                invalidAssociation = true;
                RecordFailure(
                    function,
                    $"Structured protected region containing "
                    + $"IL_{leave.SourceOffset:X4} has no owner-issued "
                    + "Instructions association.");
                continue;
            }

            if (region.Body != facts.Body
                || !sourceRegions.Contains(region))
            {
                invalidAssociation = true;
                RecordFailure(
                    function,
                    $"Structured protected-region association containing "
                    + $"IL_{leave.SourceOffset:X4} does not match its "
                    + "Instructions source context.");
                continue;
            }

            matched |= raisable.Contains(region);
        }

        return false;
    }

    static IrFunction? OwningFunction(IrNode node)
    {
        for (IrNode? current = node.Parent;
             current is not null;
             current = current.Parent)
        {
            if (current is IrFunction function)
                return function;
        }

        return null;
    }

    static void RecordFailure(IrFunction function, string reason)
        => function.ExceptionFactFailure ??= reason;

    static bool UsesSyntheticCompatibility(IrFunction? function)
        => function is null
            || (!function.IsMetadataBacked
                && function.ExceptionFlow is null);

    static bool CanRaiseSyntheticLeave(Leave leave)
        => !IsInsideSyntheticFinallyBody(leave)
            && HasSyntheticProtectedAncestor(leave);

    static bool CanRaiseSyntheticLeave(
        Leave leave,
        IrNode exclusiveBoundary)
        => !IsInsideSyntheticFinallyBody(leave)
            && HasSyntheticProtectedAncestorBelow(
                leave,
                exclusiveBoundary);

    static bool HasSyntheticProtectedAncestor(IrNode node)
    {
        for (var current = node.Parent;
             current is not null;
             current = current.Parent)
        {
            if (IsSyntheticProtectedNode(current))
                return true;
        }
        return false;
    }

    static bool HasSyntheticProtectedAncestorBelow(
        IrNode node,
        IrNode exclusiveBoundary)
    {
        for (var current = node.Parent;
             current is not null
                && !ReferenceEquals(current, exclusiveBoundary);
             current = current.Parent)
        {
            if (IsSyntheticProtectedNode(current))
                return true;
        }
        return false;
    }

    static bool IsInsideSyntheticFinallyBody(IrNode node)
    {
        for (var current = node;
             current.Parent is not null;
             current = current.Parent)
        {
            if (current.Parent is TryFinally tryFinally
                && ReferenceEquals(current, tryFinally.FinallyBody))
                return true;
        }
        return false;
    }

    static bool IsSyntheticProtectedNode(IrNode node)
        => node is TryCatch or TryFinally;
}
