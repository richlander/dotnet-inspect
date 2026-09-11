using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// One resource-free participant outcome from a population type-dependency
/// query.
/// </summary>
public abstract record AssemblyContextTypeDependencyEntry(
    AssemblyContextSubject Subject)
{
    public sealed record Completed(
        AssemblyContextSubject Subject)
        : AssemblyContextTypeDependencyEntry(Subject);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyContextTypeDependencyEntry(Subject);
}

/// <summary>
/// Population-scoped type-dependency graph facts and ordered participant
/// outcomes.
/// </summary>
public sealed record AssemblyContextTypeDependencyResult(
    TypeDependencyResult Dependency,
    ImmutableArray<AssemblyContextTypeDependencyEntry> Participants)
{
    public bool HasSurvivingParticipant =>
        Participants.Any(
            static participant =>
                participant
                    is AssemblyContextTypeDependencyEntry.Completed);

    public bool IsComplete =>
        HasSurvivingParticipant
        && Participants.All(
            static participant =>
                participant
                    is AssemblyContextTypeDependencyEntry.Completed);
}

/// <summary>
/// Scans type dependencies once over the successfully retained participants of
/// one binding-consistent assembly context group.
/// </summary>
public static class AssemblyContextTypeDependencyQuery
{
    public static InspectionQuery<AssemblyContextTypeDependencyResult>
        Definition { get; } =
        new(
            "Assembly context type dependencies",
            InspectionCost.Unbounded);

    public static AssemblyContextTypeDependencyResult Execute(
        AssemblyContextGroup group,
        string targetType,
        int? maximumDepth = null) =>
        ExecuteCore(
            group,
            rootParticipant: null,
            targetType,
            maximumDepth);

    /// <summary>
    /// Scans the complete group while selecting the target type from one exact
    /// participant. The selected participant is staged first so another
    /// participant's same-named type cannot become the dependency root.
    /// Participant-qualified lookup requires the exact normalized type name,
    /// and the Metadata-issued match registration verifies that the selected
    /// participant contributed the root. Published outcomes retain the group's
    /// committed participant order.
    /// </summary>
    public static AssemblyContextTypeDependencyResult ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant rootParticipant,
        string targetType,
        int? maximumDepth = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(rootParticipant);
        if (!group.Participants.Any(
                participant =>
                    ReferenceEquals(participant, rootParticipant)))
        {
            throw new ArgumentException(
                "The selected type-dependency participant does not belong "
                    + "to the assembly context group.",
                nameof(rootParticipant));
        }

        return ExecuteCore(
            group,
            rootParticipant,
            targetType,
            maximumDepth);
    }

    static AssemblyContextTypeDependencyResult ExecuteCore(
        AssemblyContextGroup group,
        AssemblyContextParticipant? rootParticipant,
        string targetType,
        int? maximumDepth)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

        var retained =
            ImmutableArray.CreateBuilder<ResolvedAssemblyReference>(
                group.Participants.Length);
        var subjects =
            ImmutableArray.CreateBuilder<AssemblyContextSubject>(
                group.Participants.Length);
        var acquisitionFailures =
            new Dictionary<
                AssemblyAcquisitionRegistration,
                CandidateOpenFailure>(
                    ReferenceEqualityComparer.Instance);

        AssemblyContextParticipant[] scanOrder =
            rootParticipant is null
                ? [.. group.Participants]
                :
                [
                    rootParticipant,
                    .. group.Participants.Where(participant =>
                        !ReferenceEquals(
                            participant,
                            rootParticipant)),
                ];
        foreach (AssemblyContextParticipant participant in scanOrder)
        {
            var subject =
                new AssemblyContextSubject(participant.Assembly);

            AssemblyImageAccessResult<ResolvedAssemblyReference> access =
                group.RetainAssemblyReference(
                    participant.Assembly);
            switch (access)
            {
                case AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Available available:
                    retained.Add(available.Value);
                    break;
                case AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Rejected rejected:
                    acquisitionFailures.Add(
                        subject.Registration,
                        rejected.Failure);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown participant image-access result.");
            }
        }
        foreach (AssemblyContextParticipant participant
                 in group.Participants)
        {
            subjects.Add(
                new AssemblyContextSubject(participant.Assembly));
        }

        TypeDependencyPopulationResult population =
            rootParticipant is null
                ? TypeDependencyScanner.BuildDependencyPopulation(
                    targetType,
                    retained.ToImmutable(),
                    maximumDepth)
                : TypeDependencyScanner.BuildExactDependencyPopulation(
                    targetType,
                    retained.ToImmutable(),
                    maximumDepth);
        var metadataOutcomes =
            new Dictionary<
                AssemblyAcquisitionRegistration,
                TypeDependencyCandidateOutcome>(
                    ReferenceEqualityComparer.Instance);
        foreach (TypeDependencyCandidateOutcome outcome
            in population.Candidates)
        {
            if (!metadataOutcomes.TryAdd(
                    outcome.Registration,
                    outcome))
            {
                throw new InspectionQueryException(
                    "Metadata returned duplicate type-dependency candidate outcomes.");
            }
        }
        bool rootMatched = rootParticipant is null
            || ReferenceEquals(
                population.MatchedRegistration,
                rootParticipant.Assembly.Registration);

        var candidates =
            ImmutableArray.CreateBuilder<
                AssemblyContextTypeDependencyEntry>(
                    subjects.Count);
        foreach (AssemblyContextSubject subject in subjects)
        {
            if (acquisitionFailures.TryGetValue(
                    subject.Registration,
                    out CandidateOpenFailure? acquisitionFailure))
            {
                candidates.Add(
                    new AssemblyContextTypeDependencyEntry.Rejected(
                        subject,
                        acquisitionFailure));
                continue;
            }

            if (!metadataOutcomes.Remove(
                    subject.Registration,
                    out TypeDependencyCandidateOutcome? metadataOutcome))
            {
                throw new InspectionQueryException(
                    "Metadata did not return a type-dependency outcome for a retained participant.");
            }

            candidates.Add(
                metadataOutcome switch
                {
                    TypeDependencyCandidateOutcome.Completed =>
                        new AssemblyContextTypeDependencyEntry.Completed(
                            subject),
                    TypeDependencyCandidateOutcome.Rejected rejected =>
                        new AssemblyContextTypeDependencyEntry.Rejected(
                            subject,
                            rejected.Failure),
                    _ => throw new InvalidOperationException(
                        "Unknown Metadata type-dependency candidate outcome."),
                });
        }

        if (metadataOutcomes.Count != 0)
        {
            throw new InspectionQueryException(
                "Metadata returned a type-dependency outcome outside the assembly context group.");
        }

        ImmutableArray<AssemblyContextTypeDependencyEntry> outcomes =
            candidates.MoveToImmutable();
        return new AssemblyContextTypeDependencyResult(
            rootMatched
                ? population.Dependency
                : new TypeDependencyResult(null, []),
            outcomes);
    }
}
