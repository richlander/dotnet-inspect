namespace ILInspector.Decompiler.Pipeline;

public sealed class ReferenceCoalesceBindingPass : IIrPass
{
    public string Name => "reference-coalesce-binding";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var coalesce in function.Descendants.OfType<Coalesce>().Reverse())
            coalesce.BindAssignmentType(function.TypeShapes);

        var arguments = CoercionSinks.Enumerate(function)
            .Where(sink => sink.Value is Coalesce { AssignmentType: { } source }
                && sink.Value.Parent is Call or NewObject
                && CoercionRendering.IsProvenReference(source, function.TypeShapes)
                && !source.Equals(sink.Target)
                && sink.Target is
                {
                    Kind: TypeRefKind.Definition,
                    Assembly: TypeRef.CoreLibrary,
                    Namespace: "System",
                    Name: "Object",
                })
            .Reverse().ToArray();
        foreach (var (value, target, _) in arguments)
        {
            context.Stepper.StepOver("preserve reference-coalesce argument binding", value);
            var witness = new Coerce(target, (IrExpression)value.Clone(), CoercionKind.ReferenceWitness);
            witness.InheritSourceOffset(value);
            value.ReplaceWith(witness);
        }
    }
}
