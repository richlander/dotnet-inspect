using System.Text;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal static class BrowserSpotlightTypeIdentity
{
    internal static string? TryCreate(TypeFindSemanticMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return TryCreate(
            match.Declaration.Source.Coordinate,
            match.Declaration.Name,
            match.Declaration.ModuleVersionId);
    }

    internal static string DeclarationKind(TypeFindSemanticMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.DeclarationKind.ToString();
    }

    internal static string AssemblyName(TypeFindSemanticMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Declaration.Source.Coordinate
            .LibraryIdentity.Identity.Name;
    }

    internal static string? TryCreate(
        ExactLibrarySourceCoordinate coordinate,
        MetadataTypeDefinitionName type,
        Guid moduleVersionId)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(type);

        AssemblyReferenceIdentity library = coordinate.LibraryIdentity.Identity;
        var identity = new StringBuilder(coordinate switch
        {
            ExactLibrarySourceCoordinate.Package => "package-type:",
            ExactLibrarySourceCoordinate.Platform => "platform-type:",
            ExactLibrarySourceCoordinate.Project => "project-type:",
            ExactLibrarySourceCoordinate.Local => "local-type:",
            _ => throw new InvalidOperationException(
                "Type Find returned an unsupported source coordinate."),
        });
        if (coordinate is ExactLibrarySourceCoordinate.Package package)
        {
            Append(
                identity,
                package.PackageCoordinate.PackageId.ToLowerInvariant());
            Append(
                identity,
                package.PackageCoordinate.Version.ToLowerInvariant());
        }
        else if (coordinate is ExactLibrarySourceCoordinate.Platform platform)
        {
            Append(identity, platform.Population.Family.ToString());
        }
        Append(
            identity,
            library.Name.ToLowerInvariant());
        Append(identity, library.Version?.ToString() ?? "");
        Append(identity, NormalizeCulture(library.Culture));
        Append(identity, library.PublicKeyToken?.ToLowerInvariant() ?? "");
        Append(identity, moduleVersionId.ToString("D"));
        Append(identity, type.ToMetadataFullName());
        return identity.ToString();
    }

    static void Append(StringBuilder identity, string value)
    {
        identity.Append(value.Length);
        identity.Append(':');
        identity.Append(value);
        identity.Append(';');
    }

    static string NormalizeCulture(string? culture) =>
        string.IsNullOrEmpty(culture)
            || culture.Equals("neutral", StringComparison.OrdinalIgnoreCase)
                ? ""
                : culture.ToLowerInvariant();
}
