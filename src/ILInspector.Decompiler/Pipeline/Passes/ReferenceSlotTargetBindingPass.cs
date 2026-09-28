namespace ILInspector.Decompiler.Pipeline;

public sealed class ReferenceSlotTargetBindingPass : IIrPass
{
    public string Name => "reference-slot-target-binding";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var coalesce in function.Descendants.OfType<Coalesce>().Reverse())
            coalesce.BindAssignmentType(function.TypeShapes);
        foreach (var conditional in function.Descendants.OfType<Conditional>().Reverse())
            conditional.BindReferenceAssignments(function.TypeShapes);
    }
}
