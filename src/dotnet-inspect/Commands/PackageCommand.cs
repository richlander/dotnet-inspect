using ILInspector.CSharp;
using DotnetInspector.Models;
using DotnetInspector.Core;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;
using System.Globalization;
using System.Text;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a NuGet package.
/// </summary>
public class PackageCommand
{
    public const string Name = "package";
    public static async Task<int> ExecuteAsync(InspectionOptions options)
    {
        var packageArgs = options.PackageArgs;
        var explicitVersion = options.ExplicitVersion;
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var sectionNames = pipeline.SelectableSectionNames;
        bool packageLibraryMode = options.PackageLibrary != null || options.AllLibraries;

        // @Hidden is a discovery-only pole. For the embedded-library render modes (which resolve
        // -S against the curated LibrarySections pipeline), reject it up front — before extracting
        // or fetching the package — so an invalid render selector never pays acquisition cost and
        // can never fan out to unbounded @Hidden members as a group.
        if (packageLibraryMode && LibraryCommand.RejectHiddenRenderSelector(options.Select))
            return 1;

        // Static discovery mode: -D --schema lists schema without resolving/loading the package.
        // Also keep no-target package discovery static because there is no target to make effective.
        if (!packageLibraryMode && options.Discover != null && (options.Schema || packageArgs.Length < 1))
        {
            var schemaMap = PackageDiscoverySchema();
            return DiscoverOutput.Execute(options.Discover, schemaMap,
                tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
                verbosity: (int)options.Verbosity,
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: pipeline.GetCategoryMap(),
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
                options.Select, sectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap(),
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
            // Discovery also renders its own payload, so it is exempt from the single-section
            // requirement below. It is deliberately not part of lensMode: unlike the lenses, -S
            // is meaningful with -D, which restricts discovery to the selected sections.
            var rendersOwnPayload = lensMode || options.Discover != null;
            // Gate on what the caller actually typed: --path and --type synthesize a selection,
            // and rejecting that would break the lens modes' normal use. The refusal is
            // unconditional rather than excusing --print: the lens prints its own document
            // without a selection, so accepting -S there would silently ignore it.
            if (lensMode && options.SelectExplicitlySet)
            {
                var lensName = options.ListVersions ? "--versions"
                    : options.ListLayout ? "--layout"
                    : options.ListTfms ? "--tfms"
                    : "--content";
                CommandError.Write(
                    $"-S/--select is not available with {lensName}, which renders its own payload rather than sections.");
                return 1;
            }

            // #3448 aligns the package gate with the library one: a count over several selected
            // sections is meaningful now that the file family is disjoint, so require a selection
            // rather than exactly one section.
            if (!rendersOwnPayload && options.Count && !CountOutput.ValidateSectionsSelected(options.IncludeSections))
                return 1;

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

            if (!OutputFormatResolver.ValidateSingleSectionForTabular(options.TabularExplicitlySet, options.IncludeSections))
                return 1;

            // Auto-promote verbosity when -S targets specific sections
            if (options.IncludeSections is { Count: > 0 })
            {
                var requiredVerbosity = pipeline.GetRequiredVerbosity(options.IncludeSections);
                if (requiredVerbosity > options.Verbosity)
                    options = options with { Verbosity = requiredVerbosity };
            }

            // Pre-render validation: check --fields/--columns names against the section schema
            if ((options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 }) && options.IncludeSections is { Count: > 0 })
            {
                var schemaMap = PackageDiscoverySchema();
                if (!ProjectionDiagnostics.ValidateProjection(schemaMap, options.IncludeSections, options.Fields, options.Columns))
                    return 1;
            }
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

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        if (packageArgs.Length > 1)
            return await ExecuteMultiPackageAsync(packageArgs, options, context, pipeline);

        // Handle --versions mode: list versions and exit early
        if (options.ListVersions)
        {
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
                            CommandError.Write($"Package '{range.PackageId}' not found on nuget.org");
                            return 1;
                        }

                        var unlistedVector = PackageVersionVector.CreateListingAware(
                            range!, rangeListings, options.IncludePrerelease);
                        // Materialized once: counting a lazy sequence and then re-enumerating it
                        // for the render is how a count starts to disagree with its payload.
                        var rangeRows = unlistedVector.Take(options.Limit ?? int.MaxValue).ToList();
                        if (LensProjection.TryProject(options, "--versions", rangeRows.Count, out var rangeListingExit))
                            return rangeListingExit;
                        OutputFormatter.WriteVersionListings(rangeRows, options.Tsv, options.Jsonl, Console.Out);
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
                    if (LensProjection.TryProject(options, "--versions", rangeVersions.Count, out var rangeProjectionExit))
                        return rangeProjectionExit;
                    OutputFormatter.WriteStringList(rangeVersions, "Version", "Version", options.Tsv, options.Jsonl, Console.Out);
                    return 0;
                }
                catch (Exception ex) when (ex is HttpRequestException
                    or IOException
                    or InvalidOperationException
                    or ArgumentException)
                {
                    CommandError.Write(ex);
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
                        NuGetSourceResolver.ResolveSourceKeys(options.SourceOptions)) != null)
                {
                    if (LensProjection.TryProject(options, "--versions", 1, out var cachedPinnedExit))
                        return cachedPinnedExit;
                    WriteSingleVersion(versionQueryPinned, options);
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
                    if (LensProjection.TryProject(options, "--versions", 1, out var knownPinnedExit))
                        return knownPinnedExit;
                    if (options.IncludeUnlisted)
                        OutputFormatter.WriteVersionListings([pinnedMatch], options.Tsv, options.Jsonl, Console.Out);
                    else
                        WriteSingleVersion(versionQueryPinned, options);
                    return 0;
                }

                if (knownVersions == null || knownVersions.Count == 0)
                    CommandError.Write($"Package '{normalizedName}' not found.");
                else
                    CommandError.Write($"Version '{versionQueryPinned}' of package '{normalizedName}' not found. Use --versions to see available versions.");
                return 1;
            }

            if (options.Limit == 1 && options.ForceLatest)
            {
                var sources = NuGetSourceResolver.ResolveSources(options.SourceOptions);
                var latest = await PackageExtractor.GetLatestVersionAsync(
                    context.HttpClient,
                    normalizedName,
                    sources,
                    logger.Log,
                    skipCache: true,
                    includePrerelease: options.IncludePrerelease);
                if (latest == null)
                {
                    CommandError.Write($"Package '{packageArgs[0]}' not found on nuget.org");
                    return 1;
                }

                // A single resolved version is a one-row payload, so --count reports 1.
                if (LensProjection.TryProject(options, "--latest-version", 1, out var latestProjectionExit))
                    return latestProjectionExit;
                if (options.IncludeUnlisted)
                {
                    // Latest resolution is listing-aware (#3388), so the version it returns is
                    // listed by construction. Emit it as a one-row listing so the flag still
                    // produces the tagged column the user asked for.
                    OutputFormatter.WriteVersionListings(
                        [new PackageVersionInfo(latest, Listed: true)], options.Tsv, options.Jsonl, Console.Out);
                    return 0;
                }

                WriteSingleVersion(latest, options);
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
                    CommandError.Write(
                        $"Package '{packageArgs[0]}' not found on nuget.org");
                    return 1;
                }

                if (LensProjection.TryProject(
                        options,
                        "--versions",
                        singleVersions.Count,
                        out var cachedLatestExit))
                {
                    return cachedLatestExit;
                }

                OutputFormatter.WriteStringList(
                    singleVersions,
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
                    CommandError.Write($"Package '{packageArgs[0]}' not found.");
                    return 1;
                }

                if (LensProjection.TryProject(options, "--versions-with-feed", versionFeeds.Count, out var feedExit))
                    return feedExit;
                OutputFormatter.WriteVersionFeedTable(versionFeeds, options, Console.Out);
                return 0;
            }

            if (options.IncludeUnlisted)
            {
                var listings = await PackageExtractor.GetVersionListingsAsync(
                    context.HttpClient, normalizedName, options.IncludePrerelease,
                    includeUnlisted: true, options.Limit, logger.Log, options.SourceOptions);
                if (listings == null)
                {
                    CommandError.Write($"Package '{packageArgs[0]}' not found on nuget.org");
                    return 1;
                }

                if (LensProjection.TryProject(options, "--versions", listings.Count, out var listingExit))
                    return listingExit;
                OutputFormatter.WriteVersionListings(listings, options.Tsv, options.Jsonl, Console.Out);
                return 0;
            }

            var versions = await PackageExtractor.GetVersionsAsync(context.HttpClient, normalizedName, options.IncludePrerelease, options.Limit, logger.Log, options.SourceOptions);
            if (versions == null)
            {
                CommandError.Write($"Package '{packageArgs[0]}' not found on nuget.org");
                return 1;
            }

            if (LensProjection.TryProject(options, "--versions", versions.Count, out var versionsProjectionExit))
                return versionsProjectionExit;

            OutputFormatter.WriteStringList(versions, "Version", "Version", options.Tsv, options.Jsonl, Console.Out);

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

        using var packageRequestScope = RequestTelemetry.Scope(
            version.Length > 0 ? $"package {packageName}@{version}" : $"package {packageName}",
            "package inspect");

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
            // Update version from resolution (may have been auto-discovered)
            version = resolution.Version ?? version;

            // Handle --layout mode: show file tree and exit early
            if (options.ListLayout)
                return ListPackageLayout(extractPath, options, packageName, options.TipLevel);

            // Handle --tfms mode: list target frameworks and exit early
            if (options.ListTfms)
                return ListPackageTfms(extractPath, options);

            // Parse nuspec (needed for the --dependencies early exit and full inspection)
            var nuspec = Services.NuspecParser.FindAndParse(extractPath);

            // Handle file content modes and exit early.
            if (options.ShowContent)
            {
                var packageId = nuspec?.PackageName ?? packageName;
                var packageVersion = nuspec?.Version ?? version;
                var packageReadme = PackageFileLister.ResolvePackageReadme(extractPath, nuspec?.ReadmeFile);
                return PrintPackageFileContents(
                    [ReadPackageFileContents(extractPath, packageId, packageVersion, packageReadme, nuspec?.ReadmeFile, options)],
                    options);
            }

            // Handle --dependencies mode: resolve transitive deps and show tree
            if (options.ShowDependencies)
            {
                CommandError.WriteLine("Tip: use 'depends --package' for dependency trees.");
                var depResult = new InspectionResult { PackageName = packageName, Version = version };
                if (nuspec != null) ApplyNuspec(nuspec, depResult);
                return await ShowDependencyTreeAsync(client, depResult, options, logger);
            }

            if (options.AllLibraries)
            {
                return await ExecutePackageAllLibrariesAsync(
                    extractPath,
                    target.IsLocalFile,
                    target.OriginalArgument,
                    packageName,
                    version,
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

            bool wantsSignals = options.IncludeSections?.Contains(PackageSections.Signals) == true
                || DiscoverRequestsSection(options.Discover, PackageSections.Signals, pipeline);
            using var vulnerabilityTrafficScope = AllowsVulnerabilityTraffic(options)
                ? NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData)
                : null;

            var result = await PackageInspector.InspectAsync(
                extractPath, packageName, version, target.IsLocalFile,
                target.IsLocalFile ? target.OriginalArgument : null,
                nuspec, client, logger, options.ForceLatest, options.Verbosity,
                resolution.NupkgPath,
                fetchMetadata: wantsSignals,
                sourceOptions: options.SourceOptions);

            // Apply package size (not cached in index — comes from nupkg file)
            if (packageSize.HasValue)
                result.PackageSize = packageSize;

            // Verify package signature if nupkg is available
            if (resolution.NupkgPath != null && (options.Verbosity >= Verbosity.Normal || wantsSignals))
            {
                logger.Log($"Verifying package signature: {Path.GetFileName(resolution.NupkgPath)}");
                result.SignatureResult = await SignatureVerifier.VerifyAsync(resolution.NupkgPath);
            }

            result.Source = target.IsLocalFile ? SourceKind.File : SourceKind.NuGet;

            PopulatePackageFileSections(result, extractPath, options);
            if (ShouldPopulatePackageSourceFiles(options))
                await PopulatePackageSourceFilesAsync(result, extractPath, packageName, version, options, context, logger);

            // Filter output based on options
            FilterResultForOutput(result, options);

            // Effective discovery renders the discovered rows below and answers the projection
            // against them. Counting here would count the package document instead, which is a
            // different payload than the one -D displays.
            if (options.Count && !effectiveDiscovery)
            {
                ProjectionAudit.MarkHonored(ProjectionAudit.Count);
                Console.WriteLine(OutputFormatter.FormatResult(result, options, pipeline));
                return 0;
            }

            if ((options.Value || options.Urls || options.Paths) && !effectiveDiscovery)
                return WritePackageShapeProjection(result, options);

            // --print joins the other payload projections rather than short-circuiting earlier:
            // it projects the rows the selected section renders, from the same view those rows
            // come from. Discovery is excluded because it renders its own payload below and
            // refuses --print with an accurate reason.
            if (options.Print && !effectiveDiscovery)
                return WritePackagePrintProjection(result, extractPath, options);

            if (options.Bare)
                return PrintPackageBareSelection(result, extractPath, packageName, version, options);

            if (wantsSignals)
            {
                result.BinarySignals = await PackageInspector.ScanBinarySignalsAsync(
                    extractPath, packageName, version, client, logger, acquirePdb: true);
            }

            if (wantsSignals)
                await AuditSignalBuilder.PopulatePackageAuditAsync(
                    result, client, logger, options.SourceOptions);

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

                return DiscoverOutput.ExecuteEffective(options.Discover, effective, schemaMap,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
                    verbosity: (int)userVerbosity, rootLabel: $"package {packageName}", fullSchema: fullSchemaMap,
                    sectionCostAnnotations: pipeline.GetCostAnnotations(),
                    sectionCategories: pipeline.GetCategoryMap(),
                    catalogHiddenSections: options.Schema ? null : pipeline.GetCatalogHiddenSections(),
                    listedCategoryDoors: pipeline.GetListedCategoryDoors(),
                    projection: options);
            }
            WarnEmptySections(result, options, pipeline);
            bool hasProjection = options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };
            if (options.Tabular)
            {
                if (options.Jsonl && TryGetSingleFileSection(options, out var fileSection) && !hasProjection)
                {
                    WritePackageFilesJsonl(result, fileSection);
                    return 0;
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
                    var view = new InspectionResultView(result);
                    var rendered = OutputFormatter.RenderTable(!options.NoHeader,
                        (writer, formatter) =>
                        {
                            OutputFormatter.ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                            MarkoutSerializer.Serialize(view, writer, formatter, InspectionContext.Default, writerOpts);
                        });
                    ProjectionDiagnostics.DiagnoseRendered(options.Fields ?? options.Columns, rendered);
                    Console.Out.Write(rendered);
                }
                else
                {
                    OutputFormatter.WritePackageTable(result, options, pipeline, showHeader: !options.NoHeader);
                }
            }
            else
            {
                var output = OutputFormatter.FormatResult(result, options, pipeline);
                if (hasProjection)
                    ProjectionDiagnostics.DiagnoseRendered(options.Fields ?? options.Columns, output);
                if (!string.IsNullOrEmpty(options.OutputPath))
                {
                    File.WriteAllText(options.OutputPath, output);
                }
                else
                {
                    Console.WriteLine(output);
                }
            }

            return 0;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            CommandError.Write($"Package '{packageName}' version '{version}' not found on nuget.org.");
            CommandError.WriteLine("Use 'dotnet-inspect package <name> --versions' to list available versions.");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            CommandError.WriteLine($"Failed to download package: {ex.Message}");
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

    private static void WriteSingleVersion(
        string version,
        InspectionOptions options)
        => OutputFormatter.WriteStringList(
            [version],
            "Version",
            "Version",
            options.Tsv,
            options.Jsonl,
            Console.Out);

    private static async Task<int> ExecuteMultiPackageAsync(
        string[] packageArgs,
        InspectionOptions options,
        CommandContext context,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (!ValidateMultiPackageMode(options))
            return 1;

        if (options.ShowContent)
            return await ExecuteMultiPackageContentAsync(packageArgs, options, context);

        if (!TryResolveMultiPackageRowSection(options, out var rowSection))
            return 1;
        bool wantsFilesSection = HasPathFilter(options)
            || IsPackageFileSection(rowSection)
            || options.IncludeSections?.Any(IsPackageFileSection) == true
            || SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        if (!options.JsonOutput && rowSection == null)
        {
            CommandError.Write("Multiple package output requires --json or a row format such as --table, --tsv, or --jsonl.");
            CommandError.WriteLine("For package surveys, try: dotnet-inspect package <pkg>... --path @readme --tsv");
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
            var result = await InspectPackageAsync(target, options, context, wantsFilesSection);
            if (result == null)
                return 1;
            FilterResultForOutput(result, options);
            results.Add(result);
        }

        if (options.Count)
        {
            CountOutput.WriteCount(CountMultiPackageRows(results, rowSection, options));
            return 0;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(results.ToArray(), JsonContext.Default.InspectionResultArray));
            return 0;
        }

        WriteMultiPackageTable(results, rowSection!, options);
        return 0;
    }

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
        if (section.Equals(PackageSections.PackageInfo, StringComparison.OrdinalIgnoreCase)
            || IsPackageFileSection(section))
        {
            return true;
        }

        CommandError.Write($"Multiple package row output does not support section: {section}.");
        CommandError.WriteLine("Use --json, or select Package Info, Package files, or a package file section (see -D @Files).");
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

    private static readonly string[] PackageSignalsColumnNames =
    [
        "Area",
        "Signal",
        "Value",
        "Evidence"
    ];

    private static DocumentSchema PackageDiscoverySchema()
        => AddPackageDynamicDiscoveryItems(InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema());

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

    private static bool DiscoverRequestsSection(string[]? discover, string sectionName, SectionPipeline<InspectionResult> pipeline)
    {
        if (discover is not { Length: > 0 })
            return false;

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

    private static bool ValidateMultiPackageMode(InspectionOptions options)
    {
        List<string> conflicts = [];
        if (options.ExplicitVersion != null) conflicts.Add("--version");
        if (options.ListVersions) conflicts.Add("--versions/--version/--latest-version");
        if (options.ListLayout) conflicts.Add("--layout");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.Print) conflicts.Add("--print");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
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
        if (options.Tree && options.Discover == null)
        {
            CommandError.Write("package --tree is discovery-tree output and requires -D/--discover.");
            CommandError.WriteLine("Use --layout to show the package file tree.");
            return false;
        }

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
            new ShapeProjectionOptions(kind, options.PrintRow, options.JsonOutput, options.Jsonl, options.JsonArray));
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
        var view = new InspectionResultView(result);
        List<PackageFileRow>? rows = section switch
        {
            PackageSections.FilesNuspec => view.NuspecFiles,
            PackageSections.FilesReadme => view.PackageReadme,
            PackageSections.FilesSkills => view.SkillFiles,
            // ValidatePackagePrintSelection admits only the three sections above, so reaching
            // here means the two lists disagree; say so rather than answering as if empty.
            _ => throw new InvalidOperationException($"'{section}' is not a printable section.")
        };

        // A family with no rows and a file listing that was never collected are different facts.
        // Reporting the second as the first would tell the caller this package ships no such
        // document when the truth is that nothing ever looked.
        if (rows is null)
        {
            CommandError.Write(
                $"the package file listing was not collected, so '{section}' cannot be printed.");
            return 1;
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
        if (options.ContentScope != PackageFileContentScope.Full
            && rows.FirstOrDefault(row => !IsMarkdownDocument(row.Path, isReadmeSection)) is { } nonMarkdown)
        {
            CommandError.Write(
                $"--frontmatter/--yaml-header and --body apply to Markdown documents; '{nonMarkdown.Path}' is not Markdown.");
            return 1;
        }

        // Row identity is metadata, so the selection is resolved before any document is read and
        // the payload of exactly one row is acquired -- one --print authorizes one fetch.
        var printableRows = new List<PrintableRow>(rows.Count);
        var sizeByPath = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            printableRows.Add(new PrintableRow(i + 1, section, rows[i].Path, rows[i].Path, null));
            sizeByPath[rows[i].Path] = rows[i].Size;
        }

        return PrintProjectionOutput.Write(
            printableRows,
            row => ReadPackageFileContent(
                extractPath,
                result.PackageName ?? string.Empty,
                result.Version ?? string.Empty,
                new PackageFile(row.Path!, sizeByPath[row.Path!], IsReadme: isReadmeSection),
                options.ContentScope,
                normalizeGithubLinksToRaw: !options.BrowsableUrls).Content,
            new PrintProjectionOptions(
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                options.OutputPath));
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

        string? value = field.ToLowerInvariant() switch
        {
            "version" => result.Version,
            "readme" => result.PackageReadmeFile,
            "repository" => result.Repository,
            "repository commit" or "repository_commit" => result.RepositoryCommit,
            "repository type" or "repository_type" => result.RepositoryType,
            "license" => result.License,
            "license url" or "license_url" => result.LicenseUrl,
            "source" => result.Source,
            "type" => result.PackageTypes is { Count: > 0 } ? string.Join(", ", result.PackageTypes) : null,
            "signed" => result.SignatureResult is null
                ? null
                : result.SignatureResult.IsUnsigned ? "Unsigned"
                    : result.SignatureResult.AuthorVerified || result.SignatureResult.RepositoryVerified ? "Verified"
                    : result.SignatureResult.StatusMessage,
            "size" => result.PackageSize?.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value)
            ? []
            : [new ShapeProjectionRow(1, section, value, Label: field)];
    }

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

        var results = new List<PackageFileContentSet>();
        foreach (var target in targets)
        {
            var result = await ReadPackageFileContentsAsync(target, options, context);
            if (result == null)
                return 1;
            results.Add(result);
        }

        return PrintPackageFileContents(results, options);
    }

    private static async Task<PackageFileContentSet?> ReadPackageFileContentsAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        CommandContext context)
    {
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

            var nuspec = Services.NuspecParser.FindAndParse(extractPath);

            var packageId = nuspec?.PackageName ?? target.PackageName;
            var packageVersion = nuspec?.Version ?? version;
            var packageReadme = PackageFileLister.ResolvePackageReadme(extractPath, nuspec?.ReadmeFile);
            return ReadPackageFileContents(extractPath, packageId, packageVersion, packageReadme, nuspec?.ReadmeFile, options);
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

    private static async Task<InspectionResult?> InspectPackageAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        CommandContext context,
        bool wantsFilesSection)
    {
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

            var nuspec = Services.NuspecParser.FindAndParse(extractPath);

            long? packageSize = null;
            if (resolution.NupkgPath != null && File.Exists(resolution.NupkgPath))
                packageSize = new FileInfo(resolution.NupkgPath).Length;

            bool wantsSignals = options.IncludeSections?.Contains(PackageSections.Signals) == true
                || DiscoverRequestsSection(options.Discover, PackageSections.Signals, PackageSectionDescriptors.CreatePipeline());
            using var vulnerabilityTrafficScope = AllowsVulnerabilityTraffic(options)
                ? NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData)
                : null;
            var result = await PackageInspector.InspectAsync(
                extractPath,
                target.PackageName,
                version,
                target.IsLocalFile,
                target.IsLocalFile ? target.OriginalArgument : null,
                nuspec,
                context.HttpClient,
                logger,
                options.ForceLatest,
                options.Verbosity,
                resolution.NupkgPath,
                fetchMetadata: wantsSignals,
                sourceOptions: options.SourceOptions);

            if (packageSize.HasValue)
                result.PackageSize = packageSize;

            result.Source = target.IsLocalFile ? SourceKind.File : SourceKind.NuGet;

            if (wantsFilesSection)
                PopulatePackageFileSections(result, extractPath, options);

            if (wantsSignals)
            {
                result.BinarySignals = await PackageInspector.ScanBinarySignalsAsync(
                    extractPath, target.PackageName, version, context.HttpClient, logger, acquirePdb: true);
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

    private static async Task PopulatePackageSourceFilesAsync(
        InspectionResult result,
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options,
        CommandContext context,
        VerboseLogger logger)
    {
        result.SourceFiles = [];

        var libraries = SelectPackageLibrariesForSourceFiles(extractPath, options);
        foreach (var libraryPath in libraries)
        {
            var relativePath = Path.GetRelativePath(extractPath, libraryPath).Replace('\\', '/');
            var rows = await SourceFileCollector.CollectFromAssemblyAsync(
                libraryPath,
                packageName,
                version,
                isPlatformAssembly: false,
                logger,
                context.HttpClient,
                browsableUrls: options.BrowsableUrls,
                typeFilter: options.TypeFilter);
            result.SourceFiles.AddRange(rows.Select(row => new PackageSourceFileInfo(
                relativePath,
                row.Type,
                row.Url)));
        }
    }

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

    private static PackageFileContentSet ReadPackageFileContents(
        string extractPath,
        string packageName,
        string version,
        string? readmeFile,
        string? declaredReadmeFile,
        InspectionOptions options)
    {
        var files = PackageFileLister.ListAll(extractPath, readmeFile);
        var selectedFiles = FilterPackageFiles(files, options);
        var contents = selectedFiles
            .Select(file => ReadPackageFileContent(
                extractPath,
                packageName,
                version,
                WithDeclaredReadmeRole(file, declaredReadmeFile),
                options.ContentScope,
                normalizeGithubLinksToRaw: !options.BrowsableUrls))
            .ToList();
        return new PackageFileContentSet(packageName, version, contents);
    }

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
        bool normalizeGithubLinksToRaw)
    {
        var fullPath = Path.Combine(extractPath, file.Path.Replace('/', Path.DirectorySeparatorChar));

        // Scoping and link rewriting are Markdown conventions. Applied to anything else they
        // corrupt the document the package shipped rather than presenting it, and the caller
        // has no way to see that it happened. So Markdown documents are presented, and every
        // other kind is passed through exactly as shipped -- including its byte order mark,
        // which ReadAllText would otherwise consume and silently shorten the document by.
        if (!IsMarkdownDocument(file.Path, file.IsReadme))
            return new PackageFileContent(packageName, version, file.Path, file.Size, Found: true, ReadTextPreservingPreamble(fullPath), file.IsReadme);

        var content = MarkdownContent.ApplyScope(File.ReadAllText(fullPath), scope);
        if (normalizeGithubLinksToRaw)
            content = GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(content);

        return new PackageFileContent(packageName, version, file.Path, file.Size, Found: true, content, file.IsReadme);
    }

    /// <summary>
    /// Reads text while keeping any byte order mark the file starts with. Decoding still detects
    /// the encoding from that mark, so the text is decoded correctly; the mark is then restored
    /// as a character so a verbatim document round-trips through the text pipeline with the same
    /// bytes it shipped with rather than three fewer.
    /// </summary>
    private static string ReadTextPreservingPreamble(string fullPath)
    {
        var content = File.ReadAllText(fullPath);

        Span<byte> head = stackalloc byte[3];
        using var stream = File.OpenRead(fullPath);
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var hasUtf8Preamble = read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;

        return hasUtf8Preamble ? '\uFEFF' + content : content;
    }

    private static int PrintPackageFileContents(IReadOnlyList<PackageFileContentSet> results, InspectionOptions options)
    {
        var rows = FlattenPackageFileContentRows(results, options).ToList();

        // Same rule the print projection applies: a Markdown scope names a Markdown construct,
        // and non-Markdown documents are passed through verbatim. Without this the scope would
        // be accepted and then silently ignored, answering a --frontmatter request with the
        // whole document -- a projection answered from a different payload than the one asked
        // for, which is the defect class this command is being kept clear of.
        if (options.ContentScope != PackageFileContentScope.Full
            && rows.FirstOrDefault(row => row.Found && !IsMarkdownDocument(row.Path, row.IsReadme)) is { } nonMarkdown)
        {
            CommandError.Write(
                $"--frontmatter/--yaml-header and --body apply to Markdown documents; '{nonMarkdown.Path}' is not Markdown. "
                + "Narrow the selection to Markdown, for example --path \"*.md\".");
            return 1;
        }

        // A path that matches nothing still yields one row so the render can show it as absent.
        // Counting that row would answer "one file matched" when none did, so count found files,
        // as the bare writer below already does.
        if (LensProjection.TryProject(options, "--content", rows.Count(row => row.Found), out var contentProjectionExit))
            return contentProjectionExit;

        if (options.Bare)
            return PrintBarePackageFileContentRows(rows, options.OutputPath);

        var output = options.Jsonl
            ? RenderPackageFileContentJsonl(rows)
            : RenderPackageFileContentBlocks(rows);

        if (!string.IsNullOrEmpty(options.OutputPath))
            File.WriteAllText(options.OutputPath, output);
        else
            Console.Write(output);

        return 0;
    }

    private static int PrintBarePackageFileContentRows(IReadOnlyList<PackageFileContent> rows, string? outputPath)
    {
        var found = rows.Where(row => row.Found).ToList();
        if (found.Count != 1)
        {
            CommandError.Write(found.Count == 0
                ? "--bare found no selected package content."
                : $"--bare requires exactly one selected package content file; found {found.Count}.");
            return 1;
        }

        return WriteBarePackageText(found[0].Content, outputPath);
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

    private static string RenderPackageFileContentJsonl(IReadOnlyList<PackageFileContent> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder
                .Append(JsonSerializer.Serialize(row, PackageFileContentJsonContext.Default.PackageFileContent))
                .Append('\n');
        return builder.ToString();
    }

    private static string RenderPackageFileContentBlocks(IReadOnlyList<PackageFileContent> rows)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                builder.AppendLine();

            var row = rows[i];
            var path = row.Found ? row.Path : "<absent>";
            // The separator is tool-owned framing, so its untrusted parts are
            // contained even though the file content below it is deliberately
            // raw -- otherwise a ZIP entry path forges a second separator.
            builder.AppendLine(CSharpIdentifier.ContainRenderedText(
                $"------------ {row.Package} :: {path} ------------"));
            if (!row.Found)
            {
                builder.AppendLine("(absent)");
                continue;
            }

            builder.Append(row.Content);
            if (row.Content.Length == 0 || row.Content[^1] != '\n')
                builder.AppendLine();
        }

        return builder.ToString();
    }

    private static int CountMultiPackageRows(IReadOnlyList<InspectionResult> results, string? section, InspectionOptions options)
        => IsPackageFileSection(section)
            ? results.Sum(result => options.SkipEmpty ? GetPackageFileRows(result, section!).Count : Math.Max(1, GetPackageFileRows(result, section!).Count))
            : results.Sum(result => new InspectionResultView(result).Metadata.Count);

    private static void WriteMultiPackageTable(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        if (IsPackageFileSection(section))
        {
            WriteMultiPackageFilesTable(results, section, options);
            return;
        }

        WriteMultiPackagePackageInfoTable(results, options);
    }

    private static void WriteMultiPackageFilesTable(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        if (options.Jsonl)
        {
            WriteMultiPackageFilesJsonl(results, section, options);
            return;
        }

        var rows = results
            .SelectMany(result =>
            {
                var files = GetPackageFileRows(result, section);
                if (files.Count == 0)
                    return options.SkipEmpty ? [] : [[result.PackageName, result.Version, "", ""]];

                return files
                    .Select(file => new[]
                    {
                        result.PackageName,
                        result.Version,
                        file.Path,
                        file.Size.ToString(CultureInfo.InvariantCulture),
                    });
            })
            .ToArray();

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(
                ["Package", "Version", "Path", "Size"],
                ["package", "version", "path", "size"],
                rows);
            markoutWriter.Flush();
        }, options.Rows);
    }

    private static void WritePackageFilesJsonl(InspectionResult result, string section)
    {
        var files = GetPackageFileRows(result, section);
        if (files.Count == 0)
            return;

        foreach (var file in files)
        {
            var row = new PackageFileJsonRow(file.Path, file.Size);
            Console.WriteLine(JsonSerializer.Serialize(row, PackageFileJsonRowContext.Default.PackageFileJsonRow));
        }
    }

    private static void WriteMultiPackageFilesJsonl(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        foreach (var result in results)
        {
            var files = GetPackageFileRows(result, section);
            if (files.Count == 0)
            {
                if (!options.SkipEmpty)
                {
                    var empty = new PackageFileMultiJsonRow(result.PackageName, result.Version, "", null);
                    Console.WriteLine(JsonSerializer.Serialize(empty, PackageFileMultiJsonRowContext.Default.PackageFileMultiJsonRow));
                }
                continue;
            }

            foreach (var file in files)
            {
                var row = new PackageFileMultiJsonRow(result.PackageName, result.Version, file.Path, file.Size);
                Console.WriteLine(JsonSerializer.Serialize(row, PackageFileMultiJsonRowContext.Default.PackageFileMultiJsonRow));
            }
        }
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
            var files = GetPackageFileRows(result, section);
            return PrintBarePackageFiles(extractPath, packageName, version, files, options, section);
        }

        if (section.Equals(PackageSections.SourceLinkFiles, StringComparison.OrdinalIgnoreCase))
        {
            var urls = result.SourceFiles?.Select(row => row.Url);
            return PrintBarePackageUrlColumn(urls, section, options.OutputPath);
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

        var content = ReadPackageFileContent(
            extractPath,
            packageName,
            version,
            files[0],
            PackageFileContentScope.Full,
            normalizeGithubLinksToRaw: !options.BrowsableUrls);
        return WriteBarePackageText(content.Content, options.OutputPath);
    }

    private static int PrintBarePackageUrlColumn(IEnumerable<string?>? urls, string section, string? outputPath)
    {
        var values = urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToList() ?? [];

        if (values.Count > 0)
            return WriteBarePackageText(string.Join('\n', values), outputPath);

        CommandError.Write($"--bare found no URL in section '{section}'.");
        return 1;
    }

    private static int WriteBarePackageText(string content, string? outputPath)
    {
        var output = content.EndsWith('\n') ? content : content + '\n';
        if (!string.IsNullOrEmpty(outputPath))
            File.WriteAllText(outputPath, output);
        else
            Console.Write(output);
        return 0;
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

    private static void WriteMultiPackagePackageInfoTable(IReadOnlyList<InspectionResult> results, InspectionOptions options)
    {
        var rows = results
            .SelectMany(result => new InspectionResultView(result).Metadata
                .Select(field => new[]
                {
                    result.PackageName,
                    field.Key,
                    field.Value?.ToString() ?? "",
                }))
            .ToArray();

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(
                ["Package", "Field", "Value"],
                ["package", "field", "value"],
                rows);
            markoutWriter.Flush();
        }, options.Rows);
    }

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
        || section.Equals(PackageSections.Statistics, StringComparison.OrdinalIgnoreCase)
        || section.Equals(PackageSections.Vulnerabilities, StringComparison.OrdinalIgnoreCase);

    private static bool AllowsVulnerabilityTraffic(InspectionOptions options) =>
        options.Verbosity >= Verbosity.Detailed
        || options.IncludeSections?.Any(IsNetworkUsingPackageSection) == true;

    private static bool ValidatePackageLibraryMode(InspectionOptions options)
    {
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

        var pipeline = LibrarySections.CreatePipeline();
        var scannerRegistry = LibrarySections.CreateScannerRegistry();
        var libraryOptions = CreateLibraryOptions(assemblyName: null, packageReference, options);

        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap(),
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
            libraryOptions = libraryOptions with { IncludeSections = selectResult.Sections };

        if (libraryOptions.Count && !CountOutput.ValidateSingleSection(libraryOptions.IncludeSections))
            return 1;

        var requiredVerbosity = pipeline.GetRequiredVerbosity(libraryOptions.IncludeSections);
        if (requiredVerbosity > libraryOptions.Verbosity)
            libraryOptions = libraryOptions with { Verbosity = requiredVerbosity };

        var scanners = pipeline.GetRequiredScanners(libraryOptions.Verbosity, libraryOptions.IncludeSections);
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        List<LibraryInspection> inspections = [];
        foreach (var selection in selected)
        {
            var inspection = await LibraryMetadataService.InspectAsync(
                selection.Path,
                libraryOptions,
                logger,
                packageName,
                version,
                context.HttpClient,
                scanners: scanners,
                scannerRegistry: scannerRegistry);
            if (inspection == null)
            {
                logger.LogWarning($"Could not read library: {Path.GetFileName(selection.Path)}");
                continue;
            }

            var relativePath = Path.GetRelativePath(extractPath, selection.Path).Replace('\\', '/');
            inspection.FileName = relativePath;
            inspection.Tfm = TfmResolver.ExtractTfmFromPath(relativePath);
            inspection.Source = SourceKind.NuGet;
            inspections.Add(inspection);
        }

        if (inspections.Count == 0)
        {
            CommandError.Write($"No libraries could be read from package '{packageName}'.");
            return 1;
        }

        if (libraryOptions.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspections.ToArray(), JsonContext.Default.LibraryInspectionArray));
            return 0;
        }

        var sections = GetAllLibrariesSections(inspections, libraryOptions, pipeline);
        if (sections.Count == 0)
        {
            CommandError.WriteNote("matched sections have no data across all libraries.");
            // An empty match is still an answer to --count, and returning without projecting
            // would report the absence as unprojected output.
            if (libraryOptions.Count)
                CountOutput.WriteCount(0);
            return 0;
        }

        if (libraryOptions.TabularExplicitlySet)
        {
            if (!WriteAllLibrariesTable(packageName, version, inspections, sections, libraryOptions))
                return 1;
            return 0;
        }

        var markdown = RenderAllLibrariesMarkdown(packageName, version, inspections, sections, libraryOptions, pipeline);
        if (libraryOptions.Count)
            CountOutput.WriteCountFromMarkdown(markdown);
        else
            Console.WriteLine(markdown);
        return 0;
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
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            TabularExplicitlySet = options.TabularExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            Format = options.JsonOutput ? OutputFormat.Json
                : options.Jsonl ? OutputFormat.Jsonl
                : options.Tsv ? OutputFormat.Tsv
                : options.Tabular ? OutputFormat.Table
                : OutputFormat.Markdown,
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

        if (resolution.Tfm != null && string.IsNullOrWhiteSpace(options.Tfm))
            CommandError.WriteLine($"Using TFM: {resolution.Tfm}");

        return resolution.Paths
            .Select(path => new PackageLibrarySelection(path))
            .ToList();
    }

    private sealed record PackageLibrarySelection(string Path);

    private static List<string> GetAllLibrariesSections(
        List<LibraryInspection> inspections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        List<string> union = [];
        foreach (var inspection in inspections)
        {
            var sections = selectAll
                ? pipeline.GetAllSelectorSections(inspection)
                : pipeline.GetEffectiveSections(inspection, options.Verbosity, options.IncludeSections);
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
        if (options.Select?.Any(value => value.StartsWith("@", StringComparison.Ordinal)) == true)
        {
            CommandError.Write($"--all-libraries row output requires one concrete section; category selectors such as {SectionCategoryNames.Integrations} produce multi-section documents.");
            CommandError.WriteLine("Use Markdown output for categories, or select a section such as \"Integration: Configuration\" or Library Info.");
            return false;
        }

        if (sections.Count != 1)
        {
            CommandError.Write($"--all-libraries row output requires exactly one section; matched {sections.Count}: {string.Join(", ", sections)}.");
            CommandError.WriteLine("Use Markdown output for multi-section selections, or select one concrete section.");
            return false;
        }

        var table = BuildAllLibrariesTable(packageName, version, inspections, sections[0]);
        if (table == null)
        {
            CommandError.Write($"--all-libraries row output does not support section: {sections[0]}.");
            CommandError.WriteLine("Use Markdown output, or select Library Info, Switches, Integration: Opportunities, or a focused Integration: section.");
            return false;
        }

        if (table.Rows.Length == 0)
        {
            CommandError.WriteNote("matched section has no row data across all libraries.");
            return true;
        }

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(table.Headers, table.StableHeaders, table.Rows);
            markoutWriter.Flush();
        }, options.Rows);
        return true;
    }

    private sealed record AllLibrariesTable(string[] Headers, string[] StableHeaders, string[][] Rows);

    private static AllLibrariesTable? BuildAllLibrariesTable(
        string packageName,
        string version,
        List<LibraryInspection> inspections,
        string section)
    {
        if (section.Equals("Library Info", StringComparison.OrdinalIgnoreCase))
        {
            var libraryInfoRows = inspections
                .SelectMany(inspection => BuildLibraryInfoRows(packageName, version, inspection))
                .ToArray();
            return new(
                ["Package", "Version", "Library", "TFM", "Field", "Value"],
                ["package", "version", "library", "tfm", "field", "value"],
                libraryInfoRows);
        }

        if (section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase))
        {
            var opportunityRows = inspections
                .SelectMany(inspection => (inspection.IntegrationOpportunities ?? [])
                    .Select(opportunity => WithProvenance(
                        packageName,
                        version,
                        inspection,
                        opportunity.Integration,
                        opportunity.Api,
                        opportunity.IntegrationType,
                        opportunity.LookFor)))
                .ToArray();
            return new(
                ["Package", "Version", "Library", "TFM", "Integration", "API", "Integration Type", "Look For"],
                ["package", "version", "library", "tfm", "integration", "api", "integration_type", "look_for"],
                opportunityRows);
        }

        if (section.Equals("Switches", StringComparison.OrdinalIgnoreCase))
        {
            var switchRows = inspections
                .SelectMany(inspection => inspection.SwitchInspection.PayloadsForRendering()
                    .Select(switchInfo => WithProvenance(
                        packageName,
                        version,
                        inspection,
                        switchInfo.Kind,
                        switchInfo.Switch,
                        switchInfo.Api)))
                .ToArray();
            return new(
                ["Package", "Version", "Library", "TFM", "Kind", "Switch", "API"],
                ["package", "version", "library", "tfm", "kind", "switch", "api"],
                switchRows);
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
        var valueColumn = hasApis ? "API" : "Type";
        var valueStableColumn = hasApis ? "api" : "type";
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
            ["Package", "Version", "Library", "TFM", "Kind", valueColumn],
            ["package", "version", "library", "tfm", "kind", valueStableColumn],
            focusedRows);
    }

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
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        foreach (var section in sections)
        {
            if (IsAggregatedAllLibrariesSection(section))
                AppendAggregatedSection(sb, section, inspections);
            else
                AppendPerLibrarySections(sb, section, inspections, options, pipeline);
        }

        var markdown = sb.ToString().TrimEnd();
        return MarkdownTableRowLimiter.Apply(markdown, options.Rows);
    }

    private static bool IsAggregatedAllLibrariesSection(string section)
        => section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase)
           || section.Equals("Switches", StringComparison.OrdinalIgnoreCase)
           || LibraryIntegrationCatalog.All.Any(descriptor => descriptor.SectionName.Equals(section, StringComparison.OrdinalIgnoreCase));

    private static void AppendAggregatedSection(StringBuilder sb, string section, List<LibraryInspection> inspections)
    {
        if (section.Equals(IntegrationSectionNames.Opportunities, StringComparison.OrdinalIgnoreCase))
        {
            var opportunityRows = inspections
                .SelectMany(inspection => (inspection.IntegrationOpportunities ?? [])
                    .Select(row => new
                    {
                        Library = inspection.FileName,
                        row.Integration,
                        Api = CodeCell(row.Api),
                        row.IntegrationType,
                        row.LookFor
                    }))
                .OrderBy(row => row.Integration, StringComparer.Ordinal)
                .ThenBy(row => row.Api, StringComparer.Ordinal)
                .ToList();
            if (opportunityRows.Count == 0)
                return;

            AppendHeading(sb, section);
            var includeLibrary = opportunityRows.Select(row => row.Library).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            AppendTable(sb,
                includeLibrary ? ["Library", "Integration", "API", "Integration Type", "Look For"] : ["Integration", "API", "Integration Type", "Look For"],
                opportunityRows.Select(row => includeLibrary
                    ? new[] { CodeCell(row.Library), row.Integration, row.Api, row.IntegrationType, row.LookFor }
                    : [row.Integration, row.Api, row.IntegrationType, row.LookFor]));
            return;
        }

        if (section.Equals("Switches", StringComparison.OrdinalIgnoreCase))
        {
            var switchRows = inspections
                .SelectMany(inspection => inspection.SwitchInspection.PayloadsForRendering()
                    .Select(row => new
                    {
                        Library = inspection.FileName,
                        row.Kind,
                        Switch = CodeCell(row.Switch),
                        Api = CodeCell(row.Api)
                    }))
                .OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Switch, StringComparer.Ordinal)
                .ToList();
            if (switchRows.Count == 0)
                return;

            AppendHeading(sb, section);
            var includeLibrary = switchRows.Select(row => row.Library).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            AppendTable(sb,
                includeLibrary ? ["Library", "Kind", "Switch", "API"] : ["Kind", "Switch", "API"],
                switchRows.Select(row => includeLibrary
                    ? new[] { CodeCell(row.Library), row.Kind, row.Switch, row.Api }
                    : [row.Kind, row.Switch, row.Api]));
            return;
        }

        var descriptor = LibraryIntegrationCatalog.All.FirstOrDefault(d =>
            d.SectionName.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
            return;

        var signals = inspections
            .SelectMany(inspection => descriptor.GetSignals(inspection)
                .Select(signal => new { Library = inspection.FileName, Signal = signal }))
            .ToList();
        if (signals.Count == 0)
            return;

        var hasApis = signals.Any(row => row.Signal.Shape == IntegrationSignalShape.Api);
        var includeTypes = descriptor.IncludeTypesWhenApisPresent;
        var focusedRows = signals
            .Where(row => !hasApis || includeTypes || row.Signal.Shape == IntegrationSignalShape.Api)
            .OrderBy(row => row.Signal.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Signal.Name, StringComparer.Ordinal)
            .ToList();
        if (focusedRows.Count == 0)
            return;

        AppendHeading(sb, section);
        var includeLibraryColumn = focusedRows.Select(row => row.Library).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var includeKindColumn = focusedRows.Select(row => row.Signal.Kind).Distinct(StringComparer.Ordinal).Count() > 1;
        var valueColumn = hasApis ? "API" : "Type";

        List<string> headers = [];
        if (includeLibraryColumn) headers.Add("Library");
        if (includeKindColumn) headers.Add("Kind");
        headers.Add(valueColumn);

        AppendTable(sb, headers, focusedRows.Select(row =>
        {
            List<string> values = [];
            if (includeLibraryColumn) values.Add(CodeCell(row.Library));
            if (includeKindColumn) values.Add(row.Signal.Kind);
            values.Add(CodeCell(row.Signal.Name));
            return values.ToArray();
        }));
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
            if (!pipeline.GetEffectiveSections(inspection, options.Verbosity, options.IncludeSections).Contains(section, StringComparer.OrdinalIgnoreCase))
                continue;

            var rendered = RenderLibrarySection(inspection, section, options);
            if (rendered.Length == 0)
                continue;

            if (sb.Length > 0 && !sb.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                sb.AppendLine();
            sb.AppendLine(rendered);
            sb.AppendLine();
        }
    }

    private static string RenderLibrarySection(LibraryInspection inspection, string section, LibraryOptions options)
    {
        var view = new LibraryInspectionView(inspection);
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = [section],
            Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
        };
        var markdown = MarkoutSerializer.Serialize(view, InspectionContext.Default, writerOptions).Trim();
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

    private static void AppendHeading(StringBuilder sb, string section)
    {
        if (sb.Length > 0 && !sb.ToString().EndsWith("\n\n", StringComparison.Ordinal))
            sb.AppendLine();
        sb.AppendLine($"## {section}");
        sb.AppendLine();
    }

    private static void AppendTable(StringBuilder sb, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        sb.AppendLine($"| {string.Join(" | ", headers.Select(EscapeMarkdownCell))} |");
        sb.AppendLine($"| {string.Join(" | ", headers.Select(_ => "---"))} |");
        foreach (var row in rows)
            sb.AppendLine($"| {string.Join(" | ", row.Select(EscapeMarkdownCell))} |");
        sb.AppendLine();
    }

    private static string EscapeMarkdownCell(string value)
        => value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "&#124;", StringComparison.Ordinal);

    private static string CodeCell(string value)
        => $"`{value.Replace("`", "\\`", StringComparison.Ordinal)}`";

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

        if (LensProjection.TryProject(options, "--layout", results.Count, out var projectionExitCode))
            return projectionExitCode;

        PackageOutputFormatter.WriteFileTree(results);
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

        if (LensProjection.TryProject(options, "--tfms", tfms.Count, out var projectionExit))
            return projectionExit;

        OutputFormatter.WriteStringList(tfms, "TFM", "Tfm", options.Tsv, options.Jsonl, Console.Out);
        return 0;
    }

    private static async Task<int> ShowDependencyTreeAsync(
        HttpClient client, InspectionResult result, InspectionOptions options, VerboseLogger logger)
    {
        var selection = DependencyResolutionService.SelectDependencyGroup(
            result.DependencyGroups,
            options.Tfm,
            allowCompatibleFallbackForRequestedTfm: false);
        if (selection.Status == DependencyResolutionService.DependencyGroupSelectionStatus.NoDependencyGroups)
        {
            CommandError.WriteLine("No dependencies declared in package.");
            return 0;
        }

        if (selection.Status == DependencyResolutionService.DependencyGroupSelectionStatus.NoMatchingTargetFramework)
        {
            CommandError.Write($"No dependencies found for TFM '{selection.TargetFramework}'.");
            CommandError.WriteLine("Available TFMs: " + string.Join(", ", selection.AvailableTargetFrameworks));
            return 1;
        }

        var group = selection.Group!;
        var tfm = selection.TargetFramework ?? group.TargetFramework;

        if (group.Dependencies.Count == 0)
        {
            var emptyView = new EmptyDepsView
            {
                Title = $"{result.PackageName} ({result.Version})",
                Description = $"No additional dependencies for {tfm}."
            };
            Console.WriteLine(MarkoutSerializer.Serialize(emptyView, InspectionContext.Default));
            return 0;
        }

        // Resolve transitive dependencies
        var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depNodes = await DependencyResolutionService.ResolveDependencyTreeAsync(
            client, group.Dependencies, tfm, globalSeen, logger.Log);

        var view = new PackageDependenciesView
        {
            Title = CSharpIdentifier.ContainRenderedText($"{result.PackageName} {result.Version}"),
            Dependencies = ToTreeNodes(depNodes)
        };

        MarkoutSerializer.Serialize(view, Console.Out, PackageDependenciesContext.Default);
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
            var label = CSharpIdentifier.ContainRenderedText(
                !string.IsNullOrEmpty(n.Author)
                    ? $"{n.PackageId} {n.Version} [{n.Author}]"
                    : $"{n.PackageId} {n.Version}");
            return n.Children.Count > 0
                ? new TreeNode(label) { Children = ToTreeNodes(n.Children) }
                : new TreeNode(label);
        }).ToList();
    }
}
