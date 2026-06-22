namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Marks direct constructor calls that survived all constructor-raising passes as
/// explicit unsupported residuals. C# can spell only a constructor chain
/// (<c>: base(...)</c>/<c>: this(...)</c>) or an object creation expression; a
/// leftover <c>call instance .ctor</c> body statement has no faithful statement
/// spelling and must not leak as <c>base(...)</c> in arbitrary methods.
/// </summary>
public sealed class ConstructorCallDiagnosticsPass : IIrPass
{
    public string Name => "constructor-call-diagnostics";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var statement in function.Descendants.OfType<ExpressionStatement>().ToList())
        {
            if (statement.Expression is not Call { Callee: { Name: ".ctor", HasThis: true } } call)
                continue;
            if (IsLiftableConstructorChain(function, statement, call))
                continue;

            var marker = new UnsupportedNode(
                call.SourceOffset >= 0 ? call.SourceOffset : statement.SourceOffset,
                "call .ctor",
                $"direct constructor call to {call.Callee.DeclaringType.ToDisplayString()} is not representable as a C# statement");
            marker.InheritSourceOffset(call);
            var replacement = new ExpressionStatement(marker);
            replacement.InheritSourceOffset(statement);
            context.Stepper.StepOver($"mark unraised {call.Callee.DeclaringType.Name}..ctor call unsupported", statement);
            statement.ReplaceWith(replacement);
        }
    }

    static bool IsLiftableConstructorChain(IrFunction function, ExpressionStatement statement, Call call)
    {
        if (function.Name != ".ctor"
            || call.Arguments is not [LoadArgument { Index: 0 }, ..]
            || function.Body.Blocks is not [{ } entry, ..])
        {
            return false;
        }

        int chainIndex = ChildIndexOf(entry, statement);
        return chainIndex >= 0
            && entry.Children.Take(chainIndex).All(IsFieldInitializerStore);
    }

    static int ChildIndexOf(Block block, IrNode node)
    {
        for (int i = 0; i < block.Children.Count; i++)
            if (ReferenceEquals(block.Children[i], node))
                return i;
        return -1;
    }

    static bool IsFieldInitializerStore(IrNode node)
        => node is StoreField { HasInstance: true, Instance: LoadArgument { Index: 0 } } store
            && !ReferencesPlace(store.Value);

    static bool ReferencesPlace(IrExpression value)
    {
        foreach (var node in (IEnumerable<IrNode>)[value, .. value.Descendants])
        {
            if (node is LoadArgument or LoadLocal or LoadStackSlot or LoadLocalAddress or LoadArgumentAddress)
                return true;
        }
        return false;
    }
}
