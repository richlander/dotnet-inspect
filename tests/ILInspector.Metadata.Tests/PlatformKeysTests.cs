namespace ILInspector.Metadata.Tests;

public class PlatformKeysTests
{
    [Theory]
    [InlineData("b77a5c561934e089")] // ECMA / .NET Framework
    [InlineData("b03f5f7f11d50a3a")] // Microsoft (.NET ref assemblies)
    [InlineData("7cec85d7bea7798e")] // System.Private.CoreLib
    [InlineData("B77A5C561934E089")] // case-insensitive
    public void IsPlatform_IsTrue_ForTrustedKeyTokens(string token)
        => Assert.True(PlatformKeys.IsPlatform(token));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef")] // an arbitrary third-party key
    [InlineData("cafebabecafebabe")]
    public void IsPlatform_IsFalse_ForUntrustedOrMissingTokens(string? token)
        => Assert.False(PlatformKeys.IsPlatform(token));
}
