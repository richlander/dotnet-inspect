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
}
