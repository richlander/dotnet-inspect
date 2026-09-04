using System.Runtime.Versioning;

namespace InspectWeb.Engine.PackageFacade;

/// <summary>
/// Maps <c>InspectWeb.Engine.Core</c>'s DTO-neutral projections onto this facade's own wire
/// records. Core owns the projection semantics; this file owns nothing but the transport shape.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageWireProjection
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

    internal static BrowserPackageSurface Project(BrowserPackageSurfaceInfo surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return new(
            surface.Package,
            surface.Version,
            surface.Frameworks,
            surface.ActiveFramework,
            Project(surface.Icon),
            surface.DefaultAssemblyId,
            Project(surface.CompileLibrary),
            [.. surface.Assemblies.Select(Project)],
            [.. surface.Types.Select(Project)],
            [.. surface.Accessibility.Select(Project)],
            surface.TotalMembers,
            Project(surface.Documents),
            surface.InspectionErrors,
            surface.InspectionError);
    }

    internal static BrowserAssemblySurface Project(BrowserAssemblySurfaceInfo assembly) =>
        new(
            assembly.Id,
            assembly.Name,
            assembly.Version,
            assembly.Culture,
            assembly.PublicKeyToken,
            assembly.Asset,
            assembly.PublicTypes,
            assembly.PublicMembers,
            assembly.PlatformPack);

    internal static BrowserAccessibilityDescriptor Project(
        BrowserAccessibilityInfo accessibility) =>
        new(
            accessibility.Id,
            accessibility.Label,
            accessibility.Order,
            accessibility.IsDefault,
            accessibility.Count);

    internal static BrowserTypeSurface Project(BrowserTypeSurfaceInfo type) =>
        new(
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
            [
                .. member.BodySelectors.Select(selector => new BrowserMemberBodySelector(
                    selector.Token,
                    selector.MemberName,
                    selector.SelectorKey)),
            ]);

    internal static BrowserPackageCacheStats Project(BrowserPackageCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.Packages,
            snapshot.Resident,
            snapshot.Workspaces,
            snapshot.ResidentBytes);
    }

    internal static BrowserPackageDocument Project(BrowserPackageDocumentEntry document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.Kind,
            document.Name,
            document.Path,
            document.Size);
    }

    internal static BrowserPackageDocument[] Project(
        IReadOnlyList<BrowserPackageDocumentEntry> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return [.. documents.Select(Project)];
    }

    internal static BrowserPackageDocumentContent Project(
        BrowserPackageDocumentPayload document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.Kind,
            document.Name,
            document.Path,
            document.Text);
    }

    internal static BrowserPackageIcon? Project(BrowserPackageIconPayload? icon) =>
        icon is null
            ? null
            : new BrowserPackageIcon(icon.MediaType, icon.Base64);
}
