namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises an in-place struct constructor call to a whole-value assignment:
/// <c>ldloca s; call S::.ctor(args)</c> is the C# compiler's lowering of
/// <c>s = new S(args)</c> when <c>s</c> is a local, by-ref argument, or field of
/// struct type (the JIT initializes the struct in place rather than copying a
/// temporary). The IL spelling prints as the illegal <c>s..ctor(args);</c>
/// (CS0201, "is not a valid statement"), so this pass rewrites it back into the
/// <see cref="NewObject"/> assignment the printer already renders as
/// <c>s = new S(args);</c>.
///
/// The constructor fully owns the receiver address — it is the call's receiver
/// and nothing else in the tree — so replacing the in-place init with a
/// whole-value assignment is observationally identical. The this/base
/// constructor-chain case (receiver is <c>this</c>) is left to
/// <see cref="ConstructorChainPass"/> and the printer's base(...)/this(...)
/// spelling; this pass only matches the address of a nameable storage location.
/// </summary>
public sealed class StructConstructorPass : IIrPass
{
    public string Name => "struct-constructor";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var statement in function.Descendants.OfType<ExpressionStatement>().ToList())
        {
            if (statement.Parent is null)
                continue;  // detached by an earlier rewrite in this walk
            if (statement.Expression is not Call { Callee: { Name: ".ctor", HasThis: true } } call)
                continue;
            if (call.Arguments.Count == 0 || !IsRaisableTarget(call.Arguments[0]))
                continue;

            var children = call.DetachChildren().Cast<IrExpression>().ToList();
            var receiver = children[0];
            var value = new NewObject(call.Callee, children.Skip(1));
            statement.ReplaceWith(Assign(receiver, value));
        }
    }

    // Only the address of a nameable storage location is a safe target: the
    // address is a leaf load (local/argument) or a field load whose own
    // receiver travels with it, so the rewrite stays a local tree edit.
    static bool IsRaisableTarget(IrExpression receiver)
        => receiver is LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress;

    static IrNode Assign(IrExpression receiver, NewObject value) => receiver switch
    {
        LoadLocalAddress local => new StoreLocal(local.Index, local.Type, value),
        LoadArgumentAddress argument => new StoreArgument(argument.Index, argument.Name, argument.Type, value),
        LoadFieldAddress field => new StoreField(field.Field, DetachInstance(field), value),
        _ => throw new InvalidOperationException($"Unexpected struct-ctor target {receiver.Describe()}."),
    };

    // The field address has already left the call tree; lift its own receiver
    // out so the new StoreField can adopt it without a double parent.
    static IrExpression? DetachInstance(LoadFieldAddress field)
    {
        var instance = field.Instance;
        instance?.Detach();
        return instance;
    }
}
