namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Fidelity predicates shared by the two short-circuit re-forms —
/// <see cref="ShortCircuitTernaryPass"/> (constant-arm ternary) and
/// <see cref="BooleanFoldingPass"/>'s guarded-return fold. Both lift a lowered
/// branch shape into a spelled <c>&amp;&amp;</c>/<c>||</c>; these identify the
/// condition and operand shapes where that lift would change the bound C#
/// semantics or produce output that does not compile. Sharing keeps the two
/// passes' hazard sets from drifting apart.
/// </summary>
internal static class ShortCircuitFidelity
{
    /// <summary>
    /// Whether <paramref name="condition"/> is a user-defined-truthiness evaluation
    /// — a call to <c>operator true</c>/<c>operator false</c> the compiler inserts
    /// when a user type is used in boolean context. The printer strips such a call
    /// to its bare user-typed receiver (it renders <c>op_True(a)</c>/<c>op_False(a)</c>
    /// as <c>a</c>), so lifting or returning the receiver rebinds to a user-defined
    /// or nonexistent operator: <c>a || y</c>/<c>a &amp;&amp; y</c> bind the
    /// user-defined conditional <c>|</c>/<c>&amp;</c> (result typed as the user type,
    /// not <c>bool</c>), and a bare <c>return a</c> needs a user→bool conversion that
    /// does not exist. Only a type carrying <c>op_True</c>/<c>op_False</c> reaches
    /// here, so this is the complete rebind set; a primitive-bool condition
    /// (comparison, bool property/field/local/method, nested logical operator) binds
    /// the primitive operator and is safe to lift.
    /// </summary>
    internal static bool IsUserDefinedTruthiness(IrExpression condition)
        => condition is Call { Callee.Name: "op_True" or "op_False" };

    /// <summary>
    /// Whether <paramref name="operand"/> is a managed by-ref (<c>in</c>/<c>ref</c>/
    /// <c>out</c>) dereference. csc treats a managed by-ref as non-null and
    /// side-effect-free, so a spelled <c>a &amp;&amp; *r</c>/<c>a || *r</c> collapses
    /// to a branchless <c>&amp;</c>/<c>|</c> that eagerly dereferences a location the
    /// branch had guarded — an observable <see cref="System.NullReferenceException"/>
    /// divergence on a null by-ref. A raw <em>pointer</em> dereference <c>*p</c> is
    /// deliberately excluded: a pointer read can access-violate, so csc keeps the
    /// branch and lifting it is safe.
    /// </summary>
    internal static bool IsManagedByRefDeref(IrExpression operand)
        => operand is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef };
}
