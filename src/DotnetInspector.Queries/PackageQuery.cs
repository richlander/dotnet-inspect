using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The production envelope in which a package-query facet is available.</summary>
public enum PackageQueryFacetTier
{
    Nuspec,
    PackageContent,
    SearchMetadata,
}

/// <summary>One product-owned package-query facet.</summary>
/// <param name="Id">Stable opaque identity submitted by consumers without interpretation.</param>
/// <param name="Label">User-facing label.</param>
/// <param name="Summary">Short explanation of the predicate.</param>
/// <param name="Weight">Producer-owned display order; this is not result ranking.</param>
/// <param name="Tier">The production envelope in which the facet is available.</param>
/// <param name="SelectionGroupId">
/// Optional opaque identity shared by facets with product-owned compatibility
/// and OR-combination semantics.
/// </param>
/// <param name="DisplayGroupId">
/// Optional opaque identity shared by facets rendered as one grouped control.
/// </param>
/// <param name="DisplayGroupLabel">Accessible label for the grouped control.</param>
public sealed record PackageQueryFacetDescriptor(
    string Id,
    string Label,
    string Summary,
    int Weight,
    PackageQueryFacetTier Tier,
    string? SelectionGroupId = null,
    string? DisplayGroupId = null,
    string? DisplayGroupLabel = null)
{
    /// <summary>
    /// Whether this facet can be OR-combined with other combining facets in
    /// its selection group.
    /// </summary>
    public bool CombinesWithinSelectionGroup { get; init; }
}

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
    PackageContentCandidateLimitExceeded,
    InvalidSearchText,
    InvalidPackageInput,
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
        PackageQueryRequestFailureReason.InvalidSearchText =>
            "The Gallery search text is invalid.",
        PackageQueryRequestFailureReason.InvalidPackageInput =>
            "Enter a package ID or a literal package-ID prefix followed by one '*'.",
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
        PackageQueryRequestFailureReason.PackageContentCandidateLimitExceeded =>
            $"Package-content facets accept at most {PackageQuery.MaximumPackageContentCandidates} candidates.",
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
        bool includePrerelease,
        NuGetGalleryDiscoveryRequest? galleryRequest = null,
        SourceSelector? packageInput = null)
    {
        Prefix = prefix;
        PrefixEvidence = prefixEvidence;
        Definitions = definitions;
        Facets = [.. definitions.Select(definition => definition.Descriptor)];
        MaximumCandidates = maximumCandidates;
        MaximumMatches = maximumMatches;
        IncludePrerelease = includePrerelease;
        GalleryRequest = galleryRequest;
        PackageInput = packageInput;
    }

    public InertString Prefix { get; }
    public ImmutableArray<PackageQueryFacetDescriptor> Facets { get; }
    public int MaximumCandidates { get; }
    public int MaximumMatches { get; }
    public bool IncludePrerelease { get; }
    public NuGetGalleryDiscoveryRequest? GalleryRequest { get; }
    public SourceSelector? PackageInput { get; }

    internal InertString PrefixEvidence { get; }
    internal ImmutableArray<PackageQueryFacetDefinition> Definitions { get; }
}

/// <summary>Whether evidence describes the query input or an inspected package.</summary>
public enum PackageQueryEvidenceScope
{
    Package,
    Query,
}

/// <summary>A complete observed item count and bounded inert display previews.</summary>
public sealed record PackageQueryEvidenceSummary(
    int Count,
    ImmutableArray<InertString> Preview);

/// <summary>One product-authored explanation for a package-query match.</summary>
public sealed record PackageQueryEvidence(
    string Id,
    InertString Text)
{
    public PackageQueryEvidenceScope Scope { get; init; }
    public PackageQueryEvidenceSummary? Summary { get; init; }
    public string Value => Text.ToString();
}

/// <summary>One package that satisfied every selected package-query facet.</summary>
public sealed record PackageQueryMatch(
    PackageQueryPackage Package,
    PackageQueryFacetTier Tier,
    ImmutableArray<PackageQueryEvidence> Evidence)
{
    public PackageQueryMatch(
        PackageProfileMatch Package,
        PackageQueryFacetTier Tier,
        ImmutableArray<PackageQueryEvidence> Evidence)
        : this(new PackageQueryPackage(Package), Tier, Evidence)
    {
    }
}

/// <summary>The stage at which one package-query item failed.</summary>
public enum PackageQueryFailureKind
{
    Search,
    SearchContract,
    ManifestAcquisition,
    ManifestContract,
    InvalidManifest,
    PackageContentAcquisition,
    PackageContentEvaluation,
}

/// <summary>One visible package-query failure.</summary>
public sealed record PackageQueryFailure(
    string? PackageId,
    string? Version,
    PackageSourceResultIdentity Source,
    PackageQueryFailureKind Kind,
    string Message,
    PackageManifestFailureReason? ManifestFailureReason = null);

/// <summary>Why one package-query stream stopped.</summary>
public enum PackageQueryCompletionKind
{
    Exhausted,
    MatchLimitReached,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    Failed,
    GalleryResponseComplete,
    ExactPackageComplete,
}

/// <summary>Terminal accounting for one package-query stream.</summary>
public sealed record PackageQuerySummary(
    InertString Prefix,
    PackageSourceResultIdentity Source,
    int CandidateLimit,
    int MatchLimit,
    int Candidates,
    int Matches,
    int Failures,
    PackageQueryCompletionKind Completion)
{
    public int? SourceCandidates { get; init; }
    public long? EstimatedTotalHits { get; init; }
}

/// <summary>A bounded checkpoint in package-query work.</summary>
public sealed record PackageQueryProgress(
    PackageQueryProgressPhase Phase,
    int Completed,
    int Limit);

/// <summary>The user-meaningful phase represented by package-query progress.</summary>
public enum PackageQueryProgressPhase
{
    Search,
    Manifest,
    PackageContent,
}

/// <summary>One event from a package-query stream.</summary>
public abstract record PackageQueryEvent
{
    private PackageQueryEvent()
    {
    }

    public sealed record Progress(PackageQueryProgress Value)
        : PackageQueryEvent;

    public sealed record Match(PackageQueryMatch Value) : PackageQueryEvent;
    public sealed record Failure(PackageQueryFailure Value) : PackageQueryEvent;
    public sealed record Completed(PackageQuerySummary Value) : PackageQueryEvent;
}

/// <summary>The result of acquiring admitted package content for one query candidate.</summary>
public abstract record PackageQueryContentResult
{
    private PackageQueryContentResult()
    {
    }

    public sealed record Available(IPackageContent Content)
        : PackageQueryContentResult;

    public sealed record Unavailable(string Message)
        : PackageQueryContentResult;
}

/// <summary>
/// Host capability for acquiring one exact candidate's admitted package content.
/// </summary>
public interface IPackageQueryContentProvider
{
    ValueTask<PackageQueryContentResult> GetContentAsync(
        PackageQueryPackage package,
        CancellationToken cancellationToken);
}

internal sealed record PackageQueryFacetDefinition(
    PackageQueryFacetDescriptor Descriptor,
    Func<PackageQueryPackage, bool> MatchesManifest,
    Func<PackageQueryPackage, PackageContentFacts?, PackageQueryFacetEvidence> Evidence,
    Func<PackageContentFacts, bool>? MatchesPackageContent = null);

internal sealed record PackageQueryFacetEvidence(
    InertString Text,
    PackageQueryEvidenceSummary? Summary = null);

internal sealed record PackageContentFacts(
    PackageQueryEvidenceSummary? SkillDocuments,
    string? ToolSettingsVersion);

/// <summary>
/// Plans and executes product-owned manifest and package-content facets over a
/// bounded package profile without loading inspected assemblies.
/// </summary>
public static partial class PackageQuery
{
    public const int DefaultMaximumCandidates = 200;
    public const int DefaultMaximumMatches = 100;
    public const int MaximumPackageContentCandidates = 20;
    public const int MaximumFacetIdLength = 100;
    public const int MaximumToolSettingsBytes = 64 * 1024;
    public const int MaximumEvidencePreviewItems = 3;
    public const int MaximumEvidencePreviewCharacters = 160;

    public const string PrefixEvidenceId = "package.query.scope.prefix";
    public const string ExactPackageEvidenceId = "package.query.scope.exact-package";
    public const string VerifiedFacetId = "package.query.source-verified";
    public const string ToolFacetId = "package.query.dotnet-tool";
    public const string ToolV1FacetId = "package.query.dotnet-tool-v1";
    public const string ToolV2FacetId = "package.query.dotnet-tool-v2";
    public const string HasDependenciesFacetId = "package.query.has-dependencies";
    public const string NoDependenciesFacetId = "package.query.no-dependencies";
    public const string MillionDownloadsFacetId = "package.query.downloads-1m";
    public const string EmbeddedReadmeFacetId = "package.query.embedded-readme";
    public const string EmbeddedSkillFacetId = "package.query.embedded-skill";
    public const string DependencySelectionGroupId = "package.query.dependencies";
    public const string ToolSelectionGroupId = "package.query.dotnet-tool-format";
    public const string ToolDisplayGroupId = "package.query.display.dotnet-tool";

    static readonly ImmutableArray<PackageQueryFacetDefinition> Definitions =
    [
        new(
            new PackageQueryFacetDescriptor(
                VerifiedFacetId,
                "source verified",
                "The package source reports a verified package identity.",
                100,
                PackageQueryFacetTier.Nuspec),
            static match => match.Verified == true,
            static (_, _) => Describe(
                "The package source reports this package as verified.")),
        new(
            new PackageQueryFacetDescriptor(
                ToolFacetId,
                ".NET Tool",
                "The package manifest declares the .NET tool package type.",
                200,
                PackageQueryFacetTier.Nuspec,
                ToolSelectionGroupId,
                ToolDisplayGroupId,
                ".NET tool format"),
            static match => match.RequiredManifest.IsToolPackage,
            static (_, _) => Describe(
                "The package manifest declares a .NET tool package.")),
        new(
            new PackageQueryFacetDescriptor(
                ToolV1FacetId,
                "v1",
                "Downloads the package and matches the portable .NET tool format.",
                210,
                PackageQueryFacetTier.PackageContent,
                ToolSelectionGroupId,
                ToolDisplayGroupId,
                ".NET tool format")
            {
                CombinesWithinSelectionGroup = true,
            },
            static match => match.RequiredManifest.IsToolPackage,
            static (_, _) => Describe(
                "DotnetToolSettings.xml declares the portable .NET tool v1 format."),
            static content => content.ToolSettingsVersion == "1"),
        new(
            new PackageQueryFacetDescriptor(
                ToolV2FacetId,
                "v2",
                "Downloads the package and matches the RID-specific .NET tool format.",
                220,
                PackageQueryFacetTier.PackageContent,
                ToolSelectionGroupId,
                ToolDisplayGroupId,
                ".NET tool format")
            {
                CombinesWithinSelectionGroup = true,
            },
            static match => match.RequiredManifest.IsToolPackage,
            static (_, _) => Describe(
                "DotnetToolSettings.xml declares the RID-specific .NET tool v2 format."),
            static content => content.ToolSettingsVersion == "2"),
        new(
            new PackageQueryFacetDescriptor(
                HasDependenciesFacetId,
                "has dependencies",
                "The package manifest declares at least one dependency.",
                300,
                PackageQueryFacetTier.Nuspec,
                DependencySelectionGroupId),
            static match => HasDependencies(match),
            static (match, _) => DescribeDependencies(match)),
        new(
            new PackageQueryFacetDescriptor(
                NoDependenciesFacetId,
                "no dependencies",
                "The package manifest declares no dependencies.",
                400,
                PackageQueryFacetTier.Nuspec,
                DependencySelectionGroupId),
            static match => !HasDependencies(match),
            static (match, _) => DescribeDependencies(match)),
        new(
            new PackageQueryFacetDescriptor(
                MillionDownloadsFacetId,
                "1M+ downloads",
                "The package source reports at least one million total downloads.",
                500,
                PackageQueryFacetTier.Nuspec),
            static match => match.TotalDownloads >= 1_000_000,
            static (match, _) => Describe(
                $"The package source reports {match.TotalDownloads?.ToString("N0", CultureInfo.InvariantCulture)} total downloads.")),
        new(
            new PackageQueryFacetDescriptor(
                EmbeddedReadmeFacetId,
                "embedded README",
                "The package manifest declares an embedded README file.",
                600,
                PackageQueryFacetTier.Nuspec),
            static match => !string.IsNullOrWhiteSpace(
                match.RequiredManifest.ReadmeFile),
            static (_, _) => Describe(
                "The package manifest declares an embedded README file.")),
        new(
            new PackageQueryFacetDescriptor(
                EmbeddedSkillFacetId,
                "embedded SKILL.md",
                "Downloads the package and matches a skills/SKILL.md or skills/**/SKILL.md file.",
                700,
                PackageQueryFacetTier.PackageContent),
            static _ => true,
            static (_, content) => DescribeItems(
                content?.SkillDocuments
                    ?? throw new InvalidOperationException(
                        "Skill-document evidence requires its package-content inventory."),
                "skill document",
                "skill documents"),
            static content => content.SkillDocuments is { Count: > 0 }),
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

        string prefix = request.Prefix.EndsWith('*')
            ? request.Prefix[..^1]
            : request.Prefix;
        string prefixEvidence =
            $"Package ID matches prefix \"{prefix}\".";
        if (prefix.Contains('*')
            || !PackageProfileQuery.IsValidPrefix(prefix)
            || !InertString.IsPermitted(TextPolicy.Prose, prefix)
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

        return PlanCore(
            Evidence(prefix),
            Evidence(prefixEvidence),
            request.FacetIds,
            request.MaximumCandidates,
            request.MaximumMatches,
            request.IncludePrerelease);
    }

    static PackageQueryPlanResult PlanCore(
        InertString prefix,
        InertString scopeEvidence,
        IReadOnlyCollection<string>? facetIds,
        int maximumCandidates,
        int maximumMatches,
        bool includePrerelease,
        NuGetGalleryDiscoveryRequest? galleryRequest = null,
        SourceSelector? packageInput = null)
    {
        if (maximumMatches
            is <= 0 or > PackageProfileQuery.MaximumPackageLimit)
        {
            return Rejected(
                PackageQueryRequestFailureReason.InvalidMatchLimit,
                value: maximumMatches);
        }

        IReadOnlyCollection<string> requested =
            facetIds ?? [];
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
                .FirstOrDefault(group =>
                    group.Skip(1).Any()
                    && group.Any(definition =>
                        !definition.Descriptor
                            .CombinesWithinSelectionGroup));
        if (incompatible is not null)
        {
            return Rejected(
                PackageQueryRequestFailureReason.IncompatibleFacets,
                incompatible.Select(definition =>
                    definition.Descriptor.Id));
        }
        if (maximumCandidates > MaximumPackageContentCandidates
            && selected.Any(definition =>
                definition.Descriptor.Tier
                    == PackageQueryFacetTier.PackageContent))
        {
            return Rejected(
                PackageQueryRequestFailureReason
                    .PackageContentCandidateLimitExceeded,
                value: maximumCandidates);
        }

        return new PackageQueryPlanResult.Accepted(
            new PackageQueryPlan(
                prefix,
                scopeEvidence,
                selected,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                galleryRequest,
                packageInput));
    }

    /// <summary>Executes and materializes one validated package query.</summary>
    public static async ValueTask<ImmutableArray<PackageQueryEvent>>
        ExecuteToArrayAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            CancellationToken cancellationToken = default)
        => await ExecuteToArrayAsync(
            source,
            plan,
            contentProvider: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Executes and materializes one validated package query with the explicit
    /// package-content capability required by package-content facets.
    /// </summary>
    public static async ValueTask<ImmutableArray<PackageQueryEvent>>
        ExecuteToArrayAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            CancellationToken cancellationToken = default)
    {
        var events = ImmutableArray.CreateBuilder<PackageQueryEvent>();
        await foreach (PackageQueryEvent queryEvent in ExecuteAsync(
            source,
            plan,
            contentProvider,
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
        await foreach (PackageQueryEvent queryEvent in ExecuteAsync(
            source,
            plan,
            contentProvider: null,
            cancellationToken).ConfigureAwait(false))
        {
            yield return queryEvent;
        }
    }

    /// <summary>
    /// Executes a package query with the explicit host capability required to
    /// acquire admitted package content.
    /// </summary>
    public static async IAsyncEnumerable<PackageQueryEvent> ExecuteAsync(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        IPackageQueryContentProvider? contentProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        bool requiresPackageContent = plan.Definitions.Any(definition =>
            definition.Descriptor.Tier
                == PackageQueryFacetTier.PackageContent);
        if (requiresPackageContent && contentProvider is null)
        {
            throw new InvalidOperationException(
                "Package-content facets require an explicit package-content provider.");
        }

        int candidates = 0;
        int matches = 0;
        int failures = 0;
        int packageContentCompleted = 0;
        int? sourceCandidates = null;
        long? estimatedTotalHits = null;
        bool searchOutcomeObserved = false;
        bool sourceSearchFailed = false;
        cancellationToken.ThrowIfCancellationRequested();
        yield return Progress(
            PackageQueryProgressPhase.Search,
            completed: 0,
            limit: 1);
        await foreach (PackageQueryInputEvent inputEvent
            in AcquireInputAsync(source, plan, cancellationToken)
                .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inputEvent is PackageQueryInputEvent.Acquired acquired)
            {
                sourceCandidates = acquired.Count;
                estimatedTotalHits = acquired.EstimatedTotalHits;
                searchOutcomeObserved = true;
                yield return Progress(
                    PackageQueryProgressPhase.Search, completed: 1, limit: 1);
                continue;
            }
            bool sourceWideSearchFailure =
                inputEvent is PackageQueryInputEvent.Failure searchFailure
                && IsSourceWideSearchFailure(searchFailure.Value);
            sourceSearchFailed |= sourceWideSearchFailure;
            if (!searchOutcomeObserved)
            {
                searchOutcomeObserved = true;
                if (!sourceWideSearchFailure)
                {
                    yield return Progress(
                        PackageQueryProgressPhase.Search,
                        completed: 1,
                        limit: 1);
                }
            }

            switch (inputEvent)
            {
                case PackageQueryInputEvent.Match match:
                    candidates++;
                    if (match.Value.Manifest is not null)
                    {
                        yield return Progress(
                            PackageQueryProgressPhase.Manifest,
                            candidates,
                            plan.MaximumCandidates);
                    }
                    if (!TryMatchManifest(
                        plan,
                        match.Value,
                        out ImmutableArray<PackageQueryEvidence>.Builder
                            evidence))
                        continue;

                    if (requiresPackageContent)
                    {
                        if (packageContentCompleted == 0)
                        {
                            yield return Progress(
                                PackageQueryProgressPhase.PackageContent,
                                completed: 0,
                                limit: plan.MaximumCandidates);
                        }
                        PackageQueryContentResult contentResult =
                            await contentProvider!.GetContentAsync(
                                match.Value,
                                cancellationToken).ConfigureAwait(false);
                        if (contentResult
                            is PackageQueryContentResult.Unavailable unavailable)
                        {
                            packageContentCompleted++;
                            yield return Progress(
                                PackageQueryProgressPhase.PackageContent,
                                packageContentCompleted,
                                plan.MaximumCandidates);
                            failures++;
                            yield return new PackageQueryEvent.Failure(
                                new PackageQueryFailure(
                                    match.Value.PackageId,
                                    match.Value.Version,
                                    match.Value.Source,
                                    PackageQueryFailureKind
                                        .PackageContentAcquisition,
                                    unavailable.Message));
                            continue;
                        }

                        IPackageContent content =
                            ((PackageQueryContentResult.Available)contentResult)
                                .Content;
                        PackageContentFacts? facts = null;
                        try
                        {
                            facts = await ReadPackageContentFactsAsync(
                                content,
                                plan.Definitions,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (
                            ex is IOException
                                or InvalidDataException
                                or DecoderFallbackException
                                or NotSupportedException
                                or UnauthorizedAccessException
                                or XmlException)
                        {
                            // Iterator catch clauses cannot yield; null is
                            // projected below as a typed item failure.
                        }
                        if (facts is null)
                        {
                            packageContentCompleted++;
                            yield return Progress(
                                PackageQueryProgressPhase.PackageContent,
                                packageContentCompleted,
                                plan.MaximumCandidates);
                            failures++;
                            yield return new PackageQueryEvent.Failure(
                                new PackageQueryFailure(
                                    match.Value.PackageId,
                                    match.Value.Version,
                                    match.Value.Source,
                                    PackageQueryFailureKind
                                        .PackageContentEvaluation,
                                    "The package content could not be evaluated."));
                            continue;
                        }

                        packageContentCompleted++;
                        yield return Progress(
                            PackageQueryProgressPhase.PackageContent,
                            packageContentCompleted,
                            plan.MaximumCandidates);
                        if (!TryMatchPackageContent(
                            plan,
                            match.Value,
                            facts,
                            evidence))
                        {
                            continue;
                        }
                    }

                    matches++;
                    yield return new PackageQueryEvent.Match(
                        new PackageQueryMatch(
                            match.Value,
                            requiresPackageContent
                                ? PackageQueryFacetTier.PackageContent
                                : match.Value.Manifest is not null
                                    ? PackageQueryFacetTier.Nuspec
                                    : PackageQueryFacetTier.SearchMetadata,
                            evidence.ToImmutable()));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (matches >= plan.MaximumMatches)
                    {
                        yield return Completed(
                            plan,
                            source.Source,
                            candidates,
                            matches,
                            failures,
                            plan.PackageInput is SourceSelector.Package
                                ? PackageQueryCompletionKind.ExactPackageComplete
                                : sourceCandidates == candidates
                                ? PackageQueryCompletionKind.GalleryResponseComplete
                                : PackageQueryCompletionKind.MatchLimitReached,
                            sourceCandidates,
                            estimatedTotalHits);
                        yield break;
                    }
                    break;

                case PackageQueryInputEvent.Failure failure:
                    failures++;
                    if (!sourceWideSearchFailure)
                    {
                        candidates++;
                        yield return Progress(
                            PackageQueryProgressPhase.Manifest,
                            candidates,
                            plan.MaximumCandidates);
                    }

                    yield return new PackageQueryEvent.Failure(failure.Value);
                    break;

                case PackageQueryInputEvent.Completed completed:
                    yield return Completed(
                        plan,
                        source.Source,
                        completed.Candidates,
                        matches,
                        failures,
                        sourceSearchFailed
                            ? PackageQueryCompletionKind.Failed
                            : completed.Completion,
                        sourceCandidates,
                        estimatedTotalHits);
                    yield break;
            }
        }

        throw new InvalidOperationException(
            "The package query input ended without a completion event.");
    }

    static bool IsSourceWideSearchFailure(PackageQueryFailure failure) =>
        failure.Kind == PackageQueryFailureKind.Search
        || failure is
        {
            Kind: PackageQueryFailureKind.SearchContract,
            PackageId: null,
            Version: null,
        };

    static bool TryMatchManifest(
        PackageQueryPlan plan,
        PackageQueryPackage match,
        out ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        evidence = ImmutableArray.CreateBuilder<PackageQueryEvidence>(
            plan.Definitions.Length + 1);
        AddScopeEvidence(plan, evidence);
        var handledGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (PackageQueryFacetDefinition definition in plan.Definitions)
        {
            string? groupId = definition.Descriptor.SelectionGroupId;
            if (groupId is not null && !handledGroups.Add(groupId))
                continue;

            PackageQueryFacetDefinition[] alternatives = groupId is null
                ? [definition]
                :
                [
                    .. plan.Definitions.Where(candidate =>
                        candidate.Descriptor.SelectionGroupId == groupId),
                ];
            PackageQueryFacetDefinition[] matched =
            [
                .. alternatives.Where(candidate =>
                    candidate.MatchesManifest(match)),
            ];
            if (matched.Length == 0)
            {
                evidence.Clear();
                return false;
            }

            foreach (PackageQueryFacetDefinition candidate in matched)
            {
                if (candidate.Descriptor.Tier
                    != PackageQueryFacetTier.Nuspec)
                {
                    continue;
                }
                evidence.Add(CreateFacetEvidence(candidate, match, null));
            }
        }

        return true;
    }

    static bool TryMatchPackageContent(
        PackageQueryPlan plan,
        PackageQueryPackage match,
        PackageContentFacts content,
        ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        var handledGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (PackageQueryFacetDefinition definition in plan.Definitions)
        {
            if (definition.Descriptor.Tier
                    != PackageQueryFacetTier.PackageContent)
            {
                continue;
            }

            string? groupId = definition.Descriptor.SelectionGroupId;
            if (groupId is not null && !handledGroups.Add(groupId))
                continue;

            PackageQueryFacetDefinition[] alternatives = groupId is null
                ? [definition]
                :
                [
                    .. plan.Definitions.Where(candidate =>
                        candidate.Descriptor.Tier
                            == PackageQueryFacetTier.PackageContent
                        && candidate.Descriptor.SelectionGroupId == groupId),
                ];
            PackageQueryFacetDefinition[] matched =
            [
                .. alternatives.Where(candidate =>
                    candidate.MatchesPackageContent is not null
                    && candidate.MatchesPackageContent(content)),
            ];
            if (matched.Length == 0)
            {
                return false;
            }

            foreach (PackageQueryFacetDefinition candidate in matched)
            {
                evidence.Add(CreateFacetEvidence(candidate, match, content));
            }
        }

        return true;
    }

    static async ValueTask<PackageContentFacts> ReadPackageContentFactsAsync(
        IPackageContent content,
        ImmutableArray<PackageQueryFacetDefinition> definitions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] entries = [.. content.EnumerateEntries()];
        bool needsSkills = definitions.Any(definition =>
            definition.Descriptor.Id == EmbeddedSkillFacetId);
        bool needsToolSettings = definitions.Any(definition =>
            definition.Descriptor.Id is ToolV1FacetId or ToolV2FacetId);
        PackageQueryEvidenceSummary? skills = needsSkills
            ? SummarizeItems(entries.Where(IsSkillDocument), StringComparer.Ordinal)
            : null;
        string? toolVersion = needsToolSettings
            ? await ReadToolSettingsVersionAsync(
                content,
                entries,
                cancellationToken).ConfigureAwait(false)
            : null;
        return new PackageContentFacts(skills, toolVersion);
    }

    static async ValueTask<string?> ReadToolSettingsVersionAsync(
        IPackageContent content,
        IEnumerable<string> entries,
        CancellationToken cancellationToken)
    {
        string[] settingsPaths =
        [
            .. entries
                .Where(IsToolSettings)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        string? packageVersion = null;
        foreach (string path in settingsPaths)
        {
            if (!content.TryOpenEntry(
                path,
                MaximumToolSettingsBytes,
                out Stream? stream))
            {
                throw new IOException(
                    "The selected tool settings entry is unavailable.");
            }

            await using (stream.ConfigureAwait(false))
            {
                byte[] bytes = await BoundedContentReader.ReadAllBytesAsync(
                    stream,
                    MaximumToolSettingsBytes,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                string xml = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true).GetString(bytes);
                if (xml.Length > 0 && xml[0] == '\uFEFF')
                    xml = xml[1..];
                DotnetToolSettingsData? settings =
                    DotnetToolSettingsParser.ParseContentOrThrow(xml);
                if (settings is null)
                    return null;
                string version = settings.Version ?? "1";
                if (packageVersion is null)
                {
                    packageVersion = version;
                }
                else if (!packageVersion.Equals(
                    version,
                    StringComparison.Ordinal))
                {
                    return null;
                }
            }
        }

        return packageVersion;
    }

    static bool IsSkillDocument(string path)
    {
        string[] segments = path.Split('/');
        return segments.Length >= 2
            && segments[0].Equals(
                "skills",
                StringComparison.OrdinalIgnoreCase)
            && segments[^1].Equals(
                "SKILL.md",
                StringComparison.OrdinalIgnoreCase);
    }

    static bool IsToolSettings(string path)
    {
        string[] segments = path.Split('/');
        return segments.Length is >= 2 and <= 4
            && segments[0].Equals(
                "tools",
                StringComparison.OrdinalIgnoreCase)
            && segments[^1].Equals(
                "DotnetToolSettings.xml",
                StringComparison.OrdinalIgnoreCase);
    }

    static PackageQueryFailure FromProfileFailure(
        PackageProfileFailure failure) =>
        new(
            failure.PackageId,
            failure.Version,
            failure.Source,
            failure.Kind switch
            {
                PackageProfileFailureKind.Search =>
                    PackageQueryFailureKind.Search,
                PackageProfileFailureKind.SearchContract =>
                    PackageQueryFailureKind.SearchContract,
                PackageProfileFailureKind.ManifestAcquisition =>
                    PackageQueryFailureKind.ManifestAcquisition,
                PackageProfileFailureKind.ManifestContract =>
                    PackageQueryFailureKind.ManifestContract,
                PackageProfileFailureKind.InvalidManifest =>
                    PackageQueryFailureKind.InvalidManifest,
                _ => throw new InvalidOperationException(
                    "Unknown package-profile failure kind."),
            },
            failure.Message,
            failure.ManifestFailureReason);

    static bool HasDependencies(PackageQueryPackage match) =>
        match.RequiredManifest.DependencyGroups.Any(group =>
            !group.Dependencies.IsEmpty);

    static PackageQueryFacetEvidence DescribeDependencies(PackageQueryPackage match) =>
        DescribeItems(
            SummarizeItems(
                match.RequiredManifest.DependencyGroups
                    .SelectMany(group => group.Dependencies)
                    .Select(dependency => dependency.Id),
                StringComparer.OrdinalIgnoreCase),
            "dependency",
            "dependencies");

    static PackageQueryEvidenceSummary SummarizeItems(
        IEnumerable<string> items,
        StringComparer comparer)
    {
        string[] distinct = [.. items.Distinct(comparer).Order(comparer)];
        return new PackageQueryEvidenceSummary(
            distinct.Length,
            [
                .. distinct.Take(MaximumEvidencePreviewItems)
                    .Select(item => new InertString(
                        TextPolicy.Field, item, MaximumEvidencePreviewCharacters)),
            ]);
    }

    static PackageQueryFacetEvidence DescribeItems(
        PackageQueryEvidenceSummary summary,
        string singular,
        string plural)
    {
        string heading = $"{summary.Count.ToString(CultureInfo.InvariantCulture)} "
            + Pluralize(summary.Count, singular, plural);
        if (summary.Preview.IsEmpty)
            return new PackageQueryFacetEvidence(Evidence(heading + "."), summary);

        InertString preview = InertString.Join(", ", TextPolicy.Prose, [.. summary.Preview]);
        int remaining = summary.Count - summary.Preview.Length;
        InertString text = remaining > 0
            ? InertString.Format(TextPolicy.Prose,
                $"{heading}: {preview} (+{remaining.ToString(CultureInfo.InvariantCulture)} more).")
            : InertString.Format(TextPolicy.Prose, $"{heading}: {preview}.");
        return new PackageQueryFacetEvidence(text, summary);
    }

    static PackageQueryFacetEvidence Describe(string text) => new(Evidence(text));

    static PackageQueryEvidence CreateFacetEvidence(
        PackageQueryFacetDefinition definition,
        PackageQueryPackage package,
        PackageContentFacts? content)
    {
        PackageQueryFacetEvidence description = definition.Evidence(package, content);
        return new PackageQueryEvidence(definition.Descriptor.Id, description.Text)
        {
            Summary = description.Summary,
        };
    }

    static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? singular : plural;

    static InertString Evidence(string value) =>
        new(TextPolicy.Prose, value);

    static PackageQueryEvent.Progress Progress(
        PackageQueryProgressPhase phase,
        int completed,
        int limit) =>
        new(new PackageQueryProgress(phase, completed, limit));

    static PackageQueryEvent.Completed Completed(
        PackageQueryPlan plan,
        PackageSourceResultIdentity source,
        int candidates,
        int matches,
        int failures,
        PackageQueryCompletionKind completion,
        int? sourceCandidates = null,
        long? estimatedTotalHits = null) =>
        new(
            new PackageQuerySummary(
                plan.Prefix,
                source,
                plan.MaximumCandidates,
                plan.MaximumMatches,
                candidates,
                matches,
                failures,
                completion)
            {
                SourceCandidates = sourceCandidates,
                EstimatedTotalHits = estimatedTotalHits,
            });

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
