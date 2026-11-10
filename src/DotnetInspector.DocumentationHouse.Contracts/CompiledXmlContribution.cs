using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse;

public enum CompiledXmlContributionKind
{
    Candidate,
    Absent,
    Partial,
    Unavailable,
}

/// <summary>
/// Resource-free source evidence for one compiled-XML selection observation.
/// A contribution names the documentation subject it was created for, or no
/// subject when it describes the Library's companion selection for a
/// whole-Library summary read (docs/design/documentation-house.md#whole-library-compiled-summaries);
/// per-subject settlement rejects a contribution without its subject.
/// </summary>
public sealed class CompiledXmlContribution
{
    private CompiledXmlContribution(
        CompiledXmlContributionKind kind,
        DocumentationSubjectReference? subject,
        LibraryReference library,
        LibraryContentReference apiContent,
        DocumentationSourceReference source,
        LibraryContentReference? compiledXmlContent,
        int? precedence)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(apiContent);
        ArgumentNullException.ThrowIfNull(source);
        if ((kind == CompiledXmlContributionKind.Candidate)
            != (compiledXmlContent is not null))
        {
            throw new ArgumentException(
                "Only a candidate contribution names compiled XML content.",
                nameof(compiledXmlContent));
        }
        if (kind != CompiledXmlContributionKind.Candidate
            && precedence is not null)
        {
            throw new ArgumentException(
                "Only a candidate contribution may declare precedence.",
                nameof(precedence));
        }
        if (precedence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precedence),
                "Compiled XML precedence cannot be negative.");
        }

        Kind = kind;
        Subject = subject;
        Library = library;
        ApiContent = apiContent;
        Source = source;
        CompiledXmlContent = compiledXmlContent;
        Precedence = precedence;
    }

    public CompiledXmlContributionKind Kind { get; }
    public DocumentationSubjectReference? Subject { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference ApiContent { get; }
    public DocumentationSourceReference Source { get; }
    public LibraryContentReference? CompiledXmlContent { get; }
    public int? Precedence { get; }

    public static CompiledXmlContribution Candidate(
        DocumentationSubjectReference? subject,
        LibraryReference library,
        LibraryContentReference apiContent,
        DocumentationSourceReference source,
        LibraryContentReference compiledXmlContent,
        int? precedence = null) =>
        new(
            CompiledXmlContributionKind.Candidate,
            subject,
            library,
            apiContent,
            source,
            compiledXmlContent,
            precedence);

    public static CompiledXmlContribution Absent(
        DocumentationSubjectReference? subject,
        LibraryReference library,
        LibraryContentReference apiContent,
        DocumentationSourceReference source) =>
        new(
            CompiledXmlContributionKind.Absent,
            subject,
            library,
            apiContent,
            source,
            compiledXmlContent: null,
            precedence: null);

    public static CompiledXmlContribution Partial(
        DocumentationSubjectReference? subject,
        LibraryReference library,
        LibraryContentReference apiContent,
        DocumentationSourceReference source) =>
        new(
            CompiledXmlContributionKind.Partial,
            subject,
            library,
            apiContent,
            source,
            compiledXmlContent: null,
            precedence: null);

    public static CompiledXmlContribution Unavailable(
        DocumentationSubjectReference? subject,
        LibraryReference library,
        LibraryContentReference apiContent,
        DocumentationSourceReference source) =>
        new(
            CompiledXmlContributionKind.Unavailable,
            subject,
            library,
            apiContent,
            source,
            compiledXmlContent: null,
            precedence: null);
}
