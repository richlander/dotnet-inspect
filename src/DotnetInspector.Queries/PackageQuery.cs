using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The production envelope in which a package-query facet is available.</summary>
public enum PackageQueryFacetTier
{
    Nuspec,
    PackageContent,
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
    string? DisplayGroupLabel = null);

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
    PackageSourceIdentity Producer,
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
        PackageProfileMatch package,
        CancellationToken cancellationToken);
}

internal sealed record PackageQueryFacetDefinition(
    PackageQueryFacetDescriptor Descriptor,
    Func<PackageProfileMatch, bool> MatchesManifest,
    Func<PackageProfileMatch, InertString> Evidence,
    Func<PackageContentFacts, bool>? MatchesPackageContent = null);

internal sealed record PackageContentFacts(
    bool HasSkillDocument,
    string? ToolSettingsVersion);

/// <summary>
/// Plans and executes product-owned manifest and package-content facets over a
/// bounded package profile without loading inspected assemblies.
/// </summary>
public static class PackageQuery
{
    public const int DefaultMaximumCandidates = 200;
    public const int DefaultMaximumMatches = 100;
    public const int MaximumPackageContentCandidates = 20;
    public const int MaximumFacetIdLength = 100;
    public const int MaximumToolSettingsBytes = 64 * 1024;

    public const string PrefixEvidenceId = "package.query.scope.prefix";
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
            static match => match.Verified,
            static _ => Evidence(
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
            static match => match.Manifest.IsToolPackage,
            static _ => Evidence(
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
                ".NET tool format"),
            static match => match.Manifest.IsToolPackage,
            static _ => Evidence(
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
                ".NET tool format"),
            static match => match.Manifest.IsToolPackage,
            static _ => Evidence(
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
        new(
            new PackageQueryFacetDescriptor(
                EmbeddedSkillFacetId,
                "embedded SKILL.md",
                "Downloads the package and matches a skills/SKILL.md or skills/**/SKILL.md file.",
                700,
                PackageQueryFacetTier.PackageContent),
            static _ => true,
            static _ => Evidence(
                "The package contains a skills/SKILL.md document."),
            static content => content.HasSkillDocument),
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
        if (request.MaximumCandidates > MaximumPackageContentCandidates
            && selected.Any(definition =>
                definition.Descriptor.Tier
                    == PackageQueryFacetTier.PackageContent))
        {
            return Rejected(
                PackageQueryRequestFailureReason
                    .PackageContentCandidateLimitExceeded,
                value: request.MaximumCandidates);
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
                    if (!TryMatchManifest(
                        plan,
                        match.Value,
                        out ImmutableArray<PackageQueryEvidence>.Builder
                            evidence))
                        continue;

                    if (requiresPackageContent)
                    {
                        PackageQueryContentResult contentResult =
                            await contentProvider!.GetContentAsync(
                                match.Value,
                                cancellationToken).ConfigureAwait(false);
                        if (contentResult
                            is PackageQueryContentResult.Unavailable unavailable)
                        {
                            failures++;
                            yield return new PackageQueryEvent.Failure(
                                new PackageQueryFailure(
                                    match.Value.PackageId,
                                    match.Value.Version,
                                    match.Value.Producer,
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
                                or UnauthorizedAccessException)
                        {
                            // Iterator catch clauses cannot yield; null is
                            // projected below as a typed item failure.
                        }
                        if (facts is null)
                        {
                            failures++;
                            yield return new PackageQueryEvent.Failure(
                                new PackageQueryFailure(
                                    match.Value.PackageId,
                                    match.Value.Version,
                                    match.Value.Producer,
                                    PackageQueryFailureKind
                                        .PackageContentEvaluation,
                                    "The package content could not be evaluated."));
                            continue;
                        }

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
                                : PackageQueryFacetTier.Nuspec,
                            evidence.MoveToImmutable()));
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

                    yield return new PackageQueryEvent.Failure(
                        FromProfileFailure(failure.Value));
                    break;

                case PackageProfileEvent.Completed completed:
                    yield return Completed(
                        plan,
                        completed.Value.Producer,
                        completed.Value.Candidates,
                        matches,
                        failures,
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

    static bool TryMatchManifest(
        PackageQueryPlan plan,
        PackageProfileMatch match,
        out ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        evidence = ImmutableArray.CreateBuilder<PackageQueryEvidence>(
            plan.Definitions.Length + 1);
        evidence.Add(
            new PackageQueryEvidence(
                PrefixEvidenceId,
                plan.PrefixEvidence));
        foreach (PackageQueryFacetDefinition definition in plan.Definitions)
        {
            if (!definition.MatchesManifest(match))
            {
                evidence.Clear();
                return false;
            }

            if (definition.Descriptor.Tier == PackageQueryFacetTier.Nuspec)
            {
                evidence.Add(
                    new PackageQueryEvidence(
                        definition.Descriptor.Id,
                        definition.Evidence(match)));
            }
        }

        return true;
    }

    static bool TryMatchPackageContent(
        PackageQueryPlan plan,
        PackageProfileMatch match,
        PackageContentFacts content,
        ImmutableArray<PackageQueryEvidence>.Builder evidence)
    {
        foreach (PackageQueryFacetDefinition definition in plan.Definitions)
        {
            if (definition.Descriptor.Tier
                    != PackageQueryFacetTier.PackageContent)
            {
                continue;
            }
            if (definition.MatchesPackageContent is null
                || !definition.MatchesPackageContent(content))
            {
                return false;
            }

            evidence.Add(
                new PackageQueryEvidence(
                    definition.Descriptor.Id,
                    definition.Evidence(match)));
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
        bool hasSkill = needsSkills && entries.Any(IsSkillDocument);
        string? toolVersion = needsToolSettings
            ? await ReadToolSettingsVersionAsync(
                content,
                entries,
                cancellationToken).ConfigureAwait(false)
            : null;
        return new PackageContentFacts(hasSkill, toolVersion);
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
                DotnetToolSettingsData? settings =
                    DotnetToolSettingsParser.ParseContent(xml);
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
            failure.Producer,
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
