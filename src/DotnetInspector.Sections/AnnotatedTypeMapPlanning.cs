using System.Collections.Immutable;

using DotnetInspector.Queries;

using InertText;

namespace DotnetInspector.Sections;

// docs/design/annotated-type-map-planning.md owns this registry and planner.

public enum AnnotatedTypeMapFamilyKind
{
    ApiShape,
    BodyShape,
    FactStream,
    Finding,
}

/// <summary>
/// A finding descriptor family's classification under the Annotated Source
/// Finding provenance matrix. The map admits only <see cref="LocalInstruction"/>.
/// </summary>
public enum AnnotatedTypeMapFindingClassification
{
    LocalInstruction,
    CalleeInstruction,
    CalleeMethod,
    MemberHeader,
    CallRelationship,
}

/// <summary>
/// One family a map can carry. Identity is the instance: a lookalike with the
/// same id never resolves. The two shape-fact families and the fact stream
/// are fixed instances; finding families are created by their producers.
/// </summary>
public sealed class AnnotatedTypeMapFamily
{
    private AnnotatedTypeMapFamily(
        string id,
        AnnotatedTypeMapFamilyKind kind,
        AnnotatedTypeMapFindingClassification? classification)
    {
        Id = id;
        Kind = kind;
        Classification = classification;
    }

    public static AnnotatedTypeMapFamily ApiShape { get; } =
        new("shape.api", AnnotatedTypeMapFamilyKind.ApiShape, null);

    public static AnnotatedTypeMapFamily BodyShape { get; } =
        new("shape.body", AnnotatedTypeMapFamilyKind.BodyShape, null);

    public static AnnotatedTypeMapFamily FactStream { get; } =
        new("fact-stream", AnnotatedTypeMapFamilyKind.FactStream, null);

    public string Id { get; }

    public AnnotatedTypeMapFamilyKind Kind { get; }

    /// <summary>The provenance classification of a finding family; null otherwise.</summary>
    public AnnotatedTypeMapFindingClassification? Classification { get; }

    /// <summary>Whether the map carries this family whatever analyses a request names.</summary>
    public bool IsMapCarried => Kind != AnnotatedTypeMapFamilyKind.Finding;

    public static AnnotatedTypeMapFamily Finding(
        string id,
        AnnotatedTypeMapFindingClassification classification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Enum.IsDefined(classification))
            throw new ArgumentOutOfRangeException(nameof(classification));
        return new(id, AnnotatedTypeMapFamilyKind.Finding, classification);
    }
}

public enum AnnotatedTypeMapRequestForm
{
    Map,
    Contribution,
}

public enum AnnotatedTypeMapTerminal
{
    Rows,
    Count,
    Exists,
}

/// <summary>
/// What one request's result reads, and so which adapter a family binds:
/// one-version and map-diff results of a terminal never share an adapter.
/// </summary>
public enum AnnotatedTypeMapResultKind
{
    OneVersionRows,
    OneVersionCount,
    OneVersionExists,
    MapDiffRows,
    MapDiffCount,
    MapDiffExists,
    Contribution,
}

[Flags]
public enum AnnotatedTypeMapFocusSupport
{
    Type = 1,
    Members = 2,
}

public sealed class AnnotatedTypeMapAdapterQueries
{
    private readonly ImmutableDictionary<
        AnnotatedTypeMapResultKind,
        InspectionQuery<AnnotatedTypeMapContribution>> _queries;

    public AnnotatedTypeMapAdapterQueries(
        InspectionQuery<AnnotatedTypeMapContribution>? oneVersionRows = null,
        InspectionQuery<AnnotatedTypeMapContribution>? oneVersionCount = null,
        InspectionQuery<AnnotatedTypeMapContribution>? oneVersionExists = null,
        InspectionQuery<AnnotatedTypeMapContribution>? mapDiffRows = null,
        InspectionQuery<AnnotatedTypeMapContribution>? mapDiffCount = null,
        InspectionQuery<AnnotatedTypeMapContribution>? mapDiffExists = null,
        InspectionQuery<AnnotatedTypeMapContribution>? contribution = null)
    {
        var queries = ImmutableDictionary.CreateBuilder<
            AnnotatedTypeMapResultKind,
            InspectionQuery<AnnotatedTypeMapContribution>>();
        void Add(
            AnnotatedTypeMapResultKind kind,
            InspectionQuery<AnnotatedTypeMapContribution>? query)
        {
            if (query is not null)
                queries.Add(kind, query);
        }

        Add(AnnotatedTypeMapResultKind.OneVersionRows, oneVersionRows);
        Add(AnnotatedTypeMapResultKind.OneVersionCount, oneVersionCount);
        Add(AnnotatedTypeMapResultKind.OneVersionExists, oneVersionExists);
        Add(AnnotatedTypeMapResultKind.MapDiffRows, mapDiffRows);
        Add(AnnotatedTypeMapResultKind.MapDiffCount, mapDiffCount);
        Add(AnnotatedTypeMapResultKind.MapDiffExists, mapDiffExists);
        Add(AnnotatedTypeMapResultKind.Contribution, contribution);
        if (queries.Count == 0)
        {
            throw new ArgumentException(
                "At least one adapter query is required.");
        }

        RejectShared(oneVersionRows, mapDiffRows, AnnotatedTypeMapTerminal.Rows);
        RejectShared(oneVersionCount, mapDiffCount, AnnotatedTypeMapTerminal.Count);
        RejectShared(oneVersionExists, mapDiffExists, AnnotatedTypeMapTerminal.Exists);
        _queries = queries.ToImmutable();
    }

    public IEnumerable<AnnotatedTypeMapResultKind> ResultKinds =>
        _queries.Keys.Order();

    public InspectionQuery<AnnotatedTypeMapContribution>? For(
        AnnotatedTypeMapResultKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        return _queries.GetValueOrDefault(kind);
    }

    private static void RejectShared(
        InspectionQuery<AnnotatedTypeMapContribution>? oneVersion,
        InspectionQuery<AnnotatedTypeMapContribution>? mapDiff,
        AnnotatedTypeMapTerminal terminal)
    {
        if (oneVersion is not null && ReferenceEquals(oneVersion, mapDiff))
        {
            throw new ArgumentException(
                $"One-version and map-diff {terminal} results cannot share an adapter query.");
        }
    }
}

public sealed class AnnotatedTypeMapRegistration
{
    public AnnotatedTypeMapRegistration(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapFocusSupport focus,
        AnnotatedTypeMapAdapterQueries queries)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(queries);
        if (focus == 0
            || (focus & ~(AnnotatedTypeMapFocusSupport.Type | AnnotatedTypeMapFocusSupport.Members)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(focus));
        }

        if (family.Kind == AnnotatedTypeMapFamilyKind.Finding
            && family.Classification != AnnotatedTypeMapFindingClassification.LocalInstruction)
        {
            throw new ArgumentException(
                $"The map does not admit finding family '{family.Id}' ({family.Classification}).",
                nameof(family));
        }

        foreach (AnnotatedTypeMapResultKind kind in queries.ResultKinds)
        {
            if (!AnnotatedTypeMapResultKinds.Reads(kind, family))
            {
                throw new ArgumentException(
                    $"No {kind} result reads family '{family.Id}', so it cannot register an adapter for it.",
                    nameof(queries));
            }
        }

        Family = family;
        Focus = focus;
        Queries = queries;
    }

    public AnnotatedTypeMapFamily Family { get; }

    public AnnotatedTypeMapFocusSupport Focus { get; }

    public AnnotatedTypeMapAdapterQueries Queries { get; }
}

public sealed class AnnotatedTypeMapRegistry
{
    private readonly List<AnnotatedTypeMapRegistration> _registrations = [];

    public AnnotatedTypeMapRegistry Add(AnnotatedTypeMapRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        foreach (AnnotatedTypeMapRegistration existing in _registrations)
        {
            if (ReferenceEquals(existing.Family, registration.Family))
            {
                throw new ArgumentException(
                    $"Family '{registration.Family.Id}' is already registered.",
                    nameof(registration));
            }

            if (StringComparer.Ordinal.Equals(existing.Family.Id, registration.Family.Id))
            {
                throw new ArgumentException(
                    $"Family id '{registration.Family.Id}' is already registered.",
                    nameof(registration));
            }
        }

        _registrations.Add(registration);
        return this;
    }

    public AnnotatedTypeMapCatalog Compile() => new([.. _registrations]);
}

/// <summary>
/// One Finding Diff Any Member key: the census key's exact bytes. The request
/// checks keys only for presence and repetition; an adapter reports a key
/// outside the focus Type's census as a typed failure.
/// </summary>
public sealed class AnnotatedTypeMapMemberKey : IEquatable<AnnotatedTypeMapMemberKey>
{
    public AnnotatedTypeMapMemberKey(OpaqueByteRange key)
    {
        if (key.IsEmpty)
            throw new ArgumentException("A Member key cannot be empty.", nameof(key));
        Key = key;
    }

    public OpaqueByteRange Key { get; }

    public bool Equals(AnnotatedTypeMapMemberKey? other) =>
        other is not null && Key.Equals(other.Key);

    public override bool Equals(object? obj) => Equals(obj as AnnotatedTypeMapMemberKey);

    public override int GetHashCode() => Key.GetHashCode();
}

public sealed class AnnotatedTypeMapPlanningRequest
{
    public AnnotatedTypeMapPlanningRequest(
        AnnotatedTypeMapRequestForm form,
        SubjectRelationFocusAuthority focus,
        bool versionPair,
        IEnumerable<AnnotatedTypeMapFamily> families,
        AnnotatedTypeMapTerminal? terminal = null,
        IEnumerable<AnnotatedTypeMapMemberKey>? memberKeys = null)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(families);
        if (!Enum.IsDefined(form))
            throw new ArgumentOutOfRangeException(nameof(form));
        if (focus.Kind != StructuralSubjectKind.Type)
        {
            throw new ArgumentException(
                "Annotated Type Map planning requires an exact Type focus.",
                nameof(focus));
        }

        ImmutableArray<AnnotatedTypeMapFamily> familySnapshot = [.. families];
        if (familySnapshot.Any(static family => family is null))
            throw new ArgumentException("Families cannot contain null.", nameof(families));
        if (familySnapshot.Distinct(ReferenceEqualityComparer.Instance).Count() != familySnapshot.Length)
            throw new ArgumentException("Families cannot repeat.", nameof(families));

        switch (form)
        {
            case AnnotatedTypeMapRequestForm.Map:
                if (terminal is not { } mapTerminal || !Enum.IsDefined(mapTerminal))
                    throw new ArgumentException("A map request requires a terminal.", nameof(terminal));
                if (!versionPair && !familySnapshot.Any(static family => !family.IsMapCarried))
                {
                    throw new ArgumentException(
                        "A one-version map request must name at least one finding family.",
                        nameof(families));
                }
                break;
            case AnnotatedTypeMapRequestForm.Contribution:
                if (terminal is not null)
                    throw new ArgumentException("A family contribution request has no terminal.", nameof(terminal));
                if (familySnapshot.IsEmpty)
                    throw new ArgumentException("A family contribution request requires a family.", nameof(families));
                break;
        }

        ImmutableArray<AnnotatedTypeMapMemberKey>? keys = null;
        if (memberKeys is not null)
        {
            ImmutableArray<AnnotatedTypeMapMemberKey> keySnapshot = [.. memberKeys];
            if (keySnapshot.IsEmpty)
                throw new ArgumentException("A Member key set cannot be empty.", nameof(memberKeys));
            if (keySnapshot.Any(static key => key is null))
                throw new ArgumentException("Member keys cannot contain null.", nameof(memberKeys));
            if (keySnapshot.Distinct().Count() != keySnapshot.Length)
                throw new ArgumentException("Member keys cannot repeat.", nameof(memberKeys));
            keys = keySnapshot;
        }

        Form = form;
        Focus = focus;
        VersionPair = versionPair;
        Families = familySnapshot;
        Terminal = terminal;
        MemberKeys = keys;
        ResultKind = AnnotatedTypeMapResultKinds.Of(form, versionPair, terminal);
    }

    public AnnotatedTypeMapRequestForm Form { get; }

    public SubjectRelationFocusAuthority Focus { get; }

    public bool VersionPair { get; }

    public ImmutableArray<AnnotatedTypeMapFamily> Families { get; }

    public AnnotatedTypeMapTerminal? Terminal { get; }

    /// <summary>The Member-scope questions, or null for the whole Type.</summary>
    public ImmutableArray<AnnotatedTypeMapMemberKey>? MemberKeys { get; }

    public AnnotatedTypeMapResultKind ResultKind { get; }
}

public enum AnnotatedTypeMapPlanningFailureKind
{
    UnknownFamily,
    MapCarriedSelected,
    UnsupportedFocus,
    UnsupportedResultKind,
    QueryUnavailable,
}

public sealed record AnnotatedTypeMapPlanningFailure(
    AnnotatedTypeMapPlanningFailureKind Kind,
    string FamilyId,
    string Message);

public abstract record AnnotatedTypeMapPlanningResult
{
    private AnnotatedTypeMapPlanningResult()
    {
    }

    public sealed record Accepted(AnnotatedTypeMapPlan Plan)
        : AnnotatedTypeMapPlanningResult;

    public sealed record Rejected(AnnotatedTypeMapPlanningFailure Failure)
        : AnnotatedTypeMapPlanningResult;
}

/// <summary>Why a family appears in a plan.</summary>
public enum AnnotatedTypeMapDemandReason
{
    /// <summary>The request selected the family and its result reads it.</summary>
    Selected,

    /// <summary>The map carries the family and the request's result reads it.</summary>
    MapCarried,

    /// <summary>The request selected the family, but its result does not read it.</summary>
    SelectedNotDemanded,
}

/// <summary>
/// One line of a plan's trail: a family, why it is in the plan, and the
/// adapter query it binds, or none when its result does not read it.
/// </summary>
public sealed record AnnotatedTypeMapPlanTrailEntry(
    AnnotatedTypeMapFamily Family,
    AnnotatedTypeMapDemandReason Reason,
    InspectionQuery<AnnotatedTypeMapContribution>? Query);

public sealed class AnnotatedTypeMapPlan
{
    internal AnnotatedTypeMapPlan(
        AnnotatedTypeMapPlanningRequest request,
        ImmutableArray<AnnotatedTypeMapPlanTrailEntry> trail,
        ImmutableArray<InspectionQuery<AnnotatedTypeMapContribution>> queries,
        InspectionCost cost)
    {
        Request = request;
        Trail = trail;
        Queries = queries;
        Cost = cost;
    }

    public AnnotatedTypeMapPlanningRequest Request { get; }

    public AnnotatedTypeMapResultKind ResultKind => Request.ResultKind;

    /// <summary>Every resolved family in registry order, with its reason and adapter.</summary>
    public ImmutableArray<AnnotatedTypeMapPlanTrailEntry> Trail { get; }

    /// <summary>The distinct typed-query demand, in first-bound order.</summary>
    public ImmutableArray<InspectionQuery<AnnotatedTypeMapContribution>> Queries { get; }

    public InspectionCost Cost { get; }

    /// <summary>
    /// The plan's trail as stable lines a host can show as Debug evidence:
    /// the result kind, then one line per family.
    /// </summary>
    public ImmutableArray<string> Explain()
    {
        var lines = ImmutableArray.CreateBuilder<string>(Trail.Length + 1);
        lines.Add($"result-kind={ResultKind}");
        foreach (AnnotatedTypeMapPlanTrailEntry entry in Trail)
        {
            lines.Add(
                $"{entry.Family.Id}\t{entry.Reason}\t{entry.Query?.Name ?? "-"}");
        }
        return lines.MoveToImmutable();
    }
}

public sealed class AnnotatedTypeMapCatalog
{
    private readonly ImmutableArray<AnnotatedTypeMapRegistration> _registrations;

    internal AnnotatedTypeMapCatalog(
        ImmutableArray<AnnotatedTypeMapRegistration> registrations) =>
        _registrations = registrations;

    public ImmutableArray<AnnotatedTypeMapRegistration> Registrations => _registrations;

    /// <summary>
    /// The registry snapshot as stable lines, one per registration in
    /// registry order: family id, kind, classification, focus support, and
    /// each result kind's adapter query.
    /// </summary>
    public ImmutableArray<string> Describe() =>
        [.. _registrations.Select(static registration =>
            string.Join(
                '\t',
                [
                    registration.Family.Id,
                    registration.Family.Kind.ToString(),
                    registration.Family.Classification?.ToString() ?? "-",
                    registration.Focus.ToString(),
                    .. registration.Queries.ResultKinds.Select(
                        kind => $"{kind}={registration.Queries.For(kind)!.Name}"),
                ]))];

    public AnnotatedTypeMapPlanningResult Resolve(
        AnnotatedTypeMapPlanningRequest request,
        IInspectionQueryCatalog queryCatalog)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(queryCatalog);

        foreach (AnnotatedTypeMapFamily family in request.Families)
        {
            if (Find(family) is null)
            {
                return Reject(
                    AnnotatedTypeMapPlanningFailureKind.UnknownFamily,
                    family.Id,
                    $"Family '{family.Id}' is not registered.");
            }

            if (request.Form == AnnotatedTypeMapRequestForm.Map && family.IsMapCarried)
            {
                return Reject(
                    AnnotatedTypeMapPlanningFailureKind.MapCarriedSelected,
                    family.Id,
                    $"A map request cannot select map-carried family '{family.Id}'.");
            }
        }

        var demanded = new List<(AnnotatedTypeMapFamily Family, AnnotatedTypeMapDemandReason Reason)>();
        if (request.Form == AnnotatedTypeMapRequestForm.Map)
        {
            foreach (AnnotatedTypeMapFamily carried in
                (ReadOnlySpan<AnnotatedTypeMapFamily>)
                [
                    AnnotatedTypeMapFamily.ApiShape,
                    AnnotatedTypeMapFamily.BodyShape,
                    AnnotatedTypeMapFamily.FactStream,
                ])
            {
                if (!AnnotatedTypeMapResultKinds.Reads(request.ResultKind, carried, request.Families))
                    continue;
                if (Find(carried) is null)
                {
                    return Reject(
                        AnnotatedTypeMapPlanningFailureKind.UnknownFamily,
                        carried.Id,
                        $"Map-carried family '{carried.Id}' is not registered.");
                }
                demanded.Add((carried, AnnotatedTypeMapDemandReason.MapCarried));
            }
        }

        foreach (AnnotatedTypeMapFamily family in request.Families)
        {
            demanded.Add((
                family,
                AnnotatedTypeMapResultKinds.Reads(request.ResultKind, family, request.Families)
                    ? AnnotatedTypeMapDemandReason.Selected
                    : AnnotatedTypeMapDemandReason.SelectedNotDemanded));
        }

        var trail = ImmutableArray.CreateBuilder<AnnotatedTypeMapPlanTrailEntry>();
        var queries = ImmutableArray.CreateBuilder<InspectionQuery<AnnotatedTypeMapContribution>>();
        var seenQueries = new HashSet<object>(ReferenceEqualityComparer.Instance);
        InspectionCost cost = InspectionCost.NetworkFree;
        AnnotatedTypeMapFocusSupport focus = request.MemberKeys is null
            ? AnnotatedTypeMapFocusSupport.Type
            : AnnotatedTypeMapFocusSupport.Members;

        foreach (AnnotatedTypeMapRegistration registration in _registrations)
        {
            int index = demanded.FindIndex(
                entry => ReferenceEquals(entry.Family, registration.Family));
            if (index < 0)
                continue;
            AnnotatedTypeMapDemandReason reason = demanded[index].Reason;
            if (reason == AnnotatedTypeMapDemandReason.SelectedNotDemanded)
            {
                trail.Add(new(registration.Family, reason, null));
                continue;
            }

            if ((registration.Focus & focus) == 0)
            {
                return Reject(
                    AnnotatedTypeMapPlanningFailureKind.UnsupportedFocus,
                    registration.Family.Id,
                    $"Family '{registration.Family.Id}' does not support {focus} focus.");
            }

            InspectionQuery<AnnotatedTypeMapContribution>? query =
                registration.Queries.For(request.ResultKind);
            if (query is null)
            {
                return Reject(
                    AnnotatedTypeMapPlanningFailureKind.UnsupportedResultKind,
                    registration.Family.Id,
                    $"Family '{registration.Family.Id}' has no {request.ResultKind} adapter.");
            }

            if (!queryCatalog.Contains(query))
            {
                return Reject(
                    AnnotatedTypeMapPlanningFailureKind.QueryUnavailable,
                    registration.Family.Id,
                    $"Adapter query '{query.Name}' for '{registration.Family.Id}' is not " +
                    "registered in the inspection query catalog.");
            }

            trail.Add(new(registration.Family, reason, query));
            if (seenQueries.Add(query))
            {
                queries.Add(query);
                InspectionCost queryCost = queryCatalog.CostOf(query);
                if (queryCost > cost)
                    cost = queryCost;
            }
        }

        return new AnnotatedTypeMapPlanningResult.Accepted(
            new(request, trail.ToImmutable(), queries.ToImmutable(), cost));
    }

    private AnnotatedTypeMapRegistration? Find(AnnotatedTypeMapFamily family) =>
        _registrations.FirstOrDefault(
            registration => ReferenceEquals(registration.Family, family));

    private static AnnotatedTypeMapPlanningResult.Rejected Reject(
        AnnotatedTypeMapPlanningFailureKind kind,
        string familyId,
        string message) =>
        new(new(kind, familyId, message));
}

/// <summary>
/// The result-kind table of docs/design/annotated-type-map-planning.md: which
/// families each kind of result reads.
/// </summary>
internal static class AnnotatedTypeMapResultKinds
{
    public static AnnotatedTypeMapResultKind Of(
        AnnotatedTypeMapRequestForm form,
        bool versionPair,
        AnnotatedTypeMapTerminal? terminal) =>
        (form, versionPair, terminal) switch
        {
            (AnnotatedTypeMapRequestForm.Contribution, _, _) => AnnotatedTypeMapResultKind.Contribution,
            (_, false, AnnotatedTypeMapTerminal.Rows) => AnnotatedTypeMapResultKind.OneVersionRows,
            (_, false, AnnotatedTypeMapTerminal.Count) => AnnotatedTypeMapResultKind.OneVersionCount,
            (_, false, AnnotatedTypeMapTerminal.Exists) => AnnotatedTypeMapResultKind.OneVersionExists,
            (_, true, AnnotatedTypeMapTerminal.Rows) => AnnotatedTypeMapResultKind.MapDiffRows,
            (_, true, AnnotatedTypeMapTerminal.Count) => AnnotatedTypeMapResultKind.MapDiffCount,
            (_, true, AnnotatedTypeMapTerminal.Exists) => AnnotatedTypeMapResultKind.MapDiffExists,
            _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
        };

    /// <summary>Whether any result of this kind can read the family.</summary>
    public static bool Reads(AnnotatedTypeMapResultKind kind, AnnotatedTypeMapFamily family) =>
        family.Kind switch
        {
            AnnotatedTypeMapFamilyKind.ApiShape or AnnotatedTypeMapFamilyKind.BodyShape =>
                kind is AnnotatedTypeMapResultKind.OneVersionRows
                    or AnnotatedTypeMapResultKind.MapDiffRows
                    or AnnotatedTypeMapResultKind.MapDiffCount
                    or AnnotatedTypeMapResultKind.MapDiffExists
                    or AnnotatedTypeMapResultKind.Contribution,
            AnnotatedTypeMapFamilyKind.FactStream =>
                kind is AnnotatedTypeMapResultKind.OneVersionRows
                    or AnnotatedTypeMapResultKind.MapDiffRows
                    or AnnotatedTypeMapResultKind.Contribution,
            AnnotatedTypeMapFamilyKind.Finding =>
                kind is not (AnnotatedTypeMapResultKind.MapDiffCount
                    or AnnotatedTypeMapResultKind.MapDiffExists),
            _ => false,
        };

    /// <summary>Whether this request's result reads the family, given its selected families.</summary>
    public static bool Reads(
        AnnotatedTypeMapResultKind kind,
        AnnotatedTypeMapFamily family,
        ImmutableArray<AnnotatedTypeMapFamily> selected) =>
        Reads(kind, family)
        && !(family.Kind == AnnotatedTypeMapFamilyKind.FactStream
            && kind == AnnotatedTypeMapResultKind.MapDiffRows
            && !selected.Any(static candidate => candidate.Kind == AnnotatedTypeMapFamilyKind.Finding));
}
