namespace ILInspector.Decompiler.Pipeline;

/// <summary>The ECMA-335 evaluation-stack families.</summary>
public enum StackFamily { I4, I8, F, I, O }

/// <summary>
/// A type's C#-relevant shape, knowable only by resolving the definition (its
/// base type): the distinction <see cref="TypeFamilies.Of"/> cannot make for a
/// bare definition. <see cref="Unknown"/> covers the unresolved cases — a
/// cross-assembly type with no loaded definition, or anything not a definition.
/// </summary>
public enum TypeShape { Unknown, Reference, ValueType, Enum }

/// <summary>
/// The complete C#-relevant classification of a type — richer than
/// <see cref="TypeShape"/>, which folds interface and delegate into
/// <see cref="TypeShape.Reference"/> because the pipeline's evaluation-stack
/// purposes never need that distinction. This is the consumer-facing projection
/// (<see cref="MetadataSource.ClassifyType(TypeRef)"/>) built from the same
/// base-type reads as <see cref="TypeShape"/> — not a parallel resolver.
/// <see cref="Unknown"/> covers the unresolved cases: a cross-assembly type with
/// no locatable definition, or a non-type handle.
/// </summary>
public enum TypeShapeKind { Unknown, Class, Struct, Enum, Interface, Delegate }

/// <summary>
/// The single home for type-family classification (review consolidation:
/// this knowledge previously lived in the importer's slot merging, the
/// structuring pass's float detection, and three printer spellings). When
/// <c>IsValueType</c> knowledge arrives via resolution, it lands here too.
/// </summary>
public static class TypeFamilies
{
    /// <summary>The stack family, where it is knowable without resolution; null when it is not (a bare definition may be struct, enum, or class).</summary>
    public static StackFamily? Of(TypeRef? type)
    {
        if (type is null)
            return null;
        if (type.Kind is TypeRefKind.SzArray or TypeRefKind.Array)
            return StackFamily.O;
        if (type.Kind != TypeRefKind.Definition || type.Assembly != TypeRef.CoreLibrary || type.Namespace != "System")
            return null;
        return type.Name switch
        {
            "Boolean" or "Char" or "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32" => StackFamily.I4,
            "Int64" or "UInt64" => StackFamily.I8,
            "Single" or "Double" => StackFamily.F,
            "IntPtr" or "UIntPtr" => StackFamily.I,
            "Object" or "String" => StackFamily.O,
            _ => null,
        };
    }

    /// <summary>
    /// The result type of a binary numeric op, per ECMA-335 III.1.5: the wider
    /// operand family wins (F over all, I8 over I4/I, I over I4). Returns the
    /// wider operand's EXACT type, not a canonical one, so signedness and width
    /// survive (<c>uint + uint</c> stays <c>uint</c>); a same-family pair or an
    /// unknown operand keeps the left type, the prior behavior. Only genuine
    /// cross-family pairs (<c>int + long</c>) change — the left-operand shortcut
    /// was wrong for exactly those.
    /// </summary>
    public static TypeRef? BinaryResult(TypeRef? left, TypeRef? right)
    {
        var leftFamily = Of(left);
        var rightFamily = Of(right);
        // A binary op mixing a resolved primitive with an unresolved Definition
        // is the flags-enum idiom: the enum's underlying integer drives the IL
        // `or`/`and`/`add`, but the C# result type is the enum, on whichever side
        // it sits. `flags | 0x20` and `0x20 | flags` are both `flags`-typed. Only
        // an enum reaches a Binary opcode as a Definition operand — a struct or
        // decimal `|`/`+` lowers to an operator-overload call, not this node — so
        // preferring the Definition keeps a flags-enum OR/AND accumulation
        // enum-typed instead of collapsing to the underlying integer, which would
        // then need a cast on every arm and break at the next bare-integer arm
        // (CS0019, #2990). Symmetric to the both-null fallthrough below.
        if (left is { Kind: TypeRefKind.Definition } && leftFamily is null && rightFamily is not null)
            return left;
        if (right is { Kind: TypeRefKind.Definition } && rightFamily is null && leftFamily is not null)
            return right;
        if (leftFamily is not { } lf || rightFamily is not { } rf)
            return left;
        // A bitwise And/Or/Xor that mixes a bool comparison with an integer
        // (e.g. `(a > 0) | (b ? 2 : 0)`) is an integer result, not bool: the
        // bool operand materializes to 0/1. Prefer the non-bool operand's type
        // so the expression doesn't read as bool and force a `? 1 : 0` (CS0029)
        // at a numeric sink. A bool/bool pair (genuine logical `&`/`|`) is
        // untouched — it falls through to the same-family branch below.
        var leftBool = IsBoolean(left);
        var rightBool = IsBoolean(right);
        if (leftBool ^ rightBool)
            return leftBool ? right : left;
        if (lf == rf)
            return left;
        return Wider(lf, rf) == rf ? right : left;
    }

    static StackFamily Wider(StackFamily a, StackFamily b) => (a, b) switch
    {
        (StackFamily.F, _) or (_, StackFamily.F) => StackFamily.F,
        (StackFamily.I8, _) or (_, StackFamily.I8) => StackFamily.I8,
        (StackFamily.I, _) or (_, StackFamily.I) => StackFamily.I,
        _ => a,
    };

    public static bool IsFloat(TypeRef? type) => Of(type) == StackFamily.F;

    /// <summary>True when the family is a known integer (I4/I8/I).</summary>
    public static bool IsInteger(TypeRef? type) => Of(type) is StackFamily.I4 or StackFamily.I8 or StackFamily.I;

    /// <summary>
    /// True when C# null syntax (<c>??</c>, <c>??=</c>, <c>?.</c>, or a null arm)
    /// is known to be illegal for this type because it is a non-nullable value
    /// type. Nullable&lt;T&gt; is deliberately excluded.
    /// </summary>
    public static bool IsKnownNonNullableValueType(TypeRef? type, IReadOnlyDictionary<TypeRef, TypeShape>? shapes = null)
    {
        if (type is null || IsNullableType(type))
            return false;
        if (Of(type) is StackFamily.I4 or StackFamily.I8 or StackFamily.I or StackFamily.F)
            return true;
        if (type.DeclaredValueTypeHint == ValueTypeHint.ValueType)
            return true;
        if (shapes is not null && shapes.TryGetValue(type, out var shape))
            return shape is TypeShape.ValueType or TypeShape.Enum;
        return type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "DateTime" or "DateOnly" or "TimeOnly" or "TimeSpan" or "Guid" or "Decimal"
                or "RuntimeTypeHandle" or "RuntimeFieldHandle" or "ModuleHandle"
                or "TypedReference";
    }

    public static bool IsNullableType(TypeRef type)
        => type is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Nullable`1" },
        };

    /// <summary>The canonical TypeRef for a family — what the evaluation stack physically carries at a join.</summary>
    public static TypeRef Canonical(StackFamily family) => family switch
    {
        StackFamily.I4 => TypeRef.CoreLib("System", "Int32"),
        StackFamily.I8 => TypeRef.CoreLib("System", "Int64"),
        StackFamily.F => TypeRef.CoreLib("System", "Double"),
        StackFamily.I => TypeRef.CoreLib("System", "IntPtr"),
        _ => TypeRef.CoreLib("System", "Object"),
    };

    /// <summary>
    /// True when a value of <paramref name="source"/> needs an explicit C# cast
    /// to flow into a <paramref name="target"/> position — the missing-cast case
    /// behind CS0266. Restricted to numeric primitives of the SAME stack family
    /// (int↔uint, ushort↔char, long↔ulong, nint↔nuint, int→byte, …): there the
    /// cast is a pure reinterpretation of bits the evaluation stack already
    /// carries, so it is faithful to the IL, never a value change. Cross-family
    /// narrowing (long→int) is excluded — that always carries an explicit IL
    /// <c>conv</c> already, so it never reaches here as a bare mismatch. Boolean
    /// is excluded: it shares the I4 family but `(int)b` is not even legal C#.
    /// </summary>
    public static bool NeedsNumericCast(TypeRef? source, TypeRef? target)
        => CSharpConversionRules.NeedsNumericCast(source, target);

    public static bool IsBoolean(TypeRef? type)
        => type is { Name: "Boolean", Assembly: TypeRef.CoreLibrary, Namespace: "System" };

    public static bool IsNumericPrimitive(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32"
                or "Int64" or "UInt64" or "IntPtr" or "UIntPtr" or "Char" or "Single" or "Double";

    /// <summary>An integer-family primitive — the values that can carry an enum's underlying representation, so an explicit cast to the enum is faithful (floats excluded).</summary>
    public static bool IsIntegerLike(TypeRef type)
        => IsNumericPrimitive(type) && type.Name is not ("Single" or "Double");

    /// <summary>Byte width of a fixed-width integer primitive; null for the platform-sized (nint/nuint) and float types.</summary>
    static int? Width(TypeRef? type) => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
        ? type.Name switch
        {
            "SByte" or "Byte" => 1,
            "Int16" or "UInt16" or "Char" => 2,
            "Int32" or "UInt32" => 4,
            "Int64" or "UInt64" => 8,
            _ => null,
        }
        : null;

    /// <summary>True when two fixed-width integer primitives occupy the same byte width (ushort/char, int/uint) — a same-width cast subsumes any inner conversion to the sibling.</summary>
    public static bool SameWidth(TypeRef? a, TypeRef? b) => Width(a) is { } wa && wa == Width(b);

    /// <summary>True for the unsigned fixed-width integer primitives (char included; nuint excluded — platform width is unknown here).</summary>
    static bool IsUnsignedInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Byte" or "UInt16" or "Char" or "UInt32" or "UInt64" };

    /// <summary>True for the signed fixed-width integer primitives (sbyte/short/int/long); nint excluded — platform width is unknown here.</summary>
    public static bool IsSignedFixedWidthInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "SByte" or "Int16" or "Int32" or "Int64" };

    /// <summary>
    /// For a widening integer conversion to an unsigned <paramref name="target"/>, the
    /// unsigned sibling of <paramref name="source"/>'s width — the cast that
    /// zero-extends a negative constant faithfully. `conv.u8` of an int32 is
    /// <c>(ulong)(uint)x</c> (= <c>uint.MaxValue</c> for <c>ldc.i4.m1</c>), not the
    /// sign-extended <c>(ulong)x</c> (= <c>ulong.MaxValue</c>). Null when no
    /// zero-extension reinterpret applies: a same-or-narrower or native-width source,
    /// a signed or native-width target, or a non-integer (#2101).
    /// </summary>
    public static TypeRef? ZeroExtendingSource(TypeRef? source, TypeRef? target)
        => IsUnsignedInteger(target)
            && Width(source) is { } sourceWidth
            && Width(target) is { } targetWidth
            && sourceWidth < targetWidth
            ? sourceWidth switch
            {
                1 => TypeRef.CoreLib("System", "Byte"),
                2 => TypeRef.CoreLib("System", "UInt16"),
                4 => TypeRef.CoreLib("System", "UInt32"),
                _ => null,
            }
            : null;

    /// <summary>
    /// The unsigned sibling a SIGNED operand must be reinterpreted through when a
    /// widening conversion to <paramref name="target"/> zero-extends it
    /// (<c>conv.u8</c>/<c>conv.u</c> of a non-constant). The zero-extension
    /// operates at the operand's IL STACK width — a sub-int operand is
    /// sign-extended to int32 first — so an <c>sbyte s</c> widens as
    /// <c>(ulong)(uint)s</c>, never <c>(ulong)(byte)s</c> (which would discard the
    /// sign extension and compute the wrong value). A native-unsigned target
    /// (<c>nuint</c>, <c>conv.u</c>) zero-extends the int32 stack value on 64-bit
    /// and reinterprets it on 32-bit, so <c>(nuint)(uint)x</c> is faithful on both;
    /// a <see cref="long"/> source is native-width and left alone. Null when no
    /// widening reinterpret applies: an unsigned or non-fixed-width source, or a
    /// <see cref="long"/> source into <see cref="ulong"/>/native (same stack width).
    /// </summary>
    public static TypeRef? WideningZeroExtendSibling(TypeRef? signedSource, TypeRef? target)
    {
        if (Width(signedSource) is not { } sourceWidth || !IsSignedFixedWidthInteger(signedSource))
            return null;
        // A native-unsigned target has no fixed width here, but conv.u of an
        // int32-stack value zero-extends it, so an int32-stack signed source
        // (width <= 4) takes the uint sibling regardless of platform width.
        if (sourceWidth <= 4 && IsNativeUnsignedInteger(target))
            return TypeRef.CoreLib("System", "UInt32");
        var stackType = sourceWidth <= 4 ? TypeRef.CoreLib("System", "Int32") : TypeRef.CoreLib("System", "Int64");
        return ZeroExtendingSource(stackType, target);
    }

    /// <summary>The native-sized unsigned integer primitive (<c>nuint</c>/<see cref="UIntPtr"/>).</summary>
    static bool IsNativeUnsignedInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "UIntPtr" };

    /// <summary>
    /// True when the C# <em>checked</em> conversion <paramref name="source"/> →
    /// <paramref name="target"/> can throw for some value — i.e. when a bare
    /// cast inside a lexical <c>checked</c> region recompiles to a
    /// <c>conv.ovf</c> that changes IL semantics, so a synthesized reinterpret
    /// cast there must wrap in <c>unchecked(...)</c>. False exactly for the
    /// value-preserving-for-all-values pairs — identity, same-signedness
    /// widening, and unsigned into strictly-wider signed — where the checked
    /// and unchecked conversions agree and the wrap would be pure noise
    /// (`checked((int)intBackedEnum + 1)` must stay bare — the work-2302 CI
    /// canary). Platform-width (nint/nuint) and unknown pairs answer true:
    /// when the width is unknown, the wrap is the safe direction.
    /// </summary>
    public static bool CheckedConversionCanThrow(TypeRef? source, TypeRef? target)
        => CSharpConversionRules.CheckedConversionCanThrow(source, target);

    /// <summary>
    /// True when an integer constant is in <paramref name="target"/>'s range, so
    /// C# converts it implicitly (a constant-expression conversion) and no cast
    /// is needed. A value outside the range — a negative into unsigned, a bitmask
    /// wider than the target — does not convert bare and needs an unchecked cast.
    /// </summary>
    public static bool ConstantFits(long value, TypeRef target)
        => CSharpConversionRules.ConstantFits(value, target);

    /// <summary>
    /// The full C# implicit numeric widening table between integer/native/char
    /// primitives: <paramref name="source"/>'s value range is wholly contained in
    /// <paramref name="target"/>'s, so the conversion never overflows and a
    /// <c>checked</c> context emits a plain <c>conv</c> (or nothing) rather than a
    /// <c>conv.ovf.*</c>. Everything else integer→integer is an <em>explicit</em>
    /// (narrowing or sign-changing) conversion that <c>checked</c> turns into a
    /// <c>conv.ovf.*</c> — the printer relies on this to decide when a plain
    /// conversion spelled inside a checked region needs an <c>unchecked(...)</c>
    /// guard. Verified against csc codegen for the whole conversion matrix
    /// (including <c>nint</c>/<c>nuint</c> and the <c>.un</c> overflow forms).
    /// </summary>
    public static bool IsImplicitIntegerWidening(TypeRef source, TypeRef target)
        => CSharpConversionRules.IsImplicitIntegerWidening(source, target);

    /// <summary>The unsigned-counterpart TypeRef of a signed integer type; null when already unsigned, float, or unknown.</summary>
    public static TypeRef? UnsignedCounterpart(TypeRef? type) => UnsignedCastKeyword(type) switch
    {
        "uint" => TypeRef.CoreLib("System", "UInt32"),
        "ulong" => TypeRef.CoreLib("System", "UInt64"),
        "nuint" => TypeRef.CoreLib("System", "UIntPtr"),
        _ => null,
    };

    /// <summary>The signed-counterpart TypeRef of a wide unsigned integer type; null when already signed, float, sub-int, or unknown.</summary>
    public static TypeRef? SignedCounterpart(TypeRef? type) => SignedCastKeyword(type) switch
    {
        "int" => TypeRef.CoreLib("System", "Int32"),
        "long" => TypeRef.CoreLib("System", "Int64"),
        "nint" => TypeRef.CoreLib("System", "IntPtr"),
        _ => null,
    };

    /// <summary>
    /// The C# cast keyword that reinterprets a signed-integer type as its
    /// unsigned counterpart; null when the type is already unsigned, float
    /// (.un means unordered there), or unknown.
    /// </summary>
    public static string? UnsignedCastKeyword(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            ? type.Name switch
            {
                "SByte" or "Int16" or "Int32" => "uint",
                "Int64" => "ulong",
                "IntPtr" => "nuint",
                _ => null,
            }
            : null;

    /// <summary>
    /// The C# cast keyword that reinterprets a <em>wide</em> unsigned-integer type
    /// (uint/ulong/nuint) as its signed counterpart; null when already signed,
    /// float, sub-int (byte/ushort/char promote to int), or unknown. The mirror of
    /// <see cref="UnsignedCastKeyword"/>, used to reconcile a signed ordering
    /// comparison whose operands are a same-width signed/unsigned pair.
    /// </summary>
    public static string? SignedCastKeyword(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            ? type.Name switch
            {
                "UInt32" => "int",
                "UInt64" => "long",
                "UIntPtr" => "nint",
                _ => null,
            }
            : null;

    /// <summary>
    /// True for the unsigned fixed/native integer primitives (byte, ushort,
    /// uint, ulong, nuint). Excludes bool and char, which are unsigned in
    /// representation but are not the integers a signed/unsigned bitwise
    /// reconciliation should treat as the unsigned partner.
    /// </summary>
    public static bool IsUnsignedIntegerPrimitive(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "Byte" or "UInt16" or "UInt32" or "UInt64" or "UIntPtr";
}

/// <summary>
/// The single home for condition negation — the type-aware IL duals (the
/// NaN subtlety): integer comparisons invert their kind; float
/// comparisons invert kind AND flip the unordered flag (NOT(a &lt; b) is
/// a &gt;= b unordered, never plain a &gt;= b); unknown operand types wrap
/// in LogicalNot honestly; double negation unwraps.
/// </summary>
public static class Conditions
{
    public static ComparisonKind Inverse(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equal => ComparisonKind.NotEqual,
        ComparisonKind.NotEqual => ComparisonKind.Equal,
        ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
        ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
        _ => ComparisonKind.LessThan,
    };

    /// <summary>Swaps operand order (a &lt; b becomes b &gt; a); the dual of <see cref="Inverse"/>, which negates.</summary>
    public static ComparisonKind Mirror(ComparisonKind kind) => kind switch
    {
        ComparisonKind.LessThan => ComparisonKind.GreaterThan,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.GreaterThan => ComparisonKind.LessThan,
        ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
        _ => kind,
    };

    /// <summary>Negates a DETACHED condition, producing a detached result ready for adoption.</summary>
    public static IrExpression Negate(IrExpression condition) => condition switch
    {
        LogicalNot not => (IrExpression)not.DetachChildren()[0],
        Comparison comparison => InvertComparison(comparison),
        _ => new LogicalNot(condition),
    };

    static IrExpression InvertComparison(Comparison comparison)
    {
        bool? isFloat = OperandFloatness(comparison);
        if (isFloat is null && comparison.Kind is not (ComparisonKind.Equal or ComparisonKind.NotEqual))
            return new LogicalNot(comparison);  // ordering duals need known operand types

        var operands = comparison.DetachChildren();
        bool isUnsigned = isFloat == true ? !comparison.IsUnsigned : comparison.IsUnsigned;
        return new Comparison(Inverse(comparison.Kind), isUnsigned, (IrExpression)operands[0], (IrExpression)operands[1]);
    }

    /// <summary>True = float operands, false = known integer, null = unknown.</summary>
    static bool? OperandFloatness(Comparison comparison)
    {
        bool? Of(TypeRef? type) => TypeFamilies.Of(type) switch
        {
            StackFamily.F => true,
            StackFamily.I4 or StackFamily.I8 or StackFamily.I => false,
            _ => null,
        };
        return Of(comparison.Left.ResultType) ?? Of(comparison.Right.ResultType);
    }
}
