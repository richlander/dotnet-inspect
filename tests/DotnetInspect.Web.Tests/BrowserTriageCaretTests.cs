using System.Runtime.Versioning;
using DotnetInspect.Web.Interop.Analysis;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserTriageCaretTests
{
    [Theory]
    [InlineData("    object value = Box();", 19, 5)]
    [InlineData("    Call();", 4, 1)]
    [InlineData("    ThisIsALongExpressionNameThatPlacesTheCallPastColumnForty();", 59, 2)]
    public void CaretPointsAtTheExactSourceExtent(string source, int column, int length)
    {
        string caret = AnalysisExports.TriageCaret(source, column, length);
        Assert.Equal(column, caret.IndexOf('^'));
        Assert.Equal(length, caret.Count(character => character == '^'));
        Assert.DoesNotContain('\n', caret);
    }

    [Theory]
    [InlineData("    Call();", -1, 1)]
    [InlineData("    Call();", 4, 0)]
    [InlineData("    Call();", 4, 100)]
    public void InvalidExtentsAreRejected(string source, int column, int length)
        => Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisExports.TriageCaret(source, column, length));

    [Fact]
    public void WholeBodiesAreRejected()
        => Assert.Throws<ArgumentException>(() => AnalysisExports.TriageCaret("Call();\nOther();", 0, 1));
}
