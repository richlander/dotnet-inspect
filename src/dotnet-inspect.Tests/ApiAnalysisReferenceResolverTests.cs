using DotnetInspector.Inspectors;
using DotnetInspector.Options;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins that the API-analysis reference resolver forwards <see cref="ApiOptions"/> inputs. Issue
/// #2895 deliberately makes the type-scoped analysis index (via
/// <see cref="ApiAnalysisInspection.OpenTypeAnalysisIndex"/>) honor <c>--project</c> and
/// <c>--tfm</c>, matching the member-analysis path; before the refactor the type path built its
/// resolver with no options and silently ignored those inputs.
/// </summary>
public class ApiAnalysisReferenceResolverTests
{
    [Fact]
    public void CreateReferenceResolver_ForwardsProjectAssetsAndTfm()
    {
        var options = new ApiOptions
        {
            ProjectAssetsPath = "/tmp/project.assets.json",
            Tfm = "net8.0",
        };

        var resolver = ApiAnalysisInspection.CreateReferenceResolver("/tmp/Sample.dll", options);

        Assert.Equal("/tmp/project.assets.json", resolver.Options.ProjectAssetsPath);
        Assert.Equal("net8.0", resolver.Options.TargetFramework);
        // Fixed policy the API path always applies, regardless of options.
        Assert.False(resolver.Options.IncludeDepsJsonAssets);
        Assert.False(resolver.Options.IncludeAspNetCoreSharedFramework);
        Assert.True(resolver.Options.PreferImplementationAssemblies);
    }

    [Fact]
    public void CreateReferenceResolver_WithoutOptions_LeavesProjectAssetsAndTfmUnset()
    {
        var resolver = ApiAnalysisInspection.CreateReferenceResolver("/tmp/Sample.dll");

        Assert.Null(resolver.Options.ProjectAssetsPath);
        Assert.Null(resolver.Options.TargetFramework);
    }
}
