using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Materializes a <c>bool</c> operand of an integer bitwise <c>&amp;</c>/<c>|</c>/
/// <c>^</c> into the <c>0/1</c> integer it carries. A bitwise <c>and</c>/<c>or</c>/
/// <c>xor</c> that mixes a <c>bool</c>-typed operand (a <see cref="Comparison"/>
/// result, a bool-typed slot, a bool field/call) with an integer operand combines
/// two <c>i4</c> (0/1) values on the IL stack, so the opcode is legal — but C# has
/// no <c>bool &amp; int</c> (CS0019). <see cref="TypeFamilies.BinaryResult"/> already
/// sinks the <em>result</em> of the mix to <c>int</c>; this pass supplies the
/// operand-side complement by wrapping the bool operand in
/// <c>(operand ? 1 : 0)</c>.
///
/// <para>Doing it as an IR rewrite (rather than a printer special-case) lets the
/// existing numeric machinery finish the job: the wrapped operand is a normal
/// <c>int</c> <see cref="Conditional"/>, so the printer's mixed-sign reconciliation
/// supplies the <c>(uint)</c> reinterpret for a <c>uint</c>/<c>nuint</c> sibling
/// (<c>(uint)(a ? 1 : 0) | c</c>, not the CS0266 <c>int | uint</c>), and a nested
/// conditional condition parenthesizes itself (<c>((x ? y : z) ? 1 : 0)</c>). This
/// is the same shape csc already emits when the bool was an explicit
/// <c>cond ? 1 : 0</c>, so it round-trips identically.</para>
///
/// <para>A genuine <c>bool &amp; bool</c> (a logical/bitwise bool op) is untouched:
/// the rewrite requires exactly one bool operand and an integer sibling.</para>
/// </summary>
public sealed class BitwiseBoolOperandPass : IIrPass
{
    public string Name => "bitwise-bool-operand";

    public void Run(IrFunction function, PassContext context)
    {
        var matches = function.Descendants
            .OfType<Binary>()
            .Where(IsBitwiseBoolIntegerMix)
            .ToList();

        var intType = TypeRef.CoreLib("System", "Int32");
        // Process deepest-first. Descendants is pre-order (ancestor before
        // descendant), so reversing guarantees every nested match is materialized
        // on the live tree before an enclosing match clones-and-replaces the
        // subtree that contains it — otherwise the outer rewrite would clone the
        // still-bool inner mix and the stale inner match would rewrite the
        // discarded original (leaving a `bool & int` CS0019 in the live tree).
        matches.Reverse();
        foreach (var binary in matches)
        {
            // Exactly one operand is bool (IsBitwiseBoolIntegerMix guarantees it);
            // wrap that one. Clone it so the new conditional owns a detached copy
            // before the in-place replace swaps it out of the binary.
            var boolOperand = TypeFamilies.IsBoolean(binary.Left.ResultType) ? binary.Left : binary.Right;
            var conditional = new Conditional(
                (IrExpression)boolOperand.Clone(),
                new Constant(1, intType),
                new Constant(0, intType))
            {
                MergedType = intType,
            };
            context.Stepper.StepOver("materialize bool operand of bitwise op to (cond ? 1 : 0)", boolOperand);
            boolOperand.ReplaceWith(conditional);
        }
    }

    /// <summary>A bitwise <c>&amp;</c>/<c>|</c>/<c>^</c> with exactly one <c>bool</c> operand and an integer sibling — the CS0019 <c>bool &amp; int</c> mix.</summary>
    static bool IsBitwiseBoolIntegerMix(Binary binary)
    {
        if (binary.Kind is not (BinaryKind.And or BinaryKind.Or or BinaryKind.Xor))
            return false;
        bool leftBool = TypeFamilies.IsBoolean(binary.Left.ResultType);
        bool rightBool = TypeFamilies.IsBoolean(binary.Right.ResultType);
        if (leftBool == rightBool)
            return false;
        // The non-bool sibling must be a genuine integer. IsInteger(bool) is true
        // (bool is stack family I4), so the bool side is excluded by leftBool/
        // rightBool above and only the integer sibling is tested here.
        return leftBool
            ? TypeFamilies.IsInteger(binary.Right.ResultType)
            : TypeFamilies.IsInteger(binary.Left.ResultType);
    }
}
