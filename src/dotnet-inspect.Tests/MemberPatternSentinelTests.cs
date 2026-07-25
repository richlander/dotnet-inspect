using DotnetInspector.Inspectors;

namespace DotnetInspector.Tests;

public class MemberPatternSentinelTests
{
    [Theory]
    [InlineData(".Serialize", "Serialize")]   // leading-dot sentinel is stripped
    [InlineData("Serialize", "Serialize")]    // no sentinel, unchanged
    [InlineData(".ctor", ".ctor")]            // constructor name preserved
    [InlineData(".cctor", ".cctor")]          // static constructor name preserved
    [InlineData(".CTOR", ".CTOR")]            // constructor preserved case-insensitively
    [InlineData(".Cctor", ".Cctor")]          // static constructor preserved case-insensitively
    [InlineData(".ctor*", ".ctor*")]          // constructor glob preserved
    [InlineData(".cctor*", ".cctor*")]        // static constructor glob preserved
    [InlineData(".ctorInfo", "ctorInfo")]     // not a constructor; sentinel stripped
    [InlineData(".*", "*")]                    // stripped form still matches ctors, so strip
    [InlineData(".*ctor", "*ctor")]            // stripped form still matches ctors, so strip
    [InlineData(".", "")]                     // bare sentinel strips to empty
    [InlineData("..ctor", ".ctor")]           // only a single leading dot is stripped
    public void Strip_HandlesSentinelAndConstructorNames(string input, string expected)
        => Assert.Equal(expected, MemberPatternSentinel.Strip(input));
}
