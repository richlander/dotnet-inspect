namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Shared preconditions for the coercion spellings owned by <see cref="CSharpPrinter"/>.
/// The insertion pass asks the same questions before it inserts a <see cref="Coerce"/>,
/// so slot-store wrappability cannot drift from what the renderer can spell.
/// </summary>
public static class CoercionRendering
{
    /// <summary>
    /// The slot-store wrappability contract: every accepted pair is both a
    /// coercion spelling that <c>CoerceText</c> renders and a stack-family
    /// relation the verifier can carry in one live range. The spelling half is
    /// owned by <see cref="CanSpellValueCoercion"/>; the remaining family check
    /// is slot-specific. The asymmetries are intentional: integer to bool has no
    /// spelling, missing enum underlying data assumes I4 for same-family enum
    /// casts, and slot I4 to I8 enum widening requires a resolved enum source.
    /// </summary>
    public static bool CanSpellSlotCoercion(
        TypeRef? valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
    {
        if (valueType is null)
            return false;
        if (!CanSpellValueCoercion(valueType, target, shapes, enumUnderlyingTypes))
            return false;

        var valueFamily = SlotSemanticFamily(valueType, shapes, enumUnderlyingTypes);
        var targetFamily = SlotSemanticFamily(target, shapes, enumUnderlyingTypes);
        return valueFamily is not null && targetFamily is not null
            && (valueFamily == targetFamily
                || (valueFamily == StackFamily.I4 && targetFamily == StackFamily.I8
                    && enumUnderlyingTypes.ContainsKey(valueType)));
    }

    /// <summary>
    /// Type-level coercion spellings owned by <c>CoerceText</c>. This predicate
    /// intentionally excludes value-sensitive spellings such as numeric constants
    /// and node-shape spellings such as address-to-pointer casts; callers with
    /// only source/target types cannot prove those cases.
    /// </summary>
    public static bool CanSpellValueCoercion(
        TypeRef valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => valueType.Equals(target)
            || CanSpellBoolToInteger(valueType, target)
            || CanSpellIntegerToEnum(valueType, target, shapes)
            || CanSpellEnumToInteger(valueType, target, shapes, enumUnderlyingTypes)
            || CanSpellEnumToEnum(valueType, target, shapes, enumUnderlyingTypes)
            || CanSpellPrimitiveNumeric(valueType, target);

    public static bool CanSpellIntegerToEnum(
        TypeRef? valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => shapes.GetValueOrDefault(target) == TypeShape.Enum
            && valueType is not null
            && !target.Equals(valueType)
            && TypeFamilies.IsIntegerLike(valueType);

    public static bool CanSpellEnumToInteger(
        TypeRef? valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => TypeFamilies.IsIntegerLike(target)
            && EnumSemanticFamily(valueType, shapes, enumUnderlyingTypes) is { } underlyingFamily
            && TypeFamilies.Of(target) is { } targetFamily
            && (underlyingFamily == targetFamily
                || underlyingFamily == StackFamily.I4 && targetFamily == StackFamily.I8);

    public static bool CanSpellEnumToEnum(
        TypeRef? valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => EnumSemanticFamily(target, shapes, enumUnderlyingTypes) is not null
            && EnumSemanticFamily(valueType, shapes, enumUnderlyingTypes) is not null
            && valueType?.Equals(target) != true;

    public static bool CanSpellUnknownEnumConstant(
        TypeRef? valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => target is { Kind: TypeRefKind.Definition, Name: not "Boolean" }
            && shapes.GetValueOrDefault(target) == TypeShape.Unknown
            && !TypeFamilies.IsNumericPrimitive(target)
            && valueType is not null
            && TypeFamilies.IsIntegerLike(valueType);

    public static bool CanSpellBoolToInteger(TypeRef? valueType, TypeRef target)
        => TypeFamilies.IsIntegerLike(target) && TypeFamilies.IsBoolean(valueType);

    public static bool CanSpellPrimitiveNumeric(TypeRef? valueType, TypeRef? target)
        => !TypeFamilies.IsBoolean(valueType)
            && !TypeFamilies.IsBoolean(target)
            && valueType is not null
            && target is not null
            && TypeFamilies.IsNumericPrimitive(valueType)
            && TypeFamilies.IsNumericPrimitive(target)
            && TypeFamilies.Of(valueType) == TypeFamilies.Of(target);

    static StackFamily? SlotSemanticFamily(
        TypeRef? type,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => TypeFamilies.Of(type) ?? EnumSemanticFamily(type, shapes, enumUnderlyingTypes);

    /// <summary>
    /// The stack family of an enum-typed value's underlying: the resolved
    /// underlying family, or I4 for a missing-<c>value__</c> enum shape. Null for
    /// non-enums.
    /// </summary>
    public static StackFamily? EnumSemanticFamily(
        TypeRef? type,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
    {
        if (type is null || TypeFamilies.Of(type) is not null)
            return null;
        if (enumUnderlyingTypes.GetValueOrDefault(type) is { } underlying)
            return TypeFamilies.Of(underlying);
        return shapes.GetValueOrDefault(type) == TypeShape.Enum ? StackFamily.I4 : null;
    }
}
