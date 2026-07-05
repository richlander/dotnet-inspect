namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// C#'s operator precedence levels, ordered so a HIGHER value binds tighter.
/// The parenthesization rule (#2376, phase 1): a child renders bare in a
/// context iff its precedence is strictly greater than the context's demand
/// (or equal, on the associativity-safe side — the context passes the
/// tightened demand for its hazard side). This is the printer's legitimate
/// surface-spelling job (value-typed-emission.md, "the output half"; ILSpy's
/// InsertParenthesesVisitor is the prior art) — precedence never migrates
/// into passes.
/// </summary>
public enum Precedence
{
    /// <summary>Assignment, lambda — the loosest level the printer emits in expression position.</summary>
    Assignment = 0,
    /// <summary>The conditional operator <c>c ? a : b</c> (right-associative; its CONDITION is the hazard side).</summary>
    Conditional = 1,
    /// <summary>The null-coalescing operator <c>??</c> (right-associative; its LEFT operand is the hazard side).</summary>
    NullCoalescing = 2,
    ConditionalOr = 3,
    ConditionalAnd = 4,
    BitwiseOr = 5,
    BitwiseXor = 6,
    BitwiseAnd = 7,
    Equality = 8,
    /// <summary>Relational and type-testing: <c>&lt;</c> <c>&gt;</c> <c>&lt;=</c> <c>&gt;=</c> <c>is</c> <c>as</c>.</summary>
    Relational = 9,
    Shift = 10,
    Additive = 11,
    Multiplicative = 12,
    /// <summary>Unary prefix forms: <c>!</c> <c>-</c> <c>~</c> casts <c>await</c> prefix <c>++/--</c>.</summary>
    Unary = 13,
    /// <summary>Primary: literals, names, member access, invocation, <c>new</c>, <c>typeof</c>, indexing, postfix <c>++/--</c>, <c>checked(...)</c>/<c>unchecked(...)</c>.</summary>
    Primary = 14,
}

/// <summary>
/// A rendered expression fragment carrying the precedence of its OWN text —
/// the load-bearing phase-1 decision (#2376): composing helpers that emit
/// text with no single node behind it (a <c>b ? 1 : 0</c> truthiness
/// composition, a cast form) report what they actually produced, so
/// parenthesization reasons over rendered shapes and never rescans strings.
/// </summary>
public readonly record struct Rendered(string Text, Precedence Precedence)
{
    /// <summary>The fragment's text, parenthesized iff the context binds tighter than the fragment.</summary>
    public string At(Precedence context)
        => Precedence >= context ? Text : $"({Text})";

    public static Rendered Primary(string text) => new(text, Precedence.Primary);

    public override string ToString() => Text;
}

/// <summary>
/// The node-shape half of the precedence model: the precedence of a node's
/// STRUCTURAL rendering. Context-sensitive renderers (a <see cref="Coerce"/>
/// may spell a bare name, a cast, or a truthiness ternary) are reported at
/// their loosest possible form — conservative wraps are never invalid; the
/// composing helpers return <see cref="Rendered"/> with the exact value where
/// the difference matters.
/// </summary>
public static class CSharpPrecedence
{
    public static Precedence Of(IrExpression node) => node switch
    {
        // Loosest structural forms first.
        Conditional or SwitchExpression or UnionSwitchExpression => Precedence.Conditional,
        Coalesce => Precedence.NullCoalescing,
        LogicalBinary { Kind: LogicalKind.Or } => Precedence.ConditionalOr,
        LogicalBinary => Precedence.ConditionalAnd,
        Binary { Kind: BinaryKind.Or } => Precedence.BitwiseOr,
        Binary { Kind: BinaryKind.Xor } => Precedence.BitwiseXor,
        Binary { Kind: BinaryKind.And } => Precedence.BitwiseAnd,
        Comparison { Kind: ComparisonKind.Equal or ComparisonKind.NotEqual } => Precedence.Equality,
        Comparison => Precedence.Relational,
        IsInstance or IsPattern or RecursivePropertyDeclarationPattern
            or SingleElementListPattern or PositionalPattern => Precedence.Relational,
        Binary { Kind: BinaryKind.ShiftLeft or BinaryKind.ShiftRight } => Precedence.Shift,
        Binary { Kind: BinaryKind.Add or BinaryKind.Subtract } => Precedence.Additive,
        Binary => Precedence.Multiplicative,
        // Casts and prefix unary forms. Convert/CastClass/Unbox/UnboxAny spell
        // casts; Box is invisible or a cast; Coerce's loosest spelling is the
        // truthiness ternary, so its structural report is Conditional — the
        // composing path returns the exact Rendered where it matters.
        Unary or LogicalNot or AwaitExpression => Precedence.Unary,
        Convert or CastClass or Unbox or UnboxAny or Box => Precedence.Unary,
        Coerce => Precedence.Conditional,
        IncrementDecrement { IsPrefix: true } => Precedence.Unary,
        // Everything else — loads, constants, calls, creations, literals,
        // postfix ++/--, typeof, ranges, collection forms — renders primary.
        // (An operator-spelled Call is the documented exception, handled where
        // Operand already distinguishes it; the structural default stays
        // Primary because invocation is the overwhelmingly common spelling.)
        _ => Precedence.Primary,
    };
}
