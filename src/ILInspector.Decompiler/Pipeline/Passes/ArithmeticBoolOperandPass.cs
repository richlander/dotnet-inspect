using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Materializes <c>bool</c> operands that feed integer arithmetic/shift opcodes.
/// IL comparisons carry an <c>i4</c> 0/1 value, so <c>add</c>/<c>sub</c>/...
/// can legally consume them, but C# has no <c>int + bool</c> spelling.
/// When every store into one scoped slot is such arithmetic and every load
/// returns to integer arithmetic, the slot carries the arithmetic's I4 result,
/// not Boolean identity; preserve that result before materializing operands.
/// </summary>
public sealed class ArithmeticBoolOperandPass : IIrPass
{
    public string Name => "arithmetic-bool-operand";

    public void Run(IrFunction function, PassContext context)
    {
        PreserveIntegerSlotIdentity(function, context.Stepper);

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

    static void PreserveIntegerSlotIdentity(IrFunction function, Stepper stepper)
    {
        IEnumerable<IrNode> scopes = new IrNode[] { function.Body }
            .Concat(function.Descendants.OfType<Lambda>().Select(static lambda => (IrNode)lambda.Body))
            .Concat(function.Descendants.OfType<LocalFunctionStatement>().Select(static local => (IrNode)local.Body));
        var intType = TypeRef.CoreLib("System", "Int32");
        foreach (var scope in scopes)
        {
            var nodes = CoercionSinks.ScopeNodes(scope).ToList();
            var loads = nodes.OfType<LoadStackSlot>().ToLookup(static load => load.Slot);
            foreach (var stores in nodes.OfType<StoreStackSlot>().GroupBy(static store => store.Slot))
            {
                var slotLoads = loads[stores.Key].ToList();
                if (slotLoads.Count == 0
                    || !stores.All(static store =>
                        store.Value is Binary binary
                        && IsArithmeticBoolIntegerMix(binary)
                        && HasBooleanOrInt32Operands(binary))
                    || !slotLoads.All(static load =>
                        TypeFamilies.IsBoolean(load.Type)
                        && load.Parent is Binary binary
                        && IsArithmeticBoolIntegerMix(binary)
                        && HasBooleanOrInt32Operands(binary)))
                {
                    continue;
                }

                stepper.StepOver($"preserve integer identity of arithmetic stack slot {stores.Key}", stores.First());
                foreach (var load in slotLoads)
                {
                    var replacement = new LoadStackSlot(load.Slot, intType);
                    replacement.InheritSourceOffset(load);
                    load.ReplaceWith(replacement);
                }
            }
        }
    }

    static bool HasBooleanOrInt32Operands(Binary binary)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        return IsBooleanOrInt32(binary.Left.ResultType)
            && IsBooleanOrInt32(binary.Right.ResultType);

        bool IsBooleanOrInt32(TypeRef? type)
            => TypeFamilies.IsBoolean(type) || intType.Equals(type);
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
