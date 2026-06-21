namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The numeric and cast spelling concern of <see cref="CSharpPrinter"/>: the
/// last-mile decisions C# requires but IL leaves implicit — explicit numeric
/// cast insertion (the missing-cast case behind CS0266), unchecked
/// reinterpretation of out-of-range constants, unsigned-operand casts for
/// div/rem/shr.un, redundant shift-width-mask elision, and the unordered-float
/// comparison rules. The pure type-level decisions these rely on live in
/// <see cref="TypeFamilies"/>; what stays here is the spelling, which is coupled
/// to the printer's recursive <c>Operand</c>/<c>Expression</c>/<c>TypeText</c>.
/// Split into its own file (a partial of the same class) so the main file is the
/// tree-walk visitor; behavior is identical.
/// </summary>
public sealed partial class CSharpPrinter
{
    string BinaryText(Binary binary)
    {
        // div.un/rem.un compute on unsigned operands; shr.un shifts an
        // unsigned left operand. Operands that are already unsigned (or
        // float, where .un means unordered, not unsigned) print plain.
        bool castBoth = binary.IsUnsigned && binary.Kind is BinaryKind.Divide or BinaryKind.Remainder;
        bool castLeft = castBoth || (binary.IsUnsigned && binary.Kind is BinaryKind.ShiftRight);
        // A bitwise &/|/^ between a signed and an unsigned integer of the same
        // stack width is CS0019 (no implicit signed<->unsigned conversion), but
        // the bit result is identical under either interpretation. Reinterpret
        // the signed operand as unsigned — `S_0 | (ulong)S_1` — which is a no-op
        // (same-width) reinterpret, so the `or`/`and`/`xor` opcode is unchanged.
        bool mixedSignBitwise = MixedSignBitwise(binary);
        string left = mixedSignBitwise ? BitwiseUnsignedOperand(binary.Left)
            : castLeft ? UnsignedOperand(binary.Left)
            : Operand(binary.Left);
        bool isShift = binary.Kind is BinaryKind.ShiftLeft or BinaryKind.ShiftRight;
        string right = isShift ? ShiftCount(binary)
            : mixedSignBitwise ? BitwiseUnsignedOperand(binary.Right)
            : castBoth ? UnsignedOperand(binary.Right)
            : Operand(binary.Right);
        string text = $"{left} {BinaryOperator(binary)} {right}";
        // add.ovf/sub.ovf/mul.ovf (and their .un forms) carry an overflow check
        // the default (unchecked) C# context would drop — spell it explicitly so
        // the recompiled IL keeps the .ovf opcode. A nested checked binary
        // re-wraps redundantly but emits the same opcode stream.
        return binary.IsChecked ? $"checked({text})" : text;
    }

    /// <summary>
    /// True when a bitwise <c>&amp;</c>/<c>|</c>/<c>^</c> has one signed and one
    /// unsigned integer operand of the same stack width (e.g. <c>ulong | long</c>).
    /// C# rejects that pair (CS0019) because neither converts to the other, yet
    /// the bit result is the same under either interpretation, so the printer
    /// reinterprets the signed operand as unsigned. Restricted to same-width
    /// signed/unsigned integer pairs — bool/char are excluded by
    /// <see cref="TypeFamilies.IsUnsignedIntegerPrimitive"/>.
    /// </summary>
    bool MixedSignBitwise(Binary binary)
    {
        if (binary.Kind is not (BinaryKind.And or BinaryKind.Or or BinaryKind.Xor))
            return false;
        var left = EffectiveType(binary.Left);
        var right = EffectiveType(binary.Right);
        var family = TypeFamilies.Of(left);
        if (family is not (StackFamily.I4 or StackFamily.I8 or StackFamily.I) || family != TypeFamilies.Of(right))
            return false;
        bool leftSigned = TypeFamilies.UnsignedCastKeyword(left) is not null;
        bool rightSigned = TypeFamilies.UnsignedCastKeyword(right) is not null;
        return (leftSigned && TypeFamilies.IsUnsignedIntegerPrimitive(right))
            || (rightSigned && TypeFamilies.IsUnsignedIntegerPrimitive(left));
    }

    /// <summary>
    /// Reinterprets a bitwise operand as its unsigned counterpart for the
    /// signed/unsigned reconciliation in <see cref="MixedSignBitwise"/>. An
    /// already-unsigned operand prints plain; an operand that reduces to a
    /// negative constant (possibly behind conv nodes) gets the
    /// <c>unchecked((uint)(-x))</c> bit spelling so the out-of-range cast is
    /// legal (CS0221); any other signed operand takes the same-width reinterpret
    /// cast, which emits no opcode.
    /// </summary>
    string BitwiseUnsignedOperand(IrExpression operand)
    {
        var unsigned = TypeFamilies.UnsignedCounterpart(EffectiveType(operand));
        if (unsigned is null)
            return Operand(operand);
        string cast = $"({TypeText(unsigned)}){Operand(operand)}";
        return TryGetIntegerConstant(operand, out long value) && !TypeFamilies.ConstantFits(value, unsigned)
            ? $"unchecked({cast})"
            : cast;
    }

    /// <summary>The integer value an expression reduces to, peeling unchecked conv nodes; false when it is not a constant.</summary>
    static bool TryGetIntegerConstant(IrExpression expression, out long value)
    {
        while (expression is Convert { IsChecked: false } convert)
            expression = convert.Operand;
        switch (expression)
        {
            case Constant { Value: int i }:
                value = i;
                return true;
            case Constant { Value: long l }:
                value = l;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>
    /// Renders a shift's count operand, stripping a redundant width mask. C#
    /// masks a shift count by the left operand's width — int/uint by 31, long/
    /// ulong by 63 — and the compiler bakes that mask into the IL. Reading it
    /// back and spelling it explicitly (<c>n &amp; 31</c>) would double-mask on
    /// recompile, since the compiler re-applies its own mask. Dropping a count
    /// mask that exactly matches the implicit width mask keeps the opcode stream
    /// faithful (and the masks are idempotent, so the value is unchanged).
    /// </summary>
    string ShiftCount(Binary shift)
    {
        if (shift.Right is Binary { Kind: BinaryKind.And, Right: Constant { Value: int mask } } masked
            && ShiftWidthMask(EffectiveType(shift.Left)) is { } width && mask == width)
            return IntShiftCount(masked.Left);
        return IntShiftCount(shift.Right);
    }

    // C#'s shift operators take an `int` count; a `uint` or enum count is CS0019.
    // IL's shl/shr take an int32 count, so reinterpreting a uint or a 32-bit enum as
    // int emits no conv — the cast is fidelity-neutral. A long/native-int count
    // already carries its own narrowing Convert (int32-typed by the time it lands
    // here), and small ints widen to int implicitly, so neither needs a cast.
    string IntShiftCount(IrExpression count)
        => NeedsIntShiftCast(EffectiveType(count)) ? $"(int){Operand(count)}" : Operand(count);

    bool NeedsIntShiftCast(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "UInt32" }
            || (type is not null && _function.TypeShapes.GetValueOrDefault(type) == TypeShape.Enum);

    static int? ShiftWidthMask(TypeRef? leftOperand) => TypeFamilies.Of(leftOperand) switch
    {
        StackFamily.I4 => 31,
        StackFamily.I8 => 63,
        _ => null,
    };

    string ComparisonText(Comparison comparison)
        => ComparisonText(comparison.Kind, comparison.IsUnsigned, comparison.Left, comparison.Right);

    string ComparisonText(ComparisonKind kind, bool isUnsigned, IrExpression left, IrExpression right)
    {
        // On floats .un means UNORDERED, and C#'s ordering operators are
        // ordered — 'a >= b unordered' must print as !(a < b) or NaN inputs
        // execute the wrong path. Equality needs no special form: C#'s ==
        // is beq and != is bne.un already.
        if (isUnsigned && IsFloatComparison(left, right)
            && kind is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual)
        {
            return $"!({Operand(left)} {ComparisonOperator(Conditions.Inverse(kind))} {Operand(right)})";
        }
        // Null tests render the is-form (taste doc): it is always the
        // reference test the IL performs, where == could round-trip to an
        // op_Equality call under operator overloads.
        if (kind is ComparisonKind.Equal or ComparisonKind.NotEqual
            && (left is Constant { Value: null } || right is Constant { Value: null }))
        {
            var operand = right is Constant { Value: null } ? left : right;
            return kind == ComparisonKind.Equal
                ? $"{Operand(operand)} is null"
                : $"{Operand(operand)} is not null";
        }
        // The `cgt.un`/`clt.un` against null idiom csc emits for a reference
        // inequality (`ldnull; cgt.un` = `obj != null`): an unsigned ordering of a
        // reference against null tests non-nullness (null is 0, so `x > 0` / `0 <
        // x` unsigned is `x != 0`). There is no is-null ordering form, so this is
        // always the not-null test; rendered literally `obj > null` is CS0019.
        // Pointers forbid the is-pattern (CS8521), so spell those `!= null`.
        if (isUnsigned
            && ((kind == ComparisonKind.GreaterThan && right is Constant { Value: null })
                || (kind == ComparisonKind.LessThan && left is Constant { Value: null })))
        {
            var operand = kind == ComparisonKind.GreaterThan ? left : right;
            return operand.ResultType is { Kind: TypeRefKind.Pointer }
                ? $"{Operand(operand)} != null"
                : $"{Operand(operand)} is not null";
        }
        // A pointer compared to a native-int zero is a null check: csc lowers
        // `ptr == null` to `ldc.i4.0; conv.u; ceq`, so the zero arrives as an
        // `int`/`nuint` 0 (often through a Convert). Comparing a pointer to that
        // integer is CS0019; spell it `ptr == null`. C# forbids `is null` on
        // pointer types (CS8521), so this uses the equality form, not is-null.
        if (kind is ComparisonKind.Equal or ComparisonKind.NotEqual)
        {
            var pointer = left.ResultType is { Kind: TypeRefKind.Pointer } ? left
                : right.ResultType is { Kind: TypeRefKind.Pointer } ? right
                : null;
            if (pointer is not null)
            {
                var other = ReferenceEquals(pointer, left) ? right : left;
                if (other.ResultType is not { Kind: TypeRefKind.Pointer } && IsZeroConstant(other))
                {
                    return $"{Operand(pointer)} {ComparisonOperator(kind)} null";
                }
            }
        }
        return isUnsigned
            ? $"{UnsignedOperand(left)} {ComparisonOperator(kind)} {UnsignedOperand(right)}"
            : $"{Operand(left)} {ComparisonOperator(kind)} {Operand(right)}";
    }

    static bool IsFloatComparison(IrExpression left, IrExpression right)
        => TypeFamilies.IsFloat(left.ResultType) || TypeFamilies.IsFloat(right.ResultType);

    /// <summary>
    /// True when an expression is an integer zero literal, peeling any native-int
    /// reinterpret converts (`ldc.i4.0; conv.u`) the IL emits for a pointer
    /// `null`. Used to spell a pointer-vs-zero comparison as `ptr == null`.
    /// </summary>
    static bool IsZeroConstant(IrExpression expression)
    {
        while (expression is Convert convert)
        {
            expression = convert.Operand;
        }

        return expression is Constant { Value: 0 or 0L or 0u or 0UL or (short)0 or (sbyte)0 or (byte)0 or (ushort)0 };
    }

    /// <summary>Casts a signed-integer operand to its unsigned counterpart; already-unsigned, float (.un = unordered), and unknown-typed operands print plain.</summary>
    string UnsignedOperand(IrExpression operand)
    {
        string? cast = TypeFamilies.UnsignedCastKeyword(operand.ResultType);
        return cast is null ? Operand(operand) : $"({cast}){Operand(operand)}";
    }

    /// <summary>
    /// Renders <paramref name="value"/> for a position typed <paramref name="target"/>,
    /// inserting the explicit numeric cast C# requires when the value's type
    /// would not implicitly convert (the missing-cast case behind CS0266). The
    /// cast reinterprets bits the evaluation stack already carries (same family),
    /// so it is faithful to the IL — see <see cref="TypeFamilies.NeedsNumericCast"/>.
    /// </summary>
    string CastValue(IrExpression value, TypeRef? target)
    {
        // A fixed-statement pinned local reads as a pointer (the fixed variable),
        // so the IL's conv.u/conv.i deriving an unmanaged pointer from it
        // (Convert over the pinned load) is a pointer reinterpret. Into a pointer
        // target that is the explicit pointer cast — (uint*)V_0 — not the
        // managed-reference-to-nuint conversion the conv would otherwise print.
        if (target is { Kind: TypeRefKind.Pointer }
            && value is Convert { Operand: LoadLocal pinnedLoad }
            && _fixedLocals.Contains(pinnedLoad.Index))
        {
            return $"({TypeText(target)}){LocalName(pinnedLoad.Index)}";
        }
        // An integer flowing into an enum-typed position — a comparison kind, a
        // flags value computed at run time — needs an explicit (Enum)x cast: C#
        // converts int→enum implicitly only for the literal 0. The cast is always
        // legal off any integer and is faithful, since IL carries an enum as its
        // underlying integer (TypedConstantsPass already retypes the constant
        // operands, so only the genuinely non-constant boundaries reach here).
        // This runs before the merge-node bail below: a ternary of integer arms
        // into an enum (`flag ? 1 : 0`) is a real CS0266, and the cast wraps the
        // whole merge — `(StringComparison)(flag ? 1 : 0)` — which is legal off a
        // concrete enum target (the bail's CS0030 risk is type-parameter-only).
        if (target is { } enumTarget
            && _function.TypeShapes.GetValueOrDefault(enumTarget) == TypeShape.Enum
            && EffectiveType(value) is { } enumSource && !enumTarget.Equals(enumSource)
            && TypeFamilies.IsIntegerLike(enumSource))
        {
            // A negative literal must be parenthesized after the cast (CS0075),
            // as the enum-constant path above (line ~482) already does.
            bool negativeLiteral = value is Constant { Value: int iv } && iv < 0
                || value is Constant { Value: long lv } && lv < 0;
            return $"({TypeText(enumTarget)}){(negativeLiteral ? $"({Operand(value)})" : Operand(value))}";
        }
        // A bool-valued expression flowing into an integer target is IL's
        // comparison/test result consumed as a number — `cgt.un; ret` from an int
        // method (e.g. byte.Sign, Convert.ToInt32(bool)). C# has no implicit
        // bool→int, so spell it as the faithful `cond ? 1 : 0`; Roslyn folds that
        // back to the bare comparison opcode, so it recompiles exactly. Runs
        // before the merge-node bail so a bool ternary/coalesce into int is wrapped
        // too (its arms are bool, the wrap is legal).
        if (target is { } intTarget && TypeFamilies.IsIntegerLike(intTarget)
            && EffectiveType(value) is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return $"{Condition(value)} ? 1 : 0";
        // Cast only off a value whose rendered C# type reliably equals its IR
        // result type. A merge node (ternary/coalesce) reports a merged type the
        // arms may not actually share, and a stack slot's type is the join of
        // every store — both diverge from the rendered type in slot-confused or
        // generic bodies, where a keyed cast would be illegal (CS0030). Leave
        // those to render as-is (the enum cast above is the one safe exception).
        if (value is Conditional or Coalesce or LoadStackSlot)
            return Expression(value);
        // A constant carries an exact value: C# converts an in-range one to the
        // target type implicitly (render bare), while an out-of-range one — a
        // negative into unsigned, a bitmask wider than the target — does not
        // convert bare and is CS0266/CS0221, so reinterpret its bits with an
        // unchecked cast (uint.MaxValue's `ldc.i4.m1` → unchecked((uint)(-1))).
        if (value is Constant { Value: int or long } konst && target is { } t && TypeFamilies.IsNumericPrimitive(t))
            return NumericConstant(konst, t);
        if (!TypeFamilies.NeedsNumericCast(EffectiveType(value), target))
            return Expression(value);
        // A plain conversion to a same-width sibling (conv.u2 → ushort feeding a
        // char slot) is subsumed by the boundary cast: emit one cast to the
        // target on the conversion's operand, not (char)((ushort)x). An
        // out-of-range constant operand still needs the unchecked spelling.
        if (value is Convert { IsChecked: false, IsUnsigned: false } conv && TypeFamilies.SameWidth(conv.Target, target))
            return conv.Operand is Constant { Value: int or long } convConst
                ? NumericConstant(convConst, target!)
                : $"({TypeText(target!)}){Operand(conv.Operand)}";
        return $"({TypeText(target!)}){Operand(value)}";
    }

    /// <summary>
    /// An integer constant rendered for a numeric target: bare when in range (C#
    /// converts it implicitly), reinterpreted with an unchecked cast when out of
    /// range (a negative into unsigned, a mask wider than the target — CS0031/
    /// CS0221 as a bare or plain-cast literal).
    /// </summary>
    string NumericConstant(Constant konst, TypeRef target)
    {
        long literal = konst.Value is int i ? i : (long)konst.Value!;
        return TypeFamilies.ConstantFits(literal, target)
            ? Expression(konst)
            : $"unchecked(({TypeText(target)})({Expression(konst)}))";
    }

    /// <summary>
    /// The C# type the rendered expression actually has. For an unsigned
    /// div/rem/shr the printer casts the operands to their unsigned type, so the
    /// rendered result is unsigned even though the node's ECMA binary-promotion
    /// ResultType keeps the signed operand type; reflect that so a boundary into
    /// the matching unsigned type does not redundantly re-cast.
    /// </summary>
    static TypeRef? EffectiveType(IrExpression value)
    {
        if (value is Binary binary)
        {
            // C# promotes every sub-int (byte/sbyte/short/ushort/char) binary
            // arithmetic/bitwise/shift result to int: `a - b` over two chars is
            // typed `int`, never `char`. The IR keeps the narrow operand type,
            // so report int here — otherwise the missing-cast logic (CastValue)
            // sees an implicit char→uint and drops the (uint) cast C# requires,
            // emitting CS0266. A narrowing store back to a sub-int local always
            // carries its own conv (a Convert node, not a bare Binary), so this
            // never strips a cast the IL needs — it only adds the same-width
            // (uint)/(int) reinterpret, which emits no opcode.
            if (IsSubIntInteger(binary.ResultType))
                return TypeRef.CoreLib("System", "Int32");
            if (binary is { IsUnsigned: true, Kind: BinaryKind.Divide or BinaryKind.Remainder or BinaryKind.ShiftRight }
                && TypeFamilies.UnsignedCounterpart(binary.ResultType) is { } unsigned)
                return unsigned;
        }
        return value.ResultType;
    }

    /// <summary>A sub-int integer (byte/sbyte/short/ushort/char) — the primitives C# promotes to int in any binary numeric/bitwise/shift expression.</summary>
    static bool IsSubIntInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "Byte" or "SByte" or "Int16" or "UInt16" or "Char";

    static string BinaryOperator(Binary binary) => binary.Kind switch
    {
        BinaryKind.Add => "+",
        BinaryKind.Subtract => "-",
        BinaryKind.Multiply => "*",
        BinaryKind.Divide => "/",
        BinaryKind.Remainder => "%",
        BinaryKind.And => "&",
        BinaryKind.Or => "|",
        BinaryKind.Xor => "^",
        BinaryKind.ShiftLeft => "<<",
        _ => ">>",
    };

    static string ComparisonOperator(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equal => "==",
        ComparisonKind.NotEqual => "!=",
        ComparisonKind.LessThan => "<",
        ComparisonKind.LessThanOrEqual => "<=",
        ComparisonKind.GreaterThan => ">",
        _ => ">=",
    };
}
