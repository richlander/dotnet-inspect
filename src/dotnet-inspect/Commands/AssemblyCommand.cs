using DotnetInspector.Models;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;

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

        // Validate section filters
        // Validate exclude section filters
        var (_, resolvedExclude) = SectionRegistry.ResolveFilters(
            pipeline.AllSectionNames, null, options.ExcludeSections, out var sectionError);
        if (sectionError) return 1;
        options = options with { ExcludeSections = resolvedExclude };

        // Discovery mode: any bare projection flag lists available names
        if (SelectResolver.IsDiscovery(options.Select, options.Columns, options.Fields))
        {
            var schema = new MarkoutContext().GetSchemaInfo<LibraryInspectionView>();
            SelectOutput.WriteDiscovery(SelectResolver.GetDiscoveryEntries(options.Select, options.Columns, options.Fields,
                SectionRegistry.LibrarySections, schema));
            return 0;
        }

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(options.Select, pipeline.AllSectionNames);
        if (SelectOutput.WriteErrors(selectResult.Unresolved)) return 1;
        if (selectResult.Sections != null)
            options = options with { IncludeSections = selectResult.Sections };

        // --source-link-audit at non-detailed verbosity: implicitly select audit section
        if (options.IncludeSourcelinkAudit && options.Verbosity < Verbosity.Detailed)
        {
            HashSet<string> sections = options.IncludeSections != null ? [.. options.IncludeSections] : [];
            sections.Add("Source Link Audit");
            options = options with { IncludeSections = sections };
        }

        // -S targeting specific sections: promote verbosity to ensure data collection
        var requiredVerbosity = pipeline.GetRequiredVerbosity(options.IncludeSections);
        if (requiredVerbosity > options.Verbosity)
            options = options with { Verbosity = requiredVerbosity };

        // Warn if --oneline combined with detailed verbosity without section selector
        OutputFormatResolver.WarnIfOneLineDetailMismatch(options.OneLine, options.Verbosity, options.IncludeSections);

#if DEBUG
        // Detailed verbosity legitimately needs network for PDB/SourceLink
        if (options.Verbosity >= Verbosity.Detailed || options.IncludeSourcelinkAudit)
            DotnetInspector.Core.HttpClientFactory.AllowNetwork();
#endif

        // Compute which scanners are needed for the requested sections
        var scanners = pipeline.GetRequiredScanners(
            options.Verbosity, options.IncludeSections, options.ExcludeSections);

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
                    options.PlatformFramework);

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }

                logger.Log($"Using platform runtime library: {framework} {version}");

                var inspection = await LibraryMetadataService.InspectAsync(resolvedPath!, options, logger, null, null, context.HttpClient, isPlatformAssembly: true, scanners: scanners, scannerRegistry: scannerRegistry);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {resolvedPath}");
                    return 1;
                }

                inspection.Source = SourceKind.Platform;
                inspection.PlatformVersion = version;
                inspection.LastModified = File.GetLastWriteTimeUtc(resolvedPath!);


                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                ExtractResourcesIfRequested(resolvedPath!, options, logger);
                return 0;
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


                if (inspections.Count == 1)
                    OutputFormatter.WriteLibraryResult(inspections[0], options, pipeline);
                else
                    OutputFormatter.WriteLibraryResults(inspections, options, pipeline);

                if (assemblyPaths.Count > 0)
                    ExtractResourcesIfRequested(assemblyPaths[0], options, logger);

                return 0;
            }
            else
            {
                // Load from filesystem
                if (!File.Exists(assemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {assemblyPath}");
                    return 1;
                }

                var inspection = await LibraryMetadataService.InspectAsync(assemblyPath!, options, logger, null, null, context.HttpClient, scanners: scanners, scannerRegistry: scannerRegistry);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {assemblyPath}");
                    return 1;
                }

                inspection.Source = SourceKind.File;


                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                ExtractResourcesIfRequested(assemblyPath!, options, logger);
                return 0;
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

        // Normalize the assembly path for comparison
        string normalizedAssemblyName = assemblyName.Replace('\\', '/');

        // First try to match by relative path (for disambiguation)
        string[] matchingFiles = allDlls
            .Where(f =>
            {
                string relativePath = Path.GetRelativePath(extractPath, f).Replace('\\', '/');
                return relativePath.Equals(normalizedAssemblyName, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        // If no exact path match, try matching by filename
        if (matchingFiles.Length == 0)
        {
            matchingFiles = allDlls
                .Where(f => Path.GetFileName(f).Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).Equals(assemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (matchingFiles.Length == 0)
        {
            Console.Error.WriteLine($"Error: Library '{assemblyName}' not found in package.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --files' to list available libraries.");
            if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        if (matchingFiles.Length > 1)
        {
            Console.Error.WriteLine($"Multiple matches found for '{assemblyName}':");
            foreach (var f in matchingFiles)
            {
                Console.Error.WriteLine($"  {Path.GetRelativePath(extractPath, f)}");
            }
            Console.Error.WriteLine("Specify the full relative path to disambiguate.");
            if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchingFiles[0])}");
        return ([matchingFiles[0]], extractPath, tempDir, nupkgPath);
    }

}
