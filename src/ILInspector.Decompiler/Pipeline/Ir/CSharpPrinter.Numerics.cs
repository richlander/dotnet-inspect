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
        // A checked binary already inside a checked context drops its own wrapper
        // (the enclosing checked covers it); only the outermost one wraps. Set the
        // context around the operands so nested checked nodes collapse into ours.
        bool enclosingChecked = _checkedContext;
        if (binary.IsChecked)
        {
            _checkedContext = true;
            try
            {
                return BinaryBody(binary, wrap: !enclosingChecked, uncheckedOverflow: false);
            }
            finally
            {
                _checkedContext = enclosingChecked;
            }
        }
        // The symmetric insert: a plain (unchecked) overflow-prone add/sub/mul
        // spelled inside a checked region would silently acquire `.ovf` semantics on
        // recompile — `checked(a + unchecked(b * 2))` recompiles the inner `mul` as
        // `mul.ovf`. Wrap it in `unchecked(...)` and clear the context so its
        // operands recompile plain (mirroring how the checked path manages the
        // context, so a nested checked node inside re-arms its own wrapper).
        bool uncheckedOverflow = enclosingChecked && IsPlainOverflowProneArithmetic(binary);
        if (uncheckedOverflow)
            _checkedContext = false;
        try
        {
            return BinaryBody(binary, wrap: false, uncheckedOverflow: uncheckedOverflow);
        }
        finally
        {
            _checkedContext = enclosingChecked;
        }
    }

    /// <summary>
    /// A plain (non-overflow) integer <c>+</c>/<c>-</c>/<c>*</c> whose opcode would
    /// silently flip to its <c>.ovf</c> form if recompiled inside a lexical
    /// <c>checked</c> region. The check is on the result stack family, so a float
    /// add (which has no overflow form) is excluded.
    /// </summary>
    static bool IsPlainOverflowProneArithmetic(Binary binary)
        => !binary.IsChecked
            && binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply
            && TypeFamilies.IsInteger(binary.ResultType);

    /// <summary>
    /// True when <paramref name="type"/> can only be an enum where it meets an
    /// integer operand: a named definition with no primitive stack family that
    /// the shape map does not class as a reference or (non-enum) struct. A
    /// cross-assembly enum loads no definition, so it resolves to
    /// <see cref="TypeShape.Unknown"/> — this structural test, not a shape
    /// lookup, is what recognizes it. Callers pair it with an integer sibling:
    /// IL verification admits no other value type as an integer-op operand, so
    /// the pairing is the proof it is an enum.
    /// </summary>
    bool IsEnumLikeInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition }
            && TypeFamilies.Of(type) is null
            && _function.TypeShapes.GetValueOrDefault(type) is not (TypeShape.Reference or TypeShape.ValueType);

    /// <summary>
    /// Wraps a synthesized same-width integer reinterpret cast — <c>(uint)x</c>,
    /// <c>(int)x</c>, <c>(EnumType)x</c> — in <c>unchecked(...)</c> when it is
    /// emitted inside a lexical <c>checked</c> region (or when <paramref name="force"/>
    /// is set, for an out-of-range constant cast). Such a cast recompiles to a
    /// <c>conv.ovf.*.un</c> it never had if it falls inside a <c>checked</c> context,
    /// silently adding an overflow check; the wrapper keeps the reinterpretation
    /// range-check-free. A no-op outside a checked region.
    /// </summary>
    string CheckedSafeCast(string castText, bool force = false)
        => _checkedContext || force ? $"unchecked({castText})" : castText;

    /// <summary>
    /// Casts an integer operand to the enum type it is compared or combined with
    /// — <c>(MethodAttributes)access</c> — so an enum-vs-integer comparison or
    /// bitwise op type-checks (CS0019). The cast reinterprets the integer bits
    /// the IL already carries as the enum, so it is faithful. A negative literal
    /// is parenthesized (CS0075), mirroring <see cref="CastValue"/>'s enum path.
    /// </summary>
    string EnumIntegerCast(IrExpression value, TypeRef enumType)
    {
        bool negativeLiteral = value is Constant { Value: int iv } && iv < 0
            || value is Constant { Value: long lv } && lv < 0;
        return CheckedSafeCast($"({TypeText(enumType)}){(negativeLiteral ? $"({Operand(value)})" : Operand(value))}");
    }

    string BinaryBody(Binary binary, bool wrap, bool uncheckedOverflow)
    {
        // A bitwise &/|/^ of an enum and an integer (`method.Attributes & 7`) is
        // CS0019 though the IL combines the shared underlying integer; cast the
        // integer operand to the enum type. A cross-assembly enum is unresolved
        // (TypeShape.Unknown), so this structural test is what catches it. A
        // bitwise op is never checked, so it never needs the `wrap` form.
        if (binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor)
        {
            if (IsEnumLikeInteger(binary.Left.ResultType) && TypeFamilies.IsInteger(binary.Right.ResultType))
                return $"{Operand(binary.Left)} {BinaryOperator(binary)} {EnumIntegerCast(binary.Right, binary.Left.ResultType!)}";
            if (IsEnumLikeInteger(binary.Right.ResultType) && TypeFamilies.IsInteger(binary.Left.ResultType))
                return $"{EnumIntegerCast(binary.Left, binary.Right.ResultType!)} {BinaryOperator(binary)} {Operand(binary.Right)}";
        }
        // div.un/rem.un compute on unsigned operands; shr.un shifts an
        // unsigned left operand. Operands that are already unsigned (or
        // float, where .un means unordered, not unsigned) print plain.
        bool castBoth = binary.IsUnsigned && binary.Kind is BinaryKind.Divide or BinaryKind.Remainder;
        bool castLeft = castBoth || (binary.IsUnsigned && binary.Kind is BinaryKind.ShiftRight);
        // A bitwise &/|/^ — or an unchecked +/-/* at 64-bit/native width — between
        // a signed and an unsigned integer of the same stack width has no C#
        // common type (CS0019/CS0034), even though the IL op is sign-neutral at
        // that width. Reinterpret the signed operand as unsigned — `S_0 | (ulong)S_1`,
        // `count * (nuint)stride` — a no-op same-width cast, so the opcode and its
        // stack width are unchanged.
        bool mixedSign = MixedSignBitwise(binary) || MixedSignArithmetic(binary);
        // A constant +/-/* whose subtree reinterprets an out-of-range signed
        // constant as unsigned (e.g. `(uint)-1 + 1`) is a C# *constant expression*:
        // the compiler evaluates it in a checked context (even unchecked add/sub/mul
        // of constants), so the cast and any arithmetic overflow are CS0220/CS0221
        // errors unless an enclosing `unchecked(...)` covers them. Wrap the
        // *outermost* such constant binary once and drop the inner/per-operand
        // `unchecked` so the whole expression is covered without nesting.
        bool parentWraps = binary.Parent is Binary parent && IsUncheckedConstantArithmetic(parent);
        bool fixedWidthConstantOverflow = SubtreeOverflowsFixedWidthConstantArithmetic(binary);
        bool uncheckedConstant = !wrap && !uncheckedOverflow && !parentWraps && IsUncheckedConstantArithmetic(binary);
        bool covered = uncheckedConstant || uncheckedOverflow || parentWraps;
        bool preserveUnsignedConstants = fixedWidthConstantOverflow
            && !SubtreeReinterpretsOutOfRangeConstant(binary)
            && covered
            && IsIntegerConstantExpression(binary)
            && IsUnsignedFixedWidthInteger(EffectiveType(binary))
            && !mixedSign;
        string left = mixedSign ? BitwiseUnsignedOperand(binary.Left, wrapConstantCast: !covered)
            : castLeft ? UnsignedOperand(binary.Left)
            : preserveUnsignedConstants ? UnsignedConstantArithmeticOperand(binary.Left, EffectiveType(binary))
            : Operand(binary.Left);
        bool isShift = binary.Kind is BinaryKind.ShiftLeft or BinaryKind.ShiftRight;
        string right = isShift ? ShiftCount(binary)
            : mixedSign ? BitwiseUnsignedOperand(binary.Right, wrapConstantCast: !covered)
            : castBoth ? UnsignedOperand(binary.Right)
            : preserveUnsignedConstants ? UnsignedConstantArithmeticOperand(binary.Right, EffectiveType(binary))
            : Operand(binary.Right);
        string text = $"{left} {BinaryOperator(binary)} {right}";
        // add.ovf/sub.ovf/mul.ovf (and their .un forms) carry an overflow check
        // the default (unchecked) C# context would drop — spell it explicitly so
        // the recompiled IL keeps the .ovf opcode. Wrap only the outermost checked
        // node (wrap); a nested one is already covered by the enclosing context.
        if (wrap)
            return $"checked({text})";
        return uncheckedConstant || uncheckedOverflow ? $"unchecked({text})" : text;
    }

    string? EnumArithmeticText(Binary binary)
    {
        if (EnumArithmeticUnderlyingType(binary) is not { } target)
            return null;

        var leftEnumUnderlying = EnumUnderlyingType(binary.Left.ResultType);
        var rightEnumUnderlying = EnumUnderlyingType(binary.Right.ResultType);
        string left = leftEnumUnderlying is not null ? CastValue(binary.Left, target) : Operand(binary.Left);
        string right = rightEnumUnderlying is not null ? CastValue(binary.Right, target) : Operand(binary.Right);
        return $"{left} {BinaryOperator(binary)} {right}";
    }

    TypeRef? EnumArithmeticUnderlyingType(Binary binary)
    {
        if (binary.Kind is not (BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply
            or BinaryKind.Divide or BinaryKind.Remainder))
        {
            return null;
        }

        var leftEnumUnderlying = EnumUnderlyingType(binary.Left.ResultType);
        var rightEnumUnderlying = EnumUnderlyingType(binary.Right.ResultType);
        if (leftEnumUnderlying is null && rightEnumUnderlying is null)
            return null;
        var target = leftEnumUnderlying ?? rightEnumUnderlying;
        return target is not null && TypeFamilies.IsIntegerLike(target) ? target : null;
    }

    /// <summary>
    /// An unchecked <c>+</c>/<c>-</c>/<c>*</c> that is a C# integer constant
    /// expression whose subtree either reinterprets an out-of-range signed constant
    /// as unsigned or overflows its fixed-width integer result. Such an expression
    /// is evaluated in a checked constant context and must sit inside an
    /// <c>unchecked(...)</c> to compile. Used to wrap the outermost such binary
    /// (and detect that a parent already wraps it).
    /// </summary>
    bool IsUncheckedConstantArithmetic(Binary binary)
        => !binary.IsChecked
            && binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply
            && IsIntegerConstantExpression(binary)
            && (SubtreeReinterpretsOutOfRangeConstant(binary)
                || SubtreeOverflowsFixedWidthConstantArithmetic(binary));

    /// <summary>Whether a mixed-sign arithmetic reconciliation or an explicit cast anywhere in the constant subtree reinterprets an out-of-range signed constant as unsigned — the cast that needs <c>unchecked</c>.</summary>
    bool SubtreeReinterpretsOutOfRangeConstant(IrExpression expression)
    {
        switch (expression)
        {
            case Convert { IsChecked: false, Target: var target } convert:
                // An explicit cast of an out-of-range constant to unsigned, e.g. `(uint)(-1)`.
                if (TypeFamilies.IsUnsignedIntegerPrimitive(target)
                    && TryGetIntegerConstant(convert.Operand, out long value)
                    && !TypeFamilies.ConstantFits(value, target))
                    return true;
                return SubtreeReinterpretsOutOfRangeConstant(convert.Operand);
            case Binary binary:
                if (MixedSignArithmetic(binary)
                    && (IsOutOfRangeUnsignedConstant(binary.Left) || IsOutOfRangeUnsignedConstant(binary.Right)))
                    return true;
                return SubtreeReinterpretsOutOfRangeConstant(binary.Left)
                    || SubtreeReinterpretsOutOfRangeConstant(binary.Right);
            default:
                return false;
        }
    }

    /// <summary>Whether any unchecked fixed-width integer constant arithmetic in the subtree overflows the type it renders as.</summary>
    bool SubtreeOverflowsFixedWidthConstantArithmetic(IrExpression expression)
    {
        switch (expression)
        {
            case Convert { IsChecked: false } convert:
                return SubtreeOverflowsFixedWidthConstantArithmetic(convert.Operand);
            case Binary binary:
                return TryEvaluateIntegerConstantExpression(binary, out _, out bool overflow) && overflow
                    || SubtreeOverflowsFixedWidthConstantArithmetic(binary.Left)
                    || SubtreeOverflowsFixedWidthConstantArithmetic(binary.Right);
            default:
                return false;
        }
    }

    bool TryEvaluateIntegerConstantExpression(IrExpression expression, out System.Numerics.BigInteger value, out bool overflow)
    {
        switch (expression)
        {
            case Convert { IsChecked: false } convert:
                return TryEvaluateIntegerConstantExpression(convert.Operand, out value, out overflow);
            case Constant constant:
                return TryReadIntegerConstant(constant, out value, out overflow);
            case Binary { IsChecked: false, Kind: BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply } binary:
            {
                if (!TryEvaluateIntegerConstantExpression(binary.Left, out var left, out var leftOverflow)
                    || !TryEvaluateIntegerConstantExpression(binary.Right, out var right, out var rightOverflow)
                    || !TryGetIntegerRange(EffectiveType(binary), out var min, out var max))
                {
                    value = default;
                    overflow = false;
                    return false;
                }

                value = binary.Kind switch
                {
                    BinaryKind.Add => left + right,
                    BinaryKind.Subtract => left - right,
                    _ => left * right,
                };
                overflow = leftOverflow || rightOverflow || value < min || value > max;
                return true;
            }
            default:
                value = default;
                overflow = false;
                return false;
        }
    }

    static bool TryReadIntegerConstant(Constant constant, out System.Numerics.BigInteger value, out bool overflow)
    {
        value = constant.Value switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            _ => default,
        };
        overflow = false;
        return constant.Value is sbyte or byte or short or ushort or int or uint or long or ulong;
    }

    static bool TryGetIntegerRange(TypeRef? type, out System.Numerics.BigInteger min, out System.Numerics.BigInteger max)
    {
        if (type is not { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" })
        {
            min = max = default;
            return false;
        }

        switch (type.Name)
        {
            case "SByte":
                min = sbyte.MinValue; max = sbyte.MaxValue; return true;
            case "Byte":
                min = byte.MinValue; max = byte.MaxValue; return true;
            case "Int16":
                min = short.MinValue; max = short.MaxValue; return true;
            case "UInt16" or "Char":
                min = ushort.MinValue; max = ushort.MaxValue; return true;
            case "Int32":
                min = int.MinValue; max = int.MaxValue; return true;
            case "UInt32":
                min = uint.MinValue; max = uint.MaxValue; return true;
            case "Int64":
                min = long.MinValue; max = long.MaxValue; return true;
            case "UInt64":
                min = ulong.MinValue; max = ulong.MaxValue; return true;
            default:
                min = max = default;
                return false;
        }
    }

    string UnsignedConstantArithmeticOperand(IrExpression operand, TypeRef? target)
        => operand is Constant constant && target is not null && EffectiveType(constant)?.Equals(target) == true
            ? UnsignedConstantText(constant, target)
            : Operand(operand);

    static string UnsignedConstantText(Constant constant, TypeRef target)
        => target.Name switch
        {
            "UInt32" => $"{System.Convert.ToUInt32(constant.Value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture)}u",
            "UInt64" => $"{System.Convert.ToUInt64(constant.Value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture)}UL",
            _ => ConstantText(constant),
        };

    static bool IsUnsignedFixedWidthInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "UInt32" or "UInt64" };

    /// <summary>
    /// True when a bitwise <c>&amp;</c>/<c>|</c>/<c>^</c> has one signed and one
    /// unsigned integer operand of the same stack width (e.g. <c>ulong | long</c>).
    /// C# rejects that pair (CS0019) because neither converts to the other, yet
    /// the bit result is the same under either interpretation, so the printer
    /// reinterprets the signed operand as unsigned. Restricted to same-width
    /// signed/unsigned integer pairs — bool/char are excluded by
    /// <see cref="TypeFamilies.IsUnsignedIntegerPrimitive"/>.
    /// </summary>
    static bool MixedSignBitwise(Binary binary)
        => binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor
            && MixedSignSameWidthIntegers(binary);

    /// <summary>
    /// True when an <em>unchecked</em> <c>+</c>/<c>-</c>/<c>*</c> has one signed
    /// and one unsigned integer operand of the same stack width (e.g.
    /// <c>int * uint</c>, <c>nuint * nint</c>, <c>ulong - long</c>). Same
    /// reinterpret rationale as <see cref="MixedSignBitwise"/>: <c>add</c>/
    /// <c>sub</c>/<c>mul</c> are bit-identical for two's-complement signed and
    /// unsigned operands, so casting the signed side to unsigned reuses the same
    /// opcode at the same stack width — whereas the bare C# form binds to the wider
    /// common type (<c>int * uint</c> ⇒ <c>long</c>, a 64-bit <c>mul</c>) or has
    /// none at all (<c>ulong * long</c> ⇒ CS0019). <see cref="EffectiveType"/>
    /// reports this unsigned result so a nested parent (<c>1 + (int * uint)</c>)
    /// sees the unsigned operand and reconciles in turn. Checked operations are
    /// excluded: <c>add.ovf</c> vs <c>add.ovf.un</c> differ by signedness.
    /// </summary>
    static bool MixedSignArithmetic(Binary binary)
        => !binary.IsChecked
            && binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply
            && MixedSignSameWidthIntegers(binary);

    /// <summary>
    /// True when an integer arithmetic (unchecked <c>+</c>/<c>-</c>/<c>*</c>) or
    /// bitwise (<c>&amp;</c>/<c>|</c>/<c>^</c>) binary <em>renders</em> unsigned:
    /// both operands are the same-width <em>wide</em> integer (int/uint, long/ulong,
    /// nint/nuint — sub-int byte/short/char excluded, since they promote to int)
    /// and at least one is unsigned. The printer leaves a both-unsigned pair bare
    /// and reconciles a mixed-sign pair to unsigned, so either way the rendered
    /// result is unsigned. Drives <see cref="EffectiveType"/> only; deliberately
    /// conservative, so a case it misses stays at its ECMA <c>ResultType</c> rather
    /// than over-claiming an unsigned rendering.
    /// </summary>
    static bool RendersUnsigned(Binary binary)
    {
        bool arith = !binary.IsChecked && binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply;
        bool bitwise = binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor;
        if (!arith && !bitwise)
            return false;
        var left = EffectiveType(binary.Left);
        var right = EffectiveType(binary.Right);
        return IsWideInteger(left) && IsWideInteger(right)
            && TypeFamilies.Of(left) == TypeFamilies.Of(right)
            && (TypeFamilies.IsUnsignedIntegerPrimitive(left) || TypeFamilies.IsUnsignedIntegerPrimitive(right));
    }

    /// <summary>The 4-byte, 8-byte, and native integer primitives — the widths C# does not promote to a wider type (unlike sub-int byte/short/char, which promote to int).</summary>
    static bool IsWideInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "Int32" or "UInt32" or "Int64" or "UInt64" or "IntPtr" or "UIntPtr";

    /// <summary>
    /// The operand-type test shared by <see cref="MixedSignBitwise"/> and
    /// <see cref="MixedSignArithmetic"/>: one signed and one unsigned integer of
    /// the same stack width (e.g. <c>ulong</c>/<c>long</c>, <c>uint</c>/<c>int</c>),
    /// the pair C# rejects (or silently widens) but a sign-neutral IL op permits.
    /// bool/char are excluded by <see cref="TypeFamilies.IsUnsignedIntegerPrimitive"/>.
    /// </summary>
    static bool MixedSignSameWidthIntegers(Binary binary)
        => MixedSignSameWidthIntegers(binary.Left, binary.Right);

    /// <summary>
    /// One signed and one unsigned integer operand of the same stack width (e.g.
    /// <c>ulong</c>/<c>long</c>, <c>nuint</c>/<c>nint</c>) — the pair C# rejects
    /// (CS0034: the operator is ambiguous; neither type converts to the other) but
    /// a sign-neutral IL op permits. Shared by the binary, comparison, and
    /// compound-assignment paths.
    /// </summary>
    static bool MixedSignSameWidthIntegers(IrExpression leftOperand, IrExpression rightOperand)
    {
        var left = EffectiveType(leftOperand);
        var right = EffectiveType(rightOperand);
        // Both operands must be WIDE integers: a sub-int (byte/short/ushort/char)
        // promotes to int in C#, so `ushort - int` is already `int - int` and
        // needs no reconciliation — treating the sub-int as the unsigned partner
        // would wrongly cast the int operand to uint (`S_0 - (uint)S_1`, CS0266).
        if (!IsWideInteger(left) || !IsWideInteger(right) || TypeFamilies.Of(left) != TypeFamilies.Of(right))
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
    string BitwiseUnsignedOperand(IrExpression operand, bool wrapConstantCast = true)
    {
        var unsigned = TypeFamilies.UnsignedCounterpart(EffectiveType(operand));
        if (unsigned is null)
            return Operand(operand);
        string cast = $"({TypeText(unsigned)}){Operand(operand)}";
        bool constantOutOfRange = wrapConstantCast && TryGetIntegerConstant(operand, out long value) && !TypeFamilies.ConstantFits(value, unsigned);
        return CheckedSafeCast(cast, force: constantOutOfRange);
    }

    /// <summary>
    /// An integer constant whose value does not fit its unsigned counterpart (e.g.
    /// a negative signed constant reinterpreted as <c>uint</c>) — the operand whose
    /// cast is a compile-time overflow unless an enclosing <c>unchecked</c> covers it.
    /// </summary>
    bool IsOutOfRangeUnsignedConstant(IrExpression operand)
    {
        var unsigned = TypeFamilies.UnsignedCounterpart(EffectiveType(operand));
        return unsigned is not null
            && TryGetIntegerConstant(operand, out long value)
            && !TypeFamilies.ConstantFits(value, unsigned);
    }

    /// <summary>Whether an operand reduces (through unchecked conv nodes, and nested unchecked +/-/* of constants) to a C# integer constant expression.</summary>
    static bool IsIntegerConstantExpression(IrExpression expression)
    {
        while (expression is Convert { IsChecked: false } convert)
            expression = convert.Operand;
        return expression is Constant { Value: sbyte or byte or short or ushort or int or uint or long or ulong }
            || (expression is Binary { IsChecked: false, Kind: BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply } binary
                && IsIntegerConstantExpression(binary.Left)
                && IsIntegerConstantExpression(binary.Right));
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
        => NeedsIntShiftCast(EffectiveType(count)) ? CheckedSafeCast($"(int){Operand(count)}") : Operand(count);

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
        // An enum compared to an integer (`access == bestAccess`,
        // `methodAccess == 6`) is CS0019 though the IL compares their shared
        // underlying integer; cast the integer operand to the enum type. A
        // cross-assembly enum is unresolved (TypeShape.Unknown), so this is the
        // path that fixes it.
        if (IsEnumLikeInteger(left.ResultType) && TypeFamilies.IsInteger(right.ResultType))
            return $"{Operand(left)} {ComparisonOperator(kind)} {EnumIntegerCast(right, left.ResultType!)}";
        if (IsEnumLikeInteger(right.ResultType) && TypeFamilies.IsInteger(left.ResultType))
            return $"{EnumIntegerCast(left, right.ResultType!)} {ComparisonOperator(kind)} {Operand(right)}";
        // An equality test between a same-width signed/unsigned integer pair
        // (`ulong != (long)i`, `nuint == nint`) has no C# common type (CS0034),
        // yet `ceq`/`bne.un` compare the raw bits regardless of sign. Reinterpret
        // the signed operand as unsigned — a same-width no-op cast — so both sides
        // share a type. Ordering comparisons are excluded: a signed `clt`/`cgt`
        // would change meaning under an unsigned reinterpret.
        if (kind is ComparisonKind.Equal or ComparisonKind.NotEqual
            && MixedSignSameWidthIntegers(left, right))
        {
            return $"{BitwiseUnsignedOperand(left)} {ComparisonOperator(kind)} {BitwiseUnsignedOperand(right)}";
        }
        // A signed ordering (`clt`/`cgt`) between a same-width signed/unsigned pair
        // also has no C# common type (CS0034), but unlike equality the sign matters:
        // the IL compares as signed, so reinterpret the unsigned operand to its
        // signed counterpart (a same-width no-op cast) rather than to unsigned.
        // The unsigned ordering (`clt.un`/`cgt.un`) reconciles to unsigned through
        // the UnsignedOperand fallthrough below.
        if (kind is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual
            && !isUnsigned
            && MixedSignSameWidthIntegers(left, right))
        {
            return $"{SignedOperand(left)} {ComparisonOperator(kind)} {SignedOperand(right)}";
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
    string UnsignedOperand(IrExpression operand, bool checkedSafe = true)
    {
        string? cast = TypeFamilies.UnsignedCastKeyword(operand.ResultType);
        if (cast is null)
            return Operand(operand);
        string text = $"({cast}){Operand(operand)}";
        return checkedSafe ? CheckedSafeCast(text) : text;
    }

    /// <summary>
    /// Casts a wide unsigned-integer operand to its signed counterpart (uint→int,
    /// ulong→long, nuint→nint) for a signed mixed-sign ordering reconciliation;
    /// already-signed and unknown-typed operands print plain. An unsigned constant
    /// outside the signed range takes the <c>unchecked</c> spelling so the
    /// out-of-range cast is legal (CS0221).
    /// </summary>
    string SignedOperand(IrExpression operand)
    {
        // Key off EffectiveType, not ResultType: a nested mixed-sign subtree
        // (`long + ulong`) has a signed ResultType but renders unsigned
        // (`(ulong)a + b`), so the signed reinterpret must wrap the rendered
        // unsigned text — matching BitwiseUnsignedOperand's use of EffectiveType.
        var signed = TypeFamilies.SignedCounterpart(EffectiveType(operand));
        if (signed is null)
            return Operand(operand);
        string cast = $"({TypeText(signed)}){Operand(operand)}";
        // A constant unsigned operand's value may exceed the signed range
        // (e.g. (long)ulong.MaxValue), which is CS0221 without unchecked; the
        // unsigned value, not the peeled signed value, is what overflows, so wrap
        // any constant operand defensively (a no-op for a fitting constant).
        return CheckedSafeCast(cast, force: TryGetIntegerConstant(operand, out _));
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
        if (target is { Kind: TypeRefKind.Pointer }
            && EffectiveType(value) is { Kind: TypeRefKind.Pointer } pointerSource
            && value is not Constant { Value: null }
            && !target.Equals(pointerSource))
        {
            return $"({TypeText(target)}){Operand(value)}";
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
        if (value is Binary enumArithmetic
            && target is { } enumArithmeticTarget
            && EnumArithmeticUnderlyingType(enumArithmetic)?.Equals(enumArithmeticTarget) == true)
        {
            return EnumArithmeticText(enumArithmetic) ?? Expression(value);
        }
        if (target is { } primitiveTarget
            && TypeFamilies.IsIntegerLike(primitiveTarget)
            && EnumUnderlyingType(EffectiveType(value)) is { } underlying
            && underlying.Equals(primitiveTarget))
        {
            return $"({TypeText(primitiveTarget)}){Operand(value)}";
        }
        // The same cast, for a cross-assembly enum. ClassifyShape only sees types
        // defined in the inspected assembly, so a framework enum like
        // StringComparison resolves to Unknown rather than Enum and the branch
        // above does not fire. But type-safe IL only puts a bare integer constant
        // in a non-primitive named-type position when that type is an enum (a
        // reference target carries a box, a struct a construction), so the
        // (Enum)value cast is faithful — it recompiles to the same ldc.i4 — and is
        // needed: the bare literal makes overload resolution pick a wrong overload
        // (e.g. string.Equals(string, StringComparison) falls back to the static
        // object.Equals(object, object), CS0176). The member name is unavailable
        // without loading the defining assembly, so the cast is the best honest
        // spelling. Constants only: a non-constant integer into such a position is
        // rarer and not needed for the validity defects this targets.
        if (value is Constant { Value: int or long }
            && target is { Kind: TypeRefKind.Definition, Name: not "Boolean" } unknownEnum
            && _function.TypeShapes.GetValueOrDefault(unknownEnum) == TypeShape.Unknown
            && !TypeFamilies.IsNumericPrimitive(unknownEnum)
            && EffectiveType(value) is { } unknownEnumSource && TypeFamilies.IsIntegerLike(unknownEnumSource))
        {
            bool negativeLiteral = value is Constant { Value: int iv } && iv < 0
                || value is Constant { Value: long lv } && lv < 0;
            return $"({TypeText(unknownEnum)}){(negativeLiteral ? $"({Operand(value)})" : Operand(value))}";
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

    string ConditionalText(Conditional conditional)
    {
        var target = conditional.MergedType;
        // `?:` is right-associative, so a conditional in the condition position
        // reassociates without parentheses (`(a ? b : c) ? d : e` would reparse
        // as `a ? b : (c ? d : e)`). The arms render through Operand, which
        // already wraps a nested conditional where needed.
        var condition = conditional.Condition is Conditional
            ? $"({Condition(conditional.Condition)})"
            : Condition(conditional.Condition);
        return $"{condition} ? {ConditionalArm(conditional.WhenTrue, target)} : {ConditionalArm(conditional.WhenFalse, target)}";
    }

    string ConditionalArm(IrExpression arm, TypeRef? target)
        => target is { } intTarget && TypeFamilies.IsIntegerLike(intTarget)
            && EffectiveType(arm) is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
                ? $"({Condition(arm)} ? 1 : 0)"
                : Operand(arm);

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
            // An integer arithmetic/bitwise binary renders unsigned whenever an
            // operand renders unsigned at the same width: the printer either leaves
            // a both-unsigned pair bare (`uint + uint`) or reconciles a mixed-sign
            // pair by reinterpreting the signed side (`(uint)x + count`). Either
            // way the rendered result is unsigned even though the ECMA ResultType
            // keeps a signed operand type. Report it, so a parent (a nested
            // `1 + (int * uint)`, or a CastValue boundary into a signed target)
            // sees the unsigned type and reconciles or casts in turn — the
            // propagation that resolves the `int op uint` family up the whole tree.
            if (RendersUnsigned(binary))
                return TypeFamilies.IsUnsignedIntegerPrimitive(binary.ResultType)
                    ? binary.ResultType
                    : TypeFamilies.UnsignedCounterpart(binary.ResultType);
        }
        return value.ResultType;
    }

    TypeRef? EnumUnderlyingType(TypeRef? type)
        => type is not null && _function.EnumUnderlyingTypes.TryGetValue(type, out var underlying)
            ? underlying
            : null;

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
