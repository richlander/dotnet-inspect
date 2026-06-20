namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's null-coalescing assignment lowering:
/// <code>
/// if (target is null) { target = fallback; }
/// </code>
/// into <c>target ??= fallback</c>. Handles the local-variable, field, and
/// static-property forms whose then-branch is a single direct store. The tested
/// place and the assigned place must be the same re-evaluable location — the
/// receiver is compared structurally so the collapsed form re-loads it exactly
/// as the if-form did, keeping IL identical. Indexers and the temp-spilled
/// instance-auto-property shape (where csc lifts the value through a stack slot)
/// are left to a later slice.
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

            match.Target.Detach();
            match.Value.Detach();
            var replacement = new NullCoalescingAssignment(match.Target, match.Value);
            context.Stepper.StepOver("raise null check assignment to ??=", statement);
            statement.ReplaceWith(replacement);
        }
    }

    sealed record Match(IrExpression Target, IrExpression Value);

    static Match? TryMatch(IfStatement statement)
    {
        if (statement.HasElse
            || statement.Then.Children is not [IrNode store]
            || NullTestedTarget(statement.Condition) is not { } target)
        {
            return null;
        }

        if (TypeFamilies.Of(target.ResultType) is StackFamily.I4 or StackFamily.I8 or StackFamily.I or StackFamily.F)
            return null;

        return StoredValue(target, store) is { } value ? new Match(target, value) : null;
    }

    static IrExpression? NullTestedTarget(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: { } operand } when IsAssignablePlace(operand) => operand,
        Comparison { Kind: ComparisonKind.Equal, Left: { } left, Right: Constant { Value: null } } when IsAssignablePlace(left) => left,
        Comparison { Kind: ComparisonKind.Equal, Left: Constant { Value: null }, Right: { } right } when IsAssignablePlace(right) => right,
        _ => null,
    };

    static bool IsAssignablePlace(IrExpression expression) => expression switch
    {
        LoadLocal => true,
        LoadField => true,
        LoadProperty { IndexArguments.Count: 0 } => true,
        _ => false,
    };

    static IrExpression? StoredValue(IrExpression target, IrNode store) => (target, store) switch
    {
        (LoadLocal local, StoreLocal s) when s.Index == local.Index => s.Value,
        (LoadField field, StoreField s)
            when s.Field.Name == field.Field.Name
                && Equals(s.Field.DeclaringType, field.Field.DeclaringType)
                && SameReceiver(s.Instance, field.Instance) => s.Value,
        (LoadProperty property, StoreProperty s)
            when s.IndexArguments.Count == 0
                && s.PropertyName == property.PropertyName
                && Equals(s.Accessor.DeclaringType, property.Accessor.DeclaringType)
                && SameReceiver(s.HasInstance ? s.Instance : null, property.HasInstance ? property.Instance : null) => s.Value,
        _ => null,
    };

    static bool SameReceiver(IrExpression? left, IrExpression? right) => (left, right) switch
    {
        (null, null) => true,
        (LoadArgument a, LoadArgument b) => a.Index == b.Index,
        (LoadLocal a, LoadLocal b) => a.Index == b.Index,
        (LoadField a, LoadField b) => a.Field.Name == b.Field.Name
            && Equals(a.Field.DeclaringType, b.Field.DeclaringType)
            && SameReceiver(a.Instance, b.Instance),
        _ => false,
    };
}
