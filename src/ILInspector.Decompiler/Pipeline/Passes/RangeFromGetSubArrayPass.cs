using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's array range-slice lowering back into a C# range indexer:
/// <c>RuntimeHelpers.GetSubArray(a, range)</c> becomes <c>a[range]</c>. The
/// <c>range</c> argument is one of the compiler's <see cref="System.Range"/>
/// constructions — <c>new Range((Index)i, (Index)j)</c>, <c>Range.StartAt</c>,
/// <c>Range.EndAt</c>, or <c>Range.All</c> — which the pass rewrites into a
/// <see cref="RangeExpression"/> (<c>i..j</c>, <c>i..</c>, <c>..j</c>, <c>..</c>).
///
/// <para><c>GetSubArray</c> is a compiler-only helper with no hand-written
/// spelling, so recognizing it is unambiguous and the round-trip is opcode-exact:
/// the recovered <c>a[range]</c> re-lowers to the same call. This first slice
/// covers from-start endpoints (the implicit <c>int</c>-to-<see cref="System.Index"/>
/// conversion); from-end endpoints (<c>^n</c>) and the string/span <c>Substring</c>
/// / <c>Slice</c> forms are left untouched (a later slice).</para>
/// </summary>
public sealed class RangeFromGetSubArrayPass : IIrPass
{
    public string Name => "range-getsubarray";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (call.Callee is not { Name: "GetSubArray", DeclaringType.Namespace: "System.Runtime.CompilerServices", DeclaringType.Name: "RuntimeHelpers" })
                continue;
            if (call.Arguments is not [var receiver, var rangeArg])
                continue;
            if (call.ResultType is not { } resultType)
                continue;
            if (!TryReadRange(rangeArg, out var start, out var end))
                continue;

            // Validation is complete; now detach the parts we keep (the receiver
            // and the from-start endpoint values) and discard the lowering
            // wrappers (the Range/Index construction) by replacing the call.
            receiver.Detach();
            start?.Detach();
            end?.Detach();
            var range = new RangeExpression(start, end);
            var slice = new SliceExpression(receiver, range, resultType);
            context.Stepper.StepOver("raise GetSubArray range slice to a[..] indexer", call);
            call.ReplaceWith(slice);
        }
    }

    /// <summary>
    /// Recognizes a <see cref="System.Range"/> construction and yields its
    /// endpoint value expressions (still attached — the caller detaches them only
    /// after the whole shape is confirmed). A null endpoint is an omitted side of
    /// the range. Returns false for any shape that is not a from-start range
    /// construction (e.g. a from-end <c>^n</c> endpoint), leaving the call as-is.
    /// </summary>
    static bool TryReadRange(IrExpression rangeArg, out IrExpression? start, out IrExpression? end)
    {
        start = null;
        end = null;
        switch (rangeArg)
        {
            case NewObject { Constructor.DeclaringType: { Namespace: "System", Name: "Range" }, Arguments: [var lo, var hi] }:
                return TryFromStartEndpoint(lo, out start) && TryFromStartEndpoint(hi, out end);
            case Call { Callee: { Name: "StartAt", DeclaringType: { Namespace: "System", Name: "Range" } }, Arguments: [var lo] }:
                return TryFromStartEndpoint(lo, out start);
            case Call { Callee: { Name: "EndAt", DeclaringType: { Namespace: "System", Name: "Range" } }, Arguments: [var hi] }:
                return TryFromStartEndpoint(hi, out end);
            case LoadProperty { HasInstance: false, PropertyName: "All", Accessor.DeclaringType: { Namespace: "System", Name: "Range" } }:
                return true; // Range.All → `..`
            default:
                return false;
        }
    }

    /// <summary>
    /// A from-start endpoint is the implicit <c>int</c>-to-<see cref="System.Index"/>
    /// conversion (<c>(Index)expr</c>); the inner <c>int</c> expression is the
    /// range endpoint. A from-end <c>new Index(n, fromEnd: true)</c> is not
    /// matched here.
    /// </summary>
    static bool TryFromStartEndpoint(IrExpression index, out IrExpression? value)
    {
        if (index is Call { Callee: { Name: "op_Implicit", DeclaringType: { Namespace: "System", Name: "Index" } }, Arguments: [var inner] })
        {
            value = inner;
            return true;
        }

        value = null;
        return false;
    }
}
