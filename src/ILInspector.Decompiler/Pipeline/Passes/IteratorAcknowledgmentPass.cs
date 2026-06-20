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

        // MoveNext/GetEnumerator live on the <X>d__ type and also construct/return
        // the state machine; the kickoff lives on the user's type.
        if (IsStateMachineType(function.DeclaringType))
            return false;
        if (!ReturnsEnumerable(function.Signature.ReturnType))
            return false;

        var handoff = function.Descendants.OfType<NewObject>()
            .FirstOrDefault(n => IsStateMachineType(n.Constructor.DeclaringType));
        if (handoff is null)
            return false;

        stateMachine = MetadataName(handoff.Constructor.DeclaringType);
        sourceOffset = handoff.SourceOffset;
        return true;
    }

    static bool IsStateMachineType(TypeRef type)
        => MetadataName(type).Contains(">d__", StringComparison.Ordinal);

    static bool ReturnsEnumerable(TypeRef type)
    {
        var ns = Namespace(type);
        if (ns is not ("System.Collections" or "System.Collections.Generic"))
            return false;
        var name = MetadataName(type);
        return name.StartsWith("IEnumerable", StringComparison.Ordinal)
            || name.StartsWith("IEnumerator", StringComparison.Ordinal);
    }

    static string MetadataName(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType?.Name ?? "" : type.Name;

    static string Namespace(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType?.Namespace ?? "" : type.Namespace;
}
