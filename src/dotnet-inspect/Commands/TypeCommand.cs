using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using Markout;
using DotnetInspector.Services;
using DotnetInspector.Views;

using Decompiler = ILInspector.Decompiler;

namespace DotnetInspector.Commands;

/// <summary>
/// Discovers types in a package or library (terse, no docs by default).
/// </summary>
public static class TypeCommand
{
    public const string Name = "type";

    public static async Task<int> ExecuteAsync(TypeOptions options)
    {
        // Shared preamble: section validation, discovery, verbosity promotion
        var (preamble, error) = ApiCommand.RunPreamble(options);
        if (error.HasValue) return error.Value;

        options = (TypeOptions)preamble.Options;
        var typePipeline = preamble.TypePipeline;
        var memberPipeline = preamble.MemberPipeline;

        try
        {
            if (await TryExecutePlatformPrefixBrowseAsync(options, typePipeline) is { } prefixBrowseExitCode)
                return prefixBrowseExitCode;
            if (await TryExecuteFindIfMissAsync(options) is { } findIfMissExitCode)
                return findIfMissExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        // Shared source resolution
        var (source, sourceError) = await ApiCommand.ResolveSourceAsync(options);
        if (sourceError.HasValue)
        {
            NamespacePrefixHints.WriteIfLikelyBareTypeName(options.OriginalTypeQuery ?? options.PackagePath ?? options.TypeName ?? "");
            return sourceError.Value;
        }

        var searchPath = source.SearchPath;
        var runtimeAssemblyPath = source.RuntimeAssemblyPath;
        var packageName = source.PackageName;
        var packageVersion = source.PackageVersion;
        var apiSource = source.ApiSource;
        var apiVersion = source.ApiVersion;
        var selectedTfm = source.SelectedTfm;
        var tempDir = source.TempDir;
        var typeName = source.TypeName;
        var originalTypeQuery = options.OriginalTypeQuery ?? options.TypeName;
        var context = source.Context;
        var logger = context.Logger;

        try
        {
            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - list all types
                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from library.");
                    return 1;
                }

                if (apiDllPath != null)
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);

                if (!string.IsNullOrEmpty(options.PackagePath))
                {
                    var (pkgName, _) = PackageExtractor.ParsePackageReference(options.PackagePath);
                    api.Name = pkgName;
                }
                else if (apiDllPath != null)
                {
                    api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                }

                var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                api.Tfm = selectedTfm;
                api.Source = apiSource;
                api.Version = apiVersion;
                api.Library = apiDllPath != null ? Path.GetFileName(apiDllPath) : null;

                // --columns Description implicitly enables doc enrichment (local XML only)
                var listOptions = options;
                if (options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true)
                    listOptions = options with { ShowDocs = true };

                if (pdbLookupPath != null && listOptions.ShowDocs)
                    SourceEnricher.EnrichFromLocalXmlDocs(api.Types, pdbLookupPath, listOptions, logger);

                if (options.EffectiveDiscovery)
                {
                    ApiCommand.ApplySurfaceFilters(api, options, options.TypeFilter);
                    var schema = ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();
                    var effective = typePipeline.GetAvailableSections(api, options.IncludeSections);
                    return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
                        tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
                        verbosity: (int)options.Verbosity,
                        sectionCostAnnotations: typePipeline.GetCostAnnotations(),
                        sectionCategories: typePipeline.GetCategoryMap());
                }

                ApiCommand.WriteFullApiOutput(api, options, selectedTfm);

                if (!options.FormatExplicitlySet && !options.IsRawOutput)
                {
                    var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                        : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                        : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                        : "";

                    // Pick a representative type: prefer the one with most members
                    var exampleType = api.Types
                        .OrderByDescending(t => t.Members.Count)
                        .FirstOrDefault();

                    if (exampleType != null)
                    {
                        var simpleName = exampleType.FullName.Contains('.')
                            ? exampleType.FullName[(exampleType.FullName.LastIndexOf('.') + 1)..] : exampleType.FullName;

                        List<Tip> tips =
                        [
                            new(MemberCommand.Name, $"{simpleName} {sourceFlag}", "inspect type members"),
                            new(Name, $"{sourceFlag} --shape", "view type shape"),
                            new(Name, $"-t \"*Writer*\" {sourceFlag}", "filter types by pattern"),
                        ];

                        Hints.WriteTips(options.TipLevel, [.. tips]);
                    }
                }
            }
            else
            {
                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from library.");
                    return 1;
                }

                if (apiDllPath != null)
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);

                var allTypeNames = api.Types.Select(t => t.FullName).ToList();
                var lookupResult = TypeMatcher.Lookup(allTypeNames, typeName);

                if (lookupResult.Match != null)
                {
                    var apiType = api.Types.First(t => t.FullName == lookupResult.Match);

                    // Check each member filter before producing output
                    if (options.MemberFilter.Count > 0)
                    {
                        var memberNames = apiType.Members.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        List<string> missedFilters = [];

                        foreach (var filter in options.MemberFilter)
                        {
                            bool isGlob = filter.Contains('*') || filter.Contains('?');
                            bool anyMatch = isGlob
                                ? memberNames.Any(n => TypeMatcher.MatchesGlob(n, filter))
                                : memberNames.Any(n => string.Equals(n, filter, StringComparison.OrdinalIgnoreCase));

                            if (!anyMatch)
                                missedFilters.Add(filter);
                        }

                        if (missedFilters.Count > 0)
                        {
                            Console.Error.WriteLine($"Error: No members matched filter '{string.Join(", ", missedFilters)}'");
                            var memberResult = TypeMatcher.LookupMembers(memberNames, missedFilters);
                            if (memberResult.Suggestions.Count > 0)
                            {
                                Console.Error.WriteLine();
                                Console.Error.WriteLine("Did you mean:");
                                foreach (var s in memberResult.Suggestions)
                                    Console.Error.WriteLine($"  {s}");
                            }
                            return 1;
                        }
                    }

                    var foundIn = apiDllPath != null ? Path.GetFileNameWithoutExtension(apiDllPath) : null;

                    // Default --docs on for single-type view at Normal+ unless explicitly disabled
                    TypeOptions effectiveOptions = options;
                    if (!options.DocsExplicitlySet && options.Verbosity >= Verbosity.Normal)
                        effectiveOptions = options with { ShowDocs = true };

                    // The resolved assembly path enables decompiler-backed
                    // sections (whole-type Decompiled Source).
                    effectiveOptions = effectiveOptions with { DllPath = apiType.SourceAssemblyPath ?? runtimeAssemblyPath ?? apiDllPath };

                    // Real local names for the listing: acquire the portable
                    // PDB the same way the member command does — only when the
                    // section is actually requested (network).
                    if (effectiveOptions.DllPath is { } dllForPdb
                        && effectiveOptions.IncludeSections is { Count: > 0 }
                        && ApiCommand.GetRequestedMemberSections(apiType, effectiveOptions)
                            .Contains(SectionNames.DecompiledSource))
                    {
                        var pdbPath = await ApiCommand.TryAcquirePdbPathAsync(
                            dllForPdb, effectiveOptions, logger, context.HttpClient);
                        effectiveOptions = effectiveOptions with { PdbPath = pdbPath };
                    }

                    if (ShouldRejectQuietShape(effectiveOptions))
                    {
                        Console.Error.WriteLine("Error: -v:q is not supported by the type shape renderer.");
                        Console.Error.WriteLine("Use -v:m, -v:n, or -v:d for tree output, or add --markdown -v:q for compact section output.");
                        return 1;
                    }

                    // Default --shape on for single-type view when the user is not running a
                    // section/projection query and did not explicitly choose another renderer.
                    // Verbosity grows the tree view; --markdown opts into the section/document view.
                    if (!effectiveOptions.ShapeExplicitlySet && ShouldDefaultToShape(effectiveOptions))
                        effectiveOptions = effectiveOptions with { ShapeOutput = true };

                    // Explicit --shape cannot honor a section/projection query; warn rather than
                    // silently dropping the selection.
                    if (effectiveOptions is { ShapeOutput: true, HasSectionQuery: true, Count: false })
                        Console.Error.WriteLine("Warning: --shape does not support -S/--columns/--fields; selection was ignored.");

                    // Enrich with local XML docs only (source info is in the source command)
                    {
                        var dllPath = runtimeAssemblyPath ?? apiDllPath;
                        if (dllPath != null && effectiveOptions.ShowDocs)
                            SourceEnricher.EnrichFromLocalXmlDocs(apiType, dllPath, effectiveOptions, logger);
                    }

                    if (effectiveOptions.EffectiveDiscovery)
                    {
                        return ApiCommand.ExecuteEffectiveDiscovery(apiType, memberPipeline, effectiveOptions);
                    }

                    bool hasProjection = effectiveOptions.Columns is { Length: > 0 } || effectiveOptions.Fields is { Length: > 0 };
                    bool tabularProjection = hasProjection
                        && !effectiveOptions.JsonOutput
                        && !effectiveOptions.Count
                        && effectiveOptions is not TypeOptions { ShapeOutput: true };

                    // Pre-render: validate --columns/--fields names against the section schema
                    // (catches typos) when a specific section is selected, mirroring the package path.
                    if (tabularProjection && effectiveOptions.IncludeSections is { Count: > 0 })
                    {
                        var projSchema = ApiCommand.GetTypeDocumentSchema(effectiveOptions);
                        if (!ProjectionDiagnostics.ValidateProjection(projSchema, effectiveOptions.IncludeSections, effectiveOptions.Fields, effectiveOptions.Columns))
                            return 1;
                    }

                    if (tabularProjection)
                    {
                        // Capture output so we can warn when a requested column produced no data
                        // (e.g. Select without --show-index, or a column not shown at this verbosity).
                        var sw = new StringWriter();
                        ApiCommand.WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions, sw);
                        var rendered = sw.ToString();
                        ProjectionDiagnostics.DiagnoseRendered(effectiveOptions.Fields ?? effectiveOptions.Columns, rendered);
                        Console.Out.Write(rendered);
                    }
                    else
                    {
                        ApiCommand.WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions);
                    }

                    // Notify when a requested section matched but has no data for this type.
                    // JSON and markdown both honor -S; tabular output falls back to showing all
                    // members and shape replaces selection, so skip those.
                    if (!effectiveOptions.OneLine
                        && effectiveOptions is not TypeOptions { ShapeOutput: true })
                    {
                        ApiCommand.WarnEmptySelectedSections(apiType, effectiveOptions, memberPipeline);
                    }

                    if (!effectiveOptions.FormatExplicitlySet && !effectiveOptions.IsRawOutput)
                    {
                        var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                            : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                            : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                            : "";

                        var simpleName = apiType.FullName.Contains('.')
                            ? apiType.FullName[(apiType.FullName.LastIndexOf('.') + 1)..] : apiType.FullName;

                        var overloadGroups = apiType.Members
                            .Where(ApiMemberSectionDescriptors.IsMethodLike)
                            .GroupBy(m => m.Name)
                            .OrderByDescending(g => g.Count())
                            .ToList();
                        var exampleGroup = overloadGroups.FirstOrDefault();

                        List<Tip> tips = [];

                        if (exampleGroup != null)
                        {
                            var memberName = exampleGroup.Key == ".ctor" ? ".ctor" : exampleGroup.Key;
                            tips.Add(new(MemberCommand.Name, $"{simpleName} {sourceFlag} {memberName}:1", "view member detail (source, IL)"));
                        }

                        if (overloadGroups.Any(g => g.Count() > 1))
                            tips.Add(new(MemberCommand.Name, $"{simpleName} {sourceFlag} --show-index", "show Name:N overload index"));

                        tips.Add(new(Name, $"{simpleName} {sourceFlag} --shape", "view type shape"));
                        tips.Add(new(MemberCommand.Name, $"-m {simpleName}.{(exampleGroup?.Key ?? "Method")} {sourceFlag}", "dotted member syntax"));

                        if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                            tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..{packageVersion} -t {simpleName}", "compare API changes"));

                        Hints.WriteTips(effectiveOptions.TipLevel, [.. tips]);
                    }
                }
                else if (!TryWritePrefixBrowse(
                    api,
                    apiDllPath,
                    originalTypeQuery,
                    typeName,
                    packageName,
                    apiSource,
                    apiVersion,
                    selectedTfm,
                    options))
                {
                    var widePrefixExitCode = await TryExecuteWidePlatformPrefixFallbackAsync(options, originalTypeQuery, typePipeline);
                    if (widePrefixExitCode.HasValue)
                        return widePrefixExitCode.Value;

                    if (lookupResult.Suggestions.Count > 0)
                    {
                        bool isGlob = typeName.Contains('*') || typeName.Contains('?');
                        if (isGlob)
                        {
                            // Glob matched multiple types — show types view with filter
                            AnnotateSurface(api, options, apiDllPath, packageName, apiSource, apiVersion, selectedTfm);

                            options = options with
                            {
                                TypeFilter = typeName,
                                Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
                            };

                            ApiCommand.WriteFullApiOutput(api, options, selectedTfm);
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                            Console.Error.WriteLine();
                            Console.Error.WriteLine("Did you mean:");
                            foreach (var s in lookupResult.Suggestions)
                                Console.Error.WriteLine($"  {s}");
                            return 1;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                        return 1;
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static Task<int?> TryExecuteWidePlatformPrefixFallbackAsync(
        TypeOptions options,
        string? originalTypeQuery,
        SectionPipeline<ApiSurface> typePipeline)
    {
        if (!options.AllowPlatformPrefixFallback || string.IsNullOrWhiteSpace(originalTypeQuery))
            return Task.FromResult<int?>(null);
        if (originalTypeQuery.Contains('*') || originalTypeQuery.Contains('?'))
            return Task.FromResult<int?>(null);

        return TryExecutePlatformPrefixBrowseAsync(options with
        {
            PlatformPrefixQuery = originalTypeQuery,
            AllowPlatformPrefixFallback = false
        }, typePipeline);
    }

    private static bool ShouldDefaultToShape(TypeOptions options)
        => !options.HasSectionQuery
           && !options.JsonOutput
           && !options.OneLine
           && !options.Tsv
           && !options.Jsonl
           && !options.NoHeader
           && !options.PlainText
           && !options.Count
           && !options.MarkdownExplicitlySet;

    private static bool ShouldRejectQuietShape(TypeOptions options)
        => !options.MarkdownExplicitlySet
           && !options.JsonOutput
           && !options.OneLine
           && !options.Tsv
           && !options.Jsonl
           && !options.NoHeader
           && !options.PlainText
           && !options.Count
           && !options.HasSectionQuery
           && options.Verbosity == Verbosity.Quiet;

    internal static async Task<int?> TryExecuteFindIfMissAsync(TypeOptions options)
    {
        var query = options.OriginalTypeQuery ?? options.PackagePath ?? options.TypeName;
        if (options.AssemblyPath != null || options.PlatformAssembly != null || options.TypeName != null)
            return null;

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var resolution = await TypeFindIfMissResolver.ResolvePlatformAsync(
            query,
            options.IncludeAll,
            options.SourceOptions,
            context.HttpClient,
            logger);
        if (resolution.Status == TypeFindIfMissStatus.None)
            return null;

        if (resolution.Status == TypeFindIfMissStatus.Found)
        {
            var match = resolution.Match!;
            Console.Error.WriteLine($"Note: Type '{query}' resolved via platform find to {match.FullName} in {match.Library}.");
            var routeOptions = options with
            {
                TypeName = match.FullName,
                PackagePath = null,
                PlatformAssembly = match.Library,
                PlatformFramework = match.Source,
                OriginalTypeQuery = match.FullName,
                PlatformPrefixQuery = null,
                AllowPlatformPrefixFallback = false
            };
            return await ExecuteAsync(routeOptions);
        }

        Console.Error.WriteLine($"Error: Type '{query}' matched multiple platform types. Use `find {query} --platform` to choose a source library.");
        return 1;
    }

    internal static async Task<int?> TryExecutePlatformPrefixBrowseAsync(
        TypeOptions options,
        SectionPipeline<ApiSurface> typePipeline)
    {
        var query = options.PlatformPrefixQuery;
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        if (await PackageExistsAsync(query, options, context))
            return null;

        var api = await BuildPlatformPrefixSurfaceAsync(query, options, context, logger);
        if (api == null || api.Types.Count == 0)
            return null;

        var browseOptions = options with
        {
            PackagePath = null,
            PlatformPrefixQuery = null,
            TypeFilter = null,
            ShapeOutput = false,
            ShapeExplicitlySet = false,
            Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
        };

        Console.Error.WriteLine($"Note: Showing best-effort platform prefix matches for '{query}'.");
        Console.Error.WriteLine($"Note: Use `find \"{ToFindPrefixPattern(query)}\" --platform` to see source libraries.");

        if (browseOptions.EffectiveDiscovery)
        {
            var schema = ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();
            var effective = typePipeline.GetAvailableSections(api, browseOptions.IncludeSections);
            return DiscoverOutput.ExecuteEffective(browseOptions.Discover, effective, schema,
                tree: browseOptions.Tree, json: browseOptions.JsonOutput, tsv: browseOptions.Tsv, jsonl: browseOptions.Jsonl, markdown: !browseOptions.OneLine && !browseOptions.JsonOutput,
                verbosity: (int)browseOptions.Verbosity,
                sectionCostAnnotations: typePipeline.GetCostAnnotations(),
                sectionCategories: typePipeline.GetCategoryMap());
        }

        ApiCommand.WriteFullApiOutput(api, browseOptions);
        return 0;
    }

    private static string ToFindPrefixPattern(string query)
        => query.EndsWith('*') ? query : $"{query}*";

    private static async Task<bool> PackageExistsAsync(string packageName, TypeOptions options, CommandContext context)
    {
        if (NuGetCache.TryGetLatestCachedVersion(packageName) != null)
            return true;

        try
        {
            var versions = await PackageExtractor.GetVersionsAsync(
                context.HttpClient,
                packageName,
                includePrerelease: true,
                limit: 1,
                log: context.Logger.Log,
                sourceOptions: options.SourceOptions);
            return versions is { Count: > 0 };
        }
        catch (Exception ex)
        {
            context.Logger.Log($"Could not query package versions for '{packageName}': {ex.Message}");
            return false;
        }
    }

    private static async Task<ApiSurface?> BuildPlatformPrefixSurfaceAsync(
        string query,
        TypeOptions options,
        CommandContext context,
        VerboseLogger logger)
    {
        var pattern = query.EndsWith('*') ? query : $"{query}*";
        var findOptions = new FindOptions
        {
            Pattern = pattern,
            PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames,
            IncludeAll = options.IncludeAll,
            Limit = options.Limit,
            SourceOptions = options.SourceOptions
        };

        List<string> tempDirs = [];
        var searchResults = await TypeSearchService.CollectTypesAsync(findOptions, pattern, logger, tempDirs, context.HttpClient);
        AssemblyCollector.CleanupTempDirs(tempDirs);

        var distinctResults = searchResults
            .Where(r => r.Assembly != null && r.Source != null)
            .DistinctBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctResults.Count == 0)
            return null;

        var resultNamesByAssembly = distinctResults
            .GroupBy(r => (Framework: r.Source!, Assembly: r.Assembly!, Version: r.SourceVersion))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var merged = new ApiSurface
        {
            Name = query,
            Source = SourceKind.Platform,
            Version = string.Join(", ", distinctResults.Select(r => $"{r.Source}@{r.SourceVersion}").Distinct()),
            Tfm = "platform"
        };

        foreach (var ((framework, assembly, _), fullNames) in resultNamesByAssembly)
        {
            var (assemblyPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                assembly,
                context.HttpClient,
                logger.Log,
                framework);
            if (assemblyPath == null || error != null)
            {
                logger.Log($"Warning: Could not resolve platform library '{assembly}' in {framework}: {error}");
                continue;
            }

            var api = AssemblyReader.ExtractApiSurface(assemblyPath, options.IncludeAll);
            if (api == null)
                continue;

            ApiServices.ResolveForwardedTypes(api, assemblyPath, logger, options.IncludeAll);

            foreach (var type in api.Types.Where(t => fullNames.Contains(t.FullName)))
            {
                type.SourceAssemblyPath = assemblyPath;
                merged.Types.Add(type);
            }
        }

        merged.Types = merged.Types
            .DistinctBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        RecomputeSurfaceCounts(merged);
        return merged;
    }

    private static bool TryWritePrefixBrowse(
        ApiSurface api,
        string? apiDllPath,
        string? originalTypeQuery,
        string resolvedTypeName,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        TypeOptions options)
    {
        if (string.IsNullOrWhiteSpace(originalTypeQuery))
            return false;
        if (originalTypeQuery.Contains('*') || originalTypeQuery.Contains('?'))
            return false;

        var matches = FindPrefixMatches(api.Types, originalTypeQuery);
        if (matches.Count == 0)
            return false;

        api.Types = matches;
        RecomputeSurfaceCounts(api);
        AnnotateSurface(api, options, apiDllPath, packageName, apiSource, apiVersion, selectedTfm);

        var browseOptions = options with
        {
            TypeFilter = null,
            ShapeOutput = false,
            ShapeExplicitlySet = false,
            Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
        };

        Console.Error.WriteLine($"Note: Type '{resolvedTypeName}' not found. Showing best-effort prefix matches for '{originalTypeQuery}'.");
        ApiCommand.WriteFullApiOutput(api, browseOptions, selectedTfm);
        return true;
    }

    private static List<ApiType> FindPrefixMatches(IEnumerable<ApiType> types, string query)
    {
        var normalized = TypeMatcher.Normalize(query.Trim());
        return types
            .Where(type => IsPrefixMatch(type.FullName, normalized) || IsPrefixMatch(type.Name, normalized))
            .ToList();
    }

    private static bool IsPrefixMatch(string candidate, string prefix)
        => candidate.Length > prefix.Length
           && candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static void AnnotateSurface(
        ApiSurface api,
        TypeOptions options,
        string? apiDllPath,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm)
    {
        if (!string.IsNullOrEmpty(options.PackagePath))
        {
            var (pkgName, _) = PackageExtractor.ParsePackageReference(options.PackagePath);
            api.Name = pkgName;
        }
        else if (apiDllPath != null)
        {
            api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
        }

        api.Tfm = selectedTfm;
        api.Source = apiSource;
        api.Version = apiVersion;
        api.Library = apiDllPath != null ? Path.GetFileName(apiDllPath) : null;
    }

    private static void RecomputeSurfaceCounts(ApiSurface api)
    {
        api.PublicTypeCount = api.Types.Count;
        api.PublicMethodCount = api.Types.Sum(t => t.Members.Count(ApiMemberSectionDescriptors.IsMethodLike));
        api.PublicPropertyCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "property"));
        api.PublicFieldCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "field"));
        api.PublicEventCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "event"));
    }
}
