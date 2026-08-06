namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Collapses a constructor-chain call's spilled argument temporaries into the
/// call so it lands as the body's first statement, where the printer lifts it
/// to a <c>: base(args)</c> / <c>: this(args)</c> signature initializer.
///
/// The compiler spills any base/this argument that carries control flow — the
/// ubiquitous <c>base(message ?? SR.Default)</c> exception shape — into a
/// temporary the general inliner then declines to fold: the chain receiver is
/// evaluated first, and <see cref="TypeRef"/> cannot yet prove it is a class
/// rather than a mutable byref struct, so it reads as an impure leaf the stored
/// value may not be reordered past. But the receiver of a constructor's own
/// base/this call is the object under construction — an immutable reference with
/// no observable evaluation effect — so moving an argument's computation past it
/// never reorders anything. This pass makes that one safe move
/// <see cref="ConstructorChainPass"/> set up: it inlines each single-use
/// argument temp stored in the run of statements immediately preceding the
/// chain call. Left in place the call prints as an invalid <c>base(temp);</c>
/// body statement (CS0175) that drops its argument on recompile.
/// </summary>
public sealed class ConstructorChainArgumentPass : IIrPass
{
    public string Name => "constructor-chain-argument";

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Signature.HasThis)
            return;

        // ConstructorChainPass has already canonicalized the receiver to `this`.
        if (FindChainCall(function) is not { } call
            || call.Parent is not ExpressionStatement statement
            || statement.Parent is not Block)
        {
            return;
        }

        var usage = SpilledReceiverFold.CountPlaces(function);
        var orderSensitiveArguments = SpilledReceiverFold.OrderSensitiveArguments(function);
        SpilledReceiverFold.TryFold(
            statement,
            call,
            usage,
            context,
            "inline spilled base/this constructor argument",
            orderSensitiveArguments: orderSensitiveArguments);
    }

    static Call? FindChainCall(IrFunction function)
    {
        foreach (var node in function.Descendants)
        {
            if (node is Call { Callee: { Name: ".ctor", HasThis: true } } call
                && call.Arguments is [LoadArgument { Index: 0 }, ..])
            {
                return call;
            }
        }
        return null;
    }
}
