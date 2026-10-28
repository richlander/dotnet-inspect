using System.Text;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal static class BrowserSpotlightTypeIdentity
{
    internal static string? TryCreate(
        ExactLibrarySourceCoordinate coordinate,
        MetadataTypeDefinitionName type,
        Guid moduleVersionId)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(type);
        if (coordinate is not ExactLibrarySourceCoordinate.Package package)
            return null;

        AssemblyReferenceIdentity library = package.LibraryIdentity.Identity;
        var identity = new StringBuilder("package-type:");
        Append(
            identity,
            package.PackageCoordinate.PackageId.ToLowerInvariant());
        Append(
            identity,
            package.PackageCoordinate.Version.ToLowerInvariant());
        Append(identity, library.Name.ToLowerInvariant());
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
