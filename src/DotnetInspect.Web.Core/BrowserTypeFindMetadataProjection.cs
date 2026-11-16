using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal sealed record BrowserAssemblyResolutionProvenanceProjection(
    string Kind,
    string? PackageId,
    string? PackageVersion,
    string? TargetFramework,
    string? RuntimeIdentifier,
    string? AssetPath,
    string? Framework,
    string? FrameworkVersion,
    string? Project,
    string? ResolverSource,
    string? ContentReference,
    string? Digest,
    string? DeclaredName);

internal sealed record BrowserTypeFindMetadataProjection(
    string AssemblyName,
    string AssemblyVersion,
    string? AssemblyCulture,
    string? AssemblyPublicKeyToken,
    BrowserAssemblyResolutionProvenanceProjection Provenance,
    string? PatternNamespaceMatch,
    string DeclarationKind,
    string? DefinitionKind,
    bool? IsEditorBrowsableNever,
    bool? IsObsolete)
{
    internal static BrowserTypeFindMetadataProjection Project(
        TypeFindSemanticMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var library = match.Declaration.Source.AssemblyIdentity;
        var discovery = match.DiscoveryAttributes;
        return new(
            library.Name,
            library.Version?.ToString()
                ?? throw new InvalidOperationException(
                    "An exact Type source has no assembly version."),
            library.Culture,
            library.PublicKeyToken,
            ProjectProvenance(match.Declaration.Source.Selection),
            match.Pattern is TypeFindPattern.Namespace namespacePattern
                ? namespacePattern.Match.ToString()
                : null,
            match.DeclarationKind.ToString(),
            match.DefinitionKind?.ToString(),
            discovery?.IsEditorBrowsableNever,
            discovery?.IsObsolete);
    }

    static BrowserAssemblyResolutionProvenanceProjection ProjectProvenance(
        AssemblyResolutionProvenance provenance) =>
        provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset package => new(
                "Package",
                package.PackageId,
                package.PackageVersion,
                package.Tfm,
                package.Rid,
                package.AssetPath,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                ResolverSource: null,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.PlatformAsset platform => new(
                "Platform",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                platform.Framework,
                platform.FrameworkVersion,
                Project: null,
                platform.ResolverSource,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.ProjectAsset project => new(
                "Project",
                PackageId: null,
                PackageVersion: null,
                project.Tfm,
                project.Rid,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                project.Project,
                ResolverSource: null,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.LocalAsset local => new(
                "Local",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                local.ResolverSource,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.DesignatedAsset designated => new(
                "Designated",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                designated.ResolverSource,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.EmbeddedAsset embedded => new(
                "Embedded",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                ResolverSource: null,
                embedded.ContentRef,
                embedded.Digest,
                embedded.DeclaredName),
            _ => throw new InvalidOperationException(
                "Type Find returned unsupported assembly provenance."),
        };
}
