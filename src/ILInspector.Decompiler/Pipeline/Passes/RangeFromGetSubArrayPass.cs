using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's range-slice lowerings back into a C# range indexer.
/// For arrays, <c>RuntimeHelpers.GetSubArray(a, range)</c> becomes <c>a[range]</c>. The
/// <c>range</c> argument is one of the compiler's <see cref="System.Range"/>
/// constructions — <c>new Range(lo, hi)</c>, <c>Range.StartAt</c>,
/// <c>Range.EndAt</c>, or <c>Range.All</c> — which the pass rewrites into a
/// <see cref="RangeExpression"/> (<c>i..j</c>, <c>i..</c>, <c>..j</c>, <c>..</c>).
///
/// <para>Each endpoint is either a from-start index — the implicit
/// <c>int</c>-to-<see cref="System.Index"/> conversion, rendered bare (<c>i</c>) —
/// or a from-end index — <c>new Index(n, fromEnd: true)</c>, rendered <c>^n</c>
/// via an <see cref="IndexFromEnd"/> node — so the whole space of array range
/// slices (<c>a[i..^1]</c>, <c>a[^3..^1]</c>, <c>a[..^1]</c>, …) is recovered.</para>
///
/// <para>The string/span <c>Substring</c> / <c>Slice</c> forms share their
/// methods with ordinary source calls, so those are raised only when csc's hidden
/// start/receiver spills prove the range lowering. The two-bound from-start form
/// (<c>V = start; …Substring(V, end - V)…</c>) is recovered in any statement
/// position — return, local assignment, or call argument — provided the spilled
/// start temp is single-use and nothing effectful is evaluated before the call.</para>
/// </summary>
public sealed class RangeFromGetSubArrayPass : IIrPass
{
    public string Name => "range-getsubarray";

    /// <summary>A validated range endpoint: the inner index expression plus whether it counts from the end (<c>^n</c>).</summary>
    readonly record struct Endpoint(IrExpression Inner, bool FromEnd);

    public void Run(IrFunction function, PassContext context)
    {
        while (TryRaiseStringOrSpanRange(function, context.Stepper))
        {
        }

        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (!MemberIdentity.IsRuntimeHelpersGetSubArray(call))
                continue;
            if (call.Arguments is not [var receiver, var rangeArg])
                continue;
            if (call.ResultType is not { } resultType)
                continue;
            if (!TryReadRange(rangeArg, out var startEndpoint, out var endEndpoint))
                continue;

            // Validation is complete; now detach the parts we keep (the receiver
            // and each endpoint's inner index value) and discard the lowering
            // wrappers (the Range/Index construction) by replacing the call.
            receiver.Detach();
            var start = BuildEndpoint(startEndpoint);
            var end = BuildEndpoint(endEndpoint);
            var range = new RangeExpression(start, end);
            var slice = new SliceExpression(receiver, range, resultType);
            context.Stepper.StepOver("raise GetSubArray range slice to a[..] indexer", call);
            call.ReplaceWith(slice);
        }
    }

    static bool TryRaiseStringOrSpanRange(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            for (int i = 1; i < block.Children.Count; i++)
            {
                if (TryRaiseFromStartRange(function, block, i, stepper))
                    return true;
                if (TryRaiseFromEndOpenRange(function, block, i, stepper))
                    return true;
            }
        }

        return false;
    }

    static bool TryRaiseFromStartRange(IrFunction function, Block block, int index, Stepper stepper)
    {
        // The compiler spills the range start into a hidden int temp, whose store
        // sits immediately before the statement that consumes the slice. The
        // consumer is not restricted to a `return`: the witness idiom assigns the
        // slice to a named local (`token = text.Substring(V, i - V)`), and it can
        // equally appear as a call argument, so any statement position qualifies
        // as long as the spill temp is single-use (below) and the store stays the
        // consumer's immediate predecessor in this basic block.
        if (block.Children[index - 1] is not StoreLocal startStore
            || HasSourceLocalName(function, startStore.Index)
            || !startStore.Type.Equals(TypeRef.CoreLib("System", "Int32"))
            || !IsSingleUseStartSpill(function, startStore.Index))
        {
            return false;
        }

        var consumer = block.Children[index];
        foreach (var call in consumer.Descendants.OfType<Call>())
        {
            if (!IsStringOrSpanRangeCall(call)
                || call.Callee.ParameterTypes.Length != 2
                || call.Arguments is not [var receiver, LoadLocal start, Binary { Kind: BinaryKind.Subtract } length]
                || start.Index != startStore.Index
                || length.Right is not LoadLocal lengthStart
                || lengthStart.Index != startStore.Index)
            {
                continue;
            }

            // Everything the consuming statement evaluates before the call must be
            // side-effect free. The spill store currently runs first, so raising
            // the slice (which re-spills the start at the call site on recompile)
            // would otherwise move the start's evaluation past an observable effect.
            if (!NothingEffectfulBefore(consumer, call))
                return false;

            receiver.Detach();
            var rangeStart = (IrExpression)startStore.DetachChildren()[0];
            var rangeEnd = length.Left;
            rangeEnd.Detach();
            var slice = new SliceExpression(receiver, new RangeExpression(rangeStart, rangeEnd), call.ResultType);
            slice.InheritSourceOffset(call);

            stepper.StepOver("raise compiler-spilled string/span Slice range to a[..] indexer", call);
            call.ReplaceWith(slice);
            startStore.Detach();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the hidden start-index temp is referenced only by the range
    /// lowering: exactly one store (the spill), exactly two reads (the range
    /// start and the <c>end - start</c> length), and no address-of. A
    /// basic-block-adjacent store is a read's unique reaching definition, but a
    /// reused or aliased slot would leave live references behind after the store
    /// is detached, so those are declined.
    /// </summary>
    static bool IsSingleUseStartSpill(IrFunction function, int index)
    {
        int loads = 0;
        int stores = 0;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal load when load.Index == index:
                    loads++;
                    break;
                case StoreLocal store when store.Index == index:
                    stores++;
                    break;
                case LoadLocalAddress address when address.Index == index:
                    return false;
            }
        }

        return stores == 1 && loads == 2;
    }

    /// <summary>
    /// Whether every expression the consuming statement evaluates before
    /// <paramref name="call"/> is side-effect free, walking the parent chain from
    /// the call up to (but excluding) the consumer statement and checking each
    /// earlier sibling.
    /// </summary>
    static bool NothingEffectfulBefore(IrNode consumer, IrNode call)
    {
        for (IrNode node = call; node != consumer; node = node.Parent!)
        {
            if (node.Parent is not { } parent)
                return false;
            for (int i = 0; i < node.ChildIndex; i++)
            {
                if (parent.Children[i] is not IrExpression sibling || !IsSideEffectFree(sibling))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Allow-list of non-observable expressions, mirroring the same-named helpers
    /// in <see cref="StackAllocSpanPass"/> and its peers: anything outside the
    /// proven-safe set is treated as possibly effectful.
    /// </summary>
    static bool IsSideEffectFree(IrExpression expression) => expression switch
    {
        Constant or LoadLocal or LoadArgument or LoadStackSlot
            or LoadLocalAddress or LoadArgumentAddress or SizeOf => true,
        Unary unary => IsSideEffectFree(unary.Operand),
        Binary { Kind: BinaryKind.Divide or BinaryKind.Remainder } => false,
        Binary { IsChecked: true } => false,
        Binary binary => IsSideEffectFree(binary.Left) && IsSideEffectFree(binary.Right),
        Convert { IsChecked: true } => false,
        Convert convert => IsSideEffectFree(convert.Operand),
        _ => false,
    };

    static bool TryRaiseFromEndOpenRange(IrFunction function, Block block, int returnIndex, Stepper stepper)
    {
        if (returnIndex < 2
            || block.Children[returnIndex - 2] is not StoreLocal offsetStore
            || HasSourceLocalName(function, offsetStore.Index)
            || !offsetStore.Type.Equals(TypeRef.CoreLib("System", "Int32"))
            || block.Children[returnIndex - 1] is not StoreStackSlot receiverStore
            || block.Children[returnIndex] is not Return { Value: Call call }
            || !IsStringOrSpanRangeCall(call)
            || call.Callee.ParameterTypes.Length != 1
            || call.Arguments is not [var receiver, Binary { Kind: BinaryKind.Subtract } start]
            || receiver is not LoadStackSlot receiverLoad
            || receiverLoad.Slot != receiverStore.Slot
            || LengthReceiver(start.Left) is not { } lengthReceiver
            || !PlaceIdentity.SameStackSlot(receiver, lengthReceiver)
            || start.Right is not LoadLocal offsetLoad
            || offsetLoad.Index != offsetStore.Index)
        {
            return false;
        }

        var offset = (IrExpression)offsetStore.DetachChildren()[0];
        var rangeStart = new IndexFromEnd(offset);
        var sourceReceiver = (IrExpression)receiverStore.DetachChildren()[0];
        var slice = new SliceExpression(sourceReceiver, new RangeExpression(rangeStart, end: null), call.ResultType);
        slice.InheritSourceOffset(call);

        stepper.StepOver("raise compiler-spilled string/span from-end Slice range to a[^n..] indexer", call);
        call.ReplaceWith(slice);
        offsetStore.Detach();
        receiverStore.Detach();
        return true;
    }

    static bool IsStringOrSpanRangeCall(Call call)
        => MemberIdentity.IsStringSubstring(call) || MemberIdentity.IsSpanSlice(call);

    static IrExpression? LengthReceiver(IrExpression expression) => expression switch
    {
        LoadProperty { HasInstance: true, PropertyName: "Length" } property => property.Instance,
        _ => null,
    };

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && function.LocalNames[index] is { } name
            && CSharpNaming.IsUsableIdentifier(name);

    /// <summary>
    /// Detaches the endpoint's inner index expression and wraps it in an
    /// <see cref="IndexFromEnd"/> (<c>^n</c>) when the index counts from the end;
    /// an omitted endpoint stays null (the open side of the range).
    /// </summary>
    static IrExpression? BuildEndpoint(Endpoint? endpoint)
    {
        if (endpoint is not { } value)
            return null;
        value.Inner.Detach();
        return value.FromEnd ? new IndexFromEnd(value.Inner) : value.Inner;
    }

    /// <summary>
    /// Recognizes a <see cref="System.Range"/> construction and yields its
    /// endpoints (still attached — the caller detaches them only after the whole
    /// shape is confirmed). A null endpoint is an omitted side of the range.
    /// Returns false for any shape that is not a recognized range construction,
    /// leaving the call as-is.
    /// </summary>
    static bool TryReadRange(IrExpression rangeArg, out Endpoint? start, out Endpoint? end)
    {
        start = null;
        end = null;
        switch (rangeArg)
        {
            case NewObject { Arguments: [var lo, var hi] } construction when MemberIdentity.IsRangeConstructor(construction.Constructor):
                return TryEndpoint(lo, out start) && TryEndpoint(hi, out end);
            case Call { Arguments: [var lo] } startAt when MemberIdentity.IsRangeStartAt(startAt.Callee):
                return TryEndpoint(lo, out start);
            case Call { Arguments: [var hi] } endAt when MemberIdentity.IsRangeEndAt(endAt.Callee):
                return TryEndpoint(hi, out end);
            case LoadProperty { HasInstance: false } all when MemberIdentity.IsRangeAllGetter(all.Accessor):
                return true; // Range.All → `..`
            default:
                return false;
        }
    }

    /// <summary>
    /// Classifies a single <see cref="System.Index"/> endpoint: the implicit
    /// <c>int</c>-to-<c>Index</c> conversion (<c>(Index)expr</c>) is a from-start
    /// index whose inner <c>int</c> renders bare; <c>new Index(n, fromEnd: true)</c>
    /// is a from-end index whose offset renders as <c>^n</c>. Anything else (an
    /// unmodeled <c>Index</c> shape) leaves the whole call unraised.
    /// </summary>
    static bool TryEndpoint(IrExpression index, out Endpoint? endpoint)
    {
        switch (index)
        {
            case Call { Arguments: [var inner] } conversion when MemberIdentity.IsIndexFromStartConversion(conversion.Callee):
                endpoint = new Endpoint(inner, FromEnd: false);
                return true;
            case NewObject { Arguments: [var offset, Constant { Value: true }] } fromEnd when MemberIdentity.IsIndexFromEndConstructor(fromEnd.Constructor):
                endpoint = new Endpoint(offset, FromEnd: true);
                return true;
            default:
                endpoint = null;
                return false;
        }
    }
}
