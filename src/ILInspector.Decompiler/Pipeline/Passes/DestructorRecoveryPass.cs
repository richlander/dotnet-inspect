namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers a C# destructor (<c>~T() { … }</c>) from the finalizer the compiler
/// emits for it. A destructor lowers to a <c>Finalize</c> override whose body is
/// <code>
/// try { BODY } finally { base.Finalize(); }
/// </code>
/// — the <c>base.Finalize()</c> call is implicit in source but mandatory in IL,
/// and C# forbids writing it (CS0250), so the literal rendering never compiles.
/// This pass strips the scaffold to <c>BODY</c> and marks the function a
/// destructor (<see cref="IrFunction.IsDestructor"/>); consumers render the header
/// as <c>~TypeName()</c>, where the base-finalizer call and the protecting
/// try/finally are re-emitted by the compiler. Recompiling the destructor restores
/// the original IL, so the recovery is opcode-exact.
///
/// <para>Runs after structuring so the EH region is a <see cref="TryFinally"/>.
/// Conservative: the method must be a parameterless instance <c>void Finalize</c>
/// whose body is exactly that try/finally with a finally of nothing but the base
/// call; anything else (a prologue before the try, extra finally work) keeps the
/// literal form.</para>
/// </summary>
public sealed class DestructorRecoveryPass : IIrPass
{
    public string Name => "destructor";

    public void Run(IrFunction function, PassContext context)
    {
        if (function.Name != "Finalize"
            || !function.Signature.HasThis
            || function.Signature.Parameters.Length != 0
            || !IsVoid(function.Signature.ReturnType))
        {
            return;
        }

        // The body is a single block whose first statement is the try/finally;
        // anything after it (the implicit void return) is trailing.
        if (function.Body.Blocks is not [{ Children: [TryFinally tryFinally, ..] } block])
            return;
        if (!IsBaseFinalizeOnly(tryFinally.FinallyBody))
            return;

        var tryBody = tryFinally.TryBody;
        var trailing = block.Children.Skip(tryFinally.ChildIndex + 1).ToList();

        tryBody.Detach();
        var lastBlock = (Block)tryBody.Children[^1];
        foreach (var statement in trailing)
        {
            statement.Detach();
            lastBlock.Add(statement);
        }

        context.Stepper.StepOver("recover finalizer try/finally as a destructor body", tryFinally);
        function.SetChild(0, tryBody);
        function.IsDestructor = true;
    }

    static bool IsVoid(TypeRef type) => type is { Namespace: "System", Name: "Void" };

    /// <summary>The finally body is nothing but the implicit <c>base.Finalize()</c> call.</summary>
    static bool IsBaseFinalizeOnly(BlockContainer finallyBody)
        => finallyBody.Blocks is [{ Children: [ExpressionStatement { Expression: Call call }] }]
            && call is { IsVirtual: false, Callee.Name: "Finalize", Callee.HasThis: true, Arguments: [LoadArgument { Index: 0 }] };
}
