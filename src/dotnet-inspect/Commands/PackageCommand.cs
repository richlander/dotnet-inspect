using DotnetInspector.Models;
using System.Runtime.InteropServices;
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

        // Static discovery mode: -D --schema lists schema without resolving/loading the package.
        // Also keep no-target package discovery static because there is no target to make effective.
        if (options.Discover != null && (options.Schema || packageArgs.Length < 1))
        {
            var schemaMap = InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema();
            return DiscoverOutput.Execute(options.Discover, schemaMap,
                tree: options.Tree, json: options.JsonOutput, markdown: !options.OneLine && !options.JsonOutput,
                verbosity: (int)options.Verbosity,
                sectionCostAnnotations: pipeline.GetCostAnnotations());
        }

        // -D defaults to effective discovery for target-based commands.
        bool effectiveDiscovery = options.Discover != null && !options.Schema;
        var userVerbosity = options.Verbosity; // preserve for display formatting
        if (effectiveDiscovery)
            options = options with { Verbosity = Verbosity.Detailed };

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(options.Select, sectionNames, pipeline.InfoSectionNames);
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
            options = options with { IncludeSections = selectResult.Sections };

        if (options.Count && !CountOutput.ValidateSingleSection(options.IncludeSections))
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
            foreach (var section in options.IncludeSections)
                ProjectionDiagnostics.ValidateProjection(schemaMap, section, options.Fields, options.Columns);
        }

        if (packageArgs.Length < 1)
        {
            Console.Error.WriteLine("Error: Package name or path required.");
            Console.Error.WriteLine("Run 'dotnet-inspect package --help' for usage.");
            return 1;
        }

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

            var versionWriter = new Markout.MarkoutWriter(Console.Out, new Markout.OneLineFormatter(showHeader: false));
            versionWriter.WriteList(CollectionsMarshal.AsSpan(versions));

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
                ListPackageTfms(extractPath);
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
                    tree: options.Tree, json: options.JsonOutput, markdown: !options.OneLine && !options.JsonOutput,
                    verbosity: (int)userVerbosity, rootLabel: $"package {packageName}", fullSchema: fullSchemaMap,
                    sectionCostAnnotations: pipeline.GetCostAnnotations());
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
                    if (options.OneLineExplicitlySet && options.IncludeSections is { Count: > 1 })
                    {
                        Console.Error.WriteLine($"Error: Selection matches {diagnostic.Sections.Length} sections: {string.Join(", ", diagnostic.Sections)}.");
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("Oneline format displays one section at a time.");
                        Console.Error.WriteLine("Use -S with a specific section name, or --markdown for multi-section output.");
                        return 1;
                    }

                    // Narrow to the Package Info section for oneline
                    options = options with { IncludeSections = new HashSet<string> { PackageSections.PackageInfo } };
                }

                if (hasProjection)
                {
                    // Capture output for projection diagnostics
                    var sw = new StringWriter();
                    var writerOpts = OutputFormatter.BuildWriterOptions(result, options, pipeline);
                    var view = new InspectionResultView(result);
                    MarkoutSerializer.Serialize(view, sw, new OneLineFormatter(showHeader: !options.NoHeader), InspectionContext.Default, writerOpts);
                    var rendered = sw.ToString();
                    ProjectionDiagnostics.DiagnoseRendered(options.Fields ?? options.Columns, rendered);
                    Console.Out.Write(rendered);
                }
                else
                {
                    OutputFormatter.WritePackageOneLine(result, options, pipeline, showHeader: !options.NoHeader);
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

        var fileWriter = new Markout.MarkoutWriter(Console.Out, new Markout.OneLineFormatter(showHeader: false));
        fileWriter.WriteList(results.ToArray());
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

    private static void ListPackageTfms(string extractPath)
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

        var tfmWriter = new Markout.MarkoutWriter(Console.Out, new Markout.OneLineFormatter(showHeader: false));
        tfmWriter.WriteList(CollectionsMarshal.AsSpan(tfms));
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
