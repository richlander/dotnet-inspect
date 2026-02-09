namespace DotnetInspector.Services.Tests;

public class DependencyResolutionServiceTests
{
    [Theory]
    [InlineData("net9.0", 4)]
    [InlineData("net8.0", 4)]
    [InlineData("net6.0", 4)]
    [InlineData("netcoreapp3.1", 3)]
    [InlineData("netstandard2.0", 2)]
    [InlineData("netstandard2.1", 2)]
    [InlineData("net472", 1)]
    [InlineData("net461", 1)]
    [InlineData("unknown", 0)]
    public void GetTfmPriority_ReturnsExpectedPriority(string tfm, int expectedPriority)
    {
        Assert.Equal(expectedPriority, DependencyResolutionService.GetTfmPriority(tfm));
    }

    [Theory]
    [InlineData("net9.0", 9.0)]
    [InlineData("net8.0", 8.0)]
    [InlineData("netcoreapp3.1", 3.1)]
    [InlineData("netstandard2.0", 2.0)]
    [InlineData("netstandard2.1", 2.1)]
    public void ExtractTfmVersion_ReturnsExpectedVersion(string tfm, double expectedVersion)
    {
        Assert.Equal(expectedVersion, DependencyResolutionService.ExtractTfmVersion(tfm));
    }

    [Theory]
    [InlineData("[1.0.0, )", "1.0.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("[2.1.0, 3.0.0)", "2.1.0")]
    public void ResolveVersionFromRange_ReturnsMinVersion(string range, string expectedVersion)
    {
        Assert.Equal(expectedVersion, DependencyResolutionService.ResolveVersionFromRange(range));
    }

    [Fact]
    public void ResolveVersionFromRange_InvalidRange_ReturnsNull()
    {
        Assert.Null(DependencyResolutionService.ResolveVersionFromRange("not-a-version"));
    }

    [Fact]
    public void FindBestMatchingTfmGroup_ExactMatch()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net8.0" },
            new() { TargetFramework = "net9.0" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(groups, "net9.0");
        Assert.Equal("net9.0", result?.TargetFramework);
    }

    [Fact]
    public void FindBestMatchingTfmGroup_FallsBackToLowerTfm()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net6.0" },
            new() { TargetFramework = "net8.0" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(groups, "net9.0");
        Assert.Equal("net8.0", result?.TargetFramework);
    }

    [Fact]
    public void FindBestMatchingTfmGroup_FallsBackToAny()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "any" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(groups, "net9.0");
        Assert.Equal("any", result?.TargetFramework);
    }

    [Fact]
    public void FindBestMatchingTfmGroup_NoMatch_ReturnsNull()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net9.0" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(groups, "net6.0");
        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatchingTfmGroup_NetStandard_MatchesNetApp()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "netstandard2.0" },
            new() { TargetFramework = "netstandard2.1" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(groups, "net8.0");
        Assert.Equal("netstandard2.1", result?.TargetFramework);
    }

    [Fact]
    public void DependencyNode_Record_Properties()
    {
        var child = new DependencyNode("ChildPkg", "1.0.0", "Author1", []);
        var node = new DependencyNode("ParentPkg", "2.0.0", "Author2", [child]);

        Assert.Equal("ParentPkg", node.PackageId);
        Assert.Equal("2.0.0", node.Version);
        Assert.Equal("Author2", node.Author);
        Assert.Single(node.Children);
        Assert.Equal("ChildPkg", node.Children[0].PackageId);
    }
}
