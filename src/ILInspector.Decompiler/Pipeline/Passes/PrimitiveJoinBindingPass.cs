namespace ILInspector.Decompiler.Pipeline;

public sealed class PrimitiveJoinBindingPass : IIrPass
{
    public string Name => "primitive-join-binding";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var join in function.Descendants.OfType<IPrimitiveJoin>().Reverse())
        {
            join.BindPrimitiveTargets(
                function.TypeShapes,
                function.EnumUnderlyingTypes);
        }
    }
}
