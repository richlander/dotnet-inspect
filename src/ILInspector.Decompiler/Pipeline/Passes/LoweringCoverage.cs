namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The compiled completeness ledger for C# idiom recovery: one property per
/// Roslyn <c>LocalRewriter</c> lowering, typed as the raising pass that inverts
/// it — or <see cref="NoImpl"/> for an idiom we still owe, or <see cref="Direct"/>
/// for a lowering the importer or the structuring subsystem handle with no
/// dedicated pass. This is the typed form of the principle in docs/decompiler.md:
/// "Roslyn's <c>Lowering/</c> directory is our completeness checklist."
///
/// <para>A PR that adds a raising pass flips its one property from
/// <see cref="NoImpl"/> to the pass type — the ledger is the roadmap and the
/// burn-down in one place. <c>LoweringCoverageTests</c> reflects over it: it
/// reports idiom coverage, cross-checks every pass-typed entry is registered in
/// <see cref="IrPasses.Default"/>, and fails if this property set drifts from the
/// checked-in snapshot of Roslyn's <c>LocalRewriter/</c> directory.</para>
///
/// <para>Internal and never invoked on a product path (trim-removable) — a
/// build-time checklist, not a runtime component. Synced against dotnet/roslyn
/// <c>src/Compilers/CSharp/Portable/Lowering/LocalRewriter/</c> @ main (60
/// lowerings).</para>
/// </summary>
internal static class LoweringCoverage
{
    // ───────── Idiom lowerings we INVERT — property type IS the raising pass ─────────

    public static InlineArrayCollectionPass CollectionExpression       => new();
    public static NullConditionalPass       ConditionalAccess          => new();
    public static BooleanFoldingPass        ConditionalOperator        => new();
    public static BooleanFoldingPass        NullCoalescingOperator     => new();
    public static DelegateConstructionPass  DelegateCreationExpression => new();
    public static FixedStatementPass        FixedStatement             => new();
    public static LockSugarPass             LockStatement              => new();
    public static StackAllocSpanPass        StackAlloc                 => new();
    public static IncrementDecrementPass    CompoundAssignmentOperator => new();
    public static SwitchRaisingPass         SwitchExpression           => new();
    public static SwitchRaisingPass         PatternSwitchStatement     => new();

    // Control flow is raised by the structuring subsystem; per-method completeness
    // is measured by --gaps, not all-or-nothing here. Mapped to the owning pass.
    public static ForLoopPass               ForStatement               => new();
    public static DoWhileLoopPass           DoStatement                => new();
    public static StructuringPass           WhileStatement             => new();
    public static StructuringPass           IfStatement                => new();
    public static StructuringPass           BreakStatement             => new();
    public static StructuringPass           ContinueStatement          => new();
    public static EhStructuringPass         TryStatement               => new();

    // ───────── Idiom lowerings we still OWE — the roadmap (flip to a pass when done) ─────────

    public static NoImpl IsPatternOperator                   => NoImpl.Owed("x is T t / { Prop: ... }");
    public static NoImpl UsingStatement                      => NoImpl.Owed("using (var x = ...) { }");
    public static NoImpl ForEachStatement                    => NoImpl.Owed("foreach (var x in xs)");
    public static NoImpl StringInterpolation                 => NoImpl.Owed("$\"{a}{b}\"");
    public static NoImpl Range                               => NoImpl.Owed("a[i..j]");
    public static NoImpl Index                               => NoImpl.Owed("a[^1]");
    public static NoImpl TupleCreationExpression             => NoImpl.Owed("(a, b)");
    public static NoImpl TupleBinaryOperator                 => NoImpl.Owed("(a, b) == (c, d)");
    public static NoImpl DeconstructionAssignmentOperator    => NoImpl.Owed("(a, b) = t");
    public static NoImpl NullCoalescingAssignmentOperator    => NoImpl.Owed("a ??= b");
    public static NoImpl ObjectOrCollectionInitializerExpression => NoImpl.Owed("new T { X = 1 }");
    public static NoImpl AnonymousObjectCreation             => NoImpl.Owed("new { a, b }");
    public static NoImpl Query                               => NoImpl.Owed("from x in xs select ...");
    public static NoImpl Await                               => NoImpl.Owed("await t — async state machine");
    public static NoImpl Yield                               => NoImpl.Owed("yield return — iterator state machine");

    // ───────── No idiom to recover: importer-built, or structuring residual (Direct) ─────────

    public static Direct AssignmentOperator        => Direct.Importer;
    public static Direct BinaryOperator            => Direct.Importer;
    public static Direct UnaryOperator             => Direct.Importer;
    public static Direct Call                      => Direct.Importer;
    public static Direct Field                     => Direct.Importer;
    public static Direct Event                     => Direct.Importer;
    public static Direct PropertyAccess            => Direct.Importer;
    public static Direct Conversion                => Direct.Importer;
    public static Direct Literal                   => Direct.Importer;
    public static Direct LocalDeclaration          => Direct.Importer;
    public static Direct MultipleLocalDeclarations => Direct.Importer;
    public static Direct ObjectCreationExpression  => Direct.Importer;
    public static Direct ExpressionStatement       => Direct.Importer;
    public static Direct Block                     => Direct.Importer;
    public static Direct FunctionPointerInvocation => Direct.Importer;
    public static Direct HostObjectMemberReference => Direct.Importer;
    public static Direct PreviousSubmissionReference => Direct.Importer;
    public static Direct PointerElementAccess      => Direct.Importer;
    public static Direct IndexerAccess             => Direct.Importer;
    public static Direct AsOperator                => Direct.Importer;
    public static Direct IsOperator                => Direct.Importer;
    public static Direct StringConcat              => Direct.Importer;
    public static Direct BasePatternSwitchLocalRewriter => Direct.Importer;
    public static Direct ReturnStatement           => Direct.Importer;
    public static Direct ThrowStatement            => Direct.Importer;
    public static Direct GotoStatement             => Direct.Structuring;
    public static Direct LabeledStatement          => Direct.Structuring;
}

/// <summary>
/// An idiom Roslyn lowers to IL that the decompiler does not yet raise — the
/// property's type marks the gap; <see cref="Form"/> is the C# shape we owe.
/// </summary>
internal sealed class NoImpl
{
    public string Form { get; }
    public string? Issue { get; }

    NoImpl(string form, string? issue) { Form = form; Issue = issue; }

    public static NoImpl Owed(string form, string? issue = null) => new(form, issue);
}

/// <summary>
/// A lowering with no dedicated raising pass: either the importer builds the
/// construct directly (no distinct idiom to recover), or the structuring
/// subsystem owns it (control-flow completeness is measured by <c>--gaps</c>,
/// not this ledger).
/// </summary>
internal sealed class Direct
{
    public string Reason { get; }

    Direct(string reason) { Reason = reason; }

    public static Direct Importer { get; } = new("importer/printer builds it directly — no idiom to recover");
    public static Direct Structuring { get; } = new("control-flow residual — completeness tracked by --gaps");
}
