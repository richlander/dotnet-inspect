using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// How much of a participant's API surface a group-scoped surface query projects.
/// </summary>
public enum ApiSurfaceScope
{
    /// <summary>
    /// The default consumer surface: public types with their public members, minus the types and
    /// members the extractor hides (<c>[EditorBrowsable(Never)]</c> and the hidden-attribute set).
    /// </summary>
    Public,

    /// <summary>
    /// Every type and member the extractor reaches, including non-public and hidden ones. This is
    /// the <c>includeAll</c> surface.
    /// </summary>
    IncludeAll,

    /// <summary>
    /// The default consumer surface, plus non-public types from the include-all surface. A type
    /// present in both keeps its default-surface member list, so asking for non-public types never
    /// silently adds private members to a public type. Public types the extractor deliberately
    /// suppresses remain suppressed rather than re-entering the default public bucket with their
    /// include-all member list.
    /// </summary>
    PublicWithNonPublicTypes,
}

/// <summary>
/// One accessibility bucket over a projected API surface: its stable id, its label, the order it
/// is presented in, whether it is the bucket a consumer shows by default, and how many types fell
/// into it.
/// </summary>
/// <remarks>
/// This is the product's answer to "which accessibility groupings does this surface have, and in
/// what order", so a front end neither classifies an accessibility spelling nor invents an order
/// for the labels it shows.
/// </remarks>
public sealed record ApiAccessibilityBucket(
    string Id,
    string Label,
    int Order,
    bool IsDefault,
    int Count);

/// <summary>
/// Classifies the accessibility spellings <see cref="ApiType"/> and <see cref="ApiMember"/> carry
/// into ordered, product-owned buckets.
/// </summary>
/// <remarks>
/// A null or empty spelling means public, which is also how a serialized surface records it.
/// Composite spellings are classified most-visible-first — <c>protected internal</c> and
/// <c>private protected</c> both bucket as <c>protected</c> — because that is the accessibility a
/// consumer of another assembly can actually reach. Gated by
/// <c>Execute_ClassifiesCompositeAccessibilityAsProtected</c>.
/// </remarks>
public static class ApiAccessibility
{
    static readonly ApiAccessibilityBucket PublicBucket =
        new("public", "public", 0, IsDefault: true, Count: 0);

    static readonly ApiAccessibilityBucket ProtectedBucket =
        new("protected", "protected", 1, IsDefault: false, Count: 0);

    static readonly ApiAccessibilityBucket InternalBucket =
        new("internal", "internal", 2, IsDefault: false, Count: 0);

    static readonly ApiAccessibilityBucket PrivateBucket =
        new("private", "private", 3, IsDefault: false, Count: 0);

    /// <summary>
    /// Every product-owned accessibility value in query and presentation order, without
    /// target-specific counts.
    /// </summary>
    public static ImmutableArray<ApiAccessibilityBucket> Values { get; } =
        [PublicBucket, ProtectedBucket, InternalBucket, PrivateBucket];

    /// <summary>The bucket one accessibility spelling belongs to, with a zero count.</summary>
    public static ApiAccessibilityBucket Classify(string? accessibility)
    {
        if (string.IsNullOrWhiteSpace(accessibility))
            return PublicBucket;

        string value = accessibility.ToLowerInvariant();
        return
            value.Contains("protected", StringComparison.Ordinal) ? ProtectedBucket
            : value.Contains("internal", StringComparison.Ordinal) ? InternalBucket
            : value.Contains("private", StringComparison.Ordinal) ? PrivateBucket
            : PublicBucket;
    }

    /// <summary>
    /// The occupied buckets over a set of accessibility spellings, in presentation order, each
    /// carrying its count. The default bucket is always present even when it is empty, so a
    /// consumer's default filter never selects nothing.
    /// </summary>
    public static ImmutableArray<ApiAccessibilityBucket> Buckets(
        IEnumerable<string?> accessibilities)
    {
        ArgumentNullException.ThrowIfNull(accessibilities);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string? accessibility in accessibilities)
        {
            string id = Classify(accessibility).Id;
            counts[id] = counts.TryGetValue(id, out int count) ? count + 1 : 1;
        }

        return
        [
            .. Values
                .Where(bucket => bucket.IsDefault || counts.ContainsKey(bucket.Id))
                .Select(bucket => bucket with
                {
                    Count = counts.TryGetValue(bucket.Id, out int count) ? count : 0,
                })
                .OrderBy(bucket => bucket.Order),
        ];
    }
}

/// <summary>
/// One participant's projected API surface and the metadata rows the extractor rejected while
/// producing it.
/// </summary>
public sealed record AssemblyApiSurface(
    ApiSurface Surface,
    ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

/// <summary>Which bound stopped a bounded API-surface projection.</summary>
public enum ApiSurfaceProjectionLimit
{
    /// <summary>More participants were selected than the projection may walk.</summary>
    Participants,

    /// <summary>The projected types reached the type bound.</summary>
    Types,

    /// <summary>The projected members reached the member bound.</summary>
    Members,

    /// <summary>The projected inspection failures reached their bound.</summary>
    InspectionFailures,

    /// <summary>The projected type forwarders reached their bound.</summary>
    TypeForwarders,

    /// <summary>The inspected metadata rows reached their bound.</summary>
    MetadataRows,

    /// <summary>The projected models reached their retained-text character bound.</summary>
    RetainedTextCharacters,
}

/// <summary>
/// Hard bounds for one API-surface projection. A host with a fixed work and output budget —
/// Browser/Wasm is the motivating one — declares them explicitly instead of invoking the
/// unbounded projection and hoping the artifact is small.
/// </summary>
/// <remarks>
/// The bounds are on the projection, not on one image's metadata: a participant is projected
/// whole or not at all, so no type is ever returned with a shortened member list. Exceeding a
/// bound is reported as <see cref="ApiSurfaceProjectionTruncation"/>, never as a smaller
/// success-shaped result. The bounds are hard: they are enforced inside the extraction that
/// retains the rows (<see cref="ApiSurfaceExtractionBounds"/>), so an over-budget participant is
/// abandoned rather than materialized and then rejected.
/// </remarks>
public sealed record ApiSurfaceProjectionLimits
{
    public ApiSurfaceProjectionLimits(
        int maxParticipants,
        int maxTypes,
        int maxMembers,
        int maxInspectionFailures,
        int maxTypeForwarders,
        int maxMetadataRows)
        : this(
            maxParticipants,
            maxTypes,
            maxMembers,
            maxInspectionFailures,
            maxTypeForwarders,
            maxMetadataRows,
            int.MaxValue)
    {
    }

    public ApiSurfaceProjectionLimits(
        int maxParticipants,
        int maxTypes,
        int maxMembers,
        int maxInspectionFailures,
        int maxTypeForwarders,
        int maxMetadataRows,
        int maxRetainedTextCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParticipants, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTypes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMembers, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInspectionFailures);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTypeForwarders);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMetadataRows);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedTextCharacters);
        MaxParticipants = maxParticipants;
        MaxTypes = maxTypes;
        MaxMembers = maxMembers;
        MaxInspectionFailures = maxInspectionFailures;
        MaxTypeForwarders = maxTypeForwarders;
        MaxMetadataRows = maxMetadataRows;
        MaxRetainedTextCharacters = maxRetainedTextCharacters;
    }

    /// <summary>The most participants one projection may walk.</summary>
    public int MaxParticipants { get; }

    /// <summary>The most types one projection may return.</summary>
    public int MaxTypes { get; }

    /// <summary>The most members one projection may return.</summary>
    public int MaxMembers { get; }

    /// <summary>The most inspection failures one projection may return.</summary>
    public int MaxInspectionFailures { get; }

    /// <summary>The most type forwarders one projection may return.</summary>
    public int MaxTypeForwarders { get; }

    /// <summary>The most metadata rows one projection may inspect.</summary>
    public int MaxMetadataRows { get; }

    /// <summary>
    /// The most text characters the projected model fields may retain across all participants.
    /// Repeated references are charged once per field.
    /// </summary>
    public int MaxRetainedTextCharacters { get; }
}

/// <summary>
/// The explicit report that a bounded projection stopped early: which bound stopped it, what that
/// bound was, and what was and was not projected.
/// </summary>
public sealed record ApiSurfaceProjectionTruncation(
    ApiSurfaceProjectionLimit Limit,
    int Bound,
    int ProjectedParticipants,
    int OmittedParticipants,
    int ProjectedTypes,
    int ProjectedMembers,
    int ProjectedInspectionFailures,
    int ProjectedTypeForwarders,
    int InspectedMetadataRows,
    int ProjectedRetainedTextCharacters);

/// <summary>
/// Ordered API-surface outcomes for every participant in one assembly context group, plus the
/// accessibility buckets over every type the group's available participants projected.
/// </summary>
/// <remarks>
/// <see cref="Truncation"/> is null for a complete projection. When it is not null the result is
/// explicitly partial and <see cref="IsComplete"/> is false, so a consumer cannot mistake a
/// bounded projection for the whole surface.
/// </remarks>
public sealed record AssemblyContextApiSurfaceResult(
    AssemblyContextResult<AssemblyApiSurface> Assemblies,
    ImmutableArray<ApiAccessibilityBucket> Accessibility,
    ApiSurfaceProjectionTruncation? Truncation = null)
{
    public bool IsComplete => Assemblies.IsComplete && Truncation is null;
}

/// <summary>
/// Projects an API surface from every participant in one binding-consistent assembly context
/// group, in deterministic participant order.
/// </summary>
/// <remarks>
/// The query owns every <see cref="AssemblyInspectionSession"/> it opens; a consumer hands it a
/// group and receives typed per-participant availability, rejection, or failure. Ordering,
/// snapshot reuse, participant failure, accessibility bucketing, and inspection-failure
/// preservation are gated by <c>AssemblyContextApiSurfaceQueryTests</c>.
/// </remarks>
public static class AssemblyContextApiSurfaceQuery
{
    public static InspectionQuery<AssemblyContextApiSurfaceResult> Definition { get; } =
        new("Assembly context API surface", InspectionCost.Unbounded);

    /// <summary>
    /// The bounded projection: the same evidence under caller-declared participant, type, and
    /// member, metadata-row, and retained-text bounds, with any early stop reported as
    /// <see cref="ApiSurfaceProjectionTruncation"/>.
    /// </summary>
    public static InspectionQuery<AssemblyContextApiSurfaceResult> BoundedDefinition { get; } =
        new("Assembly context API surface (bounded)", InspectionCost.NetworkFree);

    public static AssemblyContextApiSurfaceResult Execute(
        AssemblyContextGroup group,
        bool includeAll = false)
        => Execute(
            group,
            includeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public);

    public static AssemblyContextApiSurfaceResult Execute(
        AssemblyContextGroup group,
        ApiSurfaceScope scope)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        AssemblyContextResult<AssemblyApiSurface> assemblies =
            AssemblyContextQueryExecutor.Execute(
                group,
                session => Project(session, scope));
        return new AssemblyContextApiSurfaceResult(
            assemblies,
            ApiAccessibility.Buckets(
                assemblies.Assemblies
                    .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
                    .SelectMany(entry => entry.Value.Surface.Types)
                    .Select(type => type.Accessibility)));
    }

    /// <summary>Projects one participant while retaining its complete binding universe.</summary>
    public static AssemblyContextEntry<AssemblyApiSurface> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        ApiSurfaceScope scope = ApiSurfaceScope.Public)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session => Project(session, scope));
    }

    /// <summary>
    /// Projects a selected participant set under explicit bounds, stopping at the first bound it
    /// would exceed and reporting that stop.
    /// </summary>
    /// <param name="group">The binding-consistent group that owns every session opened here.</param>
    /// <param name="scope">How much of each participant's surface to project.</param>
    /// <param name="limits">The caller's hard work and retention bounds.</param>
    /// <param name="participants">
    /// The participants to project, in the order they should be projected. Null projects every
    /// participant of <paramref name="group"/>. Selecting a subset is how a host that needs one
    /// package's surface out of a multi-package workspace avoids materializing the rest.
    /// </param>
    /// <remarks>
    /// <para>
    /// The bound is enforced inside the extraction, not checked after it: each participant is
    /// extracted against the budget the earlier participants left, and one that would exceed it is
    /// abandoned before its surface is materialized. Such a participant is <em>omitted</em> from
    /// the result — a participant is projected whole or not at all — the remaining participants
    /// are not walked, and the result carries an <see cref="ApiSurfaceProjectionTruncation"/>.
    /// Nothing is silently trimmed, and no type is ever returned with a shortened member list.
    /// </para>
    /// <para>
    /// So the projected rows always satisfy the declared bounds:
    /// <see cref="ApiSurfaceProjectionTruncation.ProjectedTypes"/> never exceeds
    /// <see cref="ApiSurfaceProjectionLimits.MaxTypes"/> and
    /// <see cref="ApiSurfaceProjectionTruncation.ProjectedMembers"/> never exceeds
    /// <see cref="ApiSurfaceProjectionLimits.MaxMembers"/>; both count exactly the rows the result
    /// carries. The same applies to retained inspection failures, type forwarders, metadata rows,
    /// and retained model-field text. A participant that was walked and abandoned is counted as
    /// omitted, because none of its rows are returned.
    /// </para>
    /// <para>
    /// Accessibility buckets are computed over the participants that were actually projected, so
    /// a truncated result's buckets describe exactly the rows it carries.
    /// </para>
    /// <para>
    /// Gated by <c>AssemblyContextApiSurfaceQueryTests.ExecuteBounded_*</c> — which asserts the
    /// projected rows themselves, not the truncation report, satisfy both bounds — over the
    /// extraction-side gate <c>ApiSurfaceExtractorBoundsTests</c>.
    /// </para>
    /// </remarks>
    public static AssemblyContextApiSurfaceResult ExecuteBounded(
        AssemblyContextGroup group,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits limits,
        IReadOnlyList<AssemblyContextParticipant>? participants = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(limits);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        IReadOnlyList<AssemblyContextParticipant> selected =
            participants ?? group.Participants;
        ApiSurfaceProjectionTruncation? truncation = null;
        int walked = 0;
        int inspectionFailures = 0;
        int typeForwarders = 0;
        int metadataRows = 0;
        int retainedTextCharacters = 0;
        if (selected.Count > limits.MaxParticipants)
        {
            truncation = new ApiSurfaceProjectionTruncation(
                ApiSurfaceProjectionLimit.Participants,
                limits.MaxParticipants,
                ProjectedParticipants: limits.MaxParticipants,
                OmittedParticipants: selected.Count - limits.MaxParticipants,
                ProjectedTypes: 0,
                ProjectedMembers: 0,
                ProjectedInspectionFailures: 0,
                ProjectedTypeForwarders: 0,
                InspectedMetadataRows: 0,
                ProjectedRetainedTextCharacters: 0);
            selected = [.. selected.Take(limits.MaxParticipants)];
        }

        var entries = ImmutableArray.CreateBuilder<AssemblyContextEntry<AssemblyApiSurface>>(
            selected.Count);
        int types = 0;
        int members = 0;
        foreach (AssemblyContextParticipant participant in selected)
        {
            // Each participant gets the budget its predecessors left, so the extraction stops
            // inside the image that would overflow it instead of after building it.
            var bounds = new ApiSurfaceExtractionBounds(
                limits.MaxTypes - types,
                limits.MaxMembers - members,
                limits.MaxInspectionFailures - inspectionFailures,
                limits.MaxTypeForwarders - typeForwarders,
                limits.MaxMetadataRows - metadataRows,
                limits.MaxRetainedTextCharacters - retainedTextCharacters);
            AssemblyContextEntry<ApiSurfaceExtractionResult> entry =
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    group,
                    participant,
                    session => ProjectBounded(session, scope, bounds));
            walked++;
            if (entry is not AssemblyContextEntry<ApiSurfaceExtractionResult>.Available available)
            {
                entries.Add(Unavailable(entry));
                continue;
            }

            if (available.Value is ApiSurfaceExtractionResult.Exceeded exceeded)
            {
                ApiSurfaceProjectionLimit limit = exceeded.Bound switch
                {
                    ApiSurfaceExtractionBound.Types => ApiSurfaceProjectionLimit.Types,
                    ApiSurfaceExtractionBound.Members => ApiSurfaceProjectionLimit.Members,
                    ApiSurfaceExtractionBound.InspectionFailures =>
                        ApiSurfaceProjectionLimit.InspectionFailures,
                    ApiSurfaceExtractionBound.TypeForwarders =>
                        ApiSurfaceProjectionLimit.TypeForwarders,
                    ApiSurfaceExtractionBound.MetadataRows =>
                        ApiSurfaceProjectionLimit.MetadataRows,
                    ApiSurfaceExtractionBound.RetainedTextCharacters =>
                        ApiSurfaceProjectionLimit.RetainedTextCharacters,
                    _ => throw new InvalidOperationException(
                        "Unknown API-surface extraction bound."),
                };
                int bound = exceeded.Bound switch
                {
                    ApiSurfaceExtractionBound.Types => limits.MaxTypes,
                    ApiSurfaceExtractionBound.Members => limits.MaxMembers,
                    ApiSurfaceExtractionBound.InspectionFailures =>
                        limits.MaxInspectionFailures,
                    ApiSurfaceExtractionBound.TypeForwarders =>
                        limits.MaxTypeForwarders,
                    ApiSurfaceExtractionBound.MetadataRows =>
                        limits.MaxMetadataRows,
                    ApiSurfaceExtractionBound.RetainedTextCharacters =>
                        limits.MaxRetainedTextCharacters,
                    _ => throw new InvalidOperationException(
                        "Unknown API-surface extraction bound."),
                };
                truncation = new ApiSurfaceProjectionTruncation(
                    limit,
                    bound,
                    ProjectedParticipants: entries.Count,
                    OmittedParticipants: selected.Count - walked + 1
                        + (truncation?.OmittedParticipants ?? 0),
                    ProjectedTypes: types,
                    ProjectedMembers: members,
                    ProjectedInspectionFailures: inspectionFailures,
                    ProjectedTypeForwarders: typeForwarders,
                    InspectedMetadataRows: metadataRows,
                    ProjectedRetainedTextCharacters: retainedTextCharacters);
                break;
            }

            var extracted =
                (ApiSurfaceExtractionResult.Extracted)available.Value;
            ApiSurface surface = extracted.Surface;
            entries.Add(
                new AssemblyContextEntry<AssemblyApiSurface>.Available(
                    available.Subject,
                    new AssemblyApiSurface(surface, [.. surface.InspectionFailures])));
            types += surface.Types.Count;
            members += surface.Types.Sum(type => type.Members.Count);
            inspectionFailures += surface.InspectionFailures.Count;
            typeForwarders += surface.TypeForwarders.Count;
            metadataRows += extracted.MetadataRows;
            retainedTextCharacters += extracted.RetainedTextCharacters;
        }

        if (truncation is not null)
        {
            truncation = truncation with
            {
                ProjectedTypes = types,
                ProjectedMembers = members,
                ProjectedInspectionFailures = inspectionFailures,
                ProjectedTypeForwarders = typeForwarders,
                InspectedMetadataRows = metadataRows,
                ProjectedRetainedTextCharacters = retainedTextCharacters,
            };
        }

        var assemblies = new AssemblyContextResult<AssemblyApiSurface>(entries.DrainToImmutable());
        return new AssemblyContextApiSurfaceResult(
            assemblies,
            ApiAccessibility.Buckets(
                assemblies.Assemblies
                    .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
                    .SelectMany(entry => entry.Value.Surface.Types)
                    .Select(type => type.Accessibility)),
            truncation);
    }

    /// <summary>
    /// Projects one participant at one scope. Every scope — including the composed one — is a
    /// single extraction: the extractor owns which types and members each scope keeps, so the
    /// composed surface is no longer built by materializing the same image's surface twice and
    /// discarding most of the second.
    /// </summary>
    static AssemblyApiSurface Project(
        AssemblyInspectionSession session,
        ApiSurfaceScope scope)
    {
        ApiSurface surface = session.ApiSurface(ExtractionScope(scope));
        return new AssemblyApiSurface(surface, [.. surface.InspectionFailures]);
    }

    /// <summary>Projects one participant under the remaining extraction budget.</summary>
    static ApiSurfaceExtractionResult ProjectBounded(
        AssemblyInspectionSession session,
        ApiSurfaceScope scope,
        ApiSurfaceExtractionBounds bounds)
        => session.BoundedApiSurface(ExtractionScope(scope), bounds);

    /// <summary>
    /// Carries a participant outcome that produced no surface across to the projected result's
    /// value type. Rejection and failure belong to the participant, not to the bound, so they are
    /// reported exactly as an unbounded projection reports them.
    /// </summary>
    static AssemblyContextEntry<AssemblyApiSurface> Unavailable(
        AssemblyContextEntry<ApiSurfaceExtractionResult> entry)
        => entry switch
        {
            AssemblyContextEntry<ApiSurfaceExtractionResult>.Rejected rejected =>
                new AssemblyContextEntry<AssemblyApiSurface>.Rejected(
                    rejected.Subject,
                    rejected.Failure),
            AssemblyContextEntry<ApiSurfaceExtractionResult>.Failed failed =>
                new AssemblyContextEntry<AssemblyApiSurface>.Failed(
                    failed.Subject,
                    failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown assembly context participant outcome."),
        };

    static ApiSurfaceExtractionScope ExtractionScope(ApiSurfaceScope scope) => scope switch
    {
        ApiSurfaceScope.IncludeAll => ApiSurfaceExtractionScope.IncludeAll,
        ApiSurfaceScope.PublicWithNonPublicTypes =>
            ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
        _ => ApiSurfaceExtractionScope.Public,
    };

    internal static string MetadataTypeIdentity(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.DefinitionName is { } definitionName)
            return definitionName.ToEscapedFullName();

        string name = type.MetadataName ?? type.Name;
        return string.IsNullOrEmpty(type.Namespace)
            ? name
            : $"{type.Namespace}.{name}";
    }
}
