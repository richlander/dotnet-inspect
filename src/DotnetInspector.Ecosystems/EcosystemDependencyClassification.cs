using System.Collections.Immutable;

namespace DotnetInspector.Ecosystems;

/// <summary>Profile-relative counts for one recognition operation.</summary>
public sealed record EcosystemDependencyClassificationSummary(
    int CandidateEcosystemCount,
    int PackageAssociationCount,
    int AssemblyAssociationCount,
    int AssemblyEvidenceCount,
    int ObservationCount,
    int RecognizedObservationCount,
    int CandidateOnlyObservationCount,
    int UnrecognizedObservationCount,
    int RecognizedEcosystemCount,
    int RecognitionCount,
    int CandidateCount);

/// <summary>
/// One direct-dependency occurrence paired with one product ecosystem whose
/// authored association matched it.
/// </summary>
public abstract record EcosystemDependencyMatchEntry
{
    private protected EcosystemDependencyMatchEntry(
        EcosystemDependencyObservation observation,
        EcosystemDependencyDescriptor ecosystem,
        ImmutableArray<EcosystemDependencyAssociation> matchingAssociations)
    {
        Observation = observation;
        Ecosystem = ecosystem;
        MatchingAssociations = matchingAssociations;
    }

    public EcosystemDependencyObservation Observation { get; }

    public EcosystemDependencyDescriptor Ecosystem { get; }

    public ImmutableArray<EcosystemDependencyAssociation>
        MatchingAssociations { get; }

    public virtual bool Equals(EcosystemDependencyMatchEntry? other) =>
        ReferenceEquals(this, other)
        || other is not null
            && Observation == other.Observation
            && Ecosystem == other.Ecosystem
            && MatchingAssociations.SequenceEqual(
                other.MatchingAssociations);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Observation);
        hash.Add(Ecosystem);
        foreach (EcosystemDependencyAssociation association in
                 MatchingAssociations)
        {
            hash.Add(association);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// One direct-dependency occurrence paired with one recognized ecosystem.
/// Assembly observations also retain the exact Package AssemblyDef evidence
/// that corroborated their authored association.
/// </summary>
public sealed record EcosystemDependencyRecognitionEntry
    : EcosystemDependencyMatchEntry
{
    internal EcosystemDependencyRecognitionEntry(
        EcosystemDependencyObservation observation,
        EcosystemDependencyDescriptor ecosystem,
        ImmutableArray<EcosystemDependencyAssociation> matchingAssociations,
        ImmutableArray<EcosystemAssemblyDefinitionEvidence>
            matchingAssemblyEvidence)
        : base(observation, ecosystem, matchingAssociations) =>
        MatchingAssemblyEvidence = matchingAssemblyEvidence;

    public ImmutableArray<EcosystemAssemblyDefinitionEvidence>
        MatchingAssemblyEvidence { get; }

    public bool Equals(EcosystemDependencyRecognitionEntry? other) =>
        base.Equals(other)
        && MatchingAssemblyEvidence.SequenceEqual(
            other!.MatchingAssemblyEvidence);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        foreach (EcosystemAssemblyDefinitionEvidence evidence in
                 MatchingAssemblyEvidence)
        {
            hash.Add(evidence);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// One assembly-reference occurrence whose authored ecosystem association
/// matched but whose shipped Package AssemblyDef evidence did not corroborate
/// the reference identity.
/// </summary>
public sealed record EcosystemDependencyCandidateEntry
    : EcosystemDependencyMatchEntry
{
    internal EcosystemDependencyCandidateEntry(
        EcosystemDependencyObservation.AssemblyReference observation,
        EcosystemDependencyDescriptor ecosystem,
        ImmutableArray<EcosystemDependencyAssociation> matchingAssociations)
        : base(observation, ecosystem, matchingAssociations)
    {
    }
}

/// <summary>
/// Reusable resource-free classification over one direct-observation batch.
/// </summary>
public sealed record EcosystemDependencyClassification
{
    internal EcosystemDependencyClassification(
        EcosystemDependencyClassificationSummary summary,
        ImmutableArray<EcosystemDependencyDescriptor> recognizedEcosystems,
        ImmutableArray<EcosystemDependencyDescriptor> candidateEcosystems,
        ImmutableArray<EcosystemDependencyMatchEntry> matches,
        ImmutableArray<EcosystemDependencyRecognitionEntry> recognized,
        ImmutableArray<EcosystemDependencyCandidateEntry> candidates,
        ImmutableArray<EcosystemDependencyObservation> unrecognized)
    {
        Summary = summary;
        RecognizedEcosystems = recognizedEcosystems;
        CandidateEcosystems = candidateEcosystems;
        Matches = matches;
        Recognized = recognized;
        Candidates = candidates;
        Unrecognized = unrecognized;
    }

    public EcosystemDependencyClassificationSummary Summary { get; }

    public ImmutableArray<EcosystemDependencyDescriptor>
        RecognizedEcosystems { get; }

    public ImmutableArray<EcosystemDependencyDescriptor>
        CandidateEcosystems { get; }

    public ImmutableArray<EcosystemDependencyMatchEntry> Matches { get; }

    public ImmutableArray<EcosystemDependencyRecognitionEntry> Recognized
        { get; }

    public ImmutableArray<EcosystemDependencyCandidateEntry> Candidates
        { get; }

    public ImmutableArray<EcosystemDependencyObservation> Unrecognized
        { get; }

    public bool Equals(EcosystemDependencyClassification? other) =>
        ReferenceEquals(this, other)
        || other is not null
            && Summary == other.Summary
            && RecognizedEcosystems.SequenceEqual(
                other.RecognizedEcosystems)
            && CandidateEcosystems.SequenceEqual(
                other.CandidateEcosystems)
            && Matches.SequenceEqual(other.Matches)
            && Recognized.SequenceEqual(other.Recognized)
            && Candidates.SequenceEqual(other.Candidates)
            && Unrecognized.SequenceEqual(other.Unrecognized);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Summary);
        foreach (EcosystemDependencyDescriptor ecosystem in
                 RecognizedEcosystems)
        {
            hash.Add(ecosystem);
        }
        foreach (EcosystemDependencyDescriptor ecosystem in
                 CandidateEcosystems)
        {
            hash.Add(ecosystem);
        }
        foreach (EcosystemDependencyMatchEntry match in Matches)
            hash.Add(match);
        foreach (EcosystemDependencyRecognitionEntry recognition in
                 Recognized)
        {
            hash.Add(recognition);
        }
        foreach (EcosystemDependencyCandidateEntry candidate in Candidates)
            hash.Add(candidate);
        foreach (EcosystemDependencyObservation observation in Unrecognized)
            hash.Add(observation);

        return hash.ToHashCode();
    }
}

/// <summary>
/// Deterministic matching of direct dependencies against one validated
/// product profile.
/// </summary>
public static class EcosystemDependencyClassifier
{
    public static EcosystemDependencyClassification Classify(
        EcosystemDependencyRecognitionProfile profile,
        IEnumerable<EcosystemDependencyObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(observations);

        EcosystemDependencyObservation[] population = [.. observations];
        var identities =
            new HashSet<EcosystemDependencyObservationIdentity>();
        int previousSourceOrder = 0;
        foreach (EcosystemDependencyObservation observation in population)
        {
            if (observation is null)
            {
                throw new ArgumentException(
                    "An ecosystem dependency observation population cannot contain null entries.",
                    nameof(observations));
            }
            if (!identities.Add(observation.Identity))
            {
                throw new ArgumentException(
                    $"Observation identity '{observation.Identity}' occurs more than once.",
                    nameof(observations));
            }
            if (observation.SourceOrder <= previousSourceOrder)
            {
                throw new ArgumentException(
                    "Ecosystem dependency observations must use strictly ascending source order.",
                    nameof(observations));
            }

            previousSourceOrder = observation.SourceOrder;
        }

        var recognized =
            ImmutableArray.CreateBuilder<EcosystemDependencyRecognitionEntry>();
        var candidates =
            ImmutableArray.CreateBuilder<EcosystemDependencyCandidateEntry>();
        var matches =
            ImmutableArray.CreateBuilder<EcosystemDependencyMatchEntry>();
        var recognizedEcosystems =
            ImmutableArray.CreateBuilder<EcosystemDependencyDescriptor>();
        var candidateEcosystems =
            ImmutableArray.CreateBuilder<EcosystemDependencyDescriptor>();
        var recognizedObservations =
            new HashSet<EcosystemDependencyObservationIdentity>();
        var candidateObservations =
            new HashSet<EcosystemDependencyObservationIdentity>();
        var matchedObservations =
            new HashSet<EcosystemDependencyObservationIdentity>();

        foreach (EcosystemDependencyProfileEntry profileEntry in profile.Entries)
        {
            bool ecosystemRecognized = false;
            bool ecosystemCandidate = false;
            foreach (EcosystemDependencyObservation observation in population)
            {
                ImmutableArray<EcosystemDependencyAssociation>
                    matchingAssociations =
                [
                    .. profileEntry.Associations.Where(association =>
                        association.Matches(
                            observation.Domain,
                            observation.MatchValue)),
                ];
                if (matchingAssociations.IsEmpty)
                    continue;

                matchedObservations.Add(observation.Identity);
                if (observation is
                    EcosystemDependencyObservation.AssemblyReference assembly)
                {
                    ImmutableArray<EcosystemAssemblyDefinitionEvidence>
                        matchingEvidence =
                    [
                        .. profileEntry.AssemblyEvidence.Where(
                            evidence => evidence.Matches(assembly.Reference)),
                    ];
                    if (matchingEvidence.IsEmpty)
                    {
                        ecosystemCandidate = true;
                        candidateObservations.Add(observation.Identity);
                        var candidate =
                            new EcosystemDependencyCandidateEntry(
                                assembly,
                                profileEntry.Ecosystem,
                                matchingAssociations);
                        candidates.Add(candidate);
                        matches.Add(candidate);
                        continue;
                    }

                    ecosystemRecognized = true;
                    recognizedObservations.Add(observation.Identity);
                    var recognition =
                        new EcosystemDependencyRecognitionEntry(
                            observation,
                            profileEntry.Ecosystem,
                            matchingAssociations,
                            matchingEvidence);
                    recognized.Add(recognition);
                    matches.Add(recognition);
                    continue;
                }

                ecosystemRecognized = true;
                recognizedObservations.Add(observation.Identity);
                var packageRecognition =
                    new EcosystemDependencyRecognitionEntry(
                        observation,
                        profileEntry.Ecosystem,
                        matchingAssociations,
                        []);
                recognized.Add(packageRecognition);
                matches.Add(packageRecognition);
            }

            if (ecosystemRecognized)
                recognizedEcosystems.Add(profileEntry.Ecosystem);
            if (ecosystemCandidate)
                candidateEcosystems.Add(profileEntry.Ecosystem);
        }

        ImmutableArray<EcosystemDependencyObservation> unrecognized =
        [
            .. population.Where(observation =>
                !matchedObservations.Contains(observation.Identity)),
        ];
        var summary = new EcosystemDependencyClassificationSummary(
            profile.Entries.Length,
            profile.PackageAssociationCount,
            profile.AssemblyAssociationCount,
            profile.AssemblyEvidenceCount,
            population.Length,
            recognizedObservations.Count,
            candidateObservations.Count(identity =>
                !recognizedObservations.Contains(identity)),
            unrecognized.Length,
            recognizedEcosystems.Count,
            recognized.Count,
            candidates.Count);

        return new EcosystemDependencyClassification(
            summary,
            recognizedEcosystems.ToImmutable(),
            candidateEcosystems.ToImmutable(),
            matches.ToImmutable(),
            recognized.ToImmutable(),
            candidates.ToImmutable(),
            unrecognized);
    }
}
