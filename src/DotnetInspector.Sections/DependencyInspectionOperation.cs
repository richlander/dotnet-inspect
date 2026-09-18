namespace DotnetInspector.Sections;

/// <summary>
/// The baseline content and supplemental evidence settled by one dependency
/// inspection execution.
/// </summary>
public sealed record DependencyInspectionResult
{
    public DependencyInspectionResult(
        DependencyInspectionContent content,
        DependencyInspectionEvidenceDocument evidence)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Evidence = evidence
            ?? throw new ArgumentNullException(nameof(evidence));
    }

    public DependencyInspectionContent Content { get; }

    public DependencyInspectionEvidenceDocument Evidence { get; }
}

/// <summary>
/// Creates ordinary and evidence-enriched terminal values from one settled
/// dependency inspection.
/// </summary>
public static class DependencyInspectionOperation
{
    public static InspectionEnvelope<DependencyInspectionContent> Execute(
        DependencyInspectionResult result,
        InspectionShare share,
        IEnumerable<InspectionDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new InspectionEnvelope<DependencyInspectionContent>(
            result.Content,
            share,
            diagnostics);
    }

    public static EvidenceInspectionEnvelope<
        DependencyInspectionContent,
        DependencyInspectionEvidenceDocument> ExecuteWithEvidence(
            DependencyInspectionResult result,
            InspectionShare share,
            IEnumerable<InspectionDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        InspectionEnvelope<DependencyInspectionContent> inspection =
            Execute(result, share, diagnostics);
        return new(inspection, result.Evidence);
    }
}
