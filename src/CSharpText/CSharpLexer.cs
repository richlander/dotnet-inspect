namespace CSharpText;

/// <summary>
/// Tokenizes C# text and carries conservative lexical structure across lines.
/// </summary>
internal static class CSharpLexer
{
    /// <summary>
    /// The lexical state a C# scan carries from one line to the next.
    /// <para>
    /// C# literals nest: an interpolation hole holds ordinary C#, which may open a further
    /// literal, whose holes may open more. A scanner that recognizes literal forms one at a
    /// time cannot express that, and each form it gets wrong reports the wrong brace depth —
    /// which is the one thing the callers read. A stack of frames states the nesting directly,
    /// so a hole's braces are counted against the hole that owns them and never against the
    /// enclosing block.
    /// </para>
    /// </summary>
    internal sealed class LexState
    {
        private readonly List<Frame> frames = [];

        /// <summary>An unterminated block comment continues onto the next line.</summary>
        public bool InBlockComment;

        /// <summary>
        /// Open square brackets carried across lines. An attribute list, like any bracketed
        /// construct, may span lines; a line inside one is not a declaration, and reading it as
        /// one truncated an accessor at a "set" that was really an attribute name (adversarial
        /// review, GPT).
        /// </summary>
        public int BracketDepth;

        /// <summary>
        /// Set when a brace could not be placed — an unterminated single-line literal, a
        /// delimiter run this scanner will not guess at, or any conditional directive. The depth
        /// count is unusable from that point on, and callers treat it as "do not know" rather
        /// than as a depth.
        /// <para>
        /// This is the blunt, sticky answer, and it is what the line-oriented recovery helpers on
        /// this type read. <see cref="StructuralDepthKnown"/> is the sharper one: a conditional
        /// group whose branches each balance leaves the depth after its <c>#endif</c> knowable,
        /// which this flag cannot express because it never clears.
        /// </para>
        /// </summary>
        public bool Untracked;

        /// <summary>
        /// Conditional groups currently open, innermost last. Each records the brace depth at its
        /// <c>#if</c> and whether any branch has failed to return to it.
        /// </summary>
        private readonly List<Conditional> conditionals = [];

        /// <summary>
        /// Set when a conditional group's branches did not balance. Never cleared, which is what
        /// makes the loss survive a later balanced group -- closing one restores the depth for its
        /// own frame and must not clear a loss recorded before it opened.
        /// <para>
        /// Two separate properties, each with its own gate, because conflating them produced three
        /// successive false citations here:
        /// </para>
        /// <list type="bullet">
        /// <item>the ASSIGNMENT in <see cref="CloseConditional"/> is gated by
        /// <c>DeclarationIndexTests.AnUnbalancedConditional_StillLosesEveryLaterRow</c> -- making
        /// it a no-op fails that test, and four others;</item>
        /// <item>the STICKINESS is gated by
        /// <c>DeclarationIndexTests.ADirectiveHiddenInsideACommentWithinAGroup_LosesTheDepth</c>
        /// and <c>...ADirectiveHiddenInsideALiteralWithinAGroup_LosesTheDepth</c>: each sets this
        /// field inside a group that then closes balanced, so clearing it on a balanced close
        /// fails both, and no other test does.</item>
        /// </list>
        /// <para>
        /// Round 3 corrected a citation that named the unbalanced test for the stickiness, where
        /// it gates nothing, by asserting it gated neither property -- which round 5 falsified by
        /// mutating the assignment (adversarial review rounds 3 and 5).
        /// </para>
        /// </summary>
        private bool conditionalDepthLost;

        /// <summary>
        /// Set when the depth was lost for a reason that has nothing to do with conditionals.
        /// Tracked apart from <see cref="Untracked"/> so that a conditional directive does not
        /// permanently mask the difference between the two causes.
        /// </summary>
        private bool literalDepthLost;

        /// <summary>
        /// <para>
        /// Whether the brace depth is a fact the scan can vouch for, and so whether it describes
        /// the compiled program. False inside a conditional group, because the branch being
        /// scanned may be the one the compiler discards. False after a group whose branches did
        /// not each return to the depth they started at, because then the depth after the
        /// <c>#endif</c> depends on which branch compiles. True again after a group that did
        /// balance: every branch leaves the same depth behind, so it no longer matters which one
        /// the compiler keeps.
        /// </para>
        /// <para>
        /// Withholding the inside of a balanced group is deliberate and is not merely caution
        /// about liveness. A declaration written wholly inside one branch does have a correct
        /// span, but one that <em>crosses</em> a directive does not: an <c>int P { get; }</c>
        /// whose initializer is <c>= 1;</c> in one branch and <c>= 2;</c> in another occupies a
        /// different, and in the second case non-contiguous, set of lines per build, which no
        /// single line range can express. At token granularity the two shapes are
        /// indistinguishable, so the conservative answer covers both. Gated by
        /// <c>AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd</c> for the crossing
        /// shape and <c>ABalancedConditional_CostsOnlyTheRowsInsideIt</c> for the contained one.
        /// </para>
        /// </summary>
        public bool StructuralDepthKnown =>
            !literalDepthLost && !conditionalDepthLost && conditionals.Count == 0;

        /// <summary>
        /// Reports a brace that closed, so that a conditional group can notice a branch reaching
        /// below the depth it opened at. Such a branch is closing a scope that was opened outside
        /// the group, which means the group's branches disagree about which declaration encloses
        /// the text after the <c>#endif</c> even when they agree about the depth -- and depth is
        /// all the balance rule measures. Gated by
        /// <c>DeclarationIndexTests.ABranchThatClosesAScopeItsGroupDidNotOpen_LosesTheDepth</c>.
        /// </summary>
        public void NoteDepth(int depth)
        {
            if (conditionals.Count > 0 && depth < conditionals[^1].BaseDepth)
                conditionals[^1].Unbalanced = true;
        }

        /// <summary>
        /// Records that the depth was lost for a non-conditional reason. <c>Untracked</c> is read
        /// only from <see cref="ExtractMethodBody"/> and the helpers it calls -- the token-emitting
        /// path the declaration index consumes never reads it -- and is gated by
        /// <c>ExtractMethodBodyTests.AConstructorRecoveredPastAnUnterminatedLiteral_StillCapturesItsText</c>;
        /// <c>literalDepthLost</c> is what the index reads.
        /// </summary>
        public void LoseDepth()
        {
            literalDepthLost = true;
            Untracked = true;
        }

        /// <summary>Whether a conditional group is currently open.</summary>
        public bool InConditional => conditionals.Count > 0;

        /// <summary>
        /// Gives up on the depth for a conditional reason that no <c>#endif</c> can repair, which
        /// is sticky exactly as an unbalanced group is because it sets the same field. Gated by
        /// <c>DeclarationIndexTests.ADirectiveHiddenInsideACommentWithinAGroup_LosesTheDepth</c>
        /// and <c>...ADirectiveHiddenInsideALiteralWithinAGroup_LosesTheDepth</c>, one per arm of
        /// its only caller's condition.
        /// </summary>
        public void LoseConditionalDepth()
        {
            conditionalDepthLost = true;
            Untracked = true;
        }

        /// <summary>
        /// Which run of source between conditional directives the scanner is in. Incremented at
        /// every directive that starts or ends a branch, so two tokens sharing a value were
        /// certainly written in the same compiled branch. See <see cref="ScanToken.Section"/> for
        /// why the converse does not hold and why that asymmetry is the useful one.
        /// </summary>
        public int Section { get; private set; }

        /// <summary>Opens a conditional group at brace depth <paramref name="depth"/>.</summary>
        public void OpenConditional(int depth)
        {
            Section++;
            conditionals.Add(new Conditional(depth));
            Untracked = true;
        }

        /// <summary>
        /// Ends the current branch at <c>#elif</c> or <c>#else</c> and returns the depth the next
        /// branch starts from. A branch that did not return to the group's opening depth makes the
        /// group unbalanced; the depth is reset either way, so one branch's braces are never
        /// counted against the next. That reset is <em>unverified and ungated</em>: it is an
        /// equivalent mutation, because any branch whose depth deviates raises the unbalanced flag
        /// in the same breath and the group is condemned either way. It is kept so that each
        /// branch's check means what it says -- a branch measured against a previous branch's
        /// leftovers is not a per-branch check -- not because an answer depends on it. The
        /// unbalanced flag itself is gated by
        /// <c>DeclarationIndexTests.ABranchThatDoesNotReturnToTheOpeningDepth_UnbalancesTheGroup</c>.
        /// </summary>
        public int NextBranch(int depth)
        {
            Section++;
            Untracked = true;

            if (conditionals.Count == 0)
            {
                // An #elif or #else with no open group is malformed source. Refuse to guess.
                conditionalDepthLost = true;
                return depth;
            }

            var group = conditionals[^1];

            // The flag is what decides. It is raised here rather than at the #endif because the
            // reset below erases the evidence: by the time the group closes, a branch that left a
            // brace open is indistinguishable from one that did not.
            if (depth != group.BaseDepth)
                group.Unbalanced = true;

            // The reset itself is unobservable, since the flag has already condemned the group in
            // every case that would reach it -- verified by mutation, and recorded as equivalent
            // rather than gated. It is kept so that each branch's check means what it says: a
            // later branch measured against an earlier branch's leftovers is not a per-branch
            // check at all.
            return group.BaseDepth;
        }

        /// <summary>
        /// Closes the current conditional group and returns the depth that follows it. Balance is
        /// judged over the last branch as well, and an unbalanced inner group is propagated
        /// outward: an enclosing group cannot be balanced if something inside it was not.
        /// </summary>
        public int CloseConditional(int depth)
        {
            // Unlike the increments at #if and #elif/#else, this one is UNVERIFIED AND UNGATED, and
            // recorded as conservative rather than load-bearing: it is an equivalent mutation for
            // safety and a small over-refusal for recall. Merging "inside the group's last branch"
            // with "after the group" can only make two tokens compare equal, and a header inside a
            // branch is at an unknown depth, where knownness is already intersected away and cannot
            // be restored. So no answer becomes wrong without it. It is kept because a section is
            // meant to name one run of source between directives, and a consumer reasoning about
            // nesting should not have to know that the last branch and the text after it were
            // silently merged.
            Section++;
            Untracked = true;

            if (conditionals.Count == 0)
            {
                conditionalDepthLost = true;
                return depth;
            }

            var group = conditionals[^1];
            conditionals.RemoveAt(conditionals.Count - 1);

            // An enclosing group cannot balance if something inside it did not, so an inner
            // failure propagates outward rather than being forgiven by the outer #endif. Gated by
            // AnUnbalancedInnerConditional_PoisonsTheGroupAroundIt.
            if (group.Unbalanced || depth != group.BaseDepth)
            {
                if (conditionals.Count > 0)
                    conditionals[^1].Unbalanced = true;
                else
                    conditionalDepthLost = true;
            }

            return group.BaseDepth;
        }

        private sealed class Conditional(int baseDepth)
        {
            public readonly int BaseDepth = baseDepth;
            public bool Unbalanced;

            public Conditional Copy() => new(BaseDepth) { Unbalanced = Unbalanced };
        }

        public bool InLiteral => frames.Count > 0;

        public Frame Top => frames[^1];

        public void Push(Frame frame) => frames.Add(frame);

        public void Pop() => frames.RemoveAt(frames.Count - 1);

        public void Replace(Frame frame) => frames[^1] = frame;

        /// <summary>
        /// A copy that can be advanced without disturbing the scan in progress. Used to ask
        /// where a line's code resumes without consuming the line.
        /// </summary>
        public LexState Clone()
        {
            var copy = new LexState
            {
                InBlockComment = InBlockComment,
                Untracked = Untracked,
                BracketDepth = BracketDepth,
                conditionalDepthLost = conditionalDepthLost,
                literalDepthLost = literalDepthLost,
            };
            copy.frames.AddRange(frames);
            // Ungated, and unverified as a property of any caller: neither of the two sites that
            // clone a state consults structural knownness or emits tokens -- both are backward-scan
            // probes reading Untracked, InLiteral, InBlockComment and BracketDepth -- so dropping
            // this line changes no observable answer today. It is here because a clone that
            // reported knownness the original does not have would be wrong the moment a probe did
            // look, and a state that lies is a worse default than a line with no test.
            copy.conditionals.AddRange(conditionals.Select(c => c.Copy()));
            return copy;
        }

        /// <summary>
        /// True when the state cannot survive a line break: an ordinary or single-quoted
        /// interpolated literal must close on the line that opens it.
        /// </summary>
        public bool HasLineBoundLiteral
        {
            get
            {
                foreach (var frame in frames)
                {
                    // Since C# 11 a hole may span lines even in a single-quoted literal; only
                    // the literal's own text is bound to one line (adversarial review, Gemini).
                    if (!frame.Verbatim && !frame.Raw && !frame.InHole)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// One string literal. <see cref="InHole"/> separates the literal's own text from the
        /// ordinary C# inside an interpolation hole, which is scanned as code.
        /// </summary>
        public struct Frame
        {
            /// <summary>Quotes that close the literal: three or more for a raw form, else one.</summary>
            public int QuoteRun;

            /// <summary>Braces that open a hole, and the "$" count that set them. Zero when not interpolated.</summary>
            public int DollarRun;

            /// <summary>"" escapes a quote and the literal may span lines.</summary>
            public bool Verbatim;

            /// <summary>A raw form: no backslash escapes, and it may span lines.</summary>
            public bool Raw;

            /// <summary>Scanning the ordinary C# of an interpolation hole rather than literal text.</summary>
            public bool InHole;

            /// <summary>Braces open inside the hole, counted from the hole's own opener.</summary>
            public int HoleDepth;
        }
    }

    /// <summary>
    /// Length of the run of <paramref name="c"/> starting at <paramref name="start"/>.
    /// </summary>
    /// <summary>
    /// Reports whether <paramref name="line"/> is a preprocessor directive, and whether it is one
    /// of the conditional-compilation directives whose branches the compiler may discard.
    /// </summary>
    /// <summary>
    /// Which conditional directive a line spells, if any. <see cref="None"/> covers both a
    /// non-conditional directive and a line that is not a directive at all; callers distinguish
    /// those by <c>IsDirective</c>'s return value.
    /// </summary>
    private enum Conditional { None, If, NextBranch, EndIf }

    /// <summary>Whether <paramref name="c"/> can continue a C# identifier.</summary>
    /// <summary>
    /// Whether <paramref name="c"/> continues a C# identifier. Letters, digits and underscore are
    /// the obvious part; the Unicode categories are the rest of what the language allows, and
    /// omitting them read "#endif\u0301" (a combining mark) as the #endif directive when Roslyn
    /// reports CS1024 and CS1027 and leaves the group open (adversarial review round 5,
    /// GPT-5.6 Sol).
    /// </summary>
    private static bool IsIdentifierPart(char c) =>
        char.IsLetterOrDigit(c)
        || c == '_'
        || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark
            or System.Globalization.UnicodeCategory.ConnectorPunctuation
            or System.Globalization.UnicodeCategory.Format;

    private static bool IsDirective(string line, out Conditional conditional)
    {
        conditional = Conditional.None;

        var trimmed = line.AsSpan().TrimStart();

        // A UTF-8 byte order mark is not whitespace, so TrimStart leaves it in front of the "#"
        // and the directive reads as code. Roslyn strips the preamble before parsing, so a file
        // whose first line is "\uFEFF#if X" opens a conditional group there; scanning it as code
        // vouched for the wrong branch's declaration (adversarial review round 5, GPT-5.6 Sol).
        if (!trimmed.IsEmpty && trimmed[0] == '\uFEFF')
            trimmed = trimmed[1..].TrimStart();

        if (trimmed.IsEmpty || trimmed[0] != '#')
            return false;

        var name = trimmed[1..].TrimStart();

        // "elif" and "else" are one case: each ends the branch above it and starts another, and
        // the group's balance is judged the same way at both.
        //
        // The character after the name must not continue an identifier: "#endif_foo" spells the
        // single identifier "endif_foo", which is not the #endif directive at all, and reading it
        // as one closes a group the compiler leaves open. Underscore is the gap that
        // char.IsLetterOrDigit misses. Anything else that follows -- "#endif-" (CS1025), or a
        // comment as in "#endif//note" -- Roslyn still recognizes as the directive, so accepting
        // those matches it (adversarial review round 3, Gemini 3.1 Pro).
        foreach (var (candidate, kind) in (ReadOnlySpan<(string, Conditional)>)
                 [("if", Conditional.If), ("elif", Conditional.NextBranch),
                  ("else", Conditional.NextBranch), ("endif", Conditional.EndIf)])
        {
            if (name.StartsWith(candidate, StringComparison.Ordinal) &&
                (name.Length == candidate.Length || !IsIdentifierPart(name[candidate.Length])))
            {
                conditional = kind;
                break;
            }
        }

        return true;
    }

    private static int RunLength(string line, int start, char c)
    {
        int i = start;
        while (i < line.Length && line[i] == c)
            i++;
        return i - start;
    }

    /// <summary>
    /// Scans one line of C# text, carrying <paramref name="state"/> across lines, and returns
    /// the last significant character on it — the last non-whitespace character that is not
    /// inside a comment. Braces adjust <paramref name="depth"/> only where they are structural:
    /// in code that is not inside any literal. Braces inside an interpolation hole belong to
    /// that hole, and braces in literal text or a comment are content.
    /// </summary>
    internal static char ScanLine(string line, LexState state, ref int depth)
    {
        Scan(line, state, ref depth, start: 0, untilLiteralCloses: false, out char significant);
        return significant;
    }

    /// <summary>
    /// Scans <paramref name="lines"/> as one continuous stretch of C# and returns every token on
    /// them, in order.
    /// <para>
    /// The scan is the same one the slicer's predicates run on; this entry point differs only in
    /// keeping what that scan works out instead of discarding it at each line break. It exists so
    /// the token stream can be pinned and checked directly, ahead of the predicates moving onto
    /// it.
    /// </para>
    /// </summary>
    internal static List<ScanToken> ScanTokens(IReadOnlyList<string> lines)
    {
        var state = new LexState();
        var tokens = new List<ScanToken>();
        int depth = 0;

        for (int i = 0; i < lines.Count; i++)
            Scan(lines[i], state, ref depth, start: 0, untilLiteralCloses: false, out _, tokens: tokens, lineIndex: i);

        return tokens;
    }

    /// <summary>
    /// Scans <paramref name="line"/> from <paramref name="start"/>, returning the index it
    /// stopped at. With <paramref name="untilLiteralCloses"/> the scan stops as soon as the
    /// literal it opened is closed, which is how a caller consumes a single literal.
    /// </summary>
    internal static int Scan(
        string line,
        LexState state,
        ref int depth,
        int start,
        bool untilLiteralCloses,
        out char significant,
        bool untilCodeResumes = false,
        bool untilBracketsClose = false,
        List<ScanToken>? tokens = null,
        int lineIndex = 0)
    {
        bool knownOnEntry = state.StructuralDepthKnown;
        int mark = tokens?.Count ?? 0;

        int stopped = ScanCore(
            line, state, ref depth, start, untilLiteralCloses, out significant,
            untilCodeResumes, untilBracketsClose, tokens, lineIndex);

        // Losing the place is discovered at the end of a line, after tokens on it were emitted.
        // Those tokens recorded a depth that has since become meaningless, so correct them rather
        // than leave a stale "known" that reads exactly like a real depth.
        if (tokens is not null && knownOnEntry && !state.StructuralDepthKnown)
        {
            for (int t = mark; t < tokens.Count; t++)
                tokens[t] = tokens[t] with { DepthKnown = false };
        }

        return stopped;
    }

    private static int ScanCore(
        string line,
        LexState state,
        ref int depth,
        int start,
        bool untilLiteralCloses,
        out char significant,
        bool untilCodeResumes,
        bool untilBracketsClose,
        List<ScanToken>? tokens,
        int lineIndex)
    {
        significant = '\0';
        int i = start;
        bool opened = false;
        bool bracketOpened = false;

        void Emit(int atDepth, ScanTokenKind kind, int column, int length) =>
            EmitAt(atDepth, state.BracketDepth, kind, column, length);

        void EmitAt(int atDepth, int atBracketDepth, ScanTokenKind kind, int column, int length)
        {
            if (tokens is null || length <= 0)
                return;

            // Literal text arrives in fragments — an escape, a quote run, a stretch of plain
            // characters — because the scan decides what each one means separately. They are one
            // token to a caller, so adjacent fragments coalesce instead of surfacing that.
            // Adjacency is same line and touching columns: a literal that spans lines emits one
            // token per line, so an empty line inside a verbatim literal must not fuse the
            // fragments on either side of it.
            if (kind == ScanTokenKind.StringLiteral && tokens.Count > 0)
            {
                var previous = tokens[^1];

                if (previous.Kind == ScanTokenKind.StringLiteral &&
                    previous.Line == lineIndex &&
                    previous.End == column)
                {
                    tokens[^1] = previous with { Length = previous.Length + length };
                    return;
                }
            }

            tokens.Add(new ScanToken(kind, lineIndex, column, length, atDepth, atBracketDepth, state.StructuralDepthKnown, state.Section));
        }

        // Advances past the rest of an open block comment, clearing the carried flag if the
        // comment ends on this line. Shared by the branch that opens one and the branch that
        // resumes one carried in from an earlier line.
        int ConsumeBlockComment(int from)
        {
            int j = from;

            while (j < line.Length)
            {
                if (line[j] == '*' && j + 1 < line.Length && line[j + 1] == '/')
                {
                    state.InBlockComment = false;
                    return j + 2;
                }

                j++;
            }

            return j;
        }

        // Preprocessor-disabled text is not lexed as code: inside a branch the compiler drops,
        // "/*" opens no comment and a quote opens no string, but conditional directives are still
        // recognized and still nest. So a conditional directive sitting in what this scan believes
        // is a comment or a literal is genuinely ambiguous -- if the surrounding text is disabled
        // it is a directive, and skipping it makes a later #endif close the wrong group and restore
        // knownness early, which is the one failure the index may not have. Refuse instead.
        //
        // Only conditional ones. A skipped section is the one place the compiler does not process
        // #pragma, #region, #nullable or #line at all, so such a line inside a literal is text
        // whichever way the branch falls: it cannot open, close or renumber a group. Refusing it
        // would poison a file for a directive that changes nothing (adversarial review round 2,
        // Gemini 3.1 Pro).
        //
        // Only while a group is open: outside one the text cannot be disabled, so "#if" inside a
        // comment is unambiguously prose, and this repository's own sources write it that way.
        if ((state.InBlockComment || state.InLiteral)
            && state.InConditional
            && IsDirective(line, out Conditional hidden)
            && hidden != Conditional.None)
        {
            state.LoseConditionalDepth();
        }

        if (!state.InBlockComment && !state.InLiteral && IsDirective(line, out Conditional conditional))
        {
            // A preprocessor directive is not code, so nothing on the line is scanned. A
            // conditional directive additionally means the braces around it may belong to a
            // branch the compiler discards. That is only true *inside* the group: a group whose
            // branches each return to the depth they started at leaves the same depth behind
            // whichever branch the compiler keeps, so the depth after its #endif is knowable.
            // The unbalanced flag raised at a branch boundary is what decides; the depth reset
            // that accompanies it is unobservable, and is kept for the invariant rather than for
            // the answer. See NextBranch.
            switch (conditional)
            {
                case Conditional.If: state.OpenConditional(depth); break;
                case Conditional.NextBranch: depth = state.NextBranch(depth); break;
                case Conditional.EndIf: depth = state.CloseConditional(depth); break;
            }

            Emit(depth, ScanTokenKind.Directive, i, line.Length - i);
            return line.Length;
        }

        while (i < line.Length)
        {
            if (untilLiteralCloses && opened && !state.InLiteral)
                return i;

            if (untilCodeResumes && !state.InBlockComment && !state.InLiteral && state.BracketDepth == 0)
                return i;

            if (untilBracketsClose && bracketOpened && state.BracketDepth == 0)
                return i;

            char c = line[i];

            if (state.InBlockComment)
            {
                int commentStart = i;
                i = ConsumeBlockComment(i);
                Emit(depth, ScanTokenKind.Comment, commentStart, i - commentStart);
                continue;
            }

            // Literal text. Only this literal's closing delimiter and its hole openers matter;
            // everything else, braces included, is content.
            if (state.InLiteral && !state.Top.InHole)
            {
                var frame = state.Top;

                if (c == '{' || c == '}')
                {
                    int run = RunLength(line, i, c);

                    if (frame.DollarRun == 0 || c == '}')
                    {
                        // Not interpolated, or a closing run in literal text: content either
                        // way. A hole is closed from inside the hole, not from here.
                        Emit(depth, ScanTokenKind.StringLiteral, i, run);
                        i += run;
                        continue;
                    }

                    if (run < frame.DollarRun)
                    {
                        // Too short to delimit a hole.
                        Emit(depth, ScanTokenKind.StringLiteral, i, run);
                        i += run;
                        continue;
                    }

                    if (!frame.Raw)
                    {
                        // One "$": braces pair off as escapes, and an odd one out opens a hole.
                        if (run % 2 == 0)
                        {
                            Emit(depth, ScanTokenKind.StringLiteral, i, run);
                            i += run;
                            continue;
                        }
                    }

                    // The braces that open the hole are the last DollarRun of the run; any
                    // ahead of them are literal text.
                    frame.InHole = true;
                    frame.HoleDepth = 1;
                    state.Replace(frame);
                    Emit(depth, ScanTokenKind.StringLiteral, i, run);
                    i += run;
                    continue;
                }

                if (c == '\\' && !frame.Verbatim && !frame.Raw)
                {
                    Emit(depth, ScanTokenKind.StringLiteral, i, Math.Min(2, line.Length - i));
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    int run = RunLength(line, i, '"');

                    if (frame.Verbatim)
                    {
                        // "" is an escaped quote; a lone quote closes.
                        if (run >= 2)
                        {
                            Emit(depth, ScanTokenKind.StringLiteral, i, 2);
                            i += 2;
                            continue;
                        }

                        state.Pop();
                        significant = '"';
                        Emit(depth, ScanTokenKind.StringLiteral, i, 1);
                        i += 1;
                        continue;
                    }

                    if (run >= frame.QuoteRun)
                    {
                        state.Pop();
                        significant = '"';
                        Emit(depth, ScanTokenKind.StringLiteral, i, run);
                        i += run;
                        continue;
                    }

                    // A shorter run inside a raw literal is content.
                    Emit(depth, ScanTokenKind.StringLiteral, i, run);
                    i += run;
                    continue;
                }

                // Plain content. Consuming the whole run rather than one character at a time is
                // the same scan — none of these characters carry meaning here — and it keeps the
                // emitted token from being assembled a character at a time.
                int contentStart = i;
                i++;

                while (i < line.Length &&
                       line[i] is not ('{' or '}' or '"') &&
                       !(line[i] == '\\' && !frame.Verbatim && !frame.Raw))
                {
                    i++;
                }

                Emit(depth, ScanTokenKind.StringLiteral, contentStart, i - contentStart);
                continue;
            }

            // Ordinary C#: either top-level code or the inside of an interpolation hole.
            bool inHole = state.InLiteral;

            if (c == '/' && i + 1 < line.Length)
            {
                if (line[i + 1] == '/')
                {
                    // A line comment runs to the end of the line. Inside a hole that means the
                    // literal cannot close here, which only a multi-line form survives.
                    Emit(depth, ScanTokenKind.Comment, i, line.Length - i);
                    break;
                }

                if (line[i + 1] == '*')
                {
                    state.InBlockComment = true;
                    int openerStart = i;
                    i = ConsumeBlockComment(i + 2);
                    Emit(depth, ScanTokenKind.Comment, openerStart, i - openerStart);
                    continue;
                }
            }

            if (c == '\'')
            {
                int literalStart = i;
                i++;
                while (i < line.Length && line[i] != '\'')
                    i += line[i] == '\\' ? 2 : 1;
                i++;
                significant = '\'';
                Emit(depth, ScanTokenKind.CharLiteral, literalStart, Math.Min(i, line.Length) - literalStart);
                continue;
            }

            if (c == '$' || c == '@' || c == '"')
            {
                int open = i;
                int dollars = 0;
                bool verbatim = false;

                while (open < line.Length && (line[open] == '$' || line[open] == '@'))
                {
                    if (line[open] == '$')
                        dollars += RunLength(line, open, '$');
                    else
                        verbatim = true;

                    open += line[open] == '$' ? RunLength(line, open, '$') : 1;
                }

                if (open >= line.Length || line[open] != '"')
                {
                    // "$" or "@" not opening a literal: an identifier like "@class", or an
                    // interpolation-free use. Consume what was examined and carry on.
                    int examined = i;
                    i = open > i ? open : i + 1;
                    if (!char.IsWhiteSpace(c))
                        significant = c;
                    Emit(depth, ScanTokenKind.Punctuator, examined, i - examined);
                    continue;
                }

                int quotes = RunLength(line, open, '"');

                // Only a non-verbatim literal can be raw. After `@`, a run of three quotes is
                // an opener and one escaped quote, not a raw delimiter.
                bool raw = quotes >= 3 && !verbatim;

                if (!raw && quotes == 2)
                {
                    // The empty literal.
                    Emit(depth, ScanTokenKind.StringLiteral, i, open + 2 - i);
                    i = open + 2;
                    significant = '"';
                    if (untilLiteralCloses)
                        return i;
                    continue;
                }

                state.Push(new LexState.Frame
                {
                    QuoteRun = raw ? quotes : 1,
                    DollarRun = dollars,
                    Verbatim = verbatim,
                    Raw = raw,
                });

                opened = true;
                significant = '"';
                int openerAt = i;
                i = open + (raw ? quotes : 1);
                Emit(depth, ScanTokenKind.StringLiteral, openerAt, i - openerAt);
                continue;
            }

            // An identifier, keyword, or numeric literal. Taking the whole run at once is the
            // same scan the character-at-a-time loop performed — none of these characters is a
            // delimiter — and it is what lets a caller ask for a word rather than rebuild one.
            if (c == '_' || char.IsLetterOrDigit(c))
            {
                int wordStart = i;

                while (i < line.Length && (line[i] == '_' || char.IsLetterOrDigit(line[i])))
                    i++;

                significant = line[i - 1];
                Emit(depth, ScanTokenKind.Word, wordStart, i - wordStart);
                continue;
            }

            int depthBefore = depth;
            int bracketBefore = state.BracketDepth;

            if (c == '{')
            {
                if (inHole)
                {
                    var frame = state.Top;
                    frame.HoleDepth++;
                    state.Replace(frame);
                }
                else
                {
                    depth++;
                }
            }
            else if (c == '[')
            {
                if (!inHole)
                {
                    state.BracketDepth++;
                    bracketOpened = true;
                }
            }
            else if (c == ']')
            {
                if (!inHole && state.BracketDepth > 0)
                    state.BracketDepth--;
            }
            else if (c == '}')
            {
                if (inHole)
                {
                    var frame = state.Top;
                    int run = frame.DollarRun > 1 ? RunLength(line, i, '}') : 1;

                    if (frame.HoleDepth == 1)
                    {
                        // The hole closes and the literal's own text resumes.
                        frame.InHole = false;
                        frame.HoleDepth = 0;
                        state.Replace(frame);
                        int closerAt = i;

                        // Min because a longer run closes the hole with DollarRun braces and
                        // leaves the rest as literal text. This cannot be gated through the token
                        // stream: the leftover braces are re-consumed by the literal branch on the
                        // next step and coalesce back into this token, so consuming one brace here
                        // produces a byte-identical stream. Verified inert over every string up to
                        // length 7 on the `$ " { } \ a` alphabet (335,922 inputs, 0 divergences).
                        // It is kept because it is what the language says, and a consumer that
                        // wants un-coalesced fragments would be able to tell.
                        i += frame.DollarRun > 1 ? Math.Min(run, frame.DollarRun) : 1;
                        EmitAt(depthBefore, bracketBefore, ScanTokenKind.StringLiteral, closerAt, i - closerAt);
                        continue;
                    }

                    frame.HoleDepth--;
                    state.Replace(frame);
                }
                else
                {
                    depth--;
                    state.NoteDepth(depth);
                }
            }

            if (!char.IsWhiteSpace(c))
            {
                significant = c;
                EmitAt(depthBefore, bracketBefore, ScanTokenKind.Punctuator, i, 1);
            }

            i++;
        }

        // A literal that must close on its own line did not, so the scan lost its place.
        if (state.HasLineBoundLiteral)
            state.LoseDepth();

        return i;
    }

}
