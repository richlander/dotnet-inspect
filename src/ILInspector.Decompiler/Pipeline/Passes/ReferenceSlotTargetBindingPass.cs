namespace ILInspector.Decompiler.Pipeline;

public sealed class ReferenceSlotTargetBindingPass : IIrPass
{
    public string Name => "reference-slot-target-binding";

    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var coalesce in function.Descendants.OfType<Coalesce>().Reverse())
            coalesce.BindAssignmentType(function.TypeShapes);
        WitnessCarrierCoalesces(function, context);
        foreach (var conditional in function.Descendants.OfType<Conditional>().Reverse())
            conditional.BindReferenceAssignments(function.TypeShapes);
    }

    /// <summary>
    /// A coalesce with no assignment type of its own may still target the one
    /// reference type every load testifies when each operand is either already
    /// that type or the importer proved its widening while metadata was live.
    /// Issue the one reference-conversion witness C# needs on the left operand:
    /// <c>(Target)left ?? right</c>. Nominal <c>object</c> retains its ordinary
    /// reference-conversion rule. Nested bodies bind inside their own pipeline
    /// run.
    /// </summary>
    static void WitnessCarrierCoalesces(IrFunction function, PassContext context)
    {
        var slotTypes = CoercionSinks.TestifiedSlotTypes(
            function.Body, function.Signature.ReturnType, function.TypeShapes);
        var blockedNamedTargets = SlotMaterializationPass.Analyze(function)
            .Where(decision =>
                ReferenceEquals(decision.Scope, function)
                && decision.Type is { } type
                && !type.Equals(ObjectType)
                && decision.Vetoes.HasFlag(
                    SlotMaterializationVeto.OutsideCoercionDomain)
                && decision.Vetoes.HasFlag(
                    SlotMaterializationVeto.UnrenderableStoreType))
            .ToDictionary(
                static decision => decision.Slot,
                static decision => decision.Type!);
        foreach (var store in CoercionSinks.ScopeNodes(function.Body).OfType<StoreStackSlot>().ToArray())
        {
            if (store.Value is not Coalesce { AssignmentType: null } coalesce
                || !slotTypes.TryGetValue(store.Slot, out var slotType)
                || !CoercionRendering.IsProvenReference(slotType, function.TypeShapes)
                || !slotType.Equals(ObjectType)
                    && (!blockedNamedTargets.TryGetValue(
                            store.Slot,
                            out var blockedTarget)
                        || !blockedTarget.Equals(slotType))
                || coalesce.Left is Constant { Value: null } or Coerce
                || !CanAssignArm(function, coalesce.Left, slotType)
                || !CanAssignArm(function, coalesce.Right, slotType))
            {
                continue;
            }
            context.Stepper.StepOver("witness slot-target coalesce left operand", coalesce);
            var left = coalesce.Left;
            var witness = new Coerce(slotType, (IrExpression)left.Clone(), CoercionKind.ReferenceWitness);
            witness.InheritSourceOffset(left);
            left.ReplaceWith(witness);
            coalesce.BindReferenceAssignmentType(slotType);
        }
    }

    static bool CanAssignArm(
        IrFunction function,
        IrExpression arm,
        TypeRef target)
        => arm.AssignmentType is { } source
            && CoercionRendering.IsProvenReference(
                source,
                function.TypeShapes)
            && (target.Equals(ObjectType)
                || source.Equals(target)
                || function.ProvenReferenceWidenings.Contains(
                    new ReferenceWidening(source, target)));
}
