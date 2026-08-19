using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>How explicit subjects admit relationship evidence.</summary>
public enum InspectionGraphInducedSetAdmissionRule
{
    BothEndpointsWithinSubjectClosure,
}

/// <summary>
/// A finite set of typed subjects whose closure induces selected relationships.
/// </summary>
public sealed class InspectionGraphInducedSetRequest
{
    public InspectionGraphInducedSetRequest(
        IEnumerable<InspectionGraphSubject> subjects,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        InspectionGraphInducedSetAdmissionRule admissionRule)
    {
        InspectionGraphCollections.RequireDefined(
            admissionRule,
            nameof(admissionRule));
        Subjects = InspectionGraphCollections.Snapshot(
            subjects,
            nameof(subjects));
        if (Subjects.IsEmpty)
        {
            throw new ArgumentException(
                "An explicit induced set requires at least one subject.",
                nameof(subjects));
        }
        if (Subjects.Distinct().Count() != Subjects.Length)
        {
            throw new ArgumentException(
                "Explicit induced-set subjects must be distinct.",
                nameof(subjects));
        }

        Relationships = InspectionGraphCollections.Snapshot(
            relationships,
            nameof(relationships));
        if (Relationships.IsEmpty)
        {
            throw new ArgumentException(
                "An explicit induced set requires at least one relationship.",
                nameof(relationships));
        }
        if (Relationships.Distinct().Count() != Relationships.Length
            || Relationships.Select(static relationship => relationship.Id)
                .Distinct(StringComparer.Ordinal).Count()
                != Relationships.Length)
        {
            throw new ArgumentException(
                "Selected relationships must have distinct identities and ids.",
                nameof(relationships));
        }

        ModeRequest = InspectionGraphModeRequest.InducedSet(
            InspectionGraphInducedSetRule.ExplicitSubjects);
        AdmissionRule = admissionRule;
    }

    public InspectionGraphModeRequest ModeRequest { get; }
    public ImmutableArray<InspectionGraphSubject> Subjects { get; }
    public ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships { get; }
    public InspectionGraphInducedSetAdmissionRule AdmissionRule { get; }
}

/// <summary>Explicit-induced-set-owned graph contracts.</summary>
public static class InspectionGraphInducedSetCatalog
{
    public static InspectionGraphEvidenceDescriptor SubjectBoundEvidence
        { get; } =
        new(
            "queries.induced-subject-bound",
            InspectionGraphOwner.Queries);

    public static InspectionGraphLimitDescriptor SubjectBound { get; } =
        new(
            "queries.induced-subject-bound",
            InspectionGraphOwner.Queries,
            [SubjectBoundEvidence]);
}

/// <summary>The number of typed subjects bounding an induced projection.</summary>
public sealed record InspectionGraphInducedSubjectBoundEvidence
    : IInspectionGraphDiagnosticEvidence
{
    public InspectionGraphInducedSubjectBoundEvidence(int subjectCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(subjectCount, 1);
        SubjectCount = subjectCount;
    }

    public int SubjectCount { get; }

    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphInducedSetCatalog.SubjectBoundEvidence;
}
