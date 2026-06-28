using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Materializes <c>bool</c> operands that feed integer arithmetic/shift opcodes.
/// IL comparisons carry an <c>i4</c> 0/1 value, so <c>add</c>/<c>sub</c>/...
/// can legally consume them, but C# has no <c>int + bool</c> spelling.
/// </summary>
public sealed class ArithmeticBoolOperandPass : IIrPass
{
    public string Name => "arithmetic-bool-operand";

    public void Run(IrFunction function, PassContext context)
    {
        var matches = function.Descendants
            .OfType<Binary>()
            .Where(IsArithmeticBoolIntegerMix)
            .ToList();

        matches.Reverse();
        foreach (var binary in matches)
        {
            foreach (var boolOperand in BoolOperands(binary).ToList())
            {
                context.Stepper.StepOver("materialize bool operand of arithmetic op to (cond ? 1 : 0)", boolOperand);
                boolOperand.ReplaceWith(Materialize(boolOperand));
            }
        }
    }

    static IEnumerable<IrExpression> BoolOperands(Binary binary)
    {
        if (TypeFamilies.IsBoolean(binary.Left.ResultType))
            yield return binary.Left;
        if (TypeFamilies.IsBoolean(binary.Right.ResultType))
            yield return binary.Right;
    }

    static bool IsArithmeticBoolIntegerMix(Binary binary)
    {
        if (binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor)
            return false;

        bool leftBool = TypeFamilies.IsBoolean(binary.Left.ResultType);
        bool rightBool = TypeFamilies.IsBoolean(binary.Right.ResultType);
        if (!leftBool && !rightBool)
            return false;

        // `cgt; cgt; add` has no bool source operator; both comparisons are still
        // integer 0/1 operands and both must materialize.
        if (leftBool && rightBool)
            return true;
        return leftBool
            ? TypeFamilies.IsInteger(binary.Right.ResultType)
            : TypeFamilies.IsInteger(binary.Left.ResultType);
    }

    static Conditional Materialize(IrExpression boolOperand)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        return new Conditional(
            (IrExpression)boolOperand.Clone(),
            new Constant(1, intType),
            new Constant(0, intType))
        {
            MergedType = intType,
        };
    }
}
