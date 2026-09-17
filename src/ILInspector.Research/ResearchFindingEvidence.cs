using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

public enum ResearchFindingEvidenceState
{
    Instruction,
    Method,
    InstructionUnavailable,
}

public sealed class ResearchFindingEvidence :
    IEquatable<ResearchFindingEvidence>
{
    ResearchFindingEvidence(
        MethodIdentity subject,
        ResearchFindingEvidenceState state,
        ImmutableArray<ResearchEvidenceLocation> locations)
    {
        Subject = subject;
        State = state;
        Locations = locations;
    }

    public MethodIdentity Subject { get; }
    public ResearchFindingEvidenceState State { get; }
    public ImmutableArray<ResearchEvidenceLocation> Locations { get; }

    internal static ResearchFindingEvidence? Project(IAnnotation annotation)
        => annotation switch
        {
            Annotation<CallSiteCostEvidence> cost => new(
                cost.Payload.Callee,
                ResearchFindingEvidenceState.Method,
                [cost.Payload.EvidenceLocation]),
            Annotation<CallSiteSemanticsEvidence> semantics =>
                Instructions(
                    semantics.Payload.Callee,
                    semantics.Payload.Coordinates),
            Annotation<CallSiteSafetyEvidence> safety =>
                Instructions(
                    safety.Payload.Callee,
                    safety.Payload.Coordinates),
            _ => null,
        };

    static ResearchFindingEvidence Instructions(
        MethodIdentity subject,
        ImmutableArray<CallSiteEvidenceCoordinate> coordinates)
        => coordinates.IsDefaultOrEmpty
            ? new(
                subject,
                ResearchFindingEvidenceState.InstructionUnavailable,
                [])
            : new(
                subject,
                ResearchFindingEvidenceState.Instruction,
                [.. coordinates.Select(static coordinate =>
                    coordinate.Location)]);

    public bool Equals(ResearchFindingEvidence? other)
        => other is not null
            && Equals(Subject, other.Subject)
            && State == other.State
            && Locations.AsSpan().SequenceEqual(other.Locations.AsSpan());

    public override bool Equals(object? obj)
        => obj is ResearchFindingEvidence other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Subject);
        hash.Add(State);
        foreach (ResearchEvidenceLocation location in Locations)
            hash.Add(location);
        return hash.ToHashCode();
    }
}
