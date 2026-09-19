namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers a one-case switch expression from an equality-selected private
/// result local before return sinking dissolves its convergence point.
/// </summary>
public sealed class SingleCaseValueSwitchPass : IIrPass
{
    public string Name => "single-case-value-switch";

    public void Run(IrFunction function, PassContext context)
    {
        var branchTargets = ReferenceOwnership.CollectBranchTargets(function);
        foreach (var block in function.DescendantsOutsideNestedFunctions.OfType<Block>().ToList())
        {
            for (int i = 0; i + 1 < block.Children.Count; i++)
            {
                if (block.Children[i] is not IfStatement
                    {
                        Then.Children: [StoreLocal whenEqual],
                        Else.Children: [StoreLocal otherwise],
                    } selection
                    || block.Children[i + 1] is not Return { Value: LoadLocal result } ret
                    || whenEqual.Index != otherwise.Index
                    || whenEqual.Index != result.Index
                    || !whenEqual.Type.Equals(otherwise.Type)
                    || !whenEqual.Type.Equals(result.Type)
                    || !whenEqual.Type.Equals(whenEqual.Value.ResultType)
                    || !whenEqual.Type.Equals(otherwise.Value.ResultType)
                    // The current switch-arm spelling can infer a common unboxed type.
                    || whenEqual.Value is Box || otherwise.Value is Box
                    || !TryLabel(function, selection.Condition, out var value, out int label)
                    || IrFunction.LocalSlotReferencesInScope(function, result.Index)
                        .Any(reference => !ReferenceEquals(reference, whenEqual)
                            && !ReferenceEquals(reference, otherwise)
                            && !ReferenceEquals(reference, result))
                    || branchTargets.Contains(selection.Then.StartOffset)
                    || branchTargets.Contains(selection.Else.StartOffset)
                    || ReferenceOwnership.RewriteWouldInvalidateLabels(function, [], [selection, ret]))
                {
                    continue;
                }

                context.Stepper.StepOver("recover single-case value switch at return join", selection);
                var expression = new SwitchExpression((IrExpression)value.Clone(),
                [
                    new SwitchExpressionArm([label], isDefault: false, (IrExpression)whenEqual.Value.Clone()),
                    new SwitchExpressionArm([], isDefault: true, (IrExpression)otherwise.Value.Clone()),
                ]);
                expression.InheritSourceOffset(selection);
                var replacement = new Return(expression);
                replacement.InheritSourceOffset(ret);
                ret.Detach();
                selection.ReplaceWith(replacement);
                function.MarkLocalEliminated(result.Index);
            }
        }
    }

    static bool TryLabel(IrFunction function, IrExpression condition, out IrExpression value, out int label)
    {
        value = null!;
        label = 0;
        switch (condition)
        {
            case Comparison { Kind: ComparisonKind.Equal, Right: Constant { Value: int right } } comparison:
                value = comparison.Left;
                label = right;
                break;
            case Comparison { Kind: ComparisonKind.Equal, Left: Constant { Value: int left } } comparison:
                value = comparison.Right;
                label = left;
                break;
            case LogicalNot not:
                value = not.Operand;
                break;
            default:
                return false;
        }

        if (value.ResultType is not { } type)
            return false;
        if (CoercionRendering.IsEnum(type, function.TypeShapes))
        {
            if (!function.EnumUnderlyingTypes.TryGetValue(CoercionRendering.NamedDefinition(type), out type))
                return false;
        }
        return type is
            {
                Kind: TypeRefKind.Definition,
                Assembly: TypeRef.CoreLibrary,
                Namespace: "System",
                Name: "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32",
            }
            && CSharpConversionRules.ConstantFits(label, type);
    }
}
