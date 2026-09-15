using System.Runtime.Versioning;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// Maps <c>DotnetInspect.Web.Core</c>'s DTO-neutral compile-library outcome onto this facade's own
/// wire record.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserAnalysisWireProjection
{
    internal static BrowserCompileLibraryAvailability Project(
        BrowserCompileLibraryInfo compileLibrary)
    {
        ArgumentNullException.ThrowIfNull(compileLibrary);
        return new(
            compileLibrary.State switch
            {
                BrowserCompileLibraryState.Selected =>
                    BrowserCompileLibraryStatus.Selected,
                BrowserCompileLibraryState.NoCompileAssets =>
                    BrowserCompileLibraryStatus.NoCompileAssets,
                BrowserCompileLibraryState.NoMatchingTargetFramework =>
                    BrowserCompileLibraryStatus.NoMatchingTargetFramework,
                BrowserCompileLibraryState.EmptyCompileGroup =>
                    BrowserCompileLibraryStatus.EmptyCompileGroup,
                BrowserCompileLibraryState.InvalidImplementationAssets =>
                    BrowserCompileLibraryStatus.InvalidImplementationAssets,
                _ => throw new InvalidOperationException(
                    "Package compile-asset selection returned an unknown outcome."),
            },
            compileLibrary.TargetFramework,
            compileLibrary.Message);
    }
}
