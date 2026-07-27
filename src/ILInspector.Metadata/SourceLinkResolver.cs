using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.RegularExpressions;
using SLF = SourceLinkFetch;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves types and members to their source file locations using SourceLink information from PDBs.
/// Delegates URL mapping to the SourceLinkFetch library.
/// </summary>
public class SourceLinkResolver
{
    private readonly SLF.SourceLinkResolver _slfResolver;

    // Lazily-built per-reader indexes so batched enrichment (one ResolveTypeSource call per API
    // type) does not re-scan every TypeDefinition / PDB Document row for each type. Keyed by reader
    // instance because the metadata/pdb readers are passed in per call.
    private MetadataReader? _typeIndexReader;
    private Dictionary<string, TypeDefinitionHandle>? _fullNameIndex;
    private Dictionary<string, TypeDefinitionHandle>? _simpleNameIndex;
    private MetadataReader? _docIndexReader;
    private Dictionary<string, List<string>>? _docsByFirstSegment;

    public enum SourceResolutionMethod
    {
        /// <summary>Source resolved from method debug info (sequence points).</summary>
        SourceLink,
        /// <summary>Source inferred from PDB document name matching type name.</summary>
        Inferred
    }

    public record TypeSourceInfo(
        string? SourceFilePath,
        string? SourceUrl,
        int? LineNumber,
        string? GitHubBrowseUrl,
        SourceResolutionMethod ResolutionMethod = SourceResolutionMethod.SourceLink
    )
    {
        /// <summary>
        /// Additional source files for partial types (e.g., JObject.Async.cs alongside JObject.cs).
        /// Only populated when type has multiple source files.
        /// </summary>
        public List<PartialSourceFile> AdditionalSourceFiles { get; init; } = [];

        /// <summary>
        /// Indicates whether this type is defined across multiple partial files.
        /// </summary>
        public bool IsPartialType => AdditionalSourceFiles.Count > 0;
    }

    /// <summary>
    /// Represents a source file that is part of a partial type definition.
    /// </summary>
    public record PartialSourceFile(
        string FilePath,
        string? SourceUrl,
        string? GitHubBrowseUrl
    );

    /// <summary>
    /// Source location for a method, including the full line range from sequence points.
    /// <paramref name="Checksum"/> is the portable-PDB document hash and
    /// <paramref name="ChecksumAlgorithm"/> its algorithm name (e.g. "SHA256"); both may be null
    /// when the PDB records no document hash. They let callers authenticate a local source file
    /// on disk before preferring it over the remote SourceLink URL.
    /// </summary>
    public record MethodSourceInfo(
        string FilePath,
        string? SourceUrl,
        int StartLine,
        int EndLine,
        byte[]? Checksum = null,
        string? ChecksumAlgorithm = null
    );

    /// <summary>
    /// Reconstructs a method's source text from the full file <paramref name="sourceText"/> and the
    /// sequence-point line range (<paramref name="startLine"/>..<paramref name="endLine"/>, 1-based).
    /// Sequence points cover the body, so this scans backward to capture the signature (skipping
    /// doc comments, attributes, and preprocessor lines) and forward to include the closing brace,
    /// then dedents the block. Line numbers outside the file bounds surface as an
    /// <see cref="IndexOutOfRangeException"/>, which callers already handle by treating the source
    /// as unavailable.
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
    public static string ExtractMethodBody(string sourceText, int startLine, int endLine, string methodName, bool isDestructor = false, string? destructorTypeName = null)
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

        // A declaration whose range already terminates on its last line — an expression body's
        // ";" or an auto-property's "{ get; set; }" — owns no trailing brace to recover, so the
        // next "}" below it closes the enclosing type instead (issue #3278). A range that still
        // has a block open does own one, even when its last line ends in ";": a signature whose
        // "{" sits on the declaration line ends its sequence range on the last statement.
        bool endsAtDeclaration = startsAtDeclaration && end >= 1 && end <= lines.Length
            && IsSelfTerminatingLine(lines[end - 1])
            && !HasUnclosedBlock(lines, from, to);

        // Scan forward to include the closing brace.
        for (int i = to; !endsAtDeclaration && i < Math.Min(to + 3, lines.Length); i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("}"))
            {
                to = i + 1;
                break;
            }
            if (trimmed.Length > 0)
                break;
        }

        if (from < 0) from = 0;
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
    /// True when <paramref name="line"/> ends a declaration outright, so no trailing brace has
    /// to be recovered by the forward scan in <see cref="ExtractMethodBody"/>.
    /// </summary>
    private static bool IsSelfTerminatingLine(string line)
    {
        var trimmed = line.TrimEnd();
        return trimmed.EndsWith(';') || trimmed.EndsWith('}');
    }

    /// <summary>
    /// True when the captured range <c>[from, to)</c> opens more blocks than it closes, so a
    /// closing brace still has to be recovered below it. Braces inside comments, char literals,
    /// and string literals do not count — a property such as <c>public string M =&gt; "{";</c>
    /// closes nothing and owns no brace below it. A raw string literal is not tracked; the range
    /// is reported as still open so the forward scan runs, which is the conservative answer.
    /// A single line is never judged unclosed, so a one-line declaration needs no such analysis.
    /// </summary>
    private static bool HasUnclosedBlock(string[] lines, int from, int to)
    {
        if (to - from <= 1)
            return false;

        int depth = 0;
        bool inBlockComment = false;
        bool inVerbatimString = false;

        for (int i = Math.Max(0, from); i < Math.Min(to, lines.Length); i++)
        {
            var line = lines[i];
            for (int j = 0; j < line.Length; j++)
            {
                char c = line[j];

                if (inBlockComment)
                {
                    if (c == '*' && j + 1 < line.Length && line[j + 1] == '/')
                    {
                        inBlockComment = false;
                        j++;
                    }
                    continue;
                }

                if (inVerbatimString)
                {
                    if (c != '"')
                        continue;
                    if (j + 1 < line.Length && line[j + 1] == '"')
                        j++;
                    else
                        inVerbatimString = false;
                    continue;
                }

                if (c == '/' && j + 1 < line.Length)
                {
                    if (line[j + 1] == '/')
                        break;
                    if (line[j + 1] == '*')
                    {
                        inBlockComment = true;
                        j++;
                        continue;
                    }
                }

                if (c == '@' && j + 1 < line.Length && line[j + 1] == '"')
                {
                    inVerbatimString = true;
                    j++;
                    continue;
                }

                if (c == '"')
                {
                    if (j + 2 < line.Length && line[j + 1] == '"' && line[j + 2] == '"')
                        return true;

                    j++;
                    while (j < line.Length && line[j] != '"')
                        j += line[j] == '\\' ? 2 : 1;
                    continue;
                }

                if (c == '\'')
                {
                    j++;
                    while (j < line.Length && line[j] != '\'')
                        j += line[j] == '\\' ? 2 : 1;
                    continue;
                }

                if (c == '{')
                    depth++;
                else if (c == '}')
                    depth--;
            }
        }

        return depth > 0;
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

    private SourceLinkResolver(SLF.SourceLinkResolver slfResolver)
    {
        _slfResolver = slfResolver;
    }

    /// <summary>
    /// Creates a SourceLinkResolver from a PDB metadata reader.
    /// Returns null if no SourceLink information is available.
    /// </summary>
    public static SourceLinkResolver? Create(MetadataReader pdbReader)
    {
        var slfResolver = SLF.SourceLinkResolver.Create(pdbReader);
        if (slfResolver is null)
            return null;

        return new SourceLinkResolver(slfResolver);
    }

    /// <summary>
    /// Resolves source information for a type by finding a method with debug info.
    /// Falls back to document name matching for interfaces/abstract types without implementations.
    /// Also collects all source files for partial types.
    /// </summary>
    public TypeSourceInfo? ResolveTypeSource(MetadataReader metadata, MetadataReader pdb, TypeDefinitionHandle typeHandle)
    {
        var typeDef = metadata.GetTypeDefinition(typeHandle);
        var typeName = metadata.GetString(typeDef.Name);

        // Collect ALL unique source files from all methods of this type
        var allSourceFiles = CollectAllSourceFiles(metadata, pdb, typeHandle);

        // Also check PDB documents for files matching the type name pattern
        // This catches files that may not have any methods (e.g., partial with only fields)
        var documentFiles = FindDocumentsMatchingTypeName(pdb, typeName);
        foreach (var docFile in documentFiles)
        {
            if (!allSourceFiles.ContainsKey(docFile.FilePath))
            {
                allSourceFiles[docFile.FilePath] = docFile;
            }
        }

        if (allSourceFiles.Count == 0)
        {
            // Fallback: search all documents for a file that matches the type name
            // This works for interfaces and abstract types that have no method implementations
            return ResolveTypeSourceByDocumentName(pdb, typeName);
        }

        // Determine the primary file (prefer {TypeName}.cs over {TypeName}.*.cs)
        var primaryFile = SelectPrimarySourceFile(allSourceFiles.Values.ToList(), typeName);

        // Build additional source files list (excluding primary)
        List<PartialSourceFile> additionalFiles = [];
        if (allSourceFiles.Count > 1)
        {
            additionalFiles = allSourceFiles.Values
                .Where(f => f.FilePath != primaryFile.FilePath)
                .Select(f => new PartialSourceFile(f.FilePath, f.SourceUrl, f.GitHubBrowseUrl))
                .ToList();
        }

        return new TypeSourceInfo(
            primaryFile.FilePath,
            primaryFile.SourceUrl,
            null, // Line number not meaningful for type-level
            primaryFile.GitHubBrowseUrl,
            SourceResolutionMethod.SourceLink
        )
        {
            AdditionalSourceFiles = additionalFiles
        };
    }

    /// <summary>
    /// Resolves source information for a type by name, without requiring a TypeDefinitionHandle.
    /// Finds the type definition handle internally.
    /// </summary>
    public TypeSourceInfo? ResolveTypeSource(MetadataReader metadata, MetadataReader pdb, string typeName)
    {
        var typeHandle = FindTypeDefinitionHandle(metadata, typeName);
        if (typeHandle == null)
            return null;

        return ResolveTypeSource(metadata, pdb, typeHandle.Value);
    }

    /// <summary>
    /// Extracts the repository URL from SourceLink document mappings.
    /// </summary>
    public string? ExtractRepositoryUrl()
        => _slfResolver.ExtractRepositoryUrl();

    /// <summary>
    /// Extracts the repository URL from a PDB reader's SourceLink information.
    /// </summary>
    public static string? ExtractRepositoryUrl(MetadataReader pdbReader)
    {
        var resolver = Create(pdbReader);
        return resolver?.ExtractRepositoryUrl();
    }

    /// <summary>
    /// Extracts the commit hash from SourceLink URL patterns.
    /// </summary>
    public string? ExtractCommitHash()
        => _slfResolver.ExtractCommitHash();

    /// <summary>
    /// Finds a TypeDefinitionHandle by type name, preferring a full-name match over a simple-name
    /// match. Uses a per-reader index so repeated lookups don't re-scan all TypeDefinitions.
    /// </summary>
    private TypeDefinitionHandle? FindTypeDefinitionHandle(MetadataReader reader, string typeName)
    {
        if (_typeIndexReader != reader || _fullNameIndex == null)
        {
            var fullNames = new Dictionary<string, TypeDefinitionHandle>(StringComparer.OrdinalIgnoreCase);
            var simpleNames = new Dictionary<string, TypeDefinitionHandle>(StringComparer.OrdinalIgnoreCase);
            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                // TryAdd preserves the original "first row wins" behavior on duplicate names.
                fullNames.TryAdd(reader.GetFullTypeName(typeDef), typeDefHandle);
                simpleNames.TryAdd(reader.GetString(typeDef.Name), typeDefHandle);
            }
            _fullNameIndex = fullNames;
            _simpleNameIndex = simpleNames;
            _typeIndexReader = reader;
        }

        if (_fullNameIndex.TryGetValue(typeName, out var handle))
            return handle;
        if (_simpleNameIndex!.TryGetValue(typeName, out handle))
            return handle;
        return null;
    }

    /// <summary>
    /// Collects all unique source files from all methods of a type.
    /// </summary>
    private Dictionary<string, PartialSourceFile> CollectAllSourceFiles(
        MetadataReader metadata, MetadataReader pdb, TypeDefinitionHandle typeHandle)
    {
        var sourceFiles = new Dictionary<string, PartialSourceFile>(StringComparer.OrdinalIgnoreCase);
        var typeDef = metadata.GetTypeDefinition(typeHandle);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var sourceInfo = ResolveMethodSource(pdb, methodHandle);
            if (sourceInfo?.SourceFilePath != null && !sourceFiles.ContainsKey(sourceInfo.SourceFilePath))
            {
                sourceFiles[sourceInfo.SourceFilePath] = new PartialSourceFile(
                    sourceInfo.SourceFilePath,
                    sourceInfo.SourceUrl,
                    sourceInfo.GitHubBrowseUrl
                );
            }
        }

        return sourceFiles;
    }

    /// <summary>
    /// Finds PDB documents matching the type name pattern (e.g., JObject.cs, JObject.Async.cs).
    /// </summary>
    private List<PartialSourceFile> FindDocumentsMatchingTypeName(MetadataReader pdb, string typeName)
    {
        // Index documents by the filename segment before the first '.', so {TypeName}.cs and
        // {TypeName}.*.cs both bucket under {TypeName}. Built once per PDB reader instead of
        // scanning every Document for each type.
        if (_docIndexReader != pdb || _docsByFirstSegment == null)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var docHandle in pdb.Documents)
            {
                string filePath = pdb.GetString(pdb.GetDocument(docHandle).Name);
                string fileName = Path.GetFileName(filePath);
                int firstDot = fileName.IndexOf('.');
                string segment = firstDot >= 0 ? fileName[..firstDot] : fileName;
                if (!index.TryGetValue(segment, out var list))
                    index[segment] = list = [];
                list.Add(filePath);
            }
            _docsByFirstSegment = index;
            _docIndexReader = pdb;
        }

        if (!_docsByFirstSegment.TryGetValue(typeName, out var candidates))
            return [];

        // Within the bucket the first segment already equals typeName, so the original pattern
        // reduces to "ends with .cs".
        List<PartialSourceFile> matches = [];
        foreach (var filePath in candidates)
        {
            if (!Path.GetFileName(filePath).EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);
            matches.Add(new PartialSourceFile(filePath, sourceUrl, browseUrl));
        }

        return matches;
    }

    /// <summary>
    /// Selects the primary source file from a list of candidates.
    /// Prefers {TypeName}.cs over {TypeName}.*.cs patterns.
    /// </summary>
    private static PartialSourceFile SelectPrimarySourceFile(List<PartialSourceFile> files, string typeName)
    {
        // Prefer exact match: {TypeName}.cs
        var primaryPattern = $"{typeName}.cs";
        var primary = files.FirstOrDefault(f =>
            Path.GetFileName(f.FilePath).Equals(primaryPattern, StringComparison.OrdinalIgnoreCase));

        if (primary != null)
            return primary;

        // Otherwise, return the first one (or the one with shortest name)
        return files.OrderBy(f => Path.GetFileName(f.FilePath).Length).First();
    }

    /// <summary>
    /// Attempts to resolve source info by searching PDB documents for a matching file name.
    /// Used for interfaces and types without method implementations.
    /// </summary>
    private TypeSourceInfo? ResolveTypeSourceByDocumentName(MetadataReader pdb, string typeName)
    {
        foreach (var docHandle in pdb.Documents)
        {
            var document = pdb.GetDocument(docHandle);
            string filePath = pdb.GetString(document.Name);

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                string? sourceUrl = ApplySourceLinkMapping(filePath);
                string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);
                return new TypeSourceInfo(filePath, sourceUrl, null, browseUrl, SourceResolutionMethod.Inferred);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves source information for a specific method.
    /// </summary>
    public TypeSourceInfo? ResolveMethodSource(MetadataReader pdb, MethodDefinitionHandle methodHandle)
    {
        var debugInfoHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));

        try
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugInfoHandle);

            if (debugInfo.Document.IsNil)
                return null;

            var document = pdb.GetDocument(debugInfo.Document);
            string filePath = pdb.GetString(document.Name);

            int? lineNumber = null;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (!sp.IsHidden)
                {
                    lineNumber = sp.StartLine;
                    break;
                }
            }

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);

            return new TypeSourceInfo(filePath, sourceUrl, lineNumber, browseUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the full source line range for a method using all sequence points.
    /// Returns null if the method has no debug info.
    /// </summary>
    public MethodSourceInfo? ResolveMethodSourceRange(MetadataReader pdb, MethodDefinitionHandle methodHandle)
    {
        var debugInfoHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));

        try
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugInfoHandle);

            if (debugInfo.Document.IsNil)
                return null;

            var document = pdb.GetDocument(debugInfo.Document);
            string filePath = pdb.GetString(document.Name);

            int minLine = int.MaxValue, maxLine = 0;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.IsHidden) continue;
                if (sp.StartLine < minLine) minLine = sp.StartLine;
                if (sp.EndLine > maxLine) maxLine = sp.EndLine;
            }

            if (minLine == int.MaxValue)
                return null;

            byte[]? checksum = null;
            string? checksumAlgorithm = null;
            if (!document.Hash.IsNil)
            {
                checksum = pdb.GetBlobBytes(document.Hash);
                checksumAlgorithm = PdbContext.MapHashAlgorithm(pdb.GetGuid(document.HashAlgorithm));
            }

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            return new MethodSourceInfo(filePath, sourceUrl, minLine, maxLine, checksum, checksumAlgorithm);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Source location resolved from a method token and IL offset.
    /// </summary>
    public record ILOffsetSourceInfo(
        string? MethodName,
        string FilePath,
        string? SourceUrl,
        int Line,
        int MatchedOffset,
        string? GitHubBrowseUrl);

    /// <summary>
    /// Resolves source file and line number from a method token and IL offset
    /// by walking PDB sequence points. Applies SourceLink URL mapping when available.
    /// </summary>
    public ILOffsetSourceInfo? ResolveByILOffset(MetadataReader metadata, MetadataReader pdb, int methodToken, int ilOffset)
    {
        if (ResolveByILOffsetDirect(metadata, pdb, methodToken, ilOffset) is not { } info)
            return null;

        string? sourceUrl = ApplySourceLinkMapping(info.FilePath);
        return info with { SourceUrl = sourceUrl, GitHubBrowseUrl = ConvertToGitHubBrowseUrl(sourceUrl) };
    }

    /// <summary>
    /// Resolves source file and line number from a method token and IL offset by walking
    /// PDB sequence points, returning the last visible point at or before the requested offset.
    /// Uses only the PDB reader (no SourceLink URL mapping); used when no resolver is available.
    /// </summary>
    public static ILOffsetSourceInfo? ResolveByILOffsetDirect(MetadataReader metadata, MetadataReader pdb, int methodToken, int ilOffset)
    {
        try
        {
            var handle = MetadataTokens.Handle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return null;

            var methodDefHandle = (MethodDefinitionHandle)handle;

            var methodDef = metadata.GetMethodDefinition(methodDefHandle);
            var typeDef = metadata.GetTypeDefinition(methodDef.GetDeclaringType());
            string methodName = $"{metadata.GetFullTypeName(typeDef)}.{metadata.GetString(methodDef.Name)}";

            var debugInfo = pdb.GetMethodDebugInformation(methodDefHandle.ToDebugInformationHandle());
            if (debugInfo.SequencePointsBlob.IsNil)
                return null;

            SequencePoint? bestPoint = null;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.Offset > ilOffset)
                    break;

                if (!sp.IsHidden)
                    bestPoint = sp;
            }

            if (bestPoint is not { } point)
                return null;

            var document = pdb.GetDocument(point.Document);
            string filePath = pdb.GetString(document.Name);

            return new ILOffsetSourceInfo(methodName, filePath, null, point.StartLine, point.Offset, null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies SourceLink URL pattern to convert a file path to a source URL.
    /// </summary>
    public string? ApplySourceLinkMapping(string filePath)
        => _slfResolver.ResolveUrl(filePath);

    /// <summary>
    /// Converts a raw.githubusercontent.com URL to a github.com browse URL.
    /// </summary>
    private static string? ConvertToGitHubBrowseUrl(string? rawUrl)
        => SLF.SourceLinkResolver.ConvertToGitHubBrowseUrl(rawUrl);

}
