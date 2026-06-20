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
    // ───────── Raised by a pass — fully ─────────
    [Completeness(CompletenessLevel.Full)] public static InlineArrayCollectionPass      CollectionExpression       => new();
    [Completeness(CompletenessLevel.Full)] public static NullConditionalPass            ConditionalAccess          => new();
    [Completeness(CompletenessLevel.Full)] public static BooleanFoldingPass             ConditionalOperator        => new();
    [Completeness(CompletenessLevel.Full)] public static BooleanFoldingPass             NullCoalescingOperator     => new();
    [Completeness(CompletenessLevel.Full)] public static DelegateConstructionPass       DelegateCreationExpression => new();
    [Completeness(CompletenessLevel.Full)] public static FixedStatementPass             FixedStatement             => new();
    [Completeness(CompletenessLevel.Full)] public static LockSugarPass                  LockStatement              => new();
    [Completeness(CompletenessLevel.Full)] public static StackAllocSpanPass             StackAlloc                 => new();
    [Completeness(CompletenessLevel.Full)] public static PropertySugarPass              PropertyAccess             => new();
    [Completeness(CompletenessLevel.Full)] public static PropertySugarPass              IndexerAccess              => new();

    // ───────── Raised by a pass — partially (the "finish these" roadmap) ─────────
    [Completeness(CompletenessLevel.Partial, "value-producing jump tables only; sparse + pattern switches still emit a statement")]
    public static SwitchRaisingPass SwitchExpression => new();
    [Completeness(CompletenessLevel.Partial, "jump-table switch statements; pattern + switch-on-string not raised")]
    public static SwitchRaisingPass PatternSwitchStatement => new();
    [Completeness(CompletenessLevel.Partial, "value, array-element, field, property, and ref targets raised; checked-context and indexer/nested-struct compound not raised")]
    public static IncrementDecrementPass CompoundAssignmentOperator => new();
    [Completeness(CompletenessLevel.Partial, "stack-slot dup form (expression position); named-local, indexer-element, nested, and multi-arg Add (dictionary) initializers not raised")]
    public static ObjectInitializerPass ObjectOrCollectionInitializerExpression => new();
    [Completeness(CompletenessLevel.Partial, "reference-type IDisposable null-guard and value-type constrained dispose; ref-struct pattern dispose and await using not raised")]
    public static UsingStatementPass UsingStatement => new();
    [Completeness(CompletenessLevel.Partial, "type pattern `value is T t` (statement guard and && expression); property/positional/list sub-patterns recover as `is T t && ...`, not the deconstructed syntax")]
    public static IsPatternPass IsPatternOperator => new();
    [Completeness(CompletenessLevel.Partial, "straight-line DefaultInterpolatedStringHandler AppendLiteral/AppendFormatted-to-return only")]
    public static StringInterpolationPass StringInterpolation => new();
    [Completeness(CompletenessLevel.Partial, "local-variable ??= only; fields/properties/indexers not raised")]
    public static NullCoalescingAssignmentPass NullCoalescingAssignmentOperator => new();
    [Completeness(CompletenessLevel.Partial, "ValueTuple constructor arities 2-7; nested TRest/names not recovered")]
    public static TupleCreationPass TupleCreationExpression => new();
    [Completeness(CompletenessLevel.Partial, "array/string constant-from-end indexing only; Range/slicing not raised")]
    public static IndexFromEndPass Index => new();

    // ───────── Raised by the structuring subsystem — completeness is --gaps, not binary ─────────
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass   IfStatement       => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass   WhileStatement    => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass   BreakStatement    => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static StructuringPass   ContinueStatement => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static DoWhileLoopPass  DoStatement       => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static ForLoopPass      ForStatement      => new();
    [Completeness(CompletenessLevel.Partial, "control flow — completeness tracked by --gaps")] public static EhStructuringPass TryStatement      => new();

    // ───────── Owed — no mechanism yet (the "start these" roadmap) ─────────
    [Completeness(CompletenessLevel.None, "foreach (var x in xs)")]      public static Unhandled ForEachStatement                    => default!;
    [Completeness(CompletenessLevel.None, "a[i..j]")]                    public static Unhandled Range                               => default!;
    [Completeness(CompletenessLevel.None, "(a, b) == (c, d)")]           public static Unhandled TupleBinaryOperator                 => default!;
    [Completeness(CompletenessLevel.None, "(a, b) = t")]                 public static Unhandled DeconstructionAssignmentOperator    => default!;
    [Completeness(CompletenessLevel.None, "new { a, b }")]               public static Unhandled AnonymousObjectCreation             => default!;
    [Completeness(CompletenessLevel.None, "from x in xs select ...")]    public static Unhandled Query                               => default!;
    [Completeness(CompletenessLevel.None, "await t — async state machine")]      public static Unhandled Await => default!;
    [Completeness(CompletenessLevel.None, "yield return — iterator state machine")] public static Unhandled Yield => default!;

    // ───────── Importer-native — no idiom to recover (built directly by the stack simulation) ─────────
    [Completeness(CompletenessLevel.Full)] public static ImporterNative AssignmentOperator        => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative BinaryOperator            => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative UnaryOperator             => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Call                      => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Field                     => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Event                     => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Conversion                => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Literal                   => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative LocalDeclaration          => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative MultipleLocalDeclarations => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ObjectCreationExpression  => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ExpressionStatement       => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative Block                     => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative FunctionPointerInvocation => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative HostObjectMemberReference => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative PreviousSubmissionReference => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative PointerElementAccess      => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative AsOperator                => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative IsOperator                => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative StringConcat              => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative BasePatternSwitchLocalRewriter => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ReturnStatement           => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative ThrowStatement            => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative GotoStatement             => default!;
    [Completeness(CompletenessLevel.Full)] public static ImporterNative LabeledStatement          => default!;
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

/// <summary>Mechanism marker: no mechanism yet — the lowered form is emitted as-is. The owed roadmap.</summary>
internal sealed class Unhandled;
