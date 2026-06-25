using System.Linq;
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

        // Replacing the whole body with a marker is only honest when the body *is*
        // the narrow compiler handoff scaffold. A real kickoff carries no user
        // logic — it just constructs the state machine, copies `this`/parameters
        // into its hoisted capture fields, and returns it. If any other statement
        // is present (e.g. a side-effecting call before the handoff), acknowledging
        // would silently drop it (#1362); decline and leave the lowered residual.
        if (!IsNarrowHandoffShape(function, handoff))
            return false;

        stateMachine = IteratorShapes.MetadataName(handoff.Constructor.DeclaringType);
        sourceOffset = handoff.SourceOffset;
        return true;
    }

    // The kickoff body must consist solely of the handoff scaffold:
    //   * the state-machine construction (returned directly or stored to a slot/local),
    //   * pure capture stores into the state-machine instance, and
    //   * a single return of the constructed instance.
    // Any foreign statement — or a side effect hidden in a capture-store subtree —
    // makes whole-body replacement lossy, so the acknowledgment declines.
    static bool IsNarrowHandoffShape(IrFunction function, NewObject handoff)
    {
        // The handoff itself must be the bare `new <M>d__N(state)` construction:
        // its constructor arguments are compiler-supplied (the initial state, -2),
        // never user side effects. A side-effecting argument would be dropped by
        // whole-body replacement just like a leading statement (#1362).
        if (!handoff.Arguments.All(IsPureLoad))
            return false;

        var returns = 0;
        foreach (var statement in EnumerateKickoffStatements(function.Body))
        {
            switch (statement)
            {
                case Return ret:
                    returns++;
                    if (!IsReturnedHandoff(ret.Value, handoff))
                        return false;
                    break;
                case StoreStackSlot store when ReferenceEquals(store.Value, handoff):
                    break;
                case StoreLocal store when ReferenceEquals(store.Value, handoff):
                    break;
                case StoreField capture when IsPureLoad(capture.Instance) && IsPureLoad(capture.Value):
                    break;
                default:
                    return false;
            }
        }

        return returns == 1;
    }

    static bool IsReturnedHandoff(IrExpression? value, NewObject handoff)
    {
        // `return new <M>d__0(-2);` directly, or `return slotOrLocal;` after the
        // construction was stored above, or the object-initializer fold of the
        // construction plus its hoisted-parameter capture stores
        // (`return new <M>d__0(-2) { <>3__n = n };`). Capture member values must be
        // pure copies; a side effect there is not a real compiler handoff.
        if (ReferenceEquals(value, handoff) || IsPureLoad(value))
            return true;
        if (value is ObjectInitializerExpression initializer)
            return ReferenceEquals(initializer.Creation, handoff)
                && initializer.Children.Skip(1).Cast<IrExpression>().All(IsPureLoad);
        return false;
    }

    static IEnumerable<IrNode> EnumerateKickoffStatements(BlockContainer body)
    {
        foreach (var child in body.Children)
        {
            if (child is Block block)
            {
                foreach (var statement in block.Children)
                    yield return statement;
            }
            else
            {
                yield return child;
            }
        }
    }

    static bool IsPureLoad(IrExpression? expression) => expression switch
    {
        null => true,
        Constant => true,
        LoadArgument => true,
        LoadLocal => true,
        LoadStackSlot => true,
        LoadField field => IsPureLoad(field.Instance),
        _ => false,
    };
}
