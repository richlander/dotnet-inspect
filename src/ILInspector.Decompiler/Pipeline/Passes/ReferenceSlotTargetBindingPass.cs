namespace ILInspector.Decompiler.Pipeline;

public sealed class ReferenceSlotTargetBindingPass : IIrPass
{
    public string Name => "reference-slot-target-binding";

    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var coalesce in function.Descendants.OfType<Coalesce>().Reverse())
            coalesce.BindAssignmentType(function.TypeShapes);
        WitnessObjectCarrierCoalesces(function, context);
        foreach (var conditional in function.Descendants.OfType<Conditional>().Reverse())
            conditional.BindReferenceAssignments(function.TypeShapes);
    }

    /// <summary>
    /// A coalesce of two disjoint proven references stored to a slot whose
    /// every load testifies <c>object</c> has no assignment type of its own
    /// (neither operand converts to the other), yet the C# the IL encodes is
    /// legal with one reference-conversion witness: <c>(object)left ?? right</c>.
    /// Issue that witness on the left operand so the coalesce acquires the
    /// <c>object</c> assignment type the bounded reference storage relation
    /// already accepts (value-typed-emission.md, "Bounded reference storage
    /// testimony"). Nested bodies bind inside their own pipeline run.
    /// </summary>
    static void WitnessObjectCarrierCoalesces(IrFunction function, PassContext context)
    {
        var slotTypes = CoercionSinks.TestifiedSlotTypes(
            function.Body, function.Signature.ReturnType, function.TypeShapes);
        foreach (var store in CoercionSinks.ScopeNodes(function.Body).OfType<StoreStackSlot>().ToArray())
        {
            if (store.Value is not Coalesce { AssignmentType: null } coalesce
                || !slotTypes.TryGetValue(store.Slot, out var slotType)
                || !slotType.Equals(ObjectType)
                || coalesce.Left is Constant { Value: null } or Coerce
                || !CoercionRendering.IsProvenReference(coalesce.Left.AssignmentType, function.TypeShapes)
                || !CoercionRendering.IsProvenReference(coalesce.Right.AssignmentType, function.TypeShapes))
            {
                continue;
            }
            context.Stepper.StepOver("witness object-carrier coalesce left operand", coalesce);
            var left = coalesce.Left;
            var witness = new Coerce(ObjectType, (IrExpression)left.Clone(), CoercionKind.ReferenceWitness);
            witness.InheritSourceOffset(left);
            left.ReplaceWith(witness);
            coalesce.BindAssignmentType(function.TypeShapes);
        }
    }
}
