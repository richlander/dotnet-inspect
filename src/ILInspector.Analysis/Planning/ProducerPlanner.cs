using System.Collections.Immutable;

namespace ILInspector.Analysis.Planning;

/// <summary>The terminal a request asks of a producer.</summary>
public enum ProducerTerminal
{
    /// <summary>
    /// Complete an owner-defined fold: every unit in scope contributes to the
    /// result, and the request never stops early.
    /// </summary>
    Complete = 0,

    /// <summary>
    /// The request is settled by the first unit fact the producer reports as
    /// settling; no later unit is visited for it.
    /// </summary>
    Exists = 1,

    /// <summary>
    /// Count every unit selected by an open query without projecting rows.
    /// </summary>
    Count = 3,

    /// <summary>
    /// Every unit in scope contributes, and each unit that satisfies the
    /// producer's open query is projected to a row. A producer may declare
    /// fields it reads only for this closing, such as identity text. Planning
    /// never derives another closing from it: a consumer that needs Count or
    /// Exists requests that closing in its own work description.
    /// </summary>
    Rows = 2,
}

/// <summary>One requested producer and the terminal the requester needs.</summary>
public sealed record ProducerRequest(
    ProducerDeclaration Producer,
    ProducerTerminal Terminal = ProducerTerminal.Complete);

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

        int passCount = 0;
        foreach (ProducerDeclaration producer in completionOrder)
            passCount = Math.Max(passCount, completionPasses[producer]);
        PassCount = passCount;

        // The execution tables: everything an executor needs, by producer
        // index, so no execution re-derives the schedule from the dictionaries.
        var indices = new Dictionary<ProducerDeclaration, int>(
            producers.Length,
            ReferenceEqualityComparer.Instance);
        for (int i = 0; i < producers.Length; i++)
            indices[producers[i]] = i;

        var terminalsByIndex = new ProducerTerminal[producers.Length];
        var dependencies = new ImmutableArray<int>[producers.Length];
        var unitFactRetention = new UnitFactRetention[producers.Length];
        for (int i = 0; i < producers.Length; i++)
        {
            ProducerDeclaration producer = producers[i];
            terminalsByIndex[i] = terminals[producer];
            ImmutableArray<ProducerDependency> declared = producer.Dependencies;
            var targets = ImmutableArray.CreateBuilder<int>(declared.Length);
            foreach (ProducerDependency dependency in declared)
            {
                int target = indices[dependency.Producer];
                targets.Add(target);
                if (dependency.Kind == ProducerDependencyKind.VisitNeedsVisit)
                {
                    // A reader in the same pass reads the fact of the unit
                    // being visited; one in a later pass needs every unit's.
                    UnitFactRetention needed =
                        visitPasses[producer] == visitPasses[dependency.Producer]
                            ? UnitFactRetention.CurrentUnit
                            : UnitFactRetention.AllUnits;
                    if (needed > unitFactRetention[target])
                        unitFactRetention[target] = needed;
                }
            }

            dependencies[i] = targets.ToImmutable();
        }

        var visits = new List<int>[passCount];
        var completions = new List<int>[passCount];
        for (int pass = 0; pass < passCount; pass++)
        {
            visits[pass] = [];
            completions[pass] = [];
        }

        for (int i = 0; i < producers.Length; i++)
            visits[visitPasses[producers[i]] - 1].Add(i);
        var completionIndices = ImmutableArray.CreateBuilder<int>(
            completionOrder.Length);
        foreach (ProducerDeclaration producer in completionOrder)
        {
            int index = indices[producer];
            completionIndices.Add(index);
            completions[completionPasses[producer] - 1].Add(index);
        }

        var passes = ImmutableArray.CreateBuilder<PlannedPass>(passCount);
        for (int pass = 0; pass < passCount; pass++)
        {
            passes.Add(new PlannedPass(
                [.. visits[pass]],
                [.. completions[pass]]));
        }

        _indices = indices;
        TerminalByIndex = ImmutableArray.Create(terminalsByIndex);
        DependencyIndices = ImmutableArray.Create(dependencies);
        FactRetention = ImmutableArray.Create(unitFactRetention);
        CompletionIndices = completionIndices.ToImmutable();
        Passes = passes.ToImmutable();
    }

    readonly ImmutableDictionary<ProducerDeclaration, ProducerTerminal>
        _terminals;
    readonly ImmutableDictionary<ProducerDeclaration, int> _visitPasses;
    readonly ImmutableDictionary<ProducerDeclaration, int>
        _completionPasses;
    readonly ImmutableHashSet<ProducerDeclaration> _requested;
    readonly Dictionary<ProducerDeclaration, int> _indices;

    /// <summary>Each producer's effective terminal, by index into <see cref="Producers"/>.</summary>
    internal ImmutableArray<ProducerTerminal> TerminalByIndex { get; }

    /// <summary>Each producer's dependency targets, by index, in declaration order.</summary>
    internal ImmutableArray<ImmutableArray<int>> DependencyIndices { get; }

    /// <summary>
    /// How long an execution must retain each producer's per-unit facts for
    /// the planned producers that read them.
    /// </summary>
    internal ImmutableArray<UnitFactRetention> FactRetention { get; }

    /// <summary>The completion order, by index.</summary>
    internal ImmutableArray<int> CompletionIndices { get; }

    /// <summary>The producers that visit and complete in each pass, by index, in stage order.</summary>
    internal ImmutableArray<PlannedPass> Passes { get; }

    /// <summary>The index of a planned producer in <see cref="Producers"/>.</summary>
    internal bool TryGetIndex(ProducerDeclaration producer, out int index) =>
        _indices.TryGetValue(producer, out index);

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

/// <summary>How long a producer's per-unit facts must outlive the unit's visit.</summary>
internal enum UnitFactRetention : byte
{
    /// <summary>No planned producer reads them.</summary>
    None,

    /// <summary>Every reader visits in the same pass, after the producer, so only the current unit's fact is read.</summary>
    CurrentUnit,

    /// <summary>A reader visits in a later pass, so every unit's fact is read.</summary>
    AllUnits,
}

/// <summary>One pass of a work description: producer indices in visit order and in completion order.</summary>
internal readonly record struct PlannedPass(
    ImmutableArray<int> Visits,
    ImmutableArray<int> Completions);

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

            // Planning never ranks or merges closings; an identical
            // duplicate is the same request.
            if (terminals.TryGetValue(request.Producer, out ProducerTerminal existing)
                && existing != request.Terminal)
            {
                throw DistinctClosings(request.Producer, existing, request.Terminal);
            }

            terminals[request.Producer] = request.Terminal;
        }

        // A dependency is needed in full by its dependent: that is the
        // Complete closing, so a dependency requested with another closing
        // has two distinct closings.
        foreach (ProducerDeclaration producer in closure)
        {
            if (!requested.Contains(producer))
                terminals[producer] = ProducerTerminal.Complete;
        }

        foreach (ProducerDeclaration producer in closure)
        {
            foreach (ProducerDependency dependency in producer.Dependencies)
            {
                if (dependency.Producer is { } target
                    && terminals.TryGetValue(target, out ProducerTerminal closing)
                    && closing != ProducerTerminal.Complete)
                {
                    throw DistinctClosings(target, closing, ProducerTerminal.Complete);
                }
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

    /// <summary>
    /// Until QuerySpace composes requests (#8574), one plan runs one closing
    /// per producer; more than one is a contract error, never a lossy plan.
    /// </summary>
    static ProducerContractException DistinctClosings(
        ProducerDeclaration producer,
        ProducerTerminal first,
        ProducerTerminal second) =>
        new($"Producer '{producer.Identity}' was requested with distinct closings {first} and {second} in one plan; request each closing in its own plan.");

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
        // Identities are unique in an accepted closure, so the sort is total.
        var roots = new List<ProducerDeclaration>(closure);
        roots.Sort(static (x, y) =>
            string.CompareOrdinal(x.Identity, y.Identity));
        foreach (ProducerDeclaration producer in roots)
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

        var visitOrder = ImmutableArray.CreateBuilder<ProducerDeclaration>(closure.Count);
        var completionOrder = ImmutableArray.CreateBuilder<ProducerDeclaration>(closure.Count);
        foreach ((ProducerDeclaration producer, bool completion) in order)
            (completion ? completionOrder : visitOrder).Add(producer);

        return new(
            visitOrder.ToImmutable(),
            completionOrder.ToImmutable(),
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
            List<(ProducerDeclaration Producer, bool Completion)> predecessors =
                Predecessors(node);
            predecessors.Sort(static (x, y) =>
            {
                int byIdentity = string.CompareOrdinal(
                    x.Producer.Identity,
                    y.Producer.Identity);
                return byIdentity != 0
                    ? byIdentity
                    : x.Completion.CompareTo(y.Completion);
            });
            foreach ((ProducerDeclaration Producer, bool Completion) predecessor
                     in predecessors)
            {
                Visit(predecessor);
            }

            state[node] = 2;
            order.Add(node);
        }

        static List<(ProducerDeclaration Producer, bool Completion)> Predecessors(
            (ProducerDeclaration Producer, bool Completion) node)
        {
            var predecessors = new List<(ProducerDeclaration Producer, bool Completion)>();
            if (node.Completion)
                predecessors.Add((node.Producer, false));
            foreach (ProducerDependency dependency in node.Producer.Dependencies)
            {
                switch (dependency.Kind)
                {
                    case ProducerDependencyKind.VisitNeedsVisit when !node.Completion:
                        predecessors.Add((dependency.Producer, false));
                        break;
                    case ProducerDependencyKind.VisitNeedsResult when !node.Completion:
                    case ProducerDependencyKind.CompletionNeedsResult when node.Completion:
                        predecessors.Add((dependency.Producer, true));
                        break;
                }
            }

            return predecessors;
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
        ImmutableArray<ProducerRejection>.Builder rejections)
    {
        var distinct = new List<ProducerRejection>(
            new HashSet<ProducerRejection>(rejections));
        distinct.Sort(static (x, y) =>
        {
            int byProducer = string.CompareOrdinal(x.Producer, y.Producer);
            return byProducer != 0
                ? byProducer
                : x.Reason.CompareTo(y.Reason);
        });
        return [.. distinct];
    }
}
