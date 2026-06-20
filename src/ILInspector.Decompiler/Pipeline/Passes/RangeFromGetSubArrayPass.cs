using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's array range-slice lowering back into a C# range indexer:
/// <c>RuntimeHelpers.GetSubArray(a, range)</c> becomes <c>a[range]</c>. The
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
/// <para>The BCL <c>GetSubArray</c> is a compiler helper with no ordinary
/// user-facing spelling in range syntax, so recognizing that exact helper is
/// unambiguous and the round-trip is opcode-exact: the recovered
/// <c>a[range]</c> re-lowers to the same call. The string/span <c>Substring</c>
/// / <c>Slice</c> forms are a separate lowering and left untouched here.</para>
/// </summary>
public sealed class RangeFromGetSubArrayPass : IIrPass
{
    public string Name => "range-getsubarray";

    /// <summary>A validated range endpoint: the inner index expression plus whether it counts from the end (<c>^n</c>).</summary>
    readonly record struct Endpoint(IrExpression Inner, bool FromEnd);

    public void Run(IrFunction function, PassContext context)
    {
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
            case NewObject { Constructor.DeclaringType: { Namespace: "System", Name: "Range" }, Arguments: [var lo, var hi] }:
                return TryEndpoint(lo, out start) && TryEndpoint(hi, out end);
            case Call { Callee: { Name: "StartAt", DeclaringType: { Namespace: "System", Name: "Range" } }, Arguments: [var lo] }:
                return TryEndpoint(lo, out start);
            case Call { Callee: { Name: "EndAt", DeclaringType: { Namespace: "System", Name: "Range" } }, Arguments: [var hi] }:
                return TryEndpoint(hi, out end);
            case LoadProperty { HasInstance: false, PropertyName: "All", Accessor.DeclaringType: { Namespace: "System", Name: "Range" } }:
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
            case Call { Callee: { Name: "op_Implicit", DeclaringType: { Namespace: "System", Name: "Index" } }, Arguments: [var inner] }:
                endpoint = new Endpoint(inner, FromEnd: false);
                return true;
            case NewObject { Constructor.DeclaringType: { Namespace: "System", Name: "Index" }, Arguments: [var offset, Constant { Value: true }] }:
                endpoint = new Endpoint(offset, FromEnd: true);
                return true;
            default:
                endpoint = null;
                return false;
        }
    }
}
