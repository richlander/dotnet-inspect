using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// C# defines five line-terminator characters (ECMA-334 §6.3.1): CR, LF, NEL
// (U+0085), LINE SEPARATOR (U+2028), and PARAGRAPH SEPARATOR (U+2029). The first
// three are `char.IsControl` and were already escaped; U+2028/U+2029 are not, so
// a raw emit splits the string literal across source lines (CS1010). The printer
// must escape them.
public class StringLiteralEscapingTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void LineAndParagraphSeparators_AreEscaped()
    {
        var output = Render(nameof(CfgSampleClass.LineSeparatorLiteral));

        Assert.Contains("\\u2028", output);
        Assert.Contains("\\u2029", output);
        Assert.DoesNotContain("\u2028", output);
        Assert.DoesNotContain("\u2029", output);
    }
}
