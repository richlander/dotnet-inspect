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
    [InlineData(".ctor*", "ctor*")]           // glob is not an exact ctor name -> sentinel stripped
    [InlineData(".cctor*", "cctor*")]         // glob is not an exact ctor name -> sentinel stripped
    [InlineData(".ctorInfo", "ctorInfo")]     // not a constructor; sentinel stripped
    [InlineData(".c*", "c*")]                  // member-lens glob, not a ctor query -> strip
    [InlineData(".*", "*")]                    // member-lens glob -> strip
    [InlineData(".*ctor", "*ctor")]            // member-lens glob -> strip
    [InlineData(".?ctor", "?ctor")]            // member-lens glob -> strip
    [InlineData(".?????", "?????")]            // member-lens glob -> strip
    [InlineData(".", "")]                     // bare sentinel strips to empty
    [InlineData("..ctor", ".ctor")]           // only a single leading dot is stripped
    public void Strip_HandlesSentinelAndConstructorNames(string input, string expected)
        => Assert.Equal(expected, MemberPatternSentinel.Strip(input));
}
