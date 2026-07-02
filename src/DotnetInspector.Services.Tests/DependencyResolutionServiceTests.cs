using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public class DependencyResolutionServiceTests
{
    [Theory]
    [InlineData("net9.0", "net8.0")]
    [InlineData("net8.0", "netcoreapp3.1")]
    [InlineData("netcoreapp3.1", "netstandard2.1")]
    [InlineData("netstandard2.1", "netstandard2.0")]
    [InlineData("netstandard2.0", "net472")]
    public void TfmResolver_GetTfmPriority_OrdersCorrectly(string higher, string lower)
    {
        Assert.True(TfmResolver.GetTfmPriority(higher) > TfmResolver.GetTfmPriority(lower));
    }

    [Fact]
    public void TfmSelector_SelectHighestTfm_UsesSharedPriorityPolicy()
    {
        var tfms = new[] { "netstandard2.0", "net472", "net8.0", "net10.0" };

        Assert.Equal("net10.0", TfmSelector.SelectHighestTfm(tfms));
    }

    [Fact]
    public void TfmSelector_SelectHighestTfm_NormalizesLongFormTfms()
    {
        var tfms = new[] { ".NETFramework4.7.2", ".NETStandard2.0", ".NETCoreApp,Version=v8.0" };

        Assert.Equal(".NETCoreApp,Version=v8.0", TfmSelector.SelectHighestTfm(tfms));
    }

    [Fact]
    public void TfmSelector_OrderByTfmPriorityDescending_PreservesCallerTieBreakers()
    {
        var tfms = new[] { "net8.0-windows", "net8.0", "netstandard2.0" };

        var ordered = TfmSelector.OrderByTfmPriorityDescending(tfms, tfm => tfm)
            .ThenBy(tfm => tfm, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(["net8.0", "net8.0-windows", "netstandard2.0"], ordered);
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
