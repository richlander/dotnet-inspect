using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse.Direct;

/// <summary>
/// Binds one exact direct Library to source-neutral compiled-XML evidence.
/// </summary>
public static class DirectLibraryDocumentationHouseAdapter
{
    public static IReadOnlyList<CompiledXmlContribution>
        CreateCompiledXmlContributions(
            LibraryReference library,
            DocumentationSubjectReference subject)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(subject);
        if (!library.IsDirectArtifact)
        {
            throw new ArgumentException(
                "The direct-Library adapter requires a direct Artifact-backed Library.",
                nameof(library));
        }

        LibraryContentReference apiContent = library.ApiAssembly;
        DocumentationSourceReference source =
            CreateSourceReference(library);
        CompiledXmlContribution[] candidates =
            library.Contents
                .Where(
                    content =>
                        content.HasRole(
                            LibraryContentRole
                                .CompiledXmlDocumentation)
                        && ReferenceEquals(
                            content.AssociatedAssembly,
                            apiContent))
                .Select(
                    content =>
                        CompiledXmlContribution.Candidate(
                            subject,
                            library,
                            apiContent,
                            source,
                            content))
                .ToArray();

        if (candidates.Length > 0)
            return Array.AsReadOnly(candidates);

        CompiledXmlContribution[] unavailable =
        [
            CompiledXmlContribution.Unavailable(
                subject,
                library,
                apiContent,
                source),
        ];
        return Array.AsReadOnly(unavailable);
    }

    private static DocumentationSourceReference CreateSourceReference(
        LibraryReference library)
    {
        string assemblyName =
            library.ApiAssembly.AssemblyIdentity!.Identity.Name;
        string name = $"direct-library:{assemblyName}";
        if (name.Length > 256)
            name = "direct-library";

        return DocumentationSourceReference.Create(
            DocumentationSourceKind.DirectLibrary,
            name);
    }
}
