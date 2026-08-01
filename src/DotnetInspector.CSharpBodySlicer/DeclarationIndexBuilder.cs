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
    }

    private static readonly HashSet<string> TypeKeywords =
        ["class", "struct", "interface", "record", "enum"];

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
        int lastClosed = -1;
        bool inAttribute = false;
        int attributeDepth = 0;
        int initializerDepth = 0;
        int lastTerminatorLine = 0;

        string Text(ScanToken t) => t.TextIn(lines[t.Line]).ToString();
        Row? Enclosing() => scopes.Count > 0 && scopes[^1] >= 0 ? rows[scopes[^1]] : null;
        // A type at file scope and a statement inside a method body both report no enclosing row.
        // Only the first may declare anything: "@namespace = x;" in a method body is an
        // assignment, and reading it as a namespace is what an unqualified null check does.
        bool InAnonymousScope() => scopes.Count > 0 && scopes[^1] < 0;
        int EnclosingIndex() => scopes.Count > 0 ? scopes[^1] : -1;

        void ResetHeader()
        {
            pending.Clear();
            triviaStart = -1;
        }

        void EndDeclaration(ScanToken terminator)
        {
            ResetHeader();
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
                SpanKnown = terminator.DepthKnown && pending.All(t => t.DepthKnown),
            });
        }

        foreach (var tok in tokens)
        {
            if (!tok.DepthKnown)
                depthLost = true;

            if (tok.Kind == ScanTokenKind.Directive)
                continue;

            if (tok.Kind == ScanTokenKind.Comment)
            {
                // Trivia only counts while a header has not started. A comment sitting inside a
                // signature is not the declaration's leading documentation, and one sitting on the
                // line that ended the previous declaration trails that declaration rather than
                // opening this one.
                if (pending.Count == 0 && !inAttribute && triviaStart < 0 && tok.Line + 1 > lastTerminatorLine)
                    triviaStart = tok.Line + 1;
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
                if (text == "[") attributeDepth++;
                else if (text == "]" && --attributeDepth == 0) inAttribute = false;
                continue;
            }
            if (pending.Count == 0 && text == "[")
            {
                inAttribute = true;
                attributeDepth = 1;
                if (triviaStart < 0) triviaStart = tok.Line + 1;
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
                        SpanKnown = tok.DepthKnown && pending.All(t => t.DepthKnown),
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
                    ResetHeader();
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

            // Everything after a file-scoped namespace is inside it -- a file cannot open a second
            // one, and nothing can precede it but usings and attributes, which are not rows.
            int end = rows[i].SignatureEndLine;
            for (int j = i + 1; j < rows.Count; j++)
                end = Math.Max(end, rows[j].EndLine);
            rows[i].EndLine = end;

            // The span reaches every later declaration, so if the scan lost its place anywhere
            // after this row opened, the end it reports is a guess. Report it unknown instead.
            if (depthLost)
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
            r.BodyStartLine, r.EndLine, depths[i], r.ParentIndex, r.SpanKnown))];
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
        for (int i = 0; i < pending.Count; i++)
        {
            var t = pending[i];
            if (t.Kind != ScanTokenKind.Punctuator) continue;
            var c = text(t);
            if (c is "(" or "[" or "{") depth++;
            else if (c is ")" or "]" or "}") depth--;
            else if (c == "=" && depth == 0)
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
            var kind = words[i] switch
            {
                "class" => DeclarationKind.Class,
                "struct" => DeclarationKind.Struct,
                "interface" => DeclarationKind.Interface,
                "record" => DeclarationKind.Record,
                _ => DeclarationKind.Enum,
            };
            return (kind, nameAt < words.Count ? words[nameAt] : "");
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
    private static int TypeArgumentListEnd(List<ScanToken> pending, int start, Func<ScanToken, string> text)
    {
        int angle = 0;
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
                    return angle == 0 ? i : -1;
                continue;
            }

            // Tuple elements, arrays, pointers, nullability, and qualified names are all type
            // syntax. Anything else -- an operator, a literal, a brace -- is not. ":" is here for
            // the two halves of "global::"; the scanner emits one-character punctuators, so a
            // "::" token never arrives.
            if (c is "," or "." or "?" or "[" or "]" or "*" or "(" or ")" or ":")
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
