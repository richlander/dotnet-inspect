namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the local-variable form of csc's null-coalescing assignment lowering:
/// <code>
/// if (V is null) { V = fallback; }
/// </code>
/// into <c>V ??= fallback</c>. Scoped to locals; field/property/indexer
/// <c>??=</c> shapes carry receiver-evaluation and setter concerns left to later
/// slices.
/// </summary>
public sealed class NullCoalescingAssignmentPass : IIrPass
{
    public string Name => "null-coalescing-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var statement in function.Descendants.OfType<IfStatement>().ToList())
        {
            if (TryMatch(statement) is not { } match)
                continue;

            var value = (IrExpression)match.Store.DetachChildren()[0];
            var replacement = new NullCoalescingAssignment(match.Local.Index, match.Local.Type, value);
            context.Stepper.StepOver("raise local null check assignment to ??=", statement);
            statement.ReplaceWith(replacement);
        }
    }

    sealed record Match(LoadLocal Local, StoreLocal Store);

    static Match? TryMatch(IfStatement statement)
    {
        if (statement.HasElse
            || statement.Then.Children is not [StoreLocal store]
            || NullTestedLocal(statement.Condition) is not { } local
            || store.Index != local.Index)
        {
            return null;
        }

        if (TypeFamilies.Of(local.Type) is StackFamily.I4 or StackFamily.I8 or StackFamily.I or StackFamily.F)
            return null;

        return new Match(local, store);
    }

    static LoadLocal? NullTestedLocal(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: LoadLocal local } => local,
        Comparison { Kind: ComparisonKind.Equal, Left: LoadLocal local, Right: Constant { Value: null } } => local,
        Comparison { Kind: ComparisonKind.Equal, Left: Constant { Value: null }, Right: LoadLocal local } => local,
        _ => null,
    };
}
