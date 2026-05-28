using DotnetInspector.Commands;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for the IL offset token+offset parser used by the source --il-offset option.
/// </summary>
public class ILOffsetParserTests
{
    [Theory]
    [InlineData("0x6000001+0x5", 0x6000001, 0x5)]
    [InlineData("0x06000001+0x0005", 0x06000001, 0x0005)]
    [InlineData("0x6000004+0x15", 0x6000004, 0x15)]
    [InlineData("0x6000002+0x0", 0x6000002, 0x0)]
    public void ValidTokenAndOffset_ParsesCorrectly(string input, int expectedToken, int expectedOffset)
    {
        Assert.True(SourceCommand.TryParseILOffset(input, out var token, out var offset));
        Assert.Equal(expectedToken, token);
        Assert.Equal(expectedOffset, offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x6000001")]           // missing +offset
    [InlineData("0x6000001-0x5")]       // wrong separator
    [InlineData("0x6000001:0x5")]       // wrong separator
    [InlineData("0x2000001+0x5")]       // TypeDef table (0x02), not MethodDef (0x06)
    [InlineData("0x0A000001+0x5")]      // MemberRef table (0x0A), not MethodDef
    [InlineData("garbage+0x5")]         // non-numeric token
    [InlineData("0x6000001+garbage")]   // non-numeric offset
    public void InvalidInput_ReturnsFalse(string input)
    {
        Assert.False(SourceCommand.TryParseILOffset(input, out _, out _));
    }

    [Fact]
    public void ZeroILOffset_IsValid()
    {
        Assert.True(SourceCommand.TryParseILOffset("0x6000001+0x0", out var token, out var offset));
        Assert.Equal(0x6000001, token);
        Assert.Equal(0, offset);
    }
}
