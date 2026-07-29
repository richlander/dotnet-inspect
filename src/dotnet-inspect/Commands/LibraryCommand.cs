using DotnetInspector.Core;
using DotnetInspector.MetadataRendering;
using DotnetInspector.Models;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using ILInspector.Research;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using SignatureVerificationResult = DotnetInspector.Services.SignatureVerificationResult;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public class LibraryCommand
{
    public static async Task<int> ExecuteAsync(LibraryOptions options)
    {
        var assemblyPath = options.AssemblyName;
        var pipeline = LibrarySections.CreatePipeline();
        var scannerRegistry = LibrarySections.CreateScannerRegistry();

        var schemaMap = MetadataSectionNames.AugmentSchema(
            InspectionContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema());
        bool hasInputSource = !string.IsNullOrEmpty(assemblyPath)
            || !string.IsNullOrEmpty(options.PackagePath)
            || !string.IsNullOrEmpty(options.PlatformAssembly);

        // Static discovery mode: -D --schema lists schema without resolving/loading the library.
        if (options.Discover != null)
        {
            if (!options.Schema && hasInputSource)
            {
                // Need to run pipeline to determine effective sections — handled after data collection below.
            }
            else
            {
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
        }

        // Bare -S (a lone @Default preset — i.e. `-S` with no value) selects the network-free
        // "fixed" overview: only sections whose declared growth class is Fixed and whose cost is
        // NetworkFree, so the rendered set is structurally identical for every package (absence
        // means "not applicable", never "too long for this package"). This still includes the
        // symbol-dependent fact tables (Symbols, Signals) because they read an embedded, adjacent,
        // or already-cached PDB without touching the network. Drop the preset marker and flag the
        // fixed overview; keep display verbosity at Normal so the cache-only PDB read stays enabled
        // (never downgrading a higher verbosity the user asked for, in which case the normal
        // curated ladder applies instead of the fixed overview).
        if (options.Discover == null
            && options.Select is { Length: 1 }
            && SelectResolver.IsInfoSelector(options.Select))
        {
            options = options with { Select = null };
            if (options.Verbosity == Verbosity.Minimal)
                options = options with { Verbosity = Verbosity.Normal, FixedOverview = true };
        }

        // -D defaults to effective discovery for target-based commands.
        bool effectiveDiscovery = options.Discover != null && !options.Schema && hasInputSource;
        var userVerbosity = options.Verbosity; // preserve for display formatting
        options = options with { UserVerbosityOverride = userVerbosity };
        if (effectiveDiscovery)
            options = options with { Verbosity = Verbosity.Detailed };

        var normalized = NormalizeILOffsetSelection(options);
        if (normalized.Error is not null)
        {
            Console.Error.WriteLine(normalized.Error);
            return 1;
        }
        options = normalized.Options;

        var heapNormalized = NormalizeHeapSelection(options);
        if (heapNormalized.Error is not null)
        {
            Console.Error.WriteLine(heapNormalized.Error);
            return 1;
        }
        options = heapNormalized.Options;

        // @Hidden is a discovery-only pole: it lists via -D @Hidden / --schema and its members
        // render by exact name, but it is not a render selector. This keeps -S from fanning out to
        // unbounded @Hidden members as a group.
        if (RejectHiddenRenderSelector(options.Select))
            return 1;

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
        {
            if (selectResult.Sections.Overlaps(ILCoordinateSections)
                && string.IsNullOrWhiteSpace(options.ILOffsetParameter))
            {
                if (!HasExactILCoordinateSelection(options.Select))
                {
                    selectResult.Sections.ExceptWith(ILCoordinateSections);
                }
                else if (options.Discover == null)
                {
                    Console.Error.WriteLine("Error: IL coordinate sections require --il-offset <token>+<offset>.");
                    return 1;
                }
            }

            if (selectResult.Sections.Contains(MetadataSectionNames.Heap)
                && string.IsNullOrWhiteSpace(options.HeapParameter))
            {
                // Same discipline as the IL coordinate sections above: reached through the
                // @Metadata door the section is simply dropped, because a category selection is a
                // request for whatever applies; named exactly it is an error, because the caller
                // asked for a specific section that cannot exist without its coordinate.
                if (!HasExactSelection(options.Select, MetadataSectionNames.Heap))
                {
                    selectResult.Sections.Remove(MetadataSectionNames.Heap);
                }
                else if (options.Discover == null)
                {
                    Console.Error.WriteLine($"Error: \"{MetadataSectionNames.Heap}\" requires --heap <heap>:<address>, for example --heap \"#Strings:0x1a4\".");
                    return 1;
                }
            }

            options = options with { IncludeSections = selectResult.Sections };
        }

        if (!string.IsNullOrWhiteSpace(options.HeapParameter)
            && options.IncludeSections is { Count: > 0 }
            && !options.IncludeSections.Contains(MetadataSectionNames.Heap))
        {
            Console.Error.WriteLine($"Error: --heap requires the heap coordinate section. Omit -S or include -S \"{MetadataSectionNames.Heap}\".");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            && options.IncludeSections is { Count: > 0 }
            && !options.IncludeSections.Overlaps(ILCoordinateSections))
        {
            Console.Error.WriteLine($"Error: --il-offset requires an IL coordinate section. Omit -S or include -S \"{SectionNames.ILOffset}\", -S \"{SectionNames.MemberContext}\", -S \"{SectionNames.InstructionContext}\", -S \"{SectionNames.ExceptionContext}\", -S \"{SectionNames.CallsiteContext}\", or -S \"{SectionNames.ReturnAddressContext}\".");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            && !string.IsNullOrWhiteSpace(options.ILOffsetsPath))
        {
            Console.Error.WriteLine("Error: --il-offset cannot be combined with --il-offsets.");
            return 1;
        }

        // --il-offsets counts resolved coordinate rows, not section rows, so it does not need a
        // section filter to make --count meaningful.
        var ilOffsetsBatchMode = !string.IsNullOrWhiteSpace(options.ILOffsetsPath);
        // Discovery renders its own rows, so a section requirement describes a filter it does
        // not use. -S still narrows effective discovery, so it stays permitted.
        var rendersOwnPayload = ilOffsetsBatchMode || options.Discover != null;

        if (!rendersOwnPayload && options.Count && !CountOutput.ValidateSectionsSelected(options.IncludeSections))
            return 1;

        if (options.Count && options.Print)
        {
            Console.Error.WriteLine("Error: --count cannot be combined with --print.");
            return 1;
        }

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (shapeCount > 1)
        {
            Console.Error.WriteLine("Error: specify only one of --value, --urls, or --paths.");
            return 1;
        }

        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            // The batch path refuses shape projections with an accurate reason; a section
            // requirement reported first would not be the actual problem.
            if (!rendersOwnPayload && !ShapeProjectionOutput.ValidateSingleSection(options.IncludeSections, optionName))
                return 1;
            if (options.Count || options.Print)
            {
                Console.Error.WriteLine($"Error: {optionName} cannot be combined with --count or --print.");
                return 1;
            }
            if (options.Rows is not null)
            {
                Console.Error.WriteLine($"Error: --rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N|first|last to select a projected row.");
                return 1;
            }
        }

        if (options.JsonArray && shapeCount == 0 && !options.Print)
        {
            Console.Error.WriteLine("Error: --json-array requires --value, --urls, --paths, or --print.");
            return 1;
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            Console.Error.WriteLine("Error: --json-array cannot be combined with --json or --jsonl.");
            return 1;
        }

        if (options.Print && !rendersOwnPayload && !ValidateLibraryPrintSelection(options.IncludeSections))
            return 1;

        if (options.Print && options.Rows is not null)
        {
            Console.Error.WriteLine("Error: --rows cannot be combined with --print; use --row N|first|last to choose a printed row.");
            return 1;
        }

        if (options.ProjectionRow is not null && !options.Print && shapeCount == 0)
        {
            Console.Error.WriteLine("Error: --row requires --print, --value, --urls, or --paths.");
            return 1;
        }

        // -S targeting specific sections: promote verbosity to ensure data collection
        var requiredVerbosity = pipeline.GetRequiredVerbosity(options.IncludeSections);
        if (requiredVerbosity > options.Verbosity)
            options = options with { Verbosity = requiredVerbosity };

        // Pre-render validation: check --fields/--columns names against the section schema
        if ((options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 }) && options.IncludeSections is { Count: > 0 })
        {
            if (!ProjectionDiagnostics.ValidateProjection(schemaMap, options.IncludeSections, options.Fields, options.Columns))
                return 1;
        }

        if (!OutputFormatResolver.ValidateSingleSectionForTabular(options.TabularExplicitlySet, options.IncludeSections))
            return 1;

        // Warn if tabular output is combined with detailed verbosity without section selector
        if (!effectiveDiscovery && !options.Count)
            OutputFormatResolver.WarnIfTabularDetailMismatch(options.Tabular, options.Verbosity, options.IncludeSections);

        // Compute which scanners are needed for the requested sections
        var scanners = pipeline.GetRequiredScanners(
            options.Verbosity, options.IncludeSections, options.FixedOverview);

        // Discovery must know which metadata tables carry rows, or the whole @Metadata category
        // filters out of the catalog: its sections are explicit-only, so no verbosity requests
        // them, and their applicability is the scanned row count. The scan is deliberately the
        // cheap half of the lens -- table row counts, never rows -- so listing the category
        // accurately costs a header read rather than a projection.
        if (effectiveDiscovery)
            scanners.Add(LibrarySections.ScannerMetadata);

        // Check for valid input source
        if (string.IsNullOrEmpty(assemblyPath) &&
            string.IsNullOrEmpty(options.PackagePath) &&
            string.IsNullOrEmpty(options.PlatformAssembly))
        {
            Console.Error.WriteLine("Error: Library path, package name, or --platform required.");
            Console.Error.WriteLine("Run 'dotnet-inspect library --help' for usage.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            // Parse package name and version for symbol package download
            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageExtractor.ParsePackageReference(options.PackagePath);
            }

            if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                var (resolvedPath, framework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                    options.PlatformAssembly,
                    context.HttpClient,
                    logger.Log,
                    options.PlatformFramework,
                    useRuntimeAssemblies: true,
                    platformVersion: options.PlatformVersion);

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }

                logger.Log($"Using platform runtime library: {framework} {version}");

                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                    return await WriteILCoordinateBatchAsync(resolvedPath!, null, null, isPlatformAssembly: true, options, context.HttpClient, logger);

                // Network-free SourceLink availability probe: drives the SourceLink section
                // family in -D and keys the effective cache so a warmed/cleared PDB busts a
                // stale catalog. Skipped (false) outside discovery.
                bool sourceLinkAvailable = effectiveDiscovery && !HasILOffsetCoordinate(options)
                    && await LibraryMetadataService.ProbeLocalSourceLinkAsync(resolvedPath!, context.HttpClient, logger, isPlatformAssembly: true);

                // Identity of the bytes about to be inspected. Computed once and reused for the
                // lookup, the pre-inspection snapshot, and (via CacheEffective) the write, so a
                // discovery run hashes the assembly at most twice.
                string? inspectedContentHash = effectiveDiscovery ? TryGetContentHash(resolvedPath!) : null;

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && inspectedContentHash != null && options.Discover is { Length: 0 } && !HasILOffsetCoordinate(options))
                {
                    var cached = TryGetCachedEffective(resolvedPath!, inspectedContentHash, sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(resolvedPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                var inspection = await LibraryMetadataService.InspectAsync(resolvedPath!, options, logger, null, null, context.HttpClient, isPlatformAssembly: true, scanners: scanners, scannerRegistry: scannerRegistry, discoveryOnly: effectiveDiscovery);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {resolvedPath}");
                    return 1;
                }

                inspection.Source = SourceKind.Platform;
                inspection.PlatformVersion = version;
                inspection.LastModified = File.GetLastWriteTimeUtc(resolvedPath!);

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, resolvedPath!, null, null, isPlatformAssembly: true, options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                PopulateMetadataHeapIfRequested(inspection, options, logger);
                if (effectiveDiscovery)
                    return WriteEffectiveSections(resolvedPath!, inspection, options, pipeline, userVerbosity, sourceLinkAvailable, cache: !HasILOffsetCoordinate(options) && !HasHeapCoordinate(options), inspectedContentHash: inspectedContentHash);
                if (TryWriteLibrarySingletonCount(inspection, options))
                    return 0;
                if (options.Print)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                WarnEmptySections(inspection, options, pipeline);
                ExtractResourcesIfRequested(resolvedPath!, options);
                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                return IntegrityExitCode(inspection);
            }
            else if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(
                    assemblyPath, options.PackagePath, options.Tfm,
                    options.SourceOptions, options.IncludePrerelease, logger, context.HttpClient);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion) = extractResult.Value;
                tempDir = extractTempDir;
                packageName = resolvedPackageName;
                packageVersion = resolvedPackageVersion;

                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                    return await WriteILCoordinateBatchAsync(assemblyPaths[0], packageName, packageVersion, isPlatformAssembly: false, options, context.HttpClient, logger);

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = effectiveDiscovery && assemblyPaths.Count > 0 && !HasILOffsetCoordinate(options)
                    && await LibraryMetadataService.ProbeLocalSourceLinkAsync(assemblyPaths[0], context.HttpClient, logger, isPlatformAssembly: false,
                        packageName: packageName, packageVersion: packageVersion);

                // Identity of the bytes about to be inspected; see the platform path above.
                string? inspectedContentHash = effectiveDiscovery && assemblyPaths.Count > 0
                    ? TryGetContentHash(assemblyPaths[0])
                    : null;

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && inspectedContentHash != null && options.Discover is { Length: 0 } && assemblyPaths.Count > 0 && !HasILOffsetCoordinate(options))
                {
                    var cached = TryGetCachedEffective(assemblyPaths[0], inspectedContentHash, sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPaths[0]);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                // Verify package signature if nupkg is available
                SignatureVerificationResult? signatureResult = null;
                if (nupkgPath != null)
                {
                    logger.Log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
                    signatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
                }

                // Inspect all assemblies
                var inspections = await CollectPackageInspectionsAsync(
                    assemblyPaths, options, logger, packageName, packageVersion,
                    extractPath, context.HttpClient, signatureResult, scanners, scannerRegistry, effectiveDiscovery);

                if (inspections.Count == 0)
                {
                    Console.Error.WriteLine("Error: No libraries could be read from the package.");
                    return 1;
                }

                foreach (var insp in inspections)
                    insp.Source = SourceKind.NuGet;

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspections[0], assemblyPaths[0], packageName, packageVersion, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                PopulateMetadataHeapIfRequested(inspections[0], options, logger);
                if (effectiveDiscovery)
                    return WriteEffectiveSections(assemblyPaths[0], inspections[0], options, pipeline, userVerbosity, sourceLinkAvailable, cache: !HasILOffsetCoordinate(options) && !HasHeapCoordinate(options), inspectedContentHash: inspectedContentHash);
                if (TryWriteLibrarySingletonCount(inspections[0], options))
                    return 0;
                if (options.Print)
                    return await WriteLibraryPrintProjectionAsync(inspections[0], options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspections[0], options);
                WarnEmptySections(inspections[0], options, pipeline);
                if (assemblyPaths.Count > 0)
                    ExtractResourcesIfRequested(assemblyPaths[0], options);

                if (inspections.Count == 1)
                    OutputFormatter.WriteLibraryResult(inspections[0], options, pipeline);
                else
                {
                    if (RejectMultiAssemblyMetadataSelection(inspections, options))
                        return 1;
                    OutputFormatter.WriteLibraryResults(inspections, options, pipeline);
                }

                return IntegrityExitCode([.. inspections]);
            }
            else
            {
                // Load from filesystem
                if (!File.Exists(assemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {assemblyPath}");
                    return 1;
                }

                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                    return await WriteILCoordinateBatchAsync(assemblyPath!, null, null, isPlatformAssembly: false, options, context.HttpClient, logger);

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = effectiveDiscovery && !HasILOffsetCoordinate(options)
                    && await LibraryMetadataService.ProbeLocalSourceLinkAsync(assemblyPath!, context.HttpClient, logger, isPlatformAssembly: false);

                // Identity of the bytes about to be inspected; see the platform path above.
                string? inspectedContentHash = effectiveDiscovery ? TryGetContentHash(assemblyPath!) : null;

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && inspectedContentHash != null && options.Discover is { Length: 0 } && !HasILOffsetCoordinate(options))
                {
                    var cached = TryGetCachedEffective(assemblyPath!, inspectedContentHash, sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                var inspection = await LibraryMetadataService.InspectAsync(assemblyPath!, options, logger, null, null, context.HttpClient, scanners: scanners, scannerRegistry: scannerRegistry, discoveryOnly: effectiveDiscovery);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {assemblyPath}");
                    return 1;
                }

                inspection.Source = SourceKind.File;

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, assemblyPath!, null, null, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                PopulateMetadataHeapIfRequested(inspection, options, logger);
                if (effectiveDiscovery)
                    return WriteEffectiveSections(assemblyPath!, inspection, options, pipeline, userVerbosity, sourceLinkAvailable, cache: !HasILOffsetCoordinate(options) && !HasHeapCoordinate(options), inspectedContentHash: inspectedContentHash);
                if (TryWriteLibrarySingletonCount(inspection, options))
                    return 0;
                if (options.Print)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                WarnEmptySections(inspection, options, pipeline);
                ExtractResourcesIfRequested(assemblyPath!, options);
                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                return IntegrityExitCode(inspection);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            // Cleanup temp directory if we extracted from a package
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static int IntegrityExitCode(params LibraryInspection[] inspections)
    {
        return inspections.Any(insp => insp.SourceIntegrityMismatches is { Count: > 0 }) ? 1 : 0;
    }

    private static async Task<int> WriteILCoordinateBatchAsync(
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!File.Exists(options.ILOffsetsPath))
        {
            Console.Error.WriteLine($"Error: IL offsets file not found: {options.ILOffsetsPath}");
            return 1;
        }

        if (!TryReadILCoordinates(options.ILOffsetsPath!, out var coordinates, out var readErrors, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        HashSet<string> sections = options.IncludeSections is { Count: > 0 }
            ? [.. options.IncludeSections]
            : [.. BatchCoordinateSections];

        var rows = readErrors
            .Select(errorRow => new ILCoordinateBatchRow(null, errorRow.Label, null, null, "error", errorRow.Error))
            .ToList();
        using var service = SourceLinkService.Open(assemblyPath, logger.Log);
        foreach (var coordinate in coordinates)
        {
            var queryOptions = options with
            {
                ILOffsetParameter = coordinate.Coordinate,
                IncludeSections = sections,
                Select = [.. sections],
                Discover = null,
                Print = false,
                Count = false,
                Value = false,
                Urls = false,
                Paths = false
            };
            var resolved = await ILOffsetQuery.ResolveBatchAsync(
                service,
                packageName,
                packageVersion,
                isPlatformAssembly,
                queryOptions,
                httpClient,
                logger);
            rows.Add(resolved.Result is { } result
                ? BuildILCoordinateBatchRow(coordinate, result)
                : new ILCoordinateBatchRow(coordinate.Coordinate, coordinate.Label, null, null, "error", resolved.Error ?? "could not resolve"));
        }

        var batchExitCode = rows.Any(row => row.Meaning == "error") ? 1 : 0;

        // --rows narrows what the table renders, so it has to narrow the count by
        // the same window; counting the unwindowed batch answers a question the
        // user did not ask, and the audit cannot see the difference. The exit code
        // still reports every coordinate that failed to resolve, windowed out of
        // view or not, because that is a resolution result rather than a display
        // concern.
        var visibleRows = RowWindow.Apply(options.Rows, rows);

        // A coordinate that failed to resolve is still a reported row, so it counts; the
        // non-zero exit remains the signal that some coordinate did not resolve.
        if (LensProjection.TryProject(options, "--il-offsets", visibleRows.Count, out var projectionExitCode))
            return projectionExitCode != 0 ? projectionExitCode : batchExitCode;

        WriteILCoordinateBatchRows(rows, options);
        return batchExitCode;    }

    private static readonly string[] BatchCoordinateSections =
    [
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.ExceptionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static bool TryReadILCoordinates(string path, out List<ILCoordinateInput> coordinates, out List<ILCoordinateReadError> readErrors, out string? error)
    {
        coordinates = [];
        readErrors = [];
        error = null;
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var coordinateIndex = Array.FindIndex(tokens, token => ILOffsetQuery.TryParse(token, out _, out _));
            if (coordinateIndex < 0)
            {
                readErrors.Add(new ILCoordinateReadError($"{path}:{lineNumber}", "expected a MethodDef token + IL offset coordinate"));
                continue;
            }

            var labelTokens = tokens
                .Where((_, index) => index != coordinateIndex)
                .ToArray();
            coordinates.Add(new ILCoordinateInput(
                tokens[coordinateIndex],
                labelTokens.Length == 0 ? null : string.Join(' ', labelTokens)));
        }

        if (coordinates.Count == 0 && readErrors.Count == 0)
        {
            error = $"Error: {path} did not contain any IL coordinates.";
            return false;
        }

        return true;
    }

    private static ILCoordinateBatchRow BuildILCoordinateBatchRow(ILCoordinateInput input, ILOffsetProjection result)
    {
        var (meaning, evidence) = ExplainILCoordinate(result);
        return new ILCoordinateBatchRow(
            input.Coordinate,
            input.Label,
            result.Method,
            FormatBatchOffset(result),
            meaning,
            evidence);
    }

    private static (string Meaning, string Evidence) ExplainILCoordinate(ILOffsetProjection result)
    {
        if (result.ReturnAddressContext is { } returnAddress)
            return ("return address", $"call at {returnAddress.CallOffset} to {returnAddress.Callee}");
        if (result.AllocationContext is { Count: > 0 } allocations)
        {
            var allocation = allocations[0];
            return ("allocation", $"{allocation.AllocationKind} {allocation.AllocatedType}".Trim());
        }
        if (result.SafetyContext is { Count: > 0 } safetyFacts)
        {
            var safety = safetyFacts[0];
            return ("safety", $"{safety.SafetyKind} {safety.Operation}".Trim());
        }
        if (result.CostContext is { Count: > 0 } costFacts)
        {
            var cost = costFacts[0];
            return ("cost", $"{cost.CostKind} {cost.Operation}".Trim());
        }
        if (result.CallsiteContext is { } callsite)
            return ("callsite", $"{callsite.Opcode} {callsite.Callee}");
        if (result.ExceptionContext is { Count: > 0 } exceptions)
            return ("exception", string.Join(", ", exceptions.Select(e => $"{e.Context} {e.Clause}".Trim())));
        if (result.InstructionContext is { } instruction)
            return ("instruction", $"{instruction.Opcode} {instruction.Operand}".Trim());
        return ("member", result.MemberContext?.Signature ?? result.Method ?? "");
    }

    private static string? FormatBatchOffset(ILOffsetProjection result)
        => result.InstructionContext?.ILOffset is { } offset
            ? FormatHexOffset(offset)
            : result.MemberContext?.ILOffset is { } memberOffset
                ? FormatHexOffset(memberOffset)
                : result.ILOffset;

    private static string FormatHexOffset(string value)
        => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset)
            ? $"IL_{offset:X4}"
            : value;

    private static void WriteILCoordinateBatchRows(List<ILCoordinateBatchRow> rows, LibraryOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new ILCoordinateBatchResult(rows), ILCoordinateBatchJsonContext.Default.ILCoordinateBatchResult));
            return;
        }

        if (!options.Tabular && !options.Tsv && !options.Jsonl && !options.NoHeader)
        {
            Console.WriteLine("## IL Coordinates");
            Console.WriteLine();
        }

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(
                ["Coordinate", "Label", "Member", "IL Offset", "Meaning", "Evidence"],
                ["coordinate", "label", "member", "il_offset", "meaning", "evidence"],
                rows.Select(row => new[]
                {
                    row.Coordinate ?? "",
                    row.Label ?? "",
                    row.Member ?? "",
                    row.ILOffset ?? "",
                    row.Meaning,
                    row.Evidence
                }).ToArray());
            markoutWriter.Flush();
        }, options.Rows);
    }

    private static (LibraryOptions Options, string? Error) NormalizeILOffsetSelection(LibraryOptions options)
    {
        var select = options.Select?.ToList() ?? [];
        string? ilOffset = options.ILOffsetParameter;
        bool hasExplicitSelect = select.Count > 0;

        // Reject "<coordinate section>:<offset>" selectors. The legacy spellings ("IL Offset",
        // "Source Location") stay listed because they still resolve as aliases, and the current
        // name itself contains a colon, so the guard must match name + ':' rather than any colon.
        string[] parameterizedPrefixes =
        [
            "IL Offset:",
            "Source Location:",
            SectionNames.ILOffset + ":",
        ];

        for (var i = 0; i < select.Count; i++)
        {
            var value = select[i].Trim();
            if (parameterizedPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return (options, $"Error: IL offset parameters belong in --il-offset, not in -S. Use --il-offset 0x06000001+0x5 -S \"{SectionNames.ILOffset}\".");
        }

        if (!string.IsNullOrWhiteSpace(ilOffset)
            && options.Discover == null
            && !hasExplicitSelect)
        {
            select.Add(SectionNames.ILOffset);
            select.Add(SectionNames.MemberContext);
            select.Add(SectionNames.InstructionContext);
            select.Add(SectionNames.ExceptionContext);
            select.Add(SectionNames.CallsiteContext);
            select.Add(SectionNames.ReturnAddressContext);
        }

        return (options with
        {
            ILOffsetParameter = ilOffset,
            Select = select.Count == 0 ? null : [.. select]
        }, null);
    }

    // Catalog-hidden set for the effective (real-assembly) -D flows. IL-offset
    // coordinate sections are excluded so they remain discoverable at the -D top
    // level exactly when a coordinate makes them applicable (FilterEffective drops
    // them otherwise); they stay grouped under @Hidden for --schema / -S.
    private static IReadOnlySet<string> EffectiveCatalogHidden(SectionPipeline<LibraryInspection> pipeline)
    {
        var hidden = pipeline.GetCatalogHiddenSections()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        hidden.ExceptWith(ILCoordinateSections);
        return hidden;
    }

    // @Hidden is a discovery-only pole: it lists via -D @Hidden / --schema and its members
    // render by exact name, but it is not a render selector. Rejecting -S @Hidden keeps render
    // selection from fanning out to unbounded @Hidden members as a group. Shared with the
    // package embedded-library render path, which resolves
    // -S against the same curated LibrarySections pipeline.
    internal static bool RejectHiddenRenderSelector(string[]? select)
    {
        if (select is { Length: > 0 }
            && select.Any(v => v.Equals(SectionPipeline<LibraryInspection>.HiddenCategory, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Error: @Hidden is discovery-only. List it with -D @Hidden or --schema, and render its members by exact name (for example -S \"Top Leverage\").");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rejects a metadata-lens selection when a package resolved to more than one assembly.
    /// The lens renders raw ECMA-335 tables of one image: row ids are image-relative and section
    /// names carry no assembly, so several assemblies would emit repeated
    /// <c>## Metadata: TypeDef</c> headings whose rows silently belong to different images and
    /// whose row numbering restarts without saying so. Failing here keeps that ambiguity visible
    /// instead of rendering a confidently wrong document.
    /// </summary>
    private static bool RejectMultiAssemblyMetadataSelection(
        IReadOnlyCollection<LibraryInspection> inspections, LibraryOptions options)
    {
        if (inspections.Count <= 1 || options.IncludeSections is not { Count: > 0 } selected)
            return false;

        if (!selected.Any(MetadataSectionNames.IsMetadataSection))
            return false;

        Console.Error.WriteLine(
            $"Error: {SectionCategoryNames.Metadata} inspects the metadata tables of a single assembly, " +
            $"but this package resolved to {inspections.Count} assemblies.");
        Console.Error.WriteLine("Select one assembly with --library <path> and retry.");
        return true;
    }

    private static readonly string[] ILCoordinateSections =
    [
        SectionNames.ILOffset,
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.ExceptionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static readonly string[] ILCoordinateSingletonSections =
    [
        SectionNames.ILOffset,
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static bool HasILOffsetCoordinate(LibraryOptions options)
        => !string.IsNullOrWhiteSpace(options.ILOffsetParameter);

    /// <summary>
    /// True when a heap coordinate was supplied. Like an IL coordinate, it changes which sections
    /// exist, so a discovery catalog computed with one must not be served to a run without one.
    /// </summary>
    private static bool HasHeapCoordinate(LibraryOptions options)
        => !string.IsNullOrWhiteSpace(options.HeapParameter);

    /// <summary>
    /// True when <paramref name="select"/> names <paramref name="section"/> exactly, as opposed to
    /// reaching it through an <c>@Category</c>. The distinction decides whether a coordinate
    /// section with no coordinate is an error or is simply dropped.
    /// </summary>
    private static bool HasExactSelection(string[]? select, string section)
    {
        if (select is not { Length: > 0 })
            return false;

        foreach (var value in select)
        {
            if (value.StartsWith('@'))
                continue;
            if (value.Trim().Equals(section, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Validates the <c>--heap</c> coordinate and, when no selection was given, selects the
    /// section it feeds.
    ///
    /// The coordinate is parsed here — before any assembly is opened — so a malformed one fails
    /// immediately with a diagnostic naming the wrong half, rather than after the cost of an
    /// inspection.
    /// </summary>
    private static (LibraryOptions Options, string? Error) NormalizeHeapSelection(LibraryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HeapParameter))
            return (options, null);

        if (!MetadataHeapCoordinate.TryParse(options.HeapParameter, out _, out _, out string? error))
            return (options, $"Error: invalid --heap value '{options.HeapParameter}': {error}");

        if (options.Discover != null || options.Select is { Length: > 0 })
            return (options, null);

        return (options with { Select = [MetadataSectionNames.Heap] }, null);
    }

    /// <summary>
    /// Reads the heap value <c>--heap</c> named onto the model, which is what makes the
    /// coordinate-scoped section applicable.
    ///
    /// A read failure becomes a malformed value on the model rather than a null: the section was
    /// asked for by an explicit coordinate, so it must render and say what went wrong instead of
    /// vanishing as though no coordinate had been given.
    /// </summary>
    private static void PopulateMetadataHeapIfRequested(
        LibraryInspection inspection, LibraryOptions options, VerboseLogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.HeapParameter)
            || (options.Discover == null && options.IncludeSections?.Contains(MetadataSectionNames.Heap) != true))
            return;

        if (inspection.MetadataAssemblyPath is not { } path)
            return;

        if (!MetadataHeapCoordinate.TryParse(options.HeapParameter, out var heap, out int address, out _))
            throw new UnreachableException("NormalizeHeapSelection rejects a malformed --heap coordinate before this point.");

        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            if (session.MetadataHeapValue(heap, address) is { } value)
                inspection.MetadataHeap = new MetadataHeapLookup(heap, address, value);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error reading {MetadataHeapCoordinate.StreamName(heap)} heap at {address} in {path}: {ex.Message}");
            inspection.MetadataHeap = new MetadataHeapLookup(
                heap, address, new MetadataValue.Malformed($"{MetadataHeapCoordinate.StreamName(heap)} heap read failed: {ex.Message}"));
        }
    }

    private static bool HasExactILCoordinateSelection(string[]? select)
    {
        if (select is not { Length: > 0 })
            return false;

        foreach (var value in select)
        {
            if (value.StartsWith('@'))
                continue;
            if (ILCoordinateSections.Contains(value, StringComparer.OrdinalIgnoreCase)
                || value.Equals("IL Offset", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task<int> PopulateILOffsetIfRequestedAsync(
        LibraryInspection inspection,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            || (options.Discover == null && options.IncludeSections?.Overlaps(ILCoordinateSections) != true))
            return 0;

        var resolved = await ILOffsetQuery.ResolveAsync(
            assemblyPath, packageName, packageVersion, isPlatformAssembly, options, httpClient, logger);
        if (resolved.ExitCode != 0)
            return resolved.ExitCode;

        inspection.ILOffset = resolved.Result;
        return 0;
    }

    private static bool ValidateLibraryPrintSelection(HashSet<string>? sections)
    {
        if (sections is { Count: 1 } && sections.Contains(SectionNames.ILOffset))
            return true;

        Console.Error.WriteLine("Error: --print requires -S/--select to match exactly one printable section.");
        return false;
    }

    private static bool TryWriteLibrarySingletonCount(LibraryInspection inspection, LibraryOptions options)
    {
        if (!options.Count
            || options.IncludeSections is not { Count: 1 } sections
            || !sections.Overlaps(ILCoordinateSingletonSections))
        {
            return false;
        }

        var section = sections.Single();
        var hasRow = section switch
        {
            SectionNames.ILOffset => inspection.ILOffset != null,
            SectionNames.MemberContext => inspection.ILOffset?.MemberContext != null,
            SectionNames.InstructionContext => inspection.ILOffset?.InstructionContext != null,
            SectionNames.CallsiteContext => inspection.ILOffset?.CallsiteContext != null,
            SectionNames.ReturnAddressContext => inspection.ILOffset?.ReturnAddressContext != null,
            SectionNames.AllocationContext => inspection.ILOffset?.AllocationContext is { Count: > 0 },
            SectionNames.SafetyContext => inspection.ILOffset?.SafetyContext is { Count: > 0 },
            SectionNames.CostContext => inspection.ILOffset?.CostContext is { Count: > 0 },
            _ => false
        };
        CountOutput.WriteCount(hasRow ? 1 : 0);
        return true;
    }

    private static int WriteLibraryShapeProjection(LibraryInspection inspection, LibraryOptions options)
    {
        var kind = ShapeProjectionOutput.GetKind(options.Value, options.Urls, options.Paths);
        var section = options.IncludeSections!.Single();
        var rows = section switch
        {
            SectionNames.SourceLinkFiles => ProjectLibrarySourceFiles(inspection, section, kind, options),
            "Library Info" => ProjectLibraryInfo(inspection, section, kind, options),
            SectionNames.ILOffset => ProjectLibraryILOffset(inspection, section, kind, options),
            SectionNames.MemberContext => ProjectLibraryMemberContext(inspection, section, kind, options),
            SectionNames.InstructionContext => ProjectLibraryInstructionContext(inspection, section, kind, options),
            SectionNames.ExceptionContext => ProjectLibraryExceptionContext(inspection, section, kind, options),
            SectionNames.CallsiteContext => ProjectLibraryCallsiteContext(inspection, section, kind, options),
            SectionNames.ReturnAddressContext => ProjectLibraryReturnAddressContext(inspection, section, kind, options),
            _ => []
        };

        if (rows.Count == 0 && section is SectionNames.MemberContext or SectionNames.InstructionContext or SectionNames.ExceptionContext
            or SectionNames.CallsiteContext or SectionNames.ReturnAddressContext)
        {
            if (kind != ShapeProjectionKind.Value)
                Console.Error.WriteLine($"Error: section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }

        if (rows.Count == 0 && section is not (SectionNames.SourceLinkFiles or "Library Info") && section != SectionNames.ILOffset)
        {
            Console.Error.WriteLine($"Error: section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }
        if (rows.Count == 0 && section == "Library Info" && kind == ShapeProjectionKind.Value)
            return 1;

        return ShapeProjectionOutput.Write(
            rows,
            new ShapeProjectionOptions(kind, options.ProjectionRow, options.JsonOutput, options.Jsonl, options.JsonArray));
    }

    private static async Task<int> WriteLibraryPrintProjectionAsync(LibraryInspection inspection, LibraryOptions options)
    {
        var section = options.IncludeSections!.Single();
        var projection = section switch
        {
            SectionNames.ILOffset => await ProjectLibraryILOffsetPrintableAsync(inspection, section),
            _ => new PrintProjectionResult([])
        };
        if (projection.Error is not null)
        {
            Console.Error.WriteLine(projection.Error);
            return 1;
        }

        if (projection.Documents.Count == 0 && section != SectionNames.ILOffset)
        {
            Console.Error.WriteLine($"Error: section '{section}' is not printable.");
            return 1;
        }

        return PrintProjectionOutput.Write(
            projection.Documents,
            new PrintProjectionOptions(
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                Bare: false,
                OutputPath: null));
    }

    private sealed record PrintProjectionResult(IReadOnlyList<PrintableDocument> Documents, string? Error = null);

    private static async Task<PrintProjectionResult> ProjectLibraryILOffsetPrintableAsync(
        LibraryInspection inspection,
        string section)
    {
        if (inspection.ILOffset is not { } result)
            return new PrintProjectionResult([]);

        var (content, error) = await ReadILOffsetSourceLineAsync(result);
        if (error is not null)
            return new PrintProjectionResult([], error);
        if (content is null)
            return new PrintProjectionResult([]);

        return new PrintProjectionResult(
        [
            new PrintableDocument(1, section, result.Method ?? result.File ?? section, result.File, result.Url, content)
        ]);
    }

    internal static Task<(string? Content, string? Error)> ReadILOffsetSourceLineForTestsAsync(ILOffsetProjection result)
        => ReadILOffsetSourceLineAsync(result);

    private static async Task<(string? Content, string? Error)> ReadILOffsetSourceLineAsync(ILOffsetProjection result)
    {
        if (result.Line is not { } line || line < 1)
        {
            return (null, "Error: Source Location row has no source line to print.");
        }

        if (string.IsNullOrWhiteSpace(result.Url))
        {
            return (null, "Error: Source Location row has no printable source body. Use --urls or --paths to inspect available payloads.");
        }

        var rawUrl = StripUrlFragment(GitHubUrlResolver.ConvertBlobToRawUrl(result.Url));
        var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        var source = await fetcher.FetchSourceAsync(rawUrl);
        if (source is null)
        {
            return (null, $"Error: Could not fetch SourceLink source for {rawUrl}.");
        }

        return ReadLine(source.ReplaceLineEndings("\n").Split('\n'), line);
    }

    private static (string? Content, string? Error) ReadLine(IEnumerable<string> lines, int line)
    {
        var value = lines.Skip(line - 1).FirstOrDefault();
        if (value is null)
        {
            return (null, $"Error: Source line {line} is out of range.");
        }

        return (value, null);
    }

    private static string StripUrlFragment(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Fragment))
            return url;

        var builder = new UriBuilder(uri) { Fragment = "" };
        return builder.Uri.ToString();
    }

    private static List<ShapeProjectionRow> ProjectLibrarySourceFiles(LibraryInspection inspection, string section, ShapeProjectionKind kind, LibraryOptions options)
    {
        var rows = new LibraryInspectionView(inspection).SourceFilesSection ?? [];
        return rows
            .Select((row, index) =>
            {
                string? value = kind switch
                {
                    ShapeProjectionKind.Urls => row.Url,
                    ShapeProjectionKind.Value => SelectLibrarySourceValue(row, options),
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

    private static string? SelectLibrarySourceValue(SourceFileRow row, LibraryOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "type" => row.Type,
            "url" => row.Url,
            _ => row.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectLibraryILOffset(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (inspection.ILOffset is not { } result)
            return [];

        var value = kind switch
        {
            ShapeProjectionKind.Urls => result.Url,
            ShapeProjectionKind.Paths => result.File,
            ShapeProjectionKind.Value => SelectLibraryILOffsetValue(result, options),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value)
            ? []
            : [new ShapeProjectionRow(1, section, value, Label: result.Method, Url: result.Url, Path: result.File)];
    }

    private static string? SelectLibraryILOffsetValue(ILOffsetProjection result, LibraryOptions options)
    {
        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        return field?.ToLowerInvariant() switch
        {
            "method" => result.Method,
            "token" => result.Token,
            "il offset" or "iloffset" => result.ILOffset,
            "matched offset" or "matchedoffset" => result.MatchedOffset,
            "file" or "path" => result.File,
            "line" => result.Line?.ToString(CultureInfo.InvariantCulture),
            "url" or "source" => result.Url,
            _ => result.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectLibraryMemberContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.MemberContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine($"Error: --value for {SectionNames.MemberContext} requires --fields <name>.");
            return [];
        }

        var value = SelectMemberContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in {SectionNames.MemberContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectMemberContextValue(ILOffsetMemberContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "assembly" => context.Assembly,
            "type" => context.Type,
            "type kind" or "typekind" => context.TypeKind,
            "member" => context.Member,
            "signature" => context.Signature,
            "member kind" or "memberkind" => context.MemberKind,
            "visibility" => context.Visibility,
            "static" => context.Static,
            "async" => context.Async,
            "metadata token" or "metadatatoken" or "token" => context.MetadataToken,
            "il offset" or "iloffset" => context.ILOffset,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryInstructionContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.InstructionContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine($"Error: --value for {SectionNames.InstructionContext} requires --fields <name>.");
            return [];
        }

        var value = SelectInstructionContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in {SectionNames.InstructionContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectInstructionContextValue(ILOffsetInstructionContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "il offset" or "iloffset" => context.ILOffset,
            "boundary" => context.Boundary,
            "opcode" => context.Opcode,
            "operand kind" or "operandkind" => context.OperandKind,
            "operand" => context.Operand,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            "branch targets" or "branchtargets" => context.BranchTargets,
            "next offset" or "nextoffset" => context.NextOffset,
            "length" => context.Length?.ToString(CultureInfo.InvariantCulture),
            "block" => context.Block?.ToString(CultureInfo.InvariantCulture),
            "terminates block" or "terminatesblock" => context.TerminatesBlock,
            "falls through" or "fallsthrough" => context.FallsThrough,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryExceptionContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.ExceptionContext is not { Count: > 0 } rows)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine($"Error: --value for {SectionNames.ExceptionContext} requires --fields <name>.");
            return [];
        }

        List<ShapeProjectionRow> projected = [];
        for (var i = 0; i < rows.Count; i++)
        {
            var value = SelectExceptionContextValue(rows[i], field);
            if (!string.IsNullOrWhiteSpace(value))
                projected.Add(new ShapeProjectionRow(i + 1, section, value, Label: field));
        }

        if (projected.Count == 0)
            Console.Error.WriteLine($"Error: field '{field}' has no value in {SectionNames.ExceptionContext}.");

        return projected;
    }

    private static string? SelectExceptionContextValue(ILOffsetExceptionContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "region" => context.Region.ToString(CultureInfo.InvariantCulture),
            "context" => context.Context,
            "clause" => context.Clause,
            "try range" or "tryrange" => context.TryRange,
            "handler range" or "handlerrange" => context.HandlerRange,
            "filter range" or "filterrange" => context.FilterRange,
            "caught type" or "caughttype" => context.CaughtType,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryCallsiteContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.CallsiteContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine($"Error: --value for {SectionNames.CallsiteContext} requires --fields <name>.");
            return [];
        }

        var value = SelectCallsiteContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in {SectionNames.CallsiteContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectCallsiteContextValue(ILOffsetCallsiteContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "call offset" or "calloffset" or "offset" => context.CallOffset,
            "opcode" => context.Opcode,
            "call kind" or "callkind" => context.CallKind,
            "callee" => context.Callee,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            "return address" or "returnaddress" => context.ReturnAddress,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryReturnAddressContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.ReturnAddressContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine($"Error: --value for {SectionNames.ReturnAddressContext} requires --fields <name>.");
            return [];
        }

        var value = SelectReturnAddressContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in {SectionNames.ReturnAddressContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectReturnAddressContextValue(ILOffsetReturnAddressContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "il offset" or "iloffset" or "offset" => context.ILOffset,
            "call offset" or "calloffset" => context.CallOffset,
            "opcode" => context.Opcode,
            "call kind" or "callkind" => context.CallKind,
            "callee" => context.Callee,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryInfo(LibraryInspection inspection, string section, ShapeProjectionKind kind, LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value)
            return [];
        var info = new LibraryInspectionView(inspection).AssemblyInfoSection;
        if (info is null)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine("Error: --value for Library Info requires --fields <name>.");
            return [];
        }

        var values = GetLibraryInfoValues(info);
        if (!values.TryGetValue(field, out var value))
        {
            Console.Error.WriteLine($"Error: field '{field}' was not found in Library Info.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in Library Info.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static Dictionary<string, string?> GetLibraryInfoValues(LibraryInfoSection info)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(LibraryInfoSection).GetProperties())
        {
            var name = ToPascalCaseWords(property.Name);
            values[name] = FormatLibraryInfoValue(property.GetValue(info));
        }

        return values;
    }

    private static string? FormatLibraryInfoValue(object? value)
        => value switch
        {
            null => null,
            bool b => b ? "Yes" : "No",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static string ToPascalCaseWords(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(value.Length + 8);
        builder.Append(value[0]);
        for (var i = 1; i < value.Length; i++)
        {
            var current = value[i];
            var previous = value[i - 1];
            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
                builder.Append(' ');
            builder.Append(current);
        }

        return builder.ToString();
    }

    private static int WriteEffectiveSections(string assemblyPath, LibraryInspection inspection,
        LibraryOptions options, SectionPipeline<LibraryInspection> pipeline, Verbosity userVerbosity = Verbosity.Minimal,
        bool sourceLinkAvailable = false, bool cache = true, string? inspectedContentHash = null)
    {
        // Seed the network-free SourceLink-availability fact so the SourceLink section family
        // gates on a cached/embedded/adjacent PDB during discovery (never clears a value the
        // inspection already established from an embedded or adjacent PDB).
        inspection.HasSourceLink |= sourceLinkAvailable;

        // Compute all structurally applicable sections for discovery/caching,
        // including opt-in sections whose renderability depends on the section's
        // own work (for example SourceLink audit sections).
        var allEffective = pipeline.GetDiscoverableSections(inspection);
        var schemaMap = MetadataSectionNames.AugmentSchema(
            InspectionContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema());

        // Field-level filtering on ALL effective sections (unfiltered) for caching
        var filteredSchema = FilterSchemaToEffectiveFields(inspection, allEffective, schemaMap, pipeline, allEffective.ToArray());
        if (cache)
            CacheEffective(assemblyPath, inspection.HasSourceLink, allEffective, filteredSchema, inspectedContentHash);

        // Apply user filters
        var effective = FilterEffective(allEffective, options);

        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath);
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, filteredSchema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
            verbosity: (int)userVerbosity, rootLabel: rootLabel, fullSchema: schemaMap,
            sectionCostAnnotations: pipeline.GetCostAnnotations(),
            sectionCategories: pipeline.GetCategoryMap(),
            catalogHiddenSections: EffectiveCatalogHidden(pipeline),
            listedCategoryDoors: pipeline.GetListedCategoryDoors(),
            projection: options);
    }

    // ── Effective sections cache ──

    // Bumped to v19: the cached catalog now carries the @Metadata lens sections, and entries
    // written by v18 were poisoned by the CRLF split bug below, so stale entries must not be read.
    private const string EffectiveCategory = "effective-v19";

    static LibraryCommand()
    {
        CoreCache.RegisterVersionedCategory("effective-v", EffectiveCategory);
    }

    private static (List<string> Sections, DocumentSchema Schema)? TryGetCachedEffective(string assemblyPath, string contentHash, bool hasSourceLink)
    {
        string key = BuildEffectiveCacheKey(assemblyPath, contentHash, hasSourceLink);
        var cached = CoreCache.TryGet(EffectiveCategory, key, extension: "tsv");
        if (cached == null) return null;

        var sections = new List<string>();
        var schema = new DocumentSchema();
        foreach (var raw in cached.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Entries are written with AppendLine, which is CRLF on Windows. Splitting on '\n'
            // alone leaves the '\r' attached to the last field of every line, so a cached section
            // name would not compare equal to the registered name and would silently escape
            // name-keyed filters such as the catalog-hidden set.
            var line = raw.TrimEnd('\r');
            var parts = line.Split('\t');
            var name = parts[0];
            sections.Add(name);
            if (parts.Length >= 3)
                schema.Add(name, parts[1], parts[2].Split(','));
            else
                schema.AddSection(name);
        }
        return (sections, schema);
    }

    /// <summary>
    /// Stores a discovery catalog under the identity of the bytes it was derived from.
    /// </summary>
    /// <param name="inspectedContentHash">
    /// Content hash captured immediately <em>before</em> the inspection that produced
    /// <paramref name="sections"/>, or <see langword="null"/> when it was not captured.
    /// </param>
    private static void CacheEffective(string assemblyPath, bool hasSourceLink, List<string> sections,
        DocumentSchema filteredSchema, string? inspectedContentHash)
    {
        // The catalog describes the bytes the inspection parsed, but the key is derived from a
        // separate read that happens after it. If the assembly is replaced in between — an
        // ordinary rebuild racing a discovery run — the pre- and post-inspection hashes disagree,
        // and writing the entry would file this catalog under the *replacement's* identity, where
        // it would be served as a correct answer indefinitely. Declining to cache turns that
        // silent, persistent mislabelling into a recomputation on the next run.
        //
        // This narrows the window rather than closing it: the hash and the parse remain two reads,
        // so bytes that change and change back around the inspection still agree. Closing it needs
        // the inspection to report the identity of the image it actually parsed, tracked in #3478.
        var currentContentHash = TryGetContentHash(assemblyPath);
        if (currentContentHash == null) return;
        if (inspectedContentHash != null && !string.Equals(inspectedContentHash, currentContentHash, StringComparison.Ordinal))
            return;

        string key = BuildEffectiveCacheKey(assemblyPath, currentContentHash, hasSourceLink);
        var sb = new System.Text.StringBuilder();
        foreach (var name in sections)
        {
            var section = filteredSchema.GetSection(name);
            if (section != null && section.Items.Length > 0)
                sb.Append($"{name}\t{section.ItemKind}\t{string.Join(',', section.Items.Select(i => i.Name))}").Append('\n');
            else
                sb.Append(name).Append('\n');
        }
        CoreCache.Set(EffectiveCategory, key, sb.ToString(), extension: "tsv");
    }

    /// <summary>
    /// SHA-256 of an assembly's bytes, or <see langword="null"/> when they cannot be read.
    /// </summary>
    private static string? TryGetContentHash(string assemblyPath)
    {
        // A cached catalog is only valid for the bytes it was computed from, so the cache is keyed
        // by content. Neither size nor write time identifies content: a rebuild in place, or
        // copying a different assembly over the same path, routinely produces a same-sized file,
        // and a write time can be preserved by a copy, restored from an archive (whose recorded
        // stamps are coarse and often fixed for reproducibility), or shared by two writes that
        // land inside one filesystem timestamp tick. Any of those served the previous file's
        // catalog. Hashing removes the collision by construction rather than narrowing it:
        // different bytes cannot share a key. It costs a read and a SHA-256 pass — measured at
        // 9.8 ms for the largest assembly in this repository against a ~2.4 s warm discovery run,
        // so well under 1% of the work the cache exists to avoid.
        //
        // Gate: MetadataLensTests.LibraryCommand_DiscoverEffective_SameSizeReplacement_
        // InvalidatesCache and ..._PreservedWriteTimeReplacement_InvalidatesCache pin both
        // collisions; both fail if the key stops being content-derived.
        try
        {
            // Share the file as permissively as the inspector that is about to read it, so a
            // concurrent reader or a pending delete does not turn a cache lookup into a failure.
            using var stream = new FileStream(
                Path.GetFullPath(assemblyPath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The identity of the bytes is unknown, so no cache entry can be proven to describe
            // them. Callers bypass the cache rather than key on a weaker identity: this costs a
            // full discovery run, and the caller reads the same file immediately afterwards,
            // which surfaces the underlying failure with its own diagnostics.
            return null;
        }
    }

    private static string BuildEffectiveCacheKey(string assemblyPath, string contentHash, bool hasSourceLink)
    {
        // Include a network-free SourceLink-availability token so warming/clearing a cached PDB
        // (which flips whether the SourceLink section family is effective) busts a stale -D
        // catalog.
        //
        // The path is resolved because the key is built from whatever the caller typed. A
        // relative path names different files from different working directories, so two
        // same-sized assemblies at the same relative path — the normal case for one repository
        // and its worktrees — otherwise share a key and serve each other's catalog.
        return $"{Path.GetFullPath(assemblyPath)}#{contentHash}#sl{(hasSourceLink ? 1 : 0)}";
    }

    private static List<string> FilterEffective(List<string> sections, LibraryOptions options)
    {
        if (options.IncludeSections is { Count: > 0 })
            sections = sections.Where(s => options.IncludeSections.Contains(s)).ToList();
        if (!HasILOffsetCoordinate(options))
            sections = sections.Where(s => !ILCoordinateSections.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        return sections;
    }

    private static int RenderEffective(List<string> effective, DocumentSchema schema, LibraryOptions options,
        Verbosity userVerbosity = Verbosity.Minimal, string? rootLabel = null)
    {
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
            verbosity: (int)userVerbosity, rootLabel: rootLabel,
            sectionCostAnnotations: LibrarySections.CreatePipeline().GetCostAnnotations(),
            sectionCategories: LibrarySections.CreatePipeline().GetCategoryMap(),
            catalogHiddenSections: EffectiveCatalogHidden(LibrarySections.CreatePipeline()),
            listedCategoryDoors: LibrarySections.CreatePipeline().GetListedCategoryDoors(),
            projection: options);
    }

    /// <summary>
    /// Renders the targeted sections and filters the schema to only fields that produced output.
    /// </summary>
    private static DocumentSchema FilterSchemaToEffectiveFields(LibraryInspection inspection,
        List<string> effectiveSections, DocumentSchema schema, SectionPipeline<LibraryInspection> pipeline,
        string[] discover)
    {
        // Resolve which sections are being discovered
        var targetSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in discover)
        {
            var resolved = schema.ResolveSection(d);
            if (resolved != null && effectiveSections.Contains(resolved))
                targetSections.Add(resolved);
        }
        if (targetSections.Count == 0) return schema;

        var filteredSections = new HashSet<string>(
            targetSections.Where(name =>
                string.Equals(name, LibrarySections.LibraryInfo.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SectionNames.MemberContext, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        if (filteredSections.Count == 0)
            return schema;

        var view = new LibraryInspectionView(inspection);
        var writerOpts = new MarkoutWriterOptions { IncludeSections = filteredSections };
        var renderManifest = RenderManifestFormatter.Capture(
            view,
            InspectionContext.Default,
            writerOpts,
            schema);

        return DiscoverOutput.FilterSchemaToRenderedFields(
            effectiveSections,
            schema,
            renderManifest,
            filteredSections);
    }

    private static void WarnEmptySections(LibraryInspection inspection, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var (empty, requested) = pipeline.GetEmptySections(inspection, options.Verbosity, options.IncludeSections);
        var failures = inspection.InspectionFailures;
        List<LibraryInspectionFailureJson> relevantFailures = failures?
            .Where(failure => empty.Any(section => FailureAffectsSection(failure.Section, section)))
            .ToList() ?? [];
        foreach (var failure in relevantFailures)
        {
            Console.Error.WriteLine(
                $"Warning: {failure.Section} inspection failed ({failure.Finding}): {failure.Reason}");
        }

        var unexplained = empty
            .Where(section => !relevantFailures.Any(
                failure => FailureAffectsSection(failure.Section, section)))
            .ToList();
        if (unexplained.Count > 0 && empty.Count == requested)
        {
            var label = unexplained.Count == 1 ? "section has" : "sections have";
            Console.Error.WriteLine(
                $"Note: {unexplained.Count} matched {label} no data: {string.Join(", ", unexplained)}.");
        }
    }

    internal static bool FailureAffectsSection(string failureSection, string section)
    {
        if (failureSection.Equals(section, StringComparison.OrdinalIgnoreCase))
            return true;

        if (failureSection.Equals("Classified Methods", StringComparison.Ordinal))
        {
            return section.Equals("Library Info", StringComparison.OrdinalIgnoreCase)
                   || section.Equals("P/Invoke Methods", StringComparison.OrdinalIgnoreCase)
                   || section.Equals("Async Methods", StringComparison.OrdinalIgnoreCase);
        }

        if (section.Equals("Library Info", StringComparison.OrdinalIgnoreCase))
        {
            return failureSection is "Extension Methods"
                or "Resources"
                or "Custom Attributes"
                or "Type Forwarders"
                or "Union Types"
                or "Switches"
                or LibraryIntegrationCatalog.RollupName
                or EcosystemIntegrationNames.OpenTelemetry;
        }

        if (failureSection.Equals(EcosystemIntegrationNames.OpenTelemetry, StringComparison.Ordinal))
        {
            return section.Equals(IntegrationSectionNames.OpenTelemetry, StringComparison.OrdinalIgnoreCase);
        }

        return failureSection.Equals(LibraryIntegrationCatalog.RollupName, StringComparison.Ordinal)
               && LibraryIntegrationCatalog.All.Any(
                   descriptor => descriptor.SectionName.Equals(section, StringComparison.OrdinalIgnoreCase));
    }

    private static void ExtractResourcesIfRequested(string assemblyPath, LibraryOptions options)
    {
        if (string.IsNullOrEmpty(options.ExtractResources))
            return;

        using var session = AssemblyInspectionSession.Open(assemblyPath);
        var extracted = session.ExtractResources(options.ExtractResources);
        if (extracted.Count == 0)
        {
            Console.Error.WriteLine("No embedded resources found.");
        }
        else
        {
            Console.Error.WriteLine($"Extracted {extracted.Count} resource(s) to {options.ExtractResources}");
            foreach (var path in extracted)
            {
                Console.Error.WriteLine($"  {Path.GetFileName(path)}");
            }
        }
    }

    private static async Task<List<LibraryInspection>> CollectPackageInspectionsAsync(
        List<string> assemblyPaths, LibraryOptions options, VerboseLogger logger,
        string? packageName, string? packageVersion, string extractPath,
        HttpClient httpClient, SignatureVerificationResult? signatureResult,
        HashSet<string>? scanners = null, ScannerRegistry? scannerRegistry = null,
        bool discoveryOnly = false)
    {
        List<LibraryInspection> inspections = [];

        foreach (var targetPath in assemblyPaths)
        {
            var version = packageVersion ?? (packageName != null ? PackageExtractor.ExtractVersionFromPath(targetPath, packageName) : null);

            var inspection = await LibraryMetadataService.InspectAsync(targetPath, options, logger, packageName, version, httpClient, scanners: scanners, scannerRegistry: scannerRegistry, discoveryOnly: discoveryOnly);
            if (inspection == null)
            {
                logger.Log($"Warning: Could not read library: {Path.GetFileName(targetPath)}");
                continue;
            }

            // Populate TFM from path for multi-TFM display
            var relativePath = Path.GetRelativePath(extractPath, targetPath).Replace('\\', '/');
            inspection.Tfm = TfmResolver.ExtractTfmFromPath(relativePath);

            if (signatureResult != null)
            {
                inspection.Publisher = signatureResult.Publisher;
                inspection.PublisherVerified = signatureResult.AuthorVerified;
                inspection.RepositoryVerified = signatureResult.RepositoryVerified;
                inspection.SignatureStatus = signatureResult.StatusMessage;
            }

            inspections.Add(inspection);
        }

        return inspections;
    }

    private static async Task<(List<string> assemblyPaths, string extractPath, string? tempDir, string? nupkgPath, string? packageName, string? packageVersion)?> ExtractFromPackageAsync(
        string? assemblyName,
        string packageSource,
        string? tfm,
        NuGetSourceOptions? sourceOptions,
        bool includePrerelease,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(
            httpClient, packageSource, logger.Log, sourceOptions: sourceOptions, includePrerelease: includePrerelease);
        if (!outcome.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
            return null;
        }
        var resolution = outcome.Result!;

        string extractPath = resolution.ExtractPath;
        string? tempDir = resolution.TempDir;
        string? nupkgPath = resolution.NupkgPath;
        string? resolvedPackageName = resolution.PackageName;
        string? resolvedPackageVersion = resolution.Version;

        // Find DLLs in the extracted package
        string[] allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
        if (allDlls.Length == 0)
        {
            var payload = await TryResolveToolPayloadPackageAsync(
                resolution, packageSource, sourceOptions, logger, httpClient).ConfigureAwait(false);

            if (payload.Error != null)
            {
                Console.Error.WriteLine(payload.Error);
                DeleteTempDir(tempDir);
                return null;
            }

            if (payload.Result != null)
            {
                DeleteTempDir(tempDir);
                resolution = payload.Result;
                extractPath = resolution.ExtractPath;
                tempDir = resolution.TempDir;
                nupkgPath = resolution.NupkgPath;
                resolvedPackageName = resolution.PackageName;
                resolvedPackageVersion = resolution.Version;
                allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
            }
        }

        // --tfm all: return all assemblies from every TFM
        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
        {
            var (candidates, _) = TfmSelector.SelectHighestAssembliesFromPackage(extractPath, tfm);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                DeleteTempDir(tempDir);
                return null;
            }
            return (candidates, extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        // --tfm <specific>: find assembly by TFM
        if (!string.IsNullOrEmpty(tfm))
        {
            var tfmAssembly = TfmSelector.FindAssemblyByTfm(extractPath, tfm, resolution.PackageName);
            if (tfmAssembly == null)
            {
                Console.Error.WriteLine($"Error: No library found for TFM '{tfm}'.");
                Console.Error.WriteLine("Available TFMs:");
                var tfms = TfmSelector.GetPackageTfms(allDlls, extractPath);
                foreach (var t in tfms)
                {
                    Console.Error.WriteLine($"  {t}");
                }
                DeleteTempDir(tempDir);
                return null;
            }
            logger.Log($"Using TFM: {tfm}");
            return ([tfmAssembly], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        // No --tfm and no assembly name: select the highest-priority TFM (default)
        if (string.IsNullOrEmpty(assemblyName))
        {
            var candidates = TfmSelector.GetPackageAssemblies(extractPath);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                DeleteTempDir(tempDir);
                return null;
            }

            var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(candidates, extractPath, resolution.PackageName);
            if (selectedPath == null)
            {
                // No TFM structure found, fall back to first DLL
                return ([candidates[0]], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
            }

            logger.Log($"Using TFM: {selectedTfm}");
            return ([selectedPath], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        var (matchedAssembly, matchedTfm) = TfmSelector.FindAssemblyInPackage(extractPath, assemblyName, tfm);
        if (matchedAssembly == null)
        {
            Console.Error.WriteLine($"Error: Library '{assemblyName}' not found in package.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --path \"lib/\"' to list available libraries.");
            DeleteTempDir(tempDir);
            return null;
        }

        if (matchedTfm != null)
            logger.Log($"Using TFM: {matchedTfm}");

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchedAssembly)}");
        return ([matchedAssembly], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
    }

    private sealed record ToolPayloadResolution(PackageExtractionResult? Result, string? Error);

    private static async Task<ToolPayloadResolution> TryResolveToolPayloadPackageAsync(
        PackageExtractionResult package,
        string originalPackageSource,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var payloadId = GetToolPayloadPackageId(package.ExtractPath, package.PackageName);
        if (payloadId == null)
            return new(null, null);

        var version = package.Version ?? GetNuspecVersion(package.ExtractPath);
        if (version == null)
            return new(null, $"Error: Tool package '{package.PackageName}' has no DLLs and its version could not be determined.");

        var localPayload = TryFindLocalSiblingPackage(originalPackageSource, payloadId, version);
        var payloadOutcome = localPayload != null
            ? await PackageExtractor.ExtractPackageAsync(httpClient, localPayload, logger.Log).ConfigureAwait(false)
            : await PackageExtractor.ExtractPackageAsync(
                httpClient, payloadId, logger.Log, sourceOptions: sourceOptions, version: version).ConfigureAwait(false);

        if (!payloadOutcome.IsSuccess)
            return new(null, $"Error: Tool package '{package.PackageName}' has no inspectable DLLs and payload package '{payloadId}@{version}' could not be resolved: {payloadOutcome.ErrorMessage}");

        var payload = payloadOutcome.Result!;
        var dlls = Directory.GetFiles(payload.ExtractPath, "*.dll", SearchOption.AllDirectories)
            .Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dlls.Count == 0)
        {
            DeleteTempDir(payload.TempDir);
            return new(null, $"Error: Tool payload package '{payload.PackageName}@{payload.Version}' does not contain inspectable .NET DLLs.");
        }

        logger.Log($"Tool package has no DLLs; inspecting payload package: {payload.PackageName} {payload.Version}");
        return new(payload, null);
    }

    private static string? GetToolPayloadPackageId(string extractPath, string? packageName)
    {
        var toolsDir = Path.Combine(extractPath, "tools");
        if (Directory.Exists(toolsDir))
        {
            var settings = DotnetToolSettingsParser.FindAndParse(toolsDir);
            var anyPayload = settings?.RuntimeIdentifierPackages?
                .FirstOrDefault(r => r.RuntimeIdentifier.Equals("any", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(anyPayload?.PackageId))
                return anyPayload.PackageId;
        }

        return TryGetSiblingAnyPackageId(packageName);
    }

    private static string? TryGetSiblingAnyPackageId(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return null;

        string[] knownRidSuffixes =
        [
            ".win-x64",
            ".win-arm64",
            ".linux-x64",
            ".linux-arm64",
            ".osx-arm64"
        ];

        var suffix = knownRidSuffixes.FirstOrDefault(s =>
            packageName.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        return suffix == null ? null : packageName[..^suffix.Length] + ".any";
    }

    private static string? TryFindLocalSiblingPackage(string originalPackageSource, string payloadId, string version)
    {
        if (!originalPackageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(originalPackageSource));
        if (directory == null)
            return null;

        var exact = Path.Combine(directory, $"{payloadId}.{version}.nupkg");
        if (File.Exists(exact))
            return exact;

        return Directory.GetFiles(directory, $"{payloadId}.*.nupkg")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? GetNuspecVersion(string extractPath)
    {
        return NuspecParser.FindAndParse(extractPath)?.Version;
    }

    private static void DeleteTempDir(string? tempDir)
    {
        if (tempDir == null)
            return;

        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

}

internal sealed record ILCoordinateInput(string Coordinate, string? Label);

internal sealed record ILCoordinateReadError(string Label, string Error);

internal sealed record ILCoordinateBatchResult(List<ILCoordinateBatchRow> Rows);

[MarkoutSerializable]
internal sealed record ILCoordinateBatchRow(
    [property: MarkoutSkipNull] string? Coordinate,
    [property: MarkoutSkipNull] string? Label,
    [property: MarkoutSkipNull] string? Member,
    [property: MarkoutPropertyName("IL Offset")]
    [property: MarkoutSkipNull] string? ILOffset,
    string Meaning,
    string Evidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ILCoordinateBatchResult))]
internal partial class ILCoordinateBatchJsonContext : JsonSerializerContext
{
}
