using System.Collections.Immutable;

namespace CSharpText;

/// <summary>
/// Recovers <see cref="DeclarationSpan"/>s from <see cref="CSharpLexer"/>'s token stream in one
/// forward lexical pass followed by linear trust finalization.
/// <para>
/// The pass is a small state machine over three events. A <c>{</c> ends a header and opens a
/// scope; a <c>}</c> closes one; a <c>;</c> ends a header that never opened a scope. Everything
/// between those is accumulated as the header of a declaration that may or may not turn out to be
/// one. What makes this tractable without a parser is that a declaration is only recognized when
/// the enclosing scope is a type, so the pass never has to tell a member from a statement, a
/// lambda, or a local function: those all sit inside a method body, which is an anonymous scope.
/// </para>
/// </summary>
internal static class DeclarationIndexBuilder
{
    private readonly record struct RowRange(int Start, int EndExclusive);

    private sealed class Row
    {
        public DeclarationKind Kind;
        public string Name = "";
        public int TriviaStartLine;
        public int SignatureStartLine;
        public int SignatureStartColumn;
        public int FirstCodeColumn;
        public int SignatureEndLine;
        public int BodyStartLine = -1;
        public int BodyEndLine = -1;
        public int EndLine = -1;
        public int ParentIndex = -1;
        public int PreviousSiblingIndex = -1;
        public int LastChildIndex = -1;
        public int LastRefusedChildIndex = -1;
        public bool InitializerReachWalkedOutward;
        public bool SpanKnown = true;
        public bool IsStatic;
        public bool StaticModifierKnown;
        public bool IsPartial;
        public bool HasInitializer;
        public bool ClosesAtEndOfFile;
        public ImmutableArray<LineRange> AttributeLists = [];
    }

    public static ImmutableArray<DeclarationSpan> Build(
        IReadOnlyList<string> lines,
        out ImmutableArray<TransparentScopeSpan> transparentScopes,
        out ImmutableArray<ConditionalGroupSpan> conditionalGroups,
        out bool hasLineDirectives)
    {
        var tokens = CSharpLexer.ScanTokens(
            lines,
            out conditionalGroups,
            out hasLineDirectives);
        var rows = new List<Row>();
        int rootLastChildIndex = -1;
        int rootLastRefusedChildIndex = -1;
        var transparentScopeRows = ImmutableArray.CreateBuilder<TransparentScopeSpan>();
        var transparentScopeStarts =
            new Dictionary<int, (int StartLine, int BodyStartLine, int FirstRowIndex)>();
        var unknownRowRanges = new List<RowRange>();
        bool depthLost = false;

        // -1 marks an anonymous scope: a method body, a lambda, a property's accessor block, a
        // collection initializer. Members are only recognized inside a type, so an anonymous
        // scope is exactly what stops a local function from being indexed as a member.
        // Most scopes own the row whose body they delimit. Anonymous scopes and extension blocks
        // do not. An extension block still carries its enclosing type's index so its members land
        // on that type, but closing it must not close or otherwise mutate that enclosing row.
        //
        // A file-scoped namespace is different again: it owns a row but no brace closes it. The
        // scanner can see one in a conditional branch nested under a block namespace even though
        // that branch cannot compile. Its entry must not steal the block namespace's physical
        // closing brace in the configurations that drop the branch.
        // MembersKnown is meaningful only for transparent extension scopes. Their header has no
        // row to carry SpanKnown, so the scope carries that evidence to every member inside it.
        var scopes = new List<(
            int RowIndex,
            bool OwnsRow,
            bool ClosesWithBrace,
            bool MembersKnown)>();
        int unknownTransparentScopes = 0;
        var pending = new List<ScanToken>();
        int triviaStart = -1;

        // Trivia opens a row's span, so a branch-dependent trivia start is a branch-dependent
        // span. Comment and attribute-list tokens never reach `pending`, so neither `SpanKnown`
        // expression would otherwise consult them, and a doc comment or attribute list written
        // inside a conditional group would silently contribute one branch's start line to a row
        // the scan vouches for (adversarial review round 2, GPT-5.6 Sol). For a COMMENT, only the
        // token that opens the trivia matters: a later comment inside a group cannot move the
        // recorded start, and every line it occupies already falls inside the row's range. An
        // attribute list is not merely a line inside the range, so it is treated separately below.
        // Whether the recorded trivia run's START LINE is the same in every build. An
        // "assembly:" list legitimately discharges this one: such a list ends the trivia run above
        // it in every build, so whatever was above stops being the next declaration's problem.
        bool triviaKnown = true;

        // Knownness accumulated over EVERY token of the attribute list currently open, not just
        // its "[". A list can CROSS a conditional group, and what the tokens inside the group say
        // can decide whether the list binds to the declaration at all: in
        //
        //     [
        //     #if X
        //     assembly:
        //     #endif
        //     System.CLSCompliant(true)]
        //     class C { }
        //
        // the "[" is outside the group and known, but with X the list is a compilation-unit
        // attribute and C starts on the last line, while without X the list is C's own and C
        // starts on the first. Sampling at the "[" vouched for one of those two answers
        // (adversarial review round 4, GPT-5.6 Terra).
        bool attributeKnown = true;

        // Whether a header discarded in one branch may still be live in another. Kept apart from
        // the current declaration's attribute knownness: a branch-local attribute belongs to the
        // branch-local declaration that consumes it, so it makes that row unknown but must not
        // poison the first declaration after #endif. A header crossing between branches is
        // different; consuming it in the branch being scanned does not consume it in another
        // build, so that poison remains until a declaration outside the group spends it.
        bool headerKnown = true;

        // Whether every attribute list currently attached to the pending declaration exists in
        // every build. This contributes to that row's SpanKnown, then resets when the declaration
        // ends. Keeping it separate from headerKnown is gated by
        // ABranchLocalAttributedDeclaration_DoesNotPoisonTheFollowingRow.
        bool attachedAttributesKnown = true;
        int lastClosed = -1;
        int lastClosedSection = 0;
        bool inAttribute = false;
        int attributeDepth = 0;
        int attributeStart = 0;
        int attributeStartColumn = 0;
        int attributeSection = 0;
        int triviaSection = 0;
        int attributeWords = 0;
        bool unitTarget = false;
        bool unitAttribute = false;
        var attributeLists = new List<LineRange>();
        var attributeStarts = new List<(int Line, int Column)>();
        int nestedBraceDepth = 0;
        int lastTerminatorLine = 0;

        // The section of the terminator that last ended a declaration. A brace-less declaration
        // ends without closing a block, so it clears lastClosed while leaving no closed row and --
        // when it is a namespace inside a type -- no row at all. This is what lets the trailing-";"
        // rule notice that the thing standing between it and its target was branch-dependent.
        int lastTerminatorSection = 0;
        int namespaceScopeLostFrom = -1;
        bool inBlockComment = false;
        int commentOpenLine = 0;

        // Brace classification needs the header's delimiter and top-level-assignment state.
        // Carry it as tokens arrive rather than rescanning an ever-growing header at every nested
        // initializer brace. The stack also keeps mismatched delimiter kinds from cancelling into
        // a plausible span.
        var headerDelimiters = new List<string>();
        int openHeaderDelimiterCount = 0;
        bool headerDelimitersKnown = true;
        bool pendingHasTopLevelEquals = false;
        bool pendingInOperatorSymbol = false;

        string Text(ScanToken t) => t.TextIn(lines[t.Line]).ToString();
        Row? Enclosing() => scopes.Count > 0 && scopes[^1].RowIndex >= 0 ? rows[scopes[^1].RowIndex] : null;
        DeclarationHeaderGrammar.ScopeContext HeaderScope()
        {
            var enclosing = Enclosing();
            return enclosing is null
                ? DeclarationHeaderGrammar.ScopeContext.None
                : new(
                    true,
                    enclosing.Kind,
                    enclosing.Name,
                    enclosing.IsStatic,
                    enclosing.StaticModifierKnown,
                    enclosing.IsPartial);
        }

        void AddRow(Row row)
        {
            int index = rows.Count;
            if (row.ParentIndex >= 0)
            {
                row.PreviousSiblingIndex = rows[row.ParentIndex].LastChildIndex;
                rows[row.ParentIndex].LastChildIndex = index;
            }
            else
            {
                row.PreviousSiblingIndex = rootLastChildIndex;
                rootLastChildIndex = index;
            }

            rows.Add(row);
        }

        // A type at file scope and a statement inside a method body both report no enclosing row.
        // Only the first may declare anything: "@namespace = x;" in a method body is an
        // assignment, and reading it as a namespace is what an unqualified null check does.
        bool InAnonymousScope() => scopes.Count > 0 && scopes[^1].RowIndex < 0;
        int EnclosingIndex() => scopes.Count > 0 ? scopes[^1].RowIndex : -1;

        void AppendPending(ScanToken token, string tokenText)
        {
            if (token.Kind == ScanTokenKind.Word)
            {
                bool verbatim = pending.Count > 0
                    && pending[^1].Kind == ScanTokenKind.Punctuator
                    && Text(pending[^1]) == "@";
                if (headerDelimiters.Count == 0
                    && tokenText == "operator"
                    && !verbatim)
                {
                    pendingInOperatorSymbol = true;
                }
            }
            else if (token.Kind == ScanTokenKind.Punctuator)
            {
                if (tokenText is "(" or "[" or "{")
                {
                    headerDelimiters.Add(tokenText);
                    if (tokenText is "(" or "[")
                        openHeaderDelimiterCount++;
                    if (tokenText == "(")
                        pendingInOperatorSymbol = false;
                }
                else if (tokenText is ")" or "]" or "}")
                {
                    string expected = tokenText switch
                    {
                        ")" => "(",
                        "]" => "[",
                        _ => "{",
                    };
                    if (headerDelimiters.Count > 0 && headerDelimiters[^1] == expected)
                    {
                        headerDelimiters.RemoveAt(headerDelimiters.Count - 1);
                        if (expected is "(" or "[")
                            openHeaderDelimiterCount--;
                    }
                    else
                    {
                        headerDelimitersKnown = false;
                    }
                }
                else if (tokenText == "="
                    && headerDelimiters.Count == 0
                    && !pendingInOperatorSymbol)
                {
                    pendingHasTopLevelEquals = true;
                }
            }

            pending.Add(token);
        }

        // Ends the run of trivia, attribute lists and signature tokens gathered for the next
        // declaration, at a terminator sitting in conditional section <paramref name="section"/>.
        //
        // Whatever was gathered is discarded, and if the terminator was written in a DIFFERENT
        // branch than the gathered header, the discard is the whole problem: only one branch
        // compiles, so in the other build nothing discarded that header and it still belongs to
        // the declaration below. In
        //
        //     #if X
        //     // X docs
        //     #else
        //     using System;
        //     #endif
        //     class C { }
        //
        // the "using" terminator belongs to one branch and the comment to the other, so with X the
        // comment is C's documentation and without it C has none. Resetting unconditionally forgot
        // the comment AND restored knownness, and C was vouched for with the second build's answer
        // (adversarial review round 5, GPT-5.6 Sol).
        //
        // Trivia is not the only thing a terminator discards. Replace the comment with a bare
        // "public" and the same terminator throws away a MODIFIER, moving C's signature start
        // rather than its trivia start, so a rule keyed on recorded trivia alone still vouched for
        // the wrong span (round 6, GPT-5.6 Sol).
        //
        // The test is section identity, not the terminator's DepthKnown. DepthKnown asks "is this
        // inside an unresolved group?", which is true of every ordinary statement inside every
        // group, so keying on it condemns a file's whole conditional content -- it was tolerable
        // while only trivia consulted it and is not once signature tokens do. Section identity asks
        // the question the defect is actually about: were the header and the terminator that ate it
        // written in the same branch? Sections are conservative in the safe direction only (see
        // ScanToken.Section), so a header entirely inside one branch is never condemned.
        //
        // Gated by ATerminatorInOneBranchDiscardingAnothersModifier_LosesTheRowBelow,
        // ATerminatorInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow,
        // ATerminatorInOneBranchDiscardingAnothersAttribute_LosesTheRowBelow and
        // AnInitializerInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow, against
        // ATerminatorInsideAGroupDiscardingNothing_StillVouchesForTheRowBelow and
        // AStatementInsideAGroup_StillVouchesForTheRowBelow, which pin that a header and terminator
        // sharing a branch are left alone.
        void ResetHeader(ScanToken terminator)
        {
            bool crossesABranch =
                (triviaStart >= 0 && triviaSection != terminator.Section) ||
                (pending.Count > 0 && pending[0].Section != terminator.Section);

            pending.Clear();
            headerDelimiters.Clear();
            openHeaderDelimiterCount = 0;
            headerDelimitersKnown = true;
            pendingHasTopLevelEquals = false;
            pendingInOperatorSymbol = false;

            // A reset INSIDE an unresolved group may only take knownness away, never restore it,
            // because the declaration it just finished exists in one build and not the other. In
            //
            //     #if X
            //     // doc
            //     #else
            //     struct s { }
            //     #endif
            //     class Tail { }
            //
            // the comment is discarded by "struct s {", which poisons; then "}" resets again with
            // nothing recorded and nothing crossing, and ASSIGNING there declared the header clean
            // while still inside the group. But with X there is no "struct s" to have eaten the
            // comment, so the comment is Tail's documentation and Tail's trivia is line 2, not 6.
            // Intersecting instead keeps the poison until the group closes, which is exactly how
            // long the other build's header stays live. Found by a differential fuzzer over 16,673
            // fair cases (adversarial review round 6, Claude Opus 4.8); 2,597 flags, all this.
            //
            // Outside a group the assignment is what discharges a spent poison: the declaration
            // that just ended exists in every build, so the next header genuinely starts fresh.
            // Gated by ADiscardedHeaderInsideAGroup_StaysLostAcrossALaterCleanReset and
            // ADiscardedAttributeInsideAGroup_StaysLostAcrossALaterCleanReset, against
            // ATerminatorInsideAGroupDiscardingNothing_StillVouchesForTheRowBelow and
            // AStatementInsideAGroup_StillVouchesForTheRowBelow.
            // Nothing is recorded once this returns, so the trivia-line claim starts clean; the
            // crossing, if any, is a claim about the header that was thrown away, which outlives
            // this reset whenever the reset itself only happened in one build.
            triviaKnown = true;

            if (terminator.DepthKnown)
                headerKnown = true;
            else
                headerKnown &= !crossesABranch;

            attachedAttributesKnown = true;
            triviaStart = -1;
            attributeLists.Clear();
            attributeStarts.Clear();
        }

        void EndDeclaration(ScanToken terminator)
        {
            ResetHeader(terminator);
            lastTerminatorLine = terminator.Line + 1;
            lastTerminatorSection = terminator.Section;
        }

        // Emits a declaration that has no scope of its own: a field, an enum member, an abstract
        // or interface member, an extern member, a positional record, an expression-bodied member,
        // or a file-scoped namespace.
        Row EmitBodiless(
            ScanToken terminator,
            DeclarationKind kind,
            string name,
            int bodyStart,
            bool hasInitializer = false)
        {
            int sigStart = pending.Count > 0 ? pending[0].Line + 1 : terminator.Line + 1;
            int sigColumn = pending.Count > 0 ? pending[0].Column : terminator.Column;
            var row = new Row
            {
                Kind = kind,
                Name = name,
                TriviaStartLine = triviaStart >= 0 ? triviaStart : sigStart,
                SignatureStartLine = sigStart,
                SignatureStartColumn = sigColumn,
                FirstCodeColumn = FirstCodeColumn(sigStart, sigColumn),
                SignatureEndLine = terminator.Line + 1,
                BodyStartLine = bodyStart,
                EndLine = terminator.Line + 1,
                ParentIndex = EnclosingIndex(),
                AttributeLists = [.. attributeLists],
                SpanKnown = terminator.DepthKnown && triviaKnown && headerKnown
                    && attachedAttributesKnown && headerDelimitersKnown
                    && unknownTransparentScopes == 0
                    && pending.All(t => t.DepthKnown),
                IsStatic = DeclarationHeaderGrammar.HasTopLevelKeyword(
                    DeclarationHeaderGrammar.Truncate(pending, Text).Header,
                    "static",
                    Text),
                HasInitializer = hasInitializer,
            };
            AddRow(row);
            return row;
        }

        // Every declarator in one field or event declaration shares its measured span. Copy those
        // facts instead of rescanning the complete header once per comma-separated name, while
        // retaining the initializer fact recovered for this particular declarator.
        void EmitAdditionalBodiless(
            Row sharedDeclaration,
            DeclarationHeaderGrammar.Declarator declarator)
        {
            AddRow(new Row
            {
                Kind = sharedDeclaration.Kind,
                Name = declarator.Name,
                TriviaStartLine = sharedDeclaration.TriviaStartLine,
                SignatureStartLine = sharedDeclaration.SignatureStartLine,
                SignatureStartColumn = sharedDeclaration.SignatureStartColumn,
                FirstCodeColumn = sharedDeclaration.FirstCodeColumn,
                SignatureEndLine = sharedDeclaration.SignatureEndLine,
                BodyStartLine = sharedDeclaration.BodyStartLine,
                BodyEndLine = sharedDeclaration.BodyEndLine,
                EndLine = sharedDeclaration.EndLine,
                ParentIndex = sharedDeclaration.ParentIndex,
                AttributeLists = sharedDeclaration.AttributeLists,
                SpanKnown = sharedDeclaration.SpanKnown,
                IsStatic = sharedDeclaration.IsStatic,
                HasInitializer = declarator.HasInitializer,
                ClosesAtEndOfFile = sharedDeclaration.ClosesAtEndOfFile,
            });
        }

        // The index in "pending" of the first "=" that could be an initializer tail reaching BACK
        // through a conditional group, or -1 when there is none.
        //
        // An "=" reaches back when every token before it belongs to some OTHER branch, because
        // then there is a build in which none of those tokens exist and the "=" is the first thing
        // after whatever preceded the group. The FIRST "=" is not necessarily that one: a branch
        // can carry a complete declaration whose "=" is ordinary while the branch beside it carries
        // the bare tail, as in
        //
        //     int P { get; }
        //     #if X
        //         int Q = 0
        //     #else
        //         = 1
        //     #endif
        //         ;
        //
        // where "int Q = 0" masks the "= 1" that still binds to P when X is undefined. So this
        // takes the first "=" that QUALIFIES, not the first that exists. Found by adversarial
        // review round 8 (GPT-5.6 Sol).
        //
        // Sections increase monotonically as directives are encountered. Therefore an earlier
        // token shares an "=" token's section exactly when the immediately preceding pending
        // token does. Inspecting that one predecessor preserves the rule without rescanning an
        // earlier branch once per "=". Gated by
        // ConditionalInitializerTail_ExaminesEachPendingTokenOnce.
        int ReachingBackEquals()
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Kind != ScanTokenKind.Punctuator || Text(pending[i]) != "=")
                    continue;

                if (i == 0 || pending[i - 1].Section != pending[i].Section)
                    return i;
            }

            return -1;
        }

        // Whether the run under construction can be absent from a build in which the terminator
        // that follows it is present. Both terms are needed and neither implies the other.
        //
        // DepthKnown is false for a token inside a conditional group, and also for every token
        // after a group that failed to balance -- the scan stops knowing the depth at that point
        // and does not recover, so an unknown token is not necessarily a token inside a group.
        // Round 10 wrote "exactly for a token inside a conditional group" here, which round 11
        // (Gemini 3.1 Pro) falsified: in
        //
        //     #if A
        //     class Opened {
        //     #endif
        //     class Tail { }
        //
        // Tail lies outside every group and is still refused. What the rule needs is only the
        // weaker direction, and that direction holds: a token OUTSIDE every group always has
        // DepthKnown true, so a run in which every token is unknown contains no token that every
        // build is guaranteed to have. The extra unknown tokens an unbalanced group contributes
        // can only add refusals, never remove one, which is the safe direction -- and a run that
        // is genuinely in every build is normally caught by the section test below instead.
        // This is what keeps an ordinary member carrying only a CONDITIONAL INITIALIZER out of
        // this rule.
        //
        // The section test then asks whether the terminator goes with them. A run and a terminator
        // written in one branch vanish together and nothing reaches back; only a terminator written
        // somewhere else can survive the run's disappearance. Sections are conservative in the safe
        // direction only, so this raises false alarms and never misses.
        bool PendingCanVanishBefore(int section)
        {
            if (pending.Count == 0) return false;

            foreach (var p in pending)
                if (p.DepthKnown || p.Section == section)
                    return false;

            return true;
        }

        // Revokes the vouch for every row that a terminator could have bound to instead: the run of
        // siblings under one parent ending at the last block this group closed, or at the most
        // recent row when the group already consumed it.
        //
        // The walk continues OUTWARD through any parent that is itself branch-dependent, and that
        // is not decoration. A file-scoped namespace declared inside a group opens a scope with no
        // brace, so it re-parents the row the terminator appears to follow while the row it can
        // actually reach in the build WITHOUT the group sits at the outer scope, where a walk over
        // one parent never looks:
        //
        //     class A
        //     {
        //     }
        //     #if Y
        //     namespace NS;
        //     class B
        //     {
        //     }
        //     #endif
        //     ;
        //
        // Without Y the file is "class A {\n}\n;" -- a legal program, zero errors, in which A ends
        // at the ";" on line 10 -- and A was vouched at 1..3. Stopping at a parent that is VOUCHED
        // is what keeps this from being a blunt "refuse everything": a vouched parent exists
        // identically in every build, so the scope it opens exists in every build and no terminator
        // can escape it. Direct siblings are linked in source order, and each parent remembers both
        // the newest sibling already refused and whether its outward ancestor prefix was already
        // visited. A later child is still refused before that second memo stops the walk, but it
        // cannot add a new candidate outside its parent: that outer prefix still ends at the same
        // parent. A vouched stopping parent is deliberately not marked, because another refusal may
        // make it unknown later. SpanKnown only moves true to false and ParentIndex never changes,
        // so each direct sibling is refused once and each outward ancestor edge is traversed once
        // across the scan. Gated respectively by
        // ConditionalSiblingFanOut_RefusesEachSiblingOnce and
        // ConditionalNamespaceChainAndRepeatedTerminators_TraverseEachOutwardEdgeOnce. Found by
        // adversarial review rounds 10, 15, and 16 (Claude Opus 5).
        void RefuseSiblingPrefix(int parent, int lastChild)
        {
            int alreadyRefused = parent >= 0
                ? rows[parent].LastRefusedChildIndex
                : rootLastRefusedChildIndex;
            if (lastChild <= alreadyRefused)
                return;

            int newestRefused = lastChild;
            while (lastChild > alreadyRefused)
            {
                rows[lastChild].SpanKnown = false;
                lastChild = rows[lastChild].PreviousSiblingIndex;
            }

            if (parent >= 0)
                rows[parent].LastRefusedChildIndex = newestRefused;
            else
                rootLastRefusedChildIndex = newestRefused;
        }

        void RefuseSiblingsAnInitializerCouldReach()
        {
            int parent = lastClosed >= 0 ? rows[lastClosed].ParentIndex : EnclosingIndex();
            int lastChild = lastClosed >= 0
                ? lastClosed
                : parent >= 0
                    ? rows[parent].LastChildIndex
                    : rootLastChildIndex;

            while (true)
            {
                RefuseSiblingPrefix(parent, lastChild);

                if (parent < 0 || rows[parent].SpanKnown)
                    return;

                if (rows[parent].InitializerReachWalkedOutward)
                    return;
                rows[parent].InitializerReachWalkedOutward = true;

                lastChild = parent;
                parent = rows[parent].ParentIndex;
            }
        }

        foreach (var tok in tokens)
        {
            if (!tok.DepthKnown)
                depthLost = true;

            if (tok.Kind == ScanTokenKind.Directive)
                continue;

            // Comment and literal tokens are excluded, and that exclusion is load-bearing in both
            // directions. A comment or literal inside a group inside a list can move neither end
            // of the list -- "[" and "]" are punctuators -- nor decide its target, which is a
            // word, so refusing on one would cost recall for nothing, exactly as refusing on a
            // conditional comment in trivia would. The case where a literal or comment DOES
            // diverge between builds is one where it opens or closes unevenly, and the existing
            // hidden-directive guard has already lost the depth for the whole file by then, so
            // the "]" is unknown regardless.
            if (inAttribute && tok.Kind is not ScanTokenKind.Comment
                and not ScanTokenKind.StringLiteral and not ScanTokenKind.CharLiteral)
            {
                attributeKnown &= tok.DepthKnown;
            }

            if (tok.Kind == ScanTokenKind.Comment)
            {
                // A block comment yields one token per line it covers, so the token's own line is
                // not where its comment began. What decides trivia is where the comment OPENED:
                // "int A; /* x\n y */" opens on A's terminator line and trails A, and its second
                // line must not become the next declaration's trivia start -- a slice taken from
                // there would begin inside the comment, after its "/*".
                //
                // Which token opens a comment cannot be read off the text: a continuation line may
                // itself start with "//" or "/*", both measured by dumping tokens. It follows from
                // the carried state instead, which is reconstructed here.
                var comment = Text(tok);
                bool opens = !inBlockComment;
                if (opens)
                {
                    commentOpenLine = tok.Line + 1;
                    // A line comment opens no block. A block comment closes on its opening line
                    // only if a "*/" follows the "/*" rather than overlapping it: "/*/" is not a
                    // comment, it is an unterminated one.
                    inBlockComment = comment.StartsWith("/*", StringComparison.Ordinal)
                        && !(comment.Length >= 4 && comment.EndsWith("*/", StringComparison.Ordinal));
                }
                else if (comment.EndsWith("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                }

                // Trivia only counts while a header has not started. A comment sitting inside a
                // signature is not the declaration's leading documentation, and one sitting on the
                // line that ended the previous declaration trails that declaration rather than
                // opening this one.
                if (pending.Count == 0 && !inAttribute && triviaStart < 0 && commentOpenLine > lastTerminatorLine)
                {
                    triviaStart = commentOpenLine;
                    triviaSection = tok.Section;
                    triviaKnown &= tok.DepthKnown;
                }
                continue;
            }

            if (tok.Kind is ScanTokenKind.StringLiteral or ScanTokenKind.CharLiteral)
            {
                if (!inAttribute)
                    pending.Add(tok);
                continue;
            }

            var text = Text(tok);

            // An attribute list is leading trivia. Bracket nesting is tracked here rather than
            // read from ScanToken.BracketDepth because that field counts any square brackets,
            // including an indexer's and an element access's; a list is only an attribute list
            // when it opens where a header has not yet started.
            if (inAttribute)
            {
                if (text == "[")
                {
                    attributeDepth++;
                }
                else if (text == "]" && --attributeDepth == 0)
                {
                    inAttribute = false;
                    var list = new LineRange(attributeStart, tok.Line + 1);

                    // An "assembly:" or "module:" list belongs to the compilation unit, not to
                    // whatever follows it, and only the FIRST list in a run can be one: once a
                    // list has bound to the declaration below, C# binds every later list to that
                    // same declaration too, so "[Obsolete][assembly: X] class A" applies both to A
                    // (CS0657) and dropping the second would take the first's trivia with it.
                    if (unitAttribute && attributeLists.Count == 0)
                    {
                        // The list neither opens trivia nor lets earlier trivia through: Roslyn
                        // reports the next declaration's leading trivia as starting after it, so a
                        // file header comment above one belongs to the list. Ending the list here
                        // also makes a comment on its closing line trail the LIST rather than open
                        // the next declaration's trivia -- "[assembly: X] // note" is a comment
                        // about the attribute.
                        triviaStart = -1;

                        // The same assign-versus-intersect rule ResetHeader follows, and the one
                        // restore site the round-6 fix first overlooked. This path ends a header
                        // too, so inside an unresolved group it may only take knownness away. In
                        //
                        //     #if Y
                        //     #else
                        //     [System.Obsolete]
                        //     #endif
                        //     #if X
                        //     class t1 { }
                        //     #endif
                        //     [assembly: System.CLSCompliant(true)]
                        //     class Tail { }
                        //
                        // the line-3 list poisons; t1's reset empties attributeLists but rightly
                        // keeps the poison; then this path ASSIGNED it away, and Tail was vouched
                        // with one build's answer. Without Y, Roslyn binds the line-3 list to Tail
                        // and the unit list with it (CS0657), so Tail's trivia is line 3 and it
                        // carries two lists; with Y its trivia is line 9 and it carries none. No
                        // single line range describes both. Found by the differential fuzzer
                        // (adversarial review round 6, Claude Opus 4.8) after the ResetHeader fix
                        // had already cut its flag count from 3,146 to 6 -- every survivor this
                        // one site. Gated by
                        // AUnitAttributeAfterADiscardedAttribute_DoesNotRestoreTheVouch.
                        //
                        // It intersects unconditionally, where ResetHeader assigns outside a
                        // group. The difference is that ResetHeader runs where a DECLARATION
                        // ended, which happens in every build and so genuinely spends the header
                        // it consumed; this path runs where a list merely closed, consuming
                        // nothing. A poison reaching here is still live no matter what depth the
                        // bracket sits at -- above, the list is outside the group entirely and the
                        // vouch was still wrong.
                        triviaKnown = true;
                        headerKnown &= attachedAttributesKnown && attributeKnown;
                        attachedAttributesKnown = true;

                        lastTerminatorLine = tok.Line + 1;
                    }
                    else
                    {
                        attributeLists.Add(list);
                        attributeStarts.Add((attributeStart, attributeStartColumn));

                        // Every list, not just the one that opened the trivia. A list written
                        // inside a conditional group is reported in AttributeLists even though
                        // only one build compiles it, and unlike a trivia comment it is not merely
                        // a line inside the row's range -- it is a claim about what is applied to
                        // the declaration. A row whose lists depend on the build is not vouched
                        // for (adversarial review round 3, Gemini 3.1 Pro).
                        attachedAttributesKnown &= attributeKnown;
                        if (triviaStart < 0)
                        {
                            triviaStart = attributeStart;
                            triviaSection = attributeSection;
                        }
                    }
                }
                else if (attributeDepth == 1)
                {
                    // The colon is what makes this a target rather than an attribute name:
                    // "[assembly]" applies a type called assemblyAttribute and IS the following
                    // declaration's trivia. A kind test on the word would be redundant -- literals,
                    // comments and directives are skipped before this point, and every punctuator
                    // the scanner emits is one character.
                    //
                    // "@" is one of those one-character tokens and escapes the word that follows,
                    // so it does not occupy a word position: Roslyn reads "[@assembly: X]" as a
                    // compilation-unit attribute exactly as it reads "[assembly: X]".
                    if (attributeWords == 0 && text == "@")
                    {
                        // Not a word position; the escaped word is still position 0.
                    }
                    else if (attributeWords == 0)
                    {
                        unitTarget = text is "assembly" or "module";
                        attributeWords++;
                    }
                    else
                    {
                        if (attributeWords == 1 && text == ":" && unitTarget)
                            unitAttribute = true;
                        attributeWords++;
                    }
                }
                continue;
            }
            if (pending.Count == 0 && text == "[")
            {
                inAttribute = true;
                attributeDepth = 1;
                attributeStart = tok.Line + 1;
                attributeStartColumn = tok.Column;
                attributeSection = tok.Section;
                // Subsumed by the accumulation above for any input that compiles in both
                // configurations: the closing "]" is accumulated too, and for this seed to decide
                // the outcome the group would have to close between the "[" and the rest of the
                // list -- a file whose other build has no "[" at all. Mutation M6 (seed `true`)
                // accordingly survives the suite. Kept as the conservative initialization and
                // marked UNVERIFIED rather than cited to a gate (adversarial review round 4).
                attributeKnown = tok.DepthKnown;
                attributeWords = 0;
                // unitTarget needs no reset: the first word of every list assigns it, and it is
                // read only after that assignment. An explicit one here would be a dead store that
                // reads as a load-bearing rule.
                unitAttribute = false;
                continue;
            }

            if (text == "{")
            {
                // "= new(...) { ... }" is an initializer, not a member body. Neither is a brace
                // nested inside an open header delimiter: array initializers can occur in
                // attributes on parameters (including extension receivers), while object
                // initializers, lambda blocks, and property patterns can occur in constructor
                // arguments. Keep the header until the nested construct closes so the next
                // top-level brace can open the declaration body.
                if (nestedBraceDepth > 0
                    || pendingHasTopLevelEquals
                    || openHeaderDelimiterCount > 0)
                {
                    nestedBraceDepth++;
                    scopes.Add((-1, false, true, true));
                    AppendPending(tok, text);
                    continue;
                }

                // A C# 14 extension block is a scope but not a declaration: its members are emitted
                // onto the enclosing static class, so the index makes it transparent and lets them
                // land there too. Giving it a row of its own would put every extension member
                // inside a parent that has no metadata counterpart.
                var extensionScope = DeclarationHeaderGrammar.ClassifyExtensionScope(pending, HeaderScope(), Text);
                if (extensionScope is not DeclarationHeaderGrammar.ExtensionScopeKind.None)
                {
                    int startLine = triviaStart >= 0 ? triviaStart : pending[0].Line + 1;
                    bool membersKnown = extensionScope is DeclarationHeaderGrammar.ExtensionScopeKind.Known
                        && tok.DepthKnown && triviaKnown && headerKnown
                        && attachedAttributesKnown && headerDelimitersKnown
                        && pending.All(t => t.DepthKnown);
                    transparentScopeStarts[scopes.Count] =
                        (startLine, tok.Line + 1, rows.Count);
                    scopes.Add((EnclosingIndex(), false, true, membersKnown));
                    if (!membersKnown)
                        unknownTransparentScopes++;
                    EndDeclaration(tok);
                    lastClosed = -1;
                    continue;
                }

                var (kind, name) = DeclarationHeaderGrammar.Classify(pending, HeaderScope(), opensBody: true, Text);
                if (kind is { } k && Allowed(k, Enclosing(), InAnonymousScope()))
                {
                    int sigStart = pending.Count > 0 ? pending[0].Line + 1 : tok.Line + 1;
                    int sigColumn = pending.Count > 0 ? pending[0].Column : tok.Column;
                    var declarationHeader = DeclarationHeaderGrammar.Truncate(pending, Text).Header;
                    var row = new Row
                    {
                        Kind = k,
                        Name = name,
                        TriviaStartLine = triviaStart >= 0 ? triviaStart : sigStart,
                        SignatureStartLine = sigStart,
                        SignatureStartColumn = sigColumn,
                        FirstCodeColumn = FirstCodeColumn(sigStart, sigColumn),
                        SignatureEndLine = tok.Line + 1,
                        BodyStartLine = tok.Line + 1,
                        ParentIndex = EnclosingIndex(),
                        AttributeLists = [.. attributeLists],
                        SpanKnown = tok.DepthKnown && triviaKnown && headerKnown
                            && attachedAttributesKnown && headerDelimitersKnown
                            && unknownTransparentScopes == 0
                            && pending.All(t => t.DepthKnown),
                        IsStatic = DeclarationHeaderGrammar.HasTopLevelKeyword(declarationHeader, "static", Text),
                        StaticModifierKnown = headerKnown
                            && declarationHeader.All(t => t.DepthKnown),
                        IsPartial = DeclarationHeaderGrammar.HasTopLevelKeyword(declarationHeader, "partial", Text),
                    };
                    AddRow(row);
                    scopes.Add((rows.Count - 1, true, true, true));
                }
                else
                {
                    scopes.Add((-1, false, true, true));
                }
                EndDeclaration(tok);
                lastClosed = -1;
                continue;
            }

            if (text == "}")
            {
                if (nestedBraceDepth > 0)
                {
                    nestedBraceDepth--;
                    if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1);
                    AppendPending(tok, text);
                    continue;
                }

                // An enum's last member needs no trailing comma, so the closing brace terminates it.
                //
                // The eleventh way: an enum member's initializer can reach back exactly as a
                // field's can, but it is terminated by "," or "}" and so never passes through the
                // ";" path that refuses it. In
                //
                //     enum E {
                //         A
                //     #if X
                //         , B
                //     #endif
                //         = 1
                //     }
                //
                // the "= 1" belongs to B with X and to A without it, so A ends on line 2 or line 6.
                // A was already emitted and vouched at the branch-local "," by the time the "="
                // was read. Found by adversarial review round 8 (Gemini 3.1 Pro).
                if (Enclosing() is { Kind: DeclarationKind.Enum } && pending.Count > 0)
                {
                    if (ReachingBackEquals() >= 0)
                        RefuseSiblingsAnInitializerCouldReach();

                    var (ek, en) = DeclarationHeaderGrammar.Classify(pending, HeaderScope(), opensBody: false, Text);
                    if (ek is not null)
                        EmitBodiless(
                            pending[^1],
                            DeclarationKind.EnumMember,
                            en,
                            bodyStart: -1,
                            hasInitializer: DeclarationHeaderGrammar.Truncate(pending, Text).CutAtEquals);
                    EndDeclaration(tok);
                }

                if (scopes.Count > 0)
                {
                    // A file-scoped namespace has no matching brace. If one was written in a
                    // conditional branch inside a block namespace, the next physical "}" closes
                    // the block namespace in every configuration that can compile; letting the
                    // brace-less entry consume it shifts every enclosing row's EndLine outward by
                    // one. Drop such entries before finding the scope this brace actually closes.
                    // Found by adversarial review round 13 (Claude Opus 5).
                    while (scopes.Count > 0 && !scopes[^1].ClosesWithBrace)
                        scopes.RemoveAt(scopes.Count - 1);

                    if (scopes.Count == 0)
                    {
                        lastClosed = -1;
                        EndDeclaration(tok);
                        continue;
                    }

                    int scopeIndex = scopes.Count - 1;
                    if (transparentScopeStarts.Remove(
                        scopeIndex,
                        out var transparentStart))
                    {
                        transparentScopeRows.Add(new TransparentScopeSpan(
                            transparentStart.StartLine,
                            transparentStart.BodyStartLine,
                            tok.Line + 1));
                        // A branch-dependent close makes ownership just as uncertain as an
                        // uncertain opener, including for rows emitted before the close was seen.
                        if (!tok.DepthKnown || !scopes[^1].MembersKnown)
                            unknownRowRanges.Add(
                                new RowRange(transparentStart.FirstRowIndex, rows.Count));
                    }

                    var (idx, ownsRow, _, membersKnown) = scopes[^1];
                    scopes.RemoveAt(scopes.Count - 1);
                    if (!membersKnown)
                        unknownTransparentScopes--;
                    if (idx >= 0 && ownsRow)
                    {
                        rows[idx].BodyEndLine = tok.Line + 1;
                        rows[idx].EndLine = tok.Line + 1;
                        if (!tok.DepthKnown) rows[idx].SpanKnown = false;
                        lastClosed = idx;
                        lastClosedSection = tok.Section;
                    }
                    else
                    {
                        lastClosed = -1;
                    }
                }
                EndDeclaration(tok);
                continue;
            }

            if (text == ";")
            {
                // "=> Decode(() => { ...; })" — a statement terminator inside an initializer or a
                // lambda body is not the declaration's terminator.
                if (nestedBraceDepth > 0)
                {
                    AppendPending(tok, text);
                    continue;
                }

                // "public List<T>? Edges { get; } = edges;" — the initializer belongs to the
                // property whose accessor block just closed, not to a new declaration.
                //
                // The ninth way a group can change meaning: WHICH declaration an initializer
                // extends is itself branch-dependent. In
                //
                //     class C {
                //         public int P { get; }
                //     #if X
                //         public int Q { get; }
                //     #endif
                //         = 1;
                //     }
                //
                // the "= 1;" belongs to Q with X and to P without it, so P ends on line 2 in one
                // build and line 6 in the other -- both parsing with zero errors. Every rule above
                // this one asks whether a header written BEFORE a group survives it; this is the
                // reverse, an initializer reaching BACK through a group, so nothing above can see
                // it and P was vouched unconditionally.
                //
                // It has two shapes, and the second is why this test is not simply
                // "lastClosed >= 0". When the initializer sits inside the group instead of after
                // it, the block it appears to extend can be one the same group already consumed,
                // leaving lastClosed == -1 at a bare "= 0;" that still binds to a declaration
                // above the group in the other build:
                //
                //     class C {
                //         int p { get; }
                //     #if X
                //         int p { get; } = 0;
                //     #elif Y
                //         = 0;
                //     #endif
                //     }
                //
                // So the vouch survives only when the "=", the ";", and the "}" that produced the
                // target are provably in one branch. Otherwise every declaration under the same
                // parent is a candidate target -- an "#elif" chain offers several -- and the
                // refusal takes the whole run rather than guessing. Sections are conservative in
                // the safe direction only (see ScanToken.Section), so an ordinary
                // "{ get; } = value;" is never condemned. Found by adversarial review round 7
                // (Gemini 3.1 Pro); the second shape independently by Claude Opus 5 and by the
                // member-scoped fuzzer generator that round adopted.
                // The "=" does not have to be pending[0]: a modifier left behind by ANOTHER branch
                // can sit in front of it, as in an "#if X / public / #else / = 1; / #endif" pair
                // where "public" and "= 1;" never coexist in a build. The "=" starts its own run
                // whenever everything before it belongs to some other branch, so that -- not
                // position zero -- is the test.
                // The FIRST "=" is not necessarily the one that reaches back. A branch can carry a
                // complete declaration of its own whose "=" is ordinary, while the branch beside it
                // carries the bare tail:
                //
                //     class C {
                //         int P { get; }
                //     #if X
                //         int Q = 0
                //     #else
                //         = 1
                //     #endif
                //         ;
                //     }
                //
                // "int Q = 0" makes the first "=" a same-section initializer, but the "= 1" behind
                // it still binds to P when X is undefined, so P ends on line 3 or line 9 depending
                // on the build. Stopping at the first "=" hid that, so the search takes the first
                // "=" that QUALIFIES rather than the first that exists. Found by adversarial review
                // round 8 (GPT-5.6 Sol).
                int eq = ReachingBackEquals();

                bool initializerTail = eq >= 0;

                if (initializerTail)
                {
                    // The third conjunct is conservative and UNVERIFIED as a distinct requirement:
                    // no mutation has produced a wrong vouch from dropping it (adversarial review
                    // round 9, Claude Opus 5), and it may be equivalent to the first two. For
                    // eq > 0 a ";" in the same section as a qualifying "=" would force the tokens
                    // between them into that section, which disqualifies the "="; for eq == 0 a
                    // ";" in a different section is either inside a group, where the DepthKnown
                    // test below revokes the vouch anyway, or after a balanced one, where it sits
                    // on the same physical line in every build. It is kept because the argument is
                    // about reachability rather than safety, and its only observed effect is
                    // over-refusal. Do not cite it as gated.
                    bool oneBranch = lastClosed >= 0
                        && lastClosedSection == pending[eq].Section
                        && lastClosedSection == tok.Section;

                    if (!oneBranch)
                    {
                        RefuseSiblingsAnInitializerCouldReach();
                    }

                    if (lastClosed >= 0 && eq == 0)
                    {
                        rows[lastClosed].EndLine = tok.Line + 1;
                        rows[lastClosed].HasInitializer = true;

                        // This extends a span that was already measured and marked known when its
                        // accessor block closed, so it needs the same correction that close took:
                        // a conditional between the block and the initializer puts the ";" in a
                        // branch, and the end this reads is one branch's, not the declaration's.
                        if (!tok.DepthKnown) rows[lastClosed].SpanKnown = false;

                        ResetHeader(tok);
                        lastClosed = -1;
                        continue;
                    }
                }

                // The twelfth way, and the third direction. Ways 1-8 ask whether a header written
                // BEFORE a group survives it; ways 9-11 are a tail reaching BACK through one. This
                // is a declaration already closed, measured and vouched reaching FORWARD to claim a
                // terminator written after it.
                //
                // A type or namespace declaration takes an optional trailing ";" that the grammar
                // puts in the production itself ("class_declaration : ... class_body ';'?"). The
                // scan did not model it at all, which was a wrong span even with no conditionals:
                // "class A {\n}\n;" ended A at the brace where Roslyn ends it at the ";". Under a
                // conditional it becomes a wrong VOUCH, because which declaration owns the ";" is
                // branch-dependent:
                //
                //     class A
                //     {
                //     }
                //     #if Y
                //     class B { }
                //     #endif
                //     ;
                //
                // With Y the ";" is B's and A ends at its brace; without Y there is no B and the
                // ";" is A's own, so A ends four lines later. Both builds compile with zero errors.
                // Found by adversarial review round 9 (Claude Opus 5), which also established that
                // this PR is what promotes the row to vouched: before balanced-group recovery the
                // leading group poisoned the rest of the file and A was declined anyway.
                bool trailerTarget = lastClosed >= 0 && rows[lastClosed].Kind
                        is DeclarationKind.Class or DeclarationKind.Struct
                        or DeclarationKind.Interface or DeclarationKind.Record
                        or DeclarationKind.Enum or DeclarationKind.Namespace;

                if (pending.Count == 0 && trailerTarget)
                {
                    if (lastClosedSection == tok.Section && tok.DepthKnown)
                    {
                        rows[lastClosed].EndLine = tok.Line + 1;
                    }
                    else
                    {
                        // The ";" and the "}" that produced the row are in different branches, so
                        // the ";" could have attached to a declaration this build does not show,
                        // and no candidate's end is provable. Do not extend: the extension itself
                        // is one branch's answer. The candidate set is NOT simply the siblings of
                        // the row the ";" appears to follow -- a brace-less scope opener inside the
                        // group re-parents that row -- which is why the refusal walks outward
                        // through branch-dependent parents. See
                        // RefuseSiblingsAnInitializerCouldReach.
                        RefuseSiblingsAnInitializerCouldReach();
                    }

                    EndDeclaration(tok);
                    lastClosed = -1;
                    continue;
                }

                // The scan can forget the row that the ";" appears to follow, or remember a row
                // that itself exists only in one branch. In either case, if both the pending run
                // and the remembered predecessor can vanish before this terminator, the ";" can
                // reach an earlier declaration that the lexical scan no longer sees as adjacent.
                //
                // A file-scoped namespace is the first shape: it ENDS a declaration without
                // closing a block, so it leaves lastClosed at -1:
                //
                //     class Sr { }
                //     #if X
                //     namespace Nr;
                //     #endif
                //     ;
                //
                // Without X the file is "class Sr { }\n;" and Sr ends at the ";" on line 5. The
                // build WITH X does not parse, so only one configuration is fair and the pairwise
                // build-vs-build gate can never see this: it was the PRODUCT gate, comparing the
                // product's own numbers against the single valid build, that caught it.
                //
                // Such a namespace can leave no row at all when it appears inside a type. It does
                // leave a terminator, so lastTerminatorSection carries the branch evidence that
                // lastClosed cannot. Found by round 10 (Claude Opus 5).
                //
                // Requiring an EMPTY pending run then let a second group mask that fix:
                //
                //     class C {
                //         class Sr { }
                //     #if X
                //         namespace Nr;
                //     #endif
                //     #if Y
                //         int Field = 1
                //     #endif
                //         ;
                //     }
                //
                // Round 12 supplied the last missing combination: a brace-bodied non-type member
                // leaves lastClosed >= 0 but is not a trailerTarget, so the old kind test defeated
                // both masked-trailer arms while the nonnegative index defeated the brace-less
                // arm. With X below, Mh and Fh stand between Sh and the ";"; without X they both
                // vanish and the ";" becomes Sh's optional trailer:
                //
                //     class Sh { }
                // #if X
                //     void Mh() { }
                //     int Fh = 1
                // #endif
                //     ;
                //
                // The relevant question is therefore not the remembered row's kind. It is whether
                // that row (or the terminator that replaced it when lastClosed is -1) and the
                // pending run can both be absent in a build where this ";" remains. A section
                // difference means only that a directive boundary intervened; even an empty group
                // changes the section and can conservatively decline the row. Text with no
                // intervening directive shares the terminator's section. Found by adversarial
                // review round 12 (Claude Opus 5); corrected for the empty-group counterexample in
                // round 13 (Claude Opus 5).
                bool predecessorCanVanish = lastClosed >= 0
                    ? lastClosedSection != tok.Section
                    : lastTerminatorSection != tok.Section;
                if ((pending.Count == 0 || PendingCanVanishBefore(tok.Section))
                    && predecessorCanVanish)
                {
                    RefuseSiblingsAnInitializerCouldReach();
                }

                if (pending.Count > 0)
                {
                    var (kind, name) = DeclarationHeaderGrammar.Classify(pending, HeaderScope(), opensBody: false, Text);
                    if (kind is { } k && Allowed(k, Enclosing(), InAnonymousScope()))
                    {
                        var truncated = DeclarationHeaderGrammar.Truncate(pending, Text);
                        int arrow = truncated.ArrowLine;
                        var declarators =
                            k is DeclarationKind.Field or DeclarationKind.Event && arrow < 0
                                ? DeclarationHeaderGrammar.Declarators(pending, Text)
                                : null;
                        bool hasInitializer = declarators is not null
                            ? declarators[0].HasInitializer
                            : truncated.CutAtEquals && arrow < 0;

                        var sharedDeclaration =
                            EmitBodiless(tok, k, name, arrow, hasInitializer);

                        // A file-scoped namespace has no braces, but it encloses every declaration
                        // below it exactly as a block namespace encloses the ones inside it. Open a
                        // scope that runs to the end of the file so the two spell the same nesting.
                        if (k is DeclarationKind.Namespace)
                        {
                            var ns = rows[^1];
                            ns.EndLine = -1;
                            ns.ClosesAtEndOfFile = true;
                            scopes.Add((rows.Count - 1, true, false, true));

                            // A file-scoped namespace is the one scope opener in C# that uses no
                            // brace, so neither the balance rule nor the opening-depth floor can
                            // see it: a group whose branches declare different file-scoped
                            // namespaces opens and closes at the same depth and is judged
                            // balanced, while the enclosing declaration of everything below the
                            // #endif differs by branch. Its scope also runs to end of file, so no
                            // #endif can repair it. Refuse the rest of the file (adversarial
                            // review round 2, found independently by GPT-5.6 Sol and Gemini 3.1
                            // Pro).
                            if (!ns.SpanKnown && namespaceScopeLostFrom < 0)
                                namespaceScopeLostFrom = rows.Count;
                        }

                        if (declarators is not null)
                            for (int i = 1; i < declarators.Count; i++)
                                EmitAdditionalBodiless(sharedDeclaration, declarators[i]);
                    }
                }
                EndDeclaration(tok);
                lastClosed = -1;
                continue;
            }

            if (text == "," && Enclosing() is { Kind: DeclarationKind.Enum } && pending.Count > 0)
            {
                // The same reaching-back initializer, terminated by the comma that separates this
                // member from the next rather than by the enum's closing brace. See the "}" path.
                if (ReachingBackEquals() >= 0)
                    RefuseSiblingsAnInitializerCouldReach();

                var (_, name) = DeclarationHeaderGrammar.Classify(pending, HeaderScope(), opensBody: false, Text);
                EmitBodiless(
                    pending[^1],
                    DeclarationKind.EnumMember,
                    name,
                    bodyStart: -1,
                    hasInitializer: DeclarationHeaderGrammar.Truncate(pending, Text).CutAtEquals);
                EndDeclaration(tok);
                continue;
            }

            AppendPending(tok, text);
        }

        // A file-scoped namespace declared inside a conditional group scopes the rest of the file
        // to a branch-dependent parent, and nothing below it can be vouched for. This runs before
        // the end-of-file resolution below so that the namespace's own end, which is a maximum
        // over the rows it encloses, is computed from rows already marked unknown.
        if (namespaceScopeLostFrom >= 0)
            unknownRowRanges.Add(new RowRange(namespaceScopeLostFrom, rows.Count));

        foreach (var start in transparentScopeStarts.Values)
        {
            // Unlike a declaration-owning scope, a transparent scope has no open row for the EOF
            // recovery below to invalidate. Record the affected rows now and apply every
            // overlapping range in one final pass rather than rewriting the same suffix once per
            // scope.
            unknownRowRanges.Add(new RowRange(start.FirstRowIndex, rows.Count));
            transparentScopeRows.Add(new TransparentScopeSpan(
                start.StartLine,
                start.BodyStartLine,
                lines.Count));
        }

        ApplyUnknownRanges(rows, unknownRowRanges);

        // A file whose braces never close leaves rows open. Report the end as the last line rather
        // than -1, and mark the span unknown, so a caller cannot mistake a truncated file for a
        // measured span. A file-scoped namespace is the one row that legitimately stays open, and
        // it is resolved separately below.
        foreach (var r in rows)
        {
            if (r.EndLine < 0 && !r.ClosesAtEndOfFile)
            {
                r.EndLine = lines.Count;
                r.SpanKnown = false;
            }
        }

        FinalizeFileScopedNamespaces(rows, depthLost);

        // Depth counts enclosing declarations, not braces. A file-scoped namespace opens no brace,
        // so brace depth would report the same nesting differently depending on which namespace
        // spelling the file uses.
        var depths = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++)
            depths[i] = rows[i].ParentIndex >= 0 ? depths[rows[i].ParentIndex] + 1 : 0;

        transparentScopes = [.. transparentScopeRows
            .OrderBy(scope => scope.StartLine)
            .ThenBy(scope => scope.EndLine)];

        return [.. rows.Select((r, i) => new DeclarationSpan(
            r.Kind, r.Name, r.TriviaStartLine, r.SignatureStartLine, r.SignatureStartColumn,
            r.FirstCodeColumn, r.SignatureEndLine,
            r.BodyStartLine, r.BodyEndLine, r.EndLine, depths[i], r.ParentIndex, r.SpanKnown)
        {
            AttributeLists = r.AttributeLists,
            IsStatic = r.IsStatic,
            HasInitializer = r.HasInitializer,
        })];

        int FirstCodeColumn(int signatureStartLine, int signatureStartColumn) =>
            attributeStarts
                .Where(attribute => attribute.Line == signatureStartLine)
                .Select(attribute => attribute.Column)
                .DefaultIfEmpty(signatureStartColumn)
                .Min();
    }

    private static void ApplyUnknownRanges(List<Row> rows, List<RowRange> ranges)
    {
        if (ranges.Count == 0)
            return;

        var deltas = new int[rows.Count + 1];
        foreach (var range in ranges)
        {
            if (range.Start >= range.EndExclusive)
                continue;
            deltas[range.Start]++;
            deltas[range.EndExclusive]--;
        }

        int active = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            active += deltas[i];
            if (active > 0)
                rows[i].SpanKnown = false;
        }
    }

    private static void FinalizeFileScopedNamespaces(List<Row> rows, bool depthLost)
    {
        bool hasFileScopedNamespace = false;
        foreach (var row in rows)
            hasFileScopedNamespace |= row.ClosesAtEndOfFile;
        if (!hasFileScopedNamespace)
            return;

        // A file-scoped namespace scopes the rest of the file, but its declaration ends where its
        // last member ends rather than at trailing file trivia. Conditional branches can expose
        // several namespace rows to this model even though one build accepts only one. Summarize
        // every raw suffix before mutating any namespace row, preserving the former forward scan's
        // answer while avoiding one complete suffix walk per namespace.
        var suffixEnd = new int[rows.Count + 1];
        var suffixUnknown = new bool[rows.Count + 1];
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            suffixEnd[i] = Math.Max(rows[i].EndLine, suffixEnd[i + 1]);
            suffixUnknown[i] = !rows[i].SpanKnown || suffixUnknown[i + 1];
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i].ClosesAtEndOfFile)
                continue;

            rows[i].EndLine = Math.Max(rows[i].SignatureEndLine, suffixEnd[i + 1]);

            // The end is only as trustworthy as the worst row below it. A row that never closed
            // contributes a guessed EOF end, and losing structural depth anywhere after this
            // namespace opened likewise makes the aggregate end unknown.
            if (depthLost || suffixUnknown[i + 1])
                rows[i].SpanKnown = false;
        }
    }

    /// <summary>
    /// Whether a declaration of <paramref name="kind"/> is recognized in the current scope. Types
    /// and namespaces nest freely; members exist only directly inside a type. Rejecting a member
    /// outside a type is what keeps local functions, lambdas, and statements out of the index
    /// without having to recognize any of them.
    /// </summary>
    private static bool Allowed(DeclarationKind kind, Row? enclosing, bool anonymous) => anonymous ? false : kind switch
    {
        DeclarationKind.Namespace => enclosing is null or { Kind: DeclarationKind.Namespace },
        DeclarationKind.Class or DeclarationKind.Struct or DeclarationKind.Interface
            or DeclarationKind.Record or DeclarationKind.Enum or DeclarationKind.Delegate =>
            enclosing is null or { Kind: DeclarationKind.Namespace } || IsTypeKind(enclosing.Kind),
        _ => enclosing is not null && IsTypeKind(enclosing.Kind),
    };

    private static bool IsTypeKind(DeclarationKind k) => k is DeclarationKind.Class
        or DeclarationKind.Struct or DeclarationKind.Interface or DeclarationKind.Record
        or DeclarationKind.Enum;


}
