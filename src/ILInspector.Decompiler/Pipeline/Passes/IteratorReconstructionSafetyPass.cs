using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Final honesty gate for iterator bodies after post-reconstruction cleanup passes.
/// Some cleanup can expose an unsupported structured shape (for example an empty
/// branch where a conditional yield-break was dropped). Do not let such a body
/// claim Full fidelity; replace it with an explicit iterator residual marker.
/// </summary>
public sealed class IteratorReconstructionSafetyPass : IIrPass
{
    public string Name => "iterator-reconstruction-safety";

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Descendants.OfType<YieldReturn>().Any())
            return;
        if (!IteratorReconstructionValidation.HasUnsupportedStructuredShape(function.Body))
            return;

        context.Stepper.StepOver("decline unsafe reconstructed iterator shape");

        function.Body.DetachChildren();
        var marker = new UnsupportedNode(0, "iterator",
            "compiler-generated iterator state machine; reconstructed yield body failed structural validation");
        var statement = new ExpressionStatement(marker);
        var block = new Block(0);
        block.Add(statement);
        function.Body.Add(block);
    }
}
