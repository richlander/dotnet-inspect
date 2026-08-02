namespace DotnetInspector.CSharpBodySlicer;

/// <summary>
/// The lexical category of a <see cref="ScanToken"/>.
/// <para>
/// The set is deliberately coarse. It names the distinctions the slicer's predicates actually
/// ask about — is this text code, or is it commented out, quoted, or preprocessor — and stops
/// there. It does not classify operators, separate keywords from identifiers, or decompose
/// numeric literals, because nothing here needs those and inventing them would imply a fidelity
/// this scanner does not have.
/// </para>
/// </summary>
internal enum ScanTokenKind
{
    /// <summary>A run of identifier characters: a keyword, an identifier, or a numeric literal.</summary>
    Word,

    /// <summary>One significant character of code that is not part of a word.</summary>
    Punctuator,

    /// <summary>
    /// Text between a string's delimiters, delimiters included.
    /// <para>
    /// One token may span more than one literal. Adjacent literal text coalesces, and a literal
    /// that opens immediately inside another's interpolation hole is adjacent to it, so
    /// <c>$"a{$"b{</c> arrives as a single token. The guarantee is the complement rather than the
    /// extent: no character of code is ever inside one of these, because code is emitted as
    /// <see cref="Word"/> or <see cref="Punctuator"/> and so breaks the adjacency. Callers use
    /// this kind to ask "is this position code?", which it answers exactly; they must not use it
    /// to count literals or to find one literal's bounds.
    /// </para>
    /// </summary>
    StringLiteral,

    /// <summary>A single-quoted character literal, delimiters included.</summary>
    CharLiteral,

    /// <summary>Comment text: a line comment to end of line, or one line's worth of a block comment.</summary>
    Comment,

    /// <summary>A preprocessor directive. The whole line is one token.</summary>
    Directive,
}

/// <summary>
/// One lexical unit found by <see cref="BodySlicer"/>'s scanner, with the position and the
/// structural depth in effect where it sits.
/// <para>
/// A token never spans a line break. A construct that does — a block comment, a verbatim or raw
/// string literal — yields one token per line it covers, so a caller can always ask what is on a
/// given line without reconstructing the lexer's carried state. That carried state is what made
/// the predicates fragile: each one re-derived "am I inside a comment or a literal?" from text it
/// was handed, and a caller that forgot to thread the state got a plausible wrong answer.
/// </para>
/// </summary>
/// <param name="Kind">The lexical category.</param>
/// <param name="Line">Zero-based index of the line the token sits on.</param>
/// <param name="Column">Zero-based index of the token's first character within that line.</param>
/// <param name="Length">The token's length in characters. Always at least one.</param>
/// <param name="Depth">
/// The number of structural braces enclosing the token's first character. A <c>{</c> that opens a
/// block carries the depth outside it, and the matching <c>}</c> carries the depth inside, so both
/// delimiters report the depth of the text they bound rather than of each other.
/// </param>
/// <param name="BracketDepth">
/// The number of enclosing square brackets, which is how an attribute list spanning lines is told
/// from the code around it.
/// </param>
/// <param name="DepthKnown">
/// False where the scanner cannot vouch for <see cref="Depth"/>, which callers must then treat as
/// "do not know" rather than as zero. Two causes. An unterminated single-line literal loses the
/// place for the rest of the file. A conditional group loses it for the tokens inside the group,
/// because the branch being scanned may be the one the compiler discards, and recovers after an
/// <c>#endif</c> whose branches each returned to the depth the group opened at — every branch then
/// leaves the same depth behind, so it no longer matters which one compiles. A group that does not
/// meet that bar loses the place for the rest of the file, as before.
/// </param>
internal readonly record struct ScanToken(
    ScanTokenKind Kind,
    int Line,
    int Column,
    int Length,
    int Depth,
    int BracketDepth,
    bool DepthKnown)
{
    /// <summary>The index one past the token's last character, within its own line.</summary>
    public int End => Column + Length;

    /// <summary>The token's text, given the line it was found on.</summary>
    public ReadOnlySpan<char> TextIn(string line) => line.AsSpan(Column, Length);
}
