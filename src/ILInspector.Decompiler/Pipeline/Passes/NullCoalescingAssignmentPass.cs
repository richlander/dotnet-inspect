namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's null-coalescing assignment lowering:
/// <code>
/// if (V is null) { V = fallback; }            // local
/// if (obj.field is null) { obj.field = fallback; }  // field
/// </code>
/// into <c>V ??= fallback</c> / <c>obj.field ??= fallback</c>. The field form is
/// scoped to a re-evaluable receiver (a local/argument/this, or none for a static
/// field), so collapsing the two member loads into one <c>??=</c> reorders
/// nothing. Property and indexer <c>??=</c> shapes carry accessor-call concerns
/// left to later slices.
/// </summary>
public sealed class NullCoalescingAssignmentPass : IIrPass
{
    public string Name => "null-coalescing-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var statement in function.Descendants.OfType<IfStatement>().ToList())
        {
            if (TryMatchLocal(statement) is { } local)
            {
                var value = (IrExpression)local.Store.DetachChildren()[0];
                var replacement = new NullCoalescingAssignment(local.Local.Index, local.Local.Type, value);
                context.Stepper.StepOver("raise local null check assignment to ??=", statement);
                statement.ReplaceWith(replacement);
                continue;
            }

            if (TryMatchField(statement) is { } field)
            {
                var children = field.Store.DetachChildren();
                var instance = field.Store.HasInstance ? (IrExpression)children[0] : null;
                var value = (IrExpression)children[^1];
                var replacement = new NullCoalescingFieldAssignment(field.Store.Field, instance, value);
                context.Stepper.StepOver("raise field null check assignment to ??=", statement);
                statement.ReplaceWith(replacement);
            }
        }
    }

    sealed record LocalMatch(LoadLocal Local, StoreLocal Store);

    static LocalMatch? TryMatchLocal(IfStatement statement)
    {
        if (statement.HasElse
            || statement.Then.Children is not [StoreLocal store]
            || NullTested(statement.Condition) is not LoadLocal local
            || store.Index != local.Index)
        {
            return null;
        }

        if (IsNonNullableNumeric(local.Type))
            return null;

        return new LocalMatch(local, store);
    }

    sealed record FieldMatch(StoreField Store);

    static FieldMatch? TryMatchField(IfStatement statement)
    {
        if (statement.HasElse
            || statement.Then.Children is not [StoreField store]
            || NullTested(statement.Condition) is not LoadField loaded
            || !SameField(loaded.Field, store.Field)
            || !SameReceiver(loaded.Instance, store.Instance))
        {
            return null;
        }

        if (IsNonNullableNumeric(store.Field.Type))
            return null;

        return new FieldMatch(store);
    }

    static bool IsNonNullableNumeric(TypeRef type)
        => TypeFamilies.Of(type) is StackFamily.I4 or StackFamily.I8 or StackFamily.I or StackFamily.F;

    static bool SameField(FieldRef a, FieldRef b)
        => a.Name == b.Name && Equals(a.DeclaringType, b.DeclaringType);

    // The receiver is loaded once for the null test and once for the store; the
    // fold is only sound when re-evaluating it is free of side effects and yields
    // the same value — i.e. a static field (no receiver) or a plain
    // local/argument/this variable read (PlaceIdentity.SameVariable).
    static bool SameReceiver(IrExpression? a, IrExpression? b)
        => (a is null && b is null) || PlaceIdentity.SameVariable(a, b);

    static IrExpression? NullTested(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: var operand } => operand,
        Comparison { Kind: ComparisonKind.Equal, Left: var left, Right: Constant { Value: null } } => left,
        Comparison { Kind: ComparisonKind.Equal, Left: Constant { Value: null }, Right: var right } => right,
        _ => null,
    };
}
