using DotnetInspector.Models;
using DotnetInspector.Metadata;
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
        var sectionNames = pipeline.AllSectionNames;
        bool packageLibraryMode = options.PackageLibrary != null || options.AllLibraries;

        // Static discovery mode: -D --schema lists schema without resolving/loading the package.
        // Also keep no-target package discovery static because there is no target to make effective.
        if (!packageLibraryMode && options.Discover != null && (options.Schema || packageArgs.Length < 1))
        {
            var schemaMap = InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema();
            return DiscoverOutput.Execute(options.Discover, schemaMap,
                tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
                verbosity: (int)options.Verbosity,
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: pipeline.GetCategoryMap());
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
                options.Select, sectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
            if (SelectOutput.WriteUnresolved(selectResult)) return 1;
            if (selectResult.Sections != null)
                options = options with { IncludeSections = selectResult.Sections };

            if (options.Count && !CountOutput.ValidateSingleSection(options.IncludeSections))
                return 1;

            if (!OutputFormatResolver.ValidateSingleSectionForTabular(options.OneLineExplicitlySet, options.IncludeSections))
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
                var schemaMap = InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema();
                if (!ProjectionDiagnostics.ValidateProjection(schemaMap, options.IncludeSections, options.Fields, options.Columns))
                    return 1;
            }
        }

        if (packageArgs.Length < 1)
        {
            Console.Error.WriteLine("Error: Package name or path required.");
            Console.Error.WriteLine("Run 'dotnet-inspect package --help' for usage.");
            return 1;
        }

        if (options.AllLibraries && !ValidatePackageAllLibrariesMode(options))
            return 1;
        if (options.PackageLibrary != null && !ValidatePackageLibraryMode(options))
            return 1;

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        // Handle --versions mode: list versions and exit early
        if (options.ListVersions)
        {
            string normalizedName = packageArgs[0].ToLowerInvariant();

            // Cache-first for bare --version (Limit==1 && !ForceLatest):
            // check local caches before hitting NuGet, matching router behavior.
            if (options.Limit == 1 && !options.ForceLatest && !options.IncludePrerelease)
            {
                var cachedVersion = NuGetCache.TryGetLatestCachedVersion(normalizedName);
                if (cachedVersion != null)
                {
                    Console.WriteLine(cachedVersion);
                    return 0;
                }
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
                    Console.Error.WriteLine($"Error: Package '{packageArgs[0]}' not found on nuget.org");
                    return 1;
                }

                Console.WriteLine(latest);
                return 0;
            }

            var versions = await PackageExtractor.GetVersionsAsync(context.HttpClient, normalizedName, options.IncludePrerelease, options.Limit, logger.Log, options.SourceOptions);
            if (versions == null)
            {
                Console.Error.WriteLine($"Error: Package '{packageArgs[0]}' not found on nuget.org");
                return 1;
            }

            OutputFormatter.WriteStringList(versions, "Version", "Version", options.Tsv, options.Jsonl, Console.Out);

            return 0;
        }

        // Check if first argument is a local file path
        bool isLocalFile = packageArgs.Length >= 1 &&
            packageArgs[0].EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        var client = context.HttpClient;

        string packageName;
        string version;

        if (isLocalFile)
        {
            string localPath = packageArgs[0];
            if (!File.Exists(localPath))
            {
                Console.Error.WriteLine($"Error: File not found: {localPath}");
                return 1;
            }

            string fileName = Path.GetFileNameWithoutExtension(localPath);
            packageName = fileName;
            version = "local";
        }
        else
        {
            // Support format: PackageName or PackageName@version
            string packageArg = packageArgs[0];
            int atIndex = packageArg.IndexOf('@');

            if (explicitVersion != null)
            {
                packageName = atIndex > 0
                    ? packageArg[..atIndex].ToLowerInvariant()
                    : packageArg.ToLowerInvariant();
                version = explicitVersion.ToLowerInvariant();
                logger.Log($"Using --version: {version}");
            }
            else if (atIndex > 0)
            {
                packageName = packageArg[..atIndex].ToLowerInvariant();
                version = packageArg[(atIndex + 1)..].ToLowerInvariant();
                logger.Log($"Using specified version: {version}");
            }
            else if (packageArgs.Length >= 2)
            {
                packageName = packageArg.ToLowerInvariant();
                version = packageArgs[1].ToLowerInvariant();
            }
            else
            {
                packageName = packageArg.ToLowerInvariant();
                version = "";
            }

            // Validate version looks like a NuGet version (allow wildcard patterns like 11.0.0-preview*)
            // "latest" is handled as a special tag by PackageExtractor
            if (version.Length > 0
                && !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase)
                && !version.Contains('*')
                && !NuGet.Versioning.NuGetVersion.TryParse(version, out _))
            {
                string badVersion = packageArgs.Length >= 2 ? packageArgs[1] : version;
                Console.Error.WriteLine($"Error: '{badVersion}' is not a valid package version.");
                Console.Error.WriteLine("Versions look like: 1.0.0, 8.0.5, 13.0.3-beta1, 11.0.0-preview*");
                Console.Error.WriteLine($"To list available versions: dotnet-inspect package {packageName} --versions");
                return 1;
            }
        }

        string? extractPath = null;
        PackageExtractionResult? resolution = null;

        try
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(
                client,
                isLocalFile ? packageArgs[0] : packageName,
                logger.Log,
                sourceOptions: options.SourceOptions,
                version: isLocalFile ? null : (version.Length > 0 ? version : null),
                forceLatest: options.ForceLatest,
                includePrerelease: options.IncludePrerelease);

            if (!outcome.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                return 1;
            }
            resolution = outcome.Result!;

            extractPath = resolution.ExtractPath;
            // Update version from resolution (may have been auto-discovered)
            version = resolution.Version ?? version;

            // Handle --layout mode: show file tree and exit early
            if (options.ListLayout)
            {
                ListPackageLayout(extractPath, options, packageName, options.TipLevel);
                return 0;
            }

            // Handle --files mode: list files and exit early
            if (options.ListFiles)
            {
                ListPackageFiles(extractPath, options, packageName, options.TipLevel);
                return 0;
            }

            // Handle --tfms mode: list target frameworks and exit early
            if (options.ListTfms)
            {
                ListPackageTfms(extractPath, options.Tsv, options.Jsonl);
                return 0;
            }

            // Parse nuspec (needed for --readme and --dependencies early exits, and full inspection)
            NuspecData? nuspec = null;
            string[] nuspecFiles = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
            if (nuspecFiles.Length > 0)
                nuspec = Services.NuspecParser.Parse(nuspecFiles[0]);

            // Handle --readme mode: print README and exit early
            if (options.ShowReadme)
            {
                return PrintReadme(extractPath, nuspec?.ReadmeFile, options);
            }

            // Handle --dependencies mode: resolve transitive deps and show tree
            if (options.ShowDependencies)
            {
                Console.Error.WriteLine("Tip: use 'depends --package' for dependency trees.");
                var depResult = new InspectionResult { PackageName = packageName, Version = version };
                if (nuspec != null) ApplyNuspec(nuspec, depResult);
                return await ShowDependencyTreeAsync(client, depResult, options, logger);
            }

            if (options.AllLibraries)
            {
                return await ExecutePackageAllLibrariesAsync(
                    extractPath,
                    isLocalFile,
                    packageArgs[0],
                    packageName,
                    version,
                    options);
            }

            if (options.PackageLibrary != null)
            {
                return await ExecutePackageLibraryAsync(
                    extractPath,
                    isLocalFile,
                    packageArgs[0],
                    packageName,
                    version,
                    options);
            }

            long? packageSize = null;
            if (resolution.NupkgPath != null && File.Exists(resolution.NupkgPath))
            {
                packageSize = new FileInfo(resolution.NupkgPath).Length;
            }

            bool wantsSignals = options.IncludeSections?.Contains(PackageSections.Signals) == true;

            var result = await PackageInspector.InspectAsync(
                extractPath, packageName, version, isLocalFile,
                isLocalFile ? packageArgs[0] : null,
                nuspec, client, logger, options.ForceLatest, options.Verbosity,
                resolution.NupkgPath,
                fetchMetadata: wantsSignals);

            // Apply package size (not cached in index — comes from nupkg file)
            if (packageSize.HasValue)
                result.PackageSize = packageSize;

            // Verify package signature if nupkg is available
            if (resolution.NupkgPath != null && (options.Verbosity >= Verbosity.Normal || wantsSignals))
            {
                logger.Log($"Verifying package signature: {Path.GetFileName(resolution.NupkgPath)}");
                result.SignatureResult = await SignatureVerifier.VerifyAsync(resolution.NupkgPath);
            }

            result.Source = isLocalFile ? SourceKind.File : SourceKind.NuGet;

            // Filter output based on options
            FilterResultForOutput(result, options);

            if (wantsSignals)
            {
                result.BinarySignals = await PackageInspector.ScanBinarySignalsAsync(
                    extractPath, packageName, version, client, logger, acquirePdb: true);
            }

            if (wantsSignals)
                await AuditSignalBuilder.PopulatePackageAuditAsync(result, client, logger);

            // Output results
            if (effectiveDiscovery)
            {
                var effective = pipeline.GetAvailableSections(result, options.IncludeSections);
                var schemaMap = InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema();
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
                        var rendered = MarkoutSerializer.Serialize(view, InspectionContext.Default, writerOpts);
                        schemaMap = FilterSchemaToEffectiveFields(effective, schemaMap, rendered);
                    }
                }

                return DiscoverOutput.ExecuteEffective(options.Discover, effective, schemaMap,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
                    verbosity: (int)userVerbosity, rootLabel: $"package {packageName}", fullSchema: fullSchemaMap,
                    sectionCostAnnotations: pipeline.GetCostAnnotations(),
                    sectionCategories: pipeline.GetCategoryMap());
            }
            WarnEmptySections(result, options, pipeline);
            bool hasProjection = options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };
            if (options.Count)
            {
                Console.WriteLine(OutputFormatter.FormatResult(result, options, pipeline));
            }
            else if (options.OneLine)
            {
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
                    var sw = new StringWriter();
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
            Console.Error.WriteLine($"Error: Package '{packageName}' version '{version}' not found on nuget.org.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --versions' to list available versions.");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Failed to download package: {ex.Message}");
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

    private static bool ValidatePackageLibraryMode(InspectionOptions options)
    {
        List<string> conflicts = [];
        if (options.AllLibraries) conflicts.Add("--all-libraries");
        if (options.ListLayout) conflicts.Add("--layout");
        if (options.ListFiles) conflicts.Add("--files");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.ListVersions) conflicts.Add("--versions/--version/--latest-version");
        if (options.ShowReadme) conflicts.Add("--readme");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
        if (string.Equals(options.Tfm, "all", StringComparison.OrdinalIgnoreCase)) conflicts.Add("--tfm all");

        if (conflicts.Count == 0)
            return true;

        Console.Error.WriteLine($"Error: --library cannot be combined with {string.Join(", ", conflicts)}.");
        return false;
    }

    private static bool ValidatePackageAllLibrariesMode(InspectionOptions options)
    {
        List<string> conflicts = [];
        if (options.PackageLibrary != null) conflicts.Add("--library");
        if (options.ListLayout) conflicts.Add("--layout");
        if (options.ListFiles) conflicts.Add("--files");
        if (options.ListTfms) conflicts.Add("--tfms");
        if (options.ListVersions) conflicts.Add("--versions/--version/--latest-version");
        if (options.ShowReadme) conflicts.Add("--readme");
        if (options.ShowDependencies) conflicts.Add("--dependencies");
        if (options.Discover != null) conflicts.Add("-D/--discover");
        if (options.Columns != null) conflicts.Add("--columns");
        if (options.Fields != null) conflicts.Add("--fields");

        if (conflicts.Count > 0)
        {
            Console.Error.WriteLine($"Error: --all-libraries cannot be combined with {string.Join(", ", conflicts)}.");
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

    private sealed record PackageLibrarySelection(string Path);

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
            options.Select, pipeline.AllSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
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
                logger.Log($"Warning: Could not read library: {Path.GetFileName(selection.Path)}");
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
            Console.Error.WriteLine($"Error: No libraries could be read from package '{packageName}'.");
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
            Console.Error.WriteLine("Note: matched sections have no data across all libraries.");
            return 0;
        }

        if (libraryOptions.OneLineExplicitlySet)
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
            JsonOutput = options.JsonOutput,
            OneLine = options.OneLine,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            OneLineExplicitlySet = options.OneLineExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            Format = options.JsonOutput ? OutputFormat.Json
                : options.Jsonl ? OutputFormat.Jsonl
                : options.Tsv ? OutputFormat.Tsv
                : options.OneLine ? OutputFormat.Table
                : OutputFormat.Markdown,
            Verbose = options.Verbose,
            Verbosity = options.Verbosity,
            IncludeSections = options.IncludeSections,
            Discover = options.Discover,
            Tree = options.Tree,
            Select = options.Select,
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

        if (!string.IsNullOrWhiteSpace(requestedLibrary))
        {
            var (matchedAssembly, matchedTfm) = TfmSelector.FindAssemblyInPackage(extractPath, requestedLibrary, options.Tfm);
            if (matchedAssembly != null)
                return new PackageLibrarySelection(matchedAssembly);

            Console.Error.WriteLine($"Error: Library '{requestedLibrary}' not found in package '{packageName}'.");
            WritePackageLibraryCandidates(extractPath, packageName, version, options.Tfm);
            return null;
        }

        var candidates = TfmSelector.GetPackageDlls(extractPath)
            .Where(path => !path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine($"Error: No DLLs found in package '{packageName}'.");
            return null;
        }

        string? selectedTfm = options.Tfm;
        List<string> pool;
        if (!string.IsNullOrWhiteSpace(options.Tfm))
        {
            pool = candidates
                .Where(path => string.Equals(
                    TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, path).Replace('\\', '/')),
                    options.Tfm,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (pool.Count == 0)
            {
                Console.Error.WriteLine($"Error: No library found for TFM '{options.Tfm}' in package '{packageName}'.");
                WritePackageLibraryCandidates(extractPath, packageName, version, options.Tfm);
                return null;
            }
        }
        else
        {
            var (highestTfmDlls, highestTfm) = TfmSelector.SelectHighestTfmAssemblies(candidates, extractPath);
            pool = highestTfmDlls.Count > 0 ? highestTfmDlls : candidates;
            selectedTfm = highestTfm;
        }

        if (pool.Count == 1)
            return new PackageLibrarySelection(pool[0]);

        var packageNameMatch = pool
            .Where(path => Path.GetFileNameWithoutExtension(path).Equals(packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (packageNameMatch.Count == 1)
            return new PackageLibrarySelection(packageNameMatch[0]);

        Console.Error.WriteLine(selectedTfm == null
            ? $"Error: Package '{packageName}' contains multiple libraries."
            : $"Error: Package '{packageName}' contains multiple libraries for {selectedTfm}.");
        WritePackageLibraryCandidates(extractPath, packageName, version, selectedTfm, pool);
        return null;
    }

    private static List<PackageLibrarySelection>? ResolveAllPackageLibraries(
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        var candidates = TfmSelector.GetPackageDlls(extractPath)
            .Where(path => !path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine($"Error: No DLLs found in package '{packageName}'.");
            return null;
        }

        List<string> selected;
        if (string.Equals(options.Tfm, "all", StringComparison.OrdinalIgnoreCase))
        {
            selected = candidates;
        }
        else if (!string.IsNullOrWhiteSpace(options.Tfm))
        {
            selected = candidates
                .Where(path => string.Equals(
                    TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, path).Replace('\\', '/')),
                    options.Tfm,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (selected.Count == 0)
            {
                Console.Error.WriteLine($"Error: No libraries found for TFM '{options.Tfm}' in package '{packageName}'.");
                WritePackageLibraryCandidates(extractPath, packageName, version, options.Tfm);
                return null;
            }
        }
        else
        {
            var (highestTfmDlls, highestTfm) = TfmSelector.SelectHighestTfmAssemblies(candidates, extractPath);
            selected = highestTfmDlls.Count > 0 ? highestTfmDlls : candidates;
            if (highestTfm != null)
                Console.Error.WriteLine($"Using TFM: {highestTfm}");
        }

        return selected
            .OrderBy(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .Select(path => new PackageLibrarySelection(path))
            .ToList();
    }

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
            Console.Error.WriteLine("Error: --all-libraries row output requires one concrete section; category selectors such as @Integrations produce multi-section documents.");
            Console.Error.WriteLine("Use Markdown output for categories, or select a section such as Integrations, Configuration, or Library Info.");
            return false;
        }

        if (sections.Count != 1)
        {
            Console.Error.WriteLine($"Error: --all-libraries row output requires exactly one section; matched {sections.Count}: {string.Join(", ", sections)}.");
            Console.Error.WriteLine("Use Markdown output for multi-section selections, or select one concrete section.");
            return false;
        }

        var table = BuildAllLibrariesTable(packageName, version, inspections, sections[0]);
        if (table == null)
        {
            Console.Error.WriteLine($"Error: --all-libraries row output does not support section: {sections[0]}.");
            Console.Error.WriteLine("Use Markdown output, or select Library Info, Integrations, Switches, Integration Opportunities, or a focused integration section.");
            return false;
        }

        if (table.Rows.Length == 0)
        {
            Console.Error.WriteLine("Note: matched section has no row data across all libraries.");
            return true;
        }

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(table.Headers, table.StableHeaders, table.Rows);
            markoutWriter.Flush();
        });
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

        if (section.Equals(EcosystemIntegrationNames.Integrations, StringComparison.OrdinalIgnoreCase))
        {
            var integrationRows = inspections
                .SelectMany(inspection => LibraryIntegrationCatalog.All
                    .Select(descriptor => new { Inspection = inspection, Descriptor = descriptor, Signals = descriptor.GetSignals(inspection) })
                    .Where(row => row.Signals is { Count: > 0 })
                    .Select(row => WithProvenance(
                        packageName,
                        version,
                        row.Inspection,
                        row.Descriptor.Name,
                        row.Descriptor.CountRenderedRows(row.Signals!).ToString())))
                .ToArray();
            return new(
                ["Package", "Version", "Library", "TFM", "Integration", "APIs"],
                ["package", "version", "library", "tfm", "integration", "apis"],
                integrationRows);
        }

        if (section.Equals("Integration Opportunities", StringComparison.OrdinalIgnoreCase))
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
                .SelectMany(inspection => (inspection.Switches ?? [])
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
            d.Name.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
            return null;

        var signals = inspections
            .SelectMany(inspection => (descriptor.GetSignals(inspection) ?? [])
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
        => section.Equals(EcosystemIntegrationNames.Integrations, StringComparison.OrdinalIgnoreCase)
           || section.Equals("Integration Opportunities", StringComparison.OrdinalIgnoreCase)
           || section.Equals("Switches", StringComparison.OrdinalIgnoreCase)
           || LibraryIntegrationCatalog.All.Any(descriptor => descriptor.Name.Equals(section, StringComparison.OrdinalIgnoreCase));

    private static void AppendAggregatedSection(StringBuilder sb, string section, List<LibraryInspection> inspections)
    {
        if (section.Equals(EcosystemIntegrationNames.Integrations, StringComparison.OrdinalIgnoreCase))
        {
            var integrationRows = LibraryIntegrationCatalog.All
                .Select(descriptor =>
                {
                    var count = inspections.Sum(inspection =>
                    {
                        var signals = descriptor.GetSignals(inspection);
                        return signals is { Count: > 0 } ? descriptor.CountRenderedRows(signals) : 0;
                    });
                    return new { descriptor.Name, Count = count };
                })
                .Where(row => row.Count > 0)
                .ToList();
            if (integrationRows.Count == 0)
                return;

            AppendHeading(sb, section);
            AppendTable(sb, ["Integration", "APIs"], integrationRows.Select(row => new[] { row.Name, row.Count.ToString() }));
            return;
        }

        if (section.Equals("Integration Opportunities", StringComparison.OrdinalIgnoreCase))
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
                .SelectMany(inspection => (inspection.Switches ?? [])
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
            d.Name.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
            return;

        var signals = inspections
            .SelectMany(inspection => (descriptor.GetSignals(inspection) ?? [])
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
        candidates ??= TfmSelector.GetPackageDlls(extractPath)
            .Where(path => !path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(tfm)
                || string.Equals(
                    TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, path).Replace('\\', '/')),
                    tfm,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count > 0)
        {
            Console.Error.WriteLine("Available libraries:");
            foreach (var candidate in candidates
                         .Select(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"  {candidate}");
            }
        }

        var packageReference = !string.IsNullOrWhiteSpace(version)
            ? $"{packageName}@{version}"
            : packageName;
        Console.Error.WriteLine();
        Console.Error.WriteLine("Use:");
        Console.Error.WriteLine($"  dotnet-inspect package {packageReference} --library <dll>");
    }

    private static void WarnEmptySections(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var (empty, requested) = pipeline.GetEmptySections(result, options.Verbosity, options.IncludeSections);
        if (empty.Count > 0 && empty.Count == requested)
        {
            var label = empty.Count == 1 ? "section has" : "sections have";
            Console.Error.WriteLine($"Note: {empty.Count} matched {label} no data: {string.Join(", ", empty)}.");
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

    private static DocumentSchema FilterSchemaToEffectiveFields(
        List<string> effectiveSections, DocumentSchema schema, string rendered)
    {
        var filtered = new DocumentSchema();
        foreach (var name in effectiveSections)
        {
            var section = schema.GetSection(name);
            if (section == null) { filtered.AddSection(name); continue; }

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

    private static void ListPackageLayout(string extractPath, InspectionOptions options, string packageName, TipLevel tipLevel)
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
                Console.Error.WriteLine($"Error: TFM '{options.Tfm}' not found. Use --tfms to list available frameworks.");
                return;
            }

            // Show paths relative to parent of TFM dir so TFM appears as root node
            relativeBase = Path.GetDirectoryName(searchPath)!;
        }
        else
        {
            var (resolved, error) = ResolveScopedPath(extractPath, options);
            if (error != null)
            {
                Console.Error.WriteLine(error);
                return;
            }
            searchPath = resolved;
            relativeBase = extractPath;
        }

        string[] files = Directory.GetFiles(searchPath, "*", SearchOption.AllDirectories);

        var relativePaths = files
            .Select(f => Path.GetRelativePath(relativeBase, f))
            .Where(p => !p.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.StartsWith("_rels", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.StartsWith("[Content_Types]", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p);

        var results = options.Limit.HasValue 
            ? relativePaths.Take(options.Limit.Value).ToList()
            : relativePaths.ToList();

        PackageOutputFormatter.WriteFileTree(results);
        WriteFileLayoutTips(extractPath, options, packageName, tipLevel, isLayout: true);
    }

    private static void ListPackageFiles(string extractPath, InspectionOptions options, string packageName, TipLevel tipLevel)
    {
        string searchPath;
        bool useFileNameOnly = false;

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
                Console.Error.WriteLine($"Error: TFM '{options.Tfm}' not found. Use --tfms to list available frameworks.");
                return;
            }
            useFileNameOnly = true;
        }
        else
        {
            var (resolved, error) = ResolveScopedPath(extractPath, options);
            if (error != null)
            {
                Console.Error.WriteLine(error);
                return;
            }
            searchPath = resolved;
        }

        string[] files = Directory.GetFiles(searchPath, "*", SearchOption.AllDirectories);

        // Use bare filenames when scoped to a specific TFM, relative paths otherwise
        var fileNames = files
            .Select(f => useFileNameOnly
                ? Path.GetFileName(f)
                : Path.GetRelativePath(extractPath, f).Replace('\\', '/'))
            .Where(p => !p.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.StartsWith("_rels", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.StartsWith("[Content_Types]", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p);

        var results = options.Limit.HasValue
            ? fileNames.Take(options.Limit.Value)
            : fileNames;

        OutputFormatter.WriteStringList(results, "Path", "Path", options.Tsv, options.Jsonl, Console.Out);
        WriteFileLayoutTips(extractPath, options, packageName, tipLevel, isLayout: false);
    }

    internal static void WriteFileLayoutTips(string extractPath, InspectionOptions options, string packageName, TipLevel tipLevel, bool isLayout)
    {
        // Tips are not shown for --files / --layout modes
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

    private static void ListPackageTfms(string extractPath, bool tsv, bool jsonl)
    {
        var dlls = TfmSelector.GetPackageDlls(extractPath);
        var tfms = dlls
            .Select(d => TfmResolver.ExtractTfmFromPath(
                Path.GetRelativePath(extractPath, d).Replace('\\', '/')))
            .Where(t => t != null)
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => TfmResolver.GetTfmPriority(t))
            .ToList();

        OutputFormatter.WriteStringList(tfms, "TFM", "Tfm", tsv, jsonl, Console.Out);
    }

    private static async Task<int> ShowDependencyTreeAsync(
        HttpClient client, InspectionResult result, InspectionOptions options, VerboseLogger logger)
    {
        if (result.DependencyGroups is not { Count: > 0 })
        {
            Console.Error.WriteLine("No dependencies declared in package.");
            return 0;
        }

        // Pick TFM: explicit --tfm, or highest available
        var tfm = options.Tfm;
        DependencyGroup? group;
        if (!string.IsNullOrEmpty(tfm))
        {
            group = result.DependencyGroups.FirstOrDefault(g =>
                g.TargetFramework.Equals(tfm, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                Console.Error.WriteLine($"Error: No dependencies found for TFM '{tfm}'.");
                Console.Error.WriteLine("Available TFMs: " + string.Join(", ", result.DependencyGroups.Select(g => g.TargetFramework)));
                return 1;
            }
        }
        else
        {
            group = result.DependencyGroups
                .OrderByDescending(g => TfmResolver.GetTfmPriority(g.TargetFramework))
                .First();
            tfm = group.TargetFramework;
        }

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
            Title = $"{result.PackageName} {result.Version}",
            Dependencies = ToTreeNodes(depNodes)
        };

        MarkoutSerializer.Serialize(view, Console.Out, PackageDependenciesContext.Default);
        return 0;
    }

    private static List<TreeNode> ToTreeNodes(List<DependencyNode> nodes)
    {
        return nodes.Select(n =>
        {
            var label = !string.IsNullOrEmpty(n.Author)
                ? $"{n.PackageId} {n.Version} [{n.Author}]"
                : $"{n.PackageId} {n.Version}";
            return n.Children.Count > 0
                ? new TreeNode(label) { Children = ToTreeNodes(n.Children) }
                : new TreeNode(label);
        }).ToList();
    }

    private static int PrintReadme(string extractPath, string? readmeFile, InspectionOptions options)
    {
        // Use nuspec-specified readme file or fall back to README.md
        string readmeFileName = readmeFile ?? "README.md";
        string readmePath = Path.Combine(extractPath, readmeFileName);
        
        if (!File.Exists(readmePath))
        {
            Console.Error.WriteLine("Error: This package does not contain a readme file.");
            return 1;
        }

        string content = File.ReadAllText(readmePath);
        
        if (!string.IsNullOrEmpty(options.OutputPath))
        {
            File.WriteAllText(options.OutputPath, content);
        }
        else
        {
            Console.WriteLine(content);
        }
        
        return 0;
    }
}
