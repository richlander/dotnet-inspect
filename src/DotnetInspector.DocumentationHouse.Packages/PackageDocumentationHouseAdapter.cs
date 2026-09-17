using DotnetInspector.Libraries;
using DotnetInspector.Packages;

namespace DotnetInspector.DocumentationHouse.Packages;

/// <summary>
/// Binds one exact PackageHouse Library materialization to source-neutral
/// compiled-XML evidence.
/// </summary>
public static class PackageDocumentationHouseAdapter
{
    public static CompiledXmlContribution CreateCompiledXmlContribution(
        PackageHouseLibraryMaterializationReceipt materialization,
        DocumentationSubjectReference subject)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        ArgumentNullException.ThrowIfNull(subject);

        LibraryReference library = materialization.Library;
        LibraryContentReference apiContent = library.ApiAssembly;
        DocumentationSourceReference source =
            CreateSourceReference(materialization);
        LibraryContentReference? compiledXml =
            library.Contents.SingleOrDefault(
                content =>
                    content.HasRole(
                        LibraryContentRole
                            .CompiledXmlDocumentation)
                    && ReferenceEquals(
                        content.AssociatedAssembly,
                        apiContent));

        return compiledXml is null
            ? CompiledXmlContribution.Absent(
                subject,
                library,
                apiContent,
                source)
            : CompiledXmlContribution.Candidate(
                subject,
                library,
                apiContent,
                source,
                compiledXml);
    }

    private static DocumentationSourceReference CreateSourceReference(
        PackageHouseLibraryMaterializationReceipt materialization)
    {
        var coordinate = materialization.Handoff.Coordinate;
        string name =
            $"package:{coordinate.PackageId}@{coordinate.Version}";
        if (name.Length > 256)
            name = $"package:{coordinate.PackageId}";

        return DocumentationSourceReference.Create(
            DocumentationSourceKind.Package,
            name);
    }
}
