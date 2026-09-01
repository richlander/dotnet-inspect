namespace CSharpText.Tests;

public sealed class CSharpKeywordsTests
{
    public static TheoryData<string> DeclarationContextualKeywords => new()
    {
        "await", "file", "init", "record", "required", "scoped",
    };

    [Theory]
    [MemberData(nameof(DeclarationContextualKeywords))]
    public void DeclarationPolicy_EscapesConservativeContextualSet(string identifier)
        => Assert.True(CSharpKeywords.RequiresDeclarationEscape(identifier));

    [Fact]
    public void TypeDeclarationPolicy_AddsExtensionWithoutBroadeningOtherDeclarations()
    {
        Assert.False(CSharpKeywords.RequiresDeclarationEscape("extension"));
        Assert.True(CSharpKeywords.RequiresTypeDeclarationEscape("extension"));
    }

    [Theory]
    [InlineData("await", true)]
    [InlineData("file", false)]
    [InlineData("init", false)]
    [InlineData("record", false)]
    [InlineData("required", false)]
    [InlineData("scoped", false)]
    public void BodyPolicy_PreservesDeclarationOnlyContextualIdentifiers(string identifier, bool expected)
        => Assert.Equal(expected, CSharpKeywords.RequiresBodyEscape(identifier));
}
