using System.Runtime.Versioning;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// Maps <c>DotnetInspect.Web.Core</c>'s DTO-neutral compile-library outcome onto this facade's own
/// wire record.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserAnalysisWireProjection
{
    internal static BrowserPerformanceTypeSurface Project(
        BrowserTypeSurfaceInfo type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new(
            type.Id,
            type.DefinitionId,
            type.QueryId,
            type.MetadataId,
            type.Name,
            type.DisplayName,
            type.Namespace,
            type.Kind,
            type.KindFacetId,
            type.TraitFacetIds,
            type.Accessibility,
            type.AccessibilityId,
            type.Assembly,
            type.AssemblyId,
            type.AssemblyName,
            type.Members,
            type.Signature,
            type.PlatformPack);
    }

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
