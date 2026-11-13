using DotnetInspector.Libraries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.DocumentationHouse.Platform;

/// <summary>
/// Binds one exact PlatformHouse Library realization to source-neutral
/// compiled-XML evidence.
/// </summary>
public static class PlatformDocumentationHouseAdapter
{
    public static CompiledXmlContribution CreateCompiledXmlContribution(
        PlatformLibraryRealizationReceipt materialization,
        DocumentationSubjectReference subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return Create(materialization, subject);
    }

    /// <summary>
    /// The same selection evidence for a whole-Library summary read
    /// (docs/design/documentation-house.md#whole-library-compiled-summaries),
    /// naming no subject.
    /// </summary>
    public static CompiledXmlContribution CreateLibraryCompiledXmlContribution(
        PlatformLibraryRealizationReceipt materialization) =>
        Create(materialization, subject: null);

    private static CompiledXmlContribution Create(
        PlatformLibraryRealizationReceipt materialization,
        DocumentationSubjectReference? subject)
    {
        ArgumentNullException.ThrowIfNull(materialization);

        LibraryReference library =
            materialization.RealizedLibrary?.Library
            ?? throw new ArgumentException(
                "The platform adapter requires a completed one-Library realization receipt.",
                nameof(materialization));
        if (library.SourceCoordinate
                is not ExactLibrarySourceCoordinate.Platform)
        {
            throw new ArgumentException(
                "The platform adapter requires an exact Platform Library.",
                nameof(materialization));
        }

        LibraryContentReference apiContent =
            library.ApiAssembly;
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

        if (compiledXml is not null)
        {
            return CompiledXmlContribution.Candidate(
                subject,
                library,
                apiContent,
                source,
                compiledXml);
        }

        bool authoritativeAbsence =
            materialization.HouseReceipt.Request.Operation
                is PlatformHouseOperationSnapshot.Realize
            {
                ContentDemand: var contentDemand,
            }
            && contentDemand.HasFlag(
                PlatformLibraryContentDemand
                    .CompiledXmlDocumentation);
        return authoritativeAbsence
            ? CompiledXmlContribution.Absent(
                subject,
                library,
                apiContent,
                source)
            : CompiledXmlContribution.Unavailable(
                subject,
                library,
                apiContent,
                source);
    }

    private static DocumentationSourceReference
        CreateSourceReference(
            PlatformLibraryRealizationReceipt materialization)
    {
        string name =
            $"platform:{materialization.HouseReceipt.TargetSettlement.SettledTarget}";
        if (name.Length > 256)
            name = "platform";

        return DocumentationSourceReference.Create(
            DocumentationSourceKind.Platform,
            name);
    }
}
