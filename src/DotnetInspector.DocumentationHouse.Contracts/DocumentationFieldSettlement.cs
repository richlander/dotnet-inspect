using System.Collections.ObjectModel;

using CSharpText;

namespace DotnetInspector.DocumentationHouse;

public enum DocumentationChannel
{
    CompiledXml,
    AuthoredSource,
}

public enum DocumentationFieldEvidenceKind
{
    Selected,
    Corroborated,
    Conflict,
    Absent,
}

public sealed record DocumentationFieldContribution<T>(
    DocumentationChannel Channel,
    T Value)
    where T : notnull;

/// <summary>
/// Closed evidence for one documentation field across the requested channels.
/// </summary>
public sealed class DocumentationFieldEvidence<T>
    where T : notnull
{
    internal DocumentationFieldEvidence(
        DocumentationFieldEvidenceKind kind,
        IReadOnlyList<DocumentationChannel> requestedChannels,
        IReadOnlyList<DocumentationFieldContribution<T>> contributions)
    {
        Kind = kind;
        RequestedChannels = Array.AsReadOnly(
            requestedChannels.ToArray());
        Contributions = Array.AsReadOnly(
            contributions.ToArray());
    }

    public DocumentationFieldEvidenceKind Kind { get; }
    public IReadOnlyList<DocumentationChannel> RequestedChannels { get; }
    public IReadOnlyList<DocumentationFieldContribution<T>> Contributions
    {
        get;
    }
}

/// <summary>Detached field evidence from every requested channel.</summary>
public sealed class DocumentationFieldSettlement
{
    internal DocumentationFieldSettlement(
        DocumentationFieldEvidence<string> summary,
        DocumentationFieldEvidence<string> remarks,
        DocumentationFieldEvidence<string> returns,
        IReadOnlyDictionary<
            string,
            DocumentationFieldEvidence<string>> parameters,
        DocumentationFieldEvidence<
            IReadOnlyList<XmlDocumentationException>> exceptions,
        DocumentationFieldEvidence<
            IReadOnlyList<XmlDocumentationSampleReference>> samples)
    {
        Summary = summary;
        Remarks = remarks;
        Returns = returns;
        Parameters = new ReadOnlyDictionary<
            string,
            DocumentationFieldEvidence<string>>(
                new Dictionary<
                    string,
                    DocumentationFieldEvidence<string>>(
                        parameters,
                        StringComparer.Ordinal));
        Exceptions = exceptions;
        Samples = samples;
    }

    public DocumentationFieldEvidence<string> Summary { get; }
    public DocumentationFieldEvidence<string> Remarks { get; }
    public DocumentationFieldEvidence<string> Returns { get; }
    public IReadOnlyDictionary<
        string,
        DocumentationFieldEvidence<string>> Parameters { get; }
    public DocumentationFieldEvidence<
        IReadOnlyList<XmlDocumentationException>> Exceptions { get; }
    public DocumentationFieldEvidence<
        IReadOnlyList<XmlDocumentationSampleReference>> Samples { get; }
}
