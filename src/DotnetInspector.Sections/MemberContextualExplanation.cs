using System.Collections.Immutable;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public enum MemberContextualExplanationKind
{
    Command,
    MemberGroup,
    ExactMember,
}

public abstract record MemberContextualExplanationSubject
{
    private protected MemberContextualExplanationSubject(
        string typeName,
        string library,
        string? package,
        string? framework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(library);
        TypeName = typeName;
        Library = library;
        Package = package;
        Framework = framework;
    }

    public string TypeName { get; }
    public string Library { get; }
    public string? Package { get; }
    public string? Framework { get; }
}

public sealed record MemberGroupContextualExplanationSubject
    : MemberContextualExplanationSubject
{
    public MemberGroupContextualExplanationSubject(
        MemberGroupSubject group,
        string library,
        string? package,
        string? framework)
        : base(
            (group ?? throw new ArgumentNullException(nameof(group)))
                .DeclaringType
                .ToMetadataFullName(),
            library,
            package,
            framework)
    {
        Group = group;
    }

    public MemberGroupSubject Group { get; }
}

public sealed record ExactMemberContextualExplanationSubject
    : MemberContextualExplanationSubject
{
    public ExactMemberContextualExplanationSubject(
        string typeName,
        string stableSelector,
        string canonicalSignature,
        string fingerprint,
        string library,
        string? package,
        string? framework)
        : base(typeName, library, package, framework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSignature);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        StableSelector = stableSelector;
        CanonicalSignature = canonicalSignature;
        Fingerprint = fingerprint;
    }

    public string StableSelector { get; }
    public string CanonicalSignature { get; }
    public string Fingerprint { get; }
}

public sealed record ResolvedMemberGroupExplanationBasis
{
    public ResolvedMemberGroupExplanationBasis(
        ResolvedInspectionSource source,
        MemberGroupSubject subject,
        ViewFacetId? defaultFacet,
        InspectionSemanticDemand semanticDemand)
    {
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        DefaultFacet = defaultFacet;
        SemanticDemand = semanticDemand
            ?? throw new ArgumentNullException(nameof(semanticDemand));
    }

    public ResolvedInspectionSource Source { get; }
    public MemberGroupSubject Subject { get; }
    public ViewFacetId? DefaultFacet { get; }
    public InspectionSemanticDemand SemanticDemand { get; }
}

public sealed record MemberContextualExplanationDocument
{
    public MemberContextualExplanationDocument(
        MemberContextualExplanationKind kind,
        ResourceExplanationDocument resource,
        MemberContextualExplanationSubject? subject,
        ViewFacetId? defaultFacet,
        ImmutableArray<string> selectedSections,
        ImmutableArray<RelatedOperationAffordance> relatedOperations)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown contextual Member explanation kind.");
        }
        if ((kind, subject) is not
            ((MemberContextualExplanationKind.Command, null)
            or (MemberContextualExplanationKind.MemberGroup,
                MemberGroupContextualExplanationSubject)
            or (MemberContextualExplanationKind.ExactMember,
                ExactMemberContextualExplanationSubject)))
        {
            throw new ArgumentException(
                "The contextual Member explanation kind and subject "
                    + "must agree.",
                nameof(subject));
        }

        Kind = kind;
        Resource = resource
            ?? throw new ArgumentNullException(nameof(resource));
        Subject = subject;
        DefaultFacet = defaultFacet;
        SelectedSections = selectedSections.IsDefault
            ? []
            : selectedSections;
        RelatedOperations = relatedOperations.IsDefault
            ? []
            : relatedOperations;
    }

    public MemberContextualExplanationKind Kind { get; }
    public ResourceExplanationDocument Resource { get; }
    public MemberContextualExplanationSubject? Subject { get; }
    public ViewFacetId? DefaultFacet { get; }
    public ImmutableArray<string> SelectedSections { get; }
    public ImmutableArray<RelatedOperationAffordance> RelatedOperations { get; }
}

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
                subject: null,
                defaultFacet: null,
                selectedSections: [.. defaultSections],
                relatedOperations: []));
    }

    public static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainMemberGroup(
            ResourceExplanationDocument resource,
            ResolvedMemberGroupExplanationBasis basis,
            IEnumerable<string> defaultSections)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(defaultSections);

        var subject = new MemberGroupContextualExplanationSubject(
            basis.Subject,
            basis.Source.LibraryKey,
            FormatPackage(basis.Source),
            basis.Source.Framework);
        return Envelope(
            new(
                MemberContextualExplanationKind.MemberGroup,
                resource,
                subject,
                basis.DefaultFacet,
                SelectSections(
                    basis.SemanticDemand,
                    defaultSections),
                MemberRelatedOperationAffordances.All));
    }

    public static InspectionEnvelope<MemberContextualExplanationDocument>
        ExplainExactMember(
            ResourceExplanationDocument resource,
            ResolvedMemberInspectionBasis basis,
            IEnumerable<string> defaultSections)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(defaultSections);

        var subject = new ExactMemberContextualExplanationSubject(
            basis.Target.TypeName,
            basis.Target.Member.StableSelector,
            basis.Target.Member.CanonicalSignature,
            basis.Target.Member.Fingerprint,
            basis.Source.LibraryKey,
            FormatPackage(basis.Source),
            basis.Source.Framework);
        return Envelope(
            new(
                MemberContextualExplanationKind.ExactMember,
                resource,
                subject,
                basis.DefaultFacet,
                SelectSections(
                    basis.SemanticDemand,
                    defaultSections),
                MemberRelatedOperationAffordances.All));
    }

    private static ImmutableArray<string> SelectSections(
        InspectionSemanticDemand demand,
        IEnumerable<string> defaultSections) =>
        demand.Sections.IsEmpty
            ? [.. defaultSections]
            : demand.Sections;

    private static string? FormatPackage(
        ResolvedInspectionSource source) =>
        source.Package is { } packageAsset
            ? $"{packageAsset.PackageId}@{packageAsset.PackageVersion}"
            : null;

    private static InspectionEnvelope<MemberContextualExplanationDocument>
        Envelope(MemberContextualExplanationDocument document) =>
        new(
            document,
            new InspectionShare.NonProjectable(
                "member-contextual-explanation",
                "Contextual Member explanations do not yet have a portable "
                    + "Workspace projection."));
}
