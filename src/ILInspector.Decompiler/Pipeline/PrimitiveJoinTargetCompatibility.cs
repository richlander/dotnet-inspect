using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal interface IPrimitiveJoin
{
    IReadOnlyList<IrExpression> CompatibilityArms { get; }
    IReadOnlyList<IrExpression> RenderedArms { get; }
    PrimitiveJoinTargetCompatibility PrimitiveTargets { get; set; }
}

/// <summary>
/// Pre-print testimony for join target compatibility: the integer-family
/// whole-join targets and target-aware arm sources, plus the two arm facts the
/// enum and <c>char</c> routes read — every rendered arm is integer-typed, and
/// every rendered arm is a <c>char</c> constant
/// (value-typed-emission.md, "Join target testimony").
/// </summary>
internal sealed record PrimitiveJoinTargetCompatibility(
    ImmutableDictionary<TypeRef, TypeRef> WholeJoinTargets,
    ImmutableDictionary<TypeRef, TypeRef> RenderedArmTargets,
    bool IntegerArms = false,
    bool CharConstantArms = false)
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
        bool integerArms = IntegerArmsOf(renderedArms);
        bool charConstantArms = CharConstantArmsOf(renderedArms);
        if (CSharpExpressionType.Effective(expression) is not { } source
            || !TypeFamilies.IsIntegerLike(source))
        {
            return integerArms || charConstantArms
                ? Empty with { IntegerArms = integerArms, CharConstantArms = charConstantArms }
                : Empty;
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
        return new(wholeJoinTargets.ToImmutable(), renderedArmTargets.ToImmutable(), integerArms, charConstantArms);
    }

    /// <summary>Every rendered arm carries an integer-family result type.</summary>
    internal static bool IntegerArmsOf(IReadOnlyList<IrExpression> renderedArms)
        => renderedArms.Count > 0
            && renderedArms.All(static arm => arm.ResultType is { } type && TypeFamilies.IsIntegerLike(type));

    /// <summary>Every rendered arm is a <c>char</c>-valued constant.</summary>
    internal static bool CharConstantArmsOf(IReadOnlyList<IrExpression> renderedArms)
        => renderedArms.Count > 0
            && renderedArms.All(static arm => CoercionRendering.TryCharConstantValue(arm, out _));

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

    /// <summary>
    /// Re-takes only the enum and <c>char</c> arm facts over each join's
    /// current rendered arms, leaving the integer-family testimony as its own
    /// binding issued it. Coercion insertion wraps an enum-merged join's
    /// integer arms after the final binding, and residual storage binding
    /// retypes slot-load arms; the arm facts are re-taken where their readers
    /// look — residual binding's entry and its end — so each reader sees the
    /// arms it would have walked (value-typed-emission.md, "Enum and char arm
    /// facts").
    /// </summary>
    internal static void RefreshArmFacts(IrNode scope)
    {
        foreach (var join in scope.Descendants.OfType<IPrimitiveJoin>())
        {
            var arms = join.RenderedArms;
            join.PrimitiveTargets = join.PrimitiveTargets with
            {
                IntegerArms = PrimitiveJoinTargetCompatibility.IntegerArmsOf(arms),
                CharConstantArms = PrimitiveJoinTargetCompatibility.CharConstantArmsOf(arms),
            };
        }
    }

    internal static void BindInitialPrimitiveTargets(this IPrimitiveJoin join)
        => join.BindPrimitiveTargets(EmptyShapes, EmptyEnumUnderlyingTypes);

    internal static bool CanRenderPrimitiveJoinAt(
        this IrExpression expression,
        TypeRef target)
        => expression is IPrimitiveJoin join
            && join.PrimitiveTargets.WholeJoinTargets.ContainsKey(target);

    /// <summary>
    /// Whether a conditional, switch expression, or coalesce renders as a
    /// value join at <paramref name="target"/>: the one decision the printer's
    /// targeted join spellings and the residual storage policy share. A
    /// conditional of <c>char</c> constants at <c>char</c> keeps its literal
    /// route; a join of integer arms at an enum-like target takes the enum
    /// route (a coalesce only when its left operand is that enum's
    /// <c>Nullable&lt;T&gt;</c>); otherwise the issued integer-family testimony
    /// decides. Reference arms are not value joins
    /// (<see cref="CanRenderConditionalAt"/>).
    /// </summary>
    internal static bool CanRenderValueJoinAt(
        this IrExpression expression,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes)
    {
        if (expression is not IPrimitiveJoin join)
            return false;
        var testimony = join.PrimitiveTargets;
        if (testimony.WholeJoinTargets.ContainsKey(target))
            return true;
        return expression switch
        {
            Conditional => (testimony.CharConstantArms && IsCoreChar(target))
                || (testimony.IntegerArms && CoercionRendering.IsEnumLikeInteger(target, shapes)),
            SwitchExpression => testimony.IntegerArms && CoercionRendering.IsEnumLikeInteger(target, shapes),
            Coalesce coalesce => testimony.IntegerArms
                && CoercionRendering.IsEnumLikeInteger(target, shapes)
                && CoercionRendering.NullableValueType(coalesce.Left.ResultType)?.Equals(target) == true,
            _ => false,
        };
    }

    /// <summary>
    /// A conditional's full target relation: a value join at
    /// <paramref name="target"/> or reference arms the issued reference
    /// assignment testimony admits there.
    /// </summary>
    internal static bool CanRenderConditionalAt(
        this Conditional conditional,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => conditional.CanRenderValueJoinAt(target, shapes)
            || conditional.CanAssignReferenceArmsTo(target, shapes);

    static bool IsCoreChar(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Char" };

    internal static TypeRef? PrimitiveJoinArmSource(
        this IrExpression expression,
        TypeRef target)
        => expression is IPrimitiveJoin join
            && join.PrimitiveTargets.RenderedArmTargets.TryGetValue(target, out var source)
                ? source
                : null;
}
