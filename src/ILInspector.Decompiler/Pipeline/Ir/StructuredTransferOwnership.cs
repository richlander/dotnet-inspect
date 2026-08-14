namespace ILInspector.Decompiler.Pipeline;

internal static class StructuredTransferOwnership
{
    public static bool ContainsBreakTargetingOutside(IrNode root)
        => ContainsTargetingOutside(root, includeContinue: false, includeNestedFunctions: true);

    public static bool ContainsBreakOrContinueTargetingOutside(IrNode root)
        => ContainsTargetingOutside(root, includeContinue: true, includeNestedFunctions: false);

    static bool ContainsTargetingOutside(
        IrNode root,
        bool includeContinue,
        bool includeNestedFunctions)
    {
        var descendants = includeNestedFunctions
            ? root.Descendants
            : GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(root);
        foreach (var transfer in descendants
            .Where(node => node is Break || includeContinue && node is Continue))
        {
            for (var ancestor = transfer.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, root))
                    return true;
                if (transfer is Break && ancestor is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement or Switch)
                    break;
                if (transfer is Continue && ancestor is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement)
                    break;
            }
        }
        return false;
    }
}
