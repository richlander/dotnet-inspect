using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's bool→int normalization idiom back into a conditional.
/// Converting a <c>bool</c> to an <c>int</c> (a <c>[LibraryImport]</c> stub
/// marshalling a <c>bool</c> parameter to a 4-byte native int, among others)
/// lowers to <c>ldarg b; ldc.i4.0; cgt.un</c> — an unsigned "greater than zero"
/// that yields the strict 0/1 form of the bool:
/// <code>
///   int V_0 = bToUpper > false;   // cgt.un(bToUpper, 0)
/// </code>
/// Rendered literally that is <c>CS0019</c> (<c>&gt;</c> is not defined on two
/// bools) and the method never binds. Recognized, it is exactly <c>bToUpper ? 1
/// : 0</c> — the normalized int the idiom computes.
///
/// <para>The discriminator is the left operand's type: <c>cgt.un(x, 0)</c> on a
/// non-bool <c>x</c> is a genuine unsigned <c>x != 0</c> test (a pointer or
/// unsigned-int null check) and is left alone. Only a <c>bool</c> left operand
/// makes the comparison a normalization, so the rewrite keys on it.</para>
/// </summary>
public sealed class BoolToIntNormalizationPass : IIrPass
{
    public string Name => "bool-to-int-normalization";

    public void Run(IrFunction function, PassContext context)
    {
        var matches = function.Descendants
            .OfType<Comparison>()
            .Where(IsBoolToIntNormalization)
            .ToList();

        var intType = TypeRef.CoreLib("System", "Int32");
        foreach (var comparison in matches)
        {
            var operand = (IrExpression)comparison.DetachChildren()[0];
            var conditional = new Conditional(
                operand,
                new Constant(1, intType),
                new Constant(0, intType));
            context.Stepper.StepOver("raise bool→int normalization to conditional", comparison);
            comparison.ReplaceWith(conditional);
        }
    }

    /// <summary>An unsigned <c>cgt.un(boolExpr, 0/false)</c> — the bool→int normalization idiom.</summary>
    static bool IsBoolToIntNormalization(Comparison comparison)
        => comparison is { Kind: ComparisonKind.GreaterThan, IsUnsigned: true }
            && comparison.Left.ResultType is { Namespace: "System", Name: "Boolean" }
            && comparison.Right is Constant { Value: false or 0 or 0L };
}
