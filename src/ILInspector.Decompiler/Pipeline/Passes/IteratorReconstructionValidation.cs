namespace ILInspector.Decompiler.Pipeline;

internal static class IteratorReconstructionValidation
{
    public static bool HasUnsupportedStructuredShape(BlockContainer body)
        => HasUnsupportedStructuredShape(body.Blocks.SelectMany(block => block.Children));

    public static bool HasUnsupportedStructuredShape(IEnumerable<IrNode> statements)
    {
        var list = statements.ToList();
        if (HasBrokenSwitchLikeYieldSuffix(list))
            return true;

        foreach (var statement in list)
        {
            switch (statement)
            {
                case IfStatement conditional:
                    if (conditional.Then.Children.Count == 0
                        || conditional.Else is { Children.Count: 0 })
                    {
                        return true;
                    }
                    if (HasUnsupportedStructuredShape(conditional.Then.Children)
                        || (conditional.Else is { } elseArm && HasUnsupportedStructuredShape(elseArm.Children)))
                    {
                        return true;
                    }
                    break;
                case WhileLoop loop:
                    if (HasUnsupportedStructuredShape(loop.Body.Children))
                        return true;
                    break;
                case ForLoop loop:
                    if (HasUnsupportedStructuredShape(loop.Body.Children))
                        return true;
                    break;
                case DoWhileLoop loop:
                    if (HasUnsupportedStructuredShape(loop.Body.Blocks.SelectMany(block => block.Children)))
                        return true;
                    break;
            }
        }

        return false;
    }

    static bool HasBrokenSwitchLikeYieldSuffix(IReadOnlyList<IrNode> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not IfStatement conditional)
                continue;
            if (!conditional.Then.Descendants.OfType<IfStatement>().Any()
                && conditional.Else?.Descendants.OfType<IfStatement>().Any() != true)
            {
                continue;
            }
            if (statements.Skip(i + 1).OfType<YieldReturn>().Count() > 1)
                return true;
        }
        return false;
    }
}
