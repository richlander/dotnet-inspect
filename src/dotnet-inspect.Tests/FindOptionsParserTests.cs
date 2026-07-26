using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class FindOptionsParserTests
{
    private static IEnumerable<string> FindTipArgs(IEnumerable<Tip> tips)
        => tips.Where(t => t.Subcommand == FindCommand.Name).Select(t => t.Args);

    [Fact]
    public void BuildTips_MemberMode_PreservesLensAndCanonicalizesPattern()
    {
        var explicitTips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = "Serialize", Members = true }, "Serialize");
        var dotTips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = ".Serialize", Members = true }, ".Serialize");

        // Every find tip re-enables the member lens so following it does not silently revert to type
        // search...
        Assert.NotEmpty(FindTipArgs(explicitTips));
        Assert.All(FindTipArgs(explicitTips), args => Assert.Contains("--members", args));

        // ...and the explicit-flag and leading-dot forms produce identical tips.
        Assert.Equal(FindTipArgs(dotTips), FindTipArgs(explicitTips));
    }

    [Fact]
    public void BuildTips_MemberMode_PreservesConstructorPattern()
    {
        var tips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = ".ctor", Members = true }, ".ctor");

        // ".ctor" must survive into the tip (not be stripped to "ctor"), so following the tip still
        // searches for constructors.
        Assert.All(FindTipArgs(tips), args => Assert.StartsWith(".ctor ", args));
    }

    [Fact]
    public void BuildTips_TypeMode_OmitsMembersFlag()
    {
        var tips = FindOptionsParser.BuildTips(
            new FindOptions { Pattern = "JsonSerializer", Members = false }, "JsonSerializer");

        Assert.All(FindTipArgs(tips), args => Assert.DoesNotContain("--members", args));
    }
}
