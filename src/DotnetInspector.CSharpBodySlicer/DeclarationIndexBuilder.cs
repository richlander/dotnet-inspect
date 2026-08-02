using System.Collections.Immutable;

namespace DotnetInspector.CSharpBodySlicer;

/// <summary>
/// Recovers <see cref="DeclarationSpan"/>s from <see cref="BodySlicer"/>'s token stream in one
/// forward pass.
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
    private sealed class Row
    {
        public DeclarationKind Kind;
        public string Name = "";
        public int TriviaStartLine;
        public int SignatureStartLine;
        public int SignatureEndLine;
        public int BodyStartLine = -1;
        public int EndLine = -1;
        public int ParentIndex = -1;
        public bool SpanKnown = true;
        public bool ClosesAtEndOfFile;
        public ImmutableArray<LineRange> AttributeLists = [];
    }

    // "union" declares a metadata struct (Roslyn reports a StructDeclarationSyntax), so a file that
    // uses it must not lose the type and every member inside it.
    private static readonly HashSet<string> TypeKeywords =
        ["class", "struct", "interface", "record", "enum", "union"];

    // Everything a type declaration may spell BEFORE its keyword, and nothing else. "record" is
    // absent deliberately: "record class C" names the kind in two words, but the scan reaches
    // "record" first and consumes the "class" itself, so no header ever tests "class" with a
    // "record" before it. Adding it back is a dead entry that reads as a rule.
    private static readonly HashSet<string> TypeModifiers =
        [
            "public", "private", "protected", "internal", "file", "static", "abstract", "sealed",
            "partial", "readonly", "ref", "unsafe", "new",
        ];

    public static ImmutableArray<DeclarationSpan> Build(IReadOnlyList<string> lines)
    {
        var tokens = BodySlicer.ScanTokens(lines);
        var rows = new List<Row>();
        bool depthLost = false;

        // -1 marks an anonymous scope: a method body, a lambda, a property's accessor block, a
        // collection initializer. Members are only recognized inside a type, so an anonymous
        // scope is exactly what stops a local function from being indexed as a member.
        var scopes = new List<int>();
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
        int lastClosed = -1;
        bool inAttribute = false;
        int attributeDepth = 0;
        int attributeStart = 0;
        int attributeWords = 0;
        bool unitTarget = false;
        bool unitAttribute = false;
        var attributeLists = new List<LineRange>();
        int initializerDepth = 0;
        int lastTerminatorLine = 0;
        int namespaceScopeLostFrom = -1;
        bool inBlockComment = false;
        int commentOpenLine = 0;

        string Text(ScanToken t) => t.TextIn(lines[t.Line]).ToString();
        Row? Enclosing() => scopes.Count > 0 && scopes[^1] >= 0 ? rows[scopes[^1]] : null;
        // A type at file scope and a statement inside a method body both report no enclosing row.
        // Only the first may declare anything: "@namespace = x;" in a method body is an
        // assignment, and reading it as a namespace is what an unqualified null check does.
        bool InAnonymousScope() => scopes.Count > 0 && scopes[^1] < 0;
        int EnclosingIndex() => scopes.Count > 0 ? scopes[^1] : -1;

        void ResetHeader(bool atKnownPoint = true)
        {
            pending.Clear();

            // Discarding recorded trivia at a point only one build reaches makes the NEXT row's
            // trivia start branch-dependent. In
            //
            //     #if X
            //     // X docs
            //     #else
            //     using System;
            //     #endif
            //     class C { }
            //
            // the "using" terminator belongs to one branch and the comment to the other, so with X
            // the comment is C's documentation and without it C has none. Resetting unconditionally
            // forgot the comment AND restored knownness, and C was vouched for with the second
            // build's answer (adversarial review round 5, GPT-5.6 Sol).
            triviaKnown = atKnownPoint || triviaStart < 0;
            triviaStart = -1;
            attributeLists.Clear();
        }

        void EndDeclaration(ScanToken terminator)
        {
            ResetHeader(terminator.DepthKnown);
            lastTerminatorLine = terminator.Line + 1;
        }

        // Emits a declaration that has no scope of its own: a field, an enum member, an abstract
        // or interface member, an extern member, a positional record, an expression-bodied member,
        // or a file-scoped namespace.
        void EmitBodiless(ScanToken terminator, DeclarationKind kind, string name, int bodyStart)
        {
            int sigStart = pending.Count > 0 ? pending[0].Line + 1 : terminator.Line + 1;
            rows.Add(new Row
            {
                Kind = kind,
                Name = name,
                TriviaStartLine = triviaStart >= 0 ? triviaStart : sigStart,
                SignatureStartLine = sigStart,
                SignatureEndLine = terminator.Line + 1,
                BodyStartLine = bodyStart,
                EndLine = terminator.Line + 1,
                ParentIndex = EnclosingIndex(),
                AttributeLists = [.. attributeLists],
                SpanKnown = terminator.DepthKnown && triviaKnown && pending.All(t => t.DepthKnown),
            });
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
                        triviaKnown = attributeKnown;
                        lastTerminatorLine = tok.Line + 1;
                    }
                    else
                    {
                        attributeLists.Add(list);

                        // Every list, not just the one that opened the trivia. A list written
                        // inside a conditional group is reported in AttributeLists even though
                        // only one build compiles it, and unlike a trivia comment it is not merely
                        // a line inside the row's range -- it is a claim about what is applied to
                        // the declaration. A row whose lists depend on the build is not vouched
                        // for (adversarial review round 3, Gemini 3.1 Pro).
                        triviaKnown &= attributeKnown;
                        if (triviaStart < 0)
                            triviaStart = attributeStart;
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
                // "= new(...) { ... }" is an initializer, not a member body. The braces belong to
                // the value, so the declaration is still running: keep the header and let the
                // terminating ";" close it, which is what puts the whole initializer inside the
                // field's span and stops the initializer from reading as a property's accessors.
                if (Truncate(pending, Text).CutAtEquals)
                {
                    initializerDepth++;
                    scopes.Add(-1);
                    pending.Add(tok);
                    continue;
                }

                // A C# 14 extension block is a scope but not a declaration: its members are emitted
                // onto the enclosing static class, so the index makes it transparent and lets them
                // land there too. Giving it a row of its own would put every extension member
                // inside a parent that has no metadata counterpart.
                if (DeclaresAnExtensionBlock(pending, Text))
                {
                    scopes.Add(EnclosingIndex());
                    EndDeclaration(tok);
                    lastClosed = -1;
                    continue;
                }

                var (kind, name) = Classify(pending, Enclosing(), opensBody: true, Text);
                if (kind is { } k && Allowed(k, Enclosing(), InAnonymousScope()))
                {
                    int sigStart = pending.Count > 0 ? pending[0].Line + 1 : tok.Line + 1;
                    rows.Add(new Row
                    {
                        Kind = k,
                        Name = name,
                        TriviaStartLine = triviaStart >= 0 ? triviaStart : sigStart,
                        SignatureStartLine = sigStart,
                        SignatureEndLine = tok.Line + 1,
                        BodyStartLine = tok.Line + 1,
                        ParentIndex = EnclosingIndex(),
                        AttributeLists = [.. attributeLists],
                        SpanKnown = tok.DepthKnown && triviaKnown && pending.All(t => t.DepthKnown),
                    });
                    scopes.Add(rows.Count - 1);
                }
                else
                {
                    scopes.Add(-1);
                }
                EndDeclaration(tok);
                lastClosed = -1;
                continue;
            }

            if (text == "}")
            {
                if (initializerDepth > 0)
                {
                    initializerDepth--;
                    if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1);
                    pending.Add(tok);
                    continue;
                }

                // An enum's last member needs no trailing comma, so the closing brace terminates it.
                if (Enclosing() is { Kind: DeclarationKind.Enum } && pending.Count > 0)
                {
                    var (ek, en) = Classify(pending, Enclosing(), opensBody: false, Text);
                    if (ek is not null)
                        EmitBodiless(pending[^1], DeclarationKind.EnumMember, en, bodyStart: -1);
                    EndDeclaration(tok);
                }

                if (scopes.Count > 0)
                {
                    int idx = scopes[^1];
                    scopes.RemoveAt(scopes.Count - 1);
                    if (idx >= 0)
                    {
                        rows[idx].EndLine = tok.Line + 1;
                        if (!tok.DepthKnown) rows[idx].SpanKnown = false;
                        lastClosed = idx;
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
                if (initializerDepth > 0)
                {
                    pending.Add(tok);
                    continue;
                }

                // "public List<T>? Edges { get; } = edges;" — the initializer belongs to the
                // property whose accessor block just closed, not to a new declaration.
                if (lastClosed >= 0 && pending.Count > 0
                    && pending[0].Kind == ScanTokenKind.Punctuator && Text(pending[0]) == "=")
                {
                    rows[lastClosed].EndLine = tok.Line + 1;

                    // This extends a span that was already measured and marked known when its
                    // accessor block closed, so it needs the same correction that close took: a
                    // conditional between the block and the initializer puts the ";" in a branch,
                    // and the end this reads is one branch's, not the declaration's.
                    if (!tok.DepthKnown) rows[lastClosed].SpanKnown = false;
                    ResetHeader(tok.DepthKnown);
                    lastClosed = -1;
                    continue;
                }

                if (pending.Count > 0)
                {
                    var (kind, name) = Classify(pending, Enclosing(), opensBody: false, Text);
                    if (kind is { } k && Allowed(k, Enclosing(), InAnonymousScope()))
                    {
                        int arrow = Truncate(pending, Text).ArrowLine;

                        // "public int A, B, C;" declares three fields. Metadata sees three, so the
                        // index owes a row apiece — a field is declaration-only, which is exactly
                        // the case a name lookup has to serve. They share one span because they
                        // share one declaration.
                        var extra = k is DeclarationKind.Field or DeclarationKind.Event && arrow < 0
                            ? ExtraDeclaratorNames(pending, Text)
                            : null;

                        EmitBodiless(tok, k, name, arrow);

                        // A file-scoped namespace has no braces, but it encloses every declaration
                        // below it exactly as a block namespace encloses the ones inside it. Open a
                        // scope that runs to the end of the file so the two spell the same nesting.
                        if (k is DeclarationKind.Namespace)
                        {
                            var ns = rows[^1];
                            ns.EndLine = -1;
                            ns.ClosesAtEndOfFile = true;
                            scopes.Add(rows.Count - 1);

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

                        if (extra is not null)
                            foreach (var more in extra)
                                EmitBodiless(tok, k, more, bodyStart: -1);
                    }
                }
                EndDeclaration(tok);
                lastClosed = -1;
                continue;
            }

            if (text == "," && Enclosing() is { Kind: DeclarationKind.Enum } && pending.Count > 0)
            {
                var (_, name) = Classify(pending, Enclosing(), opensBody: false, Text);
                EmitBodiless(pending[^1], DeclarationKind.EnumMember, name, bodyStart: -1);
                EndDeclaration(tok);
                continue;
            }

            pending.Add(tok);
        }

        // A file-scoped namespace declared inside a conditional group scopes the rest of the file
        // to a branch-dependent parent, and nothing below it can be vouched for. This runs before
        // the end-of-file resolution below so that the namespace's own end, which is a maximum
        // over the rows it encloses, is computed from rows already marked unknown.
        if (namespaceScopeLostFrom >= 0)
            for (int i = namespaceScopeLostFrom; i < rows.Count; i++)
                rows[i].SpanKnown = false;

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

        // A file-scoped namespace *scopes* the rest of the file, but its declaration ends where its
        // last member ends, not at the last physical line: a trailing comment belongs to the file,
        // not to the namespace. Ending at EOF would put trailing trivia inside the declaration and
        // disagree with the span of every other row.
        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i].ClosesAtEndOfFile)
                continue;

            // Everything after a file-scoped namespace is inside it. A file cannot open two in any
            // one build, but the branches of a conditional group can each open one, and this scan
            // keeps every branch's rows -- so more than one row here can close at end of file, and
            // each takes a maximum over the rows below it. That over-wide end is not vouched for:
            // such a namespace is never SpanKnown, and the refusal above has already unknowed
            // every row below the first of them.
            int end = rows[i].SignatureEndLine;
            bool guessed = depthLost;
            for (int j = i + 1; j < rows.Count; j++)
            {
                end = Math.Max(end, rows[j].EndLine);
                guessed |= !rows[j].SpanKnown;
            }
            rows[i].EndLine = end;

            // The end is a maximum over the rows this namespace encloses, so it is only as good as
            // the worst of them: a row that never closed reports the last line as a guess, and
            // adopting that guess as a measured namespace end would claim a span the scan never
            // saw. The same holds if the scan lost its place anywhere after this row opened.
            if (guessed)
                rows[i].SpanKnown = false;
        }

        // Depth counts enclosing declarations, not braces. A file-scoped namespace opens no brace,
        // so brace depth would report the same nesting differently depending on which namespace
        // spelling the file uses.
        var depths = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++)
            depths[i] = rows[i].ParentIndex >= 0 ? depths[rows[i].ParentIndex] + 1 : 0;

        return [.. rows.Select((r, i) => new DeclarationSpan(
            r.Kind, r.Name, r.TriviaStartLine, r.SignatureStartLine, r.SignatureEndLine,
            r.BodyStartLine, r.EndLine, depths[i], r.ParentIndex, r.SpanKnown)
        {
            AttributeLists = r.AttributeLists,
        })];
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

    /// <summary>
    /// Drops everything from the first top-level <c>=</c> or <c>where</c> onward, and reports
    /// whether that <c>=</c> was the arrow of an expression body.
    /// <para>
    /// The distinction is the only thing separating a member's body from a field's initializer,
    /// because both are cut at the same token: <c>int P =&gt; 1;</c> and
    /// <c>Func&lt;int,int&gt; F = x =&gt; x;</c> each truncate at a top-level <c>=</c>, and each has
    /// an arrow somewhere to the right of it. Only the first has one *at* the cut. Searching the
    /// header for any arrow instead would give the field a body it does not have, and would leave
    /// the expression-bodied property looking like a field.
    /// </para>
    /// </summary>
    /// <param name="ArrowLine">
    /// 1-based line of the <c>=&gt;</c> that opens an expression body, or -1 when the header was
    /// not cut at one.
    /// </param>

    /// <summary>
    /// Whether the token at <paramref name="i"/> is the C# keyword <paramref name="keyword"/> and
    /// not an identifier that merely spells it. <c>@class</c>, <c>@delegate</c>, <c>@where</c>, and
    /// <c>@this</c> are ordinary names, and the scanner reports the <c>@</c> as its own token, so a
    /// keyword test that reads only the word treats a field named <c>@class</c> as a type
    /// declaration.
    /// </summary>
    private static bool IsKeyword(
        IReadOnlyList<ScanToken> header, int i, string keyword, Func<ScanToken, string> text) =>
        header[i].Kind == ScanTokenKind.Word
        && text(header[i]) == keyword
        && !IsVerbatim(header, i, text);

    private static bool IsVerbatim(IReadOnlyList<ScanToken> header, int i, Func<ScanToken, string> text) =>
        i > 0 && header[i - 1].Kind == ScanTokenKind.Punctuator && text(header[i - 1]) == "@";

    private readonly record struct TruncatedHeader(List<ScanToken> Header, int ArrowLine, bool CutAtEquals);

    private static TruncatedHeader Truncate(List<ScanToken> pending, Func<ScanToken, string> text)
    {
        int cut = pending.Count;
        int depth = 0;
        for (int i = 0; i < pending.Count; i++)
        {
            var t = pending[i];
            if (t.Kind == ScanTokenKind.Punctuator)
            {
                var c = text(t);
                if (c is "(" or "[" or "{") depth++;
                else if (c is ")" or "]" or "}") depth--;

                // "public C(int x) : this(x, 0)" — a constructor initializer is a second
                // parenthesized group after the parameter list, and it is the one that ends the
                // header. Without this cut the initializer looks like the parameter list and the
                // constructor is named "this" or "base".
                else if (c == ":" && depth == 0 && i + 1 < pending.Count
                    && (IsKeyword(pending, i + 1, "this", text) || IsKeyword(pending, i + 1, "base", text)))
                {
                    cut = i;
                    break;
                }
            }
            else if (depth == 0 && IsKeyword(pending, i, "where", text))
            {
                cut = i;
                break;
            }
        }

        // The arrow scan deliberately runs past a "where" clause. A generic constraint carries no
        // top-level "=", so the first one after it is still the header's own — and an
        // expression-bodied generic method spells its constraints before its arrow.
        int arrowLine = -1;
        bool cutAtEquals = false;
        depth = 0;
        bool inOperatorSymbol = false;
        for (int i = 0; i < pending.Count; i++)
        {
            var t = pending[i];
            if (t.Kind != ScanTokenKind.Punctuator)
            {
                // An operator spells its name in punctuation, and "==", "!=", "<=", ">=" and the
                // compound "+=" family contain an "=" that is part of that NAME, not an
                // assignment. Cutting there discards the parameter list, and a header with no
                // parameter list is not recognized as an operator at all: it becomes a field named
                // "operator", and for a block-bodied one the cut also makes the body look like an
                // initializer, which swallows the members that follow.
                if (depth == 0 && IsKeyword(pending, i, "operator", text))
                    inOperatorSymbol = true;
                continue;
            }

            var c = text(t);
            if (c is "(" or "[" or "{")
            {
                depth++;
                // The symbol ends where the parameter list opens, so an expression-bodied
                // operator's "=>" is still found.
                inOperatorSymbol = false;
            }
            else if (c is ")" or "]" or "}") depth--;
            else if (c == "=" && depth == 0 && !inOperatorSymbol)
            {
                bool arrow = i + 1 < pending.Count
                    && pending[i + 1].Kind == ScanTokenKind.Punctuator
                    && text(pending[i + 1]) == ">"
                    && pending[i + 1].Line == t.Line
                    && pending[i + 1].Column == t.Column + 1;
                if (arrow) arrowLine = t.Line + 1;
                cutAtEquals = true;
                if (i < cut) cut = i;
                break;
            }
        }

        return new TruncatedHeader(
            cut >= pending.Count ? pending : pending.GetRange(0, cut), arrowLine, cutAtEquals);
    }

    /// <summary>
    /// Recognizes what a header declares, if anything.
    /// <para>
    /// The header is first truncated at its first top-level <c>=</c> or <c>where</c>, which
    /// removes initializers, expression bodies, and generic constraints. After that truncation a
    /// method's parameter list is exactly the parenthesized group that ends the header, which is
    /// what distinguishes it from a tuple return type, a constraint's <c>new()</c>, or a call in
    /// an initializer — all of which are parenthesized groups that do not end it.
    /// </para>
    /// </summary>
    private static (DeclarationKind? Kind, string Name) Classify(
        List<ScanToken> pending, Row? enclosing, bool opensBody, Func<ScanToken, string> text)
    {
        if (pending.Count == 0)
            return (null, "");

        var truncated = Truncate(pending, text);
        var header = truncated.Header;
        if (header.Count == 0)
            return (null, "");

        // Word positions within the header, so a keyword test can see whether an "@" precedes the
        // word. "@class" is a name, not a type declaration.
        var at = new List<int>();
        for (int i = 0; i < header.Count; i++)
            if (header[i].Kind == ScanTokenKind.Word)
                at.Add(i);
        if (at.Count == 0)
            return (null, "");

        var words = at.Select(i => text(header[i])).ToList();
        bool Keyword(int w, string kw) => IsKeyword(header, at[w], kw, text);

        // A using directive and an extern alias are not declarations. Classify answers that here so
        // that it is locally right about them; the property is enforced independently by Allowed,
        // which rejects any member kind whose enclosing scope is not a type, and a file cannot put
        // either construct inside one. Removing these two lines therefore changes no output today
        // -- they keep Classify from returning a wrong intermediate answer, not the result.
        if (Keyword(0, "using"))
            return (null, "");
        if (Keyword(0, "extern") && words.Count > 1 && Keyword(1, "alias"))
            return (null, "");

        if (Keyword(0, "namespace"))
            return (DeclarationKind.Namespace, string.Join(".", words.Skip(1)));

        // An enum member is a bare name, possibly with an explicit value already truncated away.
        if (enclosing is { Kind: DeclarationKind.Enum })
            return (DeclarationKind.EnumMember, words[^1]);

        for (int i = 0; i < words.Count; i++)
        {
            if (!TypeKeywords.Contains(words[i]) || IsVerbatim(header, at[i], text))
                continue;
            // "record class C" and "record struct C" name the kind in two words.
            int nameAt = i + 1;
            if (words[i] == "record" && nameAt < words.Count
                && (Keyword(nameAt, "class") || Keyword(nameAt, "struct")))
                nameAt++;

            // Everything a type declaration spells before its keyword is a modifier. That is the
            // discriminator, and it is needed because "record" and "union" are contextual
            // keywords: "int record, union;" and "M(int record, int x)" are not type declarations,
            // and reading either as one hallucinates a type, loses the real members, and -- when
            // the fabricated type adopts a method's "{" as its body -- swallows the statements
            // inside it as fields.
            if (!Enumerable.Range(0, i).All(w =>
                    TypeModifiers.Contains(words[w]) && !IsVerbatim(header, at[w], text)))
                continue;

            // A bounds guard for the indexing below, not a second discriminator: a modifier-only
            // header such as "public record" does not compile, and no input tried reaches Classify
            // with one, because a header is only classified at a "{" or ";". UNVERIFIED and
            // ungated -- kept because deleting it would leave words[nameAt] unguarded, which is a
            // worse failure than an unreachable branch.
            if (nameAt >= words.Count)
                continue;

            var kind = words[i] switch
            {
                "class" => DeclarationKind.Class,
                "struct" or "union" => DeclarationKind.Struct,
                "interface" => DeclarationKind.Interface,
                "record" => DeclarationKind.Record,
                _ => DeclarationKind.Enum,
            };
            return (kind, words[nameAt]);
        }

        int paren = ParameterListStart(header, text);
        if (paren >= 0)
        {
            var name = NameBefore(header, paren, text);
            if (DeclaresADelegate(header, text))
                return (DeclarationKind.Delegate, name);
            if (Enumerable.Range(0, words.Count).Any(w => Keyword(w, "operator")))
                return (DeclarationKind.Method, OperatorName(header, paren, text));
            if (name.StartsWith('~'))
                return (DeclarationKind.Destructor, name);
            if (enclosing is not null && name == enclosing.Name && name.Length > 0)
                return (DeclarationKind.Constructor, name);
            return (DeclarationKind.Method, name);
        }

        // An indexer has a bracketed parameter list rather than a parenthesized one.
        int thisAt = at.FirstOrDefault(i => IsKeyword(header, i, "this", text), -1);
        if (thisAt >= 0 && thisAt + 1 < header.Count && text(header[thisAt + 1]) == "[")
            return (DeclarationKind.Property, "this");

        if (Enumerable.Range(0, words.Count).Any(w => Keyword(w, "event")))
            return (DeclarationKind.Event, DeclaratorNames(pending, text)[0]);

        // With no parameter list, a body means a property. An expression body counts: "int P => 1;"
        // is a property, while "Func<int,int> F = x => x;" is a field whose value happens to be a
        // lambda, and the two are told apart by whether the header was cut at the arrow.
        return opensBody || truncated.ArrowLine >= 0
            ? (DeclarationKind.Property, words[^1])
            : (DeclarationKind.Field, DeclaratorNames(pending, text)[0]);
    }

    /// <summary>
    /// The declared names of a field or field-like event declaration, in source order — one per
    /// declarator, so <c>public int A, B, C;</c> yields three. Always at least one entry.
    /// </summary>
    /// <remarks>
    /// A comma is a declarator boundary only at parenthesis, bracket, brace, and angle depth zero,
    /// and only when the token after it is a name and the token after <em>that</em> ends the
    /// declarator — a comma, an <c>=</c>, or the end.
    /// <para>
    /// Neither test suffices alone, and the two halves of a declarator need different treatment.
    /// </para>
    /// <para>
    /// <em>Before</em> the segment's <c>=</c> a <c>&lt;</c> is always a generic bracket, so angle
    /// depth is simply counted. The scanner emits one-character punctuators, so the <c>&gt;&gt;</c>
    /// closing two nested lists arrives as two tokens and needs no special handling. The lookahead alone is not enough there: it admits
    /// <c>Action&lt;string, int, float&gt; A</c>, where the comma after <c>string</c> is followed
    /// by a name and another comma — exactly a declarator list's shape. Two type arguments happen
    /// to survive the lookahead; three do not.
    /// </para>
    /// <para>
    /// <em>After</em> the <c>=</c> counting is unsound, because a relational <c>&lt;</c> never
    /// closes and would swallow every later comma. Instead a <c>&lt;</c> that follows a name is
    /// matched speculatively by <see cref="TypeArgumentListEnd"/>, and the matched region is
    /// skipped. What separates the two readings is that the region must be type-shaped all the way
    /// to its <c>&gt;</c>: <c>new Action&lt;int, int, int&gt;()</c> is, and is skipped, while
    /// <c>a &lt; b, y = c &gt; d</c> contains an <c>=</c>, which no type argument list does, so it
    /// is left alone and its comma still separates declarators.
    /// </para>
    /// <para>
    /// A stricter reading — also requiring that the closing <c>&gt;</c> not be followed by an
    /// identifier — was tried and removed: for the extra condition to change any outcome the
    /// region would have to contain a comma the lookahead accepts, which needs the shape
    /// <c>&lt;a, b, c&gt; name</c>, and the compiler rejects that as a syntax error. It was
    /// unreachable, and unreachable code that looks load-bearing is worse than none.
    /// </para>
    /// </remarks>
    private static List<string> DeclaratorNames(List<ScanToken> pending, Func<ScanToken, string> text)
    {
        var names = new List<string>();
        int start = 0;
        int depth = 0;
        int angle = 0;
        bool sawEquals = false;

        for (int i = 0; i <= pending.Count; i++)
        {
            bool end = i == pending.Count;
            if (!end)
            {
                var t = pending[i];
                if (t.Kind == ScanTokenKind.Punctuator)
                {
                    var c = text(t);
                    if (c is "(" or "[" or "{") depth++;
                    else if (c is ")" or "]" or "}") depth--;
                    else if (c == "=" && depth == 0) sawEquals = true;
                    else if (!sawEquals && depth == 0 && c is "<" or ">")
                    {
                        angle += c == "<" ? 1 : -1;
                        if (angle < 0) angle = 0;
                    }
                    else if (sawEquals && depth == 0 && c == "<"
                        && i > 0 && pending[i - 1].Kind == ScanTokenKind.Word)
                    {
                        int close = TypeArgumentListEnd(pending, i, text);
                        if (close >= 0)
                        {
                            i = close;
                            continue;
                        }
                    }

                    if (c == "," && depth == 0 && angle == 0 && IsDeclaratorBoundary(pending, i, text))
                        end = true;
                }
            }

            if (!end)
                continue;

            var name = LastNameBeforeAssignment(pending, start, i, text);
            if (name.Length > 0)
                names.Add(name);
            start = i + 1;
            angle = 0;
            sawEquals = false;
        }

        if (names.Count == 0)
            names.Add("");
        return names;
    }

    /// <summary>
    /// The index of the <c>&gt;</c> closing a type argument list that starts at
    /// <paramref name="start"/>, or <c>-1</c> if the tokens from there are not shaped like one.
    /// Only tokens that can appear inside a type argument list are accepted, which is what makes a
    /// relational <c>&lt;</c> distinguishable: <c>x = a &lt; b, y = c &gt; d</c> contains an
    /// <c>=</c>, and no type argument list does.
    /// </summary>
    /// <remarks>
    /// The closing <c>&gt;</c> must also leave every <c>(</c> and <c>[</c> opened inside the region
    /// closed. A type argument list balances its groups — <c>Func&lt;(int a, int[] b), string&gt;</c>
    /// does — but <c>a &lt; b(name: c &gt; d)</c> does not, and accepting it would hand the caller a
    /// region that swallows the <c>(</c> and leaves the matching <c>)</c> behind to drive the
    /// caller's group depth negative, after which no later comma can ever separate a declarator.
    /// </remarks>
    private static int TypeArgumentListEnd(List<ScanToken> pending, int start, Func<ScanToken, string> text)
    {
        int angle = 0;
        int group = 0;
        for (int i = start; i < pending.Count; i++)
        {
            var t = pending[i];
            if (t.Kind == ScanTokenKind.Word)
                continue;
            if (t.Kind != ScanTokenKind.Punctuator)
                return -1;

            var c = text(t);
            if (c == "<")
            {
                angle++;
                continue;
            }

            if (c == ">")
            {
                angle--;
                if (angle <= 0)
                    return angle == 0 && group == 0 ? i : -1;
                continue;
            }

            // Tuple elements, arrays, pointers, nullability, and qualified names are all type
            // syntax. Anything else -- an operator, a literal, a brace -- is not. ":" is here for
            // the two halves of "global::"; the scanner emits one-character punctuators, so a
            // "::" token never arrives.
            if (c is "(" or "[")
            {
                group++;
                continue;
            }

            if (c is ")" or "]")
            {
                group--;
                continue;
            }

            if (c is "," or "." or "?" or "*" or ":")
                continue;
            return -1;
        }

        return -1;
    }

    private static bool IsDeclaratorBoundary(List<ScanToken> pending, int comma, Func<ScanToken, string> text)
    {
        if (comma + 1 >= pending.Count || pending[comma + 1].Kind != ScanTokenKind.Word)
            return false;
        if (comma + 2 >= pending.Count)
            return true;
        var after = pending[comma + 2];
        return after.Kind == ScanTokenKind.Punctuator && text(after) is "," or "=";
    }

    private static string LastNameBeforeAssignment(
        List<ScanToken> pending, int start, int endExclusive, Func<ScanToken, string> text)
    {
        int depth = 0;
        string name = "";
        for (int i = start; i < endExclusive; i++)
        {
            var t = pending[i];
            if (t.Kind == ScanTokenKind.Punctuator)
            {
                var c = text(t);
                if (c is "(" or "[" or "{") depth++;
                else if (c is ")" or "]" or "}") depth--;
                else if (c == "=" && depth == 0) break;
            }
            else if (t.Kind == ScanTokenKind.Word && depth == 0)
            {
                name = text(t);
            }
        }
        return name;
    }

    /// <summary>
    /// The declarator names after the first, or <see langword="null"/> when the declaration
    /// declares a single name.
    /// </summary>
    private static List<string>? ExtraDeclaratorNames(List<ScanToken> pending, Func<ScanToken, string> text)
    {
        var names = DeclaratorNames(pending, text);
        return names.Count > 1 ? names.GetRange(1, names.Count - 1) : null;
    }

    /// <summary>
    /// Index of the <c>(</c> opening the parameter list, or -1. The list is the parenthesized
    /// group whose <c>)</c> is the header's final token; any earlier group belongs to a tuple type,
    /// an attribute argument, or a cast.
    /// </summary>
    private static int ParameterListStart(List<ScanToken> header, Func<ScanToken, string> text)
    {
        var last = header[^1];
        if (last.Kind != ScanTokenKind.Punctuator || text(last) != ")")
            return -1;

        int depth = 0;
        for (int i = header.Count - 1; i >= 0; i--)
        {
            if (header[i].Kind != ScanTokenKind.Punctuator) continue;
            var c = text(header[i]);
            if (c == ")") depth++;
            else if (c == "(" && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// The declared name sitting before <paramref name="parenIndex"/>, stepping over a type
    /// parameter list so that <c>Foo&lt;T&gt;(</c> is named <c>Foo</c> and not <c>T</c>. A leading
    /// <c>~</c> is kept, because it is what identifies a destructor.
    /// </summary>
    private static string NameBefore(List<ScanToken> header, int parenIndex, Func<ScanToken, string> text)
    {
        int i = parenIndex - 1;
        if (i >= 0 && header[i].Kind == ScanTokenKind.Punctuator && text(header[i]) == ">")
        {
            int angle = 0;
            for (; i >= 0; i--)
            {
                if (header[i].Kind != ScanTokenKind.Punctuator) continue;
                var c = text(header[i]);
                if (c == ">") angle++;
                else if (c == "<" && --angle == 0) { i--; break; }
            }
        }
        if (i < 0)
            return "";
        if (header[i].Kind != ScanTokenKind.Word)
            return text(header[i]);

        var name = text(header[i]);
        if (i > 0 && header[i - 1].Kind == ScanTokenKind.Punctuator && text(header[i - 1]) == "~")
            return "~" + name;
        return name;
    }

    /// <summary>
    /// The name of an operator declaration. A conversion is named for its direction rather than
    /// for its target type, so <c>implicit operator string</c> and <c>implicit operator int</c> do
    /// not both answer to "string". A symbolic operator keeps its symbol, which is not a word.
    /// </summary>
    private static string OperatorName(List<ScanToken> header, int parenIndex, Func<ScanToken, string> text)
    {
        int at = header.FindIndex(t => t.Kind == ScanTokenKind.Word && text(t) == "operator");
        if (at < 0)
            return "operator";

        // "checked" always follows the "operator" keyword, in both the symbolic and the conversion
        // form. It is kept in the name because it names a different member: "operator checked +"
        // emits op_CheckedAddition and can be declared alongside op_Addition in the same type.
        // Dropping it would make two distinct declarations share a name for no gain.
        bool isChecked = at + 1 < header.Count && IsKeyword(header, at + 1, "checked", text);
        var prefix = isChecked ? "operator checked " : "operator ";

        if (at > 0 && header[at - 1].Kind == ScanTokenKind.Word && text(header[at - 1]) is "implicit" or "explicit")
            return prefix + text(header[at - 1]);

        // Everything between "operator" and the parameter list is the operator's spelling: one
        // token for "+", two for ">>".
        int from = isChecked ? at + 2 : at + 1;
        var symbol = string.Concat(header.Skip(from).Take(parenIndex - from).Select(text));
        return symbol.Length > 0 ? prefix + symbol : "operator";
    }

    /// <summary>
    /// Whether the header declares a delegate type. A function pointer spells the same keyword —
    /// <c>delegate*&lt;int, int&gt;</c> — as a return or parameter type, and the <c>*</c> is what
    /// tells them apart.
    /// </summary>
    /// <summary>
    /// Whether the header declares a C# 14 <c>extension</c> block. The block may be generic, and
    /// its type parameter list sits between the keyword and the receiver: <c>extension&lt;T&gt;(
    /// IEnumerable&lt;T&gt; source)</c>. Testing only for a following <c>(</c> misses that form,
    /// and the cost is not one bad row -- the block is then indexed as a method, and every member
    /// inside it is rejected for sitting in a method rather than a type, so the extension members
    /// disappear from the index entirely.
    /// <para>
    /// The keyword is always the header's first token. An accessibility modifier is CS0106 and an
    /// attribute is CS7014, both measured against the compiler, so there is nothing for it to
    /// follow.
    /// </para>
    /// </summary>
    private static bool DeclaresAnExtensionBlock(List<ScanToken> pending, Func<ScanToken, string> text)
    {
        if (pending.Count < 2 || !IsKeyword(pending, 0, "extension", text))
            return false;
        if (text(pending[1]) == "(")
            return true;
        if (text(pending[1]) != "<")
            return false;

        int angle = 0;
        for (int i = 1; i < pending.Count; i++)
        {
            if (pending[i].Kind != ScanTokenKind.Punctuator)
                continue;
            var c = text(pending[i]);
            if (c is not ("<" or ">"))
                continue;
            angle += c == "<" ? 1 : -1;
            if (angle == 0)
                return i + 1 < pending.Count && text(pending[i + 1]) == "(";
        }

        return false;
    }

    private static bool DeclaresADelegate(List<ScanToken> header, Func<ScanToken, string> text)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (!IsKeyword(header, i, "delegate", text))
                continue;
            if (i + 1 >= header.Count || text(header[i + 1]) != "*")
                return true;
        }
        return false;
    }
}
