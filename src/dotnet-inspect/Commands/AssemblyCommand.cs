using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
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

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public class AssemblyCommand
{
    public static async Task<int> ExecuteAsync(AssemblyOptions options)
    {
        var assemblyPath = options.AssemblyName;
        var pipeline = LibrarySections.CreatePipeline();
        var scannerRegistry = LibrarySections.CreateScannerRegistry();

        var schemaMap = InspectionContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema();

        // Discovery mode: -D/--discover lists schema
        if (options.Discover != null)
        {
            if (options.Effective)
            {
                // Need to run pipeline to determine effective sections — handled after data collection below
                // For now, fall through to --effective with bare -D
            }
            else
            {
                return DiscoverOutput.Execute(options.Discover, schemaMap,
                    tree: options.Tree, json: options.JsonOutput, markdown: options.Markdown,
                    verbosity: (int)options.Verbosity,
                    sectionCostAnnotations: pipeline.GetCostAnnotations());
            }
        }

        // --effective with -D: run pipeline at Detailed to show sections with data
        bool effectiveDiscovery = options.Effective && options.Discover != null;
        var userVerbosity = options.Verbosity; // preserve for display formatting
        options = options with { UserVerbosityOverride = userVerbosity };
        if (effectiveDiscovery)
            options = options with { Verbosity = Verbosity.Detailed };

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(options.Select, pipeline.AllSectionNames);
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
            options = options with { IncludeSections = selectResult.Sections };

        if (options.Audit && options.IncludeSections is not { Count: > 0 })
            options = options with { IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Audit" } };

        if (options.Count && !CountOutput.ValidateSingleSection(options.IncludeSections))
            return 1;

        // -S targeting specific sections: promote verbosity to ensure data collection
        var requiredVerbosity = pipeline.GetRequiredVerbosity(options.IncludeSections);
        if (requiredVerbosity > options.Verbosity)
            options = options with { Verbosity = requiredVerbosity };

        // Pre-render validation: check --fields/--columns names against the section schema
        if ((options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 }) && options.IncludeSections is { Count: > 0 })
        {
            foreach (var section in options.IncludeSections)
                ProjectionDiagnostics.ValidateProjection(schemaMap, section, options.Fields, options.Columns);
        }

        // Explicit --oneline with multiple sections: error
        if (options.OneLineExplicitlySet && options.IncludeSections is { Count: > 1 })
        {
            Console.Error.WriteLine($"Error: Selection matches {options.IncludeSections.Count} sections: {string.Join(", ", options.IncludeSections)}.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Oneline format displays one section at a time.");
            Console.Error.WriteLine("Use -S with a specific section name, or --markdown for multi-section output.");
            return 1;
        }

        // Warn if --oneline combined with detailed verbosity without section selector
        if (!effectiveDiscovery && !options.Count)
            OutputFormatResolver.WarnIfOneLineDetailMismatch(options.OneLine, options.Verbosity, options.IncludeSections);

        // Compute which scanners are needed for the requested sections
        var scanners = pipeline.GetRequiredScanners(
            options.Verbosity, options.IncludeSections);

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

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && options.Discover is { Length: 0 })
                {
                    var cached = TryGetCachedEffective(resolvedPath!);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(resolvedPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                var inspection = await LibraryMetadataService.InspectAsync(resolvedPath!, options, logger, null, null, context.HttpClient, isPlatformAssembly: true, scanners: scanners, scannerRegistry: scannerRegistry);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {resolvedPath}");
                    return 1;
                }

                inspection.Source = SourceKind.Platform;
                inspection.PlatformVersion = version;
                inspection.LastModified = File.GetLastWriteTimeUtc(resolvedPath!);

                if (effectiveDiscovery)
                    return WriteEffectiveSections(resolvedPath!, inspection, options, pipeline, userVerbosity);
                WarnEmptySections(inspection, options, pipeline);
                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                ExtractResourcesIfRequested(resolvedPath!, options, logger);
                return IntegrityExitCode(inspection);
            }
            else if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(assemblyPath, options.PackagePath, options.Tfm, logger, context.HttpClient);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath) = extractResult.Value;
                tempDir = extractTempDir;

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && options.Discover is { Length: 0 } && assemblyPaths.Count > 0)
                {
                    var cached = TryGetCachedEffective(assemblyPaths[0]);
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
                    extractPath, context.HttpClient, signatureResult, scanners, scannerRegistry);

                if (inspections.Count == 0)
                {
                    Console.Error.WriteLine("Error: No libraries could be read from the package.");
                    return 1;
                }

                foreach (var insp in inspections)
                    insp.Source = SourceKind.NuGet;

                if (effectiveDiscovery)
                    return WriteEffectiveSections(assemblyPaths[0], inspections[0], options, pipeline, userVerbosity);
                WarnEmptySections(inspections[0], options, pipeline);
                if (inspections.Count == 1)
                    OutputFormatter.WriteLibraryResult(inspections[0], options, pipeline);
                else
                    OutputFormatter.WriteLibraryResults(inspections, options, pipeline);

                if (assemblyPaths.Count > 0)
                    ExtractResourcesIfRequested(assemblyPaths[0], options, logger);

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

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && options.Discover is { Length: 0 })
                {
                    var cached = TryGetCachedEffective(assemblyPath!);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                var inspection = await LibraryMetadataService.InspectAsync(assemblyPath!, options, logger, null, null, context.HttpClient, scanners: scanners, scannerRegistry: scannerRegistry);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {assemblyPath}");
                    return 1;
                }

                inspection.Source = SourceKind.File;

                if (effectiveDiscovery)
                    return WriteEffectiveSections(assemblyPath!, inspection, options, pipeline, userVerbosity);
                WarnEmptySections(inspection, options, pipeline);
                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                ExtractResourcesIfRequested(assemblyPath!, options, logger);
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
        bool mismatch = false;
        foreach (var insp in inspections)
        {
            if (insp.SourceIntegrityMismatches is { Count: > 0 } list)
            {
                mismatch = true;
                foreach (var file in list)
                    Console.Error.WriteLine($"Source integrity mismatch: {file}");
            }
        }
        return mismatch ? 1 : 0;
    }

    private static int WriteEffectiveSections(string assemblyPath, LibraryInspection inspection,
        AssemblyOptions options, SectionPipeline<LibraryInspection> pipeline, Verbosity userVerbosity = Verbosity.Minimal)
    {
        // Compute unfiltered effective sections at Detailed verbosity for caching
        var allEffective = pipeline.GetEffectiveSections(inspection, Verbosity.Detailed);
        var schemaMap = InspectionContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema();

        // Field-level filtering on ALL effective sections (unfiltered) for caching
        var filteredSchema = FilterSchemaToEffectiveFields(inspection, allEffective, schemaMap, pipeline, allEffective.ToArray());
        CacheEffective(assemblyPath, allEffective, filteredSchema);

        // Apply user filters
        var effective = FilterEffective(allEffective, options);

        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath);
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, filteredSchema,
            tree: options.Tree, json: options.JsonOutput, markdown: options.Markdown,
            verbosity: (int)userVerbosity, rootLabel: rootLabel);
    }

    // ── Effective sections cache ──

    private const string EffectiveCategory = "effective";

    private static (List<string> Sections, DocumentSchema Schema)? TryGetCachedEffective(string assemblyPath)
    {
        string key = GetEffectiveCacheKey(assemblyPath);
        var cached = CoreCache.TryGet(EffectiveCategory, key, extension: "tsv");
        if (cached == null) return null;

        var sections = new List<string>();
        var schema = new DocumentSchema();
        foreach (var line in cached.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
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

    private static void CacheEffective(string assemblyPath, List<string> sections, DocumentSchema filteredSchema)
    {
        string key = GetEffectiveCacheKey(assemblyPath);
        var sb = new System.Text.StringBuilder();
        foreach (var name in sections)
        {
            var section = filteredSchema.GetSection(name);
            if (section != null && section.Items.Length > 0)
                sb.AppendLine($"{name}\t{section.ItemKind}\t{string.Join(',', section.Items.Select(i => i.Name))}");
            else
                sb.AppendLine(name);
        }
        CoreCache.Set(EffectiveCategory, key, sb.ToString(), extension: "tsv");
    }

    private static string GetEffectiveCacheKey(string assemblyPath)
    {
        // Include file size for invalidation when local files change
        var size = new FileInfo(assemblyPath).Length;
        return $"{assemblyPath}#{size}";
    }

    private static List<string> FilterEffective(List<string> sections, AssemblyOptions options)
    {
        if (options.IncludeSections is { Count: > 0 })
            sections = sections.Where(s => options.IncludeSections.Contains(s)).ToList();
        return sections;
    }

    private static int RenderEffective(List<string> effective, DocumentSchema schema, AssemblyOptions options,
        Verbosity userVerbosity = Verbosity.Minimal, string? rootLabel = null)
    {
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
            tree: options.Tree, json: options.JsonOutput, markdown: options.Markdown,
            verbosity: (int)userVerbosity, rootLabel: rootLabel);
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

        var view = new LibraryInspectionView(inspection);
        var writerOpts = new MarkoutWriterOptions { IncludeSections = targetSections };
        var rendered = MarkoutSerializer.Serialize(view, InspectionContext.Default, writerOpts);

        var filtered = new DocumentSchema();
        foreach (var name in effectiveSections)
        {
            var section = schema.GetSection(name);
            if (section == null) { filtered.AddSection(name); continue; }

            // Only filter fields for discovered sections; keep others intact
            if (!targetSections.Contains(name))
            {
                filtered.Add(name, section.ItemKind, section.Items.Select(i => i.Name).ToArray());
                continue;
            }

            var effectiveItems = section.Items
                .Where(item => rendered.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Name)
                .ToArray();

            if (effectiveItems.Length > 0)
                filtered.Add(name, section.ItemKind, effectiveItems);
            else
                filtered.AddSection(name);
        }
        return filtered;
    }

    private static void WarnEmptySections(LibraryInspection inspection, AssemblyOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var (empty, requested) = pipeline.GetEmptySections(inspection, options.Verbosity, options.IncludeSections);
        if (empty.Count > 0 && empty.Count == requested)
        {
            var label = empty.Count == 1 ? "section has" : "sections have";
            Console.Error.WriteLine($"Note: {empty.Count} matched {label} no data: {string.Join(", ", empty)}.");
        }
    }

    private static void ExtractResourcesIfRequested(string assemblyPath, AssemblyOptions options, VerboseLogger logger)
    {
        if (string.IsNullOrEmpty(options.ExtractResources))
            return;

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            var extracted = ResourceScanner.ExtractAll(stream, options.ExtractResources);
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error extracting resources: {ex.Message}");
        }
    }

    private static async Task<List<LibraryInspection>> CollectPackageInspectionsAsync(
        List<string> assemblyPaths, AssemblyOptions options, VerboseLogger logger,
        string? packageName, string? packageVersion, string extractPath,
        HttpClient httpClient, SignatureVerificationResult? signatureResult,
        HashSet<string>? scanners = null, ScannerRegistry? scannerRegistry = null)
    {
        List<LibraryInspection> inspections = [];

        foreach (var targetPath in assemblyPaths)
        {
            var version = packageVersion ?? (packageName != null ? PackageExtractor.ExtractVersionFromPath(targetPath, packageName) : null);

            var inspection = await LibraryMetadataService.InspectAsync(targetPath, options, logger, packageName, version, httpClient, scanners: scanners, scannerRegistry: scannerRegistry);
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

    private static async Task<(List<string> assemblyPaths, string extractPath, string? tempDir, string? nupkgPath)?> ExtractFromPackageAsync(string? assemblyName, string packageSource, string? tfm, VerboseLogger logger, HttpClient httpClient)
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(httpClient, packageSource, logger.Log);
        if (!outcome.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
            return null;
        }
        var resolution = outcome.Result!;

        string extractPath = resolution.ExtractPath;
        string? tempDir = resolution.TempDir;
        string? nupkgPath = resolution.NupkgPath;

        // Find DLLs in the extracted package
        string[] allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);

        // --tfm all: return all assemblies from every TFM
        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = TfmSelector.GetPackageDlls(extractPath);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
            return (candidates, extractPath, tempDir, nupkgPath);
        }

        // --tfm <specific>: find assembly by TFM
        if (!string.IsNullOrEmpty(tfm))
        {
            var tfmAssembly = TfmSelector.FindAssemblyByTfm(extractPath, tfm, resolution.PackageName);
            if (tfmAssembly == null)
            {
                Console.Error.WriteLine($"Error: No library found for TFM '{tfm}'.");
                Console.Error.WriteLine("Available TFMs:");
                var tfms = allDlls
                    .Select(d => TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, d).Replace('\\', '/')))
                    .Where(t => t != null)
                    .Distinct()
                    .OrderByDescending(t => TfmResolver.GetTfmPriority(t!));
                foreach (var t in tfms)
                {
                    Console.Error.WriteLine($"  {t}");
                }
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
            logger.Log($"Using TFM: {tfm}");
            return ([tfmAssembly], extractPath, tempDir, nupkgPath);
        }

        // No --tfm and no assembly name: select the highest-priority TFM (default)
        if (string.IsNullOrEmpty(assemblyName))
        {
            var candidates = TfmSelector.GetPackageDlls(extractPath);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }

            var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(candidates, extractPath, resolution.PackageName);
            if (selectedPath == null)
            {
                // No TFM structure found, fall back to first DLL
                return ([candidates[0]], extractPath, tempDir, nupkgPath);
            }

            logger.Log($"Using TFM: {selectedTfm}");
            return ([selectedPath], extractPath, tempDir, nupkgPath);
        }

        var (matchedAssembly, matchedTfm) = TfmSelector.FindAssemblyInPackage(extractPath, assemblyName, tfm);
        if (matchedAssembly == null)
        {
            Console.Error.WriteLine($"Error: Library '{assemblyName}' not found in package.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --files' to list available libraries.");
            if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        if (matchedTfm != null)
            logger.Log($"Using TFM: {matchedTfm}");

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchedAssembly)}");
        return ([matchedAssembly], extractPath, tempDir, nupkgPath);
    }

}
