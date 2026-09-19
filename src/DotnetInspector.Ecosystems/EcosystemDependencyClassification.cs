using System.Collections.Immutable;

namespace DotnetInspector.Ecosystems;

/// <summary>Profile-relative counts for one recognition operation.</summary>
public sealed record EcosystemDependencyClassificationSummary(
    int CandidateEcosystemCount,
    int PackageAssociationCount,
    int AssemblyAssociationCount,
    int ObservationCount,
    int RecognizedObservationCount,
    int UnrecognizedObservationCount,
    int RecognizedEcosystemCount,
    int RecognitionCount);

/// <summary>
/// One direct-dependency occurrence paired with one recognized ecosystem.
/// </summary>
public sealed record EcosystemDependencyRecognitionEntry
{
    internal EcosystemDependencyRecognitionEntry(
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

    public bool Equals(EcosystemDependencyRecognitionEntry? other) =>
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
/// Reusable resource-free classification over one direct-observation batch.
/// </summary>
public sealed record EcosystemDependencyClassification
{
    internal EcosystemDependencyClassification(
        EcosystemDependencyClassificationSummary summary,
        ImmutableArray<EcosystemDependencyDescriptor> recognizedEcosystems,
        ImmutableArray<EcosystemDependencyRecognitionEntry> recognized,
        ImmutableArray<EcosystemDependencyObservation> unrecognized)
    {
        Summary = summary;
        RecognizedEcosystems = recognizedEcosystems;
        Recognized = recognized;
        Unrecognized = unrecognized;
    }

    public EcosystemDependencyClassificationSummary Summary { get; }

    public ImmutableArray<EcosystemDependencyDescriptor>
        RecognizedEcosystems { get; }

    public ImmutableArray<EcosystemDependencyRecognitionEntry> Recognized
        { get; }

    public ImmutableArray<EcosystemDependencyObservation> Unrecognized
        { get; }

    public bool Equals(EcosystemDependencyClassification? other) =>
        ReferenceEquals(this, other)
        || other is not null
            && Summary == other.Summary
            && RecognizedEcosystems.SequenceEqual(
                other.RecognizedEcosystems)
            && Recognized.SequenceEqual(other.Recognized)
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
        foreach (EcosystemDependencyRecognitionEntry recognition in
                 Recognized)
        {
            hash.Add(recognition);
        }
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
        var recognizedEcosystems =
            ImmutableArray.CreateBuilder<EcosystemDependencyDescriptor>();
        var recognizedObservations =
            new HashSet<EcosystemDependencyObservationIdentity>();

        foreach (EcosystemDependencyProfileEntry profileEntry in profile.Entries)
        {
            bool ecosystemRecognized = false;
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

                ecosystemRecognized = true;
                recognizedObservations.Add(observation.Identity);
                recognized.Add(new EcosystemDependencyRecognitionEntry(
                    observation,
                    profileEntry.Ecosystem,
                    matchingAssociations));
            }

            if (ecosystemRecognized)
                recognizedEcosystems.Add(profileEntry.Ecosystem);
        }

        ImmutableArray<EcosystemDependencyObservation> unrecognized =
        [
            .. population.Where(observation =>
                !recognizedObservations.Contains(observation.Identity)),
        ];
        var summary = new EcosystemDependencyClassificationSummary(
            profile.Entries.Length,
            profile.PackageAssociationCount,
            profile.AssemblyAssociationCount,
            population.Length,
            recognizedObservations.Count,
            unrecognized.Length,
            recognizedEcosystems.Count,
            recognized.Count);

        return new EcosystemDependencyClassification(
            summary,
            recognizedEcosystems.ToImmutable(),
            recognized.ToImmutable(),
            unrecognized);
    }
}
