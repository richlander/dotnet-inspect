using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

// Accepted reference targets need not coincide with a conditional's natural type.
internal readonly record struct ReferenceAssignmentTargets(
    bool AnyReference, ImmutableHashSet<TypeRef> Types)
{
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");
    static readonly ReferenceAssignmentTargets Any = new(true, []);

    internal bool Contains(TypeRef target, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => CoercionRendering.IsProvenReference(target, shapes)
            && (AnyReference || Types.Contains(target));

    internal static bool CanAssignNullLiteralTo(
        IrExpression value,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => value is Constant { Value: null } && Any.Contains(target, shapes);

    internal static bool CanAssignStorageTo(
        IrExpression value,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlySet<ReferenceWidening>? provenWidenings = null)
    {
        if (CanAssignNullLiteralTo(value, target, shapes))
            return true;
        if (value is Conditional conditional)
            return conditional.CanAssignReferenceArmsTo(target, shapes);
        if (value is Coalesce)
            return ForType(value.AssignmentType, shapes, provenWidenings).Contains(target, shapes);
        return ForType(value.AssignmentType, shapes, provenWidenings).Contains(target, shapes);
    }

    internal static ReferenceAssignmentTargets ForArms(
        Conditional conditional, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
    {
        var whenTrue = ForValue(conditional.WhenTrue, shapes);
        var whenFalse = ForValue(conditional.WhenFalse, shapes);
        if (whenTrue.AnyReference)
            return whenFalse;
        if (whenFalse.AnyReference)
            return whenTrue;
        return new(false, whenTrue.Types.Intersect(whenFalse.Types));
    }

    static ReferenceAssignmentTargets ForValue(
        IrExpression value, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
    {
        if (value is Constant { Value: null })
            return Any;
        if (value is Conditional conditional)
        {
            var arms = conditional.ReferenceAssignments;
            return arms.AnyReference
                ? arms
                : new(false, arms.Types.Union(ForType(conditional.ResultType, shapes).Types));
        }
        return ForType(value.AssignmentType, shapes);
    }

    static ReferenceAssignmentTargets ForType(
        TypeRef? type,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlySet<ReferenceWidening>? provenWidenings = null)
    {
        if (type is null || !CoercionRendering.IsReferenceLike(type, shapes))
            return new(false, []);
        var targets = ImmutableHashSet.Create(type, ObjectType);
        if (provenWidenings is { Count: > 0 })
        {
            // Importer-proven supertypes of this exact type: the join merge
            // established the conversion while metadata was live.
            foreach (var widening in provenWidenings)
            {
                if (widening.From.Equals(type))
                    targets = targets.Add(widening.To);
            }
        }
        return new(false, targets);
    }
}
