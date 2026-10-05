using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.ControlFlow;
using ILInspector.Metadata;
using Inspector.Text;
using static ILInspector.Decompiler.Pipeline.PointerArithmetic;
using static ILInspector.Decompiler.Pipeline.PlaceIdentity;
using DecisionKey = (string RuleId, string Category, string Subject, string Detail, string OldValue, string NewValue, string DedupDiscriminator);

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Expression text: store targets, coalesce and conditions, truthiness, unbox
/// and dereference spelling, initializers, and logical/bitwise chain layout.
/// Defines one #2095 cast-need predicate, NeedsObjectBridgeForGenericUnbox; the
/// rest of that class and the coercion-routing and join-target classes are
/// defined in Numerics.
/// </summary>
public sealed partial class CSharpPrinter
{
    static TypeRef? StorePropertyTargetType(StoreProperty store)
        => store.Accessor.ParameterTypes.Length > 0 ? store.Accessor.ParameterTypes[^1] : null;

    // The `stelem` opcode records a storage-primitive element type (e.g. `long` for
    // a long-backed enum array, or `int` for a cross-assembly enum array), which
    // drops an enum-typed integer store below its real element type and prints a
    // bare literal (CS0266). Prefer the array's own element type when it is
    // enum-like — same-assembly (`TypeShape.Enum`) or cross-assembly (an unresolved
    // non-primitive definition), matching `Coerce`'s enum-cast reasoning.
    TypeRef? StoreElementTargetType(StoreElement store)
        => CoercionSinks.StoreElementTarget(store, _function.TypeShapes);

    /// <summary>
    /// The type C# binds a target-typed <c>new()</c> to at an array element store —
    /// the array expression's static element type, which is what <c>a[i] = new()</c>
    /// constructs. Offered only when that element type is exactly the coercion target
    /// (<see cref="StoreElementTargetType"/>): a <c>stelem.ref</c> erases its token to
    /// <c>object</c>, and a covariant <c>stelem</c> token can be wider than the
    /// array's static element type, so when they disagree the exact-type-equality
    /// guard would not reflect the constructed type — decline (return null) and keep
    /// the explicit spelling. Never widens the reviewed firing set: value-type element
    /// arrays (token == element) still fire, reference-type arrays still decline.
    /// </summary>
    TypeRef? StoreElementNewTarget(StoreElement store)
    {
        var coercionTarget = StoreElementTargetType(store);
        return store.Array.ResultType is { Kind: TypeRefKind.SzArray or TypeRefKind.Array, ElementType: { } element }
            && coercionTarget is not null
            && element.Equals(coercionTarget)
            ? coercionTarget
            : null;
    }

    /// <summary>
    /// The C# text for a single-dimension array element index. C# implicitly
    /// converts a wide (<c>long</c>/<c>ulong</c>) array index to native int with
    /// a checked range conversion (<c>conv.ovf.i</c> for a signed index,
    /// <c>conv.ovf.i.un</c> for an unsigned one) that is always overflow-checked,
    /// regardless of the enclosing <c>checked</c>/<c>unchecked</c> context.
    /// Spelling that conversion explicitly (<c>a[checked((nint)i)]</c>) is
    /// verbose, unidiomatic, and round-trips to a redundant widen-then-renarrow,
    /// so it is elided:
    /// <list type="bullet">
    /// <item>an operand that already spells with the conversion's signedness
    /// (<c>long</c> for <c>conv.ovf.i</c>, <c>ulong</c> for <c>conv.ovf.i.un</c>)
    /// strips to the bare index (<c>a[i]</c>);</item>
    /// <item>any other wide (8-byte) operand is cast to the primitive matching the
    /// conversion (<c>(long)</c> / <c>(ulong)</c>), because its bare spelling would
    /// carry the wrong signedness or no integer type at all: a typed load opcode
    /// (<c>ldelem.i8</c> / <c>ldind.i8</c>) masks a <c>ulong</c> element or a wide
    /// enum as <c>Int64</c> storage, so a bare <c>a[values[j]]</c> would be CS0266
    /// (enum) or re-insert <c>conv.ovf.i.un</c> where the original was signed
    /// (<c>ulong</c> read as a signed index). The cast re-inserts the same
    /// implicit conversion, e.g. <c>a[(long)values[j]]</c>.</item>
    /// </list>
    /// Both forms recompile to the identical IL. Any other index expression is
    /// spelled unchanged.
    /// </summary>
    string ArrayIndexText(IrExpression index)
    {
        if (index is not Convert
            {
                IsChecked: true,
                Target: { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IntPtr" },
            } convert)
        {
            return Expression(index);
        }

        // The bare index expression's own C# type. A typed load opcode
        // (ldelem.i8 / ldind.i8) reports only its Int64 storage width, so recover
        // the array element or ref/pointer pointee type it masks — that is the
        // type whose signedness C# uses when it re-inserts the index conversion.
        var indexType = WideIndexOperandType(convert.Operand);
        string? primitive = indexType is
            { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" } named
            ? named.Name
            : null;

        // The operand already spells with the signedness the conversion needs, so
        // the checked native-int index conversion is implicit: drop to the bare
        // operand. A signed conv.ovf.i re-appears for a `long` index, an unsigned
        // conv.ovf.i.un for a `ulong` index.
        if ((!convert.IsUnsigned && primitive == "Int64") || (convert.IsUnsigned && primitive == "UInt64"))
            return Expression(convert.Operand);

        // The operand is a wide (8-byte) value whose bare spelling would carry the
        // wrong signedness or no integer type at all: a masked enum (a bare index
        // is CS0266), a `ulong` element used as a signed index, or a `long`
        // element used as an unsigned one. Cast to the primitive matching the
        // conversion so C# re-inserts the same conv.ovf.i / conv.ovf.i.un — the
        // signed conv keeps `(long)`, the unsigned conv `(ulong)`, opcode-exact.
        if (convert.Operand.ResultType is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Int64" or "UInt64" })
        {
            string keyword = convert.IsUnsigned ? "ulong" : "long";
            // The (long)/(ulong) reinterpret is a no-op in IL (source and target
            // are both 8-byte); the only checked conversion here is the always-
            // checked native-int index conv that stays outside the operand. Inside
            // a lexical `checked` region a SIGN-CHANGING reinterpret would instead
            // recompile to a conv.ovf.i8.un / conv.ovf.u8 the original never had, so
            // wrap it in `unchecked(...)` and render the operand plain. A same-sign
            // cast (a long-backed enum's `(long)`, a ulong-backed enum's `(ulong)`)
            // emits no conv even when checked, so it stays bare to avoid noise.
            if (_checkedContext && WideIndexCastSignChanges(indexType, convert.IsUnsigned))
            {
                bool saved = _checkedContext;
                _checkedContext = false;
                try
                {
                    return $"unchecked(({keyword}){Operand(convert.Operand)})";
                }
                finally
                {
                    _checkedContext = saved;
                }
            }
            return $"({keyword}){Operand(convert.Operand)}";
        }

        // Any other checked (nint) conversion recompiles to different IL: spell it.
        return Expression(index);
    }

    /// <summary>
    /// The index operand's real, rendered wide C# type, recovering the array
    /// element or ref/pointer pointee type that a typed load opcode
    /// (<c>ldelem.i8</c> / <c>ldind.i8</c>) reports only as its <c>Int64</c> storage
    /// width — masking a <c>ulong</c> element or a wide enum — and propagating that
    /// through a wide binary or a unary neg/not (whose stack <c>ResultType</c>
    /// keeps a signed operand type even when the rendered expression is
    /// <c>ulong</c>). The bare-rendered
    /// operand is spelled with that type, so it is the type whose signedness drives
    /// the re-inserted index conversion; for any other operand the load carries no
    /// masking and its own <c>ResultType</c> is used.
    /// </summary>
    TypeRef? WideIndexOperandType(IrExpression operand)
    {
        switch (operand)
        {
            case LoadElement { Array.ResultType: { Kind: TypeRefKind.SzArray or TypeRefKind.Array, ElementType: { } element } }:
                return element;
            case LoadIndirect load when WideIndexPointee(load.Address) is { } pointee:
                return pointee;
            // A sign-neutral wide binary renders unsigned whenever an operand
            // renders unsigned at the same width (`v[j] + x` over a `ulong`
            // element and a `ulong` is a `ulong` add), regardless of checkedness —
            // `checked` never changes an expression's C# type. Its own stack
            // ResultType keeps the signed operand type, so recover the rendered
            // type from the unmasked operands. Add/Subtract/Multiply and the
            // bitwise ops are sign-neutral; a shift's result type is its (unmasked)
            // left operand's; Divide/Remainder/ShiftRight carry the sign in their
            // opcode variant (div/div.un, rem/rem.un, shr/shr.un) via IsUnsigned.
            case Binary binary:
            {
                switch (binary.Kind)
                {
                    case BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply
                        or BinaryKind.And or BinaryKind.Or or BinaryKind.Xor:
                    {
                        var left = WideIndexOperandType(binary.Left);
                        var right = WideIndexOperandType(binary.Right);
                        if (IsWideInteger(left) && IsWideInteger(right) && TypeFamilies.Of(left) == TypeFamilies.Of(right))
                            return TypeFamilies.IsUnsignedIntegerPrimitive(left) ? left
                                : TypeFamilies.IsUnsignedIntegerPrimitive(right) ? right
                                : left;
                        return binary.ResultType;
                    }
                    case BinaryKind.ShiftLeft:
                        return WideIndexOperandType(binary.Left);
                    case BinaryKind.Divide or BinaryKind.Remainder or BinaryKind.ShiftRight:
                        return binary.IsUnsigned
                            ? TypeFamilies.UnsignedCounterpart(binary.ResultType) ?? binary.ResultType
                            : binary.ResultType;
                    default:
                        return binary.ResultType;
                }
            }
            // A bitwise `~` preserves its operand's type (`~v[j]` over a `ulong`
            // element is a `ulong`, `~e` over an enum is that enum); a unary `-`
            // cannot apply to `ulong`/`nuint` or to any enum, so UnaryText
            // re-inserts a signed reinterpret (`-(long)v[j]`) and the negate then
            // renders signed. Recover the rendered type from the unmasked operand
            // (its ResultType is the masked stack type), mapping a negate over a
            // non-negatable operand to the signed integer it now renders as: an
            // enum to `long`/`int` by its underlying width, else the unsigned
            // primitive's signed counterpart.
            case Unary { Kind: UnaryKind.Negate } negate:
            {
                var inner = WideIndexOperandType(negate.Operand);
                if (EnumUnderlyingType(inner) is { } underlying)
                    return TypeRef.CoreLib("System", Is8ByteInteger(underlying) ? "Int64" : "Int32");
                if (NegateReinterpretKeyword(inner) is not null)
                    return TypeFamilies.SignedCounterpart(inner) ?? inner;
                // An unresolved (cross-assembly) enum: UnaryText re-inserts the
                // width-based reinterpret, so the negate renders signed at its
                // masked stack width. Report that signed width so the strip is
                // clean; SignedCounterpart maps a masked unsigned width to signed
                // (Int64/Int32) and is a no-op on an already-signed width.
                if (IsUnresolvedEnumLike(inner))
                    return TypeFamilies.SignedCounterpart(negate.ResultType) ?? negate.ResultType;
                return inner;
            }
            case Unary unary:
                return WideIndexOperandType(unary.Operand);
            default:
                return operand.ResultType;
        }
    }

    static TypeRef? WideIndexPointee(IrExpression address)
        => address.ResultType is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer } indirect ? indirect.ElementType : null;

    /// <summary>
    /// True for the two 8-byte integer primitives (<c>long</c>/<c>ulong</c>) — the
    /// only enum underlying types wide enough to load through an <c>ldelem.i8</c>/
    /// <c>ldind.i8</c> mask and the ones whose negate reinterprets as <c>long</c>
    /// rather than <c>int</c>. Enums cannot be <c>nint</c>/<c>nuint</c>-backed, so
    /// native integers are not considered here.
    /// </summary>
    static bool Is8ByteInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Int64" or "UInt64" };

    /// <summary>
    /// True when the emitted wide index cast <c>(long)</c>/<c>(ulong)</c> flips the
    /// operand's signedness, so recompiling it inside a <c>checked</c> region would
    /// add a <c>conv.ovf.i8.un</c>/<c>conv.ovf.u8</c> the original never had. Both
    /// source and target are 8-byte, so only a sign change is checked-sensitive.
    /// The operand's real signedness comes from the recovered index type: its
    /// underlying type when it is an enum (an <c>ldelem.i8</c>/<c>ldind.i8</c> load
    /// carries an 8-byte-backed enum), else the type itself. An unknown or
    /// unclassifiable type is treated as a flip — wrapping is always
    /// behavior-preserving, so it is the safe default.
    /// </summary>
    bool WideIndexCastSignChanges(TypeRef? indexType, bool castUnsigned)
    {
        var underlying = EnumUnderlyingType(indexType) ?? indexType;
        if (underlying is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" } named)
        {
            if (named.Name == "Int64")
                return castUnsigned;    // signed operand, unsigned cast → flip
            if (named.Name == "UInt64")
                return !castUnsigned;   // unsigned operand, signed cast → flip
        }
        return true;                    // unknown backing: wrap to be safe
    }

    /// <summary>
    /// The text <paramref name="node"/> printed, captured on the way out so an
    /// enclosing statement can bind it to characters once it knows its own
    /// offset. See <see cref="RecordExpressionRanges"/>.
    /// </summary>
    /// <remarks>
    /// Capture is keyed by node and the last write wins. A node printed more than
    /// once keeps its most recent text, which is the one an enclosing statement
    /// is about to append; an earlier, different rendering would simply not be
    /// found in the statement and would claim nothing.
    /// </remarks>
    string Expression(IrExpression node)
    {
        if (_expressionText is null)
            return ExpressionCore(node);
        string text = ExpressionCore(node);
        _expressionText[node] = text;
        _printedRangeMetadata!.SetDefaultNodeKind(
            node,
            AnnotatedSourceNodeKindProjection.From(node));
        return text;
    }

    string MemberTargetText(IrExpression node, string text, bool isElementAccess = false)
        => WithNodeKind(
            node,
            text,
            isElementAccess
                ? "ElementAccessExpression"
                : IsBareMemberTarget(text)
                    ? "NameExpression"
                    : "MemberAccessExpression");

    static bool IsBareMemberTarget(string text)
        => !text.Contains('.', StringComparison.Ordinal)
            && !text.Contains("->", StringComparison.Ordinal);

    string WithNodeKind(IrExpression node, string text, string kind)
    {
        _printedRangeMetadata?.SetNodeKind(node, kind);
        if (_expressionText is not null)
            _expressionText[node] = text;
        return text;
    }

    string CaptureNodeText(IrNode node, string text)
    {
        if (_expressionText is not null)
            _expressionText[node] = text;
        return text;
    }

    string CaptureContextualExpression(IrExpression operand, string text, string kind)
    {
        if (_printedRangeMetadata is not null && _expressionText is not null)
        {
            var node = new SynthesizedRenderedExpression(kind);
            _printedRangeMetadata.SetNodeKind(node, kind);
            (_contextualExpressions ??= [])[(operand, kind)] =
                new(node, operand, text);
        }
        return text;
    }

    string UnsafeExpressionText(
        IrExpression expression,
        string text,
        bool force = false)
    {
        if (!_newMemorySafetyRules
            || _unsafeDepth != 0
            || (!force && !HasRequiredUnsafeOperation(expression)))
        {
            return text;
        }

        return CaptureContextualExpression(
            expression,
            $"unsafe({text})",
            "UnsafeExpression");
    }

    void CaptureContextRange(IrNode node, int start, int end)
    {
        if (_printedRanges is not null && end > start)
            (_contextRanges ??= []).Add((node, start, end));
    }

    string RenderedNodeKind(IrNode node)
        => _printedRangeMetadata?.TryGetNodeKind(node, out string? kind) == true
            ? kind
            : AnnotatedSourceNodeKindProjection.From(node);

    string ExpressionCore(IrExpression node) => node switch
    {
        LoadArgument { Index: 0, Name: "this" } => "this",
        LoadArgument a => CSharpNaming.ContainedIdentifier(a.Name),
        LoadLocal l => $"{LocalName(l.Index)}",
        Constant { Value: int or long } c when EnumSymbolicConstant(c) is { } symbolic
            => WithNodeKind(c, symbolic.Text, symbolic.Kind),
        // A retyped enum constant is still that enum whether or not a single
        // member or complete flags decomposition names it — a bare int is
        // CS0266. EnumConstantText owns the symbolic-or-cast decision (the
        // overflow-aware cast wraps an unsigned- or narrow-backed enum's
        // out-of-range/negative value in `unchecked`, e.g.
        // `unchecked((U)(-1))`). A long-backed enum keeps its `long` payload.
        Constant { Value: int or long, Type: { } enumType } c
            when CoercionRendering.IsEnum(enumType, _function.TypeShapes)
            => WithNodeKind(c, EnumConstantText(c, enumType), "ConversionExpression"),
        Constant { Value: float value } c when !float.IsFinite(value)
            => WithNodeKind(c, SingleText(value), "MemberAccessExpression"),
        Constant { Value: double value } c when !double.IsFinite(value)
            => WithNodeKind(c, DoubleText(value), "MemberAccessExpression"),
        Constant c => ConstantText(c),
        LoadField f => MemberTargetText(f, FieldTarget(f)),
        Binary b => BinaryText(b),
        Comparison c => ComparisonText(c),
        // A LogicalNot in value position (a folded `brfalse x; ldc.0/ldc.1` select,
        // e.g. `return y == null`) over a NON-bool operand is the same truthiness
        // test the condition path spells: `!y` on an object is CS0023, the faithful
        // form is `y is null` (and `x == 0` for an integer). A bool operand returns
        // null from Truthiness and keeps the bare `!operand`.
        LogicalNot { Operand: Comparison c } n => WithNodeKind(
            n,
            ComparisonText(
                Conditions.Inverse(c.Kind),
                IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
                c.Left,
                c.Right),
            ComparisonSurfaceKind(
                Conditions.Inverse(c.Kind),
                IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
                c.Left,
                c.Right)),
        LogicalNot { Operand: Call { Callee.Name: "op_Equality" or "op_Inequality" } call } n when InvertedEqualityOperatorCallText(call) is { } invertedEquality
            => WithNodeKind(n, invertedEquality, "BinaryExpression"),
        LogicalNot { Operand: Call { Callee.Name: "op_LessThan" or "op_LessThanOrEqual" or "op_GreaterThan" or "op_GreaterThanOrEqual" } call } n when InvertedRelationalOperatorCallText(call) is { } invertedRelational
            => WithNodeKind(n, invertedRelational, "BinaryExpression"),
        LogicalNot { Operand: LogicalBinary logical } n when TryPropertyPatternText(logical, negated: true) is { } negatedPattern
            => WithNodeKind(n, negatedPattern, "PatternExpression"),
        LogicalNot { Operand: { } operand } n when Truthiness(operand) is { } negated
            => WithNodeKind(
                n,
                negated.Inverted,
                negated.InvertedKind),
        LogicalNot n => $"!{Operand(n.Operand)}",
        LogicalBinary l => LogicalText(l),
        Conditional t => ConditionalText(t),
        SwitchExpression se => SwitchExpressionInline(se),
        UnionSwitchExpression se => UnionSwitchExpressionInline(se),
        TupleSwitchExpression se => TupleSwitchExpressionInline(se),
        PatternSwitchExpression se => PatternSwitchExpressionInline(se),
        NullCoalescingFieldAssignmentExpression n => $"{FieldTarget(n.Field, n.Instance)} ??= {CoerceText(n.Value, n.Field.Type)}",
        Coalesce co => CoalesceText(co),
        NullConditional nc => NullConditionalText(nc),
        Unary u => UnaryText(u),
        AwaitExpression aw => $"await {Operand(aw.Operand)}",
        IncrementDecrement id => IncrementDecrementText(id),
        // The coercion node renders through the one rule — the node IS the
        // routing guarantee; CoerceText decides cast, unchecked, name, or bare.
        Coerce co => CoerceNodeText(co),
        Convert v => ConvertText(v),
        Call c when MultiDimArrayAccessText(c) is { } text
            => WithNodeKind(
                c,
                text,
                c.Callee.Name == "Set" ? "AssignmentStatement" : "ElementAccessExpression"),
        Call c => WithNodeKind(
            c,
            CallText(c),
            AnnotatedSourceNodeKindProjection.OperatorKind(c) ?? "InvocationExpression"),
        CallIndirect ci => $"{FunctionPointerOperand(ci.Pointer)}({Arguments(ci.Arguments, ci.ParameterTypes, CallIndirectRefKinds(ci), explicitIn: true)})",
        DelegateCreation d => $"new {TypeText(d.DelegateType)}({MethodGroupText(d.Method, d.Target, d.IsVirtual)})",
        InterpolatedStringExpression i => InterpolatedStringText(i),
        Lambda lam => LambdaText(lam),
        LocalFunctionInvocation inv => $"{CSharpNaming.ContainedIdentifier(inv.Name)}({Arguments(inv.Arguments, inv.ParameterTypes, inv.ParameterRefKinds, coerceValues: false)})",
        AddressOfMethod m => AddressOfMethodText(m),
        LoadFunctionPointer p => $"/* {p.Describe()} */",
        LoadProperty p => MemberTargetText(
            p,
            PropertyTarget(p.Accessor, p.HasInstance ? p.Instance : null, p.IndexArguments, p.PropertyName, p.IsVirtual),
            p.IndexArguments.Count > 0),
        DynamicGetMember d => DynamicGetMemberText(d),
        NewObject n when MultiDimArrayCreationText(n) is { } text
            => WithNodeKind(n, text, "ArrayCreationExpression"),
        NewObject n => $"new {TypeText(n.Constructor.DeclaringType)}({Arguments(n.Arguments, n.Constructor.ParameterTypes, n.Constructor.ParameterRefKinds)})",
        TupleExpression t => $"({Arguments(t.Elements)})",
        TupleBinaryExpression t => $"{Operand(t.Left)} {(t.IsEquality ? "==" : "!=")} {Operand(t.Right)}",
        AnonymousObject a => AnonymousObjectText(a),
        ObjectInitializerExpression oi => ObjectInitializerText(oi),
        WithExpression w => WithExpressionText(w),
        InitializerBlock ib => InitializerBodyText(ib.IsCollection, ib.Entries),
        ArrayLength l => $"{Operand(l.Array)}.Length",
        SliceExpression sl => $"{ReceiverText(sl.Receiver)}[{Expression(sl.Range)}]",
        // Endpoints go through Operand(), not Expression(): the range operator `..`
        // binds tighter than `+`/`-`/`*`/… on its operand, so a compound bound must
        // keep its parentheses (`arr[(a + b)..]`, not `arr[a + b..]`, which reparses
        // as `arr[a + (b..)]` — CS0019). Operand() wraps non-atoms and leaves atoms
        // (including nested ranges and `^x`) bare, matching IndexFromEnd below.
        RangeExpression r => $"{(r.HasStart ? Operand(r.Start!) : "")}..{(r.HasEnd ? Operand(r.End!) : "")}",
        IndexFromEnd i => $"^{Operand(i.Offset)}",
        LoadElement e when MultiDimArrayElementText(e) is { } text => text,
        LoadElement e => $"{Operand(e.Array)}[{ArrayIndexText(e.Index)}]",
        NewArray n => ArrayCreationText(n.ElementType, [n.Length]),
        SpanLiteral s => $"new {TypeText(s.ElementType)}[] {{ {string.Join(", ", s.Elements.Select(Expression))} }}",
        ArrayLiteral a => $"new {TypeText(a.ElementType)}[] {{ {string.Join(", ", a.Elements.Select(Expression))} }}",
        CollectionExpression c => $"[{string.Join(", ", c.Elements.Select(CollectionElementText))}]",
        CollectionSpreadElement s => $"..{Expression(s.Source)}",
        InlineArraySpanConversion c => $"({TypeText(c.SpanType)}){Deref(c.Place)}",
        StackAllocate s => $"stackalloc byte[{Expression(s.Size)}]",
        StackAllocArray s => s.HasInitializer
            ? $"stackalloc {TypeText(s.ElementType)}[] {{ {string.Join(", ", s.Elements.ToArray().Select(e => Expression((IrExpression)e)))} }}"
            : $"stackalloc {TypeText(s.ElementType)}[{Expression(s.Count)}]",
        Box b => BoxText(b),
        IsInstance i => WithNodeKind(
            i,
            $"{Operand(i.Operand)} {(IsValueTypeTarget(i.Type) ? "is" : "as")} {TypeText(i.Type)}",
            IsValueTypeTarget(i.Type) ? "PatternExpression" : "ConversionExpression"),
        IsPattern p => $"{TypeTestValueText(p.Value)} is {TypeText(p.Type)} {LocalName(p.LocalIndex)}",
        RecursivePropertyDeclarationPattern p => $"{Operand(p.Value)} is {{ {CSharpNaming.ContainedIdentifier(p.PropertyName)}: {TypeText(p.PatternType)} {LocalName(p.LocalIndex)} }}",
        SingleElementListPattern p => $"{Operand(p.Value)} is [{ListPatternAlternativesText(p)}]",
        PositionalPattern p => PositionalPatternText(p),
        CastClass c => $"({TypeText(c.Type)}){Operand(c.Operand)}",
        UnboxAny u => $"({TypeText(u.Type)}){UnboxAnyOperand(u)}",
        Unbox u => $"ref ({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocalAddress a => $"ref {LocalName(a.Index)}",
        LoadArgumentAddress a => $"ref {CSharpNaming.ContainedIdentifier(a.Name)}",
        LoadFieldAddress f => $"ref {FieldTarget(f.Field, f.Instance)}",
        FixedBufferElementAddress f => $"ref {FixedBufferElementText(f)}",
        LoadElementAddress e when MultiDimArrayElementAddressText(e) is { } text => $"ref {text}",
        LoadElementAddress e => $"ref {Operand(e.Array)}[{ArrayIndexText(e.Index)}]",
        LoadIndirect l => DerefLoadText(l),
        SizeOf s => $"sizeof({TypeText(s.Type)})",
        DefaultValue d => $"default({TypeText(d.Type)})",
        TypeOf t => $"typeof({TypeOfTypeText(t.Type)})",
        LoadToken t => t.Kind == RuntimeTokenKind.Type && t.Type is not null
            ? $"typeof({TypeOfTypeText(t.Type)})"
            : TokenPlaceholder(t),
        CaughtException => "__exception",
        UnsupportedNode u => $"/* {u.Describe()} */",
        _ => $"/* {node.Describe()} */",
    };

    static string TokenPlaceholder(LoadToken token)
        => token.Kind switch
        {
            RuntimeTokenKind.Field => $"/* {token.Describe()} */ default(System.RuntimeFieldHandle)",
            RuntimeTokenKind.Method => $"/* {token.Describe()} */ default(System.RuntimeMethodHandle)",
            _ => $"/* {token.Describe()} */ null",
        };

    string DynamicGetMemberText(DynamicGetMember d)
    {
        string member = CSharpNaming.ContainedIdentifier(d.PropertyName);
        // A dynamic member access needs no member signature — the name is a
        // string handed to Binder.GetMember and resolved at runtime — so a
        // receiver whose static type is already `dynamic` binds `receiver.Member`
        // with no cast. The `(dynamic)` cast is only required to coerce an
        // `object`-typed operand (or a mixed expression) into dynamic dispatch.
        // Drop it when the operand's source (a dynamic parameter, or a hoisted
        // display-class field carrying [DynamicAttribute]) recovers a dynamic
        // type view.
        if (IsDynamicTypedReceiver(d.Receiver))
            return $"{DynamicDroppedCastReceiverText(d.Receiver)}.{member}";
        return $"((dynamic){Operand(d.Receiver)}).{member}";
    }

    static bool IsDynamicTypedReceiver(IrExpression receiver) => receiver switch
    {
        LoadArgument { IsDynamic: true } => true,
        LoadField { Field.IsDynamic: true } => true,
        // A by-ref `dynamic` parameter (`ref`/`in`/`out dynamic`) reads through a
        // deref of the by-ref argument; the referenced element's static type is
        // still `dynamic`, so the `(dynamic)` cast is equally redundant (#3035).
        LoadIndirect { Address: LoadArgument { IsDynamic: true } } => true,
        LoadIndirect { Address: LoadField { Field.IsDynamic: true } } => true,
        _ => false,
    };

    // Bare place text for a dynamic-typed receiver whose redundant `(dynamic)` cast
    // is dropped. A by-ref deref reads back as the underlying argument identifier
    // (`value`), so its `Operand` receiver-parentheses (`(value).Member`) are
    // dropped to match the plain-parameter form (`value.Member`) (#3035).
    string DynamicDroppedCastReceiverText(IrExpression receiver)
        => receiver is LoadIndirect load ? DerefLoad(load) : Operand(receiver);

    // A parameter authored as top-level `dynamic` must be spelled `dynamic` in
    // the declaration, not `object` (its TypeRef). This keeps a local-function
    // header consistent with a body that drops the redundant `(dynamic)` cast on
    // the parameter: `object Get(object v) => v.Length;` is CS1061, whereas
    // `object Get(dynamic v) => v.Length;` binds. A by-ref `dynamic` parameter
    // keeps its by-ref modifier — only the referenced element is `dynamic` — so
    // it is spelled `ref dynamic`, not bare `dynamic` (which would drop `ref` and
    // yield CS1615 at the call site) (#3035). Top-level method signatures are
    // spelled by the metadata signature printer; this covers the printer-owned
    // local-function declaration path.
    string ParameterTypeText(Parameter p)
    {
        if (!p.IsDynamic)
            return TypeText(p.Type);
        return p.Type.Kind == TypeRefKind.ByRef ? "ref dynamic" : "dynamic";
    }

    string ParameterTypeText(Parameter parameter, ArgumentRefKind refKind)
    {
        if (parameter.Type.Kind != TypeRefKind.ByRef || refKind == ArgumentRefKind.Value)
            return ParameterTypeText(parameter);

        string element = parameter.IsDynamic
            ? "dynamic"
            : TypeText(parameter.Type.ElementType!);
        return refKind switch
        {
            ArgumentRefKind.Out => $"out {element}",
            ArgumentRefKind.In => $"in {element}",
            _ => $"ref {element}",
        };
    }

    string CoalesceText(Coalesce co, TypeRef? target = null)
    {
        TypeRef? coalesceTarget = target ?? NullableValueType(co.Left.ResultType) ?? co.ResultType;
        // Single-arm join decision for the right side (the #2306 sibling rule):
        // a bare-safe right renders untouched, a non-safe one is spelled —
        // with the node's source type threaded like the conditional/switch
        // consumers (#2345 round-2 threading discipline).
        TypeRef? primitiveCoercionSourceType = null;
        if (coalesceTarget is { } integerTarget && TypeFamilies.IsIntegerLike(integerTarget))
        {
            coalesceTarget = EffectiveJoinTarget(integerTarget, [co.Right]);
            primitiveCoercionSourceType =
                coalesceTarget is not null
                && co.PrimitiveJoinArmSource(coalesceTarget) is { } nodeType
                    ? nodeType
                    : null;
        }
        return $"{CoalesceLeftText(co.Left)} ?? {CoalesceRightText(co.Right, coalesceTarget, primitiveCoercionSourceType)}";
    }

    string CoalesceRightText(IrExpression right, TypeRef? target, TypeRef? primitiveCoercionSourceType = null)
        // The `??` right operand demands NullCoalescing (`??` binds tighter
        // than `?:`), so a Conditional-precedence fragment — the bool→int
        // composition, a stale-`Coerce` re-target, a bare conditional right —
        // wraps by the one precedence rule (#2376 phase 1; replaces the #2345
        // rounds 3-8 string scanner). Conditional/switch arms carry no such
        // demand — their `:`/`=>` delimiters already bracket the ternary.
        => CoalesceRightRendered(right, target, primitiveCoercionSourceType).At(Precedence.NullCoalescing);

    Rendered CoalesceRightRendered(IrExpression right, TypeRef? target, TypeRef? primitiveCoercionSourceType)
        => TryCoerceJoinArmRendered(right, target, primitiveCoercionSourceType) is { } coerced
            ? coerced
            // The bool-arm composition, mirroring ConditionalArm and
            // SwitchArmValueText (the #2145 one-rule-in-all-three discipline).
            : target is { } intTarget && TypeFamilies.IsIntegerLike(intTarget)
                && EffectiveType(right) is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
                ? CapturedBoolToInteger(right, intTarget)
                // Operand() parenthesizes every non-atom itself (Conditional
                // included), so its output is always effectively primary — the
                // loose fragments reach this context only through the two
                // branches above, which report their own precedence.
                : Rendered.Primary(Operand(right));

    static TypeRef? NullableValueType(TypeRef? type)
        => type is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Nullable`1" },
            TypeArguments: [var value],
        }
            ? value
            : null;

    string CoalesceLeftText(IrExpression expression)
        => expression is LoadIndirect
        {
            Type:
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Nullable`1" },
            },
            Address.ResultType.Kind: TypeRefKind.ByRef,
        } load
            ? DerefLoad(load)
            // `??` is right-associative, so the left operand is the equal-precedence
            // hazard side: `(a ?? b) ?? c` must not render as `a ?? b ?? c`.
            : RenderedExpression(expression).At(TighterThan(Precedence.NullCoalescing));

    /// <summary>Conditions render brtrue's raw value as-is; LogicalNot over a comparison folds via the shared type-aware duals (float folds flip the unordered flag).</summary>
    string Condition(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: Comparison c } n => WithNodeKind(
            n,
            ComparisonText(
                Conditions.Inverse(c.Kind),
                IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
                c.Left, c.Right),
            ComparisonSurfaceKind(
                Conditions.Inverse(c.Kind),
                IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
                c.Left,
                c.Right)),
        LogicalNot { Operand: Call { Callee.Name: "op_Equality" or "op_Inequality" } call } n when InvertedEqualityOperatorCallText(call) is { } invertedEquality
            => WithNodeKind(n, invertedEquality, "BinaryExpression"),
        LogicalNot { Operand: Call { Callee.Name: "op_LessThan" or "op_LessThanOrEqual" or "op_GreaterThan" or "op_GreaterThanOrEqual" } call } n when InvertedRelationalOperatorCallText(call) is { } invertedRelational
            => WithNodeKind(n, invertedRelational, "BinaryExpression"),
        LogicalNot { Operand: LogicalBinary logical } n when TryPropertyPatternText(logical, negated: true) is { } negatedPattern
            => WithNodeKind(n, negatedPattern, "PatternExpression"),
        LogicalNot { Operand: Call { Callee.Name: "op_True", Arguments: [var value] } } n
            => WithNodeKind(n, InvertedUserTruthiness(value), "ConditionalExpression"),
        LogicalNot { Operand: Call { Callee.Name: "op_False", Arguments: [var value] } } n
            => TransparentConditionText(n, value, OperatorOperand(value)),
        // brtrue/brfalse test any I4/ref value; C# conditions need bool —
        // non-bool operands spell the comparison the branch performs.
        LogicalNot { Operand: { } operand } n when Truthiness(operand) is { } negated
            => WithNodeKind(n, negated.Inverted, negated.InvertedKind),
        LogicalNot n => WithNodeKind(n, $"!{Operand(n.Operand)}", "UnaryExpression"),
        Call { Callee.Name: "op_True", Arguments: [var value] } call
            => TransparentConditionText(call, value, OperatorOperand(value)),
        Call { Callee.Name: "op_False", Arguments: [var value] } call
            => WithNodeKind(call, InvertedUserTruthiness(value), "ConditionalExpression"),
        _ when Truthiness(condition) is { } truthy
            => BindDirectTruthiness(condition, truthy.Direct, truthy.DirectKind),
        _ => Expression(condition),
    };

    string UnsafeConditionText(IrExpression condition)
        => UnsafeExpressionText(condition, Condition(condition));

    string BindDirectTruthiness(
        IrExpression condition,
        string text,
        string kind)
        => condition is IsInstance or IsPattern
            ? WithNodeKind(condition, text, kind)
            : CaptureContextualExpression(condition, text, kind);

    string TransparentConditionText(IrExpression owner, IrExpression value, string text)
        => WithNodeKind(owner, text, RenderedNodeKind(value));

    string InvertedUserTruthiness(IrExpression value)
        => $"({OperatorOperand(value)} ? false : true)";

    string TypeTestValueText(IrExpression value)
        => UnionValueReceiverText(value) ?? Operand(value);

    string? UnionValueReceiverText(IrExpression value)
        => value is LoadProperty property ? UnionValueReceiverText(property) : null;

    string? ValueTypeUnionValueReceiverText(IrExpression value)
        => value is LoadProperty property
            && IsValueTypeTarget(NamedDefinition(property.Accessor.DeclaringType))
            ? UnionValueReceiverText(property)
            : null;

    string? UnionValueReceiverText(LoadProperty property)
    {
        if (property.PropertyName != "Value"
            || property.IndexArguments.Count != 0
            || !_function.UnionTypes.Contains(NamedDefinition(property.Accessor.DeclaringType)))
        {
            return null;
        }

        return property.Instance switch
        {
            LoadArgumentAddress argument => CSharpNaming.ContainedIdentifier(argument.Name),
            LoadArgument argument => CSharpNaming.ContainedIdentifier(argument.Name),
            LoadLocalAddress local => LocalName(local.Index),
            LoadLocal local => LocalName(local.Index),
            LoadFieldAddress field => FieldTarget(field.Field, field.Instance),
            LoadField field => FieldTarget(field),
            _ => null,
        };
    }

    static TypeRef NamedDefinition(TypeRef type)
        => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;

    /// <summary>
    /// Spellings for a non-bool branch operand: <c>!= 0</c> for integers and
    /// enums, <c>is null</c>/<c>is not null</c> for reference shapes. The
    /// operand is a <c>brfalse</c>/<c>brtrue</c> value, so the CLI constrains
    /// it to int, native int, object reference, or managed pointer — never a
    /// struct value. A generic instance is therefore always a reference type
    /// (generic value types cannot be branch operands, and enums are never
    /// generic), so it null-tests with no resolution. A bare definition is
    /// reference-or-enum; signature CLASS/VALUETYPE hints and the importer's
    /// same-assembly shape resolution tell them apart where they can, and an
    /// unresolved definition with neither hint still prints raw rather than guess.
    /// </summary>
    (string Direct, string Inverted, string DirectKind, string InvertedKind)? Truthiness(IrExpression operand)
    {
        // An `isinst T` tested as a branch condition is the C# type-test operator
        // `obj is T` — valid for any target, reference or value. Spelling it with
        // `as` (the value-context form) is CS0077 on a non-nullable value type
        // whose shape the printer could not resolve (e.g. a cross-assembly struct
        // like Guid or BigInteger), and even for a reference type a bare `obj as T`
        // is not a bool. The pattern is already its own truth value — wrapping it
        // in `!= 0` would be `bool != int` (CS0019); the inverse negates it.
        if (operand is IsInstance ii)
        {
            string valueText = TypeTestValueText(ii.Operand);
            string typeText = TypeText(ii.Type);
            return (
                $"{valueText} is {typeText}",
                $"{valueText} is not {typeText}",
                "PatternExpression",
                "PatternExpression");
        }

        if (operand is IsPattern pattern)
        {
            string valueText = TypeTestValueText(pattern.Value);
            string typeText = TypeText(pattern.Type);
            string direct = $"{valueText} is {typeText} {LocalName(pattern.LocalIndex)}";
            bool canUseNegatedPattern = ReferenceOwnership.LocalReferencesOnlyWithin(
                _function,
                pattern.LocalIndex,
                [pattern]);
            return (
                direct,
                canUseNegatedPattern ? $"{valueText} is not {typeText}" : $"!({direct})",
                "PatternExpression",
                canUseNegatedPattern ? "PatternExpression" : "UnaryExpression");
        }

        // A `ref bool`/`bool*` deref loads via `ldind.u1`, so its IR ResultType is
        // `byte`, but it renders as the C# bool place (`flag`/`*p`). Spelling it
        // `flag != 0`/`flag == 0` is `bool != int` (CS0019); it is its own truth
        // value, so let it render bare/negated like any other boolean.
        if (RendersAsBoolean(operand))
            return null;

        var type = operand.ResultType;
        if (type is null || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return null;

        string text = ValueTypeUnionValueReceiverText(operand) ?? Operand(operand);
        (string, string, string, string) reference = (
            $"{text} is not null",
            $"{text} is null",
            "PatternExpression",
            "PatternExpression");
        (string, string, string, string) integer = (
            $"{text} != 0",
            $"{text} == 0",
            "BinaryExpression",
            "BinaryExpression");

        // A bitwise/shift Binary is provably integral — these IL ops only operate
        // on integer or enum operands — even when its nominal result type is a
        // cross-assembly enum (e.g. TypeAttributes) the printer cannot resolve to
        // a stack family or a TypeShape. Spell the branch test `(expr) != 0`
        // rather than leaving a non-bool bitwise expression bare (CS0019). A
        // genuine bool `&`/`|` was filtered by the Boolean guard above.
        if (operand is Binary { Kind: BinaryKind.And or BinaryKind.Or or BinaryKind.Xor or BinaryKind.ShiftLeft or BinaryKind.ShiftRight })
            return integer;

        switch (TypeFamilies.Of(type))
        {
            // Boolean was filtered above, so an I4 family here is a real integer (or char).
            case StackFamily.I4 or StackFamily.I8 or StackFamily.I:
                return integer;
            case StackFamily.O:
                return reference;
            case StackFamily.F:
                return null;   // a float is never a branch operand
        }

        // A nested enum inside a generic owner is represented as a generic
        // instance even though its definition-keyed shape is an enum.
        if (type.Kind == TypeRefKind.GenericInstance)
            return CoercionRendering.IsEnum(type, _function.TypeShapes) ? integer : reference;

        switch (type.DeclaredValueTypeHint)
        {
            case ValueTypeHint.ReferenceType:
                return reference;
            case ValueTypeHint.ValueType:
                return integer;
        }

        return _function.TypeShapes.GetValueOrDefault(NamedDefinition(type)) switch
        {
            TypeShape.Reference => reference,
            TypeShape.Enum => integer,
            // A cross-assembly type is unresolved (Unknown shape): an interface like
            // IDisposable or a framework class is indistinguishable from a framework
            // enum by its TypeRef alone. Fall back to provenance — a value produced
            // by `isinst`/`as` is always a reference (or null), so its truthiness is
            // `is null`/`is not null`, never `!x` (CS0023). Spelling the integer
            // `!= 0` form for a genuine cross-assembly enum is handled above by the
            // operand's resolved type, not by this branch-operand provenance.
            _ => ProducesReference(operand) ? reference : null,
        };
    }

    /// <summary>True when the branch operand provably holds a reference because its sole definition is an <c>isinst</c>/<c>as</c> (which yields a reference or null). Sees through a single-store stack slot or local the value was spilled to.</summary>
    bool ProducesReference(IrExpression operand) => SoleDefinition(operand) is IsInstance;

    IrExpression? SoleDefinition(IrExpression operand)
    {
        switch (operand)
        {
            case IsInstance:
                return operand;
            case LoadLocal load:
            {
                var stores = _function.DescendantsOutsideNestedFunctions.OfType<StoreLocal>().Where(s => s.Index == load.Index).ToList();
                return stores.Count == 1 ? stores[0].Value : null;
            }
            default:
                return null;
        }
    }

    // `box T; unbox.any U` is the generic-math `(U)(object)x` idiom: the box is an
    // explicit (object) cast, not the transparent implicit boxing of a value into an
    // object slot. `(U)x` over a generic type parameter has no direct conversion and
    // is CS0030 — and even for a concrete type, collapsing box+unbox.any to a plain
    // `(U)x` drops the round-trip the IL actually performs. Keep the intermediary.
    string UnboxAnyOperand(UnboxAny unbox)
    {
        if (unbox.Operand is Box box)
            return $"(object){Operand(box.Operand)}";
        // `isinst T; unbox.any T` on the exact same target is csc's unconstrained/
        // struct-constrained declaration-pattern extraction (#2831): `if (x is T t)`
        // cannot store the isinst result through an `as T` local (illegal for a
        // non-class-constrained T), so csc re-tests and unboxes the value inline
        // instead. The general IsInstance expression printer would then spell this
        // nested test through `is`/`as` — `is` renders a bool (can't cast to T,
        // CS0030), and `as` is CS0413 for a non-class-constrained T — so neither
        // choice is valid here regardless of `IsValueTypeTarget`. Only when the
        // exact same test is proven to have already succeeded (an enclosing
        // `IfStatement` whose condition is the identical, side-effect-free test,
        // with this site inside its `Then` and no intervening write to the tested
        // value) is `isinst` redundant: the extraction then behaves exactly like the
        // box+unbox.any object-bridge idiom above, so it renders the same way. Off
        // that proven path the shape is left to fall through unchanged rather than
        // hide it behind an always-succeeds cast that could rewrite failure
        // semantics (NullReferenceException/false vs. a silently different throw).
        if (unbox.Operand is IsInstance sameTargetTest
            && sameTargetTest.Type.Equals(unbox.Type)
            && GenericDeclarationPatternProof.IsProvenSuccessfulTypeTest(unbox, sameTargetTest))
        {
            return $"(object){Operand(sameTargetTest.Operand)}";
        }
        if (NeedsObjectBridgeForGenericUnbox(unbox.Type, unbox.Operand.ResultType))
            return $"(object){Operand(unbox.Operand)}";
        return Operand(unbox.Operand);
    }

    bool NeedsObjectBridgeForGenericUnbox(TypeRef target, TypeRef? source)
    {
        if (target.Kind is not (TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter)
            || source is null
            || source is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Object" })
        {
            return false;
        }

        return TypeFamilies.Of(source) == StackFamily.O
            || source.Kind is TypeRefKind.SzArray or TypeRefKind.Array
            || source.DeclaredValueTypeHint == ValueTypeHint.ReferenceType
            || _function.TypeShapes.GetValueOrDefault(NamedDefinition(source)) == TypeShape.Reference;
    }

    /// <summary>
    /// True when an expression renders as a C# <c>bool</c> regardless of its IR
    /// ResultType. A <c>ref bool</c>/<c>bool*</c> deref loads via <c>ldind.u1</c>
    /// (ResultType <c>byte</c>) but the printer spells it as the underlying bool
    /// place, so a branch over it must negate rather than compare to 0.
    /// </summary>
    bool RendersAsBoolean(IrExpression operand)
        => operand is LoadIndirect { Address.ResultType: { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer, ElementType: { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary } } }
            ;

    Rendered RenderedExpression(IrExpression node)
    {
        string text = Expression(node);
        if (node is Constant { Value: int or long } constant
            && EnumSymbolicConstant(constant) is { } symbolic)
        {
            return new Rendered(text, symbolic.Precedence);
        }
        if (node is Call call && OperatorCallPrecedence(call) is { } operatorPrecedence)
            return new Rendered(text, operatorPrecedence);
        if (IsWholeExpressionWrapper(text, "checked(") || IsWholeExpressionWrapper(text, "unchecked("))
            return Rendered.Primary(text);
        if (node is Coerce && IsSimpleAtomText(text))
            return Rendered.Primary(text);
        return new Rendered(text, CSharpPrecedence.Of(node));
    }

    /// <summary>Parenthesizes compound operands; leaves atoms bare. Conservative until the precedence visitor exists.</summary>
    string Operand(IrExpression node)
    {
        string text = Expression(node);
        // A Call is an atom only when it spells as a method invocation; an
        // operator-spelled call (op_Inequality → `a != b`, op_UnaryNegation →
        // `-x`) renders as a compound expression, so it must parenthesize like
        // any other binary/unary — otherwise an enclosing `!`/`-`/binary
        // misbinds to its first operand (e.g. `!a != b`, CS0023).
        bool atomic = node is LoadArgument or LoadLocal or LoadField
            or NewObject or ArrayLength or LoadElement or FixedBufferElementAddress or SliceExpression or RangeExpression or CaughtException or SizeOf or DefaultValue or LoadToken
            or LoadProperty or TypeOf or DelegateCreation or InterpolatedStringExpression or TupleExpression or AnonymousObject or ObjectInitializerExpression or WithExpression or InitializerBlock or IndexFromEnd or CallIndirect or AddressOfMethod or NullConditional
            or IncrementDecrement or SpanLiteral or ArrayLiteral or CollectionExpression or CollectionSpreadElement
            || node is Call call && !IsOperatorCall(call)
            // A Binary/Convert that renders as a whole-expression `checked(...)`/
            // `unchecked(...)` is a C# primary expression: the wrapper's own parens
            // already bracket it, so an enclosing operator never misbinds and a
            // second pair (`a + (unchecked(b * 2))`) is pure noise. The wrapper must
            // span the ENTIRE text — a child cast can contribute a leading
            // `unchecked(` (`unchecked((uint)b) / unchecked((uint)c)`) without
            // bracketing the whole expression, and dropping its parens would misbind.
            || node is Binary or Convert or Coerce
                && (IsWholeExpressionWrapper(text, "checked(") || IsWholeExpressionWrapper(text, "unchecked("))
            // A Coerce that rendered as a member name or bare literal is an
            // atom; its cast and `cond ? 1 : 0` forms are NOT — a cast as a
            // member-access receiver misbinds onto the call result
            // (`(E)x.M()` is `(E)(x.M())`), so those keep Operand's parens.
            || node is Coerce && IsSimpleAtomText(text);
        atomic = atomic || node is Constant constant
            && EnumSymbolicConstant(constant) is not { IsCombination: true };
        atomic = atomic || node is LoadIndirect { Address: FixedBufferElementAddress }
            || node is LoadIndirect load && PointerElementAccessText(load) is not null;
        return atomic ? text : $"({text})";
    }

    string CollectionElementText(IrExpression element)
        => element is CollectionSpreadElement spread ? $"..{Expression(spread.Source)}" : Expression(element);

    /// <summary>
    /// True when rendered text is a bare identifier chain or non-negative
    /// numeric literal (`LEnum.High`, `10`) — safe unparenthesized in any
    /// operand position, including as a member-access receiver. A leading
    /// minus is deliberately NOT an atom: `-1` as a receiver misbinds
    /// (`-1.ToString()` negates the call), so negative literals keep
    /// Operand's parens (#2145).
    /// </summary>
    static bool IsSimpleAtomText(string text)
    {
        if (text.Length == 0)
            return false;
        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('.' or '_' or '@'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="text"/> is a single <paramref name="prefix"/>-wrapped
    /// expression (e.g. <c>unchecked(...)</c>) whose opening paren matches the final
    /// character — so the wrapper brackets the whole expression. A text that merely
    /// starts with the prefix because a child contributed it
    /// (<c>unchecked((uint)b) / unchecked((uint)c)</c>) returns false. Paren counting
    /// is conservative under string/char literals: a miscount only ever yields false
    /// (keep parens), never a wrong true.
    /// </summary>
    static bool IsWholeExpressionWrapper(string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length == 0 || text[^1] != ')')
            return false;
        int depth = 0;
        for (int i = prefix.Length - 1; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
                return i == text.Length - 1;
        }
        return false;
    }

    // A `&Method` operand cannot be invoked directly — `(&Method)(x)` is invalid
    // C# (CS0149) — so cast it to its delegate* result type first. The
    // CallIndirectSpellabilityPass guarantees the result type is a matching
    // delegate* before such a node survives to print.
    string FunctionPointerOperand(IrExpression pointer)
        => pointer is AddressOfMethod
            ? pointer.ResultType is { Kind: TypeRefKind.FunctionPointer } fp
                ? $"(({TypeText(fp)}){Expression(pointer)})"
                : $"({Expression(pointer)})"
            : Operand(pointer);

    /// <summary>
    /// The C# place a load/store-indirect reads or writes. Dereferencing a
    /// managed reference is implicit in C#: the address of a place (ref local,
    /// ref argument, ref field, ref element) reads back as the place, and a
    /// ref/out parameter or ref local reads as itself — no <c>*</c>. Only a
    /// genuine unmanaged pointer takes the <c>*</c>; an unknown reference keeps
    /// it rather than guess.
    /// </summary>
    string Deref(IrExpression address) => address switch
    {
        // `this` in a value-type instance method is a managed pointer to the
        // value (IL's `ldarg.0` pushes `T&`), but C#'s `this` already denotes
        // the value, so reading through it is just `this` — never `*this`,
        // which is CS0193 (DeclaringType is never an unmanaged pointer).
        LoadArgument { Index: 0, Name: "this" } => "this",
        LoadLocalAddress a => $"{LocalName(a.Index)}",
        LoadArgumentAddress a => CSharpNaming.ContainedIdentifier(a.Name),
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        FixedBufferElementAddress f => FixedBufferElementText(f),
        LoadElementAddress e => $"{Operand(e.Array)}[{ArrayIndexText(e.Index)}]",
        // `unbox T` yields a managed pointer *into* the box. C#'s only spelling
        // for that place is `System.Runtime.CompilerServices.Unsafe.Unbox<T>(o)`
        // — a `ref T`-returning intrinsic. A *pure value read* of that place
        // (`ldobj(unbox T)`) is normalized to `unbox.any` upstream by
        // `UnboxValueReadPass` and spells the universal cast `(T)o`, so this arm
        // is reached only for a genuine place: a `ref `-prefixing caller (a
        // ref-typed `Deref` caller, or a ref-typed `Conditional` arm in this same
        // switch) gets a genuine ref place, and a write through it
        // (`Unsafe.Unbox<T>(o) = v`, via `IndirectTarget`) stores into the box.
        // The obvious `(T)o` alternative is an *unbox.any*
        // copy: it reads the same value but is not an assignable place, so a
        // `ref`/`=` context over it is CS0445/CS0131, and a `ref`-prefixing
        // caller over the node's own ref-producer spelling `ref (T)o` doubles
        // the keyword (`ref ref (T)o`, CS1525). `Unsafe.Unbox` is faithful in
        // every `Deref` position; it is spelled fully qualified so it resolves
        // without depending on an emitted using directive.
        Unbox u => UnsafeUnboxText(u),
        { ResultType.Kind: TypeRefKind.Pointer } => $"*{Operand(address)}",
        // A ref-typed conditional is a ref ternary: the `ref` binds each arm
        // (`cond ? ref a : ref b`), not the expression as a whole — placing it
        // outside is CS8173 in a `= ref` position. This spelling applies only
        // when both arms are themselves references. A conditional typed ref by
        // an upstream merge that carries a non-reference arm is an inexpressible
        // merge — no valid `= ref` form exists for it — so it falls to the
        // generic spelling as a best effort, not a correctness guarantee. Only
        // BooleanFoldingPass.FoldSlotDiamond produces these, and an asymmetric
        // ref/value slot merge is not seen in non-synthetic IL.
        Conditional { ResultType.Kind: TypeRefKind.ByRef } c
            when c.WhenTrue.ResultType?.Kind == TypeRefKind.ByRef
                && c.WhenFalse.ResultType?.Kind == TypeRefKind.ByRef
            => $"({RenderedCondition(c.Condition).At(Precedence.NullCoalescing)} ? ref {Deref(c.WhenTrue)} : ref {Deref(c.WhenFalse)})",
        { ResultType.Kind: TypeRefKind.ByRef } => Operand(address),
        _ => $"*{Operand(address)}",
    };

    static readonly TypeRef s_unsafeType = TypeRef.CoreLib("System.Runtime.CompilerServices", "Unsafe");

    /// <summary>
    /// Spells an <c>unbox</c> as the managed pointer into the box:
    /// <c>System.Runtime.CompilerServices.Unsafe.Unbox&lt;T&gt;(o)</c>, a
    /// <c>ref T</c>-returning intrinsic — the only C# form that is a genuine
    /// assignable place. Fully qualified so it resolves without a using directive.
    /// <para>
    /// This is the unconditional place spelling used by <see cref="Deref"/> and
    /// <c>ArgumentLvalue</c> (ref/out/ref-return/write positions), where the
    /// value-copy cast <c>(T)o</c> is never a place (<c>ref (T)o</c> is CS0445,
    /// <c>out (T)o</c> is CS0206) so there is no safe fallback. <c>unbox</c>
    /// yields a value type for valid IL, so <c>Unsafe.Unbox&lt;T&gt;</c>'s
    /// <c>where T : struct</c> is satisfied; the only exceptions —
    /// <see cref="Nullable{T}"/> and an unconstrained generic parameter — have no
    /// assignable-place form in C# at all (a boxed <c>Nullable&lt;T&gt;</c>/open
    /// <c>T</c> cannot be referenced), so a visible compile error there is
    /// faithful to un-spellable IL rather than a regression. The member-access
    /// receiver position, where the cast <em>is</em> a valid fallback, gates
    /// through <see cref="UnboxReceiverText"/> instead.
    /// </para>
    /// </summary>
    string UnsafeUnboxText(Unbox unbox)
        => $"{FullyQualifiedTypeText(s_unsafeType)}.Unbox<{TypeText(unbox.Type)}>({Operand(unbox.Operand)})";

    /// <summary>
    /// Spells an <c>unbox</c> in a member-access receiver. Unlike a ref/out/write
    /// place, a receiver is a value position where the cast <c>((T)o)</c>
    /// compiles, so it is a safe fallback. Emits the faithful
    /// <see cref="UnsafeUnboxText"/> intrinsic — which reaches the in-box place so
    /// a mutating call or member assignment acts on the boxed payload — only when
    /// the target is a spellable non-nullable value type; otherwise the value-copy
    /// cast, which reads the same value but silently drops a mutation and is
    /// tolerable only because a receiver never needs an assignable place.
    /// </summary>
    string UnboxReceiverText(Unbox unbox)
        => CanReceiveViaUnsafeUnbox(unbox.Type)
            ? UnsafeUnboxText(unbox)
            : $"(({TypeText(unbox.Type)}){Operand(unbox.Operand)})";

    /// <summary>
    /// Whether an <c>unbox</c> receiver can spell as <c>Unsafe.Unbox&lt;T&gt;</c>.
    /// <c>unbox</c> yields a value type for valid IL, so a named definition or a
    /// generic instance qualifies — except <see cref="Nullable{T}"/> and any type
    /// the resolver knows is a reference type (only reachable from malformed IL),
    /// both of which violate <c>where T : struct</c> (CS0453). An open generic
    /// parameter is also excluded: its constraint is unknown, so the compiling
    /// value-copy cast is the safe receiver spelling.
    /// </summary>
    bool CanReceiveViaUnsafeUnbox(TypeRef type) => type.Kind switch
    {
        TypeRefKind.Definition =>
            !IsNullableDefinition(type)
            && _function.TypeShapes.GetValueOrDefault(NamedDefinition(type)) != TypeShape.Reference,
        TypeRefKind.GenericInstance =>
            !TypeFamilies.IsNullableType(type)
            && _function.TypeShapes.GetValueOrDefault(NamedDefinition(type)) != TypeShape.Reference,
        _ => false,
    };

    static bool IsNullableDefinition(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Nullable`1" };

    string FixedBufferElementText(FixedBufferElementAddress address)
        => $"{FieldTarget(address.BufferField, address.Instance)}[{Expression(address.Index)}]";

    /// <summary>
    /// Renders a <c>ldind.&lt;T&gt;</c> read. Most addresses go through
    /// <see cref="Deref"/>, but when the address was reinterpreted to a native
    /// integer (<c>ldarga; conv.u; ldind.u1</c> — the generic
    /// reinterpret-then-read idiom, e.g. <c>Enum.IsDefinedPrimitive&lt;byte&gt;</c>),
    /// <see cref="Deref"/> would render <c>*((nuint)(&amp;value))</c> — a deref of
    /// an integer, CS0193. The faithful unsafe spelling reinterprets the address
    /// as the read's own pointer type and derefs it: <c>*(T*)(&amp;value)</c>. The
    /// <c>(T*)</c> cast subsumes the <c>conv.u</c>, so the read recompiles to the
    /// same <c>ldind</c>.
    /// </summary>
    string DerefLoad(LoadIndirect load)
    {
        if (PointerElementAccessText(load) is { } indexed)
            return indexed;
        if (load.Type is { } element
            && load.Address is Convert { Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" } } conv)
        {
            // An address-of operand keeps its `&place` unsafe spelling (mirroring
            // ConvertText); any other operand is already a pointer/integer value.
            string addr = conv.Operand is LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or FixedBufferElementAddress or LoadElementAddress
                ? $"(&{Deref(conv.Operand)})"
                : Operand(conv.Operand);
            return $"*({TypeText(element)}*){addr}";
        }
        if (load.Type is { } nativeElement && IsNativeInteger(load.Address.ResultType))
            return NativeIntPointerDeref(load.Address, nativeElement);
        return Deref(load.Address);
    }

    string BoxText(Box box)
    {
        var rendered = RenderCoercion(box.Operand, box.Type);
        return WithNodeKind(
            box,
            rendered.Text,
            rendered.Kind);
    }

    string UsingResourceText(UsingStatement usingStatement)
    {
        if (usingStatement.DeclaresResourceVariable
            || usingStatement.Resource.ResultType?.Equals(usingStatement.ResourceType) == true)
        {
            return CoerceText(usingStatement.Resource, usingStatement.ResourceType);
        }

        string text = $"({TypeText(usingStatement.ResourceType)}){Operand(usingStatement.Resource)}";
        return CaptureContextualExpression(usingStatement.Resource, text, "ConversionExpression");
    }

    string DerefLoadText(LoadIndirect load)
    {
        string text = DerefLoad(load);
        return WithNodeKind(load, text, DerefLoadSurfaceKind(load, text));
    }

    string DerefLoadSurfaceKind(LoadIndirect load, string text)
    {
        if (PointerElementAccessText(load) is not null)
            return "ElementAccessExpression";
        if (load.Address is Convert
            {
                Operand.ResultType.Kind: TypeRefKind.Pointer,
                Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" },
            }
            || load.Type is not null && IsNativeInteger(load.Address.ResultType))
        {
            return "IndirectAccessExpression";
        }

        return DereferencedSurfaceKind(load.Address, text);
    }

    string DereferencedSurfaceKind(IrExpression address, string text)
        => address switch
        {
            LoadArgument { Index: 0, Name: "this" } => "NameExpression",
            LoadLocalAddress or LoadArgumentAddress => "NameExpression",
            LoadFieldAddress => IsBareMemberTarget(text) ? "NameExpression" : "MemberAccessExpression",
            FixedBufferElementAddress or LoadElementAddress => "ElementAccessExpression",
            Unbox => "InvocationExpression",
            { ResultType.Kind: TypeRefKind.Pointer } => "IndirectAccessExpression",
            Conditional { ResultType.Kind: TypeRefKind.ByRef } => "ConditionalExpression",
            { ResultType.Kind: TypeRefKind.ByRef } byRef => RenderedNodeKind(byRef),
            _ => "IndirectAccessExpression",
        };

    string? PointerElementAccessText(LoadIndirect load)
    {
        if (load.Type is not { } element
            || load.Address is not Binary { Kind: BinaryKind.Add } address
            || !TrySplitPointerAdd(address, out var pointer, out var offset)
            || pointer.ResultType is not { Kind: TypeRefKind.Pointer, ElementType: { } pointerElement }
            || !pointerElement.Equals(element)
            || !TryScaledPointerIndex(offset, pointerElement, out var index))
        {
            return null;
        }

        return $"{Operand(pointer)}[{Expression(index)}]";
    }

    string IndirectTarget(IrExpression address, TypeRef? elementType)
        => elementType is not null && IsNativeInteger(address.ResultType)
            ? NativeIntPointerDeref(address, elementType)
            : Deref(address);

    string NativeIntPointerDeref(IrExpression address, TypeRef elementType)
        => $"*({TypeText(TypeRef.Pointer(elementType))}){Operand(address)}";

    static bool IsNativeInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IntPtr" or "UIntPtr" };

    static bool IsUnboxPointerConversion(Convert convert)
        => OperationMemorySafetyContract.IsUnboxPointerConversion(convert);

    /// <summary>
    /// The C# type a store-indirect writes through. A primitive <c>stind</c>
    /// opcode carries its own signed type (<c>stind.i4</c> → <c>int</c>) which
    /// can contradict the pointer it writes through: <c>*(uint*)p = (int)x</c>
    /// is int→uint (CS0266), since the C# lvalue <c>*p</c> is typed by the
    /// pointer (<c>uint</c>), not the opcode. Prefer the element type rooted in
    /// the address — the faithful target — falling back to the opcode type when
    /// the address carries no pointer/managed-ref type (an untyped <c>stind</c>).
    /// </summary>
    TypeRef? IndirectStoreType(IrExpression address, TypeRef? opcodeType) => PointerArithmetic.PointeeType(address) ?? opcodeType;

    /// <summary>
    /// Short-circuit composition prints comparisons and nots bare (they bind
    /// tighter than &amp;&amp;/||); a same-kind chain associates without parens on the
    /// left but parenthesizes on the right (C# &amp;&amp;/|| are left-associative, so a
    /// right-nested same-kind chain <c>a &amp;&amp; (b &amp;&amp; c)</c> keeps its parens to stay
    /// opcode-exact, #3126); mixed kinds parenthesize.
    /// </summary>
    string LogicalText(LogicalBinary logical)
    {
        if (TryPropertyPatternText(logical) is { } propertyPattern)
            return WithNodeKind(logical, propertyPattern, "PatternExpression");

        string op = logical.Kind == LogicalKind.And ? "&&" : "||";
        return $"{LogicalOperandText(logical.Left, logical.Kind, rightOperand: false)} {op} {LogicalOperandText(logical.Right, logical.Kind, rightOperand: true)}";
    }

    // A single operand of a short-circuit chain of the given kind. Sides are
    // condition positions: Condition() owns truthiness (a string operand spells
    // 'is not null', never '!value') and the negation folds. A same-kind chain on
    // the LEFT associates bare — C# `&&`/`||` are left-associative, so
    // `(a && b) && c` prints `a && b && c` and recompiles to the same left-nested
    // IL. A same-kind chain on the RIGHT (<paramref name="rightOperand"/>) must
    // parenthesize: `a && (b && c)` is right-associative and csc lays it out with a
    // different branch structure than the flattened `a && b && c` (= `(a && b) && c`),
    // so dropping the parens would recompile to a divergent opcode stream (#3126).
    // A mixed-kind LogicalBinary parenthesizes on either side. Any other side
    // renders at the operator's demand — a ternary or `??` (or one hidden behind
    // a stale Coerce/Convert, the #2345/#2376 blind spot) is looser than
    // `&&`/`||` and must parenthesize, while comparisons and unary forms out-bind
    // it and stay bare (#2376 round-2: the enum/bool truthiness compositions
    // share the one precedence rule, not just BoolToInteger).
    string LogicalOperandText(IrExpression side, LogicalKind kind, bool rightOperand)
    {
        var demand = kind == LogicalKind.And ? Precedence.ConditionalAnd : Precedence.ConditionalOr;
        return side switch
        {
            LogicalBinary nested when nested.Kind == kind && !rightOperand => LogicalText(nested),
            LogicalBinary nested => $"({LogicalText(nested)})",
            _ => RenderedCondition(side).At(demand),
        };
    }

    /// <summary>
    /// Renders a short-circuit <c>&amp;&amp;</c>/<c>||</c> chain rooted at
    /// <paramref name="root"/> as one operand per line — the first operand after
    /// <paramref name="prefix"/> on the head line, each later operand under a
    /// continuation indent, the operator trailing every broken line — when the
    /// chain has at least <see cref="FluentChainMinSegments"/> operands and its
    /// single-line form would exceed <see cref="FluentChainWrapWidth"/>. Returns
    /// null (render inline) otherwise. Each operand's text is exactly the
    /// <see cref="LogicalOperandText"/> the flat <see cref="LogicalText"/> emits,
    /// and the routine declines unless the per-operand join reproduces that flat
    /// text (so a property-pattern rewrite or any other reshaping keeps the
    /// statement inline); the broken form is therefore token-identical to the
    /// inline form — only whitespace differs and the IL is unchanged. Gated on
    /// <see cref="PrinterOptions.WrapSplittableExpressions"/> by the caller.
    /// </summary>
    string? LogicalChainLines(LogicalBinary root, string prefix, string suffix, int indent)
    {
        var operands = new List<IrExpression>();
        CollectLogicalChainOperands(root, root.Kind, operands);
        if (operands.Count < FluentChainMinSegments)
            return null;

        string op = root.Kind == LogicalKind.And ? "&&" : "||";
        var texts = new List<string>(operands.Count);
        foreach (var operand in operands)
            texts.Add(LogicalOperandText(operand, root.Kind, rightOperand: false));

        // The broken form must be a pure whitespace variant of the flat chain:
        // decline unless re-rendering the flattened operands reproduces the flat
        // LogicalText exactly (guards the property-pattern rewrite and any other
        // reshaping the flat renderer would apply).
        string flat = LogicalText(root);
        if (string.Join($" {op} ", texts) != flat)
            return null;

        if (indent * 4 + prefix.Length + flat.Length + suffix.Length <= FluentChainWrapWidth)
            return null;

        string pad = new(' ', indent * 4);
        string continuation = pad + "    ";
        var sb = new System.Text.StringBuilder();
        sb.Append(pad).Append(prefix);
        for (int i = 0; i < texts.Count; i++)
        {
            if (i > 0)
                sb.Append('\n').Append(continuation);
            sb.Append(texts[i]);
            if (i < texts.Count - 1)
                sb.Append(' ').Append(op);
        }
        sb.Append(suffix);
        return sb.ToString();
    }

    /// <summary>
    /// Flattens a same-kind short-circuit chain into its operands in
    /// left-to-right source order; a nested chain of the <em>other</em> kind (or
    /// any non-<see cref="LogicalBinary"/>) is one operand and is not descended
    /// into. This descends a same-kind chain on <em>both</em> sides, so for a
    /// right-nested chain (which <see cref="LogicalOperandText"/> now parenthesizes
    /// to stay opcode-exact) the flattened join no longer matches the flat
    /// <see cref="LogicalText"/>; the caller's exact-match guard then declines the
    /// multi-line wrap and renders the parenthesized inline form (#3126).
    /// </summary>
    static void CollectLogicalChainOperands(IrExpression expression, LogicalKind kind, List<IrExpression> operands)
    {
        if (expression is LogicalBinary logical && logical.Kind == kind)
        {
            CollectLogicalChainOperands(logical.Left, kind, operands);
            CollectLogicalChainOperands(logical.Right, kind, operands);
            return;
        }
        operands.Add(expression);
    }

    /// <summary>
    /// Splices an associative bitwise <c>|</c>/<c>&amp;</c>/<c>^</c> chain rooted at
    /// <paramref name="root"/> into the already-rendered statement
    /// <paramref name="line"/> broken one operand per line — the first operand kept
    /// where it sits, each later operand on its own continuation-indented line with
    /// the operator <em>leading</em> the broken line (the flags-accumulation house
    /// style, the opposite placement from the short-circuit <see cref="LogicalChainLines"/>
    /// wrapper) — when the chain has at least <see cref="FluentChainMinSegments"/>
    /// operands and the whole statement would exceed <see cref="FluentChainWrapWidth"/>.
    /// Returns null (render inline) otherwise. Each operand's text is exactly the
    /// <see cref="BinaryOperand"/> the flat <see cref="BinaryText"/> emits at the
    /// chain's precedence, and the routine declines unless the per-operand join
    /// reproduces that flat chain text and that text occurs exactly once in the line
    /// — so any chain whose flat spelling is reshaped by the enum-coercion,
    /// mixed-sign, or unchecked-constant special cases (all sibling-context
    /// dependent) stays inline, and the splice is unambiguous. Only the interior
    /// separators of the located chain are broken, so the wrapped form is
    /// token-identical to the inline line — only whitespace differs and the IL is
    /// unchanged. Gated on <see cref="PrinterOptions.WrapSplittableExpressions"/> by
    /// the caller.
    /// </summary>
    string? BitwiseChainLines(Binary root, string line, int indent)
    {
        var operands = new List<IrExpression>();
        CollectBitwiseChainOperands(root, root.Kind, operands);
        if (operands.Count < FluentChainMinSegments)
            return null;

        string op = BinaryOperator(root);
        var precedence = CSharpPrecedence.Of(root);
        var texts = new List<string>(operands.Count);
        for (int i = 0; i < operands.Count; i++)
            texts.Add(BinaryOperand(operands[i], precedence, rightSide: i > 0));

        // The broken form must be a pure whitespace variant of the flat chain:
        // decline unless re-rendering the flattened operands reproduces the flat
        // BinaryText exactly (guards the enum-coercion / mixed-sign / unchecked
        // reshaping the flat renderer applies from sibling context).
        string chainFlat = BinaryText(root);
        if (string.Join($" {op} ", texts) != chainFlat)
            return null;

        // The flat chain must appear exactly once in the rendered statement so the
        // in-place splice is unambiguous (it may sit inside a `return`, an
        // assignment, or a value-preserving cast — all carried verbatim in the
        // surrounding head/tail).
        int idx = line.IndexOf(chainFlat, StringComparison.Ordinal);
        if (idx < 0 || idx != line.LastIndexOf(chainFlat, StringComparison.Ordinal))
            return null;

        if (indent * 4 + line.Length <= FluentChainWrapWidth)
            return null;

        string pad = new(' ', indent * 4);
        string continuation = pad + "    ";
        string head = line[..idx];
        string tail = line[(idx + chainFlat.Length)..];
        var sb = new System.Text.StringBuilder();
        sb.Append(pad).Append(head).Append(texts[0]);
        for (int i = 1; i < texts.Count; i++)
            sb.Append('\n').Append(continuation).Append(op).Append(' ').Append(texts[i]);
        sb.Append(tail);
        return sb.ToString();
    }

    /// <summary>
    /// Flattens a same-kind associative bitwise chain into its operands in
    /// left-to-right source order; a nested chain of a <em>different</em> bitwise
    /// kind (or any non-<see cref="Binary"/>) is one operand and is not descended
    /// into — matching how <see cref="BinaryOperand"/> parenthesizes it.
    /// </summary>
    static void CollectBitwiseChainOperands(IrExpression expression, BinaryKind kind, List<IrExpression> operands)
    {
        if (expression is Binary binary && binary.Kind == kind)
        {
            CollectBitwiseChainOperands(binary.Left, kind, operands);
            CollectBitwiseChainOperands(binary.Right, kind, operands);
            return;
        }
        operands.Add(expression);
    }

    /// <summary>
    /// Renders the initializer for a place whose static type is <paramref name="target"/>:
    /// a target-typed object creation (<c>new(args)</c>) when the value is a plain
    /// <c>new T(args)</c> whose constructed type is exactly the target, otherwise the
    /// ordinary coerced spelling. Reached only from single-target assignment/declaration
    /// positions (local/field/property/stack-slot/indirect store, array-element store)
    /// where the C# target type is unambiguous — never from a call argument, where a
    /// target-typed <c>new</c> would participate in overload resolution and could change
    /// binding. Return positions are intentionally out of scope for now.
    /// </summary>
    string InitializerText(IrExpression value, TypeRef? target)
        => InitializerText(value, target, target);

    /// <summary>
    /// Initializer spelling where the type C# binds a target-typed <c>new()</c> to
    /// (<paramref name="newTarget"/>) can differ from the coercion target
    /// (<paramref name="coercionTarget"/>). They differ only for an array element
    /// store: <c>a[i] = new()</c> binds to the array's static element type, while the
    /// coercion runs through the (possibly wider or <c>stelem.ref</c>-erased)
    /// <c>stelem</c> token. Every other site passes the same type for both.
    /// </summary>
    string InitializerText(IrExpression value, TypeRef? coercionTarget, TypeRef? newTarget)
        => TargetTypedNewText(value, newTarget) ?? CoerceText(value, coercionTarget);

    /// <summary>
    /// Target-typed object creation: <c>T x = new T(args)</c> shortens to
    /// <c>T x = new(args)</c> when the contextual target type is exactly the
    /// constructed type. IL-identical — the target type fixes the constructed type
    /// and therefore the constructor overload set, so both spellings emit the same
    /// <c>newobj T::.ctor(args)</c> — and matches dotnet/runtime's editorconfig
    /// (<c>csharp_style_implicit_object_creation_when_type_is_apparent</c>). Returns
    /// null (keep the explicit spelling) unless the value is a plain object creation
    /// whose type Equals the target: arrays (incl. multi-dimensional, modeled as
    /// <see cref="NewObject"/>), object/collection initializers (a separate node),
    /// tuple and nullable targets, and any base/interface/other target all decline.
    /// A bare <c>System.Object</c> target also declines: the target type may be a
    /// <c>dynamic</c> place (erased to <c>object</c> in the IR), and target-typed
    /// <c>new()</c> is illegal for a <c>dynamic</c> target (CS8752); <c>new object()</c>
    /// carries no type name to drop anyway, so the conservative decline costs nothing.
    /// </summary>
    string? TargetTypedNewText(IrExpression value, TypeRef? target)
    {
        if (!_options.PreferImplicitObjectCreation
            || target is null
            || value is not NewObject creation
            || MultiDimArrayCreationText(creation) is not null
            || IsSystemObjectType(creation.Constructor.DeclaringType)
            || !IsTargetTypedNewEligible(target)
            || !TypeIsApparent(target, creation))
        {
            return null;
        }

        string text = $"new({Arguments(creation.Arguments, creation.Constructor.ParameterTypes, creation.Constructor.ParameterRefKinds)})";
        // This spelling is composed here rather than by Expression(), which is
        // otherwise the one place a node's text is captured. Without recording it
        // the shortened form claims no characters at all, so a fact on the
        // creation falls back to underlining the whole declaration -- exactly the
        // `List<object> sink = new();` case in #3328.
        if (_expressionText is not null)
            _expressionText[creation] = text;
        return text;
    }

    /// <summary>
    /// Whether the declared type of a declaration/creation site is <em>apparent</em>
    /// from the initializer's syntax — the notion dotnet/runtime's editorconfig keys
    /// <c>csharp_style_var_when_type_is_apparent</c> and
    /// <c>csharp_style_implicit_object_creation_when_type_is_apparent</c> both hinge
    /// on. A type is apparent when the right-hand side names it directly: object
    /// creation of exactly that type (<c>new T(...)</c>), a single-dimension array
    /// creation of that type (<c>new T[n]</c>), or an explicit reference cast to it
    /// (<c>(T)x</c>). Deliberately conservative — a form is treated as apparent only
    /// when its <em>rendered</em> spelling is guaranteed to name the type, so a future
    /// opt-in <c>var</c> lens never spells <c>var</c> where the type would silently
    /// vanish. That is why numeric conversions and a target-typed <c>default</c> are
    /// declined here.
    /// <para>
    /// Several further shapes are genuinely apparent in C# but are declined by this v1
    /// because they are modeled by other IR nodes whose rendered form still needs
    /// confirming before a consumer relies on them: a value-type cast
    /// (<see cref="UnboxAny"/>, e.g. <c>(int)obj</c>), an object/collection initializer
    /// (<see cref="ObjectInitializerExpression"/> wrapping the creation), and an
    /// array/span literal (<see cref="ArrayLiteral"/>/<see cref="SpanLiteral"/>, e.g.
    /// <c>new int[] { 1, 2 }</c>). They remain conservative apparency declines; the
    /// broader <c>var</c> policy may still accept a shape through its independent
    /// exact-inference gate.
    /// </para>
    /// Pure over the typed IR (SRM-only, Roslyn-free); the target-typed-<c>new</c>
    /// shortener (<see cref="TargetTypedNewText"/>) and <c>var</c> policy
    /// (<see cref="SpellVar"/>) share this apparency judgment.
    /// </summary>
    internal static bool TypeIsApparent(TypeRef declaredType, IrExpression initializer) => initializer switch
    {
        NewObject creation => declaredType.Equals(creation.Constructor.DeclaringType),
        NewArray array => declaredType.Equals(TypeRef.SzArray(array.ElementType)),
        CastClass cast => declaredType.Equals(cast.Type),
        _ => false,
    };

    /// <summary>
    /// The bare <c>System.Object</c> type, by name — assembly-agnostic so a facade or
    /// spoofed core-library scope still matches. Used to decline target-typed
    /// <c>new()</c> for an <c>object</c>/<c>dynamic</c> target (see
    /// <see cref="TargetTypedNewText"/>).
    /// </summary>
    static bool IsSystemObjectType(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Namespace: "System", Name: "Object" };

    /// <summary>
    /// A target type admits target-typed <c>new</c> only when it is spelled as a plain
    /// constructible type name: a class or struct definition, or a generic instance
    /// that is not <see cref="Nullable{T}"/> (spelled <c>T?</c>) or a
    /// <c>ValueTuple</c> (spelled as a tuple). Pointers, by-refs, arrays, function
    /// pointers, and open generic parameters are all excluded by the kind filter.
    /// </summary>
    static bool IsTargetTypedNewEligible(TypeRef target) => target.Kind switch
    {
        TypeRefKind.Definition => !IsNullableDefinition(target),
        TypeRefKind.GenericInstance => !TypeFamilies.IsNullableType(target) && !IsValueTupleType(target),
        _ => false,
    };

    static bool IsValueTupleType(TypeRef type)
        => type is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" } element,
        }
        && element.Name.StartsWith("ValueTuple`", StringComparison.Ordinal);
}
