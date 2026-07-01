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
        if (IsIntegerConstant(whenTrue, out var trueValue) && !IsIntegerConstant(whenFalse, out _) && FitsEnumTarget(trueValue, whenFalse.ResultType, function))
            return whenFalse.ResultType;
        if (IsIntegerConstant(whenFalse, out var falseValue) && !IsIntegerConstant(whenTrue, out _) && FitsEnumTarget(falseValue, whenTrue.ResultType, function))
            return whenTrue.ResultType;
        return null;
    }

    static bool IsIntegerConstant(IrExpression expression, out long value)
    {
        if (expression is Constant constant && TypeFamilies.IsIntegerLike(constant.Type) && TryToInt64(constant.Value, out value))
            return true;
        value = 0;
        return false;
    }

    // Anchors only to an enum whose underlying type can represent the constant. The
    // printer renders an integer arm of an enum-typed conditional as an explicit
    // `(EnumType)value` cast; that cast is a constant-expression conversion, so it
    // only compiles when the value is in the underlying type's range (`(Tiny)300`
    // for `enum Tiny : byte` is CS0221). A plain (non-enum) integer target is
    // excluded entirely: the arm printer omits the cast a narrower or out-of-range
    // constant would need (e.g. `byte b = c ? 300 : x`).
    static bool FitsEnumTarget(long value, TypeRef? type, IrFunction function)
        => type is not null
            && function.TypeShapes.GetValueOrDefault(type) == TypeShape.Enum
            && function.EnumUnderlyingTypes.TryGetValue(type, out var underlying)
            && TypeFamilies.ConstantFits(value, underlying);

    static bool TryToInt64(object? value, out long result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case long l: result = l; return true;
            case short s: result = s; return true;
            case sbyte sb: result = sb; return true;
            case byte b: result = b; return true;
            case ushort us: result = us; return true;
            case uint ui: result = ui; return true;
            case char c: result = c; return true;
            case ulong ul when ul <= long.MaxValue: result = (long)ul; return true;
            default: result = 0; return false;
        }
    }
}
