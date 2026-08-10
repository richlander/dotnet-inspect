using System.Net;
using System.Text;
using DotnetInspector.Core;
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
    public void TfmSelector_GetTfmPriority_NormalizesLongFormTfms()
    {
        Assert.Equal(TfmResolver.GetTfmPriority("net8.0"), TfmSelector.GetTfmPriority(".NETCoreApp,Version=v8.0/linux-x64"));
        Assert.Equal(TfmResolver.GetTfmPriority("netstandard2.0"), TfmSelector.GetTfmPriority(".NETStandard2.0"));
        Assert.Equal(TfmResolver.GetTfmPriority("net472"), TfmSelector.GetTfmPriority(".NETFramework4.7.2"));
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
    public async Task ResolveDependencyTree_MalformedTransitiveNuspec_PropagatesTypedRejection()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        var handler = new MalformedNuspecHandler();
        using var client = new HttpClient(handler);
        string packageId = $"typed-rejection-probe-{Guid.NewGuid():N}";
        var dependencies = new List<PackageDependency>
        {
            new() { Id = packageId, Version = "1.0.0" }
        };

        await Assert.ThrowsAsync<NuspecParseException>(
            () => DependencyResolutionService.ResolveDependencyTreeAsync(
                client,
                dependencies,
                "net10.0",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                log: null));
    }

    [Fact]
    public async Task ResolveDependencyTree_UnmappedTransitivePackagePropagatesMappingFailure()
    {
        string configPath = Path.Combine(
            Path.GetTempPath(),
            $"dependency-mapping-{Guid.NewGuid():N}.config");
        File.WriteAllText(configPath, """
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="https://private.example/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="Other.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        var dependencies = new List<PackageDependency>
        {
            new() { Id = "Unmapped.Dependency", Version = "1.0.0" }
        };

        try
        {
            PackageSourceMappingException exception =
                await Assert.ThrowsAsync<PackageSourceMappingException>(
                    () => DependencyResolutionService.ResolveDependencyTreeAsync(
                        new HttpClient(),
                        dependencies,
                        "net10.0",
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        log: null,
                        sourceOptions: new NuGetSourceOptions { ConfigFile = configPath }));

            Assert.Equal(PackageSourceMappingFailure.NoPattern, exception.Failure);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task ResolveDependencyTree_UsesCallerSourcesForEveryTransitiveNuspec()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        const string index = "https://private.example/v3/index.json";
        const string flat = "https://private.example/v3-flatcontainer/";
        string suffix = Guid.NewGuid().ToString("N");
        string parentId = $"Parent.Package.{suffix}";
        string childId = $"Child.Package.{suffix}";
        var handler = new TransitiveNuspecHandler(index, flat, parentId, childId);
        using var client = new HttpClient(handler);
        var dependencies = new List<PackageDependency>
        {
            new() { Id = parentId, Version = "1.0.0" }
        };

        List<DependencyNode> result =
            await DependencyResolutionService.ResolveDependencyTreeAsync(
                client,
                dependencies,
                "net10.0",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                log: null,
                sourceOptions: new NuGetSourceOptions { Sources = [index] });

        DependencyNode parent = Assert.Single(result);
        Assert.Equal(parentId, parent.PackageId);
        Assert.Equal(childId, Assert.Single(parent.Children).PackageId);
        Assert.Contains(
            $"{flat}{parentId.ToLowerInvariant()}/1.0.0/{parentId.ToLowerInvariant()}.nuspec",
            handler.Requested);
        Assert.Contains(
            $"{flat}{childId.ToLowerInvariant()}/1.0.0/{childId.ToLowerInvariant()}.nuspec",
            handler.Requested);
        Assert.All(handler.Requested, url =>
            Assert.StartsWith("https://private.example/", url, StringComparison.Ordinal));
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

    private sealed class MalformedNuspecHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.RequestUri!.AbsolutePath.EndsWith(
                ".nuspec",
                StringComparison.OrdinalIgnoreCase)
                ? "<package><metadata><id>REJECTED-TEXT</metadata></package>"
                : """{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }

    private sealed class TransitiveNuspecHandler(
        string index,
        string flat,
        string parentId,
        string childId) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requested.Add(url);

            string? body = url switch
            {
                _ when url == index =>
                    $$"""{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"{{flat}}"}]}""",
                _ when url.EndsWith(
                    $"/{parentId.ToLowerInvariant()}.nuspec",
                    StringComparison.Ordinal) =>
                    $$"""
                    <package>
                      <metadata>
                        <id>{{parentId}}</id>
                        <version>1.0.0</version>
                        <authors>Test</authors>
                        <dependencies>
                          <group targetFramework="net10.0">
                            <dependency id="{{childId}}" version="1.0.0" />
                          </group>
                        </dependencies>
                      </metadata>
                    </package>
                    """,
                _ when url.EndsWith(
                    $"/{childId.ToLowerInvariant()}.nuspec",
                    StringComparison.Ordinal) =>
                    $$"""
                    <package>
                      <metadata>
                        <id>{{childId}}</id>
                        <version>1.0.0</version>
                        <authors>Test</authors>
                      </metadata>
                    </package>
                    """,
                _ => null
            };

            return Task.FromResult(new HttpResponseMessage(
                body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "", Encoding.UTF8),
                RequestMessage = request
            });
        }
    }
}
