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

    /// <summary>
    /// Two distinct declarations share an identity and parameters. Planning
    /// never merges or drops a declaration, so the request is rejected.
    /// </summary>
    DuplicateDeclaration,
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
        ImmutableArray<ProducerDeclaration> completionOrder,
        ImmutableDictionary<ProducerDeclaration, ProducerTerminal> terminals,
        ImmutableDictionary<ProducerDeclaration, int> visitPasses,
        ImmutableDictionary<ProducerDeclaration, int> completionPasses,
        ImmutableHashSet<ProducerDeclaration> requested)
    {
        Producers = producers;
        CompletionOrder = completionOrder;
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

    /// <summary>
    /// Every planned producer, in visit order: consistent with every
    /// dependency a visit has, with identity ties.
    /// </summary>
    public ImmutableArray<ProducerDeclaration> Producers { get; }

    /// <summary>
    /// Every planned producer, in completion order: consistent with every
    /// dependency a completion has. It may differ from the visit order.
    /// </summary>
    public ImmutableArray<ProducerDeclaration> CompletionOrder { get; }

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

        StageSchedule schedule = Schedule(closure, rejections);
        if (rejections.Count > 0)
        {
            return new ProducerPlanResult.Rejected(
                Distinct(rejections));
        }

        return new ProducerPlanResult.Accepted(
            new WorkDescription(
                schedule.VisitOrder,
                schedule.CompletionOrder,
                terminals.ToImmutableDictionary<ProducerDeclaration, ProducerTerminal>(
                    ReferenceEqualityComparer.Instance),
                schedule.VisitPasses.ToImmutableDictionary<ProducerDeclaration, int>(
                    ReferenceEqualityComparer.Instance),
                schedule.CompletionPasses.ToImmutableDictionary<ProducerDeclaration, int>(
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
                rejections.Add(new(
                    producer.Identity,
                    string.Equals(
                        other.Parameters,
                        producer.Parameters,
                        StringComparison.Ordinal)
                        ? ProducerRejectionReason.DuplicateDeclaration
                        : ProducerRejectionReason.ConflictingParameters));
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

    sealed record StageSchedule(
        ImmutableArray<ProducerDeclaration> VisitOrder,
        ImmutableArray<ProducerDeclaration> CompletionOrder,
        Dictionary<ProducerDeclaration, int> VisitPasses,
        Dictionary<ProducerDeclaration, int> CompletionPasses);

    /// <summary>
    /// Orders the stage graph: one visit node and one completion node per
    /// producer. A completion follows its own visits; a visit follows the
    /// visits it reads and the completions it reads; a completion follows the
    /// completions it reads. Cycles are detected on this graph, so a
    /// producer-level loop through different stages is not a cycle.
    /// </summary>
    static StageSchedule Schedule(
        IReadOnlyList<ProducerDeclaration> closure,
        ImmutableArray<ProducerRejection>.Builder rejections)
    {
        var order = new List<(ProducerDeclaration Producer, bool Completion)>();
        var state = new Dictionary<(ProducerDeclaration, bool), int>(
            new StageComparer());
        foreach (ProducerDeclaration producer in closure.OrderBy(
                     static producer => producer.Identity,
                     StringComparer.Ordinal))
        {
            Visit((producer, false));
            Visit((producer, true));
        }

        var visitPasses = new Dictionary<ProducerDeclaration, int>(
            ReferenceEqualityComparer.Instance);
        var completionPasses = new Dictionary<ProducerDeclaration, int>(
            ReferenceEqualityComparer.Instance);
        if (rejections.Count > 0)
            return new([], [], visitPasses, completionPasses);

        foreach ((ProducerDeclaration producer, bool completion) in order)
        {
            if (!completion)
            {
                int pass = 1;
                foreach (ProducerDependency dependency in producer.Dependencies)
                {
                    pass = dependency.Kind switch
                    {
                        ProducerDependencyKind.VisitNeedsVisit =>
                            Math.Max(pass, visitPasses[dependency.Producer]),
                        ProducerDependencyKind.VisitNeedsResult =>
                            Math.Max(pass, completionPasses[dependency.Producer] + 1),
                        _ => pass,
                    };
                }

                visitPasses[producer] = pass;
            }
            else
            {
                int pass = visitPasses[producer];
                foreach (ProducerDependency dependency in producer.Dependencies)
                {
                    if (dependency.Kind == ProducerDependencyKind.CompletionNeedsResult)
                        pass = Math.Max(pass, completionPasses[dependency.Producer]);
                }

                completionPasses[producer] = pass;
            }
        }

        return new(
            [.. order.Where(static node => !node.Completion).Select(static node => node.Producer)],
            [.. order.Where(static node => node.Completion).Select(static node => node.Producer)],
            visitPasses,
            completionPasses);

        void Visit((ProducerDeclaration Producer, bool Completion) node)
        {
            if (state.TryGetValue(node, out int mark))
            {
                if (mark == 1)
                {
                    rejections.Add(new(
                        node.Producer.Identity,
                        ProducerRejectionReason.DependencyCycle));
                }
                return;
            }

            state[node] = 1;
            foreach ((ProducerDeclaration Producer, bool Completion) predecessor
                     in Predecessors(node)
                         .OrderBy(static next => next.Producer.Identity, StringComparer.Ordinal)
                         .ThenBy(static next => next.Completion))
            {
                Visit(predecessor);
            }

            state[node] = 2;
            order.Add(node);
        }

        static IEnumerable<(ProducerDeclaration Producer, bool Completion)> Predecessors(
            (ProducerDeclaration Producer, bool Completion) node)
        {
            if (node.Completion)
                yield return (node.Producer, false);
            foreach (ProducerDependency dependency in node.Producer.Dependencies)
            {
                switch (dependency.Kind)
                {
                    case ProducerDependencyKind.VisitNeedsVisit when !node.Completion:
                        yield return (dependency.Producer, false);
                        break;
                    case ProducerDependencyKind.VisitNeedsResult when !node.Completion:
                    case ProducerDependencyKind.CompletionNeedsResult when node.Completion:
                        yield return (dependency.Producer, true);
                        break;
                }
            }
        }
    }

    sealed class StageComparer
        : IEqualityComparer<(ProducerDeclaration Producer, bool Completion)>
    {
        public bool Equals(
            (ProducerDeclaration Producer, bool Completion) x,
            (ProducerDeclaration Producer, bool Completion) y) =>
            ReferenceEquals(x.Producer, y.Producer) && x.Completion == y.Completion;

        public int GetHashCode((ProducerDeclaration Producer, bool Completion) node) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node.Producer),
                node.Completion);
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
