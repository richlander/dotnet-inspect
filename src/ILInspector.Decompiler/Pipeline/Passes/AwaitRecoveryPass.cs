namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers C# <c>await</c> expressions from the runtime-async (async v2)
/// lowering. With runtime async there is no state machine: the compiler lowers
/// <c>await x</c> to a direct call to
/// <c>System.Runtime.CompilerServices.AsyncHelpers.Await</c> (generic for a
/// value-producing await, non-generic for <c>await</c> of a non-generic Task),
/// so recovery is a call-site rewrite — each matching call becomes an
/// <see cref="AwaitExpression"/> over its single awaited operand.
///
/// Runs before <see cref="ExpressionInliningPass"/> so the await is in place as
/// a non-pure barrier before any forward-substitution: the inliner never treats
/// an <see cref="AwaitExpression"/> as movable, so it cannot reorder awaits or
/// move other effects across one.
/// </summary>
public sealed class AwaitRecoveryPass : IIrPass
{
    public string Name => "await-recovery";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (!MemberIdentityFacts.IsAsyncHelpersAwait(call))
                continue;

            var operand = (IrExpression)call.DetachChildren()[0];
            var await = new AwaitExpression(operand, call.Callee.ReturnType);
            await.InheritSourceOffset(call);
            context.Stepper.StepOver("recover await from AsyncHelpers.Await call", call);
            call.ReplaceWith(await);
        }
    }

}
