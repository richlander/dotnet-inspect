using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The production envelope in which a package-query facet is available.</summary>
public enum PackageQueryFacetTier
{
    Nuspec,
}

/// <summary>One product-owned package-query facet.</summary>
/// <param name="Id">Stable opaque identity submitted by consumers without interpretation.</param>
/// <param name="Label">User-facing label.</param>
/// <param name="Summary">Short explanation of the predicate.</param>
/// <param name="Weight">Producer-owned display order; this is not result ranking.</param>
/// <param name="Tier">The production envelope in which the facet is available.</param>
/// <param name="SelectionGroupId">
/// Optional opaque identity shared by facets that cannot be selected together.
/// </param>
public sealed record PackageQueryFacetDescriptor(
    string Id,
    string Label,
    string Summary,
    int Weight,
    PackageQueryFacetTier Tier,
    string? SelectionGroupId = null);

/// <summary>A bounded package-query request over one package-ID prefix.</summary>
public sealed record PackageQueryRequest(
    string Prefix,
    IReadOnlyCollection<string>? FacetIds = null,
    int MaximumCandidates = PackageQuery.DefaultMaximumCandidates,
    int MaximumMatches = PackageQuery.DefaultMaximumMatches,
    bool IncludePrerelease = false);

/// <summary>Why a package-query request could not become an executable plan.</summary>
public enum PackageQueryRequestFailureReason
{
    InvalidPrefix,
    InvalidCandidateLimit,
    InvalidMatchLimit,
    TooManyFacets,
    InvalidFacetId,
    UnknownFacet,
    DuplicateFacet,
    IncompatibleFacets,
}

/// <summary>
/// A typed, content-safe package-query planning failure. Returned facet IDs
/// are always product-issued.
/// </summary>
public sealed record PackageQueryRequestFailure
{
    internal PackageQueryRequestFailure(
        PackageQueryRequestFailureReason reason,
        IEnumerable<string>? facetIds = null,
        int? value = null)
    {
        Reason = reason;
        FacetIds = facetIds is null ? [] : [.. facetIds];
        Value = value;
    }

    public PackageQueryRequestFailureReason Reason { get; }
    public ImmutableArray<string> FacetIds { get; }
    public int? Value { get; }

    public string Message => Reason switch
    {
        PackageQueryRequestFailureReason.InvalidPrefix =>
            "The package-query prefix is invalid.",
        PackageQueryRequestFailureReason.InvalidCandidateLimit =>
            $"The package-query candidate limit must be between 1 and {PackageProfileQuery.MaximumPackageLimit}.",
        PackageQueryRequestFailureReason.InvalidMatchLimit =>
            $"The package-query match limit must be between 1 and {PackageProfileQuery.MaximumPackageLimit}.",
        PackageQueryRequestFailureReason.TooManyFacets =>
            "The package-query request selected more facets than the product offers.",
        PackageQueryRequestFailureReason.InvalidFacetId =>
            "A package-query facet ID is empty or invalid.",
        PackageQueryRequestFailureReason.UnknownFacet =>
            "One or more package-query facet IDs are unknown.",
        PackageQueryRequestFailureReason.DuplicateFacet =>
            "A package-query facet ID was selected more than once.",
        PackageQueryRequestFailureReason.IncompatibleFacets =>
            "The selected package-query facets cannot be combined.",
        _ => "The package-query request is invalid.",
    };
}

/// <summary>The result of validating and lowering one package-query request.</summary>
public abstract record PackageQueryPlanResult
{
    private PackageQueryPlanResult()
    {
    }

    public sealed record Accepted(PackageQueryPlan Plan) : PackageQueryPlanResult;
    public sealed record Rejected(PackageQueryRequestFailure Failure) : PackageQueryPlanResult;
}

/// <summary>
/// One validated package-query plan. Construction is product-owned so execution
/// cannot receive unknown or incompatible facet identities.
/// </summary>
public sealed class PackageQueryPlan
{
    internal PackageQueryPlan(
        InertString prefix,
        InertString prefixEvidence,
        ImmutableArray<PackageQueryFacetDefinition> definitions,
        int maximumCandidates,
        int maximumMatches,
        bool includePrerelease)
    {
        Prefix = prefix;
        PrefixEvidence = prefixEvidence;
        Definitions = definitions;
        Facets = [.. definitions.Select(definition => definition.Descriptor)];
        MaximumCandidates = maximumCandidates;
        MaximumMatches = maximumMatches;
        IncludePrerelease = includePrerelease;
    }

    public InertString Prefix { get; }
    public ImmutableArray<PackageQueryFacetDescriptor> Facets { get; }
    public int MaximumCandidates { get; }
    public int MaximumMatches { get; }
    public bool IncludePrerelease { get; }

    internal InertString PrefixEvidence { get; }
    internal ImmutableArray<PackageQueryFacetDefinition> Definitions { get; }
}

/// <summary>One product-authored explanation for a package-query match.</summary>
public sealed record PackageQueryEvidence(
    string Id,
    InertString Text)
{
    public string Value => Text.ToString();
}

/// <summary>One package that satisfied every selected package-query facet.</summary>
public sealed record PackageQueryMatch(
    PackageProfileMatch Package,
    PackageQueryFacetTier Tier,
    ImmutableArray<PackageQueryEvidence> Evidence);

/// <summary>Why one package-query stream stopped.</summary>
public enum PackageQueryCompletionKind
{
    Exhausted,
    MatchLimitReached,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    Failed,
}

/// <summary>Terminal accounting for one package-query stream.</summary>
public sealed record PackageQuerySummary(
    InertString Prefix,
    PackageSourceIdentity Producer,
    int CandidateLimit,
    int MatchLimit,
    int Candidates,
    int Matches,
    int Failures,
    PackageQueryCompletionKind Completion);

/// <summary>One event from a package-query stream.</summary>
public abstract record PackageQueryEvent
{
    private PackageQueryEvent()
    {
    }

    public sealed record Match(PackageQueryMatch Value) : PackageQueryEvent;
    public sealed record Failure(PackageProfileFailure Value) : PackageQueryEvent;
    public sealed record Completed(PackageQuerySummary Value) : PackageQueryEvent;
}

internal sealed record PackageQueryFacetDefinition(
    PackageQueryFacetDescriptor Descriptor,
    Func<PackageProfileMatch, bool> Matches,
    Func<PackageProfileMatch, InertString> Evidence);

/// <summary>
/// Plans and executes product-owned nuspec-tier facets over a bounded package
/// profile without acquiring package archives or assemblies.
/// </summary>
public static class PackageQuery
{
    public const int DefaultMaximumCandidates = 200;
    public const int DefaultMaximumMatches = 100;
    public const int MaximumFacetIdLength = 100;

    public const string PrefixEvidenceId = "package.query.scope.prefix";
    public const string VerifiedFacetId = "package.query.source-verified";
    public const string ToolFacetId = "package.query.dotnet-tool";
    public const string HasDependenciesFacetId = "package.query.has-dependencies";
    public const string NoDependenciesFacetId = "package.query.no-dependencies";
    public const string MillionDownloadsFacetId = "package.query.downloads-1m";
    public const string EmbeddedReadmeFacetId = "package.query.embedded-readme";
    public const string DependencySelectionGroupId = "package.query.dependencies";

    static readonly ImmutableArray<PackageQueryFacetDefinition> Definitions =
    [
        new(
            new PackageQueryFacetDescriptor(
                VerifiedFacetId,
                "source verified",
                "The package source reports a verified package identity.",
                100,
                PackageQueryFacetTier.Nuspec),
            static match => match.Verified,
            static _ => Evidence(
                "The package source reports this package as verified.")),
        new(
            new PackageQueryFacetDescriptor(
                ToolFacetId,
                ".NET tool",
                "The package manifest declares the .NET tool package type.",
                200,
                PackageQueryFacetTier.Nuspec),
            static match => match.Manifest.IsToolPackage,
            static _ => Evidence(
                "The package manifest declares a .NET tool package.")),
        new(
            new PackageQueryFacetDescriptor(
                HasDependenciesFacetId,
                "has dependencies",
                "The package manifest declares at least one dependency.",
                300,
                PackageQueryFacetTier.Nuspec,
                DependencySelectionGroupId),
            static match => DependencyCount(match) > 0,
            static match =>
            {
                int dependencies = DependencyCount(match);
                int groups = NonEmptyDependencyGroupCount(match);
                return Evidence(
                    $"The package manifest declares {dependencies.ToString(CultureInfo.InvariantCulture)} "
                    + $"{Pluralize(dependencies, "dependency", "dependencies")} across "
                    + $"{groups.ToString(CultureInfo.InvariantCulture)} target-framework "
                    + $"{Pluralize(groups, "group", "groups")}.");
            }),
        new(
            new PackageQueryFacetDescriptor(
                NoDependenciesFacetId,
                "no dependencies",
                "The package manifest declares no dependencies.",
                400,
                PackageQueryFacetTier.Nuspec,
                DependencySelectionGroupId),
            static match => DependencyCount(match) == 0,
            static _ => Evidence(
                "The package manifest declares no dependencies.")),
        new(
            new PackageQueryFacetDescriptor(
                MillionDownloadsFacetId,
                "1M+ downloads",
                "The package source reports at least one million total downloads.",
                500,
                PackageQueryFacetTier.Nuspec),
            static match => match.TotalDownloads >= 1_000_000,
            static match => Evidence(
                $"The package source reports {match.TotalDownloads.ToString("N0", CultureInfo.InvariantCulture)} total downloads.")),
        new(
            new PackageQueryFacetDescriptor(
                EmbeddedReadmeFacetId,
                "embedded README",
                "The package manifest declares an embedded README file.",
                600,
                PackageQueryFacetTier.Nuspec),
            static match => !string.IsNullOrWhiteSpace(
                match.Manifest.ReadmeFile),
            static _ => Evidence(
                "The package manifest declares an embedded README file.")),
    ];

    static readonly IReadOnlyDictionary<string, PackageQueryFacetDefinition>
        DefinitionsById = Definitions.ToDictionary(
            definition => definition.Descriptor.Id,
            StringComparer.Ordinal);

    /// <summary>
    /// The complete ordered facet vocabulary. The literal ID set is gated by
    /// <c>FacetDescriptors_HaveStableOrderedIds</c>.
    /// </summary>
    public static ImmutableArray<PackageQueryFacetDescriptor> Facets { get; } =
        [.. Definitions.Select(definition => definition.Descriptor)];

    /// <summary>
    /// Validates and lowers a request without throwing for user-controlled
    /// prefix, bound, or facet values.
    /// </summary>
    public static PackageQueryPlanResult Plan(PackageQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string prefixEvidence =
            $"Package ID matches prefix \"{request.Prefix}\".";
        if (!PackageProfileQuery.IsValidPrefix(request.Prefix)
            || !InertString.IsPermitted(TextPolicy.Prose, request.Prefix)
            || !InertString.IsPermitted(TextPolicy.Prose, prefixEvidence))
        {
            return Rejected(PackageQueryRequestFailureReason.InvalidPrefix);
        }

        if (request.MaximumCandidates
            is <= 0 or > PackageProfileQuery.MaximumPackageLimit)
        {
            return Rejected(
                PackageQueryRequestFailureReason.InvalidCandidateLimit,
                value: request.MaximumCandidates);
        }

        if (request.MaximumMatches
            is <= 0 or > PackageProfileQuery.MaximumPackageLimit)
        {
            return Rejected(
                PackageQueryRequestFailureReason.InvalidMatchLimit,
                value: request.MaximumMatches);
        }

        IReadOnlyCollection<string> requested =
            request.FacetIds ?? [];
        if (requested.Count > Definitions.Length)
        {
            return Rejected(PackageQueryRequestFailureReason.TooManyFacets);
        }

        if (requested.Any(id =>
            string.IsNullOrWhiteSpace(id)
            || id.Length > MaximumFacetIdLength
            || !InertString.IsPermitted(TextPolicy.Field, id)))
        {
            return Rejected(PackageQueryRequestFailureReason.InvalidFacetId);
        }

        ImmutableArray<string> requestedIds = [.. requested];
        var selectedIds = requestedIds.ToHashSet(StringComparer.Ordinal);
        if (selectedIds.Any(id => !DefinitionsById.ContainsKey(id)))
        {
            return Rejected(PackageQueryRequestFailureReason.UnknownFacet);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string[] duplicates =
        [
            .. requestedIds
                .Where(id => !seen.Add(id))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        if (duplicates.Length > 0)
        {
            return Rejected(
                PackageQueryRequestFailureReason.DuplicateFacet,
                duplicates);
        }

        ImmutableArray<PackageQueryFacetDefinition> selected =
        [
            .. Definitions.Where(definition =>
                selectedIds.Contains(definition.Descriptor.Id)),
        ];
        IGrouping<string, PackageQueryFacetDefinition>? incompatible =
            selected
                .Where(definition =>
                    definition.Descriptor.SelectionGroupId is not null)
                .GroupBy(definition =>
                    definition.Descriptor.SelectionGroupId!,
                    StringComparer.Ordinal)
                .FirstOrDefault(group => group.Skip(1).Any());
        if (incompatible is not null)
        {
            return Rejected(
                PackageQueryRequestFailureReason.IncompatibleFacets,
                incompatible.Select(definition =>
                    definition.Descriptor.Id));
        }

        return new PackageQueryPlanResult.Accepted(
            new PackageQueryPlan(
                Evidence(request.Prefix),
                Evidence(prefixEvidence),
                selected,
                request.MaximumCandidates,
                request.MaximumMatches,
                request.IncludePrerelease));
    }

    /// <summary>Executes and materializes one validated package query.</summary>
    public static async ValueTask<ImmutableArray<PackageQueryEvent>>
        ExecuteToArrayAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            CancellationToken cancellationToken = default)
    {
        var events = ImmutableArray.CreateBuilder<PackageQueryEvent>();
        await foreach (PackageQueryEvent queryEvent in ExecuteAsync(
            source,
            plan,
            cancellationToken).ConfigureAwait(false))
        {
            events.Add(queryEvent);
        }

        return events.ToImmutable();
    }

    public static async IAsyncEnumerable<PackageQueryEvent> ExecuteAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        int candidates = 0;
        int matches = 0;
        int failures = 0;
        cancellationToken.ThrowIfCancellationRequested();
        await foreach (PackageProfileEvent profileEvent
            in PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest(
                    plan.Prefix.ToString(),
                    plan.MaximumCandidates,
                    plan.IncludePrerelease),
                cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (profileEvent)
            {
                case PackageProfileEvent.Match match:
                    candidates++;
                    if (!TryMatch(plan, match.Value, out var evidence))
                        continue;

                    matches++;
                    yield return new PackageQueryEvent.Match(
                        new PackageQueryMatch(
                            match.Value,
                            PackageQueryFacetTier.Nuspec,
                            evidence));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (matches >= plan.MaximumMatches)
                    {
                        yield return Completed(
                            plan,
                            source.Identity,
                            candidates,
                            matches,
                            failures,
                            PackageQueryCompletionKind.MatchLimitReached);
                        yield break;
                    }
                    break;

                case PackageProfileEvent.Failure failure:
                    failures++;
                    if (failure.Value.Kind
                        is not PackageProfileFailureKind.Search)
                    {
                        candidates++;
                    }

                    yield return new PackageQueryEvent.Failure(failure.Value);
                    break;

                case PackageProfileEvent.Completed completed:
                    yield return Completed(
                        plan,
                        completed.Value.Producer,
                        completed.Value.Candidates,
                        matches,
                        completed.Value.Failures,
                        completed.Value.Candidates == 0
                            && completed.Value.Failures > 0
                            ? PackageQueryCompletionKind.Failed
                            : MapCompletion(
                                completed.Value.TruncationReason));
                    yield break;
            }
        }

        throw new InvalidOperationException(
            "The package profile stream ended without a completion event.");
    }

    static bool TryMatch(
        PackageQueryPlan plan,
        PackageProfileMatch match,
        out ImmutableArray<PackageQueryEvidence> evidence)
    {
        var builder = ImmutableArray.CreateBuilder<PackageQueryEvidence>(
            plan.Definitions.Length + 1);
        builder.Add(
            new PackageQueryEvidence(
                PrefixEvidenceId,
                plan.PrefixEvidence));
        foreach (PackageQueryFacetDefinition definition in plan.Definitions)
        {
            if (!definition.Matches(match))
            {
                evidence = [];
                return false;
            }

            builder.Add(
                new PackageQueryEvidence(
                    definition.Descriptor.Id,
                    definition.Evidence(match)));
        }

        evidence = builder.MoveToImmutable();
        return true;
    }

    static int DependencyCount(PackageProfileMatch match) =>
        match.Manifest.DependencyGroups.Sum(group =>
            group.Dependencies.Length);

    static int NonEmptyDependencyGroupCount(PackageProfileMatch match) =>
        match.Manifest.DependencyGroups.Count(group =>
            !group.Dependencies.IsEmpty);

    static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? singular : plural;

    static InertString Evidence(string value) =>
        new(TextPolicy.Prose, value);

    static PackageQueryEvent.Completed Completed(
        PackageQueryPlan plan,
        PackageSourceIdentity producer,
        int candidates,
        int matches,
        int failures,
        PackageQueryCompletionKind completion) =>
        new(
            new PackageQuerySummary(
                plan.Prefix,
                producer,
                plan.MaximumCandidates,
                plan.MaximumMatches,
                candidates,
                matches,
                failures,
                completion));

    static PackageQueryCompletionKind MapCompletion(
        PackageSearchTruncationReason reason) =>
        reason switch
        {
            PackageSearchTruncationReason.None =>
                PackageQueryCompletionKind.Exhausted,
            PackageSearchTruncationReason.RequestedLimit =>
                PackageQueryCompletionKind.CandidateLimitReached,
            PackageSearchTruncationReason.SourcePageLimit =>
                PackageQueryCompletionKind.SourcePageLimitReached,
            PackageSearchTruncationReason.ClientPageLimit =>
                PackageQueryCompletionKind.ClientPageLimitReached,
            _ => throw new InvalidOperationException(
                "Unknown package search truncation reason."),
        };

    static PackageQueryPlanResult.Rejected Rejected(
        PackageQueryRequestFailureReason reason,
        IEnumerable<string>? facetIds = null,
        int? value = null) =>
        new(new PackageQueryRequestFailure(reason, facetIds, value));
}
