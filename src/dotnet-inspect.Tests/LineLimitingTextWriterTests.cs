using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class LineLimitingTextWriterTests
{
    [Fact]
    public void Write_ReachingLimitWithinString_DiscardsLaterWrites()
    {
        var output = new StringWriter();
        var writer = new LineLimitingTextWriter(output, maxLines: 4);

        writer.Write("# Library\n\n## Library Info\n\n");
        writer.WriteLine();

        Assert.Equal("# Library\n\n## Library Info\n\n", output.ToString());
    }

    [Fact]
    public void WriteLine_ReachingLimitWithinValue_DiscardsLaterWrites()
    {
        var output = new StringWriter();
        var writer = new LineLimitingTextWriter(output, maxLines: 2);

        writer.WriteLine("first\nsecond\nthird");
        writer.WriteLine("fourth");

        Assert.Equal("first\nsecond\n", output.ToString());
    }

    [Fact]
    public void TailFlush_CrlfInput_DoesNotDuplicateCarriageReturns()
    {
        var output = new StringWriter { NewLine = "\r\n" };
        var writer = new TailLineLimitingTextWriter(output, maxLines: 2);

        writer.Write("first\r\nsecond\r\nthird\r\n");
        writer.FlushTail();

        Assert.Equal("second\r\nthird\r\n", output.ToString());
    }

    [Fact]
    public void TailFlush_UnterminatedTrailingCarriageReturn_PreservesContent()
    {
        var output = new StringWriter { NewLine = "\r\n" };
        var writer = new TailLineLimitingTextWriter(output, maxLines: 1);

        writer.Write("content\r");
        writer.FlushTail();

        Assert.Equal("content\r\r\n", output.ToString());
    }
}
