namespace ILInspector.Decompiler.Pipeline;

public sealed class ReferenceConditionalBindingPass : IIrPass
{
    public string Name => "reference-conditional-binding";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var conditional in function.Descendants.OfType<Conditional>().Reverse())
            conditional.BindReferenceAssignments(function.TypeShapes);
    }
}
