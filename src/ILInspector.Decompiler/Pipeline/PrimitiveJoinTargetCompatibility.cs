using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal interface IPrimitiveJoin
{
    IReadOnlyList<IrExpression> CompatibilityArms { get; }
    IReadOnlyList<IrExpression> RenderedArms { get; }
    PrimitiveJoinTargetCompatibility PrimitiveTargets { get; set; }
}

/// <summary>
/// Pre-print testimony for integer-family join target compatibility and
/// target-aware arm rendering.
/// </summary>
internal sealed record PrimitiveJoinTargetCompatibility(
    ImmutableDictionary<TypeRef, TypeRef> WholeJoinTargets,
    ImmutableDictionary<TypeRef, TypeRef> RenderedArmTargets)
{
    internal static PrimitiveJoinTargetCompatibility Empty { get; } = new(
        ImmutableDictionary<TypeRef, TypeRef>.Empty,
        ImmutableDictionary<TypeRef, TypeRef>.Empty);

    static ImmutableArray<TypeRef> IntegerTargets { get; } =
    [
        TypeRef.CoreLib("System", "SByte"),
        TypeRef.CoreLib("System", "Byte"),
        TypeRef.CoreLib("System", "Int16"),
        TypeRef.CoreLib("System", "UInt16"),
        TypeRef.CoreLib("System", "Char"),
        TypeRef.CoreLib("System", "Int32"),
        TypeRef.CoreLib("System", "UInt32"),
        TypeRef.CoreLib("System", "Int64"),
        TypeRef.CoreLib("System", "UInt64"),
        TypeRef.CoreLib("System", "IntPtr"),
        TypeRef.CoreLib("System", "UIntPtr"),
    ];

    internal static PrimitiveJoinTargetCompatibility Create(
        IrExpression expression,
        IReadOnlyList<IrExpression> compatibilityArms,
        IReadOnlyList<IrExpression> renderedArms,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
    {
        if (CSharpExpressionType.Effective(expression) is not { } source
            || !TypeFamilies.IsIntegerLike(source))
        {
            return Empty;
        }

        var wholeJoinTargets = ImmutableDictionary.CreateBuilder<TypeRef, TypeRef>();
        var renderedArmTargets = ImmutableDictionary.CreateBuilder<TypeRef, TypeRef>();
        foreach (var target in IntegerTargets)
        {
            if (!CoercionRendering.CanSpellSlotCoercion(
                source,
                target,
                shapes,
                enumUnderlyingTypes))
            {
                continue;
            }
            if (compatibilityArms.All(arm => CanRenderArm(
                arm,
                target,
                source,
                shapes,
                enumUnderlyingTypes)))
            {
                wholeJoinTargets.Add(target, source);
            }
            if (!source.Equals(target)
                && renderedArms.All(arm => CanRenderArm(
                arm,
                target,
                source,
                shapes,
                enumUnderlyingTypes)))
            {
                renderedArmTargets.Add(target, source);
            }
        }
        return new(wholeJoinTargets.ToImmutable(), renderedArmTargets.ToImmutable());
    }

    internal static bool CanCoerceArm(
        TypeRef armType,
        TypeRef target,
        TypeRef coercionSourceType,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => TypeFamilies.IsIntegerLike(armType)
            && TypeFamilies.IsIntegerLike(target)
            && CoercionRendering.CanSpellSlotCoercion(
                armType,
                target,
                shapes,
                enumUnderlyingTypes)
            && CSharpConversionRules.SameNumericSlotWidth(coercionSourceType, target);

    static bool CanRenderArm(
        IrExpression arm,
        TypeRef target,
        TypeRef coercionSourceType,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => arm is Constant { Value: int or long } or Coerce
            || (CSharpExpressionType.Effective(arm) is { } armType
                && (CoercionRendering.CanSpellBoolToInteger(armType, target)
                    || (TypeFamilies.IsIntegerLike(armType)
                        && (!CSharpConversionRules.NeedsNumericCast(armType, target)
                            || CanCoerceArm(
                                armType,
                                target,
                                coercionSourceType,
                                shapes,
                                enumUnderlyingTypes)))));
}

internal static class PrimitiveJoinTargetBinding
{
    static readonly IReadOnlyDictionary<TypeRef, TypeShape> EmptyShapes =
        ImmutableDictionary<TypeRef, TypeShape>.Empty;
    static readonly IReadOnlyDictionary<TypeRef, TypeRef> EmptyEnumUnderlyingTypes =
        ImmutableDictionary<TypeRef, TypeRef>.Empty;

    internal static void BindPrimitiveTargets(
        this IPrimitiveJoin join,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => join.PrimitiveTargets = PrimitiveJoinTargetCompatibility.Create(
            (IrExpression)join,
            join.CompatibilityArms,
            join.RenderedArms,
            shapes,
            enumUnderlyingTypes);

    internal static void BindInitialPrimitiveTargets(this IPrimitiveJoin join)
        => join.BindPrimitiveTargets(EmptyShapes, EmptyEnumUnderlyingTypes);

    internal static bool CanRenderPrimitiveJoinAt(
        this IrExpression expression,
        TypeRef target)
        => expression is IPrimitiveJoin join
            && join.PrimitiveTargets.WholeJoinTargets.ContainsKey(target);

    internal static TypeRef? PrimitiveJoinArmSource(
        this IrExpression expression,
        TypeRef target)
        => expression is IPrimitiveJoin join
            && join.PrimitiveTargets.RenderedArmTargets.TryGetValue(target, out var source)
                ? source
                : null;
}
