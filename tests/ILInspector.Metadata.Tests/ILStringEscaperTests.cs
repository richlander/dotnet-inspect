namespace ILInspector.Metadata.Tests;

public sealed class ILStringEscaperTests
{
    [Fact]
    public void ForDisplayEscapesOnlyUnpairedSurrogates()
    {
        string astral = char.ConvertFromUtf32(0x1F600);
        string input = $"{astral}\uD800x\uDC00";

        string escaped = ILStringEscaper.ForDisplay(input);

        Assert.StartsWith(astral, escaped, StringComparison.Ordinal);
        Assert.EndsWith("\\uD800x\\uDC00", escaped, StringComparison.Ordinal);
        Assert.DoesNotContain('\uD800', escaped[astral.Length..]);
        Assert.DoesNotContain('\uDC00', escaped[astral.Length..]);
    }
}
