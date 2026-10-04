using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public enum MemberContextualExplanationKind
{
    Command,
    ExactSubject,
}

public sealed record MemberContextualExplanationSubject(
    string TypeName,
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string Library,
    string? Package,
    string? Framework);

public sealed record MemberContextualExplanationDocument(
    MemberContextualExplanationKind Kind,
    ResourceExplanationDocument Resource,
    MemberContextualExplanationSubject? Subject,
    ViewFacetId? DefaultFacet,
    ImmutableArray<string> SelectedSections,
    ImmutableArray<RelatedOperationAffordance> RelatedOperations);

public static class MemberContextualExplanationOperation
{
    public static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainCommand(
            ResourceExplanationDocument resource,
            IEnumerable<string> defaultSections)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(defaultSections);
        return Envelope(
            new(
                MemberContextualExplanationKind.Command,
                resource,
                Subject: null,
                DefaultFacet: null,
                SelectedSections: [.. defaultSections],
                RelatedOperations: []));
    }

    public static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainExactSubject(
            ResourceExplanationDocument resource,
            ResolvedMemberInspectionBasis basis,
            IEnumerable<string> defaultSections)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(defaultSections);

        string? package = basis.Source.Package is { } packageAsset
            ? $"{packageAsset.PackageId}@{packageAsset.PackageVersion}"
            : null;
        var subject = new MemberContextualExplanationSubject(
            basis.Target.TypeName,
            basis.Target.Member.StableSelector,
            basis.Target.Member.CanonicalSignature,
            basis.Target.Member.Fingerprint,
            basis.Source.LibraryKey,
            package,
            basis.Source.Framework);
        return Envelope(
            new(
                MemberContextualExplanationKind.ExactSubject,
                resource,
                subject,
                basis.DefaultFacet,
                basis.SemanticDemand.Sections.IsEmpty
                    ? [.. defaultSections]
                    : basis.SemanticDemand.Sections,
                MemberRelatedOperationAffordances.All));
    }

    private static InspectionEnvelope<MemberContextualExplanationDocument>
        Envelope(MemberContextualExplanationDocument document) =>
        new(
            document,
            new InspectionShare.NonProjectable(
                "member-contextual-explanation",
                "Contextual Member explanations do not yet have a portable "
                    + "Workspace projection."));
}
