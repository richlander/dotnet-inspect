using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recognizes a compiler-generated iterator kickoff — a method returning
/// <c>IEnumerable</c>/<c>IEnumerator</c> whose body merely hands off to a
/// <c>new &lt;Method&gt;d__N(...)</c> state machine — and replaces the
/// misleading handoff (<c>return new &lt;X&gt;d__0(-2);</c>, which reads like
/// ordinary user code) with an honest <see cref="UnsupportedNode"/> marker.
///
/// <para>The <c>yield</c> body lives in the state machine's <c>MoveNext</c> and
/// is not yet reconstructed; this pass keeps that gap visible — fidelity drops to
/// <see cref="DecompilationFidelity.Partial"/> (DEC0004) — instead of emitting a
/// plausible-but-meaningless stub. It does not raise the idiom (a Diagnostic
/// native pass): the kickoff carries no user logic to preserve, so the whole
/// body is replaced.</para>
///
/// <para>The kickoff lives on the user's type; the state machine's own
/// <c>MoveNext</c>/<c>GetEnumerator</c> also construct and return a
/// <c>&lt;X&gt;d__</c> — they are excluded by requiring the declaring type to not
/// itself be a state machine.</para>
/// </summary>
public sealed class IteratorAcknowledgmentPass : IIrPass
{
    public string Name => "iterator-acknowledgment";

    public void Run(IrFunction function, PassContext context)
    {
        if (!IsIteratorKickoff(function, out var stateMachine, out var sourceOffset))
            return;

        context.Stepper.StepOver($"acknowledge iterator kickoff '{stateMachine}' (yield body not reconstructed)");

        function.Body.DetachChildren();
        var marker = new UnsupportedNode(0, "iterator",
            $"compiler-generated iterator state machine '{stateMachine}'; yield body (MoveNext) not reconstructed");
        // Carry the handoff's provenance so the state-machine allocation fact
        // (classified at import) still anchors onto the marker in the C# view.
        marker.SetSourceOffset(sourceOffset);
        var statement = new ExpressionStatement(marker);
        statement.SetSourceOffset(sourceOffset);
        var block = new Block(0);
        block.Add(statement);
        function.Body.Add(block);
    }

    static bool IsIteratorKickoff(IrFunction function, out string stateMachine, out int sourceOffset)
    {
        stateMachine = "";
        sourceOffset = -1;

        if (!IteratorShapes.TryGetKickoff(function, out var handoff))
            return false;

        stateMachine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
        sourceOffset = handoff.SourceOffset;
        return true;
    }
}
