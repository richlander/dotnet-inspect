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
            .. new[] { PublicBucket, ProtectedBucket, InternalBucket, PrivateBucket }
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

/// <summary>
/// Ordered API-surface outcomes for every participant in one assembly context group, plus the
/// accessibility buckets over every type the group's available participants projected.
/// </summary>
public sealed record AssemblyContextApiSurfaceResult(
    AssemblyContextResult<AssemblyApiSurface> Assemblies,
    ImmutableArray<ApiAccessibilityBucket> Accessibility)
{
    public bool IsComplete => Assemblies.IsComplete;
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

    static AssemblyApiSurface Project(
        AssemblyInspectionSession session,
        ApiSurfaceScope scope)
    {
        ApiSurface surface =
            session.ApiSurface(includeAll: scope == ApiSurfaceScope.IncludeAll);
        if (scope != ApiSurfaceScope.PublicWithNonPublicTypes)
            return new AssemblyApiSurface(surface, [.. surface.InspectionFailures]);

        ApiSurface all = session.ApiSurface(includeAll: true);
        var projected = surface.Types
            .Select(MetadataTypeIdentity)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ApiType type in all.Types)
        {
            if (ApiAccessibility.Classify(type.Accessibility).Id != "public"
                && projected.Add(MetadataTypeIdentity(type)))
            {
                surface.Types.Add(type);
            }
        }

        // Both extractions observed the same image, so an inspection failure either surface
        // recorded is a real rejection of the composed projection.
        surface.InspectionFailures =
        [
            .. surface.InspectionFailures
                .Concat(all.InspectionFailures)
                .Distinct(),
        ];
        return new AssemblyApiSurface(surface, [.. surface.InspectionFailures]);
    }

    internal static string MetadataTypeIdentity(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        string name = type.MetadataName ?? type.Name;
        return string.IsNullOrEmpty(type.Namespace)
            ? name
            : $"{type.Namespace}.{name}";
    }
}
