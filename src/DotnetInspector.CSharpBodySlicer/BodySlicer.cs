namespace DotnetInspector.CSharpBodySlicer;

/// <summary>
/// Isolates one member's text from a C# source file, given the line range a portable PDB
/// reports for that member.
/// <para>
/// This is not a C# parser. It has no syntax tree, recognizes declarations by scanning text,
/// and is heuristic at the edges; the cases it is known not to handle are pinned as tests
/// rather than fixed. It exists because sequence points cover a member's body but not its
/// declaration, so the signature, attributes, and doc comments have to be recovered from text.
/// </para>
/// </summary>
public static class BodySlicer
{
    /// <summary>
    /// Reconstructs a method's source text from the full file <paramref name="sourceText"/> and the
    /// sequence-point line range (<paramref name="startLine"/>..<paramref name="endLine"/>, 1-based).
    /// Sequence points cover the body, so this scans backward to capture the signature (skipping
    /// doc comments, attributes, and preprocessor lines) and forward to include the closing brace,
    /// then dedents the block. Line numbers outside the file bounds surface as an
    /// <see cref="IndexOutOfRangeException"/>, which callers already handle by treating the source
    /// as unavailable.
    /// <para>
    /// Returns <see langword="null"/> when the range carries no authored member declaration to
    /// isolate — a positional record's property accessor, a primary constructor, and a
    /// constructor synthesized from field initializers all map to the enclosing type's header.
    /// Callers must report that as absent source rather than rendering the captured text.
    /// </para>
    /// <para>
    /// <paramref name="isDestructor"/> must be set by the caller from the resolved member's
    /// identity (its kind/metadata name), not inferred from source text. A C# destructor's source
    /// line is "~Type(...)", which carries no accessibility keyword and whose metadata name
    /// ("Finalize") does not appear in the text, so the backward scan would otherwise walk past it
    /// into the preceding member and leak unrelated declarations. When set, the scan stops at the
    /// destructor's signature line, recognized via <see cref="IsDestructorSignatureLine"/>.
    /// <paramref name="destructorTypeName"/> — the declaring type's simple name — is the authoritative
    /// discriminator: the signature is "~TypeName" (optionally preceded by "extern"/"unsafe"), so a
    /// line matches only when the tilde is followed by exactly that name as a token and then either
    /// an empty-or-open-paren "(" continuation or end-of-line (for a signature whose parameter list
    /// wraps to a following line). This is robust where a single-line grammar is not: it rejects a
    /// "#line hidden" body complement that can become the first visible sequence point — whether a
    /// bare "~mask;", a field "~Preceding;", or an invocation "~Compute()"/"~Compute(x);" — because
    /// none spell the declaring type name, while still accepting a signature whose "()" wraps onto a
    /// later line. When <paramref name="destructorTypeName"/> is null/empty (callers that cannot
    /// supply it), the matcher falls back to requiring the full parameterless "~Identifier()"
    /// grammar on one line.
    /// </para>
    /// </summary>
    public static string? ExtractMethodBody(string sourceText, int startLine, int endLine, string methodName, bool isDestructor = false, string? destructorTypeName = null)
    {
        var lines = sourceText.Split('\n');
        int start = startLine;
        int end = Math.Min(endLine, lines.Length);

        // The declaring type name may arrive namespace-qualified/nested/generic; the source
        // destructor spells only the simple name, so reduce it once up front.
        string? simpleTypeName = string.IsNullOrEmpty(destructorTypeName) ? null : SimpleTypeName(destructorTypeName);

        // Scan backward from the first sequence point to capture the method signature.
        // A member whose first sequence point already lands on its own declaration line — a
        // one-line expression-bodied member, or a property/event accessor whose points map to
        // the property declaration — needs no backward scan. Scanning back from such a line
        // skips the blank separator or opening brace above it and captures the preceding member
        // or the enclosing type header instead, which misattributes source (issue #3278).
        int sigStart = start;
        bool startsAtDeclaration = start >= 1 && start <= lines.Length
            && (IsMemberSignatureLine(lines[start - 1].TrimStart(), isDestructor, simpleTypeName)
                || DeclaresMember(lines[start - 1].TrimStart(), methodName));
        for (int i = start - 2; !startsAtDeclaration && i >= Math.Max(0, start - 15); i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("///") || trimmed.StartsWith("//")
                || trimmed.StartsWith("[") || trimmed.StartsWith("#"))
                continue;
            if (trimmed == "{")
                continue;
            if (trimmed.StartsWith("}"))
            {
                sigStart = i + 2;
                break;
            }

            sigStart = i + 1;
            if (StartsWithDeclarationModifier(trimmed)
                || (isDestructor && IsDestructorSignatureLine(trimmed, simpleTypeName))
                || trimmed.Contains(methodName))
                break;
        }

        int from = sigStart - 1;
        int to = end;

        if (from < 0) from = 0;
        if (to > lines.Length) to = lines.Length;

        // A positional record's property accessor, a primary constructor, and a constructor
        // synthesized from field initializers have no authored member declaration of their own,
        // so their sequence points legitimately land on the enclosing type's header. There is
        // nothing to slice: returning the header would present a truncated type declaration as
        // the member's source, which is wrong output rather than absent output. Report absence
        // and let the caller say so.
        //
        // A range that merely *contains* the header is a different case. The backward scan
        // cannot recognize a constructor that leads with no modifier, because ".ctor" is not
        // its source spelling, so it walks past the declaration and up to the header. That
        // constructor does have authored source, and the header names the type it is named
        // for, so look below the header for it before concluding there is nothing to show.
        //
        // This runs before the end boundary is decided, because moving the start moves the
        // brace depth the end-boundary scan reads: measured from the type header the range
        // still has the type's block open, and the forward scan would then append the type's
        // closing brace to the constructor.
        int headerIndex = IndexOfTypeDeclaration(lines[from..to], start - 1 - from, out string? declaredTypeName);
        if (headerIndex >= 0)
        {
            int ctorIndex = IndexOfConstructorDeclaration(lines[from..to], headerIndex, start - 1 - from, declaredTypeName);
            if (ctorIndex < 0)
                return null;

            from += ctorIndex;
        }

        // A declaration whose range already terminates on its last line — an expression body's
        // ";" or an auto-property's "{ get; set; }" — owns no trailing brace to recover, so the
        // next "}" below it closes the enclosing type instead (issue #3278). A range that still
        // has a block open does own one, even when its last line ends in ";": a signature whose
        // "{" sits on the declaration line ends its sequence range on the last statement.
        //
        // Both answers read the same lexical state, so one scan produces both. A trailing
        // comment must not hide the terminating ";" (issue #3300), and a brace inside a comment
        // or a literal must not count as structural.
        //
        // This asks the captured range alone, not where the range began. A conventionally
        // braced member starts its sequence range on "{", so it is not "at" its declaration,
        // yet the range still closes its own block and owns no brace below it. Gating on the
        // start let the forward scan run for every such member; that was harmless while a
        // sibling followed, and swallowed the enclosing type's "}" when the member was the
        // last one in its type.
        bool endsAtDeclaration = EndsDeclaration(lines, from, to);

        // Recover the member's own closing brace when its range stops above it.
        if (!endsAtDeclaration)
            to = IndexPastClosingBrace(lines, from, to, IsAccessorName(methodName));

        if (to > lines.Length) to = lines.Length;

        while (from < to && lines[from].TrimStart().Length == 0)
            from++;

        var methodLines = lines[from..to];

        int minIndent = methodLines
            .Where(l => l.TrimStart().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var dedented = methodLines.Select(l => l.Length >= minIndent ? l[minIndent..] : l);
        return string.Join('\n', dedented).TrimEnd();
    }

    /// <summary>
    /// Keywords that make a declaration a type or namespace rather than a member.
    /// <c>record</c> covers <c>record class</c> and <c>record struct</c>, whose second keyword
    /// this never reaches. <c>namespace</c> belongs here because a range that opens on one has
    /// walked clear past every member; none of these is a legal identifier, so no member
    /// declaration can begin with one.
    /// </summary>
    private static readonly string[] TypeDeclarationKeywords =
        ["class", "struct", "interface", "enum", "record", "delegate", "namespace"];

    /// <summary>
    /// Modifiers that may precede a type keyword. This is deliberately a superset of
    /// <see cref="DeclarationModifiers"/> — <c>ref</c>, <c>file</c>, and <c>new</c> lead a type
    /// declaration but not a member whose body carries sequence points.
    /// </summary>
    private static readonly string[] TypeDeclarationModifiers =
        ["public", "private", "protected", "internal", "static", "abstract",
         "sealed", "partial", "readonly", "ref", "file", "unsafe", "new"];

    /// <summary>
    /// Index of the line in <paramref name="capturedLines"/> that opens a type declaration, or
    /// <c>-1</c> when the range opens a member declaration instead. When a header is found,
    /// <paramref name="typeName"/> receives the name it declares.
    /// <para>
    /// The match is token-based, walking leading modifiers until it reaches a type keyword or a
    /// token that is neither. That distinction matters: a member such as
    /// <c>public void Process(RecordBatch batch)</c> spells "Record" inside an identifier, and
    /// <c>public int Classify()</c> spells "Class", so a substring test would misfire on both.
    /// </para>
    /// <para>
    /// Attributes and comments are stripped from the head of the line rather than causing the
    /// line to be skipped. Skipping the line lets <c>[Obsolete] public record R(int X)</c>
    /// escape the check entirely, because the line opens with "[".
    /// </para>
    /// </summary>
    private static int IndexOfTypeDeclaration(string[] capturedLines, int target, out string? typeName)
    {
        typeName = null;

        int first = -1;
        for (int i = 0; i < capturedLines.Length && first < 0; i++)
        {
            // A line that is only trivia — blank, a comment, a directive, an attribute on its
            // own line — carries no declaration, so the declaration is on a later line.
            if (StripLeadingTrivia(capturedLines[i].TrimStart()).Length > 0)
                first = i;
        }

        if (first < 0 || !OpensTypeDeclaration(StripLeadingTrivia(capturedLines[first].TrimStart()), out _, out _))
            return -1;

        // The capture may run back through several enclosing scopes. The member belongs to the
        // innermost type still open at the target line, not to the first declaration in the
        // capture: taking the first reported every constructor inside a namespace or a nested
        // type as absent, because it searched for the wrong name at the wrong depth
        // (adversarial review, MAI-Code and GPT). A namespace is never a declaring type.
        var open = new List<(int Index, string? Name, int BodyDepth, bool Entered)>();
        var state = new LexState();
        int depth = 0;

        for (int i = first; i <= target && i < capturedLines.Length; i++)
        {
            var trimmed = StripLeadingTrivia(capturedLines[i].TrimStart());

            if (trimmed.Length > 0 && OpensTypeDeclaration(trimmed, out string? name, out bool isNamespace) && !isNamespace)
                open.Add((i, name, depth + 1, false));

            char significant = ScanLine(capturedLines[i], state, ref depth);

            if (state.Untracked)
                return -1;

            // The target's own line must not retire the type that declares it: "class C { C() { } }"
            // opens and closes C there, and the constructor is still C's.
            if (i == target)
                break;

            for (int j = 0; j < open.Count; j++)
            {
                if (!open[j].Entered && depth >= open[j].BodyDepth)
                    open[j] = open[j] with { Entered = true };
            }

            // A type stops enclosing when its body closes, and equally when its declaration
            // ends without ever opening one. Reading only the depth left at end of line saw
            // neither bodiless form: "record R(int X);" never reaches its body depth, and
            // "class Inner { }" is back below it before the line ends. Both stayed open, so a
            // sibling above the target held the innermost slot and every constructor below it
            // was reported absent (adversarial review, MAI-Code). A declaration that ended
            // encloses nothing, and it ends on the ";" or "}" that terminates it — but only
            // when that terminator is at declaration level. An attribute on a type parameter
            // may hold an array initializer, whose closing brace ends nothing while the
            // attribute's bracket is still open (adversarial review, GPT). This is the same
            // carried-bracket blindness the sibling question had to learn in round 6.
            while (open.Count > 0
                && (open[^1].Entered
                    ? depth < open[^1].BodyDepth
                    : depth == open[^1].BodyDepth - 1
                        && state.BracketDepth == 0
                        && (significant == ';' || significant == '}')))
            {
                open.RemoveAt(open.Count - 1);
            }
        }

        if (open.Count == 0)
            return -1;

        typeName = open[^1].Name;
        return open[^1].Index;
    }

    /// <summary>
    /// The line with any leading attribute lists and comments removed. A declaration may share
    /// its line with either, and both may repeat.
    /// </summary>
    private static string StripLeadingTrivia(string trimmed)
    {
        while (trimmed.Length > 0)
        {
            if (trimmed.StartsWith('#') || trimmed.StartsWith("//"))
                return string.Empty;

            if (trimmed.StartsWith("/*"))
            {
                int close = trimmed.IndexOf("*/", 2, StringComparison.Ordinal);
                if (close < 0)
                    return string.Empty;

                trimmed = trimmed[(close + 2)..].TrimStart();
                continue;
            }

            if (trimmed.StartsWith('['))
            {
                int close = IndexPastAttributeList(trimmed);
                if (close < 0)
                    return string.Empty;

                trimmed = trimmed[close..].TrimStart();
                continue;
            }

            return trimmed;
        }

        return trimmed;
    }

    /// <summary>
    /// Modifiers a constructor declaration may carry. Deliberately narrower than
    /// <see cref="TypeDeclarationModifiers"/>: "new", "ref", "sealed", "abstract", "readonly",
    /// and "file" cannot lead a constructor, and admitting "new" would let the statement
    /// <c>new R(1);</c> read as a modifier followed by a declaration.
    /// </summary>
    private static readonly string[] ConstructorModifiers =
        ["public", "private", "protected", "internal", "static", "extern", "unsafe"];

    /// <summary>
    /// Index of the line at or after <paramref name="searchFrom"/> that declares a constructor
    /// for <paramref name="typeName"/>, or <c>-1</c> when the range holds none. A constructor
    /// spells the type's own name followed by its parameter list, which is what tells an
    /// authored <c>MetadataTypeNameResult(string name)</c> from a positional record's primary
    /// constructor, whose parameters sit on the type header itself.
    /// <para>
    /// Spelling alone is not enough, because a statement can spell the same thing:
    /// <c>new R(1);</c> and a bare <c>R(1);</c> call both reach the type's name followed by
    /// "(". A declaration is separated from a statement by where it sits, so a candidate is
    /// only considered at member level — directly inside the type's own block. A statement
    /// lives in a method body, one block deeper. Adversarial review (MAI-Code) found this;
    /// <c>ConstructorRecovery_IgnoresStatementsThatSpellTheTypeName</c> is the gate.
    /// </para>
    /// <para>
    /// The accepted modifiers are the ones a constructor can carry. In particular "new" is not
    /// among them, which is what stops <c>new R(1);</c> from reading as a modifier followed by
    /// a declaration.
    /// </para>
    /// <para>
    /// The search stops at <paramref name="searchTo"/>, the member's own first sequence point.
    /// A declaration the backward scan walked past necessarily sits at or above that point, so
    /// anything below it belongs to some other member: a positional record's range can span a
    /// secondary constructor and its property initializers, and accepting that constructor
    /// presented one member's source as another's (adversarial review, GPT).
    /// </para>
    /// </summary>
    private static int IndexOfConstructorDeclaration(string[] capturedLines, int searchFrom, int searchTo, string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return -1;

        int start = Math.Max(0, searchFrom);
        int last = Math.Min(searchTo, capturedLines.Length - 1);

        // Depth relative to the declaring type's header, which the caller has already
        // identified. The header's own line is scanned first so that "class C {" and a "{" on
        // the next line both land at depth 1 for the lines that follow. Lines above the header
        // are enclosing scopes: they contribute their lexical state, but not their depth, or a
        // constructor inside a namespace or a nested type would never reach member level.
        var state = new LexState();
        int enclosing = 0;
        for (int i = 0; i < start && i < capturedLines.Length; i++)
            ScanLine(capturedLines[i], state, ref enclosing);

        int depth = 0;

        int commentOpenedAt = -1;

        for (int i = start; i <= last; i++)
        {
            // A brace this scanner could not place leaves the depth unknown, so it can no
            // longer tell a declaration from a statement. Stop rather than guess.
            if (state.Untracked)
                return -1;

            bool carriedComment = state.InBlockComment;

            if (DeclaresConstructorAtMemberLevel(capturedLines[i], state, depth, typeName))
                return IndexOfDeclarationStart(capturedLines, start, carriedComment ? commentOpenedAt : i);

            ScanLine(capturedLines[i], state, ref depth);

            if (!carriedComment && state.InBlockComment)
                commentOpenedAt = i;
            else if (!state.InBlockComment)
                commentOpenedAt = -1;
        }

        return -1;
    }

    /// <summary>
    /// The first line of the declaration that <paramref name="declared"/> completes. A
    /// declaration may begin above the line that spells the constructor's name: a modifier may
    /// sit on its own line, and an attribute list or block comment may open above it. Starting
    /// the slice at the name alone dropped those lines, which lost a modifier and — when a
    /// block comment closed on the name's line — left a stray "*/" that does not parse
    /// (adversarial review, GPT).
    /// </summary>
    private static int IndexOfDeclarationStart(string[] capturedLines, int limit, int declared)
    {
        int index = Math.Max(limit, declared);

        while (index > limit && IsDeclarationPrefixOnly(capturedLines[index - 1]))
            index--;

        return index;
    }

    /// <summary>
    /// True when the line carries only the head of a declaration that continues below it —
    /// attribute lists, comments, and modifiers, with no name and nothing that terminates a
    /// statement.
    /// </summary>
    private static bool IsDeclarationPrefixOnly(string line)
    {
        var trimmed = StripLeadingTrivia(line.TrimStart());

        if (trimmed.Length == 0)
            return false;

        int index = 0;
        while (index < trimmed.Length)
        {
            int end = index;
            while (end < trimmed.Length && (char.IsLetterOrDigit(trimmed[end]) || trimmed[end] == '_'))
                end++;

            if (end == index || Array.IndexOf(ConstructorModifiers, trimmed[index..end]) < 0)
                return false;

            index = SkipTrivia(trimmed, end);
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="text"/> opens a constructor declaration for
    /// <paramref name="typeName"/>: the type's own name, under any modifiers a constructor may
    /// carry, followed by its parameter list.
    /// </summary>
    private static bool DeclaresConstructor(string text, string typeName)
    {
        int index = SkipTrivia(text, 0);
        while (index < text.Length)
        {
            int end = index;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                end++;

            if (end == index)
                return false;

            var token = text[index..end];
            int next = SkipTrivia(text, end);
            if (token == typeName)
                return next < text.Length && text[next] == '(';

            if (Array.IndexOf(ConstructorModifiers, token) < 0)
                return false;

            index = next;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="line"/> declares a constructor for <paramref name="typeName"/>
    /// at the type's member level, given the lexical state and brace depth carried into it.
    /// <para>
    /// Asking this only of the start of the line missed every constructor that shares a line
    /// with something else: with the type header ("class C { C() { } }"), with an opening brace
    /// below the header ("{ C() { } }"), or with an earlier member ("class C { int X; C() { } }")
    /// — each reported as absent authored source (adversarial review, MAI-Code and Gemini). A
    /// member can only begin where the previous one ended, so the candidates are the start of
    /// the line and every position just past a brace or semicolon. Confirming each candidate's
    /// depth with the shared scanner is also what keeps a brace inside a comment, a string, or a
    /// character literal from being taken for the one that opens the type's block.
    /// </para>
    /// <para>
    /// The answer is a line, not a column: the caller slices whole lines, so a constructor
    /// anywhere on the line makes the whole line the answer.
    /// </para>
    /// </summary>
    private static bool DeclaresConstructorAtMemberLevel(string line, LexState entry, int entryDepth, string typeName)
    {
        // A line that closes a comment, literal, or bracketed construct carried in from above
        // and then declares the constructor is a declaration from that point, and no brace or
        // semicolon precedes it (adversarial review, GPT).
        int resumes = IndexWhereCodeResumes(line, entry);

        for (int i = 0; i <= line.Length; i++)
        {
            if (i > 0 && i != resumes && line[i - 1] is not ('{' or '}' or ';'))
                continue;

            var probe = entry.Clone();
            int depth = entryDepth;
            ScanLine(line[..i], probe, ref depth);

            // Rejects a candidate whose brace or semicolon turned out to be comment or literal
            // text, and any position that is not at the type's member level.
            if (depth != 1 || probe.InBlockComment || probe.InLiteral || probe.Untracked || probe.BracketDepth != 0)
                continue;

            if (DeclaresConstructor(StripLeadingTrivia(line[i..].TrimStart()), typeName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Index of the next significant character at or after <paramref name="index"/>, skipping
    /// whitespace and comments. C# allows either between any two tokens, so a tab-separated
    /// modifier and an interposed <c>/* */</c> spell the same declaration (adversarial review,
    /// GPT). A line comment runs to the end, so nothing significant follows it.
    /// </summary>
    private static int SkipTrivia(string text, int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                    return text.Length;

                if (text[index + 1] == '*')
                {
                    int close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (close < 0)
                        return text.Length;

                    index = close + 2;
                    continue;
                }
            }

            return index;
        }

        return text.Length;
    }

    private static bool OpensTypeDeclaration(string trimmed, out string? typeName, out bool isNamespace)
    {
        typeName = null;
        isNamespace = false;
        int index = 0;
        while (index < trimmed.Length)
        {
            int end = index;
            while (end < trimmed.Length && (char.IsLetterOrDigit(trimmed[end]) || trimmed[end] == '_'))
                end++;

            if (end == index)
                return false;

            var token = trimmed[index..end];
            if (Array.IndexOf(TypeDeclarationKeywords, token) >= 0)
            {
                // "delegate*<int, int>" is a function-pointer *type*, so it leads a member's
                // return type rather than a delegate declaration. C# allows trivia between the
                // two tokens, so "delegate *<int, int>" is the same type (adversarial review,
                // GPT).
                if (token == "delegate")
                {
                    int star = SkipTrivia(trimmed, end);
                    if (star < trimmed.Length && trimmed[star] == '*')
                        return false;
                }

                isNamespace = token == "namespace";
                typeName = NameAfterTypeKeyword(trimmed, end);
                return true;
            }

            if (Array.IndexOf(TypeDeclarationModifiers, token) < 0)
                return false;

            index = SkipTrivia(trimmed, end);
        }

        return false;
    }

    /// <summary>
    /// The declared name following a type keyword, skipping the second keyword of the two-word
    /// forms (<c>record struct</c>, <c>record class</c>), or null when none follows.
    /// </summary>
    private static string? NameAfterTypeKeyword(string trimmed, int index)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            index = SkipTrivia(trimmed, index);

            int end = index;
            while (end < trimmed.Length && (char.IsLetterOrDigit(trimmed[end]) || trimmed[end] == '_'))
                end++;

            if (end == index)
                return null;

            var token = trimmed[index..end];
            if (Array.IndexOf(TypeDeclarationKeywords, token) >= 0)
            {
                index = end;
                continue;
            }

            return token;
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="trimmed"/> (a leading-whitespace-stripped line) begins a member
    /// declaration, judged by a leading declaration modifier. Used both to break the backward
    /// signature scan in <see cref="ExtractMethodBody"/> and to decide whether a first sequence
    /// point already sits on its member's declaration line, so that scan can be skipped.
    /// <para>
    /// This deliberately omits the scan's <c>Contains(methodName)</c> clause: that clause is a
    /// safe last resort while walking up toward a known-preceding signature, but a method whose
    /// first statement recurses would spell its own name and be mistaken for its declaration.
    /// </para>
    /// <para>
    /// A member declared with no modifier at all — an implicitly private member, or an interface
    /// member — is recognized separately by <see cref="DeclaresMember"/>, which anchors on the
    /// member's own name rather than on a modifier.
    /// </para>
    /// </summary>
    private static bool IsMemberSignatureLine(string trimmed, bool isDestructor, string? simpleTypeName)
        => StartsWithDeclarationModifier(trimmed)
            || (isDestructor && IsDestructorSignatureLine(trimmed, simpleTypeName));

    /// <summary>
    /// Modifiers that can lead a C# member declaration whose body carries sequence points.
    /// </summary>
    private static readonly string[] DeclarationModifiers =
    [
        "public", "private", "protected", "internal", "static",
        "abstract", "async", "extern", "override", "partial",
        "readonly", "required", "sealed", "unsafe", "virtual"
    ];

    /// <summary>
    /// True when <paramref name="trimmed"/> opens with one of <see cref="DeclarationModifiers"/>
    /// as a whole token followed by the start of a type or name.
    /// <para>
    /// The token boundary matters in both directions: it keeps <c>internalCounter = 1;</c> and
    /// <c>file.Write(x);</c> from reading as declarations, and the follower check keeps the
    /// <c>unsafe { ... }</c> block statement from doing so. A <c>(</c> is accepted as a follower
    /// so a tuple-returning declaration still matches; no modifier is a valid expression, so no
    /// statement can open that way.
    /// </para>
    /// </summary>
    private static bool StartsWithDeclarationModifier(string trimmed)
    {
        foreach (var modifier in DeclarationModifiers)
        {
            if (!trimmed.StartsWith(modifier, StringComparison.Ordinal))
                continue;

            int i = modifier.Length;
            if (i >= trimmed.Length || !char.IsWhiteSpace(trimmed[i]))
                continue;

            while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                i++;

            if (i < trimmed.Length
                && (char.IsLetter(trimmed[i]) || trimmed[i] == '_' || trimmed[i] == '@' || trimmed[i] == '('))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the captured range <c>[from, to)</c> ends a declaration outright, so the
    /// forward scan in <see cref="ExtractMethodBody"/> has no trailing brace to recover.
    /// <para>
    /// Two conditions must hold. The range's last significant character — the last
    /// non-whitespace character outside a comment, so neither a trailing <c>// note</c> nor a
    /// blank or comment-only line below the declaration can hide it (issue #3300) — must be
    /// <c>;</c> or <c>}</c>. And the range must not leave a block open, counting only braces
    /// outside comments and literals: a property such as <c>public string M =&gt; "{";</c>
    /// opens nothing and owns no brace below it.
    /// </para>
    /// <para>
    /// A single-line range is never judged unclosed, and an untracked raw string literal is
    /// treated as leaving a block open so the forward scan runs, which is the conservative
    /// answer.
    /// </para>
    /// </summary>
    private static bool EndsDeclaration(string[] lines, int from, int to)
    {
        int first = Math.Max(0, from);
        int last = Math.Min(to, lines.Length);
        if (last <= first)
            return false;

        int depth = 0;
        var state = new LexState();
        char terminator = '\0';

        for (int i = first; i < last; i++)
        {
            // A blank, whitespace-only, or comment-only line contributes no significant
            // character, and must not erase the terminator an earlier line established.
            char significant = ScanLine(lines[i], state, ref depth);
            if (significant != '\0')
                terminator = significant;
        }

        if (terminator != ';' && terminator != '}')
            return false;

        return last - first <= 1 || (!state.Untracked && depth <= 0);
    }

    /// <summary>
    /// Lines the forward scan will read past the captured range looking for a closing brace.
    /// A member longer than this is not extended rather than scanned without bound.
    /// </summary>
    private const int ForwardScanLimit = 500;

    /// <summary>
    /// Accessor keywords, which open a sibling declaration inside a property or event block.
    /// </summary>
    private static readonly string[] AccessorKeywords = ["get", "set", "init", "add", "remove"];

    /// <summary>
    /// The metadata name prefixes that mark a member as one accessor of a property or event.
    /// </summary>
    private static readonly string[] AccessorNamePrefixes =
        ["get_", "set_", "init_", "add_", "remove_"];

    /// <summary>
    /// True when <paramref name="methodName"/> names an accessor, which is the only member that
    /// can have a sibling accessor inside the block its slice runs through.
    /// </summary>
    private static bool IsAccessorName(string methodName)
    {
        foreach (var prefix in AccessorNamePrefixes)
        {
            if (methodName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The line with any leading comments removed. A declaration may share its line with either
    /// comment form, and both may repeat.
    /// </summary>
    private static string StripLeadingComments(string trimmed)
    {
        while (trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            int end = trimmed.IndexOf("*/", 2, StringComparison.Ordinal);
            if (end < 0)
                return string.Empty;

            trimmed = trimmed[(end + 2)..].TrimStart();
        }

        return trimmed.StartsWith("//", StringComparison.Ordinal) ? string.Empty : trimmed;
    }

    /// <summary>
    /// Index on <paramref name="line"/> where a declaration could begin, given the state
    /// carried into it, or <c>-1</c> when nothing on the line is eligible.
    /// <para>
    /// A line that closes a multi-line comment, literal, or bracketed construct and then holds
    /// a real declaration must be read as a declaration from that point. Asking only whether
    /// the line *began* inside one suppressed the question on exactly the line that answers it
    /// — first for comments and literals, then again for brackets (adversarial review,
    /// MAI-Code and GPT). All three are the same question, so one answer serves them.
    /// </para>
    /// </summary>
    private static int IndexWhereCodeResumes(string line, LexState state)
    {
        if (!state.InBlockComment && !state.InLiteral && state.BracketDepth == 0)
            return 0;

        var probe = state.Clone();
        int depth = 0;
        int index = Scan(line, probe, ref depth, start: 0, untilLiteralCloses: false, out _, untilCodeResumes: true);
        return probe.InBlockComment || probe.InLiteral ? -1 : index;
    }

    /// <summary>
    /// Index just past the attribute list opening at index 0, or <c>-1</c> when it does not
    /// close on this line. Brackets nest — <c>[Foo(new[] { 1 })]</c> — and a string inside the
    /// list may spell a bracket of its own, so neither is counted structurally.
    /// </summary>
    private static int IndexPastAttributeList(string trimmed)
    {
        var probe = new LexState();
        int depth = 0;
        int index = Scan(trimmed, probe, ref depth, start: 0, untilLiteralCloses: false, out _, untilBracketsClose: true);

        // The list did not finish on this line: it continues below, or a comment or literal
        // swallowed the rest of the line.
        if (probe.BracketDepth != 0 || probe.InBlockComment || probe.InLiteral || probe.Untracked)
            return -1;

        return index;
    }

    /// <summary>
    /// True when <paramref name="line"/> opens an accessor sibling to the one being sliced.
    /// <para>
    /// This is asked only while slicing an accessor. Asking it of every member read a
    /// <c>static</c> local function, and any other statement that opens with a declaration
    /// modifier, as a sibling and truncated the enclosing method at it (adversarial review,
    /// Gemini). Only a property or event block can hold a sibling accessor, so only an accessor
    /// can be interrupted by one.
    /// </para>
    /// </summary>
    private static bool OpensSiblingAccessor(string line)
    {
        var trimmed = StripLeadingComments(line.TrimStart());

        // An accessor may carry attributes of its own. An attribute list is not itself an
        // accessor, though: reading one as a sibling truncated an accessor at an attributed
        // local function in its body (adversarial review, MAI-Code). Skip past the list and
        // ask about what follows it — nothing, when the list has the line to itself, in which
        // case the accessor below it is the line that answers.
        while (trimmed.StartsWith('['))
        {
            int close = IndexPastAttributeList(trimmed);
            if (close < 0)
                return false;

            trimmed = StripLeadingComments(trimmed[close..].TrimStart());
        }

        if (StartsWithDeclarationModifier(trimmed))
            trimmed = StripLeadingComments(SkipLeadingModifiers(trimmed));

        foreach (var keyword in AccessorKeywords)
        {
            if (!trimmed.StartsWith(keyword, StringComparison.Ordinal))
                continue;

            // "get;", "get =>", "get {", and a bare "get" whose body is below are accessors.
            // "getCount" and "setting" are not, and neither is an assignment to a local named
            // "set", which a bare "=" accepted (adversarial review, GPT).
            var rest = StripLeadingComments(trimmed[keyword.Length..].TrimStart());

            if (rest.Length == 0 || rest[0] is ';' or '{' || rest.StartsWith("=>", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The line past any run of leading declaration modifiers.
    /// </summary>
    private static string SkipLeadingModifiers(string trimmed)
    {
        bool advanced = true;
        while (advanced)
        {
            advanced = false;
            foreach (var modifier in DeclarationModifiers)
            {
                if (!trimmed.StartsWith(modifier, StringComparison.Ordinal))
                    continue;

                int i = modifier.Length;
                if (i >= trimmed.Length || !char.IsWhiteSpace(trimmed[i]))
                    continue;

                trimmed = trimmed[i..].TrimStart();
                advanced = true;
                break;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Index just past the line closing the block the captured range leaves open, or
    /// <paramref name="to"/> when the range closes on its own, the depth cannot be read, a
    /// sibling declaration intervenes, or no closing line appears within
    /// <see cref="ForwardScanLimit"/> lines.
    /// <para>
    /// Stopping at the first non-empty line below the range instead truncated every member
    /// whose sequence range ends on a statement above its closing brace, dropping the remaining
    /// statements along with the brace (found by adversarial review, MAI-Code). Reading the
    /// depth is what separates "the member's own brace is below" from "the next brace closes
    /// the enclosing type" (issue #3278), and the caller has already excluded the second case.
    /// </para>
    /// <para>
    /// The scan stops short of a sibling declaration because the open block may belong to a
    /// property rather than to the member: a getter's range sits inside the property's braces,
    /// and running to the closing brace would present the setter as part of the getter's source.
    /// Accessors resolve separately, so the scan yields rather than merge them.
    /// </para>
    /// </summary>
    private static int IndexPastClosingBrace(string[] lines, int from, int to, bool slicingAccessor)
    {
        var state = new LexState();
        int depth = 0;

        for (int i = Math.Max(0, from); i < to; i++)
            ScanLine(lines[i], state, ref depth);

        if (state.Untracked || depth <= 0)
            return to;

        int limit = Math.Min(lines.Length, to + ForwardScanLimit);

        // Where the run of lines leading up to the current one stopped holding code, or -1
        // when the current line does. A sibling's attributes and comments are the sibling's,
        // so the member ends where that run began.
        int triviaRunStart = -1;

        for (int i = to; i < limit; i++)
        {
            // Asked before the line is scanned, so it must be asked of the line's code alone. A
            // "set" inside a multi-line block comment, raw string literal, or attribute list
            // is not a sibling — but a line that *closes* one and then declares a real sibling
            // is a declaration from that point on.
            if (slicingAccessor)
            {
                int resume = IndexWhereCodeResumes(lines[i], state);
                if (resume >= 0 && OpensSiblingAccessor(lines[i][resume..]))
                    return triviaRunStart >= 0 ? triviaRunStart : i;

                triviaRunStart = HoldsOnlyTrivia(lines[i], state, triviaRunStart >= 0)
                    ? (triviaRunStart >= 0 ? triviaRunStart : i)
                    : -1;
            }

            ScanLine(lines[i], state, ref depth);

            if (state.Untracked)
                return to;

            if (depth <= 0)
                return i + 1;
        }

        return to;
    }

    /// <summary>
    /// True when <paramref name="line"/> carries nothing but blank space, comments, or
    /// attribute lists, given the state carried into it.
    /// <para>
    /// Asking this of the line's text alone answered only for trivia that both opens and
    /// closes on one line: a multi-line attribute list or block comment leading the sibling
    /// was kept as the member's source, which does not parse (adversarial review, GPT). It is
    /// the same lesson the sibling question itself took three rounds to learn — a line-level
    /// predicate must be asked of the line's code, not its text.
    /// </para>
    /// <para>
    /// A carried literal or attribute list belongs to whichever side opened it, and the run
    /// answers that: every line since it began held only trivia, so a construct still open is
    /// the sibling's. With no run open there is nothing above but the member, so the construct
    /// is the member's code. Reading the literal alone got the first half wrong — a raw string
    /// inside the sibling's own attribute broke the run and left the attribute in the slice
    /// (adversarial review, Gemini) — and reading neither got the second half wrong, which is
    /// how a collection expression came to be discarded as an attribute list in round 8.
    /// </para>
    /// <para>
    /// A preprocessor directive is trivia too. It must be the first token on its line, so it
    /// is recognized once the carried constructs are accounted for (adversarial review,
    /// MAI-Code).
    /// </para>
    /// </summary>
    private static bool HoldsOnlyTrivia(string line, LexState state, bool runOpen)
    {
        if (!runOpen && (state.InLiteral || state.BracketDepth > 0))
            return false;

        int resume = IndexWhereCodeResumes(line, state);
        if (resume < 0)
            return true;

        if (!state.InBlockComment && !state.InLiteral && line.AsSpan().TrimStart().StartsWith("#"))
            return true;

        var rest = StripLeadingComments(line[resume..].TrimStart());

        while (rest.StartsWith('['))
        {
            int close = IndexPastAttributeList(rest);

            // The list runs past this line, so nothing else can be on it.
            if (close < 0)
                return true;

            rest = StripLeadingComments(rest[close..].TrimStart());
        }

        return rest.Length == 0;
    }

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
    private sealed class LexState
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
        /// Set when a brace could not be placed — an unterminated single-line literal, or a
        /// delimiter run this scanner will not guess at. The depth count is unusable from that
        /// point on, and callers treat it as "do not know" rather than as a depth.
        /// </summary>
        public bool Untracked;

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
            var copy = new LexState { InBlockComment = InBlockComment, Untracked = Untracked, BracketDepth = BracketDepth };
            copy.frames.AddRange(frames);
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
    private static bool IsDirective(string line, out bool conditional)
    {
        conditional = false;

        var trimmed = line.AsSpan().TrimStart();

        if (trimmed.IsEmpty || trimmed[0] != '#')
            return false;

        var name = trimmed[1..].TrimStart();

        foreach (var candidate in (ReadOnlySpan<string>)["if", "elif", "else", "endif"])
        {
            if (name.StartsWith(candidate, StringComparison.Ordinal) &&
                (name.Length == candidate.Length || !char.IsLetterOrDigit(name[candidate.Length])))
            {
                conditional = true;
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
    private static char ScanLine(string line, LexState state, ref int depth)
    {
        Scan(line, state, ref depth, start: 0, untilLiteralCloses: false, out char significant);
        return significant;
    }

    /// <summary>
    /// Scans <paramref name="line"/> from <paramref name="start"/>, returning the index it
    /// stopped at. With <paramref name="untilLiteralCloses"/> the scan stops as soon as the
    /// literal it opened is closed, which is how a caller consumes a single literal.
    /// </summary>
    private static int Scan(
        string line,
        LexState state,
        ref int depth,
        int start,
        bool untilLiteralCloses,
        out char significant,
        bool untilCodeResumes = false,
        bool untilBracketsClose = false)
    {
        significant = '\0';
        int i = start;
        bool opened = false;
        bool bracketOpened = false;

        if (!state.InBlockComment && !state.InLiteral && IsDirective(line, out bool conditional))
        {
            // A preprocessor directive is not code, so nothing on the line is scanned. A
            // conditional directive additionally means the braces around it may belong to a
            // branch the compiler discards, which leaves the structural depth unknowable.
            if (conditional)
                state.Untracked = true;

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
                if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    state.InBlockComment = false;
                    i += 2;
                }
                else
                {
                    i++;
                }

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
                        i += run;
                        continue;
                    }

                    if (run < frame.DollarRun)
                    {
                        // Too short to delimit a hole.
                        i += run;
                        continue;
                    }

                    if (!frame.Raw)
                    {
                        // One "$": braces pair off as escapes, and an odd one out opens a hole.
                        if (run % 2 == 0)
                        {
                            i += run;
                            continue;
                        }
                    }

                    // The braces that open the hole are the last DollarRun of the run; any
                    // ahead of them are literal text.
                    frame.InHole = true;
                    frame.HoleDepth = 1;
                    state.Replace(frame);
                    i += run;
                    continue;
                }

                if (c == '\\' && !frame.Verbatim && !frame.Raw)
                {
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
                            i += 2;
                            continue;
                        }

                        state.Pop();
                        significant = '"';
                        i += 1;
                        continue;
                    }

                    if (run >= frame.QuoteRun)
                    {
                        state.Pop();
                        significant = '"';
                        i += run;
                        continue;
                    }

                    // A shorter run inside a raw literal is content.
                    i += run;
                    continue;
                }

                i++;
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
                    break;
                }

                if (line[i + 1] == '*')
                {
                    state.InBlockComment = true;
                    i += 2;
                    continue;
                }
            }

            if (c == '\'')
            {
                i++;
                while (i < line.Length && line[i] != '\'')
                    i += line[i] == '\\' ? 2 : 1;
                i++;
                significant = '\'';
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
                    i = open > i ? open : i + 1;
                    if (!char.IsWhiteSpace(c))
                        significant = c;
                    continue;
                }

                int quotes = RunLength(line, open, '"');

                // Only a non-verbatim literal can be raw. After `@`, a run of three quotes is
                // an opener and one escaped quote, not a raw delimiter.
                bool raw = quotes >= 3 && !verbatim;

                if (!raw && quotes == 2)
                {
                    // The empty literal.
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
                i = open + (raw ? quotes : 1);
                continue;
            }

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
                        i += frame.DollarRun > 1 ? Math.Min(run, frame.DollarRun) : 1;
                        continue;
                    }

                    frame.HoleDepth--;
                    state.Replace(frame);
                }
                else
                {
                    depth--;
                }
            }

            if (!char.IsWhiteSpace(c))
                significant = c;

            i++;
        }

        // A literal that must close on its own line did not, so the scan lost its place.
        if (state.HasLineBoundLiteral)
            state.Untracked = true;

        return i;
    }

    /// <summary>
    /// Words that can open a statement, and so rule out a declaration no matter what follows.
    /// </summary>
    private static readonly HashSet<string> StatementOpeners = new(StringComparer.Ordinal)
    {
        "return", "throw", "yield", "await", "if", "while", "for", "foreach", "do", "switch",
        "case", "using", "lock", "fixed", "checked", "unchecked", "var", "new", "base", "this",
        "ref", "out", "in", "goto", "else", "try", "catch", "finally", "break", "continue",
        "default", "is", "as", "stackalloc", "nameof", "typeof", "sizeof", "delegate"
    };

    /// <summary>
    /// True when <paramref name="trimmed"/> declares the member named by
    /// <paramref name="methodName"/> without a leading modifier — an interface member or an
    /// implicitly private one, which <see cref="IsMemberSignatureLine"/> cannot recognize.
    /// <para>
    /// The line must read as a declaration prefix: a run of type-shaped tokens that reaches the
    /// member's own name, followed by <c>(</c>, <c>&lt;</c>, <c>{</c>, or <c>=&gt;</c>. Two
    /// requirements separate a declaration from a body line that merely spells the name. A
    /// leading statement keyword rejects the line outright, which covers a recursive
    /// <c>return Target(n - 1);</c>. And the name must open a new token — a return type has to
    /// precede it — rather than continue a dotted chain, which is what tells the explicit
    /// implementation <c>int IDefault.Target =&gt; 1;</c> from the qualified call
    /// <c>Helper.Target();</c>. A trailing <c>;</c> is deliberately not accepted, so a local
    /// declaration such as <c>Foo Target;</c> does not qualify.
    /// </para>
    /// <para>
    /// An indexer is spelled <c>this[...]</c> rather than by the <c>Item</c> name its accessors
    /// carry in metadata, so a property accessor also accepts <c>this</c> in the name position.
    /// </para>
    /// </summary>
    private static bool DeclaresMember(string trimmed, string methodName)
    {
        var name = SourceSpelledMemberName(methodName, out bool isPropertyAccessor);
        if (name.Length == 0)
            return false;

        int i = 0;
        int tokenIndex = 0;
        int chainStart = 0;
        bool afterDot = false;

        while (i < trimmed.Length)
        {
            while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                i++;
            if (i >= trimmed.Length)
                return false;

            char c = trimmed[i];
            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                int tokenStart = i;
                while (i < trimmed.Length
                    && (char.IsLetterOrDigit(trimmed[i]) || trimmed[i] == '_' || trimmed[i] == '@'))
                    i++;
                var token = trimmed[tokenStart..i];

                // A dotted continuation stays part of the chain its first token opened; anything
                // else opens a new one. Only a chain that something precedes can be a member name.
                if (!afterDot)
                    chainStart = tokenIndex;
                afterDot = false;

                // "ref" and "new" can lead a declaration (a ref return, a shadowing member) and
                // can also open a statement, so neither decides the line. Skipping them without
                // consuming a token position leaves the following token to be judged: a real
                // declaration still has to reach its name after a return type, while
                // "new Foo().Bar();" and "ref var x = ref y;" are still rejected below.
                if (tokenIndex == 0 && token is "ref" or "new")
                    continue;

                if (tokenIndex == 0 && StatementOpeners.Contains(token))
                    return false;

                // A token matching the name is only the member's own name when a return type
                // precedes it, so a type spelled like its member — CancellationToken
                // CancellationToken => default; — must keep scanning rather than stop here.
                bool isNamePosition = token == name
                    || (isPropertyAccessor && token == "this");
                if (isNamePosition
                    && chainStart >= 1
                    && FollowsDeclarationName(trimmed, i, token == "this"))
                    return true;

                tokenIndex++;
                continue;
            }

            if (c == '.')
            {
                afterDot = true;
                i++;
                continue;
            }

            // A generic argument list or array rank belongs to the type token it follows, so it
            // must be consumed whole — otherwise a type argument would read as a separate token
            // and let a qualified call such as Foo<T>.Target() pass as a declaration.
            if (c is '<' or '[')
            {
                int after = SkipBalanced(trimmed, i, c, c == '<' ? '>' : ']');
                if (after < 0)
                    return false;
                i = after;
                continue;
            }

            // A tuple return type occupies the type position and carries no leading identifier,
            // so it is consumed whole and counts as the type token the member name follows.
            // Only the type position accepts one, which keeps a parenthesized expression
            // elsewhere on the line from standing in for a return type.
            if (c == '(' && tokenIndex == 0)
            {
                int after = SkipBalanced(trimmed, i, '(', ')');
                if (after < 0)
                    return false;
                i = after;
                chainStart = tokenIndex;
                tokenIndex++;
                afterDot = false;
                continue;
            }

            if (c == '?')
            {
                i++;
                continue;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// True when the character following a candidate member name at <paramref name="index"/>
    /// can open that member's parameter list, type parameter list, accessor list, or expression
    /// body. An indexer must be followed by its <c>[</c> parameter list.
    /// </summary>
    private static bool FollowsDeclarationName(string trimmed, int index, bool isIndexer)
    {
        int j = index;
        while (j < trimmed.Length && char.IsWhiteSpace(trimmed[j]))
            j++;
        if (j >= trimmed.Length)
            return false;

        if (isIndexer)
            return trimmed[j] == '[';

        if (trimmed[j] is '(' or '<' or '{')
            return true;

        return trimmed[j] == '=' && j + 1 < trimmed.Length && trimmed[j + 1] == '>';
    }

    /// <summary>
    /// The index just past the <paramref name="close"/> that balances the <paramref name="open"/>
    /// at <paramref name="index"/>, or -1 when the group does not close on this line (a signature
    /// whose type argument list wraps), in which case no declaration claim can be made.
    /// </summary>
    private static int SkipBalanced(string trimmed, int index, char open, char close)
    {
        int depth = 0;
        for (int i = index; i < trimmed.Length; i++)
        {
            if (trimmed[i] == open)
                depth++;
            else if (trimmed[i] == close)
                depth--;

            if (depth == 0)
                return i + 1;
        }

        return -1;
    }

    /// <summary>
    /// The name a member is spelled with in source: an accessor's <c>get_</c>/<c>set_</c>/
    /// <c>add_</c>/<c>remove_</c> prefix names the owning property or event, and an explicit
    /// interface implementation carries a qualifying prefix that source states separately.
    /// </summary>
    private static string SourceSpelledMemberName(string methodName, out bool isPropertyAccessor)
    {
        isPropertyAccessor = false;
        var name = methodName.AsSpan();
        int lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
            name = name[(lastDot + 1)..];

        foreach (var prefix in (ReadOnlySpan<string>)["get_", "set_"])
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                isPropertyAccessor = true;
                return name[prefix.Length..].ToString();
            }
        }

        foreach (var prefix in (ReadOnlySpan<string>)["add_", "remove_"])
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return name[prefix.Length..].ToString();
        }

        return name.ToString();
    }

    /// <summary>
    /// True when <paramref name="trimmed"/> (a leading-whitespace-stripped line) begins a C#
    /// destructor signature. Used only to locate the signature line within an already-identified
    /// destructor scan (see <see cref="ExtractMethodBody"/>).
    /// <para>
    /// When <paramref name="typeName"/> (the declaring type's simple name) is supplied it is the
    /// authoritative discriminator: after the optional <c>extern</c>/<c>unsafe</c> modifiers and the
    /// tilde, the line must spell exactly that name as a token, then either an opening <c>(</c> or
    /// nothing (a signature whose parameter list wraps to a following line). This distinguishes the
    /// signature from a <c>#line hidden</c> body complement that can become the first visible
    /// sequence point — a bare <c>~mask;</c>, a field <c>~Preceding;</c>, or an invocation
    /// <c>~Compute()</c>/<c>~Compute(x);</c> — because none spell the declaring type name, while
    /// still accepting a wrapped-parenthesis signature. A Unicode-escaped type name
    /// (<c>~\u0043()</c> for <c>~C()</c>) is decoded during the comparison.
    /// </para>
    /// <para>
    /// When <paramref name="typeName"/> is null/empty, the matcher falls back to requiring the full
    /// parameterless <c>~Identifier()</c> grammar on a single line, which still rejects the common
    /// bitwise-complement body lines (they lack the empty <c>()</c>).
    /// </para>
    /// <para>
    /// Known limitations (accepted, out of scope). This is a single-line text heuristic, not a C#
    /// tokenizer, so two exotic valid-C# spellings are not handled: (1) a comment between the tilde
    /// and the type name (<c>~ /*x*/ C()</c>) is not recognized; and (2) a body statement that
    /// bitwise-complements an invocation of a local that shadows the enclosing type name
    /// (<c>~C();</c> where a local named <c>C</c> is in scope) can be mistaken for the signature if
    /// <c>#line hidden</c> makes it the first visible sequence point. Both require a member/local
    /// spelled exactly as the enclosing type under a hidden-line body — combinations that do not
    /// occur in real destructors. Fully resolving them would require multi-line tokenization, which
    /// this Roslyn-free path deliberately avoids.
    /// </para>
    /// </summary>
    internal static bool IsDestructorSignatureLine(string trimmed, string? typeName = null)
    {
        var span = trimmed.AsSpan();
        while (true)
        {
            span = span.TrimStart();
            if (TryStripModifier(ref span, "unsafe") || TryStripModifier(ref span, "extern"))
                continue;
            break;
        }

        if (span.Length == 0 || span[0] != '~')
            return false;

        span = span[1..].TrimStart();

        if (!string.IsNullOrEmpty(typeName))
        {
            // Authoritative match: the tilde must be followed by exactly the declaring type name as
            // a token. A destructor is parameterless, so the remainder is either an opening paren or
            // empty (parameter list wrapped to a later line).
            if (!TryMatchTypeName(span, typeName, out int consumed))
                return false;

            var after = span[consumed..].TrimStart();
            return after.Length == 0 || after[0] == '(';
        }

        // Fallback (no type name): require an identifier then an empty "()" on this line.
        if (span.Length == 0 || !(char.IsLetter(span[0]) || span[0] == '_' || span[0] == '@' || span[0] == '\\'))
            return false;

        int i = 1;
        while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] == '_' || span[i] == '\\'))
            i++;

        span = span[i..].TrimStart();
        if (span.Length == 0 || span[0] != '(')
            return false;

        span = span[1..].TrimStart();
        return span.Length > 0 && span[0] == ')';
    }

    /// <summary>
    /// Matches the declaring type name at the start of <paramref name="span"/> as a complete C#
    /// identifier token, decoding <c>\uXXXX</c>/<c>\UXXXXXXXX</c> escapes and an optional verbatim
    /// <c>@</c> prefix. Succeeds only when the whole <paramref name="typeName"/> is consumed and the
    /// following character is not an identifier-continuation char (so <c>~Computed()</c> does not
    /// match the type name <c>Compute</c>). On success <paramref name="consumed"/> is the number of
    /// source characters matched.
    /// </summary>
    private static bool TryMatchTypeName(ReadOnlySpan<char> span, string typeName, out int consumed)
    {
        consumed = 0;
        int si = 0;
        if (si < span.Length && span[si] == '@')
            si++;

        int ti = 0;
        while (ti < typeName.Length)
        {
            if (si >= span.Length)
                return false;

            char decoded;
            int advance;
            if (span[si] == '\\' && si + 1 < span.Length && (span[si + 1] == 'u' || span[si + 1] == 'U'))
            {
                int digits = span[si + 1] == 'u' ? 4 : 8;
                if (si + 2 + digits > span.Length)
                    return false;
                var hex = span.Slice(si + 2, digits);
                if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int codePoint)
                    || codePoint > 0xFFFF)
                    return false;
                decoded = (char)codePoint;
                advance = 2 + digits;
            }
            else
            {
                decoded = span[si];
                advance = 1;
            }

            if (decoded != typeName[ti])
                return false;

            si += advance;
            ti++;
        }

        // Require a token boundary: the type name must not be a prefix of a longer identifier.
        if (si < span.Length)
        {
            char next = span[si];
            if (char.IsLetterOrDigit(next) || next == '_' || next == '\\')
                return false;
        }

        consumed = si;
        return true;
    }

    /// <summary>
    /// The simple (unqualified, arity-stripped) name of a possibly namespace-qualified and/or nested
    /// type full name — e.g. "NS.Outer+Inner`1" -&gt; "Inner". Used to derive the destructor type
    /// name for <see cref="IsDestructorSignatureLine"/>.
    /// </summary>
    internal static string SimpleTypeName(string typeFullName)
    {
        if (string.IsNullOrEmpty(typeFullName))
            return typeFullName;

        int lastSep = typeFullName.LastIndexOfAny(['.', '+']);
        var segment = lastSep >= 0 ? typeFullName[(lastSep + 1)..] : typeFullName;
        int backtick = segment.IndexOf('`');
        return backtick >= 0 ? segment[..backtick] : segment;
    }

    static bool TryStripModifier(ref ReadOnlySpan<char> span, string modifier)
    {
        if (!span.StartsWith(modifier))
            return false;
        // Require a token boundary so an identifier like "unsafeThing" is not stripped.
        if (span.Length > modifier.Length)
        {
            char next = span[modifier.Length];
            if (char.IsLetterOrDigit(next) || next == '_')
                return false;
        }

        span = span[modifier.Length..];
        return true;
    }
}
