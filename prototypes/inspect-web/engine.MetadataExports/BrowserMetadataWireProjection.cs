using System.Runtime.Versioning;

namespace InspectWeb.Engine.MetadataFacade;

/// <summary>
/// Maps <c>InspectWeb.Engine.Core</c>'s DTO-neutral projections onto this facade's own wire
/// records.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserMetadataWireProjection
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

    internal static BrowserTypeSurface Project(BrowserTypeSurfaceInfo type)
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
            type.Accessibility,
            type.AccessibilityId,
            type.Assembly,
            type.AssemblyId,
            type.AssemblyName,
            type.Members,
            type.Signature,
            [.. type.Api.Select(Project)],
            type.PlatformPack);
    }

    internal static BrowserMemberSurface Project(BrowserMemberSurfaceInfo member) =>
        new(
            member.Name,
            member.Kind,
            member.Signature,
            member.Accessibility,
            member.IsStatic,
            member.IsUnsafe,
            member.IsVirtual,
            member.IsAbstract,
            member.IsOverride,
            member.IsExtension,
            member.IsObsolete,
            member.GenericArity,
            member.MetadataToken,
            member.ReturnType,
            [
                .. member.Parameters.Select(parameter => new BrowserParameterSurface(
                    parameter.Name,
                    parameter.Type,
                    parameter.Modifier,
                    parameter.HasDefault,
                    parameter.DefaultValue,
                    parameter.Description)),
            ],
            member.DocumentationId,
            member.Summary,
            member.Returns,
            [
                .. member.Exceptions.Select(exception => new BrowserExceptionSurface(
                    exception.Type,
                    exception.Description)),
            ],
            member.StableSelector,
            member.AnchorDigest,
            member.CanonicalSignature,
            member.GraphSelectorKey,
            [.. member.BodySelectors.Select(Project)]);

    internal static BrowserMemberBodySelector Project(
        BrowserMemberBodySelectorInfo selector) =>
        new(selector.Token, selector.MemberName, selector.SelectorKey);
}
