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
        int? maximumDepth = null)
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

        foreach (AssemblyContextParticipant participant
            in group.Participants)
        {
            var subject =
                new AssemblyContextSubject(participant.Assembly);
            subjects.Add(subject);

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

        TypeDependencyPopulationResult population =
            TypeDependencyScanner.BuildDependencyPopulation(
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
            population.Dependency,
            outcomes);
    }
}
