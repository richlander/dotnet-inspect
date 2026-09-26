using System.Collections.Immutable;

namespace ILInspector.Analysis.Planning;

/// <summary>The terminal a request asks of a producer.</summary>
public enum ProducerTerminal
{
    /// <summary>Every unit in scope contributes to the result.</summary>
    All,

    /// <summary>
    /// The request is settled by the first unit fact the producer reports as
    /// settling; no later unit is visited for it.
    /// </summary>
    Exists,
}

/// <summary>One requested producer and the terminal the requester needs.</summary>
public sealed record ProducerRequest(
    ProducerDeclaration Producer,
    ProducerTerminal Terminal = ProducerTerminal.All);

public enum ProducerRejectionReason
{
    MissingDependency,
    DependencyCycle,
    UpwardTierDependency,
    ConflictingParameters,
}

/// <summary>One typed reason a request was rejected as a whole.</summary>
public sealed record ProducerRejection(
    string Producer,
    ProducerRejectionReason Reason);

/// <summary>
/// The planner's output: the closed producer set in dependency order, the
/// pass that holds each producer's visits and completion, and each
/// producer's effective terminal. It is computed without reading any subject.
/// </summary>
public sealed class WorkDescription
{
    internal WorkDescription(
        ImmutableArray<ProducerDeclaration> producers,
        ImmutableDictionary<ProducerDeclaration, ProducerTerminal> terminals,
        ImmutableDictionary<ProducerDeclaration, int> visitPasses,
        ImmutableDictionary<ProducerDeclaration, int> completionPasses,
        ImmutableHashSet<ProducerDeclaration> requested)
    {
        Producers = producers;
        _terminals = terminals;
        _visitPasses = visitPasses;
        _completionPasses = completionPasses;
        _requested = requested;
        PassCount = completionPasses.Values.DefaultIfEmpty(0).Max();
    }

    readonly ImmutableDictionary<ProducerDeclaration, ProducerTerminal>
        _terminals;
    readonly ImmutableDictionary<ProducerDeclaration, int> _visitPasses;
    readonly ImmutableDictionary<ProducerDeclaration, int>
        _completionPasses;
    readonly ImmutableHashSet<ProducerDeclaration> _requested;

    /// <summary>Every planned producer, in dependency order with identity ties.</summary>
    public ImmutableArray<ProducerDeclaration> Producers { get; }

    public int PassCount { get; }

    public bool Contains(ProducerDeclaration producer) =>
        _terminals.ContainsKey(producer);

    public bool WasRequested(ProducerDeclaration producer) =>
        _requested.Contains(producer);

    public ProducerTerminal TerminalOf(ProducerDeclaration producer) =>
        _terminals[producer];

    public int VisitPassOf(ProducerDeclaration producer) =>
        _visitPasses[producer];

    public int CompletionPassOf(ProducerDeclaration producer) =>
        _completionPasses[producer];
}

public abstract record ProducerPlanResult
{
    private ProducerPlanResult()
    {
    }

    public sealed record Accepted(WorkDescription Description)
        : ProducerPlanResult;

    public sealed record Rejected(ImmutableArray<ProducerRejection> Reasons)
        : ProducerPlanResult;
}

/// <summary>
/// Closes requested declarations over their dependencies and validates the
/// whole closure before any work, as owned by
/// <c>docs/design/producer-planning.md#planning</c>.
/// </summary>
public static class ProducerPlanner
{
    public static ProducerPlanResult Plan(
        IReadOnlyList<ProducerRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var rejections = ImmutableArray.CreateBuilder<ProducerRejection>();
        var byIdentity = new Dictionary<string, ProducerDeclaration>(
            StringComparer.Ordinal);
        var closure = new List<ProducerDeclaration>();
        var seen = new HashSet<ProducerDeclaration>(
            ReferenceEqualityComparer.Instance);
        var terminals = new Dictionary<ProducerDeclaration, ProducerTerminal>(
            ReferenceEqualityComparer.Instance);
        var requested = new HashSet<ProducerDeclaration>(
            ReferenceEqualityComparer.Instance);

        foreach (ProducerRequest request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Producer);
            requested.Add(request.Producer);
            Close(request.Producer);
            terminals[request.Producer] =
                terminals.TryGetValue(
                    request.Producer,
                    out ProducerTerminal existing)
                && existing == ProducerTerminal.All
                    ? ProducerTerminal.All
                    : request.Terminal;
        }

        // A dependency is needed in full by its dependent.
        foreach (ProducerDeclaration producer in closure)
        {
            if (!requested.Contains(producer))
                terminals[producer] = ProducerTerminal.All;
            foreach (ProducerDependency dependency in producer.Dependencies)
            {
                if (dependency.Producer is { } target)
                    terminals[target] = ProducerTerminal.All;
            }
        }

        if (rejections.Count > 0)
        {
            return new ProducerPlanResult.Rejected(
                Distinct(rejections));
        }

        ImmutableArray<ProducerDeclaration> ordered =
            Order(closure, rejections);
        if (rejections.Count > 0)
        {
            return new ProducerPlanResult.Rejected(
                Distinct(rejections));
        }

        var visitPasses = new Dictionary<ProducerDeclaration, int>(
            ReferenceEqualityComparer.Instance);
        var completionPasses = new Dictionary<ProducerDeclaration, int>(
            ReferenceEqualityComparer.Instance);
        foreach (ProducerDeclaration producer in ordered)
        {
            int visitPass = 1;
            foreach (ProducerDependency dependency in producer.Dependencies)
            {
                visitPass = dependency.Kind switch
                {
                    ProducerDependencyKind.VisitNeedsVisit =>
                        Math.Max(visitPass, visitPasses[dependency.Producer]),
                    ProducerDependencyKind.VisitNeedsResult =>
                        Math.Max(
                            visitPass,
                            completionPasses[dependency.Producer] + 1),
                    _ => visitPass,
                };
            }

            int completionPass = visitPass;
            foreach (ProducerDependency dependency in producer.Dependencies)
            {
                completionPass = Math.Max(
                    completionPass,
                    dependency.Kind == ProducerDependencyKind.VisitNeedsVisit
                        ? visitPasses[dependency.Producer]
                        : completionPasses[dependency.Producer]);
            }

            visitPasses[producer] = visitPass;
            completionPasses[producer] = completionPass;
        }

        return new ProducerPlanResult.Accepted(
            new WorkDescription(
                ordered,
                terminals.ToImmutableDictionary<ProducerDeclaration, ProducerTerminal>(
                    ReferenceEqualityComparer.Instance),
                visitPasses.ToImmutableDictionary<ProducerDeclaration, int>(
                    ReferenceEqualityComparer.Instance),
                completionPasses.ToImmutableDictionary<ProducerDeclaration, int>(
                    ReferenceEqualityComparer.Instance),
                requested.ToImmutableHashSet<ProducerDeclaration>(
                    ReferenceEqualityComparer.Instance)));

        void Close(ProducerDeclaration producer)
        {
            if (!seen.Add(producer))
                return;
            if (byIdentity.TryGetValue(producer.Identity, out var other)
                && !ReferenceEquals(other, producer))
            {
                if (!string.Equals(
                        other.Parameters,
                        producer.Parameters,
                        StringComparison.Ordinal))
                {
                    rejections.Add(new(
                        producer.Identity,
                        ProducerRejectionReason.ConflictingParameters));
                }
                return;
            }

            byIdentity[producer.Identity] = producer;
            closure.Add(producer);
            foreach (ProducerDependency dependency in producer.Dependencies)
            {
                if (dependency?.Producer is not { } target)
                {
                    rejections.Add(new(
                        producer.Identity,
                        ProducerRejectionReason.MissingDependency));
                    continue;
                }

                if (target.Tier > producer.Tier)
                {
                    rejections.Add(new(
                        producer.Identity,
                        ProducerRejectionReason.UpwardTierDependency));
                }

                Close(target);
            }
        }
    }

    static ImmutableArray<ProducerDeclaration> Order(
        IReadOnlyList<ProducerDeclaration> closure,
        ImmutableArray<ProducerRejection>.Builder rejections)
    {
        var ordered = ImmutableArray.CreateBuilder<ProducerDeclaration>(
            closure.Count);
        var state = new Dictionary<ProducerDeclaration, int>(
            ReferenceEqualityComparer.Instance);
        foreach (ProducerDeclaration producer in closure.OrderBy(
                     static producer => producer.Identity,
                     StringComparer.Ordinal))
        {
            Visit(producer);
        }

        return ordered.ToImmutable();

        void Visit(ProducerDeclaration producer)
        {
            if (state.TryGetValue(producer, out int mark))
            {
                if (mark == 1)
                {
                    rejections.Add(new(
                        producer.Identity,
                        ProducerRejectionReason.DependencyCycle));
                }
                return;
            }

            state[producer] = 1;
            foreach (ProducerDependency dependency in producer.Dependencies
                         .OrderBy(
                             static dependency => dependency.Producer.Identity,
                             StringComparer.Ordinal))
            {
                Visit(dependency.Producer);
            }

            state[producer] = 2;
            ordered.Add(producer);
        }
    }

    static ImmutableArray<ProducerRejection> Distinct(
        ImmutableArray<ProducerRejection>.Builder rejections) =>
        [
            .. rejections
                .Distinct()
                .OrderBy(
                    static rejection => rejection.Producer,
                    StringComparer.Ordinal)
                .ThenBy(static rejection => rejection.Reason),
        ];
}
