using DotnetInspector.Models;
using DotnetInspector.Core;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Planning;
using DotnetInspector.Queries;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using InertText;
using ILInspector.Findings;
using Markout;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a NuGet package.
/// </summary>
public class PackageCommand
{
    public const string Name = "package";
    public static Task<int> ExecuteAsync(InspectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExecuteAsync(
            options,
            new CommandContext(options.Verbose));
    }

    internal static async Task<int> ExecuteAsync(
        InspectionOptions options,
        CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        var packageArgs = options.PackageArgs;
        var explicitVersion = options.ExplicitVersion;
        var catalog = PackageSectionDescriptors.CreateCatalog();
        var sectionCatalog = catalog.Sections;
        var pipeline = catalog.Pipeline;
        var queryCatalog = catalog.QueryCatalog;
        var sectionNames = sectionCatalog.SelectableSectionNames;
        bool packageLibraryMode = options.PackageLibrary != null || options.AllLibraries;
        if (!packageLibraryMode)
            options = NormalizeDependencyProjection(options);

        if (packageArgs.Length > 1
            && !ValidateMultiPackageMode(options))
        {
            return 1;
        }

        if (packageLibraryMode
            && options.Discover is not null
            && options.Schema)
        {
            StructuralRoute route = options.AllLibraries
                ? StructuralViewRegistry.Route(
                    StructuralViewIdentity.PackageAllLibraries,
                    InspectionCatalogIdentity.LibraryAggregate)
                : StructuralViewRegistry.Route(
                    StructuralViewIdentity.PackageSingleLibrary,
                    InspectionCatalogIdentity.Library);
            StructuralOutputShape shape =
                options.AllLibraries
                && options.TabularExplicitlySet
                && !options.Count
                    ? StructuralOutputShape.Rows
                    : StructuralOutputShape.Document;
            return StructuralViewRegistry.Execute(
                route,
                StructuralDiscoveryRequest.From(options),
                shape);
        }

        if (!packageLibraryMode
            && options.Discover is not null
            && options.Schema)
        {
            return StructuralViewRegistry.Execute(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.Package,
                    InspectionCatalogIdentity.Package),
                StructuralDiscoveryRequest.From(options));
        }

        // @Hidden is a discovery-only pole. For the embedded-library render modes (which resolve
        // -S against the curated LibrarySections pipeline), reject it up front — before extracting
        // or fetching the package — so an invalid render selector never pays acquisition cost and
        // can never fan out to unbounded @Hidden members as a group.
        // Static discovery mode: -D --schema lists schema without resolving/loading the package.
        // Also keep no-target package discovery static because there is no target to make effective.
        if (!packageLibraryMode
            && options.Discover != null
            && packageArgs.Length < 1)
        {
            var schemaMap = PackageDiscoverySchema();
            if (options.Schema)
            {
                var selectedSections = options.IncludeSections is { Count: > 0 }
                    ? new HashSet<string>(
                        options.IncludeSections,
                        StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hasExplicitSelection = options.Select is { Length: > 0 };
                if (hasExplicitSelection || options.SelectDefault)
                {
                    var selectResult = SelectResolver.ResolveSelectAsSections(
                        options.Select,
                        sectionNames,
                        sectionCatalog.BareSelectSectionNames,
                        sectionCatalog.SelectionCategoryMap,
                        selectDefault: options.SelectDefault
                            && !hasExplicitSelection);
                    if (SelectOutput.WriteUnresolved(selectResult))
                        return 1;
                    if (selectResult.Sections is { Count: > 0 })
                        selectedSections.UnionWith(selectResult.Sections);
                }

                if (selectedSections.Count > 0)
                    schemaMap = FilterDiscoverySchema(schemaMap, selectedSections);
            }

            return DiscoverOutput.Execute(options.Discover, schemaMap,
                tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
                verbosity: (int)options.Verbosity,
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: sectionCatalog.SelectionCategoryMap,
                // --schema reveals the full catalog including the @Hidden pole; a static -D
                // without --schema keeps the curated top-level view.
                catalogHiddenSections: options.Schema ? null : pipeline.GetCatalogHiddenSections(),
                listedCategoryDoors: pipeline.GetListedCategoryDoors(),
                projection: options);
        }

        // Bare -S selects the network-free "fixed" overview: only sections whose declared growth
        // class is Fixed and whose cost is NetworkFree, so the rendered set is structurally
        // identical for every package (absence means "not applicable", never "too long for this
        // package"). Consume the marker so it never resolves as a section set; keep display
        // verbosity at Normal, and never downgrade a higher verbosity the user asked for - there
        // the normal curated ladder applies instead. Combined with an explicit selector (or with
        // sugar such as --path that synthesizes one) the explicit selection wins and the marker is
        // simply dropped, which is what it has always done - it used to emit a spurious
        // "@Default not found" warning on the way. See #3547.
        if (!packageLibraryMode && options.Discover == null && options.SelectDefault)
        {
            options = options with { SelectDefault = false };
            if (options.Select is null && options.Verbosity == Verbosity.Minimal)
                options = options with { Verbosity = Verbosity.Normal, FixedOverview = true };
        }

        // -D defaults to effective discovery for target-based commands.
        bool effectiveDiscovery = !packageLibraryMode && options.Discover != null && !options.Schema;
        var userVerbosity = options.Verbosity; // preserve for display formatting
        if (effectiveDiscovery)
            options = options with { Verbosity = Verbosity.Detailed };

        if (!packageLibraryMode)
        {
            // -S/--select with values: resolve as section filter for backpressure
            var selectResult = SelectResolver.ResolveSelectAsSections(
                options.Select,
                sectionNames,
                sectionCatalog.InfoSectionNames,
                sectionCatalog.SelectionCategoryMap,
                selectDefault: options.SelectDefault);
            if (SelectOutput.WriteUnresolved(selectResult)) return 1;
            if (selectResult.Sections != null)
                options = options with { IncludeSections = selectResult.Sections };

            // The alternate lens modes render their own payload and never consult the section
            // filter, so requiring -S here would force the caller to name a section that is then
            // ignored. LensProjection answers the projection for those modes instead, and -S is
            // rejected outright below rather than silently dropped.
            var lensMode = options.ListVersions || options.ListLayout || options.ListTfms
                || options.ShowContent;
            var dependencyTreeProjection = options.Tree
                && options.Discover == null
                && options.IncludeSections is { Count: 1 }
                && options.IncludeSections.Contains(PackageSections.Dependencies);
            // Discovery also renders its own payload, so it is exempt from the single-section
            // requirement below. It is deliberately not part of lensMode: unlike the lenses, -S
            // is meaningful with -D, which restricts discovery to the selected sections.
            var rendersOwnPayload = lensMode || options.Discover != null;
            // Gate on what the caller actually typed: --path and --type synthesize a selection,
            // and rejecting that would break the lens modes' normal use. The refusal is
            // unconditional rather than excusing --print: the lens prints its own document
            // without a selection, so accepting -S there would silently ignore it.
            if (lensMode
                && (options.SelectExplicitlySet
                    || options.ShowDependencies
                    || dependencyTreeProjection))
            {
                var lensName = options.ListVersions ? "--versions"
                    : options.ListLayout ? "--layout"
                    : options.ListTfms ? "--tfms"
                    : "--content";
                if (options.ShowDependencies)
                    CommandError.Write($"--dependencies cannot be combined with {lensName}.");
                else if (dependencyTreeProjection && !options.SelectExplicitlySet)
                    CommandError.Write($"--tree cannot be combined with {lensName}.");
                else
                    CommandError.Write(
                        $"-S/--select is not available with {lensName}, which renders its own payload rather than sections.");
                return 1;
            }

            string? packageLens = options.ListVersions
                ? options.ForceLatest
                    ? "--latest-version"
                    : options.ListVersionsWithFeed
                        ? "--versions-with-feed"
                        : "--versions"
                : options.ListLayout
                    ? "--layout"
                    : options.ListTfms
                        ? "--tfms"
                        : options.ShowContent
                            ? "--content"
                            : null;

            // Opaque lens payload projections are target-independent failures. Reject them
            // before version lookup, package resolution, or extraction; --count needs the rows.
            if (packageLens is not null
                && !options.Count
                && LensProjection.TryProject(
                    options,
                    packageLens,
                    rowCount: 0,
                    out var lensProjectionExit))
            {
                return lensProjectionExit;
            }

            if (packageLens is not null
                && !options.Count
                && (options.Fields is { Length: > 0 }
                    || options.Columns is { Length: > 0 }))
            {
                CommandError.Write(
                    $"--fields/--columns are not available with {packageLens}, which "
                    + "renders its own payload. Omit the projection to keep the lens output.");
                return 1;
            }

            if (!ValidateDependencyTreeProjection(options))
                return 1;

            // #3448 aligns the package gate with the library one: a count over several selected
            // sections is meaningful now that the file family is disjoint, so require a selection
            // rather than exactly one section.
            if (!rendersOwnPayload && options.Count)
            {
                if (!CountOutput.ValidateSectionsSelected(
                        options.IncludeSections, options.FixedOverview))
                {
                    return 1;
                }

                var ordered = OutputFormatter.ResolveCountMapSections(
                    pipeline, options.IncludeSections, options.FixedOverview);
                if (!CountOutput.ValidateMapFormat(
                        options.Format, ordered, options.Tree))
                    return 1;
            }

            var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
            if (shapeCount > 1)
            {
                CommandError.Write("specify only one of --value, --urls, or --paths.");
                return 1;
            }

            if (shapeCount == 1)
            {
                var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
                // In a lens mode the shape projection is refused by LensProjection with an
                // accurate reason; demanding -S first would report a section requirement that is
                // not the actual problem.
                if (!rendersOwnPayload && !ShapeProjectionOutput.ValidateSingleSection(options.IncludeSections, optionName))
                    return 1;
                if (options.Count || options.Print)
                {
                    CommandError.Write($"{optionName} cannot be combined with --count or --print.");
                    return 1;
                }
                if (options.Rows is not null)
                {
                    CommandError.Write($"--rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N|first|last to select a projected row.");
                    return 1;
                }
            }

            if (options.JsonArray && shapeCount == 0 && !options.Print)
            {
                CommandError.Write("--json-array requires --value, --urls, --paths, or --print.");
                return 1;
            }

            if (options.JsonArray && (options.JsonOutput || options.Jsonl))
            {
                CommandError.Write("--json-array cannot be combined with --json or --jsonl.");
                return 1;
            }

            // A lens renders its own payload, so demanding a printable section selection reports a
            // requirement the lens does not have. The readme lens prints its own document, and
            // the rest refuse --print through LensProjection with an accurate reason.
            if (options.Print && !rendersOwnPayload && !ValidatePackagePrintSelection(options.IncludeSections))
                return 1;

            IReadOnlyCollection<string>? tabularSections =
                options.FixedOverview
                    ? sectionCatalog.BareSelectSectionNames
                    : options.IncludeSections;
            if (!options.Count
                && !OutputFormatResolver.ValidateSingleSectionForTabular(
                    options.TabularExplicitlySet,
                    tabularSections))
                return 1;

            // Auto-promote verbosity when -S targets specific sections
            if (options.IncludeSections is { Count: > 0 })
            {
                var requiredVerbosity = pipeline.GetRequiredVerbosity(options.IncludeSections);
                if (requiredVerbosity > options.Verbosity)
                    options = options with { Verbosity = requiredVerbosity };
            }

            if (!ValidatePackageProjection(
                    options,
                    packageArgs.Length,
                    pipeline))
                return 1;
        }

        if (packageArgs.Length < 1)
        {
            CommandError.Write("Package name or path required.");
            CommandError.WriteLine("Run 'dotnet-inspect package --help' for usage.");
            return 1;
        }

        if (!ValidatePathMatchMode(options))
            return 1;
        if (!ValidatePackageContentMode(options))
            return 1;

        if (options.AllLibraries && !ValidatePackageAllLibrariesMode(options))
            return 1;
        if (options.PackageLibrary != null && !ValidatePackageLibraryMode(options))
            return 1;

        InspectionOptions producerOptions = CreateProducerOptions(
            options,
            userVerbosity,
            pipeline);
        var logger = context.Logger;

        if (packageArgs.Length > 1)
        {
            PackageSourceQueryPlan sourceQueryPlan = CreatePackageSourceQueryPlan(
                sectionCatalog,
                queryCatalog,
                producerOptions,
                excludeUnbounded:
                    options.Discover is not null && !options.Schema);
            return await ExecuteMultiPackageAsync(
                packageArgs,
                options,
                producerOptions,
                context,
                sectionCatalog,
                sourceQueryPlan);
        }

        // Handle --versions mode: list versions and exit early
        if (options.ListVersions)
        {
            using var failureScope = FeedFailureTelemetry.Scope();
            PackageVersionRange? range = null;
            string? rangeError = null;
            bool isRange = !File.Exists(packageArgs[0])
                && PackageVersionRange.TryParse(packageArgs[0], out range, out rangeError);
            if (rangeError is not null)
            {
                CommandError.Write(rangeError);
                return 1;
            }

            if (isRange)
            {
                try
                {
                    if (options.IncludeUnlisted)
                    {
                        // Listing-aware range: resolve the vector from the full listing (unlisted
                        // included) so unlisted endpoints are found rather than reported as missing,
                        // then emit each in-range version tagged with its listed status.
                        // Mirror ResolveAsync: fetch prereleases whenever the range endpoints are
                        // prerelease (even without --preview), otherwise a prerelease-endpoint range
                        // fails because its endpoints were filtered out of the listing.
                        var rangeListings = await PackageExtractor.GetVersionListingsAsync(
                            context.HttpClient, range!.PackageId,
                            range!.IncludesPrerelease || options.IncludePrerelease,
                            includeUnlisted: true, limit: null, logger.Log, options.SourceOptions);
                        if (rangeListings == null)
                        {
                            WriteVersionLookupFailure(
                                range.PackageId,
                                $"Package '{range.PackageId}' not found on eligible configured sources.");
                            return 1;
                        }

                        var unlistedVector = PackageVersionVector.CreateListingAware(
                            range!, rangeListings, options.IncludePrerelease);
                        // Materialized once: counting a lazy sequence and then re-enumerating it
                        // for the render is how a count starts to disagree with its payload.
                        var rangeRows = unlistedVector.Take(options.Limit ?? int.MaxValue).ToList();
                        var visibleRangeRows = RowWindow.Apply(options.Rows, rangeRows);
                        if (LensProjection.TryProject(
                                options,
                                "--versions",
                                visibleRangeRows.Count,
                                out var rangeListingExit,
                                ["Version", "Listing"]))
                            return rangeListingExit;
                        OutputFormatter.WriteVersionListings(
                            visibleRangeRows,
                            options,
                            Console.Out);
                        return 0;
                    }

                    var vector = await PackageVersionVector.ResolveAsync(
                        context.HttpClient,
                        range!,
                        options.SourceOptions,
                        logger.Log,
                        options.IncludePrerelease);
                    var rangeVersions = vector.Addresses
                        .Take(options.Limit ?? int.MaxValue)
                        .Select(address => address.Version.ToNormalizedString())
                        .ToList();
                    var visibleRangeVersions = RowWindow.Apply(options.Rows, rangeVersions);
                    if (LensProjection.TryProject(
                            options,
                            "--versions",
                            visibleRangeVersions.Count,
                            out var rangeProjectionExit,
                            ["Version"]))
                        return rangeProjectionExit;
                    OutputFormatter.WriteStringList(visibleRangeVersions, "Version", "Version", options.Tsv, options.Jsonl, Console.Out);
                    return 0;
                }
                catch (Exception ex) when (ex is HttpRequestException
                    or IOException
                    or InvalidOperationException
                    or ArgumentException)
                {
                    WriteVersionLookupFailure(
                        range!.PackageId,
                        ex is PackageVersionsUnavailableException
                            { HasIncompleteMetadata: false }
                            ? $"Package '{range.PackageId}' not found."
                            : ex.Message);
                    return 1;
                }
            }

            var (versionQueryName, versionQueryPinned) = PackageExtractor.ParsePackageReference(packageArgs[0]);
            string normalizedName = versionQueryName.ToLowerInvariant();
            if (string.Equals(versionQueryPinned, "latest", StringComparison.OrdinalIgnoreCase))
            {
                versionQueryPinned = null;
                options = options with { ForceLatest = true };
            }
            using var requestScope = RequestTelemetry.Scope($"package {normalizedName}", "package versions");

            if (!string.IsNullOrEmpty(versionQueryPinned)
                && options.Limit == 1
                && !options.ForceLatest)
            {
                if (!options.IncludeUnlisted
                    && NuGetCache.TryGetCachedPackage(
                        normalizedName,
                        versionQueryPinned,
                        NuGetSourceResolver.ResolveSourceKeysForPackage(
                            options.SourceOptions,
                            normalizedName)) != null)
                {
                    var visiblePinned = RowWindow.Apply(options.Rows, new[] { versionQueryPinned });
                    if (LensProjection.TryProject(
                            options,
                            "--versions",
                            visiblePinned.Count,
                            out var cachedPinnedExit,
                            ["Version"]))
                        return cachedPinnedExit;
                    WriteVersions(visiblePinned, options);
                    return 0;
                }

                // Include unlisted versions here: a pinned version query verifies a specific,
                // explicitly named version, and an unlisted version is still a valid coordinate
                // (NuGet restores known unlisted versions). Discovery hiding must not make an
                // explicitly requested unlisted version look "not found".
                var knownVersions = await PackageExtractor.GetVersionListingsAsync(
                    context.HttpClient,
                    normalizedName,
                    includePrerelease: true,
                    includeUnlisted: true,
                    limit: null,
                    log: logger.Log,
                    sourceOptions: options.SourceOptions);

                var pinnedMatch = knownVersions?.FirstOrDefault(
                    v => string.Equals(v.Version, versionQueryPinned, StringComparison.OrdinalIgnoreCase));
                if (pinnedMatch != null)
                {
                    // Either spelling renders a single version row, so the projection answers 1
                    // and returns before the render path chooses between them.
                    var visiblePinned = RowWindow.Apply(options.Rows, new[] { pinnedMatch });
                    if (LensProjection.TryProject(
                            options,
                            "--versions",
                            visiblePinned.Count,
                            out var knownPinnedExit,
                            VersionListingColumns(options)))
                        return knownPinnedExit;
                    if (options.IncludeUnlisted)
                        OutputFormatter.WriteVersionListings(
                            visiblePinned,
                            options,
                            Console.Out);
                    else
                        WriteVersions(
                            visiblePinned.Select(row => row.Version).ToArray(),
                            options);
                    return 0;
                }

                if (FeedFailureTelemetry.Current?.Failures.Any(
                        failure => failure.Phase is
                            NetworkTrafficKind.PackageSourceDiscovery
                            or NetworkTrafficKind.PackageVersionList) == true)
                    WriteVersionLookupFailure(
                        normalizedName,
                        $"Version '{versionQueryPinned}' of package '{normalizedName}' not found.");
                else if (knownVersions == null || knownVersions.Count == 0)
                    WriteVersionLookupFailure(
                        normalizedName,
                        $"Package '{normalizedName}' not found.");
                else
                    CommandError.Write($"Version '{versionQueryPinned}' of package '{normalizedName}' not found. Use --versions to see available versions.");
                return 1;
            }

            if (options.Limit == 1 && options.ForceLatest)
            {
                var sources = NuGetSourceResolver.ResolveSourcesForPackage(
                    options.SourceOptions,
                    normalizedName);
                var latest = await PackageExtractor.GetLatestVersionAsync(
                    context.HttpClient,
                    normalizedName,
                    sources,
                    logger.Log,
                    skipCache: true,
                    includePrerelease: options.IncludePrerelease);
                if (latest == null)
                {
                    WriteVersionLookupFailure(
                        normalizedName,
                        $"Package '{packageArgs[0]}' not found on eligible configured sources.");
                    return 1;
                }

                // A single resolved version is a one-row payload, so --count reports 1.
                var visibleLatest = RowWindow.Apply(options.Rows, new[] { latest });
                if (LensProjection.TryProject(
                        options,
                        "--latest-version",
                        visibleLatest.Count,
                        out var latestProjectionExit,
                        VersionListingColumns(options)))
                    return latestProjectionExit;
                if (options.IncludeUnlisted)
                {
                    // Latest resolution is listing-aware (#3388), so the version it returns is
                    // listed by construction. Emit it as a one-row listing so the flag still
                    // produces the tagged column the user asked for.
                    OutputFormatter.WriteVersionListings(
                        visibleLatest
                            .Select(version => new PackageVersionInfo(version, Listed: true))
                            .ToArray(),
                        options,
                        Console.Out);
                    return 0;
                }

                WriteVersions(visibleLatest, options);
                return 0;
            }

            if (versionQueryPinned is null
                && options.Limit == 1
                && !options.IncludeUnlisted
                && !options.ListVersionsWithFeed)
            {
                List<string>? singleVersions =
                    await PackageExtractor.GetSingleVersionListingAsync(
                    context.HttpClient,
                    normalizedName,
                    options.IncludePrerelease,
                    logger.Log,
                    options.SourceOptions);
                if (singleVersions is null)
                {
                    WriteVersionLookupFailure(
                        normalizedName,
                        $"Package '{packageArgs[0]}' not found on eligible configured sources.");
                    return 1;
                }

                var visibleSingleVersions = RowWindow.Apply(options.Rows, singleVersions);
                if (LensProjection.TryProject(
                        options,
                        "--versions",
                        visibleSingleVersions.Count,
                        out var cachedLatestExit,
                        ["Version"]))
                {
                    return cachedLatestExit;
                }

                OutputFormatter.WriteStringList(
                    visibleSingleVersions,
                    "Version",
                    "Version",
                    options.Tsv,
                    options.Jsonl,
                    Console.Out);
                return 0;
            }

            if (options.ListVersionsWithFeed)
            {
                var versionFeeds = await PackageExtractor.GetVersionListingsWithSourceAsync(
                    context.HttpClient, normalizedName, options.IncludePrerelease,
                    options.IncludeUnlisted, options.Limit, logger.Log, options.SourceOptions);
                if (versionFeeds == null)
                {
                    WriteVersionLookupFailure(
                        normalizedName,
                        $"Package '{packageArgs[0]}' not found.");
                    return 1;
                }

                var visibleVersionFeeds = RowWindow.Apply(options.Rows, versionFeeds);
                if (LensProjection.TryProject(
                        options,
                        "--versions-with-feed",
                        visibleVersionFeeds.Count,
                        out var feedExit,
                        VersionFeedColumns(visibleVersionFeeds, options)))
                    return feedExit;
                OutputFormatter.WriteVersionFeedTable(visibleVersionFeeds, options, Console.Out);
                return 0;
            }

            if (options.IncludeUnlisted)
            {
                var listings = await PackageExtractor.GetVersionListingsAsync(
                    context.HttpClient, normalizedName, options.IncludePrerelease,
                    includeUnlisted: true, options.Limit, logger.Log, options.SourceOptions);
                if (listings == null)
                {
                    WriteVersionLookupFailure(
                        normalizedName,
                        $"Package '{packageArgs[0]}' not found on eligible configured sources.");
                    return 1;
                }

                var visibleListings = RowWindow.Apply(options.Rows, listings);
                if (LensProjection.TryProject(
                        options,
                        "--versions",
                        visibleListings.Count,
                        out var listingExit,
                        ["Version", "Listing"]))
                    return listingExit;
                OutputFormatter.WriteVersionListings(
                    visibleListings,
                    options,
                    Console.Out);
                return 0;
            }

            var versions = await PackageExtractor.GetVersionsAsync(context.HttpClient, normalizedName, options.IncludePrerelease, options.Limit, logger.Log, options.SourceOptions);
            if (versions == null)
            {
                WriteVersionLookupFailure(
                    normalizedName,
                    $"Package '{packageArgs[0]}' not found on eligible configured sources.");
                return 1;
            }

            var visibleVersions = RowWindow.Apply(options.Rows, versions);
            if (LensProjection.TryProject(
                    options,
                    "--versions",
                    visibleVersions.Count,
                    out var versionsProjectionExit,
                    ["Version"]))
                return versionsProjectionExit;

            OutputFormatter.WriteStringList(visibleVersions, "Version", "Version", options.Tsv, options.Jsonl, Console.Out);

            return 0;
        }

        string? packageRangeError = null;
        if (!File.Exists(packageArgs[0])
            && PackageVersionRange.TryParse(packageArgs[0], out _, out packageRangeError))
        {
            CommandError.Write(
                $"Package range '{packageArgs[0]}' requires --versions for package inspection.");
            return 1;
        }
        if (packageRangeError is not null)
        {
            CommandError.Write(packageRangeError);
            return 1;
        }

        var client = context.HttpClient;

        var target = PackageExtractor.ParsePackageTarget(packageArgs[0], explicitVersion);
        string packageName = target.PackageName;
        string version = target.Version;
        if (target.IsLocalFile)
        {
            if (!File.Exists(target.OriginalArgument))
            {
                CommandError.Write($"File not found: {target.OriginalArgument}");
                return 1;
            }
        }
        else
        {
            if (explicitVersion != null)
                logger.Log($"Using --version: {version}");
            else if (version.Length > 0)
                logger.Log($"Using specified version: {version}");

            if (!PackageExtractor.IsValidPackageReferenceVersion(version))
            {
                string badVersion = packageArgs.Length >= 2 ? packageArgs[1] : version;
                CommandError.Write($"'{badVersion}' is not a valid package version.");
                CommandError.WriteLine("Versions look like: 1.0.0, 8.0.5, 13.0.3-beta1, 11.0.0-preview*");
                CommandError.WriteLine($"To list available versions: dotnet-inspect package {packageName} --versions");
                return 1;
            }
        }

        if (RequiresEarlyPackagePayloadPreflight(options)
            && !ProjectionDestinationWriter.ValidateBeforeAcquisition(
                PackagePayloadDestination(options)))
        {
            return 1;
        }

        using var packageRequestScope = RequestTelemetry.Scope(
            version.Length > 0 ? $"package {packageName}@{version}" : $"package {packageName}",
            "package inspect");

        if (options.Tree
            && options.Discover == null
            && !packageLibraryMode
            && (!options.Count || options.ShowDependencies))
        {
            if (options.ShowDependencies)
                CommandError.WriteLine("Tip: use 'depends --package' for dependency trees.");
            string packageReference = target.IsLocalFile
                ? target.OriginalArgument
                : version.Length > 0
                    ? $"{packageName}@{version}"
                    : packageName;
            return await ShowDependencyTreeAsync(
                client,
                packageReference,
                options,
                logger);
        }

        string? extractPath = null;
        PackageExtractionResult? resolution = null;

        try
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(
                client,
                target.IsLocalFile ? target.OriginalArgument : packageName,
                logger.Log,
                sourceOptions: options.SourceOptions,
                version: target.IsLocalFile ? null : (version.Length > 0 ? version : null),
                forceLatest: options.ForceLatest,
                includePrerelease: options.IncludePrerelease);

            if (!outcome.IsSuccess)
            {
                CommandError.Write($"{outcome.ErrorMessage}");
                return 1;
            }
            resolution = outcome.Result!;

            extractPath = resolution.ExtractPath;
            packageName = resolution.PackageName ?? packageName;
            // Update version from resolution (may have been auto-discovered)
            version = resolution.Version ?? version;

            // Handle --layout mode: show file tree and exit early
            if (options.ListLayout)
                return ListPackageLayout(extractPath, options, packageName, options.TipLevel);

            // Handle --tfms mode: list target frameworks and exit early
            if (options.ListTfms)
                return ListPackageTfms(extractPath, options);

            // Parse nuspec for full package inspection.
            var nuspec = Services.NuspecParser.FindAndParse(extractPath);

            // Handle file content modes and exit early.
            if (options.ShowContent)
            {
                var packageId = nuspec?.PackageName ?? packageName;
                var packageVersion = nuspec?.Version ?? version;
                var packageReadme = PackageFileLister.ResolvePackageReadme(extractPath, nuspec?.ReadmeFile);
                bool unaryPayload = RequiresUnaryPackageContent(options);
                PackageFileContentSet content = ReadPackageFileContents(
                    extractPath,
                    packageId,
                    packageVersion,
                    packageReadme,
                    nuspec?.ReadmeFile,
                    options,
                    suppressUnaryPayloadRead: unaryPayload);
                if (unaryPayload
                    && SelectUnaryPackageContent([content], options) is { } selectedFile)
                {
                    content = ReadPackageFileContents(
                        extractPath,
                        packageId,
                        packageVersion,
                        packageReadme,
                        nuspec?.ReadmeFile,
                        options,
                        suppressUnaryPayloadRead: true,
                        selectedFile.Path);
                }

                return PrintPackageFileContents(
                    [content],
                    options);
            }

            if (options.AllLibraries)
            {
                return await ExecutePackageAllLibrariesAsync(
                    extractPath,
                    target.IsLocalFile,
                    target.OriginalArgument,
                    packageName,
                    version,
                    target.IsLocalFile
                        ? PackageIntegrationAcquisition.Local(
                            nuspec?.PackageName,
                            nuspec?.Version)
                        : PackageIntegrationAcquisition.Remote(
                            resolution,
                            packageName,
                            version),
                    options);
            }

            if (options.PackageLibrary != null)
            {
                return await ExecutePackageLibraryAsync(
                    extractPath,
                    target.IsLocalFile,
                    target.OriginalArgument,
                    packageName,
                    version,
                    options);
            }

            long? packageSize = null;
            if (resolution.NupkgPath != null && File.Exists(resolution.NupkgPath))
            {
                packageSize = new FileInfo(resolution.NupkgPath).Length;
            }

            bool wantsSignals = RequestsSelectedOrDiscoveredSection(
                producerOptions,
                PackageSections.Signals,
                pipeline);
            bool wantsRidPackageAvailability =
                RequestsRidPackageAvailability(
                    producerOptions,
                    target.IsLocalFile,
                    pipeline);
            bool enrichesSignals =
                wantsSignals
                && options.Discover is not { Length: 0 };
            bool wantsIdentifierMetadata =
                RequiresIdentifierMetadata(
                    producerOptions,
                    pipeline,
                    includeSignals: enrichesSignals);
            bool wantsPackageMetadata =
                RequiresPackageMetadata(
                    producerOptions,
                    pipeline,
                    includeSignals: enrichesSignals);
            using var vulnerabilityTrafficScope = AllowsVulnerabilityTraffic(
                producerOptions)
                ? NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData)
                : null;

            var result = await PackageInspector.InspectAsync(
                resolution, packageName, version, target.IsLocalFile,
                target.IsLocalFile ? target.OriginalArgument : null,
                nuspec, client, logger,
                options.ForceLatest, producerOptions.Verbosity,
                fetchMetadata: wantsPackageMetadata,
                requireIdentifierMetadata: wantsIdentifierMetadata,
                verifyRidPackageAvailability: wantsRidPackageAvailability,
                sourceOptions: options.SourceOptions);

            // Apply package size (not cached in index — comes from nupkg file)
            if (packageSize.HasValue)
                result.PackageSize = packageSize;

            await PopulatePackageSignatureAsync(
                result,
                resolution.NupkgPath,
                ShouldVerifyPackageSignature(options, wantsSignals),
                logger.Log);

            result.Source = target.IsLocalFile ? SourceKind.File : SourceKind.NuGet;

            PopulatePackageFileSections(result, extractPath, options);
            if (ShouldPopulatePackageContentAudit(
                    producerOptions,
                    pipeline))
                PopulatePackageContentAudit(result, extractPath);
            PackageSourceQueryPlan sourceQueryPlan = CreatePackageSourceQueryPlan(
                sectionCatalog,
                queryCatalog,
                producerOptions,
                excludeUnbounded: effectiveDiscovery);
            if (ShouldPopulatePackageSourceFiles(producerOptions)
                || !sourceQueryPlan.SectionPlan.Queries.IsEmpty)
            {
                await PopulatePackageSourceLinkAsync(
                    result,
                    extractPath,
                    packageName,
                    version,
                    producerOptions,
                    context,
                    logger,
                    sourceQueryPlan);
            }

            // Filter output based on options
            FilterResultForOutput(result, options);

            if (wantsSignals && options.Count && !effectiveDiscovery)
            {
                await PopulatePackageSignalsAsync(
                    result, extractPath, packageName, version, client, logger, options.SourceOptions);
            }

            // Effective discovery renders the discovered rows below and answers the projection
            // against them. Counting here would count the package document instead, which is a
            // different payload than the one -D displays.
            if (options.Count && !effectiveDiscovery)
            {
                CountOutput.WriteCountResult(
                    OutputFormatter.FormatResult(result, options, pipeline),
                    options.OutputPath,
                    options.Rows);
                return PackageIntegrityExitCode(result);
            }

            if ((options.Value || options.Urls || options.Paths) && !effectiveDiscovery)
                return PackageIntegrityExitCode(
                    WritePackageShapeProjection(result, options),
                    result);

            // --print joins the other payload projections rather than short-circuiting earlier:
            // it projects the rows the selected section renders, from the same view those rows
            // come from. Discovery is excluded because it renders its own payload below and
            // refuses --print with an accurate reason.
            if (options.Print && !effectiveDiscovery)
                return PackageIntegrityExitCode(
                    WritePackagePrintProjection(result, extractPath, options),
                    result);

            if (options.Bare)
            {
                return PackageIntegrityExitCode(
                    PrintPackageBareSelection(
                        result,
                        extractPath,
                        packageName,
                        version,
                        options),
                    result);
            }

            if (enrichesSignals)
            {
                await PopulatePackageSignalsAsync(
                    result, extractPath, packageName, version, client, logger, options.SourceOptions);
            }

            // Output results
            if (effectiveDiscovery)
            {
                var effective = pipeline.GetDiscoverableSections(result, options.IncludeSections);
                var schemaMap = PackageDiscoverySchema();
                var fullSchemaMap = schemaMap;

                // Field-level filtering: detect which fields produced output
                // For bare -D, target all effective sections; for -D SectionName, target specific ones
                var discoverTargets = options.Discover is { Length: > 0 } ? options.Discover : effective.ToArray();
                {
                    var view = new InspectionResultView(result);
                    var targetSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var d in discoverTargets)
                    {
                        var resolved = schemaMap.ResolveSection(d);
                        if (resolved != null && effective.Contains(resolved))
                            targetSections.Add(resolved);
                    }
                    if (targetSections.Count > 0)
                    {
                        var writerOpts = new MarkoutWriterOptions { IncludeSections = targetSections };
                        var renderManifest = RenderManifestFormatter.Capture(
                            view,
                            InspectionContext.Default,
                            writerOpts,
                            schemaMap);
                        schemaMap = DiscoverOutput.FilterSchemaToRenderedFields(
                            effective,
                            schemaMap,
                            renderManifest,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            {
                                PackageSections.PackageInfo
                            });
                    }
                }

                return PackageIntegrityExitCode(
                    DiscoverOutput.ExecuteEffective(
                        options.Discover,
                        effective,
                        schemaMap,
                        tree: options.Tree,
                        json: options.JsonOutput,
                        tsv: options.Tsv,
                        jsonl: options.Jsonl,
                        markdown:
                            !options.Tabular
                            && !options.JsonOutput,
                        verbosity: (int)userVerbosity,
                        rootLabel: $"package {packageName}",
                        fullSchema: fullSchemaMap,
                        sectionCostAnnotations:
                            pipeline.GetCostAnnotations(),
                        sectionCategories:
                            sectionCatalog.SelectionCategoryMap,
                        catalogHiddenSections:
                            options.Schema
                                ? null
                                : pipeline
                                    .GetCatalogHiddenSections(),
                        listedCategoryDoors:
                            pipeline.GetListedCategoryDoors(),
                        projection: options),
                    result);
            }
            WarnEmptySections(result, options, pipeline);
            bool hasProjection = options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };
            var diagnosticCandidates = hasProjection
                ? GetPackageProjectionNames(options)
                : null;
            if (options.Tabular)
            {
                if (options.Jsonl && TryGetSingleFileSection(options, out var fileSection) && !hasProjection)
                {
                    WritePackageFilesJsonl(result, fileSection, options.Rows);
                    return PackageIntegrityExitCode(result);
                }

                // Multi-section check: narrow to main section or error if user explicitly selected multiple sections
                var diagnostic = OutputFormatter.CheckMultiSection(result, options, pipeline);
                if (diagnostic != null)
                {
                    // Narrow to the Package Info section for tabular output
                    options = options with { IncludeSections = new HashSet<string> { PackageSections.PackageInfo } };
                }

                if (hasProjection)
                {
                    // Capture output for projection diagnostics
                    var sw = new StringWriter { NewLine = "\n" };
                    var writerOpts = OutputFormatter.BuildWriterOptions(result, options, pipeline);
                    writerOpts.RowWindow = RowWindow.ToMarkout(options.Rows);
                    var view = new InspectionResultView(result);
                    var rendered = OutputFormatter.RenderTable(!options.NoHeader,
                        (writer, formatter) =>
                        {
                            OutputFormatter.ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                            MarkoutSerializer.Serialize(view, writer, formatter, InspectionContext.Default, writerOpts);
                        });
                    ProjectionDiagnostics.DiagnoseRendered(
                        options.Fields ?? options.Columns,
                        rendered,
                        diagnosticCandidates!);
                    Console.Out.Write(rendered);
                }
                else
                {
                    OutputFormatter.WritePackageTable(result, options, pipeline, showHeader: !options.NoHeader);
                }
            }
            else
            {
                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;

                var output = OutputFormatter.FormatResult(result, options, pipeline);
                if (hasProjection)
                    ProjectionDiagnostics.DiagnoseRendered(
                        options.Fields ?? options.Columns,
                        output,
                        diagnosticCandidates!);
                if (!string.IsNullOrEmpty(options.OutputPath))
                {
                    File.WriteAllText(options.OutputPath, output);
                }
                else
                {
                    Console.WriteLine(output);
                }
            }

            return PackageIntegrityExitCode(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            CommandError.Write($"Package '{packageName}' version '{version}' not found on eligible configured sources.");
            CommandError.WriteLine("Use 'dotnet-inspect package <name> --versions' to list available versions.");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            CommandError.WriteLine($"Failed to download package: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex) when (
            IsUnmatchedColumnProjection(options, ex))
        {
            CommandError.Write(ex.Message);
            return 1;
        }
        finally
        {
            // Only clean up temp directory if we created one (not using cache)
            if (resolution is { FromCache: false, TempDir: not null } && Directory.Exists(resolution.TempDir))
            {
                try
                {
                    Directory.Delete(resolution.TempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static void WriteVersions(
        IEnumerable<string> versions,
        InspectionOptions options)
        => OutputFormatter.WriteStringList(
            versions,
            "Version",
            "Version",
            options.Tsv,
            options.Jsonl,
            Console.Out);

    private static void WriteVersionLookupFailure(
        string packageName,
        string notFoundMessage)
    {
        var sourceFailure =
            FeedFailureTelemetry.Current?.DescribeFailure(packageName);
        CommandError.Write(sourceFailure?.ToString() ?? notFoundMessage);
    }

    private static async Task<int> ExecuteMultiPackageAsync(
        string[] packageArgs,
        InspectionOptions options,
        InspectionOptions producerOptions,
        CommandContext context,
        SectionCatalog<InspectionResult> sectionCatalog,
        PackageSourceQueryPlan sourceQueryPlan)
    {
        SectionPipeline<InspectionResult> pipeline = sectionCatalog.Pipeline;
        if (options.ShowContent)
            return await ExecuteMultiPackageContentAsync(packageArgs, options, context);

        string? rowSection = null;
        if (!options.Count
            && !TryResolveMultiPackageRowSection(options, out rowSection))
            return 1;
        var countSections = options.Count
            ? ResolveMultiPackageCountSections(options, pipeline)
            : null;
        bool wantsFilesSection = HasPathFilter(options)
            || IsPackageFileSection(rowSection)
            || options.IncludeSections?.Any(IsPackageFileSection) == true
            || (options.FixedOverview
                && sectionCatalog.BareSelectSectionNames.Any(IsPackageFileSection))
            || options.IncludeSections?.Contains(PackageSections.Signals) == true
            || options.IncludeSections?.Contains(PackageSections.AuditArtifactText) == true
            || options.IncludeSections?.Contains(PackageSections.AuditFindings) == true
            || options.FixedOverview
            || SelectResolver.IsActiveAllSelector(
                options.Select,
                options.IncludeSections)
            || countSections?.Any(IsPackageFileSection) == true;
        if (!options.Count && !options.JsonOutput && rowSection == null)
        {
            CommandError.Write("Multiple package output requires --json or a row format such as --table, --tsv, or --jsonl.");
            CommandError.WriteLine("For package surveys, try: dotnet-inspect package <pkg>... --path @readme --tsv");
            return 1;
        }
        if (!ValidateMultiPackagePackageInfoColumns(
                options,
                countSections,
                rowSection))
        {
            return 1;
        }

        var targets = new List<PackageReferenceTarget>();
        foreach (var packageArg in packageArgs)
        {
            if (!TryCreatePackageTarget(packageArg, out var target))
                return 1;
            targets.Add(target);
        }

        var results = new List<InspectionResult>();
        foreach (var target in targets)
        {
            using var packageRequestScope = RequestTelemetry.Scope(
                target.Version.Length > 0 ? $"package {target.PackageName}@{target.Version}" : $"package {target.PackageName}",
                "package inspect");
            var result = await InspectPackageAsync(
                target,
                options,
                producerOptions,
                context,
                wantsFilesSection,
                sectionCatalog,
                sourceQueryPlan);
            if (result == null)
                return 1;
            results.Add(result);
        }

        if (options.Count)
            return WriteMultiPackageCount(results, options, pipeline);

        if (options.JsonOutput)
        {
            if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                return 1;

            Console.WriteLine(JsonSerializer.Serialize(
                results.Select(PackageInspectionJson.Create).ToArray(),
                PackageInspectionJsonContext.Default.PackageInspectionJsonArray));
            return PackageIntegrityExitCode([.. results]);
        }

        try
        {
            WriteMultiPackageTable(results, rowSection!, options);
            return PackageIntegrityExitCode([.. results]);
        }
        catch (InvalidOperationException ex) when (
            IsUnmatchedColumnProjection(options, ex))
        {
            CommandError.Write(ex.Message);
            return 1;
        }
    }

    internal static int PackageIntegrityExitCode(params InspectionResult[] results)
        => PackageIntegrityExitCode(0, results);

    internal static int PackageIntegrityExitCode(
        int currentExitCode,
        params InspectionResult[] results)
    {
        var identifierFailures = results
            .Select(
                (result, index) =>
                    (
                        Input: index + 1,
                        Failure:
                            result.IdentifierConfusionFailure))
            .Where(
                failure =>
                    failure.Failure is not null)
            .Select(
                failure =>
                    (
                        failure.Input,
                        Failure: failure.Failure!.Value))
            .ToList();
        foreach (var (input, failure) in identifierFailures)
        {
            CommandError.WriteWarning(
                $"Identifier audit failed for package input #{input}: "
                + IdentifierConfusionAudit.DescribeFailure(failure));
        }

        if (currentExitCode != 0)
            return currentExitCode;

        return results.Any(
                static result =>
                    result.SourceIntegrity?.Mismatched is > 0)
            || identifierFailures.Count > 0
                ? 1
                : 0;
    }

    private static bool IsUnmatchedColumnProjection(
        InspectionOptions options,
        InvalidOperationException exception)
        => options.Columns is { Length: > 0 }
            && exception.Message.StartsWith(
                "No columns matched projection:",
                StringComparison.Ordinal);

    internal static int WriteMultiPackageCount(
        IReadOnlyList<InspectionResult> results,
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var projection = CaptureMultiPackageCountProjection(results, options, pipeline);
        var ordered = OutputFormatter.ResolveCountMapSections(
            pipeline, options.IncludeSections, options.FixedOverview);
        CountOutput.Write(
            projection, ordered, options.Format, options.NoHeader, options.OutputPath, options.Rows);
        return PackageIntegrityExitCode([.. results]);
    }

    private static CountProjection CaptureMultiPackageCountProjection(
        IReadOnlyList<InspectionResult> results,
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var selectedSections = ResolveMultiPackageCountSections(options, pipeline);
        var schema = PackageDiscoverySchema();

        var projection = new CountProjection();
        var documentSections = new HashSet<string>(
            selectedSections,
            StringComparer.OrdinalIgnoreCase);

        foreach (var section in selectedSections.Where(IsMultiPackageFieldSection))
        {
            documentSections.Remove(section);
            if (ProjectionExcludesSection(
                    schema, section, options, combinedRows: true))
            {
                projection.RecordRows(section, 0);
            }
            else
            {
                var rows = BuildMultiPackageFieldRows(
                    results,
                    section,
                    options.Fields);
                DiagnoseMissingPackageFieldSectionFields(
                    section,
                    options.Fields,
                    rows.Select(row => row[1]));
                projection.RecordRows(
                    section,
                    WindowedCount(rows.Length, options.Rows));
            }
        }

        foreach (var section in selectedSections.Where(IsPackageFileSection))
        {
            documentSections.Remove(section);
            int count = ProjectionExcludesSection(
                    schema, section, options, combinedRows: true)
                ? 0
                : BuildMultiPackageFileRows(
                    results, section, options.SkipEmpty).Count;
            projection.RecordRows(
                section,
                WindowedCount(count, options.Rows));
        }

        foreach (var section in documentSections.ToArray())
        {
            if (!ProjectionExcludesSection(schema, section, options))
                continue;

            documentSections.Remove(section);
            projection.RecordRows(section, 0);
        }

        if (documentSections.Count == 0)
            return projection;

        var documentOptions = options with
        {
            Select = null,
            SelectDefault = false,
            FixedOverview = false,
            IncludeSections = documentSections,
            Fields = HasMatchingProjection(
                schema, documentSections, "field", options.Fields)
                    ? options.Fields
                    : null,
            Columns = HasMatchingProjection(
                schema, documentSections, "column", options.Columns)
                    ? options.Columns
                    : null,
        };
        foreach (var result in results)
        {
            projection.Merge(OutputFormatter.CapturePackageCountProjection(
                result, documentOptions, pipeline));
        }

        return projection;
    }

    private static bool ProjectionExcludesSection(
        DocumentSchema schema,
        string section,
        InspectionOptions options,
        bool combinedRows = false)
    {
        var itemKind = schema.GetSection(section)?.ItemKind;
        if (itemKind?.Equals("field", StringComparison.OrdinalIgnoreCase) == true
            && options.Fields is { Length: > 0 }
            && !ProjectionMatches(schema, section, options.Fields))
        {
            return true;
        }

        return options.Columns is { Length: > 0 }
            && !ProjectionMatches(
                PackageCountColumnSchema(schema, combinedRows),
                section,
                options.Columns);
    }

    private static bool HasMatchingProjection(
        DocumentSchema schema,
        IEnumerable<string> sections,
        string itemKind,
        string[]? selectors)
        => selectors is { Length: > 0 }
            && sections.Any(section =>
                schema.GetSection(section)?.ItemKind.Equals(
                    itemKind,
                    StringComparison.OrdinalIgnoreCase) == true
                && ProjectionMatches(schema, section, selectors));

    private static bool ProjectionMatches(
        DocumentSchema schema,
        string section,
        string[] selectors)
        => schema.ValidateProjection(section, selectors).Resolved.Length > 0;

    private static HashSet<string> ResolveMultiPackageCountSections(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
        => options.IncludeSections is { Count: > 0 } includeSections
            ? new HashSet<string>(includeSections, StringComparer.OrdinalIgnoreCase)
            : options.FixedOverview
                ? new HashSet<string>(
                    pipeline.BareSelectSectionNames,
                    StringComparer.OrdinalIgnoreCase)
                : throw new InvalidOperationException(
                    "Multi-package count requires at least one selected section.");

    private static bool TryResolveMultiPackageRowSection(InspectionOptions options, out string? section)
    {
        section = null;
        if (!options.Tabular)
            return true;

        if (SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections))
        {
            CommandError.Write("Multiple package row output requires one concrete section; @All produces a multi-section document.");
            return false;
        }

        if (options.IncludeSections is not { Count: > 0 })
        {
            section = PackageSections.PackageInfo;
            return true;
        }

        if (options.IncludeSections.Count != 1)
        {
            CommandError.Write($"Multiple package row output requires exactly one section; matched {options.IncludeSections.Count}: {string.Join(", ", options.IncludeSections)}.");
            return false;
        }

        section = options.IncludeSections.Single();
        if (IsMultiPackageFieldSection(section)
            || IsPackageFileSection(section))
        {
            return true;
        }

        CommandError.Write($"Multiple package row output does not support section: {section}.");
        CommandError.WriteLine("Use --json, or select Package Info, Signature, Package files, or a package file section (see -D @Files).");
        return false;
    }

    private static bool ValidateMultiPackagePackageInfoColumns(
        InspectionOptions options,
        IReadOnlySet<string>? countSections,
        string? rowSection)
    {
        if (options.Columns is not { Length: > 0 }
            || (countSections is null && rowSection is null))
            return true;

        var schema = PackageDiscoverySchema();
        var columnSchema = PackageCountColumnSchema(
            schema,
            combinedRows: true);
        IReadOnlyCollection<string> selectedSections =
            countSections is { Count: > 0 }
                ? countSections
                : rowSection is not null
                    ? [rowSection]
                    : [];
        bool anyColumnMatches = options.Columns.Any(pattern =>
            selectedSections.Any(section =>
            {
                // "*" names the complete structural row. Narrower patterns that also
                // select package fields remain wrong-kind projections.
                if (IsMultiPackageFieldSection(section)
                    && !string.Equals(
                        pattern,
                        "*",
                        StringComparison.Ordinal)
                    && ResolveProjectionNames(
                        GetMultiPackageFieldNames(section),
                        [pattern]).Length > 0)
                {
                    return false;
                }

                return columnSchema.ValidateProjection(
                    section,
                    [pattern]).Resolved.Length > 0;
            }));
        if (anyColumnMatches)
            return true;

        CommandError.Write(
            $"No columns matched projection: {string.Join(", ", options.Columns)}");
        return false;
    }

    private static readonly string[] PackageInfoFieldNames =
    [
        "Authors",
        "Built",
        "Content",
        "Deprecated Note",
        "Framework Dependent",
        "Highest TFM",
        "Libraries",
        "License",
        "License URL",
        "Owners",
        "Published",
        "Readme",
        "Repository",
        "Repository Commit",
        "Repository Type",
        "RID-Specific Pointer",
        "Runtime Identifiers",
        "Runtime Target RID",
        "Signed",
        "Size",
        "Source",
        "TFM Count",
        "Tool Commands",
        "Type",
        "Verified",
        "Version",
        "Vulnerabilities"
    ];

    private static readonly string[] MultiPackageInfoColumnNames =
    [
        "Package",
        "Field",
        "Value",
    ];

    private static readonly string[] MultiPackageFileColumnNames =
    [
        "Package",
        "Version",
        "Path",
        "Size",
    ];

    private static readonly string[] PackageSignalsColumnNames =
    [
        "Area",
        "Signal",
        "Value",
        "Evidence"
    ];

    internal static DocumentSchema PackageDiscoverySchema()
        => AddPackageDynamicDiscoveryItems(InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema());

    internal sealed record AllLibrariesRowSchema(
        string Section,
        string[] Headers,
        string[] StableHeaders,
        string[]? AlternateHeaders = null,
        string[]? AlternateStableHeaders = null);

    internal static IReadOnlyList<AllLibrariesRowSchema>
        AllLibrariesRowSchemas { get; } =
        CreateAllLibrariesRowSchemas();

    internal static DocumentSchema PackageAllLibrariesDiscoverySchema()
    {
        var schema = new DocumentSchema();
        foreach (AllLibrariesRowSchema rowSchema in
                 AllLibrariesRowSchemas)
            schema.Add(
                rowSchema.Section,
                "column",
                rowSchema.Headers
                    .Concat(rowSchema.AlternateHeaders ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return schema;
    }

    private static IReadOnlyList<AllLibrariesRowSchema>
        CreateAllLibrariesRowSchemas()
    {
        var schemas = new List<AllLibrariesRowSchema>
        {
            new(
                SectionNames.LibraryInfo,
                [
                    "Package",
                    "Version",
                    "Library",
                    "TFM",
                    "Field",
                    "Value",
                ],
                [
                    "package",
                    "version",
                    "library",
                    "tfm",
                    "field",
                    "value",
                ]),
            new(
                "Switches",
                [
                    "Package",
                    "Version",
                    "Library",
                    "TFM",
                    "Kind",
                    "Switch",
                    "API",
                ],
                [
                    "package",
                    "version",
                    "library",
                    "tfm",
                    "kind",
                    "switch",
                    "api",
                ]),
            new(
                IntegrationSectionNames.Opportunities,
                [
                    "Package",
                    "Version",
                    "Library",
                    "TFM",
                    "Integration",
                    "API",
                    "Integration Type",
                    "Look For",
                ],
                [
                    "package",
                    "version",
                    "library",
                    "tfm",
                    "integration",
                    "api",
                    "integration_type",
                    "look_for",
                ]),
        };
        schemas.AddRange(
            LibraryIntegrationCatalog.All.Select(descriptor =>
                new AllLibrariesRowSchema(
                    descriptor.SectionName,
                    [
                        "Package",
                        "Version",
                        "Library",
                        "TFM",
                        "Kind",
                        "API",
                    ],
                    [
                        "package",
                        "version",
                        "library",
                        "tfm",
                        "kind",
                        "api",
                    ],
                    [
                        "Package",
                        "Version",
                        "Library",
                        "TFM",
                        "Kind",
                        "Type",
                    ],
                    [
                        "package",
                        "version",
                        "library",
                        "tfm",
                        "kind",
                        "type",
                    ])));
        return schemas;
    }

    private static bool ValidatePackageProjection(
        InspectionOptions options,
        int packageCount,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (options.Fields is not { Length: > 0 }
            && options.Columns is not { Length: > 0 })
        {
            return true;
        }
        if (options.Discover != null)
            return true;

        DocumentSchema schema = PackageDiscoverySchema();
        if (packageCount > 1
            && options.Count)
        {
            IReadOnlyCollection<string> countSections =
                options.FixedOverview
                    ? pipeline.BareSelectSectionNames
                    : options.IncludeSections is { Count: > 0 } includeSections
                        ? includeSections
                        : [PackageSections.PackageInfo];
            return ValidatePackageCountProjection(
                schema,
                countSections,
                options,
                combinedRows: true);
        }

        bool multiPackageRowShape =
            packageCount > 1
            && options.Tabular
            && !SelectResolver.IsActiveAllSelector(
                options.Select,
                options.IncludeSections)
            && (options.FixedOverview
                || options.IncludeSections is not { Count: > 0 }
                || (options.IncludeSections.Count == 1
                    && (IsMultiPackageFieldSection(
                            options.IncludeSections.Single())
                        || IsPackageFileSection(
                            options.IncludeSections.Single()))));
        if (multiPackageRowShape)
        {
            string section =
                options.IncludeSections is { Count: 1 } includeSections
                    ? includeSections.Single()
                    : PackageSections.PackageInfo;
            return ValidatePackageCountProjection(
                schema,
                [section],
                options,
                combinedRows: true);
        }

        if (options.FixedOverview)
        {
            return ValidatePackageCountProjection(
                schema,
                pipeline.BareSelectSectionNames,
                options,
                combinedRows: false);
        }

        if (options.IncludeSections is not { Count: > 0 })
            return true;

        return ValidatePackageCountProjection(
            schema,
            options.IncludeSections,
            options,
            combinedRows: false);
    }

    private static bool ValidatePackageCountProjection(
        DocumentSchema schema,
        IReadOnlyCollection<string> sections,
        InspectionOptions options,
        bool combinedRows)
    {
        bool valid = true;
        if (options.Fields is { Length: > 0 })
        {
            valid &= ProjectionDiagnostics.ValidateProjection(
                schema,
                sections,
                options.Fields,
                columns: null);
        }

        if (options.Columns is { Length: > 0 })
        {
            valid &= ProjectionDiagnostics.ValidateProjection(
                PackageCountColumnSchema(schema, combinedRows),
                sections,
                fields: null,
                options.Columns);
        }

        return valid;
    }

    private static DocumentSchema PackageCountColumnSchema(
        DocumentSchema schema,
        bool combinedRows)
    {
        var result = new DocumentSchema();
        foreach (string name in schema.SectionNames)
        {
            var section = schema.GetSection(name);
            if (combinedRows && IsMultiPackageFieldSection(name))
            {
                result.Add(
                    name,
                    "column",
                    MultiPackageInfoColumnNames);
            }
            else if (combinedRows && IsPackageFileSection(name))
            {
                result.Add(
                    name,
                    "column",
                    MultiPackageFileColumnNames);
            }
            else if (section is { Items.Length: > 0 }
                && string.Equals(
                    section.ItemKind,
                    "column",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    name,
                    section.ItemKind,
                    section.Items.Select(item => item.Name).ToArray());
            }
            else if (section is { Items.Length: > 0 }
                && string.Equals(
                    section.ItemKind,
                    "field",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    name,
                    "column",
                    ["Field", "Value"]);
            }
            else
            {
                result.AddSection(name);
            }
        }

        return result;
    }

    private static string[]? ResolvePackageInfoFields(
        string[]? patterns)
        => patterns is not { Length: > 0 }
            ? null
            : ResolveProjectionNames(PackageInfoFieldNames, patterns);

    private static string[]? ResolvePackageFieldSectionFields(
        string section,
        string[]? patterns)
    {
        if (patterns is not { Length: > 0 })
            return null;

        return ResolveProjectionNames(
            GetMultiPackageFieldNames(section),
            patterns);
    }

    private static bool IsMultiPackageFieldSection(string? section)
        => section is not null
            && (section.Equals(
                    PackageSections.PackageInfo,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    PackageSections.Signature,
                    StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> GetMultiPackageFieldNames(
        string section)
        => section.Equals(
            PackageSections.PackageInfo,
            StringComparison.OrdinalIgnoreCase)
            ? PackageInfoFieldNames
            : SigningSection.FieldNames;

    private static string[] ResolveProjectionNames(
        IReadOnlyList<string> availableNames,
        IReadOnlyList<string> patterns)
    {
        const string ProbeSection = "probe";
        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pattern in patterns)
        {
            foreach (string availableName in availableNames)
            {
                if (!seen.Contains(availableName)
                    && new DocumentSchema()
                        .Add(
                            ProbeSection,
                            "column",
                            [availableName])
                        .ValidateProjection(
                            ProbeSection,
                            [pattern])
                        .Resolved
                        .Length > 0)
                {
                    seen.Add(availableName);
                    resolved.Add(availableName);
                }
            }
        }

        return [.. resolved];
    }

    private static DocumentSchema AddPackageDynamicDiscoveryItems(DocumentSchema schema)
    {
        var result = new DocumentSchema();
        foreach (var name in schema.SectionNames)
        {
            var section = schema.GetSection(name);
            if (string.Equals(name, PackageSections.PackageInfo, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(name, "field", PackageInfoFieldNames);
            }
            else if (string.Equals(name, PackageSections.Signals, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(name, "column", PackageSignalsColumnNames);
            }
            else if (section is { Items.Length: > 0 })
            {
                result.Add(name, section.ItemKind, section.Items.Select(i => i.Name).ToArray());
            }
            else
            {
                result.AddSection(name);
            }
        }

        return result;
    }

    internal static bool DiscoverRequestsSection(
        string[]? discover,
        string sectionName,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (discover is null)
            return false;
        if (discover.Length == 0)
            return true;

        var categories = pipeline.GetCategoryMap();
        foreach (var value in discover)
        {
            if (categories.TryGetValue(value, out var categorySections)
                && categorySections.Contains(sectionName, StringComparer.OrdinalIgnoreCase))
                return true;

            var (matches, miss) = SelectResolver.ResolveSingle(value, pipeline.SelectableSectionNames, singleGlob: true);
            if (miss == null && matches.Contains(sectionName, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool RequestsSelectedOrDiscoveredSection(
        InspectionOptions options,
        string sectionName,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (options.IncludeSections is { } selectedSections)
        {
            return selectedSections.Contains(sectionName)
                && (options.Discover is null
                    || DiscoverRequestsSection(
                        options.Discover,
                        sectionName,
                        pipeline));
        }

        return DiscoverRequestsSection(
            options.Discover,
            sectionName,
            pipeline);
    }

    internal static InspectionOptions CreateProducerOptions(
        InspectionOptions options,
        Verbosity userVerbosity,
        SectionPipeline<InspectionResult> pipeline)
    {
        HashSet<string>? producerSections = options.IncludeSections;
        if (options.Discover is not null)
        {
            HashSet<string> candidates;
            if (options.Discover.Length == 0)
            {
                candidates = pipeline.GetCandidateSections(
                    options.Verbosity,
                    fixedOverview: options.FixedOverview);
                if (options.IncludeSections is { } selectedSections)
                    candidates.IntersectWith(selectedSections);
            }
            else
            {
                candidates = options.IncludeSections is { } selectedSections
                    ? selectedSections.ToHashSet(
                        StringComparer.OrdinalIgnoreCase)
                    : pipeline.SelectableSectionNames.ToHashSet(
                        StringComparer.OrdinalIgnoreCase);
            }

            producerSections = candidates
                .Where(section => DiscoverRequestsSection(
                    options.Discover,
                    section,
                    pipeline))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return options with
        {
            Verbosity = userVerbosity,
            IncludeSections = producerSections,
        };
    }

    internal static bool RequestsRidPackageAvailability(
        InspectionOptions options,
        bool isLocalFile,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (RequestsSelectedOrDiscoveredSection(
                options,
                PackageSections.Manifest,
                pipeline))
        {
            return true;
        }

        return isLocalFile
            && options.IncludeSections is null
            && options.Discover is null
            && pipeline.GetCandidateSections(
                    options.Verbosity,
                    fixedOverview: options.FixedOverview)
                .Contains(PackageSections.Manifest);
    }

    private static bool ValidateMultiPackageMode(InspectionOptions options)
    {
        List<string> conflicts = [];
        if (options.ExplicitVersion != null) conflicts.Add("--version");
        if (options.ListVersions) conflicts.Add("--versions/--version/--latest-version");
        if (options.ListLayout) conflicts.Add("--layout");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.Print) conflicts.Add("--print");
        if (options.Value) conflicts.Add("--value");
        if (options.Urls) conflicts.Add("--urls");
        if (options.Paths) conflicts.Add("--paths");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
        else if (options.Tree && options.Discover == null && !options.Count) conflicts.Add("--tree");
        if (options.PackageLibrary != null) conflicts.Add("--library");
        if (options.AllLibraries) conflicts.Add("--all-libraries");
        if (options.Discover != null) conflicts.Add("-D/--discover");

        if (conflicts.Count == 0)
            return true;

        CommandError.Write($"Multiple package inspection cannot be combined with {string.Join(", ", conflicts)}.");
        CommandError.WriteLine("Use id@version for per-package version pins.");
        return false;
    }

    private static bool ValidatePackageContentMode(InspectionOptions options)
    {
        bool scopedContent = options.ContentScope != PackageFileContentScope.Full;
        if (options.FrontmatterRequested && options.BodyRequested)
        {
            CommandError.Write("--frontmatter/--yaml-header cannot be combined with --body.");
            return false;
        }

        if (options.Print && options.ShowContent)
        {
            CommandError.Write("--print cannot be combined with --content.");
            return false;
        }

        if (options.PrintRow is not null
            && !options.Print
            && !options.Value
            && !options.Urls
            && !options.Paths)
        {
            CommandError.Write("--row requires --print, --value, --urls, or --paths.");
            return false;
        }

        if (options.Print && options.Rows is not null)
        {
            CommandError.Write("--rows cannot be combined with --print; use --row N|first|last to choose a printed row.");
            return false;
        }

        if (options.ShowContent && !HasPathFilter(options))
        {
            CommandError.Write("--content requires at least one --path selector.");
            return false;
        }

        if (scopedContent && !options.Print && !options.ShowContent)
        {
            CommandError.Write("--frontmatter/--yaml-header and --body require --print or --content.");
            return false;
        }

        if (options.ShowContent && options.JsonOutput)
        {
            CommandError.Write("--content supports --jsonl for structured output, not --json.");
            return false;
        }

        if (options.ShowContent && options.Tabular && !options.Jsonl)
        {
            CommandError.Write("--content supports separator output or --jsonl; it cannot be combined with --table or --tsv.");
            return false;
        }

        if (options.ShowContent)
        {
            List<string> conflicts = [];
            if (options.ListLayout) conflicts.Add("--layout");
            if (options.ListTfms) conflicts.Add("--tfms");
            if (options.ListVersions) conflicts.Add("--versions/--version/--latest-version");
            if (options.ShowDependencies) conflicts.Add("--dependencies");
            if (options.PackageLibrary != null) conflicts.Add("--library");
            if (options.AllLibraries) conflicts.Add("--all-libraries");
            if (options.Discover != null) conflicts.Add("-D/--discover");
            if (options.Columns != null) conflicts.Add("--columns");
            if (options.Fields != null) conflicts.Add("--fields");
            if (conflicts.Count > 0)
            {
                CommandError.Write($"--content cannot be combined with {string.Join(", ", conflicts)}.");
                return false;
            }
        }

        return true;
    }

    private static InspectionOptions NormalizeDependencyProjection(InspectionOptions options)
    {
        if (!options.ShowDependencies)
            return options;

        var select = options.Select?.ToList() ?? [];
        if (options.IncludeSections is { Count: > 0 })
        {
            foreach (var section in options.IncludeSections)
            {
                if (!select.Contains(section, StringComparer.OrdinalIgnoreCase))
                    select.Add(section);
            }
        }
        if (!select.Contains(PackageSections.Dependencies, StringComparer.OrdinalIgnoreCase))
            select.Add(PackageSections.Dependencies);

        return options with
        {
            Select = [.. select],
            SelectDefault = false,
            Tree = true,
        };
    }

    private static bool ValidateDependencyTreeProjection(InspectionOptions options)
    {
        bool dependencyTreeProjection =
            options.IncludeSections is { Count: 1 }
            && options.IncludeSections.Contains(PackageSections.Dependencies);
        if (!options.Tree
            || options.Discover != null
            || (options.Count
                && !options.ShowDependencies
                && !dependencyTreeProjection))
            return true;

        if (!dependencyTreeProjection)
        {
            CommandError.Write(
                options.ShowDependencies
                    ? "--dependencies is an alias for -S Dependencies --tree and cannot be combined with other section selections."
                    : "--tree requires exactly one tree-shaped section (-S Dependencies).");
            return false;
        }

        bool typedDependencyCount = options.ShowDependencies && options.Count;
        if (options.Print
            || options.Value
            || options.Urls
            || options.Paths
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || (!typedDependencyCount
                && (options.Count
                    || options.Rows is not null
                    || options.Bare
                    || options.JsonOutput
                    || options.Format != OutputFormat.Markdown
                    || options.Tabular
                    || options.Tsv
                    || options.Jsonl
                    || options.JsonArray
                    || options.NoHeader
                    || options.TabularExplicitlySet)))
        {
            var optionName = options.ShowDependencies ? "--dependencies" : "--tree";
            CommandError.Write($"{optionName} cannot be combined with row projections or non-Markdown formats.");
            return false;
        }

        return true;
    }

    private static DocumentSchema FilterDiscoverySchema(
        DocumentSchema schema,
        IReadOnlySet<string> selectedSections)
    {
        var result = new DocumentSchema();
        foreach (var name in schema.SectionNames)
        {
            if (!selectedSections.Contains(name))
                continue;

            var section = schema.GetSection(name);
            if (section is { Items.Length: > 0 })
                result.Add(name, section.ItemKind, section.Items.Select(item => item.Name).ToArray());
            else
                result.AddSection(name);
        }

        return result;
    }

    /// <summary>
    /// The printable sections are the document members of the package file family: each lists
    /// documents the package ships, so each row declares a payload <c>--print</c> can project.
    /// The whole-package listing is deliberately excluded — it lists assemblies and images too,
    /// and printability is a row capability rather than something a listing shape implies.
    /// </summary>
    private static bool ValidatePackagePrintSelection(HashSet<string>? sections)
    {
        if (sections is { Count: 1 }
            && (sections.Contains(PackageSections.FilesReadme)
                || sections.Contains(PackageSections.FilesNuspec)
                || sections.Contains(PackageSections.FilesSkills)))
            return true;

        CommandError.Write("--print requires -S/--select to match exactly one printable section.");
        return false;
    }

    private static int WritePackageShapeProjection(InspectionResult result, InspectionOptions options)
    {
        var kind = ShapeProjectionOutput.GetKind(options.Value, options.Urls, options.Paths);
        var section = options.IncludeSections!.Single();
        var rows = section switch
        {
            PackageSections.PackageInfo => ProjectPackageInfo(result, section, kind, options),
            PackageSections.Files => ProjectPackageFiles(new InspectionResultView(result).Files, section, kind, options),
            PackageSections.FilesNuspec => ProjectPackageFiles(new InspectionResultView(result).NuspecFiles, section, kind, options),
            PackageSections.FilesReadme => ProjectPackageFiles(new InspectionResultView(result).PackageReadme, section, kind, options),
            PackageSections.FilesSkills => ProjectPackageFiles(new InspectionResultView(result).SkillFiles, section, kind, options),
            PackageSections.SourceLinkFiles => ProjectPackageSourceFiles(result, section, kind, options),
            _ => []
        };

        if (rows.Count == 0
            && !PackageFileFamily.IsFamilySection(section)
            && section is not (PackageSections.PackageInfo or PackageSections.Files
                or PackageSections.FilesReadme or PackageSections.SourceLinkFiles))
        {
            CommandError.Write($"section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }

        return ShapeProjectionOutput.Write(rows,
            new ShapeProjectionOptions(
                kind,
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                new ProjectionDestination(options.OutputPath, options.Rows)));
    }

    /// <summary>
    /// Projects the printable payload of the selected section's rows. Document sections list
    /// the files they describe as rows, so the documents printed here are the rows the section
    /// renders rather than a document re-derived from the command line. Cardinality, the
    /// <c>--row</c> selector, and output shape belong to <see cref="PrintProjectionOutput"/>,
    /// so a new printable section declares its rows and needs no printer of its own.
    /// </summary>
    private static int WritePackagePrintProjection(InspectionResult result, string extractPath, InspectionOptions options)
    {
        var section = options.IncludeSections!.Single();
        var predicate = PackageFileFamily.PredicateFor(section)
            ?? throw new InvalidOperationException($"'{section}' is not a printable section.");
        List<PackageFile>? sourceRows = result.PackageFiles?
            .Where(predicate)
            .ToList();
        List<PackageFileText>? rows = new PackageInspectionText(result)
            .SelectPackageFiles(predicate);

        // A family with no rows and a file listing that was never collected are different facts.
        // Reporting the second as the first would tell the caller this package ships no such
        // document when the truth is that nothing ever looked.
        if (rows is null)
        {
            CommandError.Write(
                $"the package file listing was not collected, so '{section}' cannot be printed.");
            return 1;
        }
        if (sourceRows is null || sourceRows.Count != rows.Count)
        {
            throw new InvalidOperationException(
                $"The raw and presentation rows for '{section}' do not agree.");
        }

        // The shared writer refuses an empty payload, but it has no row to name the section from,
        // so it can only say "selected section". This package does ship documents of other kinds,
        // and naming the empty section is what tells the caller which one is missing.
        if (rows.Count == 0)
        {
            CommandError.Write($"this package contains no '{section}' document to print.");
            return 1;
        }

        // A Markdown scope names a Markdown construct. The caller named this section explicitly,
        // so silently returning the whole document -- or an empty one -- would answer a question
        // they did not ask. Report that the scope does not apply to this document instead.
        var isReadmeSection = section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase);
        int nonMarkdownIndex = options.ContentScope == PackageFileContentScope.Full
            ? -1
            : sourceRows.FindIndex(row => !IsMarkdownDocument(row.Path, isReadmeSection));
        if (nonMarkdownIndex >= 0)
        {
            CommandError.Write(
                $"--frontmatter/--yaml-header and --body apply to Markdown documents; " +
                $"'{rows[nonMarkdownIndex].Path}' is not Markdown.");
            return 1;
        }

        // Row identity is metadata, so the selection is resolved before any document is read and
        // the payload of exactly one row is acquired -- one --print authorizes one fetch.
        var printableRows = new List<PrintableRow>(rows.Count);
        var sourceByRow = new Dictionary<PrintableRow, PackageFile>(
            ReferenceEqualityComparer.Instance);
        for (var i = 0; i < rows.Count; i++)
        {
            string path = rows[i].Path.ToString();
            var row = new PrintableRow(i + 1, section, path, path, null);
            printableRows.Add(row);
            sourceByRow.Add(row, sourceRows[i]);
        }

        return PrintProjectionOutput.Write(
            printableRows,
            row =>
            {
                PackageFileContent content = ReadPackageFileContent(
                    extractPath,
                    result.PackageName ?? string.Empty,
                    result.Version ?? string.Empty,
                    sourceByRow[row],
                    options.ContentScope,
                    normalizeGithubLinksToRaw: !options.BrowsableUrls,
                    includeExactContent: HasUnstructuredOutputPath(options)
                        && options.ContentScope == PackageFileContentScope.Full);
                return content.SelectedContent is { } selected
                    ? PrintableContent.FromContainmentSelection(selected)
                    : new PrintableContent(content.Content, content.ExactContent);
            },
            new PrintProjectionOptions(
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                PackagePayloadDestination(options)));
    }

    private static List<ShapeProjectionRow> ProjectPackageFiles(IEnumerable<PackageFileRow>? files, string section, ShapeProjectionKind kind, InspectionOptions options)
    {
        List<ShapeProjectionRow> rows = [];
        var list = files?.ToList() ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            var file = list[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Paths => file.Path,
                ShapeProjectionKind.Value => SelectPackageFileValue(file, options),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
                continue;
            rows.Add(new ShapeProjectionRow(i + 1, section, value, Path: file.Path));
        }
        return rows;
    }

    private static string? SelectPackageFileValue(PackageFileRow file, InspectionOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "path" => file.Path,
            "size" => file.Size.ToString(CultureInfo.InvariantCulture),
            _ => file.Path
        };
    }

    private static List<ShapeProjectionRow> ProjectPackageSourceFiles(InspectionResult result, string section, ShapeProjectionKind kind, InspectionOptions options)
    {
        var sourceRows = new InspectionResultView(result).SourceFiles ?? [];
        return sourceRows
            .Select((row, index) =>
            {
                string? value = kind switch
                {
                    ShapeProjectionKind.Urls => row.Url,
                    ShapeProjectionKind.Value => SelectPackageSourceValue(row, options),
                    _ => null
                };
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : new ShapeProjectionRow(index + 1, section, value, Label: row.Type, Url: row.Url);
            })
            .Where(row => row is not null)
            .Cast<ShapeProjectionRow>()
            .ToList();
    }

    private static string? SelectPackageSourceValue(PackageSourceFileRow row, InspectionOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "library" => row.Library,
            "type" => row.Type,
            "url" => row.Url,
            _ => row.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectPackageInfo(InspectionResult result, string section, ShapeProjectionKind kind, InspectionOptions options)
    {
        if (kind != ShapeProjectionKind.Value)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            CommandError.Write("--value for Package Info requires --fields <name>.");
            return [];
        }

        var text = new PackageInspectionText(result);
        string? signed = GetPackageSignedValue(result);

        (string? Raw, string? Contained) value = field.ToLowerInvariant() switch
        {
            "version" => (result.Version, text.Version.ToString()),
            "readme" => (result.PackageReadmeFile, text.PackageReadmeFile?.ToString()),
            "repository" => (result.Repository, text.Repository?.ToString()),
            "repository commit" or "repository_commit" => (
                result.RepositoryCommit,
                text.RepositoryCommit?.ToString()),
            "repository type" or "repository_type" => (
                result.RepositoryType,
                text.RepositoryType?.ToString()),
            "license" => (result.License, text.License?.ToString()),
            "license url" or "license_url" => (result.LicenseUrl, text.LicenseUrl?.ToString()),
            "source" => (result.Source, text.Source?.ToString()),
            "type" => (
                result.PackageTypes is { Count: > 0 } rawTypes
                    ? string.Join(", ", rawTypes)
                    : null,
                text.PackageTypes is { Count: > 0 } containedTypes
                    ? InertString.Join(", ", TextPolicy.Field, containedTypes).ToString()
                    : null),
            "signed" => (signed, signed),
            "size" => (
                result.PackageSize?.ToString(CultureInfo.InvariantCulture),
                result.PackageSize?.ToString(CultureInfo.InvariantCulture)),
            _ => (null, null)
        };

        return string.IsNullOrWhiteSpace(value.Raw)
            ? []
            : [new ShapeProjectionRow(1, section, value.Contained!, Label: field)];
    }

    internal static string? GetPackageSignedValue(InspectionResult result)
        => result.Signed switch
        {
            true => "Verified",
            false => "Unsigned",
            null => null,
        };

    private static bool ValidatePathMatchMode(InspectionOptions options)
    {
        if (options.PathMatchMode.Equals("all", StringComparison.OrdinalIgnoreCase)
            || options.PathMatchMode.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        CommandError.Write($"--match must be 'all' or 'first', not '{options.PathMatchMode}'.");
        return false;
    }

    private static bool TryCreatePackageTarget(string packageArg, out PackageReferenceTarget target)
    {
        target = null!;
        var parsed = PackageExtractor.ParsePackageTarget(packageArg);
        if (parsed.IsLocalFile)
        {
            if (!File.Exists(packageArg))
            {
                CommandError.Write($"File not found: {packageArg}");
                return false;
            }

            target = parsed;
            return true;
        }

        if (!PackageExtractor.IsValidPackageReferenceVersion(parsed.Version))
        {
            CommandError.Write($"'{parsed.Version}' is not a valid package version.");
            CommandError.WriteLine("Versions look like: 1.0.0, 8.0.5, 13.0.3-beta1, 11.0.0-preview*");
            CommandError.WriteLine("Use id@version for per-package version pins.");
            return false;
        }

        target = parsed;
        return true;
    }

    private sealed record PackageFileContentSet(string PackageName, string Version, List<PackageFileContent> Files);

    private sealed class PackageFileContentAcquisition(
        PackageExtractionResult resolution,
        string packageName,
        string packageVersion,
        string? readmeFile,
        string? declaredReadmeFile) : IDisposable
    {
        public PackageFileContentSet Read(
            InspectionOptions options,
            bool suppressUnaryPayloadRead,
            string? selectedPayloadPath = null)
            => ReadPackageFileContents(
                resolution.ExtractPath,
                packageName,
                packageVersion,
                readmeFile,
                declaredReadmeFile,
                options,
                suppressUnaryPayloadRead,
                selectedPayloadPath);

        public void Dispose()
            => CleanupPackageExtraction(resolution);
    }

    private static async Task<int> ExecuteMultiPackageContentAsync(
        string[] packageArgs,
        InspectionOptions options,
        CommandContext context)
    {
        var targets = new List<PackageReferenceTarget>();
        foreach (var packageArg in packageArgs)
        {
            if (!TryCreatePackageTarget(packageArg, out var target))
                return 1;
            targets.Add(target);
        }

        var destination = PackagePayloadDestination(options);
        if (!ProjectionDestinationWriter.ValidateBeforeAcquisition(destination))
            return 1;

        var results = new List<PackageFileContentSet>();
        bool unaryPayload = RequiresUnaryPackageContent(options);
        if (!unaryPayload)
        {
            foreach (var target in targets)
            {
                var result = await ReadPackageFileContentsAsync(
                    target,
                    options,
                    context,
                    suppressUnaryPayloadRead: false);
                if (result == null)
                    return 1;
                results.Add(result);
            }

            return PrintPackageFileContents(results, options);
        }

        var acquisitions = new List<PackageFileContentAcquisition>();
        try
        {
            foreach (var target in targets)
            {
                PackageFileContentAcquisition? acquisition =
                    await AcquirePackageFileContentAsync(
                        target,
                        options,
                        context);
                if (acquisition == null)
                    return 1;
                acquisitions.Add(acquisition);
                results.Add(
                    acquisition.Read(
                        options,
                        suppressUnaryPayloadRead: true));
            }

            if (SelectUnaryPackageContent(results, options) is { } selectedFile)
            {
                int selectedPackage = results.FindIndex(
                    result => result.Files.Any(
                        file => ReferenceEquals(file, selectedFile)));
                if (selectedPackage < 0)
                    throw new InvalidOperationException(
                        "The selected package content row has no owning package.");

                results[selectedPackage] =
                    acquisitions[selectedPackage].Read(
                        options,
                        suppressUnaryPayloadRead: true,
                        selectedFile.Path);
            }

            return PrintPackageFileContents(results, options);
        }
        finally
        {
            foreach (var acquisition in acquisitions)
                acquisition.Dispose();
        }
    }

    private static PackageFileContent? SelectUnaryPackageContent(
        IReadOnlyList<PackageFileContentSet> results,
        InspectionOptions options)
    {
        List<PackageFileContent> visibleFiles =
        [
            .. RowWindow.Apply(
                options.Rows,
                FlattenPackageFileContentRows(results, options).ToList())
                .Where(static file => file.Found),
        ];
        return visibleFiles is [var selectedFile]
            ? selectedFile
            : null;
    }

    private static async Task<PackageFileContentSet?> ReadPackageFileContentsAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        CommandContext context,
        bool suppressUnaryPayloadRead)
    {
        using PackageFileContentAcquisition? acquisition =
            await AcquirePackageFileContentAsync(
                target,
                options,
                context);
        return acquisition?.Read(
            options,
            suppressUnaryPayloadRead);
    }

    private static async Task<PackageFileContentAcquisition?>
        AcquirePackageFileContentAsync(
            PackageReferenceTarget target,
            InspectionOptions options,
            CommandContext context)
    {
        var logger = context.Logger;
        PackageExtractionResult? resolution = null;
        bool ownershipTransferred = false;
        string version = target.Version;
        try
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(
                context.HttpClient,
                target.IsLocalFile
                    ? target.OriginalArgument
                    : target.PackageName,
                logger.Log,
                sourceOptions: options.SourceOptions,
                version: target.IsLocalFile
                    ? null
                    : (version.Length > 0 ? version : null),
                forceLatest: options.ForceLatest,
                includePrerelease: options.IncludePrerelease);

            if (!outcome.IsSuccess)
            {
                CommandError.Write($"{outcome.ErrorMessage}");
                return null;
            }

            resolution = outcome.Result!;
            version = resolution.Version ?? version;
            var nuspec =
                Services.NuspecParser.FindAndParse(
                    resolution.ExtractPath);
            var acquisition =
                new PackageFileContentAcquisition(
                    resolution,
                    nuspec?.PackageName
                        ?? resolution.PackageName
                        ?? target.PackageName,
                    nuspec?.Version ?? version,
                    PackageFileLister.ResolvePackageReadme(
                        resolution.ExtractPath,
                        nuspec?.ReadmeFile),
                    nuspec?.ReadmeFile);
            ownershipTransferred = true;
            return acquisition;
        }
        finally
        {
            if (!ownershipTransferred)
                CleanupPackageExtraction(resolution);
        }
    }

    private static void CleanupPackageExtraction(
        PackageExtractionResult? resolution)
    {
        if (resolution is not
            {
                FromCache: false,
                TempDir: not null
            }
            || !Directory.Exists(resolution.TempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(
                resolution.TempDir,
                recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static async Task<InspectionResult?> InspectPackageAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        InspectionOptions producerOptions,
        CommandContext context,
        bool wantsFilesSection,
        SectionCatalog<InspectionResult> sectionCatalog,
        PackageSourceQueryPlan sourceQueryPlan)
    {
        SectionPipeline<InspectionResult> pipeline = sectionCatalog.Pipeline;
        var logger = context.Logger;
        string? extractPath = null;
        PackageExtractionResult? resolution = null;
        string version = target.Version;

        try
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(
                context.HttpClient,
                target.IsLocalFile ? target.OriginalArgument : target.PackageName,
                logger.Log,
                sourceOptions: options.SourceOptions,
                version: target.IsLocalFile ? null : (version.Length > 0 ? version : null),
                forceLatest: options.ForceLatest,
                includePrerelease: options.IncludePrerelease);

            if (!outcome.IsSuccess)
            {
                CommandError.Write($"{outcome.ErrorMessage}");
                return null;
            }

            resolution = outcome.Result!;
            extractPath = resolution.ExtractPath;
            version = resolution.Version ?? version;
            string resolvedPackageName =
                resolution.PackageName ?? target.PackageName;

            var nuspec = Services.NuspecParser.FindAndParse(extractPath);

            long? packageSize = null;
            if (resolution.NupkgPath != null && File.Exists(resolution.NupkgPath))
                packageSize = new FileInfo(resolution.NupkgPath).Length;

            bool wantsSignals = RequestsSelectedOrDiscoveredSection(
                producerOptions,
                PackageSections.Signals,
                pipeline);
            bool wantsRidPackageAvailability =
                RequestsRidPackageAvailability(
                    producerOptions,
                    target.IsLocalFile,
                    pipeline);
            bool wantsIdentifierMetadata =
                RequiresIdentifierMetadata(producerOptions, pipeline);
            bool wantsPackageMetadata =
                RequiresPackageMetadata(producerOptions, pipeline);
            using var vulnerabilityTrafficScope = AllowsVulnerabilityTraffic(
                producerOptions)
                ? NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData)
                : null;
            var result = await PackageInspector.InspectAsync(
                resolution,
                resolvedPackageName,
                version,
                target.IsLocalFile,
                target.IsLocalFile ? target.OriginalArgument : null,
                nuspec,
                context.HttpClient,
                logger,
                options.ForceLatest,
                producerOptions.Verbosity,
                fetchMetadata: wantsPackageMetadata,
                requireIdentifierMetadata: wantsIdentifierMetadata,
                verifyRidPackageAvailability: wantsRidPackageAvailability,
                sourceOptions: options.SourceOptions);

            if (packageSize.HasValue)
                result.PackageSize = packageSize;

            await PopulatePackageSignatureAsync(
                result,
                resolution.NupkgPath,
                ShouldVerifyPackageSignature(options, wantsSignals),
                logger.Log);

            result.Source = target.IsLocalFile ? SourceKind.File : SourceKind.NuGet;

            if (wantsFilesSection)
                PopulatePackageFileSections(result, extractPath, options);

            if (ShouldPopulatePackageContentAudit(
                    producerOptions,
                    pipeline))
            {
                if (result.PackageFiles is null)
                    PopulatePackageFileSections(result, extractPath, options);
                PopulatePackageContentAudit(result, extractPath);
            }

            if (ShouldPopulatePackageSourceFiles(producerOptions)
                || !sourceQueryPlan.SectionPlan.Queries.IsEmpty)
            {
                await PopulatePackageSourceLinkAsync(
                    result,
                    extractPath,
                    resolvedPackageName,
                    version,
                    producerOptions,
                    context,
                    logger,
                    sourceQueryPlan);
            }

            FilterResultForOutput(result, options);

            if (wantsSignals)
            {
                result.BinarySignals = await PackageInspector.ScanBinarySignalsAsync(
                    extractPath, resolvedPackageName, version, context.HttpClient, logger,
                    acquirePdb: true, options.SourceOptions);
                await AuditSignalBuilder.PopulatePackageAuditAsync(
                    result, context.HttpClient, logger, options.SourceOptions);
            }

            return result;
        }
        finally
        {
            if (resolution is { FromCache: false, TempDir: not null } && Directory.Exists(resolution.TempDir))
            {
                try
                {
                    Directory.Delete(resolution.TempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static async Task PopulatePackageSignalsAsync(
        InspectionResult result,
        string extractPath,
        string? packageName,
        string? version,
        HttpClient client,
        VerboseLogger logger,
        NuGetSourceOptions? sourceOptions)
    {
        result.BinarySignals = await PackageInspector.ScanBinarySignalsAsync(
            extractPath, packageName, version, client, logger,
            acquirePdb: true, sourceOptions);
        await AuditSignalBuilder.PopulatePackageAuditAsync(
            result, client, logger, sourceOptions);
    }

    private static async Task PopulatePackageSignatureAsync(
        InspectionResult result,
        string? nupkgPath,
        bool shouldVerify,
        Action<string> log)
    {
        if (nupkgPath is null || !shouldVerify)
            return;

        log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
        result.SignatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
    }

    private static bool ShouldVerifyPackageSignature(
        InspectionOptions options,
        bool wantsSignals)
        => options.Verbosity >= Verbosity.Normal
            || wantsSignals
            || ProjectionRequestsSigned(options.Fields)
            || options.Columns?.Contains(
                "Signed",
                StringComparer.OrdinalIgnoreCase) == true
            || options.IncludeSections?.Contains(
                PackageSections.Signature) == true;

    private static bool ProjectionRequestsSigned(string[]? selectors)
        => selectors is { Length: > 0 }
            && new DocumentSchema()
                .Add(PackageSections.PackageInfo, "field", "Signed")
                .ValidateProjection(PackageSections.PackageInfo, selectors)
                .Resolved.Length > 0;

    private static string[] GetPackageProjectionNames(
        InspectionOptions options)
    {
        string itemKind =
            options.Fields is { Length: > 0 } ? "field" : "column";
        var schema = PackageDiscoverySchema();
        return schema.SectionNames
            .Select(schema.GetSection)
            .Where(section => section?.ItemKind.Equals(
                itemKind, StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(section => section!.Items.Select(item => item.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] VersionListingColumns(InspectionOptions options)
        => options.IncludeUnlisted
            ? ["Version", "Listing"]
            : ["Version"];

    private static string[] VersionFeedColumns(
        IReadOnlyList<PackageVersionSourceInfo> rows,
        InspectionOptions options)
        => options.JsonOutput || rows.Any(static row => !row.Listed)
            ? ["Version", "Feed", "Listing"]
            : ["Version", "Feed"];

    private static List<PackageFile> FilterPackageFiles(List<PackageFile> files, InspectionOptions options)
    {
        var selectors = PathSelectors(options);
        if (selectors.Length == 0)
            return files;

        if (options.PathMatchMode.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var selector in selectors)
            {
                var matches = PackageFileLister.Filter(files, selector);
                if (matches.Count > 0)
                    return [matches[0]];
            }
            return [];
        }

        var selected = new List<PackageFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in selectors)
        {
            foreach (var match in PackageFileLister.Filter(files, selector))
            {
                if (seen.Add(match.Path))
                    selected.Add(match);
            }
        }
        return selected;
    }

    private static string[] PathSelectors(InspectionOptions options)
        => options.PathFilters is { Length: > 0 } filters ? filters
            : options.PathFilter is { Length: > 0 } filter ? [filter]
            : [];

    private static bool HasPathFilter(InspectionOptions options) => PathSelectors(options).Length > 0;

    private static bool TryGetSingleFileSection(InspectionOptions options, out string section)
    {
        section = "";
        if (options.IncludeSections is not { Count: 1 })
            return false;

        section = options.IncludeSections.Single();
        return IsPackageFileSection(section);
    }

    private static bool IsPackageFileSection(string? section)
        => section != null
           && (section.Equals(PackageSections.Files, StringComparison.OrdinalIgnoreCase)
               || section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase)
               || PackageFileFamily.IsFamilySection(section));

    // Only when the section was actually asked for. @All deliberately excludes
    // SourceLink: Files (it is IsExpensive), so treating @All as a request here would
    // acquire PDBs over the network to populate rows no view renders.
    private static bool ShouldPopulatePackageSourceFiles(InspectionOptions options)
        => options.IncludeSections?.Contains(PackageSections.SourceLinkFiles) == true;

    internal static PackageSourceQueryPlan CreatePackageSourceQueryPlan(
        SectionCatalog<InspectionResult> sectionCatalog,
        InspectionQueryCatalog<SourceLinkQueryContext> queryCatalog,
        InspectionOptions options,
        bool excludeUnbounded)
    {
        SectionQueryPlan sectionPlan = sectionCatalog.PlanQueries(
            options.Verbosity,
            options.IncludeSections,
            options.FixedOverview,
            excludeUnbounded);
        // The IEnumerable overload boxes ImmutableArray; use the direct common-plan overloads
        // and reserve general compilation for uncommon multi-query demand.
        InspectionQueryPlan<SourceLinkQueryContext> queryPlan =
            sectionPlan.Queries.Length switch
            {
                0 => queryCatalog.Plan(
                    Array.Empty<InspectionQueryDefinition>()),
                1 => queryCatalog.Plan(sectionPlan.Queries[0]),
                _ => queryCatalog.Plan(sectionPlan.Queries),
            };
        return new PackageSourceQueryPlan(
            sectionPlan,
            queryPlan);
    }

    internal readonly record struct PackageSourceQueryPlan(
        SectionQueryPlan SectionPlan,
        InspectionQueryPlan<SourceLinkQueryContext> QueryPlan);

    private static async Task PopulatePackageSourceLinkAsync(
        InspectionResult result,
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options,
        CommandContext context,
        VerboseLogger logger,
        PackageSourceQueryPlan sourceQueryPlan)
    {
        var requestedQueries = sourceQueryPlan.SectionPlan.Queries;
        bool collectSourceFiles = ShouldPopulatePackageSourceFiles(options);
        bool auditAvailability =
            requestedQueries.Contains(SourceAvailabilityQuery.Definition);
        bool auditIntegrity =
            requestedQueries.Contains(SourceIntegrityQuery.Definition);
        if (collectSourceFiles)
            result.SourceFiles = [];

        int auditedLibraries = 0;
        int totalSourceFiles = 0;
        int accessibleSourceFiles = 0;
        int embeddedSourceFiles = 0;
        List<PackageSourceLinkFile> missingFiles = [];
        List<PackageSourceLinkIssue> availabilityUnavailable = [];
        List<PackageSourceLinkIssue> availabilityFailed = [];

        int checkedLibraries = 0;
        int verified = 0;
        int mismatched = 0;
        int lineEndingNormalized = 0;
        int unverifiable = 0;
        List<PackageSourceLinkFile> mismatchedFiles = [];
        List<PackageSourceLinkIssue> integrityUnavailable = [];
        List<PackageSourceLinkIssue> integrityFailed = [];

        var libraries = SelectPackageLibrariesForSourceFiles(extractPath, options);
        foreach (var libraryPath in libraries)
        {
            var relativePath = Path.GetRelativePath(extractPath, libraryPath).Replace('\\', '/');
            try
            {
                using var source = SourceLinkService.Open(libraryPath, logger.Log);
                var queryContext = new SourceLinkQueryContext(
                    source,
                    new FindingSubject(
                        $"package:{packageName}@{version}:{relativePath}",
                        relativePath),
                    context.HttpClient,
                    DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch,
                    packageName,
                    version,
                    isPlatformAssembly: false,
                    CoreSourceLinkQueryCache.Instance,
                    logger.Log,
                    options.SourceOptions);

                InspectionQueryResults? queryResults = null;
                if (!requestedQueries.IsEmpty)
                {
                    queryResults = await sourceQueryPlan.QueryPlan
                        .RunAsync(queryContext)
                        .ConfigureAwait(false);
                }
                else if (collectSourceFiles)
                {
                    await PdbAcquisitionService.AcquireAsync(
                        source.Context,
                        context.HttpClient,
                        packageName,
                        version,
                        isPlatformAssembly: false,
                        logger.Log,
                        sourceOptions: options.SourceOptions).ConfigureAwait(false);
                }

                if (collectSourceFiles)
                {
                    List<SourceFileInfo> rows = await SourceFileCollector.CollectAsync(
                        source,
                        libraryPath,
                        browsableUrls: options.BrowsableUrls,
                        typeFilter: options.TypeFilter).ConfigureAwait(false);
                    result.SourceFiles!.AddRange(rows.Select(row => new PackageSourceFileInfo(
                        relativePath,
                        row.Type,
                        row.Url)));
                }

                if (auditAvailability
                    && queryResults!.TryGet(
                        SourceAvailabilityQuery.Definition,
                        out SourceAvailabilityResult? availability))
                {
                    switch (availability)
                    {
                        case SourceAvailabilityResult.Available available:
                            auditedLibraries++;
                            totalSourceFiles += available.Summary.TotalSourceFiles;
                            accessibleSourceFiles += available.Summary.AccessibleSourceFiles;
                            embeddedSourceFiles += available.Summary.EmbeddedSourceFiles;
                            missingFiles.AddRange(
                                available.Summary.MissingSourceFiles.Select(
                                    path => new PackageSourceLinkFile(relativePath, path)));
                            break;
                        case SourceAvailabilityResult.Absent absent:
                            availabilityUnavailable.Add(
                                new PackageSourceLinkIssue(
                                    relativePath,
                                    absent.Detail ?? "SourceLink input is unavailable."));
                            break;
                        case SourceAvailabilityResult.Failed failed:
                            availabilityFailed.Add(
                                new PackageSourceLinkIssue(relativePath, failed.Reason));
                            break;
                    }
                }

                if (auditIntegrity
                    && queryResults!.TryGet(
                        SourceIntegrityQuery.Definition,
                        out SourceIntegrityResult? integrity))
                {
                    switch (integrity)
                    {
                        case SourceIntegrityResult.Available available:
                            checkedLibraries++;
                            verified += available.Summary.Verified;
                            mismatched += available.Summary.Mismatched;
                            lineEndingNormalized += available.Summary.LineEndingNormalized;
                            unverifiable += available.Summary.Unverifiable;
                            mismatchedFiles.AddRange(
                                available.Summary.MismatchedFiles.Select(
                                    path => new PackageSourceLinkFile(relativePath, path)));
                            break;
                        case SourceIntegrityResult.Absent absent:
                            integrityUnavailable.Add(
                                new PackageSourceLinkIssue(
                                    relativePath,
                                    absent.Detail ?? "SourceLink input is unavailable."));
                            break;
                        case SourceIntegrityResult.Failed failed:
                            integrityFailed.Add(
                                new PackageSourceLinkIssue(relativePath, failed.Reason));
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InspectionQueryException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (collectSourceFiles || (!auditAvailability && !auditIntegrity))
                    throw;

                logger.LogWarning(
                    $"Could not inspect SourceLink for {relativePath}: {ex.Message}");
                if (auditAvailability)
                {
                    availabilityFailed.Add(
                        new PackageSourceLinkIssue(relativePath, ex.Message));
                }
                if (auditIntegrity)
                {
                    integrityFailed.Add(
                        new PackageSourceLinkIssue(relativePath, ex.Message));
                }
            }
        }

        if (auditAvailability)
        {
            result.SourceAvailability = new PackageSourceAvailability(
                libraries.Count,
                auditedLibraries,
                totalSourceFiles,
                accessibleSourceFiles,
                embeddedSourceFiles,
                NullIfEmpty(missingFiles),
                NullIfEmpty(availabilityUnavailable),
                NullIfEmpty(availabilityFailed));
        }

        if (auditIntegrity)
        {
            result.SourceIntegrity = new PackageSourceIntegrity(
                libraries.Count,
                checkedLibraries,
                verified,
                mismatched,
                lineEndingNormalized,
                unverifiable,
                NullIfEmpty(mismatchedFiles),
                NullIfEmpty(integrityUnavailable),
                NullIfEmpty(integrityFailed));
        }
    }

    private static List<T>? NullIfEmpty<T>(List<T> values)
        => values.Count == 0 ? null : values;

    private static List<string> SelectPackageLibrariesForSourceFiles(string extractPath, InspectionOptions options)
    {
        var (selected, _) = TfmSelector.SelectHighestAssembliesFromPackage(extractPath, options.Tfm);
        return selected
            .OrderBy(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void PopulatePackageFileSections(InspectionResult result, string extractPath, InspectionOptions options)
    {
        bool wantsPackageFileRows = HasPathFilter(options)
            || options.IncludeSections?.Any(IsPackageFileSection) == true
            || SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
            || options.Discover != null;

        var packageReadme = result.PackageReadmeFile
            ?? PackageFileLister.ResolvePackageReadme(extractPath, result.ReadmeFile);
        result.PackageReadmeFile = packageReadme;
        result.HasReadme = packageReadme != null;
        result.HasAgentDocumentation = File.Exists(Path.Combine(extractPath, "AGENTS.md"));
        var files = PackageFileLister.ListAll(extractPath, packageReadme);
        result.PackageFiles = files;
        if (wantsPackageFileRows
            && (HasPathFilter(options)
            || options.IncludeSections?.Contains(PackageSections.Files) == true
            || SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
            || options.Discover != null))
        {
            result.Files = HasPathFilter(options)
                ? FilterPackageFiles(files, options)
                : files;
        }
    }

    private static bool ShouldPopulatePackageContentAudit(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
        => RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.AuditFindings,
            pipeline);

    private static void PopulatePackageContentAudit(
        InspectionResult result,
        string extractPath)
    {
        result.PackageContentAudit = PackageContentAudit.Scan(
            extractPath,
            result.PackageFiles?.Select(file => file.Path) ?? []);
    }

    private static PackageFileContentSet ReadPackageFileContents(
        string extractPath,
        string packageName,
        string version,
        string? readmeFile,
        string? declaredReadmeFile,
        InspectionOptions options,
        bool suppressUnaryPayloadRead = false,
        string? selectedPayloadPath = null)
    {
        var files = PackageFileLister.ListAll(extractPath, readmeFile);
        List<PackageFile> selectedFiles =
        [
            .. FilterPackageFiles(files, options)
                .Select(file => WithDeclaredReadmeRole(file, declaredReadmeFile)),
        ];
        bool unaryPayload = RequiresUnaryPackageContent(options);
        bool includeExactContent =
            unaryPayload
            && HasUnstructuredOutputPath(options)
            && options.ContentScope == PackageFileContentScope.Full;
        var contents = selectedFiles
            .Select(file => unaryPayload
                && (selectedPayloadPath is { } selectedPath
                    ? !file.Path.Equals(selectedPath, StringComparison.Ordinal)
                    : suppressUnaryPayloadRead || selectedFiles.Count != 1)
                ? new PackageFileContent(
                    packageName,
                    version,
                    file.Path,
                    file.Size,
                    Found: true,
                    Content: string.Empty,
                    file.IsReadme)
                : ReadPackageFileContent(
                    extractPath,
                    packageName,
                    version,
                    file,
                    options.ContentScope,
                    normalizeGithubLinksToRaw: !options.BrowsableUrls,
                    includeExactContent))
            .ToList();
        return new PackageFileContentSet(packageName, version, contents);
    }

    private static bool RequiresUnaryPackageContent(InspectionOptions options)
        => !LensProjection.IsRequested(options)
            && (options.Bare
                || HasUnstructuredOutputPath(options));

    private static bool RequiresEarlyPackagePayloadPreflight(
        InspectionOptions options)
    {
        if (options.ShowContent)
            return true;

        if (options.IncludeSections is not { Count: 1 } sections)
            return false;

        string section = sections.Single();
        return options.Print
            && (section.Equals(
                    PackageSections.FilesReadme,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    PackageSections.FilesNuspec,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    PackageSections.FilesSkills,
                    StringComparison.OrdinalIgnoreCase))
            || options.Bare
            && section.Equals(
                PackageSections.FilesReadme,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnstructuredOutputPath(InspectionOptions options)
        => !string.IsNullOrEmpty(options.OutputPath)
            && !options.JsonOutput
            && !options.Jsonl
            && !options.JsonArray;

    private static ProjectionDestination PackagePayloadDestination(InspectionOptions options)
        => new(
            options.OutputPath,
            options.Rows,
            ExactTransfer: (options.Print || RequiresUnaryPackageContent(options))
                && HasUnstructuredOutputPath(options)
                && options.ContentScope == PackageFileContentScope.Full);

    /// <summary>
    /// Restores the readme role to the document the manifest declares when
    /// <see cref="PackageFileLister.ResolvePackageReadme"/> passed over it. That resolver answers
    /// which single file the README section shows and prefers the conventional README.md, so a
    /// package that ships one and declares another leaves the declared file unflagged. The manifest
    /// still declared it a readme, and that declaration -- not which file the section displays --
    /// is what makes it Markdown.
    /// </summary>
    private static PackageFile WithDeclaredReadmeRole(PackageFile file, string? declaredReadme)
    {
        if (file.IsReadme || string.IsNullOrWhiteSpace(declaredReadme))
            return file;

        var declared = declaredReadme.Replace('\\', '/').Trim().TrimStart('/');
        return string.Equals(file.Path, declared, StringComparison.OrdinalIgnoreCase)
            ? file with { IsReadme = true }
            : file;
    }

    /// <summary>
    /// Whether a package document carries Markdown conventions. Extension answers this for
    /// ordinary files, and the package README answers it by role only where the extension has
    /// nothing to say. NuGet renders whatever the manifest declares as the readme, and packages
    /// do declare extensionless files, so keying only on extension would drop link rewriting and
    /// refuse frontmatter for a document that genuinely is Markdown.
    ///
    /// The role does not override an extension that is present. A manifest can declare anything
    /// -- <c>&lt;readme&gt;logo.png&lt;/readme&gt;</c> is malformed but shippable -- and letting a
    /// declaration force Markdown handling onto a document that names itself otherwise would run
    /// the link rewriter over a PNG and hand back a corrupted file, which is the outcome this
    /// command exists to prevent.
    /// </summary>
    private static bool IsMarkdownDocument(string path, bool isReadme)
        => MarkdownContent.IsMarkdown(path)
            || (isReadme && !NamesAKind(path));

    /// <summary>
    /// Whether a file name says anything about the document's kind. Any dot in the name is taken
    /// as saying something: <c>logo.png</c> names a suffix outright, <c>logo.png.</c> names one
    /// with a stray dot after it, and <c>.png</c> spells one as a hidden basename. Telling a
    /// hidden suffix apart from a hidden word like <c>.README</c> needs a list of known suffixes,
    /// which would go stale and still guess wrong at the edges.
    ///
    /// The two mistakes are not equally bad, so the tie goes to the conservative reading. Refusing
    /// a Markdown scope on <c>.README</c> is loud, names the file, and leaves the document
    /// readable; handing a declared PNG to the link rewriter returns a corrupted file and exit 0,
    /// which is the outcome this command exists to prevent.
    /// </summary>
    private static bool NamesAKind(string path)
        => Path.GetFileName(path.AsSpan()).Contains('.');

    private static PackageFileContent ReadPackageFileContent(
        string extractPath,
        string packageName,
        string version,
        PackageFile file,
        PackageFileContentScope scope,
        bool normalizeGithubLinksToRaw,
        bool includeExactContent)
    {
        var fullPath = Path.Combine(extractPath, file.Path.Replace('/', Path.DirectorySeparatorChar));
        byte[] exactContent = File.ReadAllBytes(fullPath);

        // Scoping and link rewriting are Markdown conventions. Applied to anything else they
        // corrupt the document the package shipped rather than presenting it, and the caller
        // has no way to see that it happened. So Markdown documents are presented, and every
        // other kind is passed through exactly as shipped -- including its byte order mark,
        // which ReadAllText would otherwise consume and silently shorten the document by.
        if (!IsMarkdownDocument(file.Path, file.IsReadme))
        {
            return new PackageFileContent(
                packageName,
                version,
                file.Path,
                file.Size,
                Found: true,
                ReadTextPreservingPreamble(exactContent),
                file.IsReadme,
                includeExactContent ? exactContent : null);
        }

        var content = MarkdownContent.ApplyScope(
            ReadText(exactContent),
            scope);
        if (PackageFileFamily.IsSkillDocument(file))
        {
            ContainmentSelectedText selected = AgentSkillDocument.PrepareForOutput(
                content,
                normalizeGithubLinksToRaw);
            return new PackageFileContent(
                packageName,
                version,
                file.Path,
                file.Size,
                Found: true,
                selected.ToString(),
                file.IsReadme,
                ExactContent: null,
                SelectedContent: selected);
        }

        if (normalizeGithubLinksToRaw)
            content = GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(content);

        return new PackageFileContent(
            packageName,
            version,
            file.Path,
            file.Size,
            Found: true,
            content,
            file.IsReadme,
            includeExactContent ? exactContent : null);
    }

    /// <summary>
    /// Reads text while keeping any byte order mark the file starts with. Decoding still detects
    /// the encoding from that mark, so the text is decoded correctly; the mark is then restored
    /// as a character so a verbatim document round-trips through the text pipeline with the same
    /// bytes it shipped with rather than three fewer.
    /// </summary>
    private static string ReadTextPreservingPreamble(byte[] content)
    {
        string text = ReadText(content);
        ReadOnlySpan<byte> bytes = content;
        bool hasPreamble =
            bytes.StartsWith(Encoding.UTF8.Preamble)
            || bytes.StartsWith(Encoding.Unicode.Preamble)
            || bytes.StartsWith(Encoding.BigEndianUnicode.Preamble)
            || bytes.StartsWith(Encoding.UTF32.Preamble)
            || bytes.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF });
        return hasPreamble ? '\uFEFF' + text : text;
    }

    private static string ReadText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static int PrintPackageFileContents(IReadOnlyList<PackageFileContentSet> results, InspectionOptions options)
    {
        var rows = FlattenPackageFileContentRows(results, options).ToList();
        var visibleRows = RowWindow.Apply(options.Rows, rows);

        // Same rule the print projection applies: a Markdown scope names a Markdown construct,
        // and non-Markdown documents are passed through verbatim. Without this the scope would
        // be accepted and then silently ignored, answering a --frontmatter request with the
        // whole document -- a projection answered from a different payload than the one asked
        // for, which is the defect class this command is being kept clear of.
        if (options.ContentScope != PackageFileContentScope.Full
            && visibleRows.FirstOrDefault(row => row.Found && !IsMarkdownDocument(row.Path, row.IsReadme)) is { } nonMarkdown)
        {
            CommandError.Write(
                $"--frontmatter/--yaml-header and --body apply to Markdown documents; '{nonMarkdown.Path}' is not Markdown. "
                + "Narrow the selection to Markdown, for example --path \"*.md\".");
            return 1;
        }

        // A path that matches nothing still yields one row so the render can show it as absent.
        // Counting that row would answer "one file matched" when none did, so count found files,
        // as the bare writer below already does.
        if (LensProjection.TryProject(options, "--content", visibleRows.Count(row => row.Found), out var contentProjectionExit))
            return contentProjectionExit;

        var destination = PackagePayloadDestination(options);
        if (options.Bare)
            return PrintBarePackageFileContentRows(visibleRows, destination);

        if (HasUnstructuredOutputPath(options)
            && ProjectionDestinationWriter.IsFile(destination))
        {
            List<PackageFileContent> found =
                visibleRows.Where(row => row.Found).ToList();
            if (found.Count != 1)
            {
                CommandError.Write(
                    $"--content --out requires exactly one selected package content file; found {found.Count}.");
                return 1;
            }

            WritePackageFileExport(found[0], destination);
            return 0;
        }

        var textRows = visibleRows
            .Select(PackageFileContentText.Create)
            .ToList();
        var output = options.Jsonl
            ? RenderPackageFileContentJsonl(textRows)
            : RenderPackageFileContentBlocks(textRows);

        ProjectionDestinationWriter.WriteText(destination, output);

        return 0;
    }

    private static int PrintBarePackageFileContentRows(
        IReadOnlyList<PackageFileContent> rows,
        ProjectionDestination destination)
    {
        var found = rows.Where(row => row.Found).ToList();
        if (found.Count != 1)
        {
            CommandError.Write(found.Count == 0
                ? "--bare found no selected package content."
                : $"--bare requires exactly one selected package content file; found {found.Count}.");
            return 1;
        }

        if (ProjectionDestinationWriter.IsFile(destination))
        {
            WritePackageFileExport(found[0], destination);
            return 0;
        }

        return WriteBarePackageContent(found[0], destination);
    }

    private static IEnumerable<PackageFileContent> FlattenPackageFileContentRows(
        IReadOnlyList<PackageFileContentSet> results,
        InspectionOptions options)
    {
        foreach (var result in results)
        {
            if (result.Files.Count > 0)
            {
                foreach (var file in result.Files)
                    yield return file;
            }
            else if (!options.SkipEmpty)
            {
                yield return new PackageFileContent(
                    result.PackageName,
                    result.Version,
                    Path: "",
                    Size: 0,
                    Found: false,
                    Content: "");
            }
        }
    }

    private static string RenderPackageFileContentJsonl(IReadOnlyList<PackageFileContentText> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder
                .Append(JsonSerializer.Serialize(row, PackageFileContentJsonContext.Default.PackageFileContentText))
                .Append('\n');
        return builder.ToString();
    }

    private static string RenderPackageFileContentBlocks(IReadOnlyList<PackageFileContentText> rows)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                builder.AppendLine();

            var row = rows[i];
            var path = row.Found
                ? row.PathText
                : new InertString(TextPolicy.Field, "<absent>");
            // The separator is tool-owned framing, so its untrusted parts are
            // contained even though the file content below it is deliberately
            // raw -- otherwise a ZIP entry path forges a second separator.
            builder.AppendLine(InertString.Format(
                TextPolicy.Field,
                $"------------ {row.PackageText} :: {path} ------------").ToString());
            if (!row.Found)
            {
                builder.AppendLine("(absent)");
                continue;
            }

            builder.Append(row.RenderedContent);
            if (row.Content.Length == 0 || row.Content[^1] != '\n')
            {
                if (row.IsContainmentSelected)
                    builder.Append('\n');
                else
                    builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void WriteMultiPackageTable(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        if (IsPackageFileSection(section))
        {
            WriteMultiPackageFilesTable(results, section, options);
            return;
        }

        WriteMultiPackageFieldTable(results, section, options);
    }

    private static void WriteMultiPackageFilesTable(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        if (options.Jsonl)
        {
            WriteMultiPackageFilesJsonl(results, section, options);
            return;
        }

        var rows = BuildMultiPackageFileRows(results, section, options.SkipEmpty)
            .Select(row => new[]
            {
                row.Package,
                row.Version,
                row.Path,
                row.Size?.ToString(CultureInfo.InvariantCulture) ?? "",
            })
            .ToArray();
        var windowedRows = RowWindow.Apply(options.Rows, rows).ToArray();

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateProjectedWriterOptions(
                options.Columns,
                options.Fields);
            OutputFormatter.ConfigureTableWriterOptions(
                writerOptions,
                options.Tsv,
                options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(
                ["Package", "Version", "Path", "Size"],
                ["package", "version", "path", "size"],
                windowedRows);
            markoutWriter.Flush();
        });
    }

    private static void WritePackageFilesJsonl(
        InspectionResult result,
        string section,
        RowWindow? rows)
    {
        var text = new PackageInspectionText(result);
        var files = GetPackageFileTextRows(result, text, section);
        if (files.Count == 0)
            return;

        foreach (var file in RowWindow.Apply(rows, files))
        {
            var row = new PackageFileJsonRow(file.Path, file.Size);
            Console.WriteLine(JsonSerializer.Serialize(row, PackageFileJsonRowContext.Default.PackageFileJsonRow));
        }
    }

    private static void WriteMultiPackageFilesJsonl(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        var rows = BuildMultiPackageFileRows(results, section, options.SkipEmpty);
        var selectedColumns = ResolveMultiPackageFileColumns(
            section,
            options.Columns);
        foreach (var row in RowWindow.Apply(options.Rows, rows))
            WriteMultiPackageFileJsonRow(row, selectedColumns);
    }

    private static List<PackageFileMultiJsonRow> BuildMultiPackageFileRows(
        IReadOnlyList<InspectionResult> results,
        string section,
        bool skipEmpty)
    {
        var rows = new List<PackageFileMultiJsonRow>();
        foreach (var result in results)
        {
            var text = new PackageInspectionText(result);
            var files = GetPackageFileTextRows(result, text, section);
            if (files.Count == 0)
            {
                if (!skipEmpty)
                {
                    rows.Add(
                        new PackageFileMultiJsonRow(
                            text.PackageName,
                            text.Version,
                            new InertString(TextPolicy.Field, ""),
                            null));
                }
                continue;
            }

            foreach (var file in files)
            {
                rows.Add(
                    new PackageFileMultiJsonRow(
                        text.PackageName,
                        text.Version,
                        file.Path,
                        file.Size));
            }
        }

        return rows;
    }

    private static string[] ResolveMultiPackageFileColumns(
        string section,
        string[]? columns)
    {
        if (columns is not { Length: > 0 })
            return MultiPackageFileColumnNames;

        return ResolveProjectionNames(
            MultiPackageFileColumnNames,
            columns);
    }

    private static void WriteMultiPackageFileJsonRow(
        PackageFileMultiJsonRow row,
        IReadOnlyList<string> columns)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (string column in columns)
            {
                switch (column)
                {
                    case "Package":
                        writer.WriteString("package", row.Package);
                        break;
                    case "Version":
                        writer.WriteString("version", row.Version);
                        break;
                    case "Path":
                        writer.WriteString("path", row.Path);
                        break;
                    case "Size":
                        if (row.Size is { } size)
                            writer.WriteNumber("size", size);
                        else
                            writer.WriteNull("size");
                        break;
                }
            }
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static int PrintPackageBareSelection(
        InspectionResult result,
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        if (options.IncludeSections is not { Count: 1 } include)
        {
            CommandError.Write("--bare requires exactly one -S section or --content payload.");
            return 1;
        }

        var section = include.Single();
        if (section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase))
        {
            var files = RowWindow.Apply(
                options.Rows,
                GetPackageFileRows(result, section));
            return PrintBarePackageFiles(extractPath, packageName, version, files, options, section);
        }

        if (section.Equals(PackageSections.SourceLinkFiles, StringComparison.OrdinalIgnoreCase))
        {
            var sourceFiles = RowWindow.Apply(options.Rows, result.SourceFiles ?? []);
            var urls = sourceFiles.Select(row => row.Url);
            return PrintBarePackageUrlColumn(
                urls,
                section,
                new ProjectionDestination(options.OutputPath, options.Rows));
        }

        CommandError.Write($"--bare does not support section '{section}'. Select a text section or a single URL section.");
        return 1;
    }

    private static int PrintBarePackageFiles(
        string extractPath,
        string packageName,
        string version,
        IReadOnlyList<PackageFile> files,
        InspectionOptions options,
        string section)
    {
        if (files.Count != 1)
        {
            CommandError.Write(files.Count == 0
                ? $"--bare found no package file in section '{section}'."
                : $"--bare requires section '{section}' to resolve exactly one package file; found {files.Count}.");
            return 1;
        }

        var destination = PackagePayloadDestination(options);
        if (!ProjectionDestinationWriter.ValidateBeforeAcquisition(destination))
            return 1;

        var content = ReadPackageFileContent(
            extractPath,
            packageName,
            version,
            files[0],
            PackageFileContentScope.Full,
            normalizeGithubLinksToRaw: !options.BrowsableUrls,
            includeExactContent: HasUnstructuredOutputPath(options));
        if (ProjectionDestinationWriter.IsFile(destination))
        {
            WritePackageFileExport(content, destination);
            return 0;
        }

        return WriteBarePackageContent(content, destination);
    }

    private static int PrintBarePackageUrlColumn(
        IEnumerable<string?>? urls,
        string section,
        ProjectionDestination destination)
    {
        var values = urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToList() ?? [];

        if (values.Count > 0)
            return WriteBarePackageText(string.Join('\n', values), destination);

        CommandError.Write($"--bare found no URL in section '{section}'.");
        return 1;
    }

    private static int WriteBarePackageText(
        string content,
        ProjectionDestination destination)
    {
        var output = content.EndsWith('\n') ? content : content + '\n';
        ProjectionDestinationWriter.WriteRenderedText(destination, output);
        return 0;
    }

    private static int WriteBarePackageContent(
        PackageFileContent content,
        ProjectionDestination destination)
    {
        if (content.SelectedContent is not { } selected)
            return WriteBarePackageText(content.Content, destination);

        ProjectionDestinationWriter.WriteText(
            destination,
            writer =>
            {
                writer.Write(selected.ToString());
                if (!content.Content.EndsWith('\n'))
                    writer.Write('\n');
            });
        return 0;
    }

    private static void WritePackageFileExport(
        PackageFileContent content,
        ProjectionDestination destination)
    {
        if (content.ExactContent is { } exact)
            ProjectionDestinationWriter.WriteExactBytes(destination, exact);
        else if (content.SelectedContent is { } selected)
            ProjectionDestinationWriter.WriteSelectedText(destination, selected);
        else
            ProjectionDestinationWriter.WriteRenderedText(destination, content.Content);
    }

    private static List<PackageFile> GetPackageFileRows(InspectionResult result, string section)
    {
        if (section.Equals(PackageSections.Files, StringComparison.OrdinalIgnoreCase))
            return result.Files ?? [];

        if (section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase))
        {
            if (result.PackageFiles is not { Count: > 0 } readmeFiles || string.IsNullOrWhiteSpace(result.PackageReadmeFile))
                return [];

            return readmeFiles
                .Where(file => string.Equals(file.Path, result.PackageReadmeFile, StringComparison.OrdinalIgnoreCase))
                .Take(1)
                .ToList();
        }

        if (result.PackageFiles is not { Count: > 0 } files)
            return [];

        if (PackageFileFamily.PredicateFor(section) is { } predicate)
            return files.Where(predicate).ToList();

        return [];
    }

    private static List<PackageFileText> GetPackageFileTextRows(
        InspectionResult result,
        PackageInspectionText text,
        string section)
    {
        if (section.Equals(PackageSections.Files, StringComparison.OrdinalIgnoreCase))
            return text.Files ?? [];

        if (section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(result.PackageReadmeFile))
                return [];

            return text.SelectPackageFiles(file =>
                    string.Equals(
                        file.Path,
                        result.PackageReadmeFile,
                        StringComparison.OrdinalIgnoreCase))?
                .Take(1)
                .ToList() ?? [];
        }

        if (PackageFileFamily.PredicateFor(section) is { } predicate)
            return text.SelectPackageFiles(predicate) ?? [];

        return [];
    }

    private static void WriteMultiPackageFieldTable(
        IReadOnlyList<InspectionResult> results,
        string section,
        InspectionOptions options)
    {
        var rows = BuildMultiPackageFieldRows(
            results,
            section,
            options.Fields);

        string rendered = OutputFormatter.RenderProjectedTable(
            !options.NoHeader,
            options.Tsv,
            options.Jsonl,
            options.Columns,
            fields: null,
            (writer, formatter, writerOptions) =>
            {
                var markoutWriter =
                    new MarkoutWriter(writer, formatter, writerOptions);
                markoutWriter.WriteTable(
                    ["Package", "Field", "Value"],
                    ["package", "field", "value"],
                    rows);
                markoutWriter.Flush();
            });
        DiagnoseMissingPackageFieldSectionFields(
            section,
            options.Fields,
            rows.Select(row => row[1]));
        Console.Out.Write(
            OutputFormatter.LimitRenderedTableRows(
                rendered,
                options.Rows,
                !options.NoHeader));
    }

    private static void DiagnoseMissingPackageFieldSectionFields(
        string section,
        string[]? patterns,
        IEnumerable<string> renderedFields)
    {
        if (patterns is not { Length: > 0 })
            return;

        var rendered = renderedFields.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var missing = patterns
            .Where(pattern =>
            {
                string[] resolved = ResolveProjectionNames(
                    GetMultiPackageFieldNames(section),
                    [pattern]);
                return resolved.Length > 0
                    && !resolved.Any(rendered.Contains);
            })
            .ToArray();
        if (missing.Length == 0)
            return;

        string label = missing.Length == 1
            ? "field has"
            : "fields have";
        CommandError.WriteNote(
            $"{missing.Length} {label} no data: {string.Join(", ", missing)}");
    }

    private static IEnumerable<MarkoutField> SelectPackageInfoFields(
        InspectionResult result,
        IReadOnlyList<string>? selectedFields)
    {
        var metadata = new InspectionResultView(result).Metadata;
        if (selectedFields == null)
            return metadata;

        var byName = metadata.ToDictionary(
            field => field.Key,
            StringComparer.OrdinalIgnoreCase);
        return selectedFields
            .Where(byName.ContainsKey)
            .Select(field => byName[field]);
    }

    private static IEnumerable<MarkoutField> SelectPackageFieldSectionFields(
        InspectionResultView view,
        string section,
        IReadOnlyList<string>? selectedFields)
    {
        IEnumerable<MarkoutField> fields =
            section.Equals(
                PackageSections.PackageInfo,
                StringComparison.OrdinalIgnoreCase)
                ? view.Metadata
                : view.SigningSectionData?
                    .ToMarkoutFields()
                    ?? [];
        if (selectedFields == null)
            return fields;

        var byName = fields.ToDictionary(
            field => field.Key,
            StringComparer.OrdinalIgnoreCase);
        return selectedFields
            .Where(byName.ContainsKey)
            .Select(field => byName[field]);
    }

    private static string[][] BuildMultiPackageFieldRows(
        IReadOnlyList<InspectionResult> results,
        string section,
        string[]? fields)
    {
        var selectedFields = ResolvePackageFieldSectionFields(
            section,
            fields);

        return results
            .SelectMany(result =>
            {
                var view = new InspectionResultView(result);
                return SelectPackageFieldSectionFields(
                        view,
                        section,
                        selectedFields)
                .Select(field => new[]
                {
                    view.PackageName,
                    field.Key,
                    field.Value?.ToString() ?? "",
                });
            })
            .ToArray();
    }

    private static int WindowedCount(int count, RowWindow? rows)
    {
        var (start, end) = ResolveRowWindow(count, rows);
        return end - start;
    }

    private static (int Start, int End) ResolveRowWindow(int count, RowWindow? rows)
        => rows is { IsUnlimited: false } window
            ? window.Resolve(count)
            : (0, count);

    private static void ApplyNuspec(NuspecData nuspec, InspectionResult result)
    {
        result.PackageName = nuspec.PackageName ?? result.PackageName;
        result.ManifestVersion = nuspec.ManifestVersion;
        result.Version = nuspec.Version ?? result.Version;
        result.Description = nuspec.Description;
        result.Authors = nuspec.Authors;
        result.Repository = nuspec.Repository;
        result.RepositoryType = nuspec.RepositoryType;
        result.RepositoryCommit = nuspec.RepositoryCommit;
        result.License = nuspec.License;
        result.LicenseUrl = nuspec.LicenseUrl;
        result.PackageTypes = nuspec.PackageTypes;
        result.IsToolPackage = nuspec.IsToolPackage;
        result.ReadmeFile = nuspec.ReadmeFile;
        result.DependencyGroups = nuspec.DependencyGroups;
    }

    private static bool IsNetworkUsingPackageSection(string section) =>
        section.Equals(PackageSections.Signals, StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            PackageSections.AuditIdentifierConfusion,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(PackageSections.Statistics, StringComparison.OrdinalIgnoreCase)
        || section.Equals(PackageSections.Vulnerabilities, StringComparison.OrdinalIgnoreCase);

    internal static bool AllowsVulnerabilityTraffic(InspectionOptions options) =>
        options.Verbosity >= Verbosity.Detailed
        || options.IncludeSections?.Any(IsNetworkUsingPackageSection) == true;

    private static bool ValidatePackageLibraryMode(InspectionOptions options)
    {
        if (options.Tree
            && !options.Count
            && options.Discover == null
            && (options.Format != OutputFormat.Markdown
                || options.Bare
                || options.Tabular
                || options.Tsv
                || options.Jsonl
                || options.JsonArray
                || options.NoHeader))
        {
            CommandError.Write("--tree cannot be combined with row projections or non-Markdown formats.");
            return false;
        }

        List<string> conflicts = [];
        if (options.AllLibraries) conflicts.Add("--all-libraries");
        if (options.ListLayout) conflicts.Add("--layout");
        if (HasPathFilter(options)) conflicts.Add("--path");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.ListVersions) conflicts.Add("--versions/--version/--latest-version");
        if (options.Print) conflicts.Add("--print");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
        if (string.Equals(options.Tfm, "all", StringComparison.OrdinalIgnoreCase)) conflicts.Add("--tfm all");

        if (conflicts.Count == 0)
            return true;

        CommandError.Write($"--library cannot be combined with {string.Join(", ", conflicts)}.");
        return false;
    }

    private static bool ValidatePackageAllLibrariesMode(InspectionOptions options)
    {
        List<string> conflicts = [];
        if (options.PackageLibrary != null) conflicts.Add("--library");
        if (options.ListLayout) conflicts.Add("--layout");
        if (HasPathFilter(options)) conflicts.Add("--path");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.ListVersions) conflicts.Add("--versions/--version/--latest-version");
        if (options.Print) conflicts.Add("--print");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
        if (options.Discover != null) conflicts.Add("-D/--discover");
        if (options.Tree && options.Discover == null && !options.Count) conflicts.Add("--tree");
        if (options.Columns != null) conflicts.Add("--columns");
        if (options.Fields != null) conflicts.Add("--fields");

        if (conflicts.Count > 0)
        {
            CommandError.Write($"--all-libraries cannot be combined with {string.Join(", ", conflicts)}.");
            return false;
        }

        return true;
    }

    private static async Task<int> ExecutePackageLibraryAsync(
        string extractPath,
        bool isLocalFile,
        string packageArg,
        string packageName,
        string version,
        InspectionOptions options)
    {
        var selected = ResolvePackageLibrary(extractPath, packageName, version, options);
        if (selected == null)
            return 1;

        var packageReference = isLocalFile
            ? packageArg
            : !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;

        return await LibraryCommand.ExecuteAsync(CreateLibraryOptions(
            assemblyName: Path.GetRelativePath(extractPath, selected.Path).Replace('\\', '/'),
            packageReference,
            options));
    }

    private static async Task<int> ExecutePackageAllLibrariesAsync(
        string extractPath,
        bool isLocalFile,
        string packageArg,
        string packageName,
        string version,
        PackageIntegrationAcquisition acquisition,
        InspectionOptions options)
    {
        var selected = ResolveAllPackageLibraries(extractPath, packageName, version, options);
        if (selected == null)
            return 1;

        var packageReference = isLocalFile
            ? packageArg
            : !string.IsNullOrWhiteSpace(version)
                ? $"{packageName}@{version}"
                : packageName;

        var catalog = LibrarySections.CreateCatalog();
        var sectionCatalog = catalog.Sections;
        var pipeline = catalog.Pipeline;
        var queryCatalog = catalog.QueryCatalog;
        var libraryOptions = CreateLibraryOptions(assemblyName: null, packageReference, options);

        libraryOptions = LibraryCommand.NormalizeBareSelect(libraryOptions);
        libraryOptions = libraryOptions with
        {
            UserVerbosityOverride = libraryOptions.Verbosity,
        };

        var selectResult = SelectResolver.ResolveSelectAsSections(
            libraryOptions.Select,
            sectionCatalog.SelectableSectionNames,
            sectionCatalog.InfoSectionNames,
            sectionCatalog.SelectionCategoryMap,
            selectDefault: libraryOptions.SelectDefault);
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
        {
            if (LibraryCommand.ApplyCoordinateSectionRequirements(
                    libraryOptions,
                    selectResult) is { } coordinateError)
            {
                CommandError.Write(coordinateError);
                return 1;
            }

            libraryOptions = libraryOptions with
            {
                IncludeSections = selectResult.Sections,
                ExactIncludeSectionsOverride = selectResult.ExactSections,
            };
        }

        if (!LibraryCommand.ValidateReferenceTreeCount(
                libraryOptions.Tree,
                libraryOptions.Count,
                libraryOptions.IncludeSections))
        {
            return 1;
        }

        if (libraryOptions.Count)
        {
            if (!CountOutput.ValidateSectionsSelected(
                    libraryOptions.IncludeSections, libraryOptions.FixedOverview))
            {
                return 1;
            }

            var ordered = OutputFormatter.ResolveCountMapSections(
                pipeline, libraryOptions.IncludeSections, libraryOptions.FixedOverview);
            if (!CountOutput.ValidateMapFormat(
                    libraryOptions.Format, ordered, libraryOptions.Tree))
                return 1;
        }

        var requiredVerbosity = pipeline.GetRequiredVerbosity(libraryOptions.IncludeSections);
        if (requiredVerbosity > libraryOptions.Verbosity)
            libraryOptions = libraryOptions with { Verbosity = requiredVerbosity };

        var candidates = pipeline.GetCandidateSections(
            libraryOptions.Verbosity,
            libraryOptions.IncludeSections,
            libraryOptions.FixedOverview);
        libraryOptions = libraryOptions with
        {
            CollectIdentifierConfusionReferenceTree =
                candidates.Contains(SectionNames.IdentifierConfusion),
        };

        SectionQueryPlan sectionPlan = sectionCatalog.PlanQueries(
            libraryOptions.Verbosity,
            libraryOptions.IncludeSections,
            libraryOptions.FixedOverview);
        List<HostQueryDemand> commandQueryDemand = [];
        if (libraryOptions.CollectReferenceTree)
        {
            commandQueryDemand.Add(
                new HostQueryDemand(
                    "reference tree",
                    AssemblyReferencesQuery.Definition));
        }
        if (sectionPlan.Queries.Contains(BodyShapesQuery.Definition)
            && libraryOptions.BodyKindQuery.HasFilter
            && libraryOptions.PerformanceTriage.HasCandidateFilters)
        {
            commandQueryDemand.Add(
                new HostQueryDemand(
                    "Body Shapes performance predicates",
                    OptimizationOpportunitiesQuery.Definition));
        }
        HashSet<InspectionQueryDefinition> queries =
            sectionPlan.Activate(commandDemand: commandQueryDemand);
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        bool requiresGroupedIntegrations =
            RequiresGroupedIntegrations(
                queries,
                out bool includeIntegrationOpportunities);
        InspectionQueryPlan<InspectionQueryContext> queryPlan =
            queryCatalog.Plan(queries);
        using PackageIntegrationsWorkspace? integrationsWorkspace =
            requiresGroupedIntegrations
                ? PackageIntegrationsWorkspace.Create(
                    selected.Select(selection =>
                    {
                        string relativePath = Path.GetRelativePath(
                                extractPath,
                                selection.Path)
                            .Replace('\\', '/');
                        return CreatePackageIntegrationAssembly(
                            selection.Path,
                            relativePath);
                    }),
                    acquisition,
                    includeIntegrationOpportunities:
                        includeIntegrationOpportunities)
                : null;
        List<LibraryInspection> inspections = [];
        List<(string FileName, string Reason)> groupedIntegrationsFailures = [];
        List<(string FileName, IdentifierConfusionAuditFailureKind FailureKind)>
            identifierAuditFailures = [];
        foreach (var selection in selected)
        {
            string relativePath = Path.GetRelativePath(
                    extractPath,
                    selection.Path)
                .Replace('\\', '/');

            Task<LibraryInspection?> InspectAsync(
                ResolvedAssemblyReference? assemblyReference,
                AssemblyIntegrationsEntry? integrations,
                AssemblyIntegrationOpportunitiesEntry? opportunities)
            {
                return LibraryMetadataService.InspectAsync(
                    selection.Path,
                    libraryOptions,
                    logger,
                    packageName,
                    version,
                    context.HttpClient,
                    queryPlan: queryPlan,
                    assemblyReference: assemblyReference,
                    integrationsEntry: integrations,
                    integrationOpportunitiesEntry: opportunities);
            }

            LibraryInspection? inspection;
            try
            {
                inspection =
                    integrationsWorkspace is null
                        ? await InspectAsync(null, null, null)
                        : await InspectGroupedAssemblyAsync(
                            integrationsWorkspace,
                            selection.Path,
                            relativePath,
                            groupedIntegrationsFailures,
                            InspectAsync);
            }
            catch (LibraryMetadataService.IdentifierConfusionReferenceTraversalException ex)
            {
                identifierAuditFailures.Add(
                    (
                        relativePath,
                        ex.FailureKind));
                continue;
            }

            if (inspection == null)
            {
                logger.LogWarning($"Could not read library: {Path.GetFileName(selection.Path)}");
                continue;
            }

            inspection.FileName = relativePath;
            inspection.Tfm =
                TfmResolver.ExtractFrameworkFolderFromPath(relativePath);
            inspection.Source = SourceKind.NuGet;
            inspections.Add(inspection);
        }

        bool integrationsIncomplete =
            integrationsWorkspace is not null
            && WriteGroupedIntegrationsFailures(
                groupedIntegrationsFailures);
        identifierAuditFailures.AddRange(
            inspections
                .Where(
                    inspection =>
                        inspection.IdentifierConfusionFailure is not null)
                .Select(
                    inspection =>
                        (
                            inspection.FileName,
                            inspection.IdentifierConfusionFailure!.Value)));
        bool identifierAuditIncomplete =
            WriteIdentifierAuditFailures(identifierAuditFailures);
        int completionExitCode =
            AllLibrariesCompletionExitCode(
                integrationsIncomplete
                    || identifierAuditIncomplete,
                libraryOptions,
                pipeline,
                [.. inspections]);

        if (inspections.Count == 0)
        {
            CommandError.Write($"No libraries could be read from package '{packageName}'.");
            return 1;
        }

        if (libraryOptions.JsonOutput && !libraryOptions.Count)
        {
            string json = JsonSerializer.Serialize(
                inspections.ToArray(),
                JsonContext.Default.LibraryInspectionArray);
            OutputDestination.Write(
                libraryOptions.OutputPath,
                libraryOptions.Rows,
                writer => writer.WriteLine(json));
            return completionExitCode;
        }

        var sections = GetAllLibrariesSections(inspections, libraryOptions, pipeline);
        bool tabularOutput =
            libraryOptions.TabularExplicitlySet
            && !libraryOptions.Count;
        if (tabularOutput
            && libraryOptions.Select?.Any(
                value => SelectResolver.TryResolveCategory(
                    value,
                    sectionCatalog.SelectionCategoryMap,
                    sectionCatalog.SelectableSectionNames,
                    out _,
                    out _)) == true)
        {
            CommandError.Write($"--all-libraries row output requires one concrete section; category selectors such as {SectionCategoryNames.Integrations} produce multi-section documents.");
            CommandError.WriteLine("Use Markdown output for categories, or select a section such as \"Integration: Configuration\" or Library Info.");
            return 1;
        }

        if (tabularOutput
            && libraryOptions.IncludeSections is { Count: > 0 })
        {
            var candidateSections = pipeline.GetCandidateSections(
                libraryOptions.Verbosity,
                libraryOptions.IncludeSections,
                libraryOptions.FixedOverview);
            string? unsupportedSection = candidateSections.FirstOrDefault(
                section => !SupportsAllLibrariesTableSection(section));
            if (unsupportedSection is not null)
            {
                CommandError.Write($"--all-libraries row output does not support section: {unsupportedSection}.");
                CommandError.WriteLine("Use Markdown output, or select Library Info, Switches, Integration: Opportunities, or a focused Integration: section.");
                return 1;
            }
        }

        if (libraryOptions.Count)
        {
            if (sections.Count == 0)
                CommandError.WriteNote("matched sections have no data across all libraries.");

            var projection = CaptureAllLibrariesCounts(
                inspections,
                sections,
                libraryOptions,
                pipeline);
            var ordered = OutputFormatter.ResolveCountMapSections(
                pipeline, libraryOptions.IncludeSections, libraryOptions.FixedOverview);
            CountOutput.Write(
                projection,
                ordered,
                libraryOptions.Format,
                libraryOptions.NoHeader,
                libraryOptions.OutputPath,
                libraryOptions.Rows);
            return completionExitCode;
        }

        if (sections.Count == 0)
        {
            CommandError.WriteNote("matched sections have no data across all libraries.");
            OutputDestination.Write(
                libraryOptions.OutputPath,
                libraryOptions.Rows,
                static _ => { });
            return completionExitCode;
        }

        if (tabularOutput)
        {
            if (!WriteAllLibrariesTable(
                    packageName,
                    version,
                    inspections,
                    sections,
                    libraryOptions))
            {
                return 1;
            }
            return completionExitCode;
        }

        var markdown = RenderAllLibrariesMarkdown(packageName, version, inspections, sections, libraryOptions, pipeline);
        OutputDestination.Write(
            libraryOptions.OutputPath,
            libraryOptions.Rows,
            writer => OutputFormatter.WriteLfLine(writer, markdown));
        return completionExitCode;
    }

    internal static Task<LibraryInspection?>
        InspectGroupedAssemblyAsync(
            PackageIntegrationsWorkspace workspace,
            string path,
            string relativePath,
            ICollection<(string FileName, string Reason)> failures,
            Func<
                ResolvedAssemblyReference?,
                AssemblyIntegrationsEntry?,
                AssemblyIntegrationOpportunitiesEntry?,
                Task<LibraryInspection?>> inspectAsync)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(inspectAsync);

        if (workspace.TryGetPreflightFailure(
                path,
                out string preflightFailure))
        {
            failures.Add((relativePath, preflightFailure));
            return Task.FromResult<LibraryInspection?>(null);
        }

        return workspace.UseAssemblyAsync(
            path,
            (retainedAssembly, integrations, opportunities) =>
            {
                switch (integrations)
                {
                    case AssemblyIntegrationsEntry.Rejected rejected:
                        failures.Add(
                            (relativePath, rejected.Failure.Detail));
                        return Task.FromResult<LibraryInspection?>(null);
                    case AssemblyIntegrationsEntry.Failed failed:
                        failures.Add(
                            (relativePath, failed.Error.Message));
                        break;
                }

                switch (opportunities)
                {
                    case AssemblyIntegrationOpportunitiesEntry.Rejected
                        rejected:
                        failures.Add(
                            (relativePath, rejected.Failure.Detail));
                        break;
                    case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                        failures.Add(
                            (relativePath, failed.Error.Message));
                        break;
                }

                return inspectAsync(
                    retainedAssembly,
                    integrations,
                    opportunities);
            });
    }

    internal static bool RequiresGroupedIntegrations(
        HashSet<InspectionQueryDefinition> queries,
        out bool includeIntegrationOpportunities)
    {
        includeIntegrationOpportunities = queries.Remove(
            AssemblyContextIntegrationOpportunitiesQuery.Definition);
        return queries.Remove(
                AssemblyContextIntegrationsQuery.Definition)
            || includeIntegrationOpportunities;
    }

    internal static PackageIntegrationAssembly
        CreatePackageIntegrationAssembly(
            string path,
            string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new PackageIntegrationAssembly(
            path,
            TfmResolver.ExtractFrameworkFolderFromPath(relativePath),
            TfmResolver.ExtractAssetDirectoryFromPath(relativePath));
    }

    internal static bool WriteGroupedIntegrationsFailures(
        IEnumerable<(string FileName, string Reason)> groupedFailures)
    {
        ArgumentNullException.ThrowIfNull(groupedFailures);

        var failures = groupedFailures
            .Distinct()
            .ToList();

        foreach (var (fileName, reason) in failures)
        {
            CommandError.WriteWarning(
                $"Integrations inspection failed for '{fileName}': {reason}");
        }

        return failures.Count > 0;
    }

    internal static bool WriteIdentifierAuditFailures(
        IEnumerable<(
            string FileName,
            IdentifierConfusionAuditFailureKind FailureKind)> auditFailures)
    {
        ArgumentNullException.ThrowIfNull(auditFailures);

        var failures = auditFailures
            .Distinct()
            .ToList();

        foreach (var (fileName, failureKind) in failures)
        {
            CommandError.WriteWarning(
                $"Identifier audit failed for '{fileName}': "
                + IdentifierConfusionAudit.DescribeFailure(failureKind));
        }

        return failures.Count > 0;
    }

    internal static int AllLibrariesCompletionExitCode(
        bool incomplete) =>
        incomplete ? 1 : 0;

    internal static int AllLibrariesCompletionExitCode(
        bool incomplete,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        params LibraryInspection[] inspections) =>
        Math.Max(
            AllLibrariesCompletionExitCode(incomplete),
            LibraryCommand.SelectedInspectionFailureExitCode(
                options,
                pipeline,
                inspections));

    internal static bool RequiresPackageMetadata(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline,
        bool includeSignals = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        return RequiresIdentifierMetadata(
                   options,
                   pipeline,
                   includeSignals)
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.Statistics,
                   pipeline)
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.Vulnerabilities,
                   pipeline);
    }

    internal static bool RequiresIdentifierMetadata(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline,
        bool includeSignals = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        return (includeSignals
                && RequestsSelectedOrDiscoveredSection(
                    options,
                    PackageSections.Signals,
                    pipeline))
               || RequestsSelectedOrDiscoveredSection(
                   options,
                   PackageSections.AuditIdentifierConfusion,
                   pipeline);
    }

    private static LibraryOptions CreateLibraryOptions(string? assemblyName, string packageReference, InspectionOptions options)
        => new()
        {
            AssemblyName = assemblyName,
            IncludeMetadata = true,
            PackagePath = packageReference,
            IncludePrerelease = options.IncludePrerelease,
            Tfm = options.Tfm,
            TypeFilter = options.TypeFilter,
            BrowsableUrls = options.BrowsableUrls,
            JsonOutput = options.JsonOutput,
            PlainText = options.Format == OutputFormat.PlainText,
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            TabularExplicitlySet = options.TabularExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            Format = options.Format,
            Verbose = options.Verbose,
            Verbosity = options.Verbosity,
            IncludeSections = options.IncludeSections,
            Discover = options.Discover,
            Tree = options.Tree,
            Select = options.Select,
            SelectDefault = options.SelectDefault,
            Columns = options.Columns,
            Fields = options.Fields,
            Schema = options.Schema,
            Count = options.Count,
            OutputPath = options.OutputPath,
            Value = options.Value,
            Urls = options.Urls,
            Paths = options.Paths,
            JsonArray = options.JsonArray,
            ProjectionRow = options.PrintRow,
            Rows = options.Rows,
            SourceOptions = options.SourceOptions,
            NoHeader = options.NoHeader,
            UserVerbosityOverride = options.Verbosity
        };

    private static PackageLibrarySelection? ResolvePackageLibrary(
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        var requestedLibrary = options.PackageLibrary;
        if (requestedLibrary == null)
            return null;
        var packageId = PackageExtractor.ParsePackageReference(packageName).name;
        var resolution = TfmSelector.SelectPackageLibrary(extractPath, packageId, requestedLibrary, options.Tfm);
        if (resolution.IsSelected)
            return new PackageLibrarySelection(resolution.Paths[0]);

        if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.RequestedLibraryNotFound)
            CommandError.Write($"Library '{requestedLibrary}' not found in package '{packageName}'.");
        else if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoAssemblies)
            CommandError.Write($"No DLLs found in package '{packageName}'.");
        else if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoMatchingTargetFramework)
            CommandError.Write($"No library found for TFM '{options.Tfm}' in package '{packageName}'.");
        else
            CommandError.Write(resolution.Tfm == null
                ? $"Package '{packageName}' contains multiple libraries."
                : $"Package '{packageName}' contains multiple libraries for {resolution.Tfm}.");

        if (resolution.Status != TfmSelector.PackageLibraryResolutionStatus.NoAssemblies)
            WritePackageLibraryCandidates(extractPath, packageName, version, resolution.Tfm ?? options.Tfm, resolution.CandidatePaths.ToList());
        return null;
    }

    private static List<PackageLibrarySelection>? ResolveAllPackageLibraries(
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        var resolution = TfmSelector.SelectPackageLibraries(extractPath, options.Tfm);
        if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoAssemblies)
        {
            CommandError.Write($"No DLLs found in package '{packageName}'.");
            return null;
        }
        if (resolution.Status == TfmSelector.PackageLibraryResolutionStatus.NoMatchingTargetFramework)
        {
            CommandError.Write($"No libraries found for TFM '{options.Tfm}' in package '{packageName}'.");
            WritePackageLibraryCandidates(extractPath, packageName, version, options.Tfm, resolution.CandidatePaths.ToList());
            return null;
        }

        return resolution.Paths
            .Select(path => new PackageLibrarySelection(path))
            .ToList();
    }

    private sealed record PackageLibrarySelection(string Path);

    internal static List<string> GetAllLibrariesSections(
        List<LibraryInspection> inspections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        LibraryCommand.WarnEmptySections(
            inspections,
            options,
            pipeline,
            writeEmptyNote: false);

        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        List<string> union = [];
        foreach (var inspection in inspections)
        {
            var sections = selectAll
                ? pipeline.GetAllSelectorSections(inspection)
                : pipeline.GetEffectiveSections(
                    inspection,
                    options.Verbosity,
                    options.IncludeSections,
                    options.FixedOverview);
            foreach (var section in sections)
            {
                if (!union.Contains(section, StringComparer.OrdinalIgnoreCase))
                    union.Add(section);
            }
        }

        var order = selectAll
            ? [.. pipeline.InfoSectionNames, .. pipeline.AllSectionNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)]
            : pipeline.AllSectionNames;
        return order
            .Where(section => union.Contains(section, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool WriteAllLibrariesTable(
        string packageName,
        string version,
        List<LibraryInspection> inspections,
        List<string> sections,
        LibraryOptions options)
    {
        if (sections.Count != 1)
        {
            CommandError.Write($"--all-libraries row output requires exactly one section; matched {sections.Count}: {string.Join(", ", sections)}.");
            CommandError.WriteLine("Use Markdown output for multi-section selections, or select one concrete section.");
            return false;
        }

        string section = sections[0];
        var table = BuildAllLibrariesTable(
            packageName,
            version,
            inspections,
            section,
            options.Rows);
        if (table == null)
        {
            CommandError.Write($"--all-libraries row output does not support section: {section}.");
            CommandError.WriteLine("Use Markdown output, or select Library Info, Switches, Integration: Opportunities, or a focused Integration: section.");
            return false;
        }

        if (!table.HasRowsBeforeWindow)
        {
            CommandError.WriteNote("matched section has no row data across all libraries.");
            OutputDestination.Write(
                options.OutputPath,
                options.Rows,
                static _ => { });
            return true;
        }

        OutputDestination.Write(options.OutputPath, options.Rows, output =>
        {
            OutputFormatter.WriteTable(output, !options.NoHeader, (writer, formatter) =>
            {
                var writerOptions = OutputFormatter.CreateTableWriterOptions(
                    options.Tsv,
                    options.Jsonl);
                var markoutWriter = new MarkoutWriter(
                    writer,
                    formatter,
                    writerOptions);
                markoutWriter.WriteTable(
                    table.Headers,
                    table.StableHeaders,
                    table.Rows);
                markoutWriter.Flush();
            });
        });
        return true;
    }

    private sealed record AllLibrariesTable(
        string[] Headers,
        string[] StableHeaders,
        string[][] Rows,
        bool HasRowsBeforeWindow);

    private static bool SupportsAllLibrariesTableSection(string section) =>
        FindAllLibrariesRowSchema(section) is not null;

    private static AllLibrariesTable? BuildAllLibrariesTable(
        string packageName,
        string version,
        List<LibraryInspection> inspections,
        string section,
        RowWindow? rowWindow)
    {
        AllLibrariesRowSchema? rowSchema =
            FindAllLibrariesRowSchema(section);
        if (rowSchema is null)
            return null;

        if (section.Equals("Library Info", StringComparison.OrdinalIgnoreCase))
        {
            var rowsByLibrary = inspections
                .Select(inspection =>
                    BuildLibraryInfoRows(packageName, version, inspection).ToArray())
                .ToArray();
            var libraryInfoRows = rowsByLibrary
                .SelectMany(rows => RowWindow.Apply(rowWindow, rows))
                .ToArray();
            return new(
                rowSchema.Headers,
                rowSchema.StableHeaders,
                libraryInfoRows,
                rowsByLibrary.Any(rows => rows.Length != 0));
        }

        if (section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase))
        {
            var opportunityRows = inspections
                .SelectMany(inspection => (inspection.IntegrationOpportunities ?? [])
                    .Select(opportunity => new
                    {
                        Inspection = inspection,
                        Opportunity = opportunity
                    }))
                .OrderBy(
                    row => row.Opportunity.Integration,
                    StringComparer.Ordinal)
                .ThenBy(
                    row => CodeCell(row.Opportunity.Api),
                    StringComparer.Ordinal)
                .Select(row => WithProvenance(
                    packageName,
                    version,
                    row.Inspection,
                    row.Opportunity.Integration,
                    row.Opportunity.Api,
                    row.Opportunity.IntegrationType,
                    row.Opportunity.LookFor))
                .ToArray();
            return new(
                rowSchema.Headers,
                rowSchema.StableHeaders,
                [.. RowWindow.Apply(rowWindow, opportunityRows)],
                opportunityRows.Length != 0);
        }

        if (section.Equals("Switches", StringComparison.OrdinalIgnoreCase))
        {
            var switchRows = inspections
                .SelectMany(inspection => inspection.SwitchInspection.PayloadsForRendering()
                    .Select(switchInfo => new
                    {
                        Inspection = inspection,
                        SwitchInfo = switchInfo
                    }))
                .OrderBy(
                    row => row.SwitchInfo.Kind,
                    StringComparer.Ordinal)
                .ThenBy(
                    row => CodeCell(row.SwitchInfo.Switch),
                    StringComparer.Ordinal)
                .Select(row => WithProvenance(
                    packageName,
                    version,
                    row.Inspection,
                    row.SwitchInfo.Kind,
                    row.SwitchInfo.Switch,
                    row.SwitchInfo.Api))
                .ToArray();
            return new(
                rowSchema.Headers,
                rowSchema.StableHeaders,
                [.. RowWindow.Apply(rowWindow, switchRows)],
                switchRows.Length != 0);
        }

        var descriptor = LibraryIntegrationCatalog.All.FirstOrDefault(d =>
            d.SectionName.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
            return null;

        var signals = inspections
            .SelectMany(inspection => descriptor.GetSignals(inspection)
                .Select(signal => new { Inspection = inspection, Signal = signal }))
            .ToList();
        var hasApis = signals.Any(row => row.Signal.Shape == IntegrationSignalShape.Api);
        var includeTypes = descriptor.IncludeTypesWhenApisPresent;
        string[] headers =
            hasApis
                ? rowSchema.Headers
                : rowSchema.AlternateHeaders
                    ?? rowSchema.Headers;
        string[] stableHeaders =
            hasApis
                ? rowSchema.StableHeaders
                : rowSchema.AlternateStableHeaders
                    ?? rowSchema.StableHeaders;
        var focusedRows = signals
            .Where(row => !hasApis || includeTypes || row.Signal.Shape == IntegrationSignalShape.Api)
            .OrderBy(row => row.Signal.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Signal.Name, StringComparer.Ordinal)
            .Select(row => WithProvenance(
                packageName,
                version,
                row.Inspection,
                row.Signal.Kind,
                row.Signal.Name))
            .ToArray();
        return new(
            headers,
            stableHeaders,
            [.. RowWindow.Apply(rowWindow, focusedRows)],
            focusedRows.Length != 0);
    }

    private static AllLibrariesRowSchema?
        FindAllLibrariesRowSchema(string section) =>
        AllLibrariesRowSchemas.FirstOrDefault(schema =>
            schema.Section.Equals(
                section,
                StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string[]> BuildLibraryInfoRows(string packageName, string version, LibraryInspection inspection)
    {
        var info = new LibraryInspectionView(inspection).AssemblyInfoSection;
        if (info == null)
            yield break;

        foreach (var (field, value) in new (string Field, object? Value)[]
                 {
                     ("Architecture", info.Architecture),
                     ("Assembly Version", info.AssemblyVersion),
                     ("Async Methods", info.AsyncMethods),
                     ("Company", info.Company),
                     ("Compilation", info.Compilation),
                     ("Copyright", info.Copyright),
                     ("Custom Attributes", info.CustomAttributes),
                     ("Deterministic", info.Deterministic ? "Yes" : "No"),
                     ("Extension Methods", info.ExtensionMethods),
                     ("Facade", info.Facade switch
                     {
                         true => "Yes",
                         false => "No",
                         null => null
                     }),
                     ("File Size", info.FileSize),
                     ("Informational Version", info.InformationalVersion),
                     ("Integrations", info.Integrations),
                     ("Methods", info.Methods),
                     ("Modified", info.Modified),
                     ("Name", info.Name),
                     ("Product", info.Product),
                     ("Public Key Token", info.PublicKeyToken),
                     ("Reproducible", info.Reproducible ? "Yes" : "No"),
                     ("Resources", info.Resources),
                     ("Signed", info.Signed),
                     ("Source", info.Source),
                     ("Switches", info.Switches),
                     ("Target Framework", info.TargetFramework),
                     ("Type Forwarders", info.TypeForwarders),
                     ("Types", info.Types),
                     ("Union Types", info.UnionTypes),
                     ("Version", info.Version)
                 })
        {
            if (value == null)
                continue;

            yield return WithProvenance(
                packageName,
                version,
                inspection,
                field,
                value.ToString() ?? "");
        }
    }

    private static string[] WithProvenance(
        string packageName,
        string version,
        LibraryInspection inspection,
        params string[] values)
        =>
        [.. new[]
        {
            packageName,
            version,
            inspection.FileName,
            inspection.Tfm ?? ""
        }, .. values];

    private static string RenderAllLibrariesMarkdown(
        string packageName,
        string version,
        List<LibraryInspection> inspections,
        List<string> sections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(version) ? packageName : $"{packageName} {version}";
        AppendBlock(sb, $"# {title}");

        foreach (var section in sections)
        {
            if (IsAggregatedAllLibrariesSection(section))
                AppendAggregatedSection(sb, section, inspections, options.Rows);
            else
                AppendPerLibrarySections(sb, section, inspections, options, pipeline);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends one rendered block, separated from whatever precedes it by exactly one blank line.
    /// </summary>
    /// <remarks>
    /// Written once rather than at each of the three call sites, which restated it and so had to
    /// keep three copies of the separator agreeing on a line ending. When a copy disagreed the
    /// result was silent: #3963 read a CRLF tail as "no blank line yet" and doubled the blank
    /// before every section on Windows.
    /// <para>
    /// Those call sites also guarded the trailing newlines with
    /// <c>!sb.ToString().EndsWith("\n\n")</c> -- the two section sites did; the title site, which
    /// runs first, never had it. That guard is unreachable and is not carried over.
    /// Every append to the buffer goes through this method, and this method always leaves exactly
    /// <c>"\n\n"</c> at the tail, so the guard was false for every block after the first and the
    /// length test excluded the first. It was load-bearing only while the tail could be CRLF,
    /// which is what #3981 removed. Keeping it would leave two mechanisms for one property and
    /// gate neither; the separation is asserted from the rendered document instead, by
    /// <c>PackageCommand_AllLibraries_AggregatedSection_SeparatesBlocksWithOneBlankLine</c>.
    /// </para>
    /// </remarks>
    private static void AppendBlock(StringBuilder sb, string rendered)
        => sb.Append(rendered).Append('\n').Append('\n');

    /// <summary>
    /// Renders one runtime-named, runtime-column section through the serializer so its rows reach
    /// the writer, which is what applies <c>--rows</c>.
    /// </summary>
    private static void AppendAggregatedSection(
        StringBuilder sb,
        string section,
        List<LibraryInspection> inspections,
        RowWindow? rows)
    {
        var document = BuildAggregatedSection(section, inspections);
        if (document is null)
            return;

        var output = new StringWriter { NewLine = "\n" };
        MarkoutSerializer.Serialize(
            document, output, InspectionContext.Default, OutputFormatter.CreateWindowedOptions(rows));
        var rendered = output.ToString().Trim();
        if (rendered.Length == 0)
            return;

        AppendBlock(sb, rendered);
    }

    private static bool IsAggregatedAllLibrariesSection(string section)
        => section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase)
           || section.Equals("Switches", StringComparison.OrdinalIgnoreCase)
           || LibraryIntegrationCatalog.All.Any(descriptor => descriptor.SectionName.Equals(section, StringComparison.OrdinalIgnoreCase));

    private static AggregatedSectionDocument? BuildAggregatedSection(
        string section,
        List<LibraryInspection> inspections)
    {
        if (section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase))
        {
            var opportunityRows = inspections
                .SelectMany(inspection => (inspection.IntegrationOpportunities ?? [])
                    .Select(row => new
                    {
                        Library = inspection.FileName,
                        Tfm = inspection.Tfm ?? "",
                        row.Integration,
                        Api = CodeCell(row.Api),
                        row.IntegrationType,
                        row.LookFor
                    }))
                .OrderBy(row => row.Integration, StringComparer.Ordinal)
                .ThenBy(row => row.Api, StringComparer.Ordinal)
                .ToList();
            if (opportunityRows.Count == 0)
                return null;

            return CreateAggregatedSection(section, new MarkoutTable(
                ["Library", "TFM", "Integration", "API", "Integration Type", "Look For"],
                opportunityRows.Select(row => new[]
                {
                    CodeCell(row.Library),
                    CodeCell(row.Tfm),
                    row.Integration,
                    row.Api,
                    row.IntegrationType,
                    row.LookFor
                }).ToList()));
        }

        if (section.Equals("Switches", StringComparison.OrdinalIgnoreCase))
        {
            var switchRows = inspections
                .SelectMany(inspection => inspection.SwitchInspection.PayloadsForRendering()
                    .Select(row => new
                    {
                        Library = inspection.FileName,
                        Tfm = inspection.Tfm ?? "",
                        row.Kind,
                        Switch = CodeCell(row.Switch),
                        Api = CodeCell(row.Api)
                    }))
                .OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Switch, StringComparer.Ordinal)
                .ToList();
            if (switchRows.Count == 0)
                return null;

            return CreateAggregatedSection(section, new MarkoutTable(
                ["Library", "TFM", "Kind", "Switch", "API"],
                switchRows.Select(row => new[]
                {
                    CodeCell(row.Library),
                    CodeCell(row.Tfm),
                    row.Kind,
                    row.Switch,
                    row.Api
                }).ToList()));
        }

        var descriptor = LibraryIntegrationCatalog.All.FirstOrDefault(d =>
            d.SectionName.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
            return null;

        var signals = inspections
            .SelectMany(inspection => descriptor.GetSignals(inspection)
                .Select(signal => new
                {
                    Library = inspection.FileName,
                    Tfm = inspection.Tfm ?? "",
                    Signal = signal
                }))
            .ToList();
        if (signals.Count == 0)
            return null;

        var hasApis = signals.Any(row => row.Signal.Shape == IntegrationSignalShape.Api);
        var includeTypes = descriptor.IncludeTypesWhenApisPresent;
        var focusedRows = signals
            .Where(row => !hasApis || includeTypes || row.Signal.Shape == IntegrationSignalShape.Api)
            .OrderBy(row => row.Signal.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Signal.Name, StringComparer.Ordinal)
            .ToList();
        if (focusedRows.Count == 0)
            return null;

        var includeKindColumn = focusedRows.Select(row => row.Signal.Kind).Distinct(StringComparer.Ordinal).Count() > 1;
        var valueColumn = hasApis ? "API" : "Type";

        List<string> headers = ["Library", "TFM"];
        if (includeKindColumn) headers.Add("Kind");
        headers.Add(valueColumn);

        return CreateAggregatedSection(section, new MarkoutTable(headers, focusedRows.Select(row =>
        {
            List<string> values = [CodeCell(row.Library), CodeCell(row.Tfm)];
            if (includeKindColumn) values.Add(row.Signal.Kind);
            values.Add(CodeCell(row.Signal.Name));
            return values.ToArray();
        }).ToList()));
    }

    private static AggregatedSectionDocument CreateAggregatedSection(
        string section,
        MarkoutTable table)
        => new()
        {
            Sections = [new AggregatedSectionView { Name = section, Body = table }]
        };

    private static CountProjection CaptureAllLibrariesCounts(
        List<LibraryInspection> inspections,
        List<string> sections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var projection = new CountProjection();

        foreach (var section in sections)
        {
            if (IsAggregatedAllLibrariesSection(section))
            {
                if (BuildAggregatedSection(section, inspections) is { } document)
                {
                    projection.Merge(CountProjectionFormatter.Capture(
                        document,
                        InspectionContext.Default,
                        OutputFormatter.CreateWindowedOptions(options.Rows)));
                }
                continue;
            }

            foreach (var inspection in inspections)
            {
                if (!pipeline.GetEffectiveSections(
                        inspection, options.Verbosity, options.IncludeSections, options.FixedOverview)
                    .Contains(section, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (MetadataSectionNames.IsMetadataSection(section))
                {
                    projection.Merge(MetadataLensRenderer.CaptureCounts(
                        inspection,
                        [section],
                        options.Rows));
                    continue;
                }

                projection.Merge(CountProjectionFormatter.Capture(
                    new LibraryInspectionView(inspection),
                    InspectionContext.Default,
                    CreateAllLibrariesWriterOptions(section, options)));
            }
        }

        return projection;
    }

    private static void AppendPerLibrarySections(
        StringBuilder sb,
        string section,
        List<LibraryInspection> inspections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        foreach (var inspection in inspections)
        {
            if (!pipeline.GetEffectiveSections(
                    inspection,
                    options.Verbosity,
                    options.IncludeSections,
                    options.FixedOverview)
                .Contains(section, StringComparer.OrdinalIgnoreCase))
                continue;

            var rendered = RenderLibrarySection(
                inspection,
                section,
                options,
                pipeline);
            if (rendered.Length == 0)
                continue;

            AppendBlock(sb, rendered);
        }
    }

    private static string RenderLibrarySection(
        LibraryInspection inspection,
        string section,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var view = new LibraryInspectionView(inspection);
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = [section],
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields),
        };
        var markdown = OutputFormatter.SerializeLibraryMarkdown(
                view,
                inspection,
                writerOptions,
                pipeline,
                options.Rows)
            .Trim();
        if (markdown.Length == 0)
            return "";

        var lines = markdown.Split('\n').ToList();
        if (lines.Count > 0 && lines[0].StartsWith("# ", StringComparison.Ordinal))
        {
            lines.RemoveAt(0);
            if (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals($"## {section}", StringComparison.Ordinal))
            {
                lines[i] = $"## {section} ({inspection.FileName})";
                break;
            }
        }

        return string.Join('\n', lines).Trim();
    }

    private static MarkoutWriterOptions CreateAllLibrariesWriterOptions(
        string section,
        LibraryOptions options)
        => new()
        {
            IncludeSections = [section],
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields),
            // Windowed before both count reduction and the aggregate heading rewrite above.
            RowWindow = RowWindow.ToMarkout(options.Rows)
        };

    /// <summary>
    /// Marks a cell as code using markout's semantic inline tag rather than literal backticks, so
    /// the formatter owns the spelling.
    /// </summary>
    /// <remarks>
    /// This also changes two escapes that hand-written backticks got wrong. A pipe was written as
    /// <c>&amp;#124;</c>, which renders literally inside a code span; markout emits <c>\|</c>,
    /// which GFM unescapes while splitting rows, before code spans are parsed. A backtick was
    /// written as <c>\`</c>, but backslash escapes do not apply inside a code span; markout uses
    /// the doubled-delimiter form instead.
    ///
    /// Both corrections are unverified against real data: no package in the differential corpus
    /// produced a pipe or a backtick in these cells. They are reachable in principle — the
    /// integration scanner takes raw metadata names, which carry arity backticks such as
    /// <c>IEnumerable`1</c>, without the display-name normalization other scanners apply — but
    /// that path is not exercised by a test, so treat this as a latent fix rather than an
    /// observed one.
    /// </remarks>
    private static string CodeCell(string value) => MarkoutInline.Code(value);

    private static void WritePackageLibraryCandidates(
        string extractPath,
        string packageName,
        string version,
        string? tfm,
        List<string>? candidates = null)
    {
        candidates ??= string.IsNullOrWhiteSpace(tfm)
            ? TfmSelector.GetPackageAssemblies(extractPath)
            : TfmSelector.SelectHighestAssembliesFromPackage(extractPath, tfm).paths;

        if (candidates.Count > 0)
        {
            CommandError.WriteLine("Available libraries:");
            foreach (var candidate in candidates
                         .Select(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                CommandError.WriteLine($"  {candidate}");
            }
        }

        var packageReference = !string.IsNullOrWhiteSpace(version)
            ? $"{packageName}@{version}"
            : packageName;
        CommandError.WriteBlankLine();
        CommandError.WriteLine("Use:");
        CommandError.WriteLine($"  dotnet-inspect package {packageReference} --library <dll>");
    }

    private static void WarnEmptySections(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var (empty, requested) = pipeline.GetEmptySections(result, options.Verbosity, options.IncludeSections);
        if (empty.Count > 0 && empty.Count == requested)
        {
            var label = empty.Count == 1 ? "section has" : "sections have";
            CommandError.WriteNote($"{empty.Count} matched {label} no data: {string.Join(", ", empty)}.");
        }
    }

    private static void FilterResultForOutput(InspectionResult result, InspectionOptions options)
    {
        // Filter dependency groups and set TFM when --tfm is requested
        if (!string.IsNullOrEmpty(options.Tfm))
        {
            result.Tfm = options.Tfm;

            if (result.DependencyGroups is { Count: > 0 })
            {
                result.DependencyGroups = result.DependencyGroups
                    .Where(g => g.TargetFramework.Equals(options.Tfm, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
    }

    private static int ListPackageLayout(string extractPath, InspectionOptions options, string packageName, TipLevel tipLevel)
    {
        string searchPath;
        string relativeBase;

        // Scope to a specific TFM if requested
        if (!string.IsNullOrEmpty(options.Tfm))
        {
            string libDir = Path.Combine(extractPath, "lib", options.Tfm);
            string toolsDir = Path.Combine(extractPath, "tools", options.Tfm);

            if (Directory.Exists(libDir))
                searchPath = libDir;
            else if (Directory.Exists(toolsDir))
                searchPath = toolsDir;
            else
            {
                CommandError.Write($"TFM '{options.Tfm}' not found. Use --tfms to list available frameworks.");
                return 1;
            }

            // Show paths relative to parent of TFM dir so TFM appears as root node
            relativeBase = Path.GetDirectoryName(searchPath)!;
        }
        else
        {
            var (resolved, error) = ResolveScopedPath(extractPath, options);
            if (error != null)
            {
                CommandError.Write(error);
                return 1;
            }
            searchPath = resolved;
            relativeBase = extractPath;
        }

        string[] files = Directory.GetFiles(searchPath, "*", SearchOption.AllDirectories);

        var relativePaths = files
            .Select(f => Path.GetRelativePath(relativeBase, f))
            .Where(p => !PackageFileLister.IsPlumbing(
                p.Replace('\\', '/')))
            .OrderBy(p => p);

        var results = options.Limit.HasValue 
            ? relativePaths.Take(options.Limit.Value).ToList()
            : relativePaths.ToList();
        var visibleResults = RowWindow.Apply(options.Rows, results);

        if (LensProjection.TryProject(options, "--layout", visibleResults.Count, out var projectionExitCode))
            return projectionExitCode;

        PackageOutputFormatter.WriteFileTree([.. visibleResults]);
        WriteFileLayoutTips(extractPath, options, packageName, tipLevel, isLayout: true);
        return 0;
    }

    internal static void WriteFileLayoutTips(string extractPath, InspectionOptions options, string packageName, TipLevel tipLevel, bool isLayout)
    {
        // Tips are not shown for --layout mode
    }

    private static (string path, string? error) ResolveScopedPath(string extractPath, InspectionOptions options)
    {
        if (options.ScopeLib)
        {
            var dir = Path.Combine(extractPath, "lib");
            return Directory.Exists(dir) ? (dir, null) : (dir, "No lib/ directory found in package.");
        }
        if (options.ScopeTools)
        {
            var dir = Path.Combine(extractPath, "tools");
            return Directory.Exists(dir) ? (dir, null) : (dir, "No tools/ directory found in package.");
        }
        return (extractPath, null);
    }

    private static int ListPackageTfms(string extractPath, InspectionOptions options)
    {
        var tfms = TfmSelector.GetPackageTfms(extractPath);
        var visibleTfms = RowWindow.Apply(options.Rows, tfms);

        if (LensProjection.TryProject(
                options,
                "--tfms",
                visibleTfms.Count,
                out var projectionExit,
                ["TFM"]))
            return projectionExit;

        OutputFormatter.WriteStringList(visibleTfms, "TFM", "Tfm", options.Tsv, options.Jsonl, Console.Out);
        return 0;
    }

    private static async Task<int> ShowDependencyTreeAsync(
        HttpClient client,
        string packageReference,
        InspectionOptions options,
        VerboseLogger logger)
    {
        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                client,
                packageReference,
                options.Tfm,
                options.SourceOptions,
                logger,
                includePrerelease: options.IncludePrerelease,
                allowCompatibleFallbackForRequestedTfm: false);

        if (result is PackageDependencyGraphResult.Error error)
        {
            CommandError.Write(
                error.Message,
                error.Detail is null ? [] : [error.Detail]);
            return 1;
        }
        if (result is PackageDependencyGraphResult.Empty empty)
        {
            if (LensProjection.TryProject(
                    options,
                    "--dependencies",
                    rowCount: 0,
                    out var projectionExit,
                    ["Package", "Version", "Author"]))
            {
                return projectionExit;
            }
            var packageName =
                new InertString(
                    TextPolicy.Field,
                    empty.ManifestPackageName);
            var version =
                new InertString(
                    TextPolicy.Field,
                    empty.ManifestVersion);
            var description =
                new InertString(TextPolicy.Field, empty.Message);
            var emptyView = new EmptyDepsView
            {
                Title = InertString.Format(
                    TextPolicy.Field,
                    $"{packageName} ({version})").ToString(),
                Description = description.ToString()
            };
            OutputDestination.Write(
                options.OutputPath,
                options.Rows,
                writer => MarkoutSerializer.Serialize(
                    emptyView,
                    writer,
                    InspectionContext.Default));
            return 0;
        }

        var graph = (PackageDependencyGraphResult.Graph)result;
        var visibleCount = WindowedCount(
            TreeRowWindow.Count(graph.Dependencies, node => node.Children),
            options.Rows);
        if (LensProjection.TryProject(
                options,
                "--dependencies",
                visibleCount,
                out var countExit,
                ["Package", "Version", "Author"]))
        {
            return countExit;
        }

        var visibleNodes = TreeRowWindow.Apply(
            graph.Dependencies,
            options.Rows,
            node => node.Children,
            (node, children) => node with { Children = children });
        var packageText =
            new InertString(
                TextPolicy.Field,
                graph.ManifestPackageName);
        var versionText =
            new InertString(
                TextPolicy.Field,
                graph.ManifestVersion);
        var view = new PackageDependenciesView
        {
            Title = InertString.Format(
                TextPolicy.Field,
                $"{packageText} {versionText}").ToString(),
            Dependencies = ToTreeNodes(visibleNodes)
        };

        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            writer => MarkoutSerializer.Serialize(
                view,
                writer,
                PackageDependenciesContext.Default));
        return 0;
    }

    /// <summary>
    /// Builds the dependency tree's labels.
    /// </summary>
    /// <remarks>
    /// Every part of a label -- id, version, and author -- is nuspec text
    /// chosen by whoever built the package, and a tree label sits in a gutter
    /// where a line terminator forges a sibling node. Containment happens on
    /// the composed label, after the parts are joined, so the separators cannot
    /// be split apart either (issue #3319).
    /// </remarks>
    private static List<TreeNode> ToTreeNodes(List<DependencyNode> nodes)
    {
        return nodes.Select(n =>
        {
            var packageId = new InertString(TextPolicy.Field, n.PackageId);
            var version = new InertString(TextPolicy.Field, n.Version);
            var label = !string.IsNullOrEmpty(n.Author)
                ? InertString.Format(
                    TextPolicy.Field,
                    $"{packageId} {version} [{new InertString(TextPolicy.Field, n.Author)}]")
                : InertString.Format(TextPolicy.Field, $"{packageId} {version}");
            return n.Children.Count > 0
                ? new TreeNode(label.ToString()) { Children = ToTreeNodes(n.Children) }
                : new TreeNode(label.ToString());
        }).ToList();
    }
}
