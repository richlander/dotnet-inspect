using DotnetInspector.Packages;

namespace DotnetInspector.Sections;

/// <summary>
/// Selects the package's primary human-readable document through the shared
/// host-neutral inspection handoff.
/// </summary>
public static class PackagePrimaryDocumentInspection
{
    public static InspectionEnvelope<PackagePrimaryDocumentResolution> Execute(
        IEnumerable<PackageContentEntry> entries,
        string? declaredReadme = null)
    {
        PackagePrimaryDocumentResolution content =
            PackagePrimaryDocument.Resolve(
                entries,
                declaredReadme);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "package/primary-document",
                "Package primary-document selection does not define a standalone share projection."));
    }
}
