namespace MsdlProxy.Tests;

public sealed class MsdlRequestValidatorTests
{
    [Theory]
    [InlineData("Foo.pdb")]
    [InlineData("Microsoft.Extensions.Http.pdb")]
    [InlineData("a.PDB")]
    public void IsValidPdbFileName_AcceptsSafeSingleSegmentPdbNames(string pdbFileName)
        => Assert.True(MsdlRequestValidator.IsValidPdbFileName(pdbFileName));

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../evil.pdb")]
    [InlineData("a/b.pdb")]
    [InlineData("a\\b.pdb")]
    [InlineData("a:b.pdb")]
    [InlineData("foo.exe")]
    [InlineData("foo")]
    [InlineData("/etc/passwd.pdb")]
    public void IsValidPdbFileName_RejectsUnsafeOrWrongExtensionNames(string pdbFileName)
        => Assert.False(MsdlRequestValidator.IsValidPdbFileName(pdbFileName));

    [Fact]
    public void IsValidPdbFileName_RejectsOverlongNames()
    {
        var name = new string('a', 300) + ".pdb";
        Assert.False(MsdlRequestValidator.IsValidPdbFileName(name));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdefFFFFFFFF")] // portable: guid(32) + FFFFFFFF(8)
    [InlineData("0123456789ABCDEF0123456789ABCDEF1")] // windows pdb: guid(32) + age(1), uppercase
    [InlineData("0123456789abcdef0123456789abcdef1a2b3c4d")] // windows pdb: guid(32) + age(8)
    public void IsValidSymbolKey_AcceptsExpectedShapes(string symbolKey)
        => Assert.True(MsdlRequestValidator.IsValidSymbolKey(symbolKey));

    [Theory]
    [InlineData("")]
    [InlineData("notahex")]
    [InlineData("0123456789abcdef0123456789abcdef")] // 32 chars: guid alone, no stamp
    [InlineData("0123456789abcdef0123456789abcdefFFFFFFFFFFFFFFFFFF")] // too long
    [InlineData("0123456789abcdef0123456789abcdefFFFFFFFG")] // non-hex trailing char
    [InlineData("../../etc/passwd")]
    public void IsValidSymbolKey_RejectsNonHexOrWrongLengthValues(string symbolKey)
        => Assert.False(MsdlRequestValidator.IsValidSymbolKey(symbolKey));
}
