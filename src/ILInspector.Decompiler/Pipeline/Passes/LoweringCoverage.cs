namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The compiled completeness ledger for C# idiom recovery: one property per
/// Roslyn <c>LocalRewriter</c> lowering (docs/decompiler.md, "Roslyn's
/// <c>Lowering/</c> directory is our completeness checklist"). Each entry is two
/// orthogonal axes:
/// <list type="bullet">
/// <item><b>Mechanism</b> — the property's <em>type</em>: the raising pass that
/// handles it, <see cref="ImporterNative"/> (the importer builds it directly, no
/// transform), or <see cref="Unhandled"/> (no mechanism). The dedicated-vs-shared
/// gradient is derived: a pass type used by one property is dedicated, by several
/// is a shared/general pass.</item>
/// <item><b>Completeness</b> — the <see cref="CompletenessAttribute"/>: how much
/// of the construct comes back as the idiom (<see cref="CompletenessLevel.Full"/>
/// / <see cref="CompletenessLevel.Partial"/> / <see cref="CompletenessLevel.None"/>),
/// with a note for the partial/owed cases.</item>
/// </list>
///
/// <para>These are independent: <c>(Full, ImporterNative)</c> is a mechanical
/// lowering that needs no pass; <c>(Partial, SwitchRaisingPass)</c> is a dedicated
/// pass that only covers some shapes; <c>(None, Unhandled)</c> is an owed idiom. A
/// PR that raises an idiom flips its line — the type to the pass, the completeness
/// up. The structuring rows (if/while/for/try, …) are <c>Partial</c> as a
/// pointer, not a measure: their real completeness is the structurer's <c>--gaps</c>
/// metric, not this attribute. <c>LoweringCoverageTests</c> reflects over it: it
/// reports both axes, cross-checks every pass is registered in
/// <see cref="IrPasses.Default"/>, and enforces the cross-axis invariants.</para>
///
/// <para>The property set is checked for exact equality against the in-repo
/// <c>RoslynLocalRewriters</c> list — an <em>in-repo consistency</em> guard, not a
/// live diff against upstream. Keeping that list synced to dotnet/roslyn's
/// <c>LocalRewriter/</c> directory is a human responsibility (the re-fetch command
/// is in <c>LoweringCoverageTests</c>); when Roslyn adds or renames a lowering, a
/// maintainer updates the list and this ledger together.</para>
///
/// <para>Internal and never invoked on a product path (trim-removable) — a
/// build-time checklist. Synced against dotnet/roslyn
/// <c>src/Compilers/CSharp/Portable/Lowering/LocalRewriter/</c> @ main (60 lowerings).</para>
/// </summary>
internal static class LoweringCoverage
{
    // Stable Roslyn LocalRewriter snapshot order. Do not move rows between
    // status sections; a raise PR should edit only its row's mechanism type and
    // completeness note. Tests derive status groups from reflection.
    [Completeness(CompletenessLevel.Partial, "new { a = x, ... } from the generated anonymous-type constructor, with projection shorthand; equality/ToString members and nested anonymous types are unaffected")]
    public static AnonymousObjectPass AnonymousObjectCreation => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative AsOperator => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative AssignmentOperator => default!;
    [Completeness(CompletenessLevel.Partial, "runtime-async (async v2) exact corelib AsyncHelpers.Await call sites recovered to `await`, multi-await order preserved; classic state-machine async not raised")]
    public static AwaitRecoveryPass Await => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative BasePatternSwitchLocalRewriter => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative BinaryOperator => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Block => default!;
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass BreakStatement => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Call => default!;
    [Completeness(CompletenessLevel.Full)] public static InlineArrayCollectionPass CollectionExpression => new();
    [Completeness(CompletenessLevel.Partial, "value, array-element, field, property, indexer, and ref targets raised, including the checked-context form (`checked(x += v)` recovered as a `checked { x += v; }` block); nested-struct compound not raised")]
    public static IncrementDecrementPass CompoundAssignmentOperator => new();
    [Completeness(CompletenessLevel.Full)] public static NullConditionalPass ConditionalAccess => new();
    [Completeness(CompletenessLevel.Full)] public static BooleanFoldingPass ConditionalOperator => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass ContinueStatement => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Conversion => default!;
    [Completeness(CompletenessLevel.Partial, "local exact BCL ValueTuple deconstruction (arities 2-7) as a fresh-local declaration, existing-local assignment, or a mix of the two over locals ((int x, y) = ...), plus Deconstruct-method calls with a local/parameter receiver; non-local existing targets (parameters/fields) and the temp-then-copy receiver forms not raised")]
    public static DeconstructionAssignmentPass DeconstructionAssignmentOperator => new();
    [Completeness(CompletenessLevel.Full)] public static DelegateConstructionPass DelegateCreationExpression => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static DoWhileLoopPass DoStatement => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Event => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ExpressionStatement => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Field => default!;
    [Completeness(CompletenessLevel.Full)] public static FixedStatementPass FixedStatement => new();
    [Completeness(CompletenessLevel.Partial, "enumerator using/while/current lowering with hidden enumerator local, the single-dimension array lowering (hidden array-copy + index for loop), string foreach lowering (hidden string-copy + index for loop), rank-2 rectangular array lowering, and PDB-discriminated custom pattern enumerators without Dispose; higher-rank arrays and no-symbol custom pattern enumerators not raised")]
    public static ForeachStatementPass ForEachStatement => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static ForLoopPass ForStatement => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative FunctionPointerInvocation => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative GotoStatement => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative HostObjectMemberReference => default!;
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass IfStatement => new();
    [Completeness(CompletenessLevel.Partial, "array/string from-end indexing (constant and variable, a[^n]); Range/slicing not raised")]
    public static IndexFromEndPass Index => new();
    [Completeness(CompletenessLevel.Full)] public static PropertySugarPass IndexerAccess => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative IsOperator => default!;
    [Completeness(CompletenessLevel.Partial, "type pattern `value is T t` (statement guard and && expression) and simple property-pattern equality (`value is T { P: constant }`); positional/list sub-patterns and broader property sub-patterns not raised")]
    public static IsPatternPass IsPatternOperator => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative LabeledStatement => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Literal => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative LocalDeclaration => default!;
    [Completeness(CompletenessLevel.Full)] public static LockSugarPass LockStatement => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative MultipleLocalDeclarations => default!;
    [Completeness(CompletenessLevel.Partial, "local-variable, field, property (instance + static, re-evaluable receiver), and indexer (re-evaluable place) ??= raised")]
    public static NullCoalescingAssignmentPass NullCoalescingAssignmentOperator => new();
    [Completeness(CompletenessLevel.Full)] public static BooleanFoldingPass NullCoalescingOperator => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ObjectCreationExpression => default!;
    [Completeness(CompletenessLevel.Partial, "stack-slot dup form (expression position) and single-use named-local form: named members, indexer members ([k] = v), single-element and multi-argument (dictionary { k, v }) Add elements, and nested initializers (Inner = { X = a } / Items = { e0, e1 }); named locals with extra outside uses are not raised")]
    public static ObjectInitializerPass ObjectOrCollectionInitializerExpression => new();
    [Completeness(CompletenessLevel.Partial, "jump-table and sparse binary-search switch statements + exact BCL String.op_Equality-chain and bucketed switch-on-string forms; pattern switches not raised")]
    public static SwitchRaisingPass PatternSwitchStatement => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative PointerElementAccess => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative PreviousSubmissionReference => default!;
    [Completeness(CompletenessLevel.Full)] public static PropertySugarPass PropertyAccess => new();
    [Completeness(CompletenessLevel.None, "declined, not owed: a query expression is translated to Enumerable.Where/Select/SelectMany/... calls during binding, before any lowering, so it leaves no IL anchor distinct from the equivalent fluent chain. It is recovered as that fluent method chain (the runtime-preferred form); re-sugaring to from..select would invent a distinction the IL does not make (taste rule case 3, no IL anchor)")]
    public static Unhandled Query => default!;
    [Completeness(CompletenessLevel.Partial, "exact BCL RuntimeHelpers.GetSubArray array range slice, from-start and from-end (^n) endpoints (a[i..j], a[i..^1], a[^3..^1], a[..^1], ...), plus compiler-spilled string/span two-bound Substring/Slice forms (s[i..j]); one-sided and from-end string/span forms plus broader manual Substring/Slice calls not raised")]
    public static RangeFromGetSubArrayPass Range => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ReturnStatement => default!;
    [Completeness(CompletenessLevel.Full)] public static StackAllocSpanPass StackAlloc => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative StringConcat => default!;
    [Completeness(CompletenessLevel.Partial, "straight-line exact BCL DefaultInterpolatedStringHandler AppendLiteral/AppendFormatted, including the alignment and format specifiers from the AppendFormatted(value, alignment) / (value, format) / (value, alignment, format) overloads (`{x,5}`, `{x:N2}`, `{x,-10:P1}`); the result is raised wherever the ToStringAndClear call is consumed in the next statement (return, local assignment, argument)")]
    public static StringInterpolationPass StringInterpolation => new();
    [Completeness(CompletenessLevel.Partial, "value-producing jump tables raise to a switch expression; the sparse binary-search form recovers as a switch statement; pattern switches not raised")]
    public static SwitchRaisingPass SwitchExpression => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ThrowStatement => default!;
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static EhStructuringPass TryStatement => new();
    [Completeness(CompletenessLevel.Partial, "tuple-valued `==`/`!=` with hidden ValueTuple operand spills: whole-tuple equality (`left == right`, supported arities 2-7) and arity-2 whole-tuple inequality, the element-literal form (`(a, b) == (c, d)`, any arity, recovered from csc's eager operand spills), and the mixed literal-vs-variable form (`(a, b) == pair`); whole-tuple inequality arity 3+ control-flow forms, nested/rest tuples, and source-named local field comparisons not raised")]
    public static TupleBinaryOperatorPass TupleBinaryOperator => new();
    [Completeness(CompletenessLevel.Partial, "exact BCL ValueTuple constructor arities 2-7; nested TRest/names not recovered")]
    public static TupleCreationPass TupleCreationExpression => new();
    [Completeness(CompletenessLevel.Full)] public static ImporterNative UnaryOperator => default!;
    [Completeness(CompletenessLevel.Partial, "reference-type exact BCL IDisposable null-guard, value-type constrained dispose, and same-assembly value/ref-struct pattern Dispose(); await using not raised")]
    public static UsingStatementPass UsingStatement => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass WhileStatement => new();
    [Completeness(CompletenessLevel.Partial, "yield return — iterator state machine; linear self-contained `yield return <const>;` sequences, yield-nothing iterators (a bare `yield break;`, or self-contained side effects preserved ahead of the break), single counting loops (`for (T i = init; i < bound; i++) yield return f(i);` self-contained modulo the loop variable), structured multi-yield iterators (conditional `if (flag) yield return …;` and several yields per loop), irreducible nested loops (transform-then-restructure: strip the state scaffolding to make the CFG reducible, then re-run the structurer), and foreach-delegation (`foreach (var x in source) yield …;` — strip the enumerator's split-disposal idiom too and recover the foreach) reconstructed (IteratorReconstructionPass); other captured shapes fall back to honest acknowledgment (IteratorAcknowledgmentPass)")] public static IteratorReconstructionPass Yield => new();
}

/// <summary>How much of a lowered construct comes back as the idiom.</summary>
internal enum CompletenessLevel { None, Partial, Full }

/// <summary>The completeness axis of a <see cref="LoweringCoverage"/> entry (the property type carries the mechanism axis).</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class CompletenessAttribute(CompletenessLevel level, string? note = null) : Attribute
{
    public CompletenessLevel Level => level;
    public string? Note => note;
}

/// <summary>Mechanism marker: the importer builds this construct directly out of the stack simulation — no raising pass, no idiom to recover.</summary>
internal sealed class ImporterNative;

/// <summary>Mechanism marker: no raising pass. Usually the owed roadmap — the
/// lowered form is emitted as-is until a pass recovers the idiom. The exception
/// is a construct with no IL anchor (e.g. a query expression, translated to
/// method calls during binding): it is rendered faithfully as its lower-sugar
/// equivalent and is deliberately declined, not owed — the note says which.</summary>
internal sealed class Unhandled;
