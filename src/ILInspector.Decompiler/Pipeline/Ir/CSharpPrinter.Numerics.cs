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
    /// Renders pointer additive arithmetic (<c>p + i</c>, <c>p - i</c>,
    /// <c>a - b</c>) without C#'s implicit <c>sizeof(element)</c> scaling. An IL
    /// pointer <c>add</c>/<c>sub</c> is byte-address arithmetic and already
    /// carries the explicit <c>* sizeof(T)</c> the source compiled to (a pointer
    /// difference likewise divides by <c>sizeof(T)</c>). Spelling it with a C#
    /// pointer <c>+</c>/<c>-</c> — which scales the integer operand by
    /// <c>sizeof(element)</c> again — would double-scale and silently compute the
    /// wrong value. Casting the pointer operand(s) to <c>byte*</c> makes the
    /// offset a raw byte count, reproducing the IL exactly; for
    /// <c>pointer ± integer</c> the result is cast back to the pointer's own type.
    /// Returns false (leaving the default integer spelling) when the node is not a
    /// pointer additive form — including <c>pointer + pointer</c> and
    /// <c>integer - pointer</c>, which are not valid pointer arithmetic.
    /// </summary>
    bool TryPointerArithmeticText(Binary binary, out string text)
    {
        text = "";
        if (binary.Kind is BinaryKind.Divide
            && TryPointerDifferenceDivisionText(binary, out text))
        {
            return true;
        }

        bool leftPointer = binary.Left.ResultType is { Kind: TypeRefKind.Pointer };
        bool rightPointer = binary.Right.ResultType is { Kind: TypeRefKind.Pointer };
        if (!leftPointer && !rightPointer)
            return false;

        // pointer - pointer: the C# difference of two `byte*` is the byte count the
        // IL `sub` computes, and the outer `/ sizeof(T)` recovers the element count.
        if (leftPointer && rightPointer)
        {
            if (binary.Kind is not BinaryKind.Subtract)
                return false;   // `pointer + pointer` is not valid pointer arithmetic
            // Two pointers of the *same* one-byte-element type already difference in
            // bytes, so the default `a - b` spelling is both faithful and legal —
            // leave it untouched. Any other pairing (a wider element, or two
            // different pointer types, which C# rejects with CS0019) is routed
            // through `byte*`.
            if (IsByteWidthPointer(binary.Left.ResultType)
                && binary.Left.ResultType!.ElementType!.Equals(binary.Right.ResultType?.ElementType))
                return false;
            text = $"{BytePointerOperand(binary.Left)} - {BytePointerOperand(binary.Right)}";
            return true;
        }

        // `integer - pointer` is not valid pointer arithmetic; only a left pointer
        // subtracts an offset. Addition is commutative, so either side may be the
        // pointer.
        if (binary.Kind is BinaryKind.Subtract && !leftPointer)
            return false;

        IrExpression pointer = leftPointer ? binary.Left : binary.Right;
        IrExpression offset = leftPointer ? binary.Right : binary.Left;
        if (pointer.ResultType is { Kind: TypeRefKind.Pointer, ElementType: { } element }
            && TryScaledPointerIndex(offset, element, out var index))
        {
            var demand = CSharpPrecedence.Of(binary);
            text = leftPointer
                ? $"{Operand(pointer)} {BinaryOperator(binary)} {BinaryOperand(index, demand, rightSide: true)}"
                : $"{BinaryOperand(index, demand, rightSide: false)} {BinaryOperator(binary)} {Operand(pointer)}";
            return true;
        }
        // A pointer to a one-byte element (byte*, sbyte*, bool*) is not scaled by
        // the IL — `sizeof(T) == 1` leaves no `* sizeof(T)` to fold — so C#'s
        // pointer `+`/`-` already reproduces the IL byte offset. Leave it.
        if (IsByteWidthPointer(pointer.ResultType))
            return false;
        string inner = leftPointer
            ? $"{BytePointerOperand(pointer)} {BinaryOperator(binary)} {Operand(offset)}"
            : $"{Operand(offset)} {BinaryOperator(binary)} {BytePointerOperand(pointer)}";
        text = $"({TypeText(pointer.ResultType!)})({inner})";
        return true;
    }

    bool TryPointerDifferenceDivisionText(Binary binary, out string text)
    {
        text = "";
        if (binary.Left is not Binary { Kind: BinaryKind.Subtract } difference
            || difference.Left.ResultType is not { Kind: TypeRefKind.Pointer, ElementType: { } leftElement } leftPointer
            || difference.Right.ResultType is not { Kind: TypeRefKind.Pointer } rightPointer
            || !leftPointer.Equals(rightPointer)
            || ByteSize(leftElement) is not { } elementSize
            || !IsConstant(binary.Right, elementSize))
        {
            return false;
        }

        text = $"({Operand(difference.Left)} - {Operand(difference.Right)})";
        return true;
    }

    /// <summary>
    /// A pointer whose element occupies a single byte (<c>byte*</c>, <c>sbyte*</c>,
    /// <c>bool*</c>). C# pointer arithmetic on such a pointer scales by one, so it
    /// already matches the IL byte-address math with no <c>byte*</c> rewrite
    /// needed. <c>void*</c> is deliberately excluded: C# defines no arithmetic on
    /// it (CS0242), so a <c>void*</c> offset must be routed through <c>byte*</c>
    /// rather than left as an illegal <c>p + i</c>.
    /// </summary>
    static bool IsByteWidthPointer(TypeRef? pointer)
        => pointer is { Kind: TypeRefKind.Pointer, ElementType: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Byte" or "SByte" or "Boolean" } };

    /// <summary>
    /// Spells a pointer operand of byte-address arithmetic as <c>(byte*)expr</c>,
    /// so a C# pointer <c>+</c>/<c>-</c> treats the sibling offset as a raw byte
    /// count rather than scaling it by <c>sizeof(element)</c>.
    /// </summary>
    string BytePointerOperand(IrExpression pointer) => $"(byte*){Operand(pointer)}";

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
    ///
    /// The cast text renders THROUGH the thunk with the checked context
    /// CLEARED whenever the wrapper will apply: nested checked nodes
    /// (<c>add.ovf</c>/<c>conv.ovf</c>) omit their own <c>checked(...)</c>
    /// under an ambient checked region, so wrapping their pre-rendered text in
    /// <c>unchecked(...)</c> silently turned their overflow checks off —
    /// <c>unchecked((uint)(a + b))</c> where the IL demands
    /// <c>unchecked((uint)checked(a + b))</c> (work-2302 review, both
    /// reviewers, product-path repro). Clearing the flag makes the nested
    /// nodes re-emit their explicit wrappers; only the reinterpret itself goes
    /// unchecked.
    /// </summary>
    string CheckedSafeCast(Func<string> renderCast, bool force = false)
    {
        if (!_checkedContext && !force)
            return renderCast();
        bool enclosing = _checkedContext;
        _checkedContext = false;
        string castText;
        try
        {
            castText = renderCast();
        }
        finally
        {
            _checkedContext = enclosing;
        }
        return $"unchecked({castText})";
    }

    /// <summary>
    /// Casts an integer operand to the enum type it is compared or combined with
    /// — <c>(MethodAttributes)access</c> — so an enum-vs-integer comparison or
    /// bitwise op type-checks (CS0019). The cast reinterprets the integer bits
    /// the IL already carries as the enum, so it is faithful. A negative literal
    /// is parenthesized (CS0075), mirroring <see cref="CoerceText"/>'s enum path.
    /// </summary>
    string EnumIntegerCast(IrExpression value, TypeRef enumType)
    {
        // A negative literal needs parentheses after the cast (CS0075); whether it
        // (or an out-of-range positive) also needs `unchecked` is decided entirely
        // by MayOverflowEnumBackingType against the backing width.
        bool negativeLiteral = value is Constant { Value: int iv } && iv < 0
            || value is Constant { Value: long lv } && lv < 0;
        bool forceUnchecked = MayOverflowEnumBackingType(value, enumType);
        return CheckedSafeEnumCast(
            value,
            enumType,
            () => $"({TypeText(enumType)}){(negativeLiteral ? $"({Operand(value)})" : Operand(value))}",
            force: forceUnchecked);
    }

    string CheckedSafeEnumCast(IrExpression value, TypeRef enumType, Func<string> renderCast, bool force = false)
    {
        if (force)
            return CheckedSafeCast(renderCast, force: true);
        // A bool composition is always 0/1, fitting every enum backing type.
        if (TypeFamilies.IsBoolean(EffectiveType(value)))
            return renderCast();
        var underlying = EnumUnderlyingType(enumType);
        return underlying is not null && !CSharpConversionRules.CheckedConversionCanThrow(EffectiveType(value), underlying)
            ? renderCast()
            : CheckedSafeCast(renderCast);
    }

    /// <summary>
    /// Renders an integer constant occupying an enum-typed position: the member
    /// name when the value resolves to a single named member, else the
    /// overflow-aware enum cast. The one name-or-cast rule for enum constants —
    /// <c>switch</c> labels, retyped constants, and enum-typed sinks all spell
    /// through here. The cast path re-types the payload to its raw integer so
    /// <see cref="EnumIntegerCast"/> renders the literal, not a recursive
    /// enum-typed spelling.
    /// </summary>
    string EnumConstantText(Constant constant, TypeRef enumType)
    {
        long value = constant.Value is int i ? i : (long)constant.Value!;
        if (EnumMemberName(new Constant(value, enumType)) is { } named)
            return named;
        var raw = constant.Value is int iv
            ? new Constant(iv, TypeRef.CoreLib("System", "Int32"))
            : new Constant(value, TypeRef.CoreLib("System", "Int64"));
        return EnumIntegerCast(raw, enumType);
    }

    /// <summary>
    /// Coerces a value meeting an enum-typed sibling or arm target — the shared
    /// decision behind bitwise/comparison operands, conditional and switch arms,
    /// coalesce right sides, and compound assignments. An integer-family value
    /// takes the enum cast (member name for a nameable constant); a bool — which
    /// shares the I4 stack family, so IL combines it with an enum freely while
    /// C# admits neither bare spelling (CS0019/CS0030) — composes the truthiness
    /// spelling with the cast: <c>(E)(cond ? 1 : 0)</c>. Null when the position
    /// needs no enum coercion, so the caller keeps its own spelling.
    /// </summary>
    string? TryCoerceEnumOperand(IrExpression value, TypeRef? enumSide)
    {
        if (enumSide is null || !IsEnumLikeInteger(enumSide))
            return null;
        if (TypeFamilies.IsBoolean(EffectiveType(value)))
            return CheckedSafeEnumCast(value, enumSide, () => $"({TypeText(enumSide)})({RenderedCondition(value).At(Precedence.NullCoalescing)} ? 1 : 0)");
        if (value.ResultType is not { } valueType || !TypeFamilies.IsIntegerLike(valueType))
            return null;
        return value is Constant { Value: int or long } konst
            ? EnumConstantText(konst, enumSide)
            : EnumIntegerCast(value, enumSide);
    }

    // True when a constant enum cast is CS0221 as a plain (checked) cast, so it must
    // be wrapped in `unchecked`: a value outside the backing type's range — negative
    // into an unsigned backing (`(U)(-1)`) or a magnitude a narrow backing cannot
    // hold (`(Tiny)128` for sbyte, `(Tiny)300` for byte). A value that fits (e.g. a
    // negative into a signed backing, `(Color)int.MinValue`) stays a bare cast.
    bool MayOverflowEnumBackingType(IrExpression value, TypeRef enumType)
    {
        if (!TryEnumCastLiteral(value, out long literal))
            return false;
        // A known backing width: the cast is a constant-expression conversion, so it
        // needs `unchecked` exactly when the value is out of that width's range.
        if (EnumUnderlyingType(enumType) is { } underlying)
            return !CSharpConversionRules.ConstantFits(literal, underlying);
        // A cross-assembly enum: the width is genuinely unknown and framework enums
        // are often byte/sbyte-backed [Flags], so conservatively assume the narrowest
        // (sbyte) backing and wrap a negative or a value above sbyte's max.
        if (_function.TypeShapes.GetValueOrDefault(enumType) == TypeShape.Unknown)
            return literal < 0 || literal > sbyte.MaxValue;
        // A same-assembly enum shape whose underlying is not in the map (e.g. no
        // `value__` field): assume C#'s default `int` backing, so an int-range value
        // (including a negative high-bit constant like int.MinValue) stays a bare
        // cast and only a genuinely out-of-int value is wrapped.
        return !CSharpConversionRules.ConstantFits(literal, TypeRef.CoreLib("System", "Int32"));
    }

    // The integer literal an enum cast will carry, seeing through a widening
    // `conv.i8`/`conv.u8` over an integer constant — a long-backed enum's small or
    // unsigned members lower as `ldc.i4; conv.i8`, so the raw operand is a `Convert`,
    // not a bare `Constant`. Returns false for a non-constant operand (a runtime
    // value never needs an out-of-range constant wrap).
    static bool TryEnumCastLiteral(IrExpression value, out long literal)
    {
        switch (value)
        {
            case Constant { Value: int i }:
                literal = i;
                return true;
            case Constant { Value: long l }:
                literal = l;
                return true;
            case Convert { Target: { } target } convert
                when TypeFamilies.IsIntegerLike(target) && TryEnumCastLiteral(convert.Operand, out var inner):
                // A widening to an unsigned target (`conv.u8`) zero-extends a 32-bit
                // source, carrying its unsigned bit pattern; a signed widening
                // (`conv.i8`) keeps the value. `Convert.IsUnsigned` marks a `.un`
                // overflow opcode, not the target signedness — so key off the target.
                literal = TypeFamilies.IsUnsignedIntegerPrimitive(target) && convert.Operand is Constant { Value: int u }
                    ? (uint)u
                    : inner;
                return true;
            default:
                literal = 0;
                return false;
        }
    }

    string BinaryBody(Binary binary, bool wrap, bool uncheckedOverflow)
    {
        // Pointer additive arithmetic is raw byte-address math in IL, but a C#
        // pointer `+`/`-` auto-scales the integer operand by `sizeof(element)`.
        // The IL already carries that scaling explicitly (the `* sizeof(T)` of
        // `p + i`, the `/ sizeof(T)` of a pointer difference), so spelling it with
        // C# pointer operators would scale a second time and silently compute the
        // wrong value. Render through `byte*` so the IL byte offset is reproduced
        // verbatim with no implicit scaling.
        if (binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Divide
            && TryPointerArithmeticText(binary, out string pointerText))
        {
            // A pointer `add.ovf`/`sub.ovf` carries an overflow check the default
            // (unchecked) C# context would drop, and a plain pointer add spelled
            // inside a checked region would silently acquire `.ovf` on recompile.
            // The `byte*` rewrite keeps the same `checked(...)`/`unchecked(...)`
            // wrapper the integer path below applies, so the overflow semantics
            // survive the re-spelling.
            if (wrap)
                return $"checked({pointerText})";
            return uncheckedOverflow ? $"unchecked({pointerText})" : pointerText;
        }
        // A bitwise &/|/^ of an enum and an integer (`method.Attributes & 7`) is
        // CS0019 though the IL combines the shared underlying integer; coerce the
        // integer operand to the enum type. A cross-assembly enum is unresolved
        // (TypeShape.Unknown), so the structural test inside the coercion is what
        // catches it. A bitwise op is never checked, so it never needs the `wrap`
        // form.
        if (binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor)
        {
            if (TryCoerceEnumOperand(binary.Right, binary.Left.ResultType) is { } coercedRight)
                return $"{Operand(binary.Left)} {BinaryOperator(binary)} {coercedRight}";
            if (TryCoerceEnumOperand(binary.Left, binary.Right.ResultType) is { } coercedLeft)
                return $"{coercedLeft} {BinaryOperator(binary)} {Operand(binary.Right)}";
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
        var demand = CSharpPrecedence.Of(binary);
        string left = mixedSign ? BitwiseUnsignedOperand(binary.Left, wrapConstantCast: !covered)
            : castLeft ? UnsignedOperand(binary.Left)
            : preserveUnsignedConstants ? UnsignedConstantArithmeticOperand(binary.Left, EffectiveType(binary))
            : BinaryOperand(binary.Left, demand, rightSide: false);
        bool isShift = binary.Kind is BinaryKind.ShiftLeft or BinaryKind.ShiftRight;
        string right = isShift ? ShiftCount(binary)
            : mixedSign ? BitwiseUnsignedOperand(binary.Right, wrapConstantCast: !covered)
            : castBoth ? UnsignedOperand(binary.Right)
            : preserveUnsignedConstants ? UnsignedConstantArithmeticOperand(binary.Right, EffectiveType(binary))
            : BinaryOperand(binary.Right, demand, rightSide: true);
        string text = $"{left} {BinaryOperator(binary)} {right}";
        // add.ovf/sub.ovf/mul.ovf (and their .un forms) carry an overflow check
        // the default (unchecked) C# context would drop — spell it explicitly so
        // the recompiled IL keeps the .ovf opcode. Wrap only the outermost checked
        // node (wrap); a nested one is already covered by the enclosing context.
        if (wrap)
            return $"checked({text})";
        return uncheckedConstant || uncheckedOverflow ? $"unchecked({text})" : text;
    }

    string BinaryOperand(IrExpression operand, Precedence parentPrecedence, bool rightSide)
    {
        // Binary operators are left-associative in C#. The right operand is the
        // hazard side for equal-precedence children (`a - (b - c)`, `a << (b << c)`),
        // so demand the next-tighter level there. The left side can associate
        // bare at equal precedence (`(a - b) - c`).
        var demand = rightSide ? TighterThan(parentPrecedence) : parentPrecedence;
        return RenderedExpression(operand).At(demand);
    }

    static Precedence TighterThan(Precedence precedence)
        => precedence == Precedence.Primary ? Precedence.Primary : (Precedence)((int)precedence + 1);

    string? EnumArithmeticText(Binary binary)
    {
        if (EnumArithmeticUnderlyingType(binary) is not { } target)
            return null;

        var leftEnumUnderlying = EnumUnderlyingType(binary.Left.ResultType);
        var rightEnumUnderlying = EnumUnderlyingType(binary.Right.ResultType);
        string left = leftEnumUnderlying is not null ? CoerceText(binary.Left, target) : Operand(binary.Left);
        string right = rightEnumUnderlying is not null ? CoerceText(binary.Right, target) : Operand(binary.Right);
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
                    && !CSharpConversionRules.ConstantFits(value, target))
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
    /// Whether a compound assignment's right operand must be cast to the lvalue
    /// type to bind. True when the lvalue and the right operand are a mixed-sign
    /// same-width integer pair (e.g. <c>nuint -= nint</c>, <c>ulong /= long</c>) —
    /// which has no C# common type (CS0034) — for an operator whose compound form
    /// is faithful under the cast: the sign-neutral unchecked <c>+</c>/<c>-</c>/
    /// <c>*</c> and bitwise <c>&amp;</c>/<c>|</c>/<c>^</c>, plus <c>/</c>/<c>%</c>
    /// when the IL opcode's signedness already matches the lvalue (the compound
    /// runs in the lvalue's type, so casting the right operand cannot flip
    /// <c>div</c>↔<c>div.un</c>). Checked <c>.ovf</c> compounds stay plain.
    /// </summary>
    bool NeedsCompoundSignCast(Binary binary, TypeRef? lvalueType)
    {
        bool signNeutral = (!binary.IsChecked && binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply)
            || binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor;
        bool divRemFaithful = binary.Kind is BinaryKind.Divide or BinaryKind.Remainder
            && binary.IsUnsigned == TypeFamilies.IsUnsignedIntegerPrimitive(lvalueType);
        return (signNeutral || divRemFaithful)
            && MixedSignSameWidthIntegers(lvalueType, EffectiveType(binary.Right));
    }

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
        => MixedSignSameWidthIntegers(EffectiveType(leftOperand), EffectiveType(rightOperand));

    /// <summary>
    /// The type-level core of the mixed-sign same-width test: one signed and one
    /// unsigned wide integer of the same stack width. The compound-assignment path
    /// uses this directly with the resolved lvalue type, which an indirect store's
    /// <c>ldind.i</c>-typed target read does not carry.
    /// </summary>
    static bool MixedSignSameWidthIntegers(TypeRef? left, TypeRef? right)
    {
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
        bool constantOutOfRange = wrapConstantCast && TryGetIntegerConstant(operand, out long value) && !CSharpConversionRules.ConstantFits(value, unsigned);
        return CheckedSafeCast(() => $"({TypeText(unsigned)}){Operand(operand)}", force: constantOutOfRange);
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
            && !CSharpConversionRules.ConstantFits(value, unsigned);
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
        => NeedsIntShiftCast(EffectiveType(count)) ? CheckedSafeCast(() => $"(int){Operand(count)}") : Operand(count);

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
            if (IsInstanceNullTestText(operand, isNotNull: kind == ComparisonKind.NotEqual) is { } typeTest)
                return typeTest;
            if (ValueTypeUnionValueReceiverText(operand) is { } unionReceiver)
            {
                return kind == ComparisonKind.Equal
                    ? $"{unionReceiver} is null"
                    : $"{unionReceiver} is not null";
            }

            return kind == ComparisonKind.Equal
                ? $"{Operand(operand)} is null"
                : $"{Operand(operand)} is not null";
        }
        // Equality over an explicitly boxed operand is reference identity over the
        // boxed object. Dropping the box evidence turns unconstrained generic
        // operands into `T == T` (CS0019) and can also bind overloaded operators.
        if (kind is ComparisonKind.Equal or ComparisonKind.NotEqual
            && (left is Box || right is Box))
        {
            return $"{BoxedReferenceOperand(left)} {ComparisonOperator(kind)} {BoxedReferenceOperand(right)}";
        }
        // The `cgt.un`/`clt.un` against null idiom csc emits for a reference
        // inequality (`ldnull; cgt.un` = `obj != null`): an unsigned ordering of a
        // reference against null tests nullness (null is 0, so `x > 0` / `0 < x`
        // unsigned is `x != 0`; the inverted dual is `x == 0`). There is no
        // is-null ordering form; rendered literally `obj > null` or `obj <= null`
        // is CS0019. Pointers forbid the is-pattern (CS8521), so spell those with
        // equality.
        if (isUnsigned
            && ((kind == ComparisonKind.GreaterThan && right is Constant { Value: null })
                || (kind == ComparisonKind.LessThan && left is Constant { Value: null })
                || (kind == ComparisonKind.LessThanOrEqual && right is Constant { Value: null })
                || (kind == ComparisonKind.GreaterThanOrEqual && left is Constant { Value: null })))
        {
            bool isNullTest = kind is ComparisonKind.LessThanOrEqual or ComparisonKind.GreaterThanOrEqual;
            var operand = kind is ComparisonKind.GreaterThan or ComparisonKind.LessThanOrEqual ? left : right;
            if (IsInstanceNullTestText(operand, isNotNull: !isNullTest) is { } typeTest)
                return typeTest;
            if (ValueTypeUnionValueReceiverText(operand) is { } unionReceiver)
                return $"{unionReceiver} is {(isNullTest ? "" : "not ")}null";

            return operand.ResultType is { Kind: TypeRefKind.Pointer }
                ? $"{Operand(operand)} {(isNullTest ? "==" : "!=")} null"
                : $"{Operand(operand)} is {(isNullTest ? "" : "not ")}null";
        }
        // A pointer compared to a native-int zero is a null check: csc lowers
        // `ptr == null` to `ldc.i4.0; conv.u; ceq`, so the zero arrives as an
        // `int`/`nuint` 0 (often through a Convert). Comparing a pointer to that
        // integer is CS0019; spell it `ptr == null`. C# forbids `is null` on
        // pointer types (CS8521), so this uses the equality form, not is-null.
        if (kind is ComparisonKind.Equal or ComparisonKind.NotEqual)
        {
            if (IsCurrentEqualityOperator()
                && IsKnownReferenceComparison(left, right))
            {
                return $"(object){Operand(left)} {ComparisonOperator(kind)} (object){Operand(right)}";
            }

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
        // underlying integer; coerce the integer operand to the enum type. A
        // cross-assembly enum is unresolved (TypeShape.Unknown), so the
        // structural test inside the coercion is what fixes it.
        if (TryCoerceEnumOperand(right, left.ResultType) is { } coercedRight)
            return $"{Operand(left)} {ComparisonOperator(kind)} {coercedRight}";
        if (TryCoerceEnumOperand(left, right.ResultType) is { } coercedLeft)
            return $"{coercedLeft} {ComparisonOperator(kind)} {Operand(right)}";
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

    bool IsCurrentEqualityOperator()
        => _function.Name is "op_Equality" or "op_Inequality";

    bool IsKnownReferenceComparison(IrExpression left, IrExpression right)
        => IsKnownReferenceType(left.ResultType) && IsKnownReferenceType(right.ResultType);

    bool IsKnownReferenceType(TypeRef? type)
        => TypeFamilies.Of(type) == StackFamily.O
            || type is { Kind: TypeRefKind.SzArray or TypeRefKind.Array }
            || type is not null && _function.TypeShapes.GetValueOrDefault(NamedDefinition(type)) == TypeShape.Reference;

    string BoxedReferenceOperand(IrExpression operand)
        => operand is Box box ? $"(object){Operand(box.Operand)}" : Operand(operand);

    string? IsInstanceNullTestText(IrExpression operand, bool isNotNull)
    {
        if (operand is not IsInstance ii)
            return null;

        string valueText = NullTestTypeTestValueText(ii.Operand);
        string test = $"{valueText} is {TypeText(ii.Type)}";
        return isNotNull ? test : $"{valueText} is not {TypeText(ii.Type)}";
    }

    string NullTestTypeTestValueText(IrExpression value)
    {
        if (ShouldPreserveClassUnionValueReceiver(value))
            return Operand(value);

        return TypeTestValueText(value);
    }

    bool ShouldPreserveClassUnionValueReceiver(IrExpression value)
    {
        if (value is not LoadProperty property
            || property.PropertyName != "Value"
            || property.IndexArguments.Count != 0
            || !_function.UnionTypes.Contains(NamedDefinition(property.Accessor.DeclaringType))
            || IsValueTypeTarget(NamedDefinition(property.Accessor.DeclaringType)))
        {
            return false;
        }

        var receiver = property.Instance;
        for (IrNode? node = property; node?.Parent is { } parent; node = parent)
        {
            if (parent is LogicalBinary { Kind: LogicalKind.And } logical
                && PlaceIdentity.SameVariable(logical.Left, receiver))
            {
                return false;
            }
        }

        return true;
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

    /// <summary>
    /// Casts a signed-integer operand to its unsigned counterpart; already-unsigned,
    /// float (.un = unordered), and unknown-typed operands print plain. A signed
    /// constant outside the unsigned target's range takes the <c>unchecked</c>
    /// spelling so the out-of-range cast is legal (CS0221).
    /// </summary>
    string UnsignedOperand(IrExpression operand, bool checkedSafe = true)
    {
        string? cast = TypeFamilies.UnsignedCastKeyword(operand.ResultType);
        if (cast is null)
            return Operand(operand);
        return checkedSafe
            ? CheckedSafeCast(() => $"({cast}){Operand(operand)}", force: IsOutOfRangeUnsignedConstant(operand))
            : $"({cast}){Operand(operand)}";
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
        // A constant unsigned operand's value may exceed the signed range
        // (e.g. (long)ulong.MaxValue), which is CS0221 without unchecked; the
        // unsigned value, not the peeled signed value, is what overflows, so wrap
        // any constant operand defensively (a no-op for a fitting constant).
        return CheckedSafeCast(() => $"({TypeText(signed)}){Operand(operand)}", force: TryGetIntegerConstant(operand, out _));
    }

    /// <summary>
    /// The coercion choke point (docs/design/value-typed-emission.md, slice 1):
    /// renders <paramref name="value"/> for a sink typed <paramref name="target"/>,
    /// inserting the explicit conversion C# requires when the value's type would
    /// not implicitly convert (the missing-cast case behind CS0266). Every
    /// typed sink spells its value through here — never an open-coded cast — so
    /// the cast/`unchecked` rules live once. The cast reinterprets bits the
    /// evaluation stack already carries (same family), so it is faithful to the
    /// IL — see <see cref="CSharpConversionRules.NeedsNumericCast"/>. Operand positions
    /// reconciling against an enum sibling use <see cref="TryCoerceEnumOperand"/>,
    /// the operand-shaped door into the same rules.
    /// </summary>
    string CoerceText(IrExpression value, TypeRef? target)
    {
        if (target is { } nativeTarget
            && IsNativeInteger(nativeTarget)
            && AddressOfValue(value) is { } address)
        {
            return $"({TypeText(nativeTarget)})(&{Deref(address)})";
        }
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
            && value is Convert
            {
                Operand: LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress,
                Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" },
            } addressConvert)
        {
            return $"({TypeText(target)})(&{Deref(addressConvert.Operand)})";
        }
        if (target is { Kind: TypeRefKind.Pointer }
            && value is not Constant { Value: null }
            && EffectiveType(value) is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IntPtr" or "UIntPtr" })
        {
            return IsZeroConstant(value)
                ? $"({TypeText(target)})null"
                : $"({TypeText(target)}){Operand(value)}";
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
            && CoercionRendering.CanSpellIntegerToEnum(EffectiveType(value), enumTarget, _function.TypeShapes))
        {
            return value is Constant { Value: int or long } enumKonst
                ? EnumConstantText(enumKonst, enumTarget)
                : EnumIntegerCast(value, enumTarget);
        }
        if (value is Binary enumArithmetic
            && target is { } enumArithmeticTarget
            && EnumArithmeticUnderlyingType(enumArithmetic)?.Equals(enumArithmeticTarget) == true)
        {
            return EnumArithmeticValueText(enumArithmetic) ?? Expression(value);
        }
        // An enum value at an integer-typed sink: cast to the target whenever
        // the cast is value-preserving — same stack family (a byte-backed enum
        // at an int sink widens losslessly; exact equality here left it BARE,
        // CS0266 — slice-4 cross-check review) or an I4 underlying at an I8
        // target. Narrowing (I8 underlying, I4 target) never gets a silent
        // cast; a real narrowing in the IL arrives as a Convert node instead.
        if (target is { } primitiveTarget
            && CoercionRendering.CanSpellEnumToInteger(
                EffectiveType(value),
                primitiveTarget,
                _function.TypeShapes,
                _function.EnumUnderlyingTypes))
        {
            // Wrap only when the checked underlying->target conversion can
            // actually throw (cross-signedness, signed->unsigned): an
            // identity/widening enum cast is overflow-free in checked C# too,
            // and wrapping it is noise — `checked((int)intBackedEnum + 1)`
            // must stay bare (work-2302 CI canary). A missing value__ assumes
            // the I4-signed shape, matching EnumSemanticFamily.
            var underlying = EnumUnderlyingType(EffectiveType(value)) ?? TypeRef.CoreLib("System", "Int32");
            return CSharpConversionRules.CheckedConversionCanThrow(underlying, primitiveTarget)
                ? CheckedSafeCast(() => $"({TypeText(primitiveTarget)}){Operand(value)}")
                : $"({TypeText(primitiveTarget)}){Operand(value)}";
        }
        // Enum → enum: C# permits the explicit conversion between any two enum
        // types directly. Reached when a slot join carries one enum and a
        // sibling range's testimony carries another of the same family
        // (slice-5b round 4: without the spelling, the range severed into an
        // unassigned read).
        if (target is { } enumToEnumTarget
            && CoercionRendering.CanSpellEnumToEnum(
                EffectiveType(value),
                enumToEnumTarget,
                _function.TypeShapes,
                _function.EnumUnderlyingTypes))
        {
            return CheckedSafeCast(() => $"({TypeText(enumToEnumTarget)}){Operand(value)}");
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
        if (value is Constant { Value: int or long } unknownKonst
            && target is { } unknownEnum
            && CoercionRendering.CanSpellUnknownEnumConstant(EffectiveType(value), unknownEnum, _function.TypeShapes))
        {
            return EnumConstantText(unknownKonst, unknownEnum);
        }
        // A bool-valued expression flowing into an integer target is IL's
        // comparison/test result consumed as a number — `cgt.un; ret` from an int
        // method (e.g. byte.Sign, Convert.ToInt32(bool)). C# has no implicit
        // bool→int, so spell it as the faithful `cond ? 1 : 0`; Roslyn folds that
        // back to the bare comparison opcode, so it recompiles exactly. Runs
        // before the merge-node bail so a bool ternary/coalesce into int is wrapped
        // too (its arms are bool, the wrap is legal).
        if (target is { } intTarget && CoercionRendering.CanSpellBoolToInteger(EffectiveType(value), intTarget))
        {
            // The composition's natural int converts implicitly only to int
            // and wider signed targets; narrow and unsigned targets need the
            // explicit cast — `(byte)(b ? 1 : 0)` (slice-5b round 4: denying
            // these severed single live ranges into unassigned reads).
            return BoolToIntegerText(value, intTarget);
        }
        if (value is Conditional conditional
            && target is { } conditionalTarget
            && TryConditionalTextForTarget(conditional, conditionalTarget) is { } targetedConditional)
            return targetedConditional;
        if (value is SwitchExpression switchExpression
            && target is { } switchTarget
            && CanRenderSwitchExpressionForTarget(switchExpression, switchTarget))
            return SwitchExpressionInline(switchExpression, switchTarget);
        if (value is UnionSwitchExpression unionSwitchExpression
            && target is { } unionSwitchTarget)
            return UnionSwitchExpressionInline(unionSwitchExpression, unionSwitchTarget);
        if (value is Coalesce coalesce
            && target is { } coalesceTarget
            && TryCoalesceTextForTarget(coalesce, coalesceTarget) is { } targetedCoalesce)
            return targetedCoalesce;
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
        if (target is not { } numericTarget || !CoercionRendering.CanSpellPrimitiveNumeric(EffectiveType(value), numericTarget))
            return Expression(value);
        if (!CSharpConversionRules.NeedsNumericCast(EffectiveType(value), target))
            return Expression(value);
        // A plain conversion to a same-width sibling (conv.u2 → ushort feeding a
        // char slot) is subsumed by the boundary cast: emit one cast to the
        // target on the conversion's operand, not (char)((ushort)x). An
        // out-of-range constant operand still needs the unchecked spelling.
        // The remaining casts are same-width reinterprets (cross-signedness or
        // sibling-width): inside a lexical checked region a bare spelling
        // recompiles to a conv.ovf the IL never had (#2301), so they route
        // through CheckedSafeCast like the enum reinterprets above.
        if (value is Convert { IsChecked: false, IsUnsigned: false } conv && CSharpConversionRules.SameNumericSlotWidth(conv.Target, numericTarget))
            return conv.Operand is Constant { Value: int or long } convConst
                ? NumericConstant(convConst, numericTarget)
                : CheckedSafeNumericCast(EffectiveType(conv.Operand), numericTarget, () => $"({TypeText(numericTarget)}){Operand(conv.Operand)}");
        return CheckedSafeNumericCast(EffectiveType(value), numericTarget, () => $"({TypeText(numericTarget)}){Operand(value)}");
    }

    string CheckedSafeNumericCast(TypeRef? source, TypeRef target, Func<string> renderCast)
        => CSharpConversionRules.CheckedConversionCanThrow(source, target)
            ? CheckedSafeCast(renderCast)
            : renderCast();

    static IrExpression? AddressOfValue(IrExpression value)
        => value switch
        {
            LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress => value,
            Convert
            {
                Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" },
                Operand: LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress
            } convert => convert.Operand,
            _ => null,
        };

    string ConditionalText(Conditional conditional)
        => ConditionalText(conditional, conditional.MergedType);

    string ConditionalText(Conditional conditional, TypeRef? target)
    {
        if (SpellsAsLogicalAnd(conditional))
        {
            return Condition(new LogicalBinary(
                LogicalKind.And,
                (IrExpression)conditional.Condition.Clone(),
                (IrExpression)conditional.WhenTrue.Clone()));
        }

        // `?:` is right-associative — the CONDITION is its hazard side
        // (`(a ? b : c) ? d : e` would reparse as `a ? b : (c ? d : e)`), so
        // the condition renders at the NullCoalescing demand: a Conditional
        // (even hidden behind a stale Coerce/Convert — RenderedCondition
        // strips wrappers for classification, the #2345 round-5 lesson) wraps;
        // every other bool form out-binds it. The arms render through Operand,
        // which already wraps a nested conditional where needed.
        var condition = RenderedCondition(conditional.Condition).At(Precedence.NullCoalescing);
        // Two-stage join decision (#2306 unified with #2322): first the
        // join-level bare-vs-spell call (EffectiveJoinTarget — neutralizes the
        // target when the bare arm set is valid C#, so zero churn); then, in
        // spell mode, #2322's merged-type source threading licenses the
        // same-slot-width arm reinterprets.
        var joinArms = new IrExpression[] { conditional.WhenTrue, conditional.WhenFalse };
        var armTarget = EffectiveJoinTarget(target, joinArms);
        TypeRef? primitiveCoercionSourceType =
            armTarget is not null
            && EffectiveType(conditional) is { } conditionalType
            && !conditionalType.Equals(armTarget)
            && CanRenderPrimitiveConditionalForTarget(conditional, armTarget)
                ? conditionalType
                : null;
        bool joinHasExactTypedArm = armTarget is { } anchorTarget && joinArms.Any(arm => JoinArmAnchorsTarget(arm, anchorTarget));
        return $"{condition} ? {ConditionalArm(conditional.WhenTrue, armTarget, primitiveCoercionSourceType, joinHasExactTypedArm)} : {ConditionalArm(conditional.WhenFalse, armTarget, primitiveCoercionSourceType, joinHasExactTypedArm)}";
    }

    /// <summary>
    /// Whether an arm anchors the join's natural type AT the target, licensing
    /// sibling in-range constants to stay bare under the C# 9 rescue. Only an
    /// arm that renders exactly-T (its bare type IS the target, or it will be
    /// spelled `(T)x` by distribution) anchors; a merely-widening arm does not
    /// (#2345 round-2 finding E, Gemini: a `byte` arm at a `uint` join widens
    /// to BOTH uint and int, so the natural type still lands on int and the
    /// bare constant fails — `c ? 1 : byteArm` at uint is CS0266; the
    /// constant must spell `(uint)1`).
    /// </summary>
    bool JoinArmAnchorsTarget(IrExpression arm, TypeRef target)
        => BareJoinType(arm)?.Equals(target) == true
            || (arm is not Constant && !ArmRendersBareSafelyAt(arm, target));

    /// <summary>
    /// The join-level bare-vs-spell decision for a primitive integer target
    /// (#2306). C# validates a conditional at T in two ways: the natural type
    /// (computed TYPE-level over the arm types — a constant contributes
    /// int/long regardless of value) widens into T, or there is NO natural
    /// type and every arm converts to T independently (the C# 9 target-typed
    /// rescue — `uint u = c ? 0 : (uint)x;` is valid precisely because
    /// int/uint have no common type, while `sbyte s = c ? 127 : (sbyte)x;`
    /// has natural type int and fails). When either holds the target is
    /// neutralized so the arms render bare (zero churn); otherwise the target
    /// distributes and the join-arm rule spells every non-exact arm, making
    /// the join uniformly T-typed by construction. Non-primitive targets
    /// (enum/char/reference) keep their dedicated paths untouched.
    /// </summary>
    TypeRef? EffectiveJoinTarget(TypeRef? target, IReadOnlyList<IrExpression> arms)
        => target is { } t && TypeFamilies.IsIntegerLike(t) && PrimitiveJoinArmsRenderBare(arms, t)
            ? null
            : target;

    bool PrimitiveJoinArmsRenderBare(IReadOnlyList<IrExpression> arms, TypeRef target)
    {
        if (arms.All(arm => ArmRendersBareSafelyAt(arm, target)))
            return true;
        var bareTypes = arms.Select(BareJoinType).ToList();
        if (bareTypes.Any(t => t is null))
            return false;
        bool hasNatural = bareTypes.Any(candidate => bareTypes.All(other =>
            other!.Equals(candidate!) || CSharpConversionRules.IsImplicitIntegerWidening(other!, candidate!)));
        return !hasNatural && arms.All(arm => ArmConvertsImplicitlyAt(arm, target));
    }

    /// <summary>The type an arm's bare rendering contributes to C#'s natural-type computation.</summary>
    TypeRef? BareJoinType(IrExpression arm) => arm switch
    {
        Constant { Value: int } => TypeRef.CoreLib("System", "Int32"),
        Constant { Value: long } => TypeRef.CoreLib("System", "Int64"),
        Coerce coerce => coerce.Target,
        Convert { Target: { } convTarget } => convTarget,
        _ => EffectiveType(arm),
    };

    /// <summary>Whether the arm expression converts implicitly to the target on its own — the per-arm half of the C# 9 rescue (constant conversions included).</summary>
    bool ArmConvertsImplicitlyAt(IrExpression arm, TypeRef target)
    {
        if (ArmRendersBareSafelyAt(arm, target))
            return true;
        return arm is Constant { Value: int or long } konst
            && CSharpConversionRules.ConstantFits(konst.Value is int i ? i : (long)konst.Value!, target);
    }

    static bool IsFalseConstant(IrExpression expression)
        => expression is Constant { Value: false } or Constant { Value: 0 };

    static bool IsBooleanLike(IrExpression expression)
        => expression.ResultType is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary };

    /// <summary>
    /// Whether <see cref="ConditionalText(Conditional, TypeRef?)"/> spells this
    /// conditional as a short-circuit <c>&amp;&amp;</c> instead of a ternary —
    /// the one place a Conditional NODE renders at ConditionalAnd precedence.
    /// The fold and <see cref="RenderedCondition"/>'s classification share this
    /// predicate so the spelling decision and its precedence report cannot
    /// drift apart (the capability-gate discipline from #2345).
    /// </summary>
    static bool SpellsAsLogicalAnd(Conditional conditional)
        => IsFalseConstant(conditional.WhenFalse)
            && IsBooleanLike(conditional)
            && IsBooleanLike(conditional.WhenTrue);

    string ConditionalArm(IrExpression arm, TypeRef? target, TypeRef? primitiveCoercionSourceType = null, bool joinHasExactTypedArm = true)
        => target is { } charTarget && IsCoreChar(charTarget) && TryCharConstantText(arm, out var charText)
            ? charText
            // An integer arm flowing into an enum-typed conditional (`ci ? 4 : raw`
            // into StringComparison) is CS0266 — int does not convert to the enum.
            // TryCoerceEnumOperand owns the decision: the enum cast for an integer
            // arm (constant or not; a cross-assembly enum is unresolved, and its
            // structural test catches it), the composed `(E)(cond ? 1 : 0)` for a
            // bool arm. A same-assembly enum arm is enum-typed (not integer-like)
            // and renders its member name via Operand.
            : TryCoerceJoinArm(arm, target, primitiveCoercionSourceType, joinHasExactTypedArm) is { } coercedArm
                ? coercedArm
            : target is { } intTarget && TypeFamilies.IsIntegerLike(intTarget)
            && EffectiveType(arm) is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
                ? BoolToIntegerText(arm, intTarget)
                : Operand(arm);

    string BoolToIntegerText(IrExpression value, TypeRef target)
        => BoolToInteger(value, target).Text;

    /// <summary>
    /// The bool→integer truthiness composition as a precedence-carrying
    /// fragment (#2376 phase 1): the Int32/Int64 form is a bare `?:`
    /// (Conditional — consumers wrap per their context via
    /// <see cref="Rendered.At"/>); other targets compose the cast form
    /// (Unary, self-delimiting). The composed `?:`'s CONDITION is the
    /// right-associative hazard side, so it renders at the NullCoalescing
    /// demand — a conditional-valued condition (possibly hidden behind a
    /// stale Coerce/Convert, #2345 rounds 4-5) wraps; every other bool form
    /// Condition() produces out-binds it and stays bare.
    /// </summary>
    Rendered BoolToInteger(IrExpression value, TypeRef target)
    {
        string condition = RenderedCondition(value).At(Precedence.NullCoalescing);
        return target is { Namespace: "System", Name: "Int32" or "Int64", Assembly: TypeRef.CoreLibrary }
            ? new Rendered($"{condition} ? 1 : 0", Precedence.Conditional)
            : new Rendered(CheckedSafeCast(() => $"({TypeText(target)})({condition} ? 1 : 0)"), Precedence.Unary);
    }

    /// <summary>
    /// A condition-position fragment: Condition()'s text with the precedence
    /// of what that text actually is. A Conditional hidden behind a stale
    /// Coerce/Convert still renders as a bare ternary, so the wrapper strips
    /// for classification — the node-type blindness that cost #2345 round 5.
    /// A Conditional the printer folds to <c>&amp;&amp;</c> (bool diamond with
    /// a false arm, <see cref="SpellsAsLogicalAnd"/>) reports the fold's
    /// precedence, not the node's — the text has no ternary in it.
    /// </summary>
    Rendered RenderedCondition(IrExpression value)
    {
        var stripped = value;
        while (stripped is Coerce coerce)
            stripped = coerce.Operand;
        while (stripped is Convert { IsChecked: false } outer && TypeFamilies.IsBoolean(EffectiveType(outer.Operand)))
            stripped = outer.Operand;
        var precedence = stripped is Conditional folded && SpellsAsLogicalAnd(folded)
            ? Precedence.ConditionalAnd
            : CSharpPrecedence.Of(stripped);
        return new Rendered(Condition(value), precedence);
    }

    /// <summary>
    /// The one join-arm coercion, all three directions: an integer-family arm
    /// at an enum-typed join takes the enum cast/name (<see cref="TryCoerceEnumOperand"/>),
    /// an enum arm at an integer-typed join takes the underlying cast —
    /// `c ? E.One : e` merged as int is CS0266 bare, in a conditional arm, a
    /// switch-expression arm, or a coalesce right side alike (#2145; slice-4
    /// second-family review found the rule present in only one of the three) —
    /// and a primitive arm at a same-family primitive join takes the
    /// reinterpret cast (#2302). Null when the arm needs no join coercion.
    /// </summary>
    string? TryCoerceJoinArm(IrExpression arm, TypeRef? target, TypeRef? primitiveCoercionSourceType = null, bool joinHasExactTypedArm = true)
        => TryCoerceJoinArmRendered(arm, target, primitiveCoercionSourceType, joinHasExactTypedArm)?.Text;

    /// <summary>
    /// The precedence-carrying form (#2376 phase 1): each branch reports the
    /// shape it actually produced — casts and constants are Unary-or-tighter
    /// and never wrap at join contexts; the stale-Coerce re-target routes
    /// through CoerceText, whose loosest spelling is a bare ternary, so it
    /// reports Conditional and the coalesce-right context (`??` binds tighter
    /// than `?:`) wraps it — the rule that replaces the #2345 rounds 3-8
    /// string scanner.
    /// </summary>
    Rendered? TryCoerceJoinArmRendered(IrExpression arm, TypeRef? target, TypeRef? primitiveCoercionSourceType = null, bool joinHasExactTypedArm = true)
    {
        // An arm carrying the pipeline's own Coerce re-targets: the node means
        // "render my operand into the position's required type", and under
        // join distribution the position's requirement IS the join target —
        // rendering the stale wrapper and re-casting made
        // `(sbyte)((sbyte)value)` from Coerce{int, Convert sbyte} at an sbyte
        // sink (#2306 corpus audit). INTEGER-LIKE targets only: re-targeting
        // is the primitive distribution's rule, and letting it preempt
        // TryCoerceEnumOperand sent Coerce{int, bool} at an enum join through
        // CoerceText's integer-gated bool branch and out bare (#2345 review,
        // GPT-5.5: CS0029 — enum targets keep the composition path below).
        if (target is { } retarget && TypeFamilies.IsIntegerLike(retarget)
            && arm is Coerce staleCoerce && !staleCoerce.Target.Equals(retarget))
        {
            return new Rendered(CoerceText(staleCoerce.Operand, retarget), Precedence.Conditional);
        }
        if (TryCoerceEnumOperand(arm, target) is { } coerced)
            return new Rendered(coerced, Precedence.Unary);
        // Same stack family only: `(int)longBackedEnum` would truncate. The
        // importer's family merge should never build such a join, but the cast
        // must not be the place that finds out (slice-4 review's verify item).
        // The same checked-region discipline as the enum->primitive CoerceText
        // exit (#2333, this PR's round-3 review): a uint-backed enum arm cast
        // to int recompiles to conv.ovf.i4.un inside a lexical checked region
        // — a throw the IL never had — so throw-capable underlying->target
        // pairs wrap; identity/widening pairs stay bare.
        if (target is { } integerTarget
            && EffectiveType(arm) is { } enumArmType
            && CoercionRendering.CanSpellEnumToInteger(
                enumArmType,
                integerTarget,
                _function.TypeShapes,
                _function.EnumUnderlyingTypes))
        {
            var underlying = EnumUnderlyingType(enumArmType) ?? TypeRef.CoreLib("System", "Int32");
            return new Rendered(
                CSharpConversionRules.CheckedConversionCanThrow(underlying, integerTarget)
                    ? CheckedSafeCast(() => $"({TypeText(integerTarget)}){Operand(arm)}")
                    : $"({TypeText(integerTarget)}){Operand(arm)}",
                Precedence.Unary);
        }
        // Third direction, unified (#2302/#2306/#2310/#2322): a primitive arm
        // at a primitive join that cannot render bare (ArmRendersBareSafelyAt
        // — the type-level natural-type rule, which also guards Convert/Coerce
        // arms already spelling the target against double casts).
        //
        // CONSTANTS are value-aware, no width guard: an out-of-range constant
        // reinterprets via NumericConstant; an in-range constant stays bare
        // under the post-spelling C# 9 rescue (`c ? 0 : (uint)x`) unless the
        // target widens back into its bare int/long (`(sbyte)127` — bare 127
        // drags the join's natural type to int) or the join has no
        // exactly-typed arm to anchor the rescue (`c ? 0 : 1` at uint would
        // re-form natural type int — all-constant joins cast every arm).
        //
        // NON-CONSTANTS take the CheckedSafeCast reinterpret gated by
        // CanCoercePrimitiveJoinArm against the merged-type coercion source
        // (#2322): same slot width only — a differing-width cast is a
        // narrowing the printer must not silently introduce (left to a real
        // Convert node in the IL). The thunk-form CheckedSafeCast renders the
        // operand outside the checked context so nested checked nodes keep
        // their own wrappers.
        if (target is { } numericTarget && TypeFamilies.IsIntegerLike(numericTarget)
            && !ArmRendersBareSafelyAt(arm, numericTarget))
        {
            if (arm is Constant { Value: int or long } konst)
            {
                long payload = konst.Value is int ci ? ci : (long)konst.Value!;
                if (!CSharpConversionRules.ConstantFits(payload, numericTarget))
                    return new Rendered(NumericConstant(konst, numericTarget), Precedence.Unary);
                var bareType = TypeRef.CoreLib("System", konst.Value is int ? "Int32" : "Int64");
                return !joinHasExactTypedArm || CSharpConversionRules.IsImplicitIntegerWidening(numericTarget, bareType)
                    ? new Rendered($"({TypeText(numericTarget)}){(payload < 0 ? $"({Expression(konst)})" : Expression(konst))}", Precedence.Unary)
                    : null;
            }
            if (EffectiveType(arm) is { } armType
                && CSharpConversionRules.NeedsNumericCast(armType, numericTarget)
                && CanCoercePrimitiveJoinArm(armType, numericTarget, primitiveCoercionSourceType ?? armType))
            {
                return new Rendered(CheckedSafeCast(() => $"({TypeText(numericTarget)}){Operand(arm)}"), Precedence.Unary);
            }
        }
        return null;
    }

    /// <summary>
    /// Whether an arm's bare rendering keeps the surrounding join valid at
    /// <paramref name="target"/> — C#'s conditional natural type is computed
    /// TYPE-level (a constant contributes int/long regardless of its value:
    /// `sbyte s = c ? 1 : 2;` is CS0266, and target-typing never rescues a
    /// conditional that has a natural type — the AssertCompiles canary refuted
    /// the value-aware theory twice). Safe exactly when the arm's rendered
    /// type IS the target or widens implicitly into it: widening is a strict
    /// partial order, so any mix of bare-safe arms yields a natural type that
    /// itself widens to the target, and the join is valid by construction.
    /// </summary>
    bool ArmRendersBareSafelyAt(IrExpression arm, TypeRef target)
    {
        if (arm is Coerce coerce)
            return coerce.Target.Equals(target);
        if (arm is Convert { Target: { } convTarget } && convTarget.Equals(target))
            return true;
        var armType = arm is Constant { Value: int or long } konst
            ? TypeRef.CoreLib("System", konst.Value is int ? "Int32" : "Int64")
            : EffectiveType(arm);
        return armType is not null
            && (armType.Equals(target) || CSharpConversionRules.IsImplicitIntegerWidening(armType, target));
    }

    string? TryConditionalTextForTarget(Conditional conditional, TypeRef target)
        => CanRenderConditionalForTarget(conditional, target)
            ? ConditionalText(conditional, target)
            : null;

    bool CanRenderConditionalForTarget(Conditional conditional, TypeRef target)
        => (IsCoreChar(target)
                && TryCharConstantText(conditional.WhenTrue, out _)
                && TryCharConstantText(conditional.WhenFalse, out _))
            || (IsEnumLikeInteger(target)
                && IsIntegerArm(conditional.WhenTrue)
                && IsIntegerArm(conditional.WhenFalse))
            || CanRenderPrimitiveConditionalForTarget(conditional, target)
            || (IsKnownReferenceLike(target)
                && CanAssignTo(conditional.WhenTrue, target)
                && CanAssignTo(conditional.WhenFalse, target));

    static bool IsIntegerArm(IrExpression arm)
        => arm.ResultType is { } type && TypeFamilies.IsIntegerLike(type);

    /// <summary>
    /// Capability gate for target-aware primitive join rendering (#2322's
    /// shape, generalized to any arm list so the switch-expression and
    /// coalesce consumers share it — the #2145 one-rule-in-all-three
    /// discipline). Capability only: the bare-vs-spell CHURN decision lives in
    /// <see cref="EffectiveJoinTarget"/>, and constants are admitted
    /// unconditionally because the join-arm rule spells them value-aware.
    /// </summary>
    bool CanRenderPrimitiveJoinForTarget(TypeRef target, TypeRef? sourceType, IReadOnlyList<IrExpression> arms)
        => sourceType is { } nodeType
            && TypeFamilies.IsIntegerLike(nodeType)
            && TypeFamilies.IsIntegerLike(target)
            && CoercionRendering.CanSpellSlotCoercion(
                nodeType,
                target,
                _function.TypeShapes,
                _function.EnumUnderlyingTypes)
            && arms.All(arm => arm is Constant { Value: int or long } or Coerce
                || CanRenderPrimitiveJoinArm(arm, target, nodeType));

    bool CanRenderPrimitiveConditionalForTarget(Conditional conditional, TypeRef target)
        => CanRenderPrimitiveJoinForTarget(
            target,
            EffectiveType(conditional),
            [conditional.WhenTrue, conditional.WhenFalse]);

    bool CanRenderPrimitiveJoinArm(IrExpression arm, TypeRef target, TypeRef coercionSourceType)
        => EffectiveType(arm) is { } armType
            && (CoercionRendering.CanSpellBoolToInteger(armType, target)
                || (TypeFamilies.IsIntegerLike(armType)
                    && (!CSharpConversionRules.NeedsNumericCast(armType, target)
                        || CanCoercePrimitiveJoinArm(armType, target, coercionSourceType))));

    bool CanCoercePrimitiveJoinArm(IrExpression arm, TypeRef target)
        => EffectiveType(arm) is { } armType
            && CanCoercePrimitiveJoinArm(armType, target, armType);

    bool CanCoercePrimitiveJoinArm(TypeRef armType, TypeRef target, TypeRef coercionSourceType)
        => TypeFamilies.IsIntegerLike(armType)
            && TypeFamilies.IsIntegerLike(target)
            && CoercionRendering.CanSpellSlotCoercion(
                armType,
                target,
                _function.TypeShapes,
                _function.EnumUnderlyingTypes)
            && CSharpConversionRules.SameNumericSlotWidth(coercionSourceType, target);

    bool CanRenderSwitchExpressionForTarget(SwitchExpression expression, TypeRef target)
        => (IsEnumLikeInteger(target) && expression.Arms.All(arm => IsIntegerArm(arm.Value)))
            || CanRenderPrimitiveJoinForTarget(target, EffectiveType(expression), [.. expression.Arms.Select(arm => arm.Value)]);

    string? TryCoalesceTextForTarget(Coalesce coalesce, TypeRef target)
    {
        // The primitive clause mirrors the conditional/switch gates (the
        // #2145 one-rule-in-all-three discipline); a plain-primitive coalesce
        // left cannot be null so it rarely fires, but the rule must not be
        // width-partial across the three consumers.
        if (CanRenderPrimitiveJoinForTarget(target, EffectiveType(coalesce), [coalesce.Left, coalesce.Right]))
            return CoalesceText(coalesce, target);
        if (!IsEnumLikeInteger(target))
            return null;
        if (NullableValueType(coalesce.Left.ResultType)?.Equals(target) == true && IsIntegerArm(coalesce.Right))
            return CoalesceText(coalesce, target);
        return TryCoerceEnumOperand(coalesce, target);
    }

    static bool IsCoreChar(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Char" };

    static bool TryCharConstantText(IrExpression expression, out string text)
    {
        switch (expression)
        {
            case Constant { Value: char c }:
                text = CharText(c);
                return true;
            case Constant { Value: int i } when i is >= char.MinValue and <= char.MaxValue:
                text = CharText((char)i);
                return true;
            case Constant { Value: long l } when l is >= char.MinValue and <= char.MaxValue:
                text = CharText((char)l);
                return true;
            default:
                text = "";
                return false;
        }
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
        return CSharpConversionRules.ConstantFits(literal, target)
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
            // so report int here — otherwise the missing-cast logic (Coerce)
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
            // `1 + (int * uint)`, or a Coerce boundary into a signed target)
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

    string? EnumArithmeticValueText(Binary binary)
    {
        bool enclosingChecked = _checkedContext;
        if (binary.IsChecked)
        {
            _checkedContext = true;
            try
            {
                var text = EnumArithmeticText(binary);
                return text is not null && !enclosingChecked ? $"checked({text})" : text;
            }
            finally
            {
                _checkedContext = enclosingChecked;
            }
        }

        bool uncheckedOverflow = enclosingChecked && EnumArithmeticCanOverflow(binary);
        if (uncheckedOverflow)
            _checkedContext = false;
        try
        {
            var text = EnumArithmeticText(binary);
            return text is not null && uncheckedOverflow ? $"unchecked({text})" : text;
        }
        finally
        {
            _checkedContext = enclosingChecked;
        }
    }

    static bool EnumArithmeticCanOverflow(Binary binary)
        => binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply;

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
