namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Shared arm-type reconciliation for the slot-diamond folds
/// (<see cref="BooleanFoldingPass"/>, <see cref="SlotDiamondPass"/>,
/// <see cref="SlotStoreDiamondPass"/>). Each fold turns two same-slot stores into
/// one <see cref="Conditional"/>; the conditional's result type decides how the
/// printer types (and splits) the reused stack slot.
/// </summary>
static class DiamondArmTypes
{
    /// <summary>
    /// The sound slot type when the importer could not merge the join (its load
    /// type is unknown) and the arms carry conflicting types — one an integer
    /// constant, the other a concrete enum value (e.g. <c>c ? 4 : GetKind()</c>
    /// where <c>GetKind</c> returns an enum). The IL stores that integer into the
    /// same slot as the enum value, so the constant is a faithful reinterpretation
    /// of the enum type; anchoring <see cref="Conditional.MergedType"/> here makes
    /// the folded conditional's result type independent of arm order.
    ///
    /// <para>Without it the result type falls back to whichever arm prints first
    /// (<see cref="Conditional.ResultType"/> is <c>MergedType ?? WhenTrue ??
    /// WhenFalse</c>). A polarity-preserved <c>!c ? const : enum</c> then leaves
    /// the integer constant first, collapsing the reused slot to <c>int</c> and
    /// mis-rendering the enum use without a cast — invalid C# (regressed by
    /// #1901, which stopped swapping the arms of a negated condition).</para>
    /// </summary>
    public static TypeRef? ConflictingArmType(IrExpression whenTrue, IrExpression whenFalse, IrFunction function)
    {
        if (IsIntegerConstant(whenTrue) && !IsIntegerConstant(whenFalse) && IsEnumTarget(whenFalse.ResultType, function))
            return whenFalse.ResultType;
        if (IsIntegerConstant(whenFalse) && !IsIntegerConstant(whenTrue) && IsEnumTarget(whenTrue.ResultType, function))
            return whenTrue.ResultType;
        return null;
    }

    static bool IsIntegerConstant(IrExpression expression)
        => expression is Constant constant && TypeFamilies.IsIntegerLike(constant.Type);

    // Restricted to enum targets: the printer always renders an integer arm for an
    // enum-typed conditional with an explicit `(EnumType)value` cast, so anchoring
    // to a known enum is faithful and valid for any integer constant. A plain
    // integer target has no such guarantee — a narrower or out-of-range constant
    // (e.g. `byte b = c ? 300 : x`) would need a cast the arm printer omits — so it
    // is intentionally excluded.
    static bool IsEnumTarget(TypeRef? type, IrFunction function)
        => type is not null && function.TypeShapes.GetValueOrDefault(type) == TypeShape.Enum;
}
