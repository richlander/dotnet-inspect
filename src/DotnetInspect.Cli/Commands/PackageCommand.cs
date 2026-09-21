using DotnetInspect.Cli.Models;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticRowSelection =
    DotnetInspect.Cli.CommandLine.CliSemanticRowSelection;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using QuerySpace;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using InertText;
using Inspector.Findings;
using Markout;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Inspects a NuGet package.
/// </summary>
public partial class PackageCommand
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
        CommandContext context) =>
        await ExecuteCoreAsync(
            options,
            context,
            preResolved: null,
            admittedPackageRoot: null,
            admittedPackageManifest: null,
            admittedPackageInfoMeasurements: null,
            admittedPackageEcosystemDependencies: null,
            inspectionObserver: null,
            workspaceLoadOptions: null).ConfigureAwait(false);

    internal static async Task<int> ExecuteAsync(
        InspectionOptions options,
        CommandContext context,
        WorkspaceContextLoadOptions workspaceLoadOptions) =>
        await ExecuteCoreAsync(
            options,
            context,
            preResolved: null,
            admittedPackageRoot: null,
            admittedPackageManifest: null,
            admittedPackageInfoMeasurements: null,
            admittedPackageEcosystemDependencies: null,
            inspectionObserver: null,
            workspaceLoadOptions).ConfigureAwait(false);

    private static async Task<int> ExecuteCoreAsync(
        InspectionOptions options,
        CommandContext context,
        PackageExtractionResult? preResolved,
        PackageRootBinding? admittedPackageRoot,
        byte[]? admittedPackageManifest,
        Func<InspectionEnvelope<PackageInfoMeasurements>>?
            admittedPackageInfoMeasurements,
        Func<Task<
            InspectionEnvelope<EcosystemDependencyRecognitionOutcome>>>?
            admittedPackageEcosystemDependencies,
        Action<InspectionResult>? inspectionObserver,
        WorkspaceContextLoadOptions? workspaceLoadOptions)
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
        if (options.WorkspacePacket is null
            && options.ShareFormat is not null)
        {
            CommandError.Write(
                "--share on package requires --workspace.");
            return 1;
        }
#if DEBUG
        if (options.WorkspacePacket is null
            && options.EvidenceEnvelopePath is not null)
        {
            CommandError.Write(
                "--evidence-envelope on package requires --workspace.");
            return 1;
        }
#endif
        if (!packageLibraryMode && options.ShowDependencies)
        {
            CommandError.Write(
                "--dependencies has been removed. Use '-S \"Dependency Hierarchy\" --tree'.");
            return 1;
        }

        if (options.Roots && options.Discover is not null)
        {
            CommandError.Write(
                "--roots cannot be combined with -D/--discover.");
            return 1;
        }

        if (options.WorkspacePacket is not null
            && options.Discover is not null
            && !TryCreateWorkspacePackageRequest(options, out _))
        {
            return 1;
        }

        if (packageArgs.Length > 1
            && !ValidateMultiPackageMode(options))
        {
            return 1;
        }

        if (packageLibraryMode
            && options.Discover is not null
            && options.Schema)
        {
            if (GetLibraryInspectionModeError(
                    options,
                    allowStaticDiscovery: true) is { } modeError)
            {
                CommandError.Write(modeError);
                return 1;
            }

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
                DiscoveryOutputRequest.Create(
                    OutputFormatResolver.ResolveStored(
                        options.Format,
                        options.JsonOutput,
                        plainText: false,
                        options.Tabular,
                        options.Tsv,
                        options.Jsonl),
                    options.Tree,
                    options.TabularExplicitlySet,
                    options.NoHeader,
                    (int)options.Verbosity,
                    options),
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: sectionCatalog.SelectionCategoryMap,
                // --schema reveals the full catalog including the @Hidden pole; a static -D
                // without --schema keeps the curated top-level view.
                catalogHiddenSections: options.Schema ? null : pipeline.GetCatalogHiddenSections(),
                listedCategoryDoors: pipeline.GetListedCategoryDoors());
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
            if (packageArgs.Length > 1
                && options.IncludeSections?.Contains(
                    PackageSections.DependencyHierarchy) == true)
            {
                CommandError.Write(
                    "Multiple package inspection cannot include Dependency Hierarchy.");
                return 1;
            }

            // The alternate lens modes render their own payload and never consult the section
            // filter, so requiring -S here would force the caller to name a section that is then
            // ignored. LensProjection answers the projection for those modes instead, and -S is
            // rejected outright below rather than silently dropped.
            var lensMode = options.ListVersions || options.ListLayout || options.ListTfms
                || options.ShowContent;
            var dependencyHierarchyProjection = options.Tree
                && options.Discover == null
                && options.IncludeSections is { Count: 1 }
                && options.IncludeSections.Contains(
                    PackageSections.DependencyHierarchy);
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
                    || dependencyHierarchyProjection))
            {
                var lensName = options.ListVersions ? "--versions"
                    : options.ListLayout ? "--layout"
                    : options.ListTfms ? "--tfms"
                    : "--content";
                if (dependencyHierarchyProjection
                    && !options.SelectExplicitlySet)
                    CommandError.Write($"--tree cannot be combined with {lensName}.");
                else
                    CommandError.Write(
                        $"-S/--select is not available with {lensName}, which renders its own payload rather than sections.");
                return 1;
            }

            string? packageLens = options.ListVersions
                ? GetVersionQueryLens(options)
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
                && options.Roots)
            {
                CommandError.Write($"{packageLens} cannot be combined with --roots.");
                return 1;
            }

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

            if (!ValidateDependencyHierarchyProjection(options))
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

            var shapeCount =
                ShapeProjectionOutput.ActiveShapeCount(
                    options.Value,
                    options.Urls,
                    options.Paths)
                + (options.Roots ? 1 : 0);
            if (shapeCount > 1)
            {
                CommandError.Write(
                    "specify only one of --value, --urls, --paths, or --roots.");
                return 1;
            }

            if (shapeCount == 1)
            {
                var optionName = options.Value ? "--value"
                    : options.Urls ? "--urls"
                    : options.Paths ? "--paths"
                    : "--roots";
                // In a lens mode the shape projection is refused by LensProjection with an
                // accurate reason; demanding -S first would report a section requirement that is
                // not the actual problem.
                if (!rendersOwnPayload && !ShapeProjectionOutput.ValidateSingleSection(options.IncludeSections, optionName))
                    return 1;
                if (options.Roots
                    && options.IncludeSections is { Count: 1 } sections
                    && !sections.Contains(PackageSections.Files))
                {
                    CommandError.Write(
                        "--roots requires the Package files section.");
                    return 1;
                }
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
                CommandError.Write(
                    "--json-array requires --value, --urls, --paths, --roots, or --print.");
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
        if (!TryValidatePackageTargetFramework(options))
            return 1;

        if (GetLibraryInspectionModeError(options) is { } libraryModeError)
        {
            CommandError.Write(libraryModeError);
            return 1;
        }

        if (options.WorkspacePacket is not null)
        {
            return await ExecuteWorkspaceExactPackageAsync(
                options,
                context,
                workspaceLoadOptions).ConfigureAwait(false);
        }

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
            if (options.EnvelopeOutput
                && DotnetInspector.Networking.HttpClientFactory.IsOffline)
            {
                CommandError.Write(
                    "--envelope for package version populations requires online configured-source settlement.");
                return 1;
            }
            if (!DotnetInspector.Networking.HttpClientFactory.IsOffline)
                return await ExecuteOnlineVersionQueryAsync(packageArgs[0], options, context);

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
                    if (options.ListVersionsWithFeed)
                    {
                        var rangeFeeds =
                            await PackageExtractor.GetVersionListingsWithSourceAsync(
                                context.HttpClient,
                                range!.PackageId,
                                range.IncludesPrerelease
                                    || options.IncludePrerelease,
                                options.IncludeUnlisted,
                                limit: null,
                                logger.Log,
                                options.SourceOptions);
                        if (rangeFeeds == null)
                        {
                            WriteVersionLookupFailure(
                                range.PackageId,
                                $"Package '{range.PackageId}' not found on eligible configured sources.");
                            return 1;
                        }

                        PackageVersionVector feedVector =
                            PackageVersionVector.Create(
                                range,
                                rangeFeeds.Select(
                                    row => row.Version),
                                options.IncludePrerelease);
                        List<PackageVersionSourceInfo> rangeFeedRows =
                        [
                            .. feedVector.Addresses.SelectMany(
                                address =>
                                    rangeFeeds.Where(
                                        row => PackageVersionsEqual(
                                            row.Version,
                                            address.Version
                                                .ToNormalizedString()))),
                        ];
                        return WriteVersionFeedRows(
                            range.PackageId,
                            rangeFeedRows,
                            options);
                    }

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

                        if (options.VersionRowSelection is not null)
                            WritePartialVersionFeedWarning(range.PackageId);
                        var unlistedVector = PackageVersionVector.CreateListingAware(
                            range!, rangeListings, options.IncludePrerelease);
                        // Materialized once: counting a lazy sequence and then re-enumerating it
                        // for the render is how a count starts to disagree with its payload.
                        var rangeRows = unlistedVector.Take(options.Limit ?? int.MaxValue).ToList();
                        if (!TrySelectVersionRows(
                                rangeRows,
                                options,
                                out IReadOnlyList<PackageVersionInfo> visibleRangeRows))
                        {
                            return 1;
                        }
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
                    if (options.VersionRowSelection is not null)
                        WritePartialVersionFeedWarning(range!.PackageId);
                    var rangeVersions = vector.Addresses
                        .Take(options.Limit ?? int.MaxValue)
                        .Select(address => address.Version.ToNormalizedString())
                        .ToList();
                    if (!TrySelectVersionRows(
                            rangeVersions,
                            options,
                            out IReadOnlyList<string> visibleRangeVersions))
                    {
                        return 1;
                    }
                    if (LensProjection.TryProject(
                            options,
                            "--versions",
                            visibleRangeVersions.Count,
                            out var rangeProjectionExit,
                            ["Version"]))
                        return rangeProjectionExit;
                    WriteVersions(visibleRangeVersions, options);
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
            bool singleVersionListing =
                options.Limit == 1
                || HasSemanticSingleVersionLimit(
                    options.VersionRowSelection);
            using var requestScope = RequestTelemetry.Scope($"package {normalizedName}", "package versions");

            if (!string.IsNullOrEmpty(versionQueryPinned)
                && singleVersionListing
                && !options.ForceLatest
                && !options.ListVersionsWithFeed)
            {
                if (!options.IncludeUnlisted
                    && NuGetCache.TryGetCachedPackage(
                        normalizedName,
                        versionQueryPinned,
                        NuGetSourceResolver.ResolveSourceKeysForPackage(
                            options.SourceOptions,
                            normalizedName)) != null)
                {
                    if (!TrySelectVersionRows(
                            new[] { versionQueryPinned },
                            options,
                            out IReadOnlyList<string> visiblePinned))
                    {
                        return 1;
                    }
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
                    if (options.VersionRowSelection is not null)
                        WritePartialVersionFeedWarning(normalizedName);
                    // Either spelling renders a single version row, so the projection answers 1
                    // and returns before the render path chooses between them.
                    if (!TrySelectVersionRows(
                            new[] { pinnedMatch },
                            options,
                            out IReadOnlyList<PackageVersionInfo> visiblePinned))
                    {
                        return 1;
                    }
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
                            FeedFailurePhase.PackageSourceDiscovery
                            or FeedFailurePhase.PackageVersionList) == true)
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

            if (singleVersionListing
                && options.ForceLatest
                && !options.ListVersionsWithFeed)
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

                if (options.VersionRowSelection is not null)
                    WritePartialVersionFeedWarning(normalizedName);
                // A single resolved version is a one-row payload, so --count reports 1.
                if (!TrySelectVersionRows(
                        new[] { latest },
                        options,
                        out IReadOnlyList<string> visibleLatest))
                {
                    return 1;
                }
                if (LensProjection.TryProject(
                        options,
                        GetVersionQueryLens(options),
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
                && !options.ListVersionsWithFeed
                && DotnetInspector.Networking.HttpClientFactory.IsOffline)
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

                if (!TrySelectVersionRows(
                        singleVersions,
                        options,
                        out IReadOnlyList<string> visibleSingleVersions))
                {
                    return 1;
                }
                if (LensProjection.TryProject(
                        options,
                        "--versions",
                        visibleSingleVersions.Count,
                        out var cachedLatestExit,
                        ["Version"]))
                {
                    return cachedLatestExit;
                }

                WriteVersions(visibleSingleVersions, options);
                return 0;
            }

            if (options.ListVersionsWithFeed)
            {
                bool hasPinnedSemanticCoordinate =
                    !string.IsNullOrEmpty(versionQueryPinned)
                    && singleVersionListing
                    && !options.ForceLatest;
                var versionFeeds = await PackageExtractor.GetVersionListingsWithSourceAsync(
                    context.HttpClient,
                    normalizedName,
                    options.IncludePrerelease
                        || hasPinnedSemanticCoordinate,
                    options.IncludeUnlisted
                        || hasPinnedSemanticCoordinate,
                    limit: null,
                    logger.Log,
                    options.SourceOptions,
                    useCache: !options.ForceLatest);
                if (versionFeeds == null)
                {
                    WriteVersionLookupFailure(
                        normalizedName,
                        $"Package '{packageArgs[0]}' not found.");
                    return 1;
                }

                if (hasPinnedSemanticCoordinate
                    && versionQueryPinned is { } pinnedVersion)
                {
                    versionFeeds =
                    [
                        .. versionFeeds.Where(
                            row => PackageVersionsEqual(
                                row.Version,
                                pinnedVersion)),
                    ];
                    if (versionFeeds.Count == 0)
                    {
                        if (HasVersionSourceFailures())
                        {
                            WriteVersionLookupFailure(
                                normalizedName,
                                $"Version '{pinnedVersion}' of package '{normalizedName}' not found.");
                        }
                        else
                        {
                            CommandError.Write(
                                $"Version '{pinnedVersion}' of package '{normalizedName}' not found. Use --versions to see available versions.");
                        }

                        return 1;
                    }
                }

                if (singleVersionListing
                    && options.ForceLatest)
                {
                    PackageVersionSourceInfo? latest =
                        versionFeeds.FirstOrDefault(
                            row => row.Listed);
                    if (latest is null)
                    {
                        WriteVersionLookupFailure(
                            normalizedName,
                            $"Package '{packageArgs[0]}' not found on eligible configured sources.");
                        return 1;
                    }

                    versionFeeds =
                    [
                        .. versionFeeds.Where(
                            row => PackageVersionsEqual(
                                row.Version,
                                latest.Version)),
                    ];
                }

                return WriteVersionFeedRows(
                    normalizedName,
                    versionFeeds,
                    options);
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

                WritePartialVersionFeedWarning(normalizedName);
                if (!TrySelectVersionRows(
                        listings,
                        options,
                        out IReadOnlyList<PackageVersionInfo> visibleListings))
                {
                    return 1;
                }
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

            List<string>? versions = await PackageExtractor.GetVersionsAsync(
                context.HttpClient,
                normalizedName,
                options.IncludePrerelease,
                options.Limit,
                logger.Log,
                options.SourceOptions);

            if (versions == null)
            {
                WriteVersionLookupFailure(
                    normalizedName,
                    $"Package '{packageArgs[0]}' not found on eligible configured sources.");
                return 1;
            }

            if (!TrySelectVersionRows(
                    versions,
                    options,
                    out IReadOnlyList<string> visibleVersions))
            {
                return 1;
            }
            if (LensProjection.TryProject(
                    options,
                    "--versions",
                    visibleVersions.Count,
                    out var versionsProjectionExit,
                    ["Version"]))
                return versionsProjectionExit;

            WriteVersions(visibleVersions, options);

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

        string? extractPath = null;
        PackageExtractionResult? resolution = null;
        InspectionResult? observedInspection = null;

        if (!TryCreatePackageInfoTargetContext(
                options,
                producerOptions,
                pipeline,
                out PackageHouseTargetContext? packageInfoTargetContext))
        {
            return 1;
        }

        try
        {
            if (preResolved is null)
            {
                PackageExtractionOutcome outcome;
                if (!target.IsLocalFile && !DotnetInspector.Networking.HttpClientFactory.IsOffline)
                {
                    outcome = PackageExtractor.TryNormalizePackageVersion(version, out string pinnedVersion)
                        ? await PackageExtractor.ExtractPinnedPackageAsync(
                            client, packageName, pinnedVersion, logger.Log,
                            sourceOptions: options.SourceOptions,
                            createComposition: context.CreatePackageSourceComposition,
                            compileTargetContext: packageInfoTargetContext)
                        : await PackageExtractor.ExtractSelectedPackageAsync(
                            client, packageName, version.Length > 0 ? version : null, logger.Log,
                            sourceOptions: options.SourceOptions,
                            includePrerelease: options.IncludePrerelease,
                            createComposition: context.CreatePackageSourceComposition,
                            compileTargetContext: packageInfoTargetContext);
                }
                else
                {
                    outcome = await PackageExtractor.ExtractPackageAsync(
                        client,
                        target.IsLocalFile ? target.OriginalArgument : packageName,
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        version: target.IsLocalFile ? null : (version.Length > 0 ? version : null),
                        forceLatest: options.ForceLatest,
                        includePrerelease: options.IncludePrerelease);
                }

                if (!outcome.IsSuccess)
                {
                    CommandError.Write($"{outcome.ErrorMessage}");
                    return 1;
                }
                resolution = outcome.Result!;
            }
            else
            {
                resolution = preResolved;
            }

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

            bool wantsEcosystemDependencies =
                RequestsPackageEcosystemDependencies(
                    producerOptions,
                    pipeline);

            // Parse nuspec for full package inspection.
            NuspecData? nuspec = FindPackageNuspecForInspection(
                extractPath,
                resolution,
                wantsEcosystemDependencies);

            // Handle file content modes and exit early.
            if (options.ShowContent)
            {
                var packageId = nuspec?.PackageName ?? packageName;
                var packageVersion = nuspec?.Version ?? version;
                var packageReadme = PackageFileLister.ResolvePackageReadme(extractPath, nuspec?.ReadmeFile);
                string? declaredLicense = nuspec?.LicenseDeclaration is
                    {
                        Kind: PackageLicenseDeclarationKind.File,
                    } license
                        ? license.Value
                        : null;
                bool unaryPayload = RequiresUnaryPackageContent(options);
                PackageFileContentSet content = ReadPackageFileContents(
                    extractPath,
                    packageId,
                    packageVersion,
                    packageReadme,
                    nuspec?.ReadmeFile,
                    declaredLicense,
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
                        declaredLicense,
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
                // Authority-backed input must not be reacquired through a legacy producer key.
                return await ExecutePackageAllLibrariesAsync(
                    client,
                    extractPath,
                    target.IsLocalFile,
                    target.OriginalArgument,
                    packageName,
                    version,
                    resolution,
                    nuspec?.PackageName,
                    nuspec?.Version,
                    admittedPackageRoot,
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
                    resolution,
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
            observedInspection = result;

            if (admittedPackageInfoMeasurements is not null
                && RequestsPackageInfoMeasurements(
                    producerOptions,
                    pipeline))
            {
                ApplyPackageInfoMeasurementInspection(
                    result,
                    admittedPackageInfoMeasurements(),
                    logger.Log);
            }
            else
            {
                await ApplyPackageInfoMeasurementsAsync(
                    result,
                    resolution,
                    packageSize,
                    options.Tfm,
                    logger.Log);
            }

            if (wantsEcosystemDependencies)
            {
                bool discloseDiagnostics =
                    RequiresPackageEcosystemDiagnosticDisclosure(
                        producerOptions);
                if (admittedPackageEcosystemDependencies is not null)
                {
                    ApplyPackageEcosystemDependencies(
                        result,
                        await admittedPackageEcosystemDependencies()
                            .ConfigureAwait(false),
                        discloseDiagnostics,
                        logger.Log);
                }
                else
                {
                    await ApplyPackageEcosystemDependenciesAsync(
                        result,
                        resolution,
                        discloseDiagnostics,
                        logger.Log);
                }
            }

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

            if (!effectiveDiscovery
                && RequestsSelectedOrDiscoveredSection(
                    producerOptions,
                    PackageSections.DependencyHierarchy,
                    pipeline))
            {
                string dependencyRoot = target.IsLocalFile
                    ? resolution.NupkgPath ?? target.OriginalArgument
                    : $"{packageName}@{version}";
                result.DependencyHierarchyProjection =
                    preResolved is null
                        ? await DependsCommand
                            .AcquirePackageSubjectProjectionAsync(
                                dependencyRoot,
                                options.Tfm,
                                options.IncludePrerelease,
                                options.SourceOptions,
                                PackageDependencyQueryPlan(options),
                                context)
                        : await DependsCommand
                            .AcquireAdmittedPackageSubjectProjectionAsync(
                                packageName,
                                version,
                                admittedPackageManifest,
                                options.Tfm,
                                options.IncludePrerelease,
                                options.SourceOptions,
                                PackageDependencyQueryPlan(options),
                                context);
            }

            if (result.DependencyHierarchyProjection is { } hierarchyProjection
                && options.DependencyQueryPlan?.HierarchyRows
                    is { Operations.Count: > 0 })
            {
                if (!DependsCommand.TrySelectHierarchyRows(
                        hierarchyProjection,
                        options.DependencyQueryPlan,
                        options.Rows,
                        options.DependencyHierarchyLegacyWindowStageIndex,
                        out IReadOnlyList<
                            DependencyHierarchyOccurrenceRow>
                            selectedHierarchyRows))
                {
                    return 1;
                }

                result.DependencyHierarchyProjection =
                    hierarchyProjection with
                    {
                        HierarchyRows = [.. selectedHierarchyRows],
                    };
                options = options with
                {
                    DependencyHierarchyRowsSelected = true,
                };
            }

            static DependencyQueryPlan PackageDependencyQueryPlan(
                InspectionOptions options)
            {
                if (options.DependencyQueryPlan is { } plan)
                    return plan;

                DependencyQueryPlanResult result =
                    DependencyQuery.ResolveIntent(
                        DependencyQueryRouteKind.PackageHierarchy,
                        PortableQueryIntent.Empty);
                return ((DependencyQueryPlanResult.Accepted)result).Plan;
            }

            // Filter output based on options
            FilterResultForOutput(result, options);

            if (!TrySelectPackageFiles(
                    result,
                    options.PackageFileRowSelection))
            {
                return 1;
            }

            if (!TrySelectPackageSourceLinkFiles(
                    result,
                    options.SourceLinkFileRowSelection))
            {
                return 1;
            }

            if (!TrySelectPackageEcosystemDependencies(
                    result,
                    options.EcosystemDependencyRowSelection))
            {
                return 1;
            }

            if (options.Tree && !effectiveDiscovery)
            {
                WritePackageDependencyHierarchyTree(result, options);
                return PackageIntegrityExitCode(result);
            }

            if (wantsSignals && options.Count && !effectiveDiscovery)
            {
                await PopulatePackageSignalsAsync(
                    result, extractPath, packageName, version, client, logger, options.SourceOptions);
            }

            if (options.Count
                && result.DependencyHierarchyProjection is { } hierarchy
                && options.IncludeSections?.Contains(
                    PackageSections.DependencyHierarchy) == true
                && !DependsCommand.IsExactAssetRowSet(
                    hierarchy,
                    DependsAssetSections.DependencyHierarchy))
            {
                CommandError.Write(
                    "--count cannot report an exact 'Dependency Hierarchy' count because the requested dependency evidence is incomplete.");
                return 1;
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

            if ((options.Value || options.Urls || options.Paths || options.Roots)
                && !effectiveDiscovery)
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
                        if (resolved != null
                            && effective.Contains(resolved)
                            && !resolved.Equals(
                                PackageSections.DependencyHierarchy,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            targetSections.Add(resolved);
                        }
                    }
                    if (targetSections.Count > 0)
                    {
                        var writerOpts = new MarkoutWriterOptions { IncludeSections = targetSections };
                        var renderManifest = RenderManifestFormatter.Capture(
                            view,
                            InspectionContext.Default,
                            writerOpts,
                            schemaMap);
                        schemaMap = DiscoverOutput.FilterSchemaToRenderedItems(
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
                        DiscoveryOutputRequest.Create(
                            OutputFormatResolver.ResolveStored(
                                options.Format,
                                options.JsonOutput,
                                plainText: false,
                                options.Tabular,
                                options.Tsv,
                                options.Jsonl),
                            options.Tree,
                            options.TabularExplicitlySet,
                            options.NoHeader,
                            (int)userVerbosity,
                            options),
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
                            pipeline.GetListedCategoryDoors()),
                    result);
            }
            WarnEmptySections(result, options, pipeline);
            bool hasProjection = options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };
            if (!options.JsonOutput
                && IsSingleDependencyHierarchySelection(options)
                && (options.Tabular || hasProjection))
            {
                if (!WritePackageDependencyHierarchyProjection(
                        result,
                        options))
                {
                    return 1;
                }
                return PackageIntegrityExitCode(result);
            }
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
                    var manifest = RenderManifestFormatter.Capture(
                        view,
                        InspectionContext.Default,
                        writerOpts,
                        PackageDiscoverySchema(),
                        writerOpts.IncludeSections?.SingleOrDefault());
                    if (options.Columns is { Length: > 0 }
                        && options.Fields is { Length: > 0 })
                    {
                        var fieldOptions = options with { Columns = null };
                        MarkoutWriterOptions fieldWriterOptions =
                            OutputFormatter.BuildWriterOptions(
                                result,
                                fieldOptions,
                                pipeline);
                        fieldWriterOptions.RowWindow =
                            RowWindow.ToMarkout(options.Rows);
                        OutputFormatter.ConfigureTableWriterOptions(
                            fieldWriterOptions,
                            options.Tsv,
                            options.Jsonl);
                        manifest.MergeRenderedFieldTablesFrom(
                            RenderManifestFormatter.Capture(
                                view,
                                InspectionContext.Default,
                                fieldWriterOptions,
                                PackageDiscoverySchema(),
                                fieldWriterOptions.IncludeSections?
                                    .SingleOrDefault()));
                    }
                    string itemKind = options.Fields is not null
                        ? "field"
                        : "column";
                    ProjectionDiagnostics.DiagnoseProjected(
                        options.Fields ?? options.Columns,
                        manifest,
                        PackageDiscoverySchema(),
                        itemKind,
                        writerOpts.IncludeSections,
                        fieldSectionsAsColumns: true);
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
                {
                    var view = new InspectionResultView(
                        result,
                        includeTitleVersion: false);
                    MarkoutWriterOptions writerOptions =
                        OutputFormatter.BuildPackageDocumentWriterOptions(
                            result,
                            options,
                            pipeline);
                    var manifest = RenderManifestFormatter.Capture(
                        view,
                        InspectionContext.Default,
                        writerOptions,
                        PackageDiscoverySchema());
                    if (options.Columns is { Length: > 0 }
                        && options.Fields is { Length: > 0 })
                    {
                        MarkoutWriterOptions fieldWriterOptions =
                            OutputFormatter.BuildPackageDocumentWriterOptions(
                                result,
                                options with { Columns = null },
                                pipeline);
                        manifest.MergeRenderedFieldTablesFrom(
                            RenderManifestFormatter.Capture(
                                view,
                                InspectionContext.Default,
                                fieldWriterOptions,
                                PackageDiscoverySchema()));
                    }
                    string itemKind = options.Fields is not null
                        ? "field"
                        : "column";
                    ProjectionDiagnostics.DiagnoseProjected(
                        options.Fields ?? options.Columns,
                        manifest,
                        PackageDiscoverySchema(),
                        itemKind,
                        writerOptions.IncludeSections,
                        fieldSectionsAsColumns: true);
                }
                bool writesFile = !string.IsNullOrEmpty(options.OutputPath);
                OutputDestination.Write(
                    options.OutputPath,
                    options.Rows,
                    writer =>
                    {
                        writer.Write(output);
                        if (!writesFile)
                            writer.WriteLine();
                    });
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
            if (observedInspection is not null)
                inspectionObserver?.Invoke(observedInspection);
            PackageExtractor.Cleanup(resolution?.TempDir);
        }
    }

    private static bool TrySelectPackageSourceLinkFiles(
        InspectionResult result,
        RowSelectionIntent<string>? intent)
    {
        if (intent is null)
            return true;

        if (!SemanticRowSelection.TrySelect(
                intent,
                result.SourceFiles ?? [],
                "Package SourceLink files",
                failure =>
                    $"Package SourceLink file row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} rows are available.",
                out IReadOnlyList<PackageSourceFileInfo> selected))
        {
            return false;
        }

        result.SourceFiles = [.. selected];
        return true;
    }

    private static bool TrySelectPackageFiles(
        InspectionResult result,
        RowSelectionIntent<string>? intent)
    {
        if (intent is null)
            return true;

        if (!SemanticRowSelection.TrySelect(
                intent,
                result.Files ?? [],
                "Package files",
                failure =>
                    $"Package file row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} rows are available.",
                out IReadOnlyList<PackageFile> selected))
        {
            return false;
        }

        result.Files = [.. selected];
        return true;
    }

    private static bool TrySelectPackageEcosystemDependencies(
        InspectionResult result,
        RowSelectionIntent<string>? intent)
    {
        if (intent is null)
            return true;

        IReadOnlyList<EcosystemDependencyRecognitionEntry> rows =
            result.EcosystemDependencyRecognitionInspection?.Content switch
            {
                EcosystemDependencyRecognitionOutcome.Complete complete =>
                    complete.Document.Classification.Recognized,
                EcosystemDependencyRecognitionOutcome.Incomplete incomplete =>
                    incomplete.Document.Classification.Recognized,
                _ => [],
            };
        if (!SemanticRowSelection.TrySelect(
                intent,
                rows,
                "Package ecosystem dependencies",
                failure =>
                    $"Package ecosystem dependency row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} rows are available.",
                out IReadOnlyList<
                    EcosystemDependencyRecognitionEntry> selected))
        {
            return false;
        }

        result.EcosystemDependencyRows = selected;
        return true;
    }
}
