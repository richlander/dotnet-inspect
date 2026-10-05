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
/// Operator-call spelling, inverted operator calls, precedence of operator
/// calls, and checked wrapping.
/// </summary>
public sealed partial class CSharpPrinter
{
    /// <summary>True when a non-instance call renders as a C# operator (`a != b`, `-x`) rather than a method invocation — the compound form that must parenthesize as an operand.</summary>
    bool IsOperatorCall(Call call)
        => AnnotatedSourceNodeKindProjection.OperatorKind(call) is not null;

    /// <summary>
    /// The direct idiom for a negated operator-spelled equality/inequality
    /// CALL (`!(Type.op_Equality(a, b))` -> `a != b`, and the reverse), the
    /// call-shaped counterpart of the native <c>ceq</c>-opcode fold above
    /// (#2955). Restricted to <see cref="MemberIdentity.IsKnownCoreLibraryOperator"/>
    /// (currently <see cref="string"/>/<see cref="Type"/>), where the BCL
    /// guarantees op_Equality and op_Inequality are each other's exact logical
    /// inverse for every input — including IEEE-754 float/double, where
    /// <c>NaN == NaN</c> is false and <c>NaN != NaN</c> is true, so the two
    /// remain consistent negations with no unordered-NaN special case to
    /// guard, unlike <c>&lt;</c>/<c>&lt;=</c>/<c>&gt;</c>/<c>&gt;=</c>. C#
    /// requires `==`/`!=` to be declared as a pair but does NOT require their
    /// implementations to be logical inverses of each other, so an arbitrary
    /// user-defined operator pair (recognized by the broader
    /// <see cref="IsOperatorCall"/> spelling guard) is deliberately excluded
    /// here: folding would substitute a call to one method with a call to a
    /// different method, which can observably change behavior for a
    /// maliciously or buggily inconsistent pair. The relational
    /// operator-call family (`op_LessThan` and friends) is folded separately
    /// by <see cref="InvertedRelationalOperatorCallText"/>, restricted to the
    /// total-order core-library value types where the four operators are exact
    /// duals. Returns null when the call does not actually spell
    /// as `==`/`!=` (an unrelated method that happens to be named
    /// op_Equality/op_Inequality without the metadata operator flag renders
    /// as a plain call, and `!` negating its result is already correct
    /// as-is).
    /// </summary>
    string? InvertedEqualityOperatorCallText(Call call)
        => call is { Arguments: [var left, var right] } && MemberIdentity.IsKnownCoreLibraryOperator(call.Callee)
            ? call.Callee.Name switch
            {
                "op_Equality" => $"{OperatorOperand(left)} != {OperatorOperand(right)}",
                "op_Inequality" => $"{OperatorOperand(left)} == {OperatorOperand(right)}",
                _ => null,
            }
            : null;

    /// <summary>
    /// The direct idiom for a negated relational operator-spelled CALL on a
    /// known total-order core-library value type:
    /// <c>!(decimal.op_LessThan(a, b))</c> -> <c>a &gt;= b</c>, with the De Morgan
    /// duals for <c>&lt;=</c>/<c>&gt;</c>/<c>&gt;=</c>. The relational counterpart of
    /// the equality fold above and the native <c>clt</c>/<c>cgt</c> fold (#2955),
    /// gated on <see cref="MemberIdentity.IsTotalOrderRelationalOperator"/> —
    /// <see cref="decimal"/>, <see cref="System.DateTime"/>,
    /// <see cref="System.DateTimeOffset"/>, <see cref="System.TimeSpan"/>,
    /// <see cref="System.DateOnly"/>, <see cref="System.TimeOnly"/> — the total
    /// orders whose four relational operators are exact duals for every input.
    /// <see cref="System.Half"/> (IEEE-754 partial order: <c>NaN</c> is unordered,
    /// so <c>!(a &lt; b)</c> can differ from <c>a &gt;= b</c>) and every
    /// user-defined operator type are excluded there, so a relational negation is
    /// never rewritten for a type where the operators can disagree;
    /// <see cref="float"/>/<see cref="double"/> never reach this path (native
    /// <c>clt</c>/<c>cgt</c>, folded with the unordered-flag guard). Returns null
    /// off the allowlist, leaving the un-folded operator call to be parenthesized.
    /// </summary>
    string? InvertedRelationalOperatorCallText(Call call)
        => call is { Arguments: [var left, var right] } && MemberIdentity.IsTotalOrderRelationalOperator(call.Callee)
            ? call.Callee.Name switch
            {
                "op_LessThan" => $"{OperatorOperand(left)} >= {OperatorOperand(right)}",
                "op_LessThanOrEqual" => $"{OperatorOperand(left)} > {OperatorOperand(right)}",
                "op_GreaterThan" => $"{OperatorOperand(left)} <= {OperatorOperand(right)}",
                "op_GreaterThanOrEqual" => $"{OperatorOperand(left)} < {OperatorOperand(right)}",
                _ => null,
            }
            : null;

    /// <summary>
    /// True when an expression is legal as a C# expression statement: an
    /// invocation, object creation, await, or inc/decrement. An operator-spelled
    /// call (`a != b`) renders as a value, not a statement, so it is excluded.
    /// Any other value is CS0201 as a statement and must be discarded with `_ =`.
    /// </summary>
    bool IsStatementExpression(IrExpression expression) => expression switch
    {
        Call call => !IsOperatorCall(call),
        NullConditional { Member: Call call } => !IsOperatorCall(call),
        CallIndirect or NewObject or IncrementDecrement or AwaitExpression or LocalFunctionInvocation => true,
        _ => false,
    };

    static Precedence? OperatorCallPrecedence(Call call)
    {
        var arguments = call.Arguments;
        string name = call.Callee.Name;
        if (name.StartsWith("op_Checked", StringComparison.Ordinal))
            name = "op_" + name["op_Checked".Length..];

        return arguments.Count switch
        {
            2 => name switch
            {
                "op_Equality" or "op_Inequality" => Precedence.Equality,
                "op_LessThan" or "op_LessThanOrEqual" or "op_GreaterThan" or "op_GreaterThanOrEqual" => Precedence.Relational,
                "op_Addition" or "op_Subtraction" => Precedence.Additive,
                "op_Multiply" or "op_Division" or "op_Modulus" => Precedence.Multiplicative,
                "op_BitwiseAnd" => Precedence.BitwiseAnd,
                "op_BitwiseOr" => Precedence.BitwiseOr,
                "op_ExclusiveOr" => Precedence.BitwiseXor,
                "op_LeftShift" or "op_RightShift" or "op_UnsignedRightShift" => Precedence.Shift,
                _ => null,
            },
            1 => name switch
            {
                "op_UnaryNegation" or "op_UnaryPlus" or "op_LogicalNot" or "op_OnesComplement"
                    or "op_Implicit" or "op_Explicit" => Precedence.Unary,
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>
    /// True when <paramref name="type"/> is a value type, so an <c>isinst</c>
    /// type-test must spell <c>obj is T</c> — <c>obj as T</c> is CS0077 on a
    /// non-nullable value type. Primitives are value types intrinsically;
    /// other definitions resolve through the shape map (enums included).
    /// </summary>
    bool IsValueTypeTarget(TypeRef type)
        => TypeFamilies.IsNumericPrimitive(type)
            || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
            || !TypeFamilies.IsNullableType(type)
                && _function.TypeShapes.GetValueOrDefault(NamedDefinition(type)) is TypeShape.ValueType or TypeShape.Enum;

    /// <summary>The operator form of an op_* call, or null when the name has no spelling (op_True/op_False and friends stay as calls).</summary>
    string? OperatorSpelling(Call call)
    {
        var arguments = call.Arguments;

        // User-defined checked operators (C# 11). The metadata name encodes the
        // checked overload (op_CheckedAddition, op_CheckedSubtraction, ...); the
        // faithful spelling wraps the operator form in checked(...) so the same
        // overload is selected, collapsing the wrapper inside an enclosing checked
        // context. Without this the call falls through to a method spelling
        // (T.op_CheckedAddition(a, b)) that is CS0571 "cannot explicitly call
        // operator" — invalid Full. See #1706.
        if (call.Callee.Name.StartsWith("op_Checked", StringComparison.Ordinal))
            return CheckedOperatorSpelling(call);

        if (arguments.Count == 2)
        {
            string? op = call.Callee.Name switch
            {
                "op_Equality" => "==", "op_Inequality" => "!=",
                "op_LessThan" => "<", "op_LessThanOrEqual" => "<=",
                "op_GreaterThan" => ">", "op_GreaterThanOrEqual" => ">=",
                "op_Addition" => "+", "op_Subtraction" => "-",
                "op_Multiply" => "*", "op_Division" => "/", "op_Modulus" => "%",
                "op_BitwiseAnd" => "&", "op_BitwiseOr" => "|", "op_ExclusiveOr" => "^",
                "op_LeftShift" => "<<", "op_RightShift" => ">>",
                "op_UnsignedRightShift" => ">>>",
                _ => null,
            };
            return op is null ? null : $"{OperatorOperand(arguments[0])} {op} {OperatorOperand(arguments[1])}";
        }
        if (arguments.Count == 1)
        {
            return call.Callee.Name switch
            {
                "op_UnaryNegation" => $"-{OperatorOperand(arguments[0])}",
                "op_UnaryPlus" => $"+{OperatorOperand(arguments[0])}",
                "op_LogicalNot" => $"!{OperatorOperand(arguments[0])}",
                "op_OnesComplement" => $"~{OperatorOperand(arguments[0])}",
                "op_Implicit" or "op_Explicit" => ConversionOperatorSpelling(call.Callee.ReturnType, arguments[0]),
                _ => null,
            };
        }
        return null;
    }

    /// <summary>
    /// An operand of a user-defined operator call. The operator's parameters may
    /// be <c>in</c>/<c>ref</c>, so the IL passes the operand's address
    /// (<c>ldarga</c>/<c>ldloca</c>/<c>ldflda</c>); C# operator syntax takes that
    /// address implicitly, so the operand is the place itself — <c>a != b</c>, not
    /// the CS1525 <c>(ref a) != (ref b)</c>. Strip the address-of; other operands
    /// render normally.
    /// </summary>
    string OperatorOperand(IrExpression argument)
    {
        if (argument is not (LoadArgumentAddress or LoadLocalAddress or LoadFieldAddress or LoadElementAddress))
            return Operand(argument);

        string text = Deref(argument);
        return WithNodeKind(
            argument,
            text,
            DereferencedSurfaceKind(argument, text));
    }

    string ConversionOperatorSpelling(TypeRef target, IrExpression value)
    {
        string targetText = TypeText(target);
        string operand = CastOperand(OperatorOperand(value), targetText);
        return $"({targetText}){operand}";
    }

    /// <summary>The checked-context spelling of a user-defined checked operator call (op_Checked*), or null when the name has no faithful operator form.</summary>
    string? CheckedOperatorSpelling(Call call)
    {
        var arguments = call.Arguments;

        // checked explicit conversion: checked((T)x).
        if (call.Callee.Name == "op_CheckedExplicit" && arguments.Count == 1)
            return WrapChecked(() => ConversionOperatorSpelling(call.Callee.ReturnType, arguments[0]));

        // The remaining checked operators share their symbol with the unchecked
        // form (op_CheckedAddition → "+"); reuse the single mapping the signature
        // renderer uses. Increment/decrement are folded to ++/-- upstream by
        // IncrementDecrementPass (#1712); a checked increment call that survives
        // to here (an unfolded shape) has no faithful functional spelling, so it
        // falls through to null (a method call).
        string? symbol = OperatorNames.MapBinaryOrUnary(call.Callee.Name["op_Checked".Length..]);
        return (symbol, arguments.Count) switch
        {
            ("+" or "-" or "*" or "/", 2)
                => WrapChecked(() => $"{OperatorOperand(arguments[0])} {symbol} {OperatorOperand(arguments[1])}"),
            ("-", 1) // op_CheckedUnaryNegation
                => WrapChecked(() => $"-{OperatorOperand(arguments[0])}"),
            _ => null,
        };
    }

    /// <summary>Wraps an operator spelling in <c>checked(...)</c>, rendering its operands in a checked context so nested checked operators collapse; an enclosing checked context drops the redundant wrapper.</summary>
    string WrapChecked(Func<string> render)
    {
        if (_checkedContext)
            return render();
        _checkedContext = true;
        try
        {
            return $"checked({render()})";
        }
        finally
        {
            _checkedContext = false;
        }
    }
}
