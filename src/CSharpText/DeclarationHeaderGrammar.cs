namespace CSharpText;

/// <summary>
/// Pure model-free grammar for classifying declaration headers from lexer tokens.
/// It owns no source traversal, scope mutation, trust propagation, or span projection.
/// </summary>
internal static class DeclarationHeaderGrammar
{
    internal enum ExtensionScopeKind
    {
        None,
        Known,
        Ambiguous,
    }

    internal readonly record struct ScopeContext(
        bool HasEnclosing,
        DeclarationKind Kind,
        string Name,
        bool IsStatic,
        bool StaticModifierKnown,
        bool IsPartial)
    {
        internal static ScopeContext None { get; } = new(false, default, "", false, false, false);
    }

    internal readonly record struct Declarator(string Name, bool HasInitializer);
    internal readonly record struct TruncatedHeader(
        List<ScanToken> Header,
        int ArrowLine,
        bool CutAtEquals);

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

    internal static bool HasTopLevelKeyword(
        IReadOnlyList<ScanToken> pending,
        string keyword,
        Func<ScanToken, string> text)
    {
        int depth = 0;
        for (int i = 0; i < pending.Count; i++)
        {
            var token = pending[i];
            if (token.Kind == ScanTokenKind.Punctuator)
            {
                string value = text(token);
                if (value is "(" or "[" or "{") depth++;
                else if (value is ")" or "]" or "}") depth--;
            }
            else if (depth == 0 && IsKeyword(pending, i, keyword, text))
            {
                return true;
            }
        }

        return false;
    }

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

    internal static TruncatedHeader Truncate(List<ScanToken> pending, Func<ScanToken, string> text)
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
    internal static (DeclarationKind? Kind, string Name) Classify(
        List<ScanToken> pending, ScopeContext enclosing, bool opensBody, Func<ScanToken, string> text)
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
        if (enclosing is { HasEnclosing: true, Kind: DeclarationKind.Enum })
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
            if (enclosing.HasEnclosing && name == enclosing.Name && name.Length > 0)
                return (DeclarationKind.Constructor, name);
            return (DeclarationKind.Method, name);
        }

        // An indexer has a bracketed parameter list rather than a parenthesized one.
        int thisAt = at.FirstOrDefault(i => IsKeyword(header, i, "this", text), -1);
        if (thisAt >= 0 && thisAt + 1 < header.Count && text(header[thisAt + 1]) == "[")
            return (DeclarationKind.Property, "this");

        if (Enumerable.Range(0, words.Count).Any(w => Keyword(w, "event")))
            return (DeclarationKind.Event, Declarators(pending, text)[0].Name);

        // With no parameter list, a body means a property. An expression body counts: "int P => 1;"
        // is a property, while "Func<int,int> F = x => x;" is a field whose value happens to be a
        // lambda, and the two are told apart by whether the header was cut at the arrow.
        return opensBody || truncated.ArrowLine >= 0
            ? (DeclarationKind.Property, words[^1])
            : (DeclarationKind.Field, Declarators(pending, text)[0].Name);
    }

    /// <summary>
    /// The names and initializer facts of a field or field-like event declaration, in source
    /// order — one per declarator, so <c>public int A, B = 1, C;</c> yields three entries and only
    /// B carries an initializer. Always at least one entry.
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
    /// matched speculatively by <see cref="TypeArgumentListEnds"/>, and the matched region is
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
    internal static List<Declarator> Declarators(
        List<ScanToken> pending,
        Func<ScanToken, string> text)
    {
        var declarators = new List<Declarator>();
        int[]? typeArgumentEnds = null;
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
                        typeArgumentEnds ??= TypeArgumentListEnds(pending, text);
                        int close = typeArgumentEnds[i];
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
                declarators.Add(new Declarator(name, sawEquals));
            start = i + 1;
            angle = 0;
            sawEquals = false;
        }

        if (declarators.Count == 0)
            declarators.Add(new Declarator("", false));
        return declarators;
    }

    /// <summary>
    /// For every <c>&lt;</c>, the index of the <c>&gt;</c> that closes a type-shaped argument
    /// list, or <c>-1</c>. Only tokens that can appear inside a type argument list are accepted,
    /// which is what makes a relational <c>&lt;</c> distinguishable:
    /// <c>x = a &lt; b, y = c &gt; d</c> contains an <c>=</c>, and no type argument list does.
    /// </summary>
    /// <remarks>
    /// The closing <c>&gt;</c> must also leave every <c>(</c> and <c>[</c> opened inside the region
    /// closed. A type argument list balances its groups — <c>Func&lt;(int a, int[] b), string&gt;</c>
    /// does — but <c>a &lt; b(name: c &gt; d)</c> does not, and accepting it would hand the caller a
    /// region that swallows the <c>(</c> and leaves the matching <c>)</c> behind to drive the
    /// caller's group depth negative, after which no later comma can ever separate a declarator.
    /// </remarks>
    private static int[] TypeArgumentListEnds(
        List<ScanToken> pending,
        Func<ScanToken, string> text)
    {
        var ends = new int[pending.Count];
        Array.Fill(ends, -1);
        var angles = new List<(int Index, int GroupDepth)>();
        int group = 0;
        for (int i = 0; i < pending.Count; i++)
        {
            var t = pending[i];
            if (t.Kind == ScanTokenKind.Word)
                continue;
            if (t.Kind != ScanTokenKind.Punctuator)
            {
                angles.Clear();
                continue;
            }

            var c = text(t);
            if (c == "<")
            {
                angles.Add((i, group));
                continue;
            }

            if (c == ">")
            {
                if (angles.Count == 0)
                    continue;
                var open = angles[^1];
                angles.RemoveAt(angles.Count - 1);
                if (group == open.GroupDepth)
                    ends[open.Index] = i;
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
            angles.Clear();
        }

        return ends;
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
    /// Whether the header is a C# 14 <c>extension</c> block, or is locally ambiguous with a
    /// constructor in a partial type named <c>extension</c>. The block may be generic, and its type
    /// parameter list sits between the keyword and the receiver:
    /// <c>extension&lt;T&gt;(IEnumerable&lt;T&gt; source)</c>. Testing only for a following
    /// <c>(</c> misses that form, and the cost is not one bad row -- the block is then indexed as a
    /// method, and every member inside it is rejected for sitting in a method rather than a type,
    /// so the extension members disappear from the index entirely.
    /// <para>
    /// The keyword is always the header's first token. An accessibility modifier is CS0106 and an
    /// attribute is CS7014, both measured against the compiler, so there is nothing for it to
    /// follow.
    /// </para>
    /// Delimiters nested inside a type-parameter attribute are balanced separately. Their
    /// punctuation is expression syntax, so a relational <c>&gt;</c> there cannot close the outer
    /// type-parameter list.
    /// </summary>
    internal static ExtensionScopeKind ClassifyExtensionScope(
        List<ScanToken> pending,
        ScopeContext enclosing,
        Func<ScanToken, string> text)
    {
        if (enclosing is not { HasEnclosing: true, Kind: DeclarationKind.Class }
            || pending.Count < 2
            || !IsKeyword(pending, 0, "extension", text))
            return ExtensionScopeKind.None;
        if (text(pending[1]) == "(")
        {
            // In a class named "extension", the plain form is also a constructor. A non-partial
            // declaration supplies enough local evidence to preserve the C# 13 interpretation.
            // A partial declaration does not: another part can make the aggregate type static,
            // in which case the same text is a C# 14 extension block. Keep that shape transparent
            // but unknown so neither interpretation can become successful source.
            // A conditional "static" token makes IsStatic true in the lexical union while leaving
            // the compiled type's staticness configuration-dependent. Test knownness first so
            // that union cannot turn constructor-shaped syntax into a trusted extension scope.
            // Gated by AConditionalStaticModifierKeepsConstructorShapedExtensionSyntaxAmbiguous.
            if (enclosing.Name == "extension" && !enclosing.StaticModifierKnown)
                return ExtensionScopeKind.Ambiguous;
            if (enclosing.Name == "extension" && !enclosing.IsStatic)
            {
                return enclosing.IsPartial
                    ? ExtensionScopeKind.Ambiguous
                    : ExtensionScopeKind.None;
            }
            return ExtensionScopeKind.Known;
        }
        if (text(pending[1]) != "<")
            return ExtensionScopeKind.None;

        int angle = 0;
        var groups = new List<string>();
        for (int i = 1; i < pending.Count; i++)
        {
            if (pending[i].Kind != ScanTokenKind.Punctuator)
                continue;
            var c = text(pending[i]);
            if (c is "(" or "[" or "{")
            {
                groups.Add(c);
                continue;
            }
            if (c is ")" or "]" or "}")
            {
                string expected = c switch
                {
                    ")" => "(",
                    "]" => "[",
                    _ => "{",
                };
                if (groups.Count == 0 || groups[^1] != expected)
                    return ExtensionScopeKind.Ambiguous;
                groups.RemoveAt(groups.Count - 1);
                continue;
            }
            if (groups.Count > 0 || c is not ("<" or ">"))
                continue;
            angle += c == "<" ? 1 : -1;
            if (angle == 0)
            {
                return i + 1 < pending.Count && text(pending[i + 1]) == "("
                    ? ExtensionScopeKind.Known
                    : ExtensionScopeKind.None;
            }
        }

        // The header began with the exact generic extension shape but did not close its type
        // parameter list. Letting ordinary method classification adopt the following body can
        // make that malformed wrapper the trusted source for a nested member. Gated by
        // AnIncompleteGenericExtensionHeaderDoesNotBecomeATrustedOuterMethod.
        return ExtensionScopeKind.Ambiguous;
    }

    /// <summary>
    /// Whether the header declares a delegate type. A function pointer spells the same keyword —
    /// <c>delegate*&lt;int, int&gt;</c> — as a return or parameter type, and the <c>*</c> is what
    /// tells them apart.
    /// </summary>
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