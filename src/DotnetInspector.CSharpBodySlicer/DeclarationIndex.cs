using System.Collections.Immutable;

namespace DotnetInspector.CSharpBodySlicer;

/// <summary>
/// The kind of declaration a <see cref="DeclarationSpan"/> describes.
/// <para>
/// The set names the distinctions a slicer caller actually asks about — is this a scope that can
/// contain members, does it have a body, is it a member of a type — and stops there. It does not
/// model accessors, parameters, or type parameters, because nothing here needs them and inventing
/// them would imply a fidelity this index does not have.
/// </para>
/// </summary>
public enum DeclarationKind
{
    /// <summary>A namespace declaration, block-scoped or file-scoped.</summary>
    Namespace,

    /// <summary>A <c>class</c> declaration.</summary>
    Class,

    /// <summary>A <c>struct</c> declaration.</summary>
    Struct,

    /// <summary>An <c>interface</c> declaration.</summary>
    Interface,

    /// <summary>A <c>record</c>, <c>record class</c>, or <c>record struct</c> declaration.</summary>
    Record,

    /// <summary>An <c>enum</c> declaration.</summary>
    Enum,

    /// <summary>A <c>delegate</c> declaration.</summary>
    Delegate,

    /// <summary>A method, including operators and local-scope-free generic methods.</summary>
    Method,

    /// <summary>An instance or static constructor.</summary>
    Constructor,

    /// <summary>A finalizer, spelled <c>~Type()</c>.</summary>
    Destructor,

    /// <summary>A property or indexer.</summary>
    Property,

    /// <summary>An event, field-like or with accessors.</summary>
    Event,

    /// <summary>A field, including <c>const</c>.</summary>
    Field,

    /// <summary>One member of an <see cref="Enum"/> declaration.</summary>
    EnumMember,
}

/// <summary>
/// An inclusive run of source lines, 1-based, matching <see cref="DeclarationSpan"/>.
/// <para>
/// Line-granular by design, so a construct sharing a line with another — the <c>{</c> of
/// <c>class A { }</c>, the <c>=&gt;</c> of <c>int P =&gt; 1;</c> — is not separable from it here.
/// Callers that need sub-line precision are slicing text the PDB cannot address either.
/// </para>
/// </summary>
public readonly record struct LineRange(int StartLine, int EndLine)
{
    /// <summary>How many lines the range covers.</summary>
    public int LineCount => EndLine - StartLine + 1;

    public override string ToString() => StartLine == EndLine ? $"{StartLine}" : $"{StartLine}-{EndLine}";
}

/// <summary>
/// One declaration found in a C# source file, with the line spans that bound it.
/// <para>
/// All line numbers are <b>1-based physical lines of the file that was indexed</b>, so a caller
/// never converts at the boundary where an off-by-one would silently select the neighbouring
/// member. They normally coincide with portable-PDB sequence points, but that correspondence is
/// the <i>caller's</i> to establish, not a property this type asserts: a <c>#line</c> directive
/// remaps what the PDB reports, in both line number and document name. Measured against a real
/// build, <c>#line 500 "elsewhere.cs"</c> above a method makes every one of its sequence points
/// read <c>elsewhere.cs:500</c> while this type reports the physical line it occupies. This is a
/// <b>known, ungated limitation</b> of correlating a row with debug info, raised in adversarial
/// review round 5 of PR #3680 by GPT-5.6 Sol; it is a property of any physical-line index and is
/// not affected by conditional recovery, which changes only which rows are vouched for and never
/// which lines they name. It is rare (4 of 20,838 files in <c>dotnet/runtime/src/libraries</c>
/// carry a renumbering <c>#line</c>, and none of those four is a conditional file), and it belongs
/// to whichever layer performs the PDB-to-row match.
/// </para>
/// </summary>
/// <param name="Kind">What was declared.</param>
/// <param name="Name">
/// The declared name, without type parameters or parameter list, and <b>without the <c>@</c> of a
/// verbatim identifier</b>: <c>class @class</c> is named <c>class</c> and <c>namespace @event</c>
/// is named <c>event</c>. The name exists to correlate a row with a metadata member, and metadata
/// never carries the escape, so reporting it would make every verbatim declaration fail to match.
/// This is therefore the declared name, not the source spelling; slice
/// <see cref="SignatureStartLine"/> to recover how it was written. Gated by
/// <c>DeclarationIndexTests.AVerbatimIdentifier_IsNamedWithoutItsEscape</c>. An operator
/// is named <c>operator +</c>, or <c>operator checked +</c> when declared <c>checked</c>, because
/// that is a distinct member (<c>op_CheckedAddition</c>, not <c>op_Addition</c>); a conversion is
/// named <c>operator implicit</c> or <c>operator explicit</c>, without its target type, so two
/// conversions from the same type share a name; an indexer is named <c>this</c>. A name is
/// therefore not an identity — <see cref="DeclarationIndex.FindByName"/> returns every match, and
/// choosing among them is the caller's problem. Empty when the name could not be
/// recovered.
/// </param>
/// <param name="TriviaStartLine">
/// The first line of the declaration's leading trivia — doc comments, ordinary comments, and
/// attribute lists — or <see cref="SignatureStartLine"/> when there is none. This is the line a
/// caller slices from to include a member's documentation.
/// </param>
/// <param name="SignatureStartLine">The first line of the declaration itself.</param>
/// <param name="SignatureEndLine">
/// The last line of the signature: the line carrying the <c>{</c>, <c>=&gt;</c>, or <c>;</c> that
/// ends it. A signature may span lines, so this is not always <see cref="SignatureStartLine"/>.
/// </param>
/// <param name="BodyStartLine">
/// The line the body opens on — the <c>{</c>, or the <c>=&gt;</c> of an expression-bodied member —
/// or <c>-1</c> for a declaration with no body at all (abstract, interface, <c>extern</c>,
/// <c>partial</c> without implementation, field, enum member, positional record).
/// </param>
/// <param name="EndLine">The last line of the declaration, inclusive.</param>
/// <param name="Depth">
/// How many declarations enclose this one. Counted from <see cref="ParentIndex"/> rather than from
/// braces, so a file-scoped and a block namespace report the same nesting for the same code.
/// </param>
/// <param name="ParentIndex">
/// Index of the enclosing declaration within <see cref="DeclarationIndex.Declarations"/>, or
/// <c>-1</c> at file scope. Nested types carry the index of the type that encloses them, and a
/// file-scoped namespace encloses everything below it exactly as a block namespace would.
/// </param>
/// <param name="SpanKnown">
/// False when the lexer lost its place before or within this declaration — an unterminated
/// single-line literal, or a conditional directive whose braces may belong to a discarded branch.
/// The spans are then a guess and callers must treat the row as "do not know" rather than as fact.
/// That such a region reports unknown rather than a guess is gated by
/// <c>DeclarationIndexTests.ASpanTheScanCannotVouchFor_ReportsUnknown</c>.
/// <para>
/// A conditional group loses the place only for the rows <em>inside</em> it, provided every branch
/// returns to the brace depth the group opened at; the depth after such an <c>#endif</c> is the
/// same whichever branch the compiler keeps, so later rows are vouched for again. A group whose
/// branches do not each balance, or that reaches below its own opening depth, or that contains a
/// directive this scan could only skip because it believed itself inside a comment or literal,
/// loses the place for the rest of the file. Measured over dotnet/runtime's libraries, the
/// remaining loss is 1.46% of declarations, against 12.12% when any conditional poisoned the file
/// to its end. Those figures are an ungated point-in-time measurement, not a property: no test
/// re-measures them, and they will drift as that corpus moves.
/// </para>
/// <para>
/// The loss is conservative rather than wrong — a row the scan cannot vouch for reports unknown
/// instead of reporting one branch's answer as the declaration's. That holds by a discipline at
/// each site rather than by a central check: a site that fixes a row's span either consults the
/// depth flag or sets <c>SpanKnown</c> false outright, the unclosed-row sweep being the only one
/// of the latter kind. Being per-site, the discipline is only as good as its weakest site, and
/// naming one of them as "the only path that can report a wrong span" was wrong twice over:
/// <c>DeclarationIndexTests.AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd</c>
/// gates the initializer path only for a terminator the scan already knows is in a branch, and
/// review round 7 produced two builds that walk straight past it — one where the initializer sits
/// after the group, one where the other branch has already consumed the row it would extend. What
/// enforces the property across sites is not a citation but the differential:
/// <c>ConditionalRecoveryFuzzTests.NoVouchedRowMovesBetweenBuilds</c> compares every vouched row
/// against Roslyn under four symbol configurations and fails on any row whose lines move between
/// two builds that both compile, which is the property this paragraph asserts. It answers only
/// half the question, because it compares the builds against each other and never reads the
/// product's own numbers;
/// <c>ConditionalRecoveryFuzzTests.EveryVouchedRowMatchesRoslynInEveryBuildItExistsIn</c> answers
/// the other half by checking the product's numbers against each build.
/// Both are only as good as the generator's reach, which rounds 7, 8 and 9 each falsified, so read
/// that file's header before reading a clean run as proof. The per-site gates
/// remain as regression pins:
/// <c>AnInitializerReachingBackThroughAGroup_LosesTheDeclarationBeforeIt</c> and
/// <c>AnInitializerConsumedByAnotherBranch_LosesTheDeclarationBeforeIt</c> for the two round-7
/// shapes, <c>AnInitializerMaskedByAnotherBranchsInitializer_LosesTheDeclarationBeforeIt</c> and
/// <c>AnEnumInitializerReachingBackThroughAGroup_LosesTheMemberBeforeIt</c> for the round-8 pair,
/// <c>ATrailingSemicolonAfterAGroup_LosesTheTypeItCouldBelongTo</c> for the round-9 forward
/// direction,
/// <c>ABodilessRowWhoseTerminatorIsInABranch_IsNotVouchedFor</c> and
/// <c>ABodilessRowWhoseModifierIsInABranch_IsNotVouchedFor</c> for the bodiless emit path.
/// Recovery after a balanced group is
/// gated by <c>DeclarationIndexTests.ABalancedConditional_CostsOnlyTheRowsInsideIt</c> and, over
/// real conditional sources, by
/// <c>DeclarationIndexTests.InAConditionalFile_EveryDeclarationOutsideTheConditionals_IsStillVouchedFor</c>.
/// </para>
/// <para>
/// <see cref="Depth"/> and <see cref="ParentIndex"/> are <em>not</em> covered by
/// <c>SpanKnown</c> in general; it is a claim about this row's lines. Where the branches of a
/// group disagree about which declaration encloses the text after the <c>#endif</c>, the group is
/// refused outright rather than vouched for with one branch's nesting — that is what the
/// opening-depth floor is for — but no test asserts nesting correctness for a row the scan does
/// vouch for beyond the corpus differentials above.
/// </para>
/// </param>
public sealed record DeclarationSpan(
    DeclarationKind Kind,
    string Name,
    int TriviaStartLine,
    int SignatureStartLine,
    int SignatureEndLine,
    int BodyStartLine,
    int EndLine,
    int Depth,
    int ParentIndex,
    bool SpanKnown)
{
    /// <summary>
    /// The attribute lists applied to this declaration, one range per <c>[...]</c> list, in source
    /// order. Empty when none.
    /// <para>
    /// Member-applied only: an <c>[assembly:]</c> or <c>[module:]</c> list belongs to the
    /// compilation unit and is never reported here, on any declaration. Ranges, not parsed
    /// attributes — the names and arguments of an attribute are metadata facts, and this layer is
    /// a lexical scan that reports where the authored text sits so a caller can slice it.
    /// </para>
    /// <para>
    /// Every list lies within <c>[TriviaStartLine, SignatureStartLine)</c>, but the trivia region
    /// is not made of attribute lists alone: doc comments and ordinary comments share it, and a
    /// comment may sit between two lists. Gated by
    /// <c>DeclarationIndexTests.EveryAttributeListRoslynReports_IsReportedIdenticallyByTheIndex</c>,
    /// which compares against Roslyn's <c>AttributeLists</c> over the whole corpus.
    /// </para>
    /// </summary>
    public IReadOnlyList<LineRange> AttributeLists { get; init; } = [];

    /// <summary>True when this declaration can itself contain member declarations.</summary>
    public bool IsType => Kind is DeclarationKind.Class or DeclarationKind.Struct
        or DeclarationKind.Interface or DeclarationKind.Record or DeclarationKind.Enum;

    /// <summary>True when the declaration has a body a sequence point could land in.</summary>
    public bool HasBody => BodyStartLine >= 0;

    /// <summary>True when <paramref name="line"/> lies within the declaration, trivia excluded.</summary>
    public bool Contains(int line) => line >= SignatureStartLine && line <= EndLine;

    /// <summary>True when <paramref name="line"/> lies within the declaration's body.</summary>
    public bool BodyContains(int line) => HasBody && line >= BodyStartLine && line <= EndLine;
}

/// <summary>
/// The declarations of one C# source file, recovered in a single forward pass over
/// <see cref="BodySlicer"/>'s token stream.
/// <para>
/// This exists because locating a member's authored text is two questions, and only one of them
/// has an exact answer. A portable PDB says exactly which lines a member's <i>body</i> occupies,
/// by row correspondence rather than by search. It says nothing about where the member's
/// <i>declaration</i> starts, because the signature, its attributes, and its doc comments all sit
/// above the first sequence point. Answering the second question by scanning backward from the
/// body re-derives file structure per request, within a fixed window, from text alone.
/// </para>
/// <para>
/// Computing the shape once inverts that. A member's declaration start is then a column of a row
/// rather than something a scan goes hunting for, which is what makes leading trivia reachable at
/// all, and the window disappears because nothing is scanning.
/// </para>
/// <para>
/// <b>This is not a parser.</b> It has no syntax tree, resolves no types, and applies no semantic
/// model; it is a lexical scan plus declaration recognition, and it is heuristic at the margins by
/// construction. The value over the per-request scan it replaces is not that the heuristics are
/// gone but that they are computed once, in one place, into an artifact whose whole-file
/// correctness can be asserted.
/// </para>
/// <para>
/// Where the lexer loses its place — an unterminated literal, or a conditional directive whose
/// braces may belong to a discarded branch — affected rows carry
/// <see cref="DeclarationSpan.SpanKnown"/> false rather than a plausible wrong span; that is the
/// property <c>ConditionalRecoveryFuzzTests.NoVouchedRowMovesBetweenBuilds</c> gates, and it is a
/// claim about vouched rows only, never about how many rows are vouched. A conditional
/// directive costs the rows inside its own branches, and costs the rest of the file only when the
/// group's branches do not agree on the structure after its <c>#endif</c>; see that member's
/// remarks and
/// <see href="https://github.com/richlander/dotnet-inspect/issues/3668">#3668</see>.
/// </para>
/// <para>
/// Whole-file correctness is gated by
/// <c>DeclarationIndexTests.EveryDeclarationRoslynReports_IsReportedIdenticallyByTheIndex</c>,
/// which compares kind, name, first line, and last line against Roslyn over the real source of
/// every PDB-bearing assembly beside the test binary; leading trivia is gated separately by
/// <c>ADeclarationsTriviaStart_MatchesRoslynsLeadingTrivia</c>, and the nesting the containment
/// lookup depends on by <c>RowsNestWithinTheirParentAndNeverOverlapASibling</c>. Roslyn is a
/// test-only oracle; this library stays Roslyn-free.
/// </para>
/// </summary>
public sealed class DeclarationIndex
{
    private DeclarationIndex(ImmutableArray<DeclarationSpan> declarations) => Declarations = declarations;

    /// <summary>
    /// Every declaration in the file, in source order of the point each was recognized. A
    /// declaration always precedes the declarations it encloses.
    /// </summary>
    public ImmutableArray<DeclarationSpan> Declarations { get; }

    /// <summary>Builds the index for <paramref name="sourceText"/>.</summary>
    public static DeclarationIndex Build(string sourceText) =>
        Build(sourceText.Split('\n'));

    /// <summary>Builds the index for a file already split into lines.</summary>
    public static DeclarationIndex Build(IReadOnlyList<string> lines) =>
        new(DeclarationIndexBuilder.Build(lines));

    /// <summary>
    /// The innermost declaration whose <i>body</i> contains <paramref name="line"/>, which is how
    /// a PDB sequence-point line selects the member it belongs to.
    /// <para>
    /// Innermost is by <see cref="DeclarationSpan.Depth"/>, so a local function or lambda does not
    /// hide the member that encloses it: only declarations are indexed, and a lambda is not one.
    /// A row whose span is not known is never returned, because a guessed span that happens to
    /// contain the line is indistinguishable from a real match.
    /// </para>
    /// </summary>
    public DeclarationSpan? FindByBodyLine(int line)
    {
        DeclarationSpan? best = null;
        foreach (var d in Declarations)
        {
            if (!d.SpanKnown || !d.BodyContains(line))
                continue;
            if (best is null || d.Depth > best.Depth)
                best = d;
        }
        return best;
    }

    /// <summary>
    /// Every declaration of kind <paramref name="kind"/> named <paramref name="name"/>. This is the
    /// entry point for locating a member that has no body, and therefore no sequence point to
    /// anchor on: an abstract or interface member, an <c>extern</c> member, a field, or an enum
    /// member. Callers must discriminate among the results themselves; name alone does not
    /// identify an overload.
    /// </summary>
    public ImmutableArray<DeclarationSpan> FindByName(DeclarationKind kind, string name) =>
        [.. Declarations.Where(d => d.Kind == kind && d.Name == name)];

    /// <summary>The declaration enclosing <paramref name="span"/>, or null at file scope.</summary>
    public DeclarationSpan? ParentOf(DeclarationSpan span) =>
        span.ParentIndex >= 0 ? Declarations[span.ParentIndex] : null;
}
