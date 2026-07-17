namespace ILInspector.CSharp.Tests;

public sealed class CSharpIdentifierTests
{
    [Theory]
    [InlineData("return", "@return")]
    [InlineData("class", "@class")]
    [InlineData("await", "@await")] // contextual, but illegal bare inside async bodies
    [InlineData("value", "value")]
    [InlineData("Foo", "Foo")]
    public void Escape_EscapesReservedKeywordsOnly(string input, string expected)
        => Assert.Equal(expected, CSharpIdentifier.Escape(input));

    [Theory]
    // Declaration-only contextual keywords are legal bare identifiers in expression
    // and body position, so the position-agnostic producer must NOT escape them.
    // Declaration-position escaping (CSharpDeclarationWriter.EscapeIdentifier) does.
    [InlineData("record")]
    [InlineData("required")]
    [InlineData("init")]
    [InlineData("file")]
    [InlineData("scoped")]
    public void Escape_LeavesDeclarationOnlyContextualKeywordsBare(string input)
        => Assert.Equal(input, CSharpIdentifier.Escape(input));

    [Theory]
    [InlineData("Foo", "Foo")]                       // already spellable -> unchanged
    [InlineData("return", "@return")]                // identifier-like keyword -> escaped
    [InlineData("<>c__DisplayClass0_0", "___c__DisplayClass0_0")] // unspellable -> sanitized (prefix + <,> -> _)
    [InlineData("<Foo>k__BackingField", "__Foo_k__BackingField")]
    [InlineData("a-b", "a_b")]                       // hyphen -> underscore
    [InlineData("1Leading", "_1Leading")]            // leading digit -> underscore prefix
    [InlineData("", "_")]                            // empty -> underscore
    public void Sanitize_ProducesSpellableIdentifier(string input, string expected)
        => Assert.Equal(expected, CSharpIdentifier.Sanitize(input));

    [Theory]
    [InlineData("<>c", "___c")]
    [InlineData("has space", "has_space")]
    [InlineData("_ok", "_ok")]
    public void SanitizeUnspellable_ReplacesNonIdentifierCharacters(string input, string expected)
        => Assert.Equal(expected, CSharpIdentifier.SanitizeUnspellable(input));

    [Theory]
    [InlineData("Foo", true)]
    [InlineData("_x", true)]
    [InlineData("return", false)]   // escapable keyword is not usable bare
    [InlineData("1x", false)]       // leading digit
    [InlineData("a-b", false)]      // non-identifier char
    [InlineData("", false)]
    public void IsUsable_RejectsKeywordsAndNonIdentifiers(string input, bool expected)
        => Assert.Equal(expected, CSharpIdentifier.IsUsable(input));

    [Theory]
    [InlineData("return", true)]    // escapable keyword IS an escapable identifier
    [InlineData("Foo", true)]
    [InlineData("a-b", false)]
    [InlineData("<>c", false)]
    public void IsEscapable_RecognizesIdentifierShape(string input, bool expected)
        => Assert.Equal(expected, CSharpIdentifier.IsEscapable(input));

    [Theory]
    [InlineData("Foo", true)]
    [InlineData("café", true)]      // Unicode letter is identifier-like
    [InlineData("<>c", false)]
    [InlineData("", false)]
    public void IsIdentifierLike_UsesUnicodeGrammar(string input, bool expected)
        => Assert.Equal(expected, CSharpIdentifier.IsIdentifierLike(input));
}
