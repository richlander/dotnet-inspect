using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One participant's typed outcome from an assembly-context query.</summary>
public abstract record AssemblyContextEntry<TValue>(
    AssemblyContextSubject Subject)
{
    public sealed record Available(
        AssemblyContextSubject Subject,
        TValue Value)
        : AssemblyContextEntry<TValue>(Subject);

    public sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyContextEntry<TValue>(Subject);

    public sealed record Failed(
        AssemblyContextSubject Subject,
        Exception Error)
        : AssemblyContextEntry<TValue>(Subject);
}

/// <summary>Ordered outcomes for every participant in an assembly context.</summary>
public sealed record AssemblyContextResult<TValue>(
    ImmutableArray<AssemblyContextEntry<TValue>> Assemblies)
{
    public bool IsComplete =>
        Assemblies.All(
            static entry =>
                entry is AssemblyContextEntry<TValue>.Available);
}

/// <summary>A presentation-independent type row from an assembly API surface.</summary>
public sealed record AssemblyTypeInventoryEntry(
    string TypeName,
    string? Namespace,
    string FullName,
    string Kind);

/// <summary>
/// Healthy type rows and metadata rows rejected while producing them.
/// </summary>
public sealed record AssemblyTypeInventory(
    ImmutableArray<AssemblyTypeInventoryEntry> Types,
    ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

/// <summary>
/// Healthy member matches and metadata rows rejected while producing them.
/// </summary>
public sealed record AssemblyMemberMatches(
    ImmutableArray<MemberSearchResult> Members,
    ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

/// <summary>One type reached through public members of the requested root type.</summary>
public sealed record ExtensionReachableTypePath(
    string Type,
    string Path);

/// <summary>
/// Reachability rows plus the per-participant type-index outcomes that bounded
/// the traversal.
/// </summary>
public sealed record AssemblyContextExtensionReachabilityResult(
    ImmutableArray<ExtensionReachableTypePath> ReachableTypes,
    AssemblyContextResult<
        ImmutableArray<ExtensionReachabilityType>> TypeInventories);

/// <summary>Reads extension members from every participant in deterministic order.</summary>
public static class AssemblyContextExtensionMethodsQuery
{
    public static InspectionQuery<
        AssemblyContextResult<ImmutableArray<ExtensionMethodInfo>>>
        Definition { get; } =
        new("Assembly context extension methods", InspectionCost.Unbounded);

    public static AssemblyContextResult<ImmutableArray<ExtensionMethodInfo>>
        Execute(
            AssemblyContextGroup group,
            bool includeAll = false)
        => AssemblyContextQueryExecutor.Execute(
            group,
            session => ExtensionMethodsQuery.Read(session, includeAll));
}

/// <summary>Reads direct implementation relationships from every participant.</summary>
public static class AssemblyContextImplementersQuery
{
    public static InspectionQuery<
        AssemblyContextResult<ImmutableArray<TypeRelationship>>>
        Definition { get; } =
        new("Assembly context implementers", InspectionCost.Unbounded);

    public static AssemblyContextResult<ImmutableArray<TypeRelationship>>
        Execute(
            AssemblyContextGroup group,
            string targetType,
            bool includeAll = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        return AssemblyContextQueryExecutor.Execute(
            group,
            session =>
                session.Implementers(targetType, includeAll)
                    .ToImmutableArray());
    }
}

/// <summary>
/// Traverses public-member return types across one binding-consistent assembly
/// context while decoding member signatures only for visited types.
/// </summary>
public static class AssemblyContextExtensionReachabilityQuery
{
    public static InspectionQuery<
        AssemblyContextExtensionReachabilityResult> Definition { get; } =
        new("Assembly context extension reachability", InspectionCost.Unbounded);

    public static AssemblyContextExtensionReachabilityResult Execute(
        AssemblyContextGroup group,
        string targetType,
        int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

        AssemblyContextResult<
            ImmutableArray<ExtensionReachabilityType>> inventories =
            AssemblyContextQueryExecutor.Execute(
                group,
                session =>
                    session.ExtensionReachabilityTypes()
                        .ToImmutableArray());
        var byFullName =
            new Dictionary<string, ReachabilityAddress>(
                StringComparer.OrdinalIgnoreCase);
        var bySimpleName =
            new Dictionary<string, ReachabilityAddress>(
                StringComparer.OrdinalIgnoreCase);

        for (int index = 0;
            index < inventories.Assemblies.Length;
            index++)
        {
            if (inventories.Assemblies[index]
                is not AssemblyContextEntry<
                    ImmutableArray<ExtensionReachabilityType>>.Available
                    available)
            {
                continue;
            }

            AssemblyContextParticipant participant =
                group.Participants[index];
            foreach (ExtensionReachabilityType type in available.Value)
            {
                var address = new ReachabilityAddress(
                    participant,
                    type.MetadataToken);
                byFullName.TryAdd(type.FullName, address);
                bySimpleName.TryAdd(type.SimpleName, address);
            }
        }

        var results =
            ImmutableArray.CreateBuilder<ExtensionReachableTypePath>();
        var visited = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            targetType,
        };
        var pending = new Queue<(string Type, string Path, int Depth)>();
        pending.Enqueue((targetType, "", maxDepth));

        while (pending.TryDequeue(out var current))
        {
            if (current.Depth <= 0)
                continue;

            if (!TryResolve(
                    current.Type,
                    byFullName,
                    bySimpleName,
                    out ReachabilityAddress address))
            {
                continue;
            }

            AssemblyImageAccessResult<
                IReadOnlyList<ExtensionReachabilityEdge>> access =
                group.UseAssemblySession(
                    address.Participant.Assembly,
                    session =>
                        session.ExtensionReachabilityEdges(
                            address.MetadataToken));
            if (access
                is not AssemblyImageAccessResult<
                    IReadOnlyList<ExtensionReachabilityEdge>>.Available
                    availableEdges)
            {
                throw new InspectionQueryException(
                    $"Reachability participant '{address.Participant.Assembly.Identity.Name}' became unavailable after its type inventory was produced.");
            }

            foreach (ExtensionReachabilityEdge edge
                in availableEdges.Value)
            {
                if (!visited.Add(edge.Type))
                    continue;

                string path = current.Path + edge.PathSegment;
                results.Add(
                    new ExtensionReachableTypePath(
                        edge.Type,
                        path));
                pending.Enqueue(
                    (edge.Type, path, current.Depth - 1));
            }
        }

        return new AssemblyContextExtensionReachabilityResult(
            results.ToImmutable(),
            inventories);
    }

    private static bool TryResolve(
        string type,
        IReadOnlyDictionary<string, ReachabilityAddress> byFullName,
        IReadOnlyDictionary<string, ReachabilityAddress> bySimpleName,
        out ReachabilityAddress address)
    {
        if (byFullName.TryGetValue(type, out ReachabilityAddress? candidate)
            || bySimpleName.TryGetValue(type, out candidate)
            || bySimpleName.TryGetValue(
                type.Split('.').Last(),
                out candidate))
        {
            address = candidate;
            return true;
        }

        address = null!;
        return false;
    }

    private sealed record ReachabilityAddress(
        AssemblyContextParticipant Participant,
        int MetadataToken);
}

/// <summary>Reads a type inventory from every participant.</summary>
public static class AssemblyContextTypeInventoryQuery
{
    public static InspectionQuery<
        AssemblyContextResult<AssemblyTypeInventory>>
        Definition { get; } =
        new("Assembly context type inventory", InspectionCost.Unbounded);

    public static AssemblyContextResult<AssemblyTypeInventory> Execute(
            AssemblyContextGroup group,
            bool includeAll = false)
        => AssemblyContextQueryExecutor.Execute(
            group,
            session =>
            {
                ApiSurface surface =
                    session.ApiSurface(includeAll, typesOnly: true);
                return new AssemblyTypeInventory(
                    surface.Types
                    .Select(
                        static type =>
                            new AssemblyTypeInventoryEntry(
                                type.Name,
                                type.Namespace,
                                type.FullName,
                                type.Kind))
                    .ToImmutableArray(),
                    surface.InspectionFailures.ToImmutableArray());
            });
}

/// <summary>Searches member names in every participant.</summary>
public static class AssemblyContextMemberMatchesQuery
{
    public static InspectionQuery<
        AssemblyContextResult<AssemblyMemberMatches>>
        Definition { get; } =
        new("Assembly context member matches", InspectionCost.Unbounded);

    public static AssemblyContextResult<AssemblyMemberMatches>
        Execute(
            AssemblyContextGroup group,
            IReadOnlyList<string> patterns,
            bool includeAll = false,
            int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        return AssemblyContextQueryExecutor.Execute(
            group,
            (subject, session) =>
            {
                ApiSurface surface = session.ApiSurface(includeAll);
                return new AssemblyMemberMatches(
                    MemberSearch.Search(
                        surface,
                        subject.Identity.Name,
                        patterns,
                        limit)
                    .ToImmutableArray(),
                    surface.InspectionFailures.ToImmutableArray());
            });
    }
}

/// <summary>
/// The one participant loop every group-scoped query runs: deterministic participant order, one
/// typed entry per participant, and rejection or artifact failure carried beside the results
/// instead of ending the run.
/// </summary>
/// <remarks>
/// Queries that inspect metadata take the session form; queries that need the image itself — a
/// Research projection opening its own <c>MetadataSource</c> and Analysis index, for instance —
/// take the snapshot form. Both keep session and snapshot ownership inside
/// <c>DotnetInspector.Queries</c> and its companion query assembly, so no consumer reaches one.
/// </remarks>
internal static class AssemblyContextQueryExecutor
{
    internal static AssemblyContextResult<TValue> Execute<TValue>(
        AssemblyContextGroup group,
        Func<AssemblyInspectionSession, TValue> inspect)
        => Execute(group, (_, session) => inspect(session));

    internal static AssemblyContextResult<TValue> Execute<TValue>(
        AssemblyContextGroup group,
        Func<AssemblyContextSubject, AssemblyInspectionSession, TValue> inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        return ExecuteCore(
            group,
            (subject, assembly) => group.UseAssemblySession(
                assembly,
                session => Guarded(subject, () => inspect(subject, session))));
    }

    internal static AssemblyContextEntry<TValue> ExecuteParticipant<TValue>(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        Func<AssemblyInspectionSession, TValue> inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        ValidateParticipant(group, participant);

        var subject = new AssemblyContextSubject(participant.Assembly);
        return Entry(
            subject,
            group.UseAssemblySession(
                participant.Assembly,
                session => Guarded(subject, () => inspect(session))));
    }

    internal static AssemblyContextResult<TValue> ExecuteOverSnapshots<TValue>(
        AssemblyContextGroup group,
        Func<AssemblyContextSubject, AssemblyImageSnapshot, TValue> inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        return ExecuteCore(
            group,
            (subject, assembly) => group.UseSnapshot(
                assembly,
                snapshot => Guarded(subject, () => inspect(subject, snapshot))));
    }

    internal static AssemblyContextEntry<TValue> ExecuteParticipantOverSnapshot<TValue>(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        Func<AssemblyContextSubject, AssemblyImageSnapshot, TValue> inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        ValidateParticipant(group, participant);

        var subject = new AssemblyContextSubject(participant.Assembly);
        return Entry(
            subject,
            group.UseSnapshot(
                participant.Assembly,
                snapshot => Guarded(subject, () => inspect(subject, snapshot))));
    }

    static void ValidateParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        if (group.Participants.Any(candidate => ReferenceEquals(
                candidate.Assembly.Registration,
                participant.Assembly.Registration)))
        {
            return;
        }

        throw new ArgumentException(
            "The requested participant is not a member of the assembly context group.",
            nameof(participant));
    }

    private static AssemblyContextResult<TValue> ExecuteCore<TValue>(
        AssemblyContextGroup group,
        Func<
            AssemblyContextSubject,
            ResolvedAssemblyReference,
            AssemblyImageAccessResult<AssemblyContextEntry<TValue>>> access)
    {
        ArgumentNullException.ThrowIfNull(group);

        var entries =
            ImmutableArray.CreateBuilder<AssemblyContextEntry<TValue>>(
                group.Participants.Length);
        foreach (AssemblyContextParticipant participant
            in group.Participants)
        {
            var subject = new AssemblyContextSubject(
                participant.Assembly);
            entries.Add(
                Entry(subject, access(subject, participant.Assembly)));
        }

        return new AssemblyContextResult<TValue>(
            entries.MoveToImmutable());
    }

    private static AssemblyContextEntry<TValue> Entry<TValue>(
        AssemblyContextSubject subject,
        AssemblyImageAccessResult<AssemblyContextEntry<TValue>> access)
        => access switch
        {
            AssemblyImageAccessResult<
                AssemblyContextEntry<TValue>>.Available available =>
                available.Value,
            AssemblyImageAccessResult<
                AssemblyContextEntry<TValue>>.Rejected rejected =>
                new AssemblyContextEntry<TValue>.Rejected(
                    subject,
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };

    private static AssemblyContextEntry<TValue> Guarded<TValue>(
        AssemblyContextSubject subject,
        Func<TValue> inspect)
    {
        try
        {
            return new AssemblyContextEntry<TValue>.Available(
                subject,
                inspect());
        }
        catch (Exception ex) when (IsArtifactFailure(ex))
        {
            return new AssemblyContextEntry<TValue>.Failed(
                subject,
                ex);
        }
    }

    internal static bool IsArtifactFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or OverflowException
            or IndexOutOfRangeException;
}
