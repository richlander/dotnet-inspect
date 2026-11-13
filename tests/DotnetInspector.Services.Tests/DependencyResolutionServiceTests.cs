using DotnetInspector.Cache;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

[Collection(PersistentCacheCollection.Name)]
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
    public void TfmSelector_GetTfmPriority_NormalizesLongFormTfms()
    {
        Assert.Equal(TfmResolver.GetTfmPriority("net8.0"), TfmSelector.GetTfmPriority(".NETCoreApp,Version=v8.0/linux-x64"));
        Assert.Equal(TfmResolver.GetTfmPriority("netstandard2.0"), TfmSelector.GetTfmPriority(".NETStandard2.0"));
        Assert.Equal(TfmResolver.GetTfmPriority("net472"), TfmSelector.GetTfmPriority(".NETFramework4.7.2"));
        Assert.Equal(TfmResolver.GetTfmPriority("net40"), TfmSelector.GetTfmPriority(".NETFramework,Version=v4.0,Profile=Client"));
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
    public void FindBestMatchingTfmGroup_LongFormGroupDoesNotExceedTarget()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net6.0" },
            new() { TargetFramework = ".NETCoreApp,Version=v8.0" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(groups, "net7.0");

        Assert.Equal("net6.0", result?.TargetFramework);
    }

    [Fact]
    public void FindBestMatchingTfmGroup_NormalizesLongFormTarget()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net6.0" },
            new() { TargetFramework = "net8.0" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(groups, ".NETCoreApp,Version=v8.0");

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
    public void FindBestMatchingTfmGroup_PrefersCompatibleFrameworkOverUniversal()
    {
        var groups = new List<DependencyGroup>
        {
            new() { TargetFramework = "any" },
            new() { TargetFramework = "netstandard2.0" }
        };

        var result = DependencyResolutionService.FindBestMatchingTfmGroup(
            groups,
            "net8.0");

        Assert.Equal("netstandard2.0", result?.TargetFramework);
    }

    [Fact]
    public void FindBestMatchingTfmGroup_PreservesLegacyExplicitGroupPrecedence()
    {
        NuspecData nuspec = NuspecParser.ParseContent(
            """
            <package>
              <metadata>
                <dependencies>
                  <dependency id="Before" version="1.0.0" />
                  <group targetFramework="any">
                    <dependency id="Middle" version="2.0.0" />
                  </group>
                  <dependency id="After" version="3.0.0" />
                </dependencies>
              </metadata>
            </package>
            """);

        DependencyGroup? result =
            DependencyResolutionService.FindBestMatchingTfmGroup(
                nuspec.DependencyGroups!,
                "net8.0");

        Assert.Equal(
            "Middle",
            Assert.Single(result!.Dependencies).Id);
    }

    [Fact]
    public void FindBestMatchingTfmGroup_MergesLegacyImplicitDependencies()
    {
        NuspecData nuspec = NuspecParser.ParseContent(
            """
            <package>
              <metadata>
                <dependencies>
                  <dependency id="Before" version="1.0.0" />
                  <group targetFramework="net9.0" />
                  <dependency id="After" version="3.0.0" />
                </dependencies>
              </metadata>
            </package>
            """);

        DependencyGroup? result =
            DependencyResolutionService.FindBestMatchingTfmGroup(
                nuspec.DependencyGroups!,
                "net8.0");

        Assert.Equal(
            ["Before", "After"],
            result!.Dependencies.Select(dependency => dependency.Id));
    }

    [Fact]
    public void SelectDependencyGroup_NoGroups_ReturnsNoDependencyGroups()
    {
        var result = DependencyResolutionService.SelectDependencyGroup(null, null);

        Assert.Equal(DependencyResolutionService.DependencyGroupSelectionStatus.NoDependencyGroups, result.Status);
        Assert.Null(result.Group);
        Assert.Empty(result.AvailableTargetFrameworks);
    }

    [Fact]
    public void SelectDependencyGroup_NoRequestedTfm_SelectsHighestTfm()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "netstandard2.0" },
            new() { TargetFramework = "net8.0" },
            new() { TargetFramework = "net472" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(groups, null);

        Assert.True(result.IsSelected);
        Assert.Equal("net8.0", result.Group?.TargetFramework);
        Assert.Equal("net8.0", result.TargetFramework);
    }

    [Fact]
    public void SelectDependencyGroup_RequestedTfm_AllowsCompatibleFallbackAndPreservesRequestedTarget()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net6.0" },
            new() { TargetFramework = "net8.0" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(groups, "net9.0");

        Assert.True(result.IsSelected);
        Assert.Equal("net8.0", result.Group?.TargetFramework);
        Assert.Equal("net9.0", result.TargetFramework);
    }

    [Fact]
    public void SelectDependencyGroup_EmptyRequestedTfm_SelectsEmptyTfmGroup()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net8.0" },
            new() { TargetFramework = "" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(groups, "");

        Assert.True(result.IsSelected);
        Assert.Equal("", result.Group?.TargetFramework);
        Assert.Equal("", result.TargetFramework);
    }

    [Fact]
    public void SelectDependencyGroup_ExactMode_EmptyRequestedTfm_SelectsEmptyTfmGroup()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net8.0" },
            new() { TargetFramework = "" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            "",
            allowCompatibleFallbackForRequestedTfm: false);

        Assert.True(result.IsSelected);
        Assert.Equal("", result.Group?.TargetFramework);
        Assert.Equal("", result.TargetFramework);
    }

    [Fact]
    public void SelectDependencyGroup_RequestedTfm_NoMatch_ReturnsAvailableTfms()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net8.0" },
            new() { TargetFramework = "net9.0" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(groups, "net6.0");

        Assert.Equal(DependencyResolutionService.DependencyGroupSelectionStatus.NoMatchingTargetFramework, result.Status);
        Assert.Null(result.Group);
        Assert.Equal("net6.0", result.TargetFramework);
        Assert.Equal(["net8.0", "net9.0"], result.AvailableTargetFrameworks);
    }

    [Theory]
    [InlineData(".NETStandard2.0", "netstandard2.0")]
    [InlineData(".NETFramework4.5", "net45")]
    [InlineData(".NETCoreApp,Version=v8.0", "net8.0")]
    [InlineData(".NETCoreApp,Version=v8.0.0", "net8.0")]
    [InlineData(
        ".NETFramework,Version=v4.0.0,Profile=Client",
        "net40-client")]
    [InlineData(
        ".NETCoreApp,Version=v8.0.0,Platform=Windows,PlatformVersion=7.0.0",
        "net8.0-windows7.0")]
    [InlineData(
        ".NETCoreApp,Version=v8.0.0,Platform=Windows,PlatformVersion=10.0.19041.0",
        "net8.0-windows10.0.19041.0")]
    public void SelectDependencyGroup_ExactMode_NormalizesNuGetLongForm(
        string declared,
        string requested)
    {
        var groups = new List<DependencyGroup>
        {
            new() { TargetFramework = declared }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            requested,
            allowCompatibleFallbackForRequestedTfm: false);

        Assert.True(result.IsSelected);
        Assert.Equal(declared, result.Group?.TargetFramework);
        Assert.Equal(requested, result.TargetFramework);
    }

    [Fact]
    public void SelectDependencyGroup_ExactMode_RejectsOverlongFrameworkVersion()
    {
        var groups = new List<DependencyGroup>
        {
            new() { TargetFramework = ".NETCoreApp,Version=v8.0.0.0.0" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            "net8.0",
            allowCompatibleFallbackForRequestedTfm: false);

        Assert.Equal(
            DependencyResolutionService.DependencyGroupSelectionStatus
                .NoMatchingTargetFramework,
            result.Status);
        Assert.Null(result.Group);
    }

    [Fact]
    public void SelectDependencyGroup_ExactMode_DoesNotConflateFrameworkProfile()
    {
        var groups = new List<DependencyGroup>
        {
            new()
            {
                TargetFramework =
                    ".NETFramework,Version=v4.0,Profile=Client",
            },
            new() { TargetFramework = "net40" },
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            "net40",
            allowCompatibleFallbackForRequestedTfm: false);

        Assert.Equal("net40", result.Group?.TargetFramework);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("net45")]
    public void SelectDependencyGroup_QualifiedGroupOutranksUniversal(
        string? requested)
    {
        var groups = new List<DependencyGroup>
        {
            new() { TargetFramework = "any" },
            new()
            {
                TargetFramework =
                    ".NETFramework,Version=v4.0,Profile=Client",
            },
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            requested);

        Assert.Equal(
            ".NETFramework,Version=v4.0,Profile=Client",
            result.Group?.TargetFramework);
    }

    [Theory]
    [InlineData("")]
    [InlineData("any")]
    public void SelectDependencyGroup_ExactMode_UniversalGroupMatchesAnyTarget(
        string declared)
    {
        var groups = new List<DependencyGroup>
        {
            new() { TargetFramework = declared }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            "net9.0",
            allowCompatibleFallbackForRequestedTfm: false);

        Assert.True(result.IsSelected);
        Assert.Equal(declared, result.Group?.TargetFramework);
    }

    [Fact]
    public void SelectDependencyGroup_ExactMode_PrefersExactGroupOverUniversal()
    {
        var groups = new List<DependencyGroup>
        {
            new() { TargetFramework = "any" },
            new() { TargetFramework = ".NETCoreApp,Version=v8.0.0" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            "net8.0",
            allowCompatibleFallbackForRequestedTfm: false);

        Assert.Equal(".NETCoreApp,Version=v8.0.0", result.Group?.TargetFramework);
    }

    [Fact]
    public void SelectDependencyGroup_ExactMode_DoesNotFallbackForRequestedTfm()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net8.0" }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(
            groups,
            "net9.0",
            allowCompatibleFallbackForRequestedTfm: false);

        Assert.Equal(DependencyResolutionService.DependencyGroupSelectionStatus.NoMatchingTargetFramework, result.Status);
        Assert.Null(result.Group);
    }

    [Fact]
    public void SelectDependencyGroup_EmptyDependencyGroup_IsStillSelected()
    {
        var groups = new List<DotnetInspector.Packages.DependencyGroup>
        {
            new() { TargetFramework = "net8.0", Dependencies = [] }
        };

        var result = DependencyResolutionService.SelectDependencyGroup(groups, null);

        Assert.True(result.IsSelected);
        Assert.Empty(result.Group!.Dependencies);
    }
}
