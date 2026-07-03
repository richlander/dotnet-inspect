using System;

namespace ILInspector.Decompiler.Pipeline.InverseArchitecture;

// The co-located half of the inverse architecture
// (docs/design/inverse-architecture.md). These attributes and enums annotate IR
// nodes with the forward construct they invert and the assumption that makes the
// inverse sound; a test/tooling reflector turns them into the generated node
// ledger, and the coverage test enforces that they stay honest. This is metadata
// only — it adds no product-path behavior and keeps the shipped decompiler
// SRM-only and NativeAOT-friendly.

/// <summary>
/// The forward-compiler construct an inverse IR node undoes. The decompiler is
/// the confident inverse of Roslyn's IL emission and a sibling of RyuJIT's
/// importer; a node names the forward construct it is the inverse of.
/// </summary>
public enum Forward
{
    /// <summary>No forward construct — a decompiler-native node.</summary>
    None,

    /// <summary>Roslyn's <c>BoundConversion</c> (the bound-tree conversion node).</summary>
    RoslynBoundConversion,

    /// <summary>Roslyn's <c>BoundParameter</c> (a method parameter / <c>this</c> reference).</summary>
    RoslynBoundParameter,

    /// <summary>Roslyn's <c>BoundLocal</c> (a local variable reference).</summary>
    RoslynBoundLocal,

    /// <summary>Roslyn's <c>BoundFieldAccess</c> (an instance or static field read).</summary>
    RoslynBoundFieldAccess,

    /// <summary>Roslyn's <c>BoundBinaryOperator</c> (arithmetic, bitwise, shift, and comparison).</summary>
    RoslynBoundBinaryOperator,

    /// <summary>Roslyn's <c>BoundUnaryOperator</c> (negation, bitwise complement).</summary>
    RoslynBoundUnaryOperator,

    /// <summary>Roslyn's <c>BoundPropertyAccess</c> / indexer access (a property or indexer read).</summary>
    RoslynBoundPropertyAccess,

    /// <summary>Roslyn's <c>BoundConditionalAccess</c> (the <c>?.</c> null-conditional access).</summary>
    RoslynBoundConditionalAccess,

    /// <summary>Roslyn's <c>BoundArrayAccess</c> (an array element reference).</summary>
    RoslynBoundArrayAccess,

    /// <summary>Roslyn's <c>BoundFunctionPointerLoad</c> (the <c>&amp;method</c> function-pointer load).</summary>
    RoslynBoundFunctionPointerLoad,

    /// <summary>Roslyn's <c>BoundFunctionPointerInvocation</c> (a <c>delegate*</c> invocation).</summary>
    RoslynBoundFunctionPointerInvocation,

    /// <summary>Roslyn's <c>BoundArrayCreation</c> (a <c>new T[n]</c> array creation).</summary>
    RoslynBoundArrayCreation,

    /// <summary>Roslyn's <c>BoundArrayLength</c> (an array's <c>.Length</c>).</summary>
    RoslynBoundArrayLength,

    /// <summary>Roslyn's <c>BoundTypeOfOperator</c> (a <c>typeof(T)</c> expression).</summary>
    RoslynBoundTypeOfOperator,

    /// <summary>Roslyn's <c>BoundSizeOfOperator</c> (a <c>sizeof(T)</c> expression).</summary>
    RoslynBoundSizeOfOperator,

    /// <summary>Roslyn's <c>BoundAsOperator</c> (a <c>value as T</c> type test).</summary>
    RoslynBoundAsOperator,

    /// <summary>Roslyn's <c>BoundCall</c> (a method or local-function invocation).</summary>
    RoslynBoundCall,

    /// <summary>Roslyn's <c>BoundObjectCreationExpression</c> (a <c>new T(...)</c> object creation).</summary>
    RoslynBoundObjectCreationExpression,

    /// <summary>Roslyn's <c>BoundDelegateCreationExpression</c> (a delegate / method-group conversion).</summary>
    RoslynBoundDelegateCreationExpression,

    /// <summary>Roslyn's <c>BoundLambda</c> (a <c>(params) =&gt; body</c> lambda).</summary>
    RoslynBoundLambda,

    /// <summary>Roslyn's <c>BoundRangeExpression</c> (a <c>start..end</c> range).</summary>
    RoslynBoundRangeExpression,

    /// <summary>Roslyn's <c>BoundFromEndIndexExpression</c> (a <c>^n</c> from-end index).</summary>
    RoslynBoundFromEndIndexExpression,

    /// <summary>Roslyn's <c>BoundStackAllocArrayCreation</c> (a <c>stackalloc T[n]</c> expression).</summary>
    RoslynBoundStackAllocArrayCreation,
}

/// <summary>
/// The soundness oracle a node's type recovery is bounded by. RyuJIT's importer
/// solves the same erased-type recovery problem from the same IL, so it bounds
/// what the decompiler may soundly assert.
/// </summary>
public enum Oracle
{
    /// <summary>No oracle bound declared.</summary>
    None,

    /// <summary>RyuJIT's ECMA-335 stack normalization (<c>bool</c>/<c>byte</c>/<c>short</c> widened to <c>int32</c>).</summary>
    RyuJitStackNormalization,

    /// <summary>RyuJIT's IL-importer type reconstruction, generally.</summary>
    RyuJitImporter,
}

/// <summary>
/// Whether an inverse node's name follows, inverts, or is native to the forward
/// vocabulary. Native names carry the highest drift risk — no forward anchor —
/// and form the standing rename-review queue.
/// </summary>
public enum NameProvenance
{
    /// <summary>Deliberately follows a forward name (e.g. <c>Convert</c> ← <c>BoundConversion</c>).</summary>
    Inherited,

    /// <summary>A deliberate antonym of the forward name (e.g. <c>raise</c> ← <c>lower</c>).</summary>
    Inverted,

    /// <summary>No forward analog — decompiler-born; must carry its own rationale.</summary>
    Native,
}

/// <summary>
/// Marks an IR node as the inverse of a forward-compiler construct (the node
/// ledger of docs/design/inverse-architecture.md). The reflector reads these to
/// generate the ledger; the coverage test enforces that every declared in-domain
/// node carries this or <see cref="NotInvertedAttribute"/>, and that any
/// <see cref="Assumes"/> resolves to a runnable predicate on
/// <see cref="InverseAssumptions"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InverseOfAttribute : Attribute
{
    public InverseOfAttribute(
        Forward forward,
        NameProvenance naming = NameProvenance.Native,
        Oracle oracle = Oracle.None,
        string? forwardName = null,
        string? precondition = null,
        string? assumes = null,
        string? witness = null)
    {
        Forward = forward;
        Naming = naming;
        Oracle = oracle;
        ForwardName = forwardName;
        Precondition = precondition;
        Assumes = assumes;
        Witness = witness;
    }

    /// <summary>The forward construct this node is the inverse of.</summary>
    public Forward Forward { get; }

    /// <summary>Whether this node's name follows, inverts, or is native to the forward vocabulary.</summary>
    public NameProvenance Naming { get; }

    /// <summary>The soundness oracle bounding this node's type recovery.</summary>
    public Oracle Oracle { get; }

    /// <summary>The forward construct's own name — the ledger's forward-construct column.</summary>
    public string? ForwardName { get; }

    /// <summary>Descriptive precondition under which the inverse is exact — the ledger's Precondition column.</summary>
    public string? Precondition { get; }

    /// <summary>
    /// Name of the runnable assumption predicate on <see cref="InverseAssumptions"/>
    /// that verifies <see cref="Precondition"/>. When set it must resolve to a
    /// release-capable <c>Check(IrFunction)</c>; the coverage test enforces this
    /// and structurally invokes it (per-predicate behavior is covered by
    /// predicate-specific tests).
    /// </summary>
    public string? Assumes { get; }

    /// <summary>Descriptive witnesses that prove the inverse — the ledger's Witness column (fixtures, tests, harness modes).</summary>
    public string? Witness { get; }
}

/// <summary>
/// Marks an IR node as deliberately outside the inverse's checkable domain
/// (docs/design/inverse-architecture.md, "Declared non-inverse boundaries"). It
/// satisfies the coverage rule without asserting an inverse, so a boundary is
/// visible rather than an unexplained gap.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotInvertedAttribute : Attribute
{
    public NotInvertedAttribute(string reason) => Reason = reason;

    /// <summary>Why this node is outside the checkable inverse domain.</summary>
    public string Reason { get; }
}
