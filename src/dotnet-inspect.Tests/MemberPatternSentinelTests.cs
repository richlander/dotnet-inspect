using DotnetInspector.Options;

namespace DotnetInspector.Tests;

public class MemberPatternSentinelTests
{
    [Theory]
    [InlineData(".Serialize", "Serialize")]   // leading-dot sentinel is stripped
    [InlineData("Serialize", "Serialize")]    // no sentinel, unchanged
    [InlineData(".ctor", ".ctor")]            // constructor name preserved
    [InlineData(".cctor", ".cctor")]          // static constructor name preserved
    [InlineData(".", "")]                     // bare sentinel strips to empty
    [InlineData("..ctor", ".ctor")]           // only a single leading dot is stripped
    public void Strip_HandlesSentinelAndConstructorNames(string input, string expected)
        => Assert.Equal(expected, MemberPatternSentinel.Strip(input));
}
