using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>How requested subjects establish inspection-graph focus.</summary>
public enum InspectionGraphMode
{
    SingleSeed,
    PeerSeeds,
    InducedSet,
}

/// <summary>How an induced graph obtains its bounded input set.</summary>
public enum InspectionGraphInducedSetRule
{
    DocumentSubjects,
    WorkspaceParticipants,
}

/// <summary>
/// The mode axis of an inspection-graph request.
/// </summary>
public sealed class InspectionGraphModeRequest
{
    InspectionGraphModeRequest(
        InspectionGraphMode mode,
        ImmutableArray<InspectionGraphSubject> seeds,
        InspectionGraphInducedSetRule? inducedSetRule)
    {
        InspectionGraphCollections.RequireDefined(mode, nameof(mode));
        if (seeds.Any(static seed => seed is null))
            throw new ArgumentException("Seed subjects cannot be null.", nameof(seeds));
        if (seeds.Distinct().Count() != seeds.Length)
            throw new ArgumentException("Seed subjects must be distinct.", nameof(seeds));

        switch (mode)
        {
            case InspectionGraphMode.SingleSeed when seeds.Length != 1:
                throw new ArgumentException(
                    "Single-seed mode requires exactly one seed.",
                    nameof(seeds));
            case InspectionGraphMode.PeerSeeds when seeds.Length < 2:
                throw new ArgumentException(
                    "Peer-seed mode requires at least two seeds.",
                    nameof(seeds));
            case InspectionGraphMode.InducedSet when !seeds.IsEmpty:
                throw new ArgumentException(
                    "Induced-set mode cannot declare seed subjects.",
                    nameof(seeds));
        }
        if (mode == InspectionGraphMode.InducedSet)
        {
            if (inducedSetRule is null)
                throw new ArgumentNullException(nameof(inducedSetRule));
            InspectionGraphCollections.RequireDefined(
                inducedSetRule.Value,
                nameof(inducedSetRule));
        }
        else if (inducedSetRule is not null)
        {
            throw new ArgumentException(
                "Only induced-set mode declares an input-set rule.",
                nameof(inducedSetRule));
        }

        Mode = mode;
        Seeds = seeds;
        InducedSetRule = inducedSetRule;
    }

    public InspectionGraphMode Mode { get; }
    public ImmutableArray<InspectionGraphSubject> Seeds { get; }
    public InspectionGraphInducedSetRule? InducedSetRule { get; }

    public static InspectionGraphModeRequest SingleSeed(
        InspectionGraphSubject seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return new(
            InspectionGraphMode.SingleSeed,
            [seed],
            inducedSetRule: null);
    }

    public static InspectionGraphModeRequest PeerSeeds(
        IEnumerable<InspectionGraphSubject> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        return new(
            InspectionGraphMode.PeerSeeds,
            [.. seeds],
            inducedSetRule: null);
    }

    public static InspectionGraphModeRequest InducedSet(
        InspectionGraphInducedSetRule rule) =>
        new(
            InspectionGraphMode.InducedSet,
            [],
            rule);
}

internal enum InspectionGraphSeedTargetPreference
{
    Node,
    Group,
}

internal static class InspectionGraphSeedBinder
{
    internal static ImmutableArray<InspectionGraphSeed> Bind(
        InspectionGraphModeRequest request,
        IReadOnlyList<InspectionGraphNode> nodes,
        IReadOnlyList<InspectionGraphGroup> groups,
        InspectionGraphSeedTargetPreference preference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(groups);
        InspectionGraphCollections.RequireDefined(preference, nameof(preference));
        if (request.Mode == InspectionGraphMode.InducedSet)
            return [];

        var bindings = ImmutableArray.CreateBuilder<InspectionGraphSeed>(
            request.Seeds.Length);
        InspectionGraphSeedRole role =
            request.Mode == InspectionGraphMode.SingleSeed
                ? InspectionGraphSeedRole.Primary
                : InspectionGraphSeedRole.Peer;
        foreach (InspectionGraphSubject subject in request.Seeds)
        {
            InspectionGraphNode? node = nodes.SingleOrDefault(
                candidate => candidate.Subject == subject);
            InspectionGraphGroup? group = groups.SingleOrDefault(
                candidate => candidate.Subject == subject);
            InspectionGraphTarget target = (node, group, preference) switch
            {
                ({ } match, null, _) =>
                    InspectionGraphTarget.Node(match.Id),
                (null, { } match, _) =>
                    InspectionGraphTarget.Group(match.Id),
                (
                    { } match,
                    not null,
                    InspectionGraphSeedTargetPreference.Node) =>
                    InspectionGraphTarget.Node(match.Id),
                (
                    not null,
                    { } match,
                    InspectionGraphSeedTargetPreference.Group) =>
                    InspectionGraphTarget.Group(match.Id),
                _ => throw new InspectionQueryException(
                    $"The requested {subject.Kind.ToString().ToLowerInvariant()} "
                    + "seed is not present in this graph. Add the subject to "
                    + "workspace scope or select a relationship and lens that "
                    + "admit it."),
            };
            bindings.Add(new InspectionGraphSeed(subject, target, role));
        }

        return bindings.ToImmutable();
    }
}
