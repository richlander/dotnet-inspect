namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Shared preconditions for the coercion spellings owned by <see cref="CSharpPrinter"/>.
/// The insertion pass asks the same questions before it inserts a <see cref="Coerce"/>,
/// so slot-store wrappability cannot drift from what the renderer can spell.
/// </summary>
public static class CoercionRendering
{
    /// <summary>
    /// The slot-store wrappability contract: every accepted pair is a coercion
    /// spelling that <c>CoerceText</c> renders. The asymmetries are intentional:
    /// integer to bool has no spelling, missing enum underlying data assumes I4
    /// for same-family enum casts, and I4 to I8 enum widening requires a resolved
    /// enum source.
    /// </summary>
    public static bool CanSpellSlotCoercion(
        TypeRef? valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
    {
        if (valueType is null)
            return false;
        if (CanSpellBoolToInteger(valueType, target)
            || CanSpellIntegerToEnum(valueType, target, shapes)
            || CanSpellEnumToInteger(valueType, target, shapes, enumUnderlyingTypes)
            || CanSpellEnumToEnum(valueType, target, shapes, enumUnderlyingTypes))
            return true;
        if (TypeFamilies.IsBoolean(valueType) || TypeFamilies.IsBoolean(target))
            return valueType.Equals(target);
        if (!TypeFamilies.IsNumericPrimitive(valueType) || !TypeFamilies.IsNumericPrimitive(target))
            return false;
        var valueFamily = TypeFamilies.Of(valueType);
        return valueFamily is not null && valueFamily == TypeFamilies.Of(target);
    }

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
                || (underlyingFamily == StackFamily.I4 && targetFamily == StackFamily.I8
                    && valueType is not null
                    && enumUnderlyingTypes.ContainsKey(valueType)));

    public static bool CanSpellEnumToEnum(
        TypeRef? valueType,
        TypeRef target,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes,
        IReadOnlyDictionary<TypeRef, TypeRef> enumUnderlyingTypes)
        => EnumSemanticFamily(target, shapes, enumUnderlyingTypes) is not null
            && EnumSemanticFamily(valueType, shapes, enumUnderlyingTypes) is { } valueFamily
            && EnumSemanticFamily(target, shapes, enumUnderlyingTypes) == valueFamily
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
