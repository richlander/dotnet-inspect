using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.CommandLine;
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
using DotnetInspector.Planning;

using Decompiler = ILInspector.Decompiler;

namespace DotnetInspector.Commands;

/// <summary>
/// Discovers types in a package or library (compact table, no docs by default).
/// </summary>
public static class TypeCommand
{
    public const string Name = "type";

    public static Task<int> ExecuteAsync(TypeOptions options)
        => ExecuteAsync(
            options,
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options));

    internal static Task<int> ExecuteAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan)
        => ExecuteCoreAsync(options, plan);

    internal static Task<int> ExecuteResolvedAsync(
        TypeOptions options,
        ApiSourceResult source,
        ApiServices.LoadedApiSurface loaded)
        => ExecuteCoreAsync(
            options,
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options),
            source,
            loaded);

    internal static TypeOptions FromDeferredMemberOptions(
        MemberOptions options)
    {
        var (memberFilter, memberLimit) =
            SharedParsers.ParseMemberFilter(
                options.RouterDeferredTypeMemberValues);
        return new()
        {
            TypeName = options.TypeName, PackagePath = options.PackagePath,
            PackageRangeAddress = options.PackageRangeAddress,
            AssemblyPath = options.AssemblyPath,
            PlatformAssembly = options.PlatformAssembly, PlatformFramework = options.PlatformFramework,
            ProjectPath = options.ProjectPath, ProjectAssetsPath = options.ProjectAssetsPath,
            SourceRepositories = options.SourceRepositories,
            Tfm = options.Tfm, IncludeAll = options.IncludeAll, Verbose = options.Verbose,
            ShowDocs = options.DocsExplicitlySet && options.ShowDocs,
            DocsExplicitlySet = options.DocsExplicitlySet,
            UseLocalDocs = options.UseLocalDocs, ShowSamples = options.ShowSamples,
            BrowsableUrls = options.BrowsableUrls, Verbosity = options.Verbosity,
            JsonOutput = options.JsonOutput, CompactJson = options.CompactJson,
            Tabular = options.Tabular, Tsv = options.Tsv, Jsonl = options.Jsonl,
            TabularExplicitlySet = options.TabularExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            FormatFlagExplicitlySet = options.FormatFlagExplicitlySet,
            MarkdownExplicitlySet = options.MarkdownExplicitlySet,
            Format = options.Format,
            PlainText = options.PlainText,
            MermaidOutput = options.MermaidOutput,
            EmbeddedMermaid = options.EmbeddedMermaid,
            Bare = options.Bare,
            NoHeader = options.NoHeader, Limit = memberLimit, MemberLimit = memberLimit,
            MemberFilter = memberFilter,
            KindFilter = options.KindFilter, UnsafeOnly = options.UnsafeOnly,
            IncludeSections = options.IncludeSections,
            ExactIncludeSectionsOverride = options.ExactIncludeSectionsOverride,
            Print = options.Print, PrintRow = options.PrintRow,
            Value = options.Value, Urls = options.Urls, Paths = options.Paths,
            Select = options.Select, SelectDefault = options.SelectDefault,
            Columns = options.Columns, Fields = options.Fields,
            Discover = options.Discover, Tree = options.Tree,
            ShapeOutput = options.ShapeOutput,
            ShapeExplicitlySet = options.ShapeExplicitlySet,
            Schema = options.Schema, Count = options.Count, Rows = options.Rows,
            JsonArray = options.JsonArray,
            PerformanceTriage = options.PerformanceTriage,
            BodyKindQuery = options.BodyKindQuery,
            SourceOptions = options.SourceOptions,
            TipLevel = options.TipLevel, RenderOptions = options.RenderOptions,
            RenderConfigWarnings = options.RenderConfigWarnings,
            RequestAllTaste = options.RequestAllTaste,
            RequestReadableLocalNames = options.RequestReadableLocalNames,
            DllPath = options.DllPath,
            PdbPath = options.PdbPath
        };
    }

    private static async Task<int> ExecuteCoreAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        ApiSourceResult? resolvedSource = null,
        ApiServices.LoadedApiSurface? loadedSurface = null)
    {
        if (plan.Intent.Surface != InspectionSurface.Type)
            throw new ArgumentException(
                "A type command requires a type inspection plan.",
                nameof(plan));

        if (!PerformanceTriageOptions.TryValidate(
                options.PerformanceTriage,
                out var performanceTriageError))
        {
            CommandError.Write(performanceTriageError);
            return 1;
        }

        // Shared preamble: section validation, discovery, verbosity promotion
        var (preamble, error) =
            ApiCommand.RunPreamble(options, plan);
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
            CommandError.Write(ex);
            return 1;
        }

        bool ownsSource = resolvedSource is null;
        ApiSourceResult source;
        if (resolvedSource is null)
        {
            var (acquiredSource, sourceError) =
                await ApiSourceResolver.ResolveAsync(options);
            if (sourceError.HasValue)
            {
                NamespacePrefixHints.WriteIfLikelyBareTypeName(options.OriginalTypeQuery ?? options.PackagePath ?? options.TypeName ?? "");
                return sourceError.Value;
            }

            source = acquiredSource;
        }
        else
        {
            source = resolvedSource;
        }

        var searchPath = source.SearchPath;
        var runtimeAssemblyPath = source.RuntimeAssemblyPath;
        var packageName = source.PackageName;
        var packageVersion = source.PackageVersion;
        var apiSource = source.ApiSource;
        var apiVersion = source.ApiVersion;
        var platformFramework = source.PlatformFramework;
        var selectedTfm = source.SelectedTfm;
        var projectAssetsPath = source.ProjectAssetsPath;
        var tempDir = source.TempDir;
        var typeName = source.TypeName;
        var originalTypeQuery = options.OriginalTypeQuery ?? options.TypeName;
        var context = source.Context;
        var logger = context.Logger;

        options = options with
        {
            PackagePath = source.ResolvedPackagePath,
            PackageRangeAddress = null,
            ProjectAssetsPath = projectAssetsPath,
        };
        bool inspectionIncomplete = false;
        try
        {
            if (string.IsNullOrEmpty(typeName)
                || new TypeGestureIntent(
                        options.TypeFilter)
                    .SelectsListingCatalog(
                        options.TypeName))
            {
                // No type specified - list all types
                var loaded = loadedSurface
                    ?? ApiServices.LoadTypeApi(
                        source,
                        options,
                        summaryOnly: CanUsePlatformSummary(
                            options,
                            searchPath,
                            runtimeAssemblyPath,
                            platformFramework));
                if (loaded == null)
                {
                    CommandError.Write("Could not extract API from library.");
                    return 1;
                }

                var api = loaded.Api;
                var pdbLookupPath = loaded.PdbLookupPath;

                // --columns Description implicitly enables doc enrichment (local XML only)
                var listOptions = options;
                if (options.Columns?.Any(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase)) == true)
                    listOptions = options with { ShowDocs = true };

                if (pdbLookupPath != null && listOptions.ShowDocs)
                    SourceEnricher.EnrichFromLocalXmlDocs(api.Types, pdbLookupPath, listOptions, logger);

                if (options.EffectiveDiscovery)
                {
                    return ExecuteListingDiscovery(api, typePipeline, options);
                }

                var listExitCode = ApiCommand.WriteFullApiOutput(api, options, selectedTfm);
                if (listExitCode != 0)
                    return listExitCode;
                inspectionIncomplete = api.InspectionFailures.Any(
                    static failure =>
                        failure.Operation
                            != ApiSurface.ConstraintResolutionOperation);

                if (!loaded.IsSummary && !options.FormatExplicitlySet && !options.IsRawOutput)
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
                        var simpleName = TypeMatcher.GetSimpleName(exampleType.FullName);

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
                var loaded = loadedSurface
                    ?? ApiServices.LoadTypeApi(source, options);
                if (loaded == null)
                {
                    CommandError.Write("Could not extract API from library.");
                    return 1;
                }

                var api = loaded.Api;
                var apiDllPath = loaded.ApiDllPath;

                var lookupResult = ApiTypeLookupService.LookupType(api, typeName);
                if (lookupResult.ImpliedMember is not null)
                {
                    lookupResult.WriteNotFoundError();
                    return 1;
                }

                if (lookupResult.Found)
                {
                    var apiType = lookupResult.Type!;

                    if (ApiCommand.ReresolveSectionsForSingleType(options) is not { } resolvedOptions)
                        return 1;
                    options = resolvedOptions;
                    if (ApiCommand.RejectDeferredDiscoveryForSingleType(
                            options,
                            memberPipeline))
                        return 1;

                    // Check each member filter before producing output
                    if (options.MemberFilter.Count > 0)
                    {
                        var memberValidation = ApiTypeLookupService.ValidateMemberFilters(apiType, options.MemberFilter);
                        if (!memberValidation.IsValid)
                        {
                            memberValidation.WriteError();
                            return 1;
                        }
                    }

                    var foundIn = apiDllPath != null ? Path.GetFileNameWithoutExtension(apiDllPath) : null;
                    ResolvedAssemblyReference? sourceAssembly =
                        loaded.TryGetSourceAssembly(apiType);

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
                        && AuthorizesPdbAcquisition(apiType, effectiveOptions))
                    {
                        var pdbPath = sourceAssembly is null
                            ? await ApiCommand.TryAcquirePdbPathAsync(
                                dllForPdb,
                                effectiveOptions,
                                logger,
                                context.HttpClient)
                            : await ApiCommand.TryAcquirePdbPathAsync(
                                dllForPdb,
                                sourceAssembly,
                                effectiveOptions,
                                logger,
                                context.HttpClient,
                                fallbackPackageName: packageName,
                                fallbackPackageVersion: packageVersion);
                        effectiveOptions = effectiveOptions with { PdbPath = pdbPath };
                    }

                    if (ShouldRejectQuietShape(effectiveOptions))
                    {
                        if (effectiveOptions.BodyKindQuery.HasFilter)
                        {
                            CommandError.Write("-v:q is not supported by Body Shapes queries.");
                            CommandError.WriteLine("Use -v:m, -v:n, or -v:d to render the selected body shapes.");
                        }
                        else
                        {
                            CommandError.Write("-v:q is not supported by the type shape renderer.");
                            CommandError.WriteLine("Use -v:m, -v:n, or -v:d for tree output, or add --markdown -v:q for compact section output.");
                        }
                        return 1;
                    }

                    // Default --shape on for single-type view when the user is not running a
                    // section/projection query and did not explicitly choose another renderer.
                    // Verbosity grows the tree view; --markdown opts into the section/document view.
                    if (!effectiveOptions.ShapeExplicitlySet && ShouldDefaultToShape(effectiveOptions))
                        effectiveOptions = effectiveOptions with { ShapeOutput = true };

                    // Explicit --shape cannot honor a section/projection query; warn rather than
                    // silently dropping the selection.
                    bool shapeProjectionWillReject =
                        LensProjection.IsRequested(effectiveOptions)
                        || effectiveOptions.JsonOutput
                            && (effectiveOptions.Fields is { Length: > 0 }
                                || effectiveOptions.Columns is { Length: > 0 });
                    if (effectiveOptions is { ShapeOutput: true, HasSectionQuery: true, Count: false }
                        && !shapeProjectionWillReject)
                    {
                        CommandError.WriteWarning("--shape does not support -S/--columns/--fields or --where Kind=...; selection was ignored.");
                    }

                    // Enrich with local XML docs only (source info is in the source command)
                    {
                        var dllPath = runtimeAssemblyPath ?? apiDllPath;
                        if (dllPath != null && effectiveOptions.ShowDocs)
                            SourceEnricher.EnrichFromLocalXmlDocs(apiType, dllPath, effectiveOptions, logger);
                    }

                    if (effectiveOptions.EffectiveDiscovery)
                    {
                        return ApiCommand.ExecuteEffectiveDiscovery(
                            apiType, memberPipeline, effectiveOptions,
                            new ApiCommand.TypeAcquisitionContext(
                                foundIn, packageName, packageVersion, apiSource, selectedTfm,
                                sourceAssembly));
                    }

                    if (effectiveOptions.DllPath is { } sourceFilesDllPath
                        && AuthorizesSourceInfoAcquisition(
                            apiType,
                            effectiveOptions))
                    {
                        await SourceEnricher.EnrichTypeWithSourceInfoAsync(
                            apiType,
                            apiType.FullName,
                            sourceFilesDllPath,
                            effectiveOptions,
                            logger,
                            context.HttpClient,
                            sourceAssembly,
                            fallbackPackageName: packageName,
                            fallbackPackageVersion: packageVersion);
                    }

                    bool hasProjection = effectiveOptions.Columns is { Length: > 0 } || effectiveOptions.Fields is { Length: > 0 };
                    bool validatesProjection = hasProjection
                        && (!effectiveOptions.JsonOutput || effectiveOptions.Count)
                        && effectiveOptions is not TypeOptions { ShapeOutput: true };
                    bool tabularProjection = validatesProjection && !effectiveOptions.Count;

                    // Pre-render: validate --columns/--fields names against the section schema
                    // (catches typos) when a specific section is selected, mirroring the package path.
                    if (validatesProjection && effectiveOptions.IncludeSections is { Count: > 0 })
                    {
                        var projSchema = ApiCommand.GetTypeDocumentSchema(effectiveOptions);
                        if (!ProjectionDiagnostics.ValidateProjection(projSchema, effectiveOptions.IncludeSections, effectiveOptions.Fields, effectiveOptions.Columns))
                            return 1;
                    }

                    int selectedSurfaceExitCode =
                        ApiCommand.WriteSelectedSurfaceDiagnostics(
                        api,
                        apiType,
                        effectiveOptions.MemberFilter);
                    if (tabularProjection)
                    {
                        // Capture output so we can warn when a requested column produced no data
                        // (e.g. a column not shown at this verbosity).
                        var sw = new StringWriter { NewLine = "\n" };
                        var writeExitCode = await ApiCommand.WriteTypeOutputAsync(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions, sw, sourceAssembly);
                        if (writeExitCode != 0)
                            return writeExitCode;
                        var rendered = sw.ToString();
                        ProjectionDiagnostics.DiagnoseRendered(effectiveOptions.Fields ?? effectiveOptions.Columns, rendered);
                        Console.Out.Write(rendered);
                    }
                    else
                    {
                        var writeExitCode = await ApiCommand.WriteTypeOutputAsync(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions, sourceAssembly: sourceAssembly);
                        if (writeExitCode != 0)
                            return writeExitCode;
                    }

                    // Notify when a requested section matched but has no data for this type.
                    // JSON and markdown both honor -S; tabular output falls back to showing all
                    // members and shape replaces selection, so skip those.
                    if (!effectiveOptions.Tabular
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

                        var simpleName = TypeMatcher.GetSimpleName(apiType.FullName);

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
                            tips.Add(new(MemberCommand.Name, $"{simpleName} {sourceFlag} -S \"Member Index\"", "full selector/identity table"));

                        tips.Add(new(Name, $"{simpleName} {sourceFlag} --shape", "view type shape"));
                        tips.Add(new(MemberCommand.Name, $"-m {simpleName}.{(exampleGroup?.Key ?? "Method")} {sourceFlag}", "dotted member syntax"));

                        if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                            tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..{packageVersion} -t {simpleName}", "compare API changes"));

                        Hints.WriteTips(effectiveOptions.TipLevel, [.. tips]);
                    }

                    if (selectedSurfaceExitCode != 0)
                        return selectedSurfaceExitCode;
                }
                else if (TryWritePrefixBrowse(
                    api,
                    apiDllPath,
                    originalTypeQuery,
                    typeName,
                    packageName,
                    apiSource,
                    apiVersion,
                    selectedTfm,
                    options,
                    typePipeline) is { } prefixBrowseExitCode)
                {
                    if (prefixBrowseExitCode != 0)
                        return prefixBrowseExitCode;
                }
                else
                {
                    var widePrefixExitCode = await TryExecuteWidePlatformPrefixFallbackAsync(options, originalTypeQuery, typePipeline);
                    if (widePrefixExitCode.HasValue)
                        return widePrefixExitCode.Value;

                    if (ApiCommand.ReresolveSectionsForSingleType(options) is not { } resolvedOptions)
                        return 1;
                    options = resolvedOptions;
                    if (ApiCommand.RejectDeferredDiscoveryForSingleType(
                            options,
                            memberPipeline))
                        return 1;

                    if (lookupResult.Suggestions.Count > 0)
                    {
                        bool isGlob = TypeMatcher.IsTypeGlobPattern(typeName);
                        if (isGlob)
                        {
                            // Glob matched multiple types — show types view with filter
                            AnnotateSurface(api, options, apiDllPath, packageName, apiSource, apiVersion, selectedTfm);

                            options = options with
                            {
                                TypeFilter = typeName,
                                Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
                            };

                            if (options.EffectiveDiscovery)
                                return ExecuteListingDiscovery(api, typePipeline, options);

                            var globExitCode = ApiCommand.WriteFullApiOutput(api, options, selectedTfm);
                            if (globExitCode != 0)
                                return globExitCode;
                        }
                        else
                        {
                            lookupResult.WriteNotFoundError();
                            return 1;
                        }
                    }
                    else
                    {
                        lookupResult.WriteNotFoundError();
                        return 1;
                    }
                }
            }

            return inspectionIncomplete ? 1 : 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            if (ownsSource
                && tempDir != null
                && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static bool CanUsePlatformSummary(
        TypeOptions options,
        string searchPath,
        string? runtimeAssemblyPath,
        string? platformFramework) =>
        runtimeAssemblyPath is not null
        && string.Equals(searchPath, runtimeAssemblyPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(platformFramework, "runtime", StringComparison.OrdinalIgnoreCase)
        && options.Verbosity == Verbosity.Quiet
        && !options.IsRawOutput
        && !options.PlainText
        && !options.Print
        && !options.Value
        && !options.Urls
        && !options.Paths
        && !options.JsonArray
        && !options.Tree
        && !options.MermaidOutput
        && !options.EmbeddedMermaid
        && !options.IncludeAll
        && !options.EffectiveDiscovery
        && !options.HasSectionQuery
        && !options.Count
        && !options.UnsafeOnly
        && !options.ShowDocs
        && options.TypeFilter is null
        && options.MemberFilter.Count == 0
        && options.KindFilter.Count == 0
        && options.Rows is null
        && !options.PerformanceTriage.HasFilters
        && !options.Limit.HasValue;

    private static Task<int?> TryExecuteWidePlatformPrefixFallbackAsync(
        TypeOptions options,
        string? originalTypeQuery,
        SectionPipeline<ApiSurface> typePipeline)
    {
        if (!options.AllowPlatformPrefixFallback
            || options.BodyKindQuery.HasFilter
            || string.IsNullOrWhiteSpace(originalTypeQuery))
            return Task.FromResult<int?>(null);
        if (TypeMatcher.IsTypeGlobPattern(originalTypeQuery))
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
           && !options.Tabular
           && !options.Tsv
           && !options.Jsonl
           && !options.NoHeader
           && !options.PlainText
           && !options.Bare
           && !options.Count
           && !options.MarkdownExplicitlySet;

    internal static bool AuthorizesPdbAcquisition(
        ApiType apiType,
        TypeOptions options)
        => options.IncludeSections is { Count: > 0 }
           && ApiCommand.GetRequestedMemberSections(apiType, options)
               .Overlaps(
               [
                   SectionNames.DecompiledSource,
                   SectionNames.BodyShapes,
               ]);

    internal static bool AuthorizesSourceInfoAcquisition(
        ApiType apiType,
        TypeOptions options)
        => ApiCommand.GetRequestedMemberSections(apiType, options)
            .Contains(SectionNames.SourceFiles);

    private static bool ShouldRejectQuietShape(TypeOptions options)
    {
        if (options.Verbosity != Verbosity.Quiet)
            return false;
        if (options.BodyKindQuery.HasFilter)
            return true;

        return !options.MarkdownExplicitlySet
            && !options.JsonOutput
            && !options.Tabular
            && !options.Tsv
            && !options.Jsonl
            && !options.NoHeader
            && !options.PlainText
            && !options.Count
            && !options.HasSectionQuery;
    }

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
            CommandError.WriteNote($"Type '{query}' resolved via platform find to {match.FullName} in {match.Library}.");
            return await ExecuteAsync(resolution.ApplyTo(options));
        }

        return resolution.WriteAmbiguousError();
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
        if (api is null
            || !HasPlatformPrefixBrowseResult(api))
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

        // This renders a listing for what entered as a single-type request, so a select the
        // preamble deferred resolves here, against the pipeline doing the rendering. Without this
        // the deferred select would be dropped and the listing would ignore -S entirely.
        if (browseOptions.SelectDeferredToListing
            || browseOptions.DiscoverDeferredToListing
            || browseOptions.Select is { Length: > 0 }
            || browseOptions.SelectDefault)
        {
            if (ApiCommand.ReresolveSectionsForListing(browseOptions) is not { } resolvedBrowseOptions)
                return 1;
            browseOptions = resolvedBrowseOptions;
        }

        CommandError.WriteNote($"Showing best-effort platform prefix matches for '{query}'.");
        CommandError.WriteNote($"Use `find \"{ToFindPrefixPattern(query)}\" --platform` to see source libraries.");

        if (browseOptions.EffectiveDiscovery)
        {
            return ExecuteListingDiscovery(api, typePipeline, browseOptions);
        }

        return ApiCommand.WriteFullApiOutput(api, browseOptions);
    }

    internal static bool HasPlatformPrefixBrowseResult(
        ApiSurface? api) =>
        api is not null
        && (api.Types.Count > 0
            || api.InspectionFailures.Count > 0);

    private static string ToFindPrefixPattern(string query)
        => query.EndsWith('*') ? query : $"{query}*";

    internal static async Task<bool> PackageExistsAsync(
        string packageName,
        TypeOptions options,
        CommandContext context)
    {
        if (PackageExtractor.HasCachedCandidateVersion(
                packageName,
                SourceResolver.ResolveSourceKeysForProbe(
                    options.SourceOptions,
                    packageName)))
        {
            return true;
        }

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

        var searchResults = await TypeSearchService.CollectTypesAsync(findOptions, pattern, logger, context.HttpClient);

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
            var (assemblyPath, resolvedFramework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                assembly,
                context.HttpClient,
                logger.Log,
                framework,
                sourceOptions: options.SourceOptions);
            if (assemblyPath == null || error != null)
            {
                logger.LogWarning($"Could not resolve platform library '{assembly}' in {framework}: {error}");
                continue;
            }

            var loaded = ApiServices.LoadFullApi(
                assemblyPath,
                runtimeAssemblyPath: null,
                packagePath: null,
                packageName: null,
                SourceKind.Platform,
                version,
                selectedTfm: null,
                logger,
                options,
                useTypedSelection: true,
                platformFramework: resolvedFramework);
            if (loaded is null)
            {
                throw new InvalidOperationException(
                    $"Could not extract API from platform library '{assemblyPath}'.");
            }

            var api = loaded.Api;

            List<ApiType> selectedTypes =
            [
                .. api.Types.Where(type =>
                    fullNames.Contains(type.FullName)),
            ];
            foreach (ApiType type in selectedTypes)
            {
                type.SourceAssemblyPath ??= assemblyPath;
                merged.Types.Add(type);
            }
            MergeSelectedInspectionFailures(
                merged,
                api,
                selectedTypes,
                fullNames,
                assemblyPath);
        }

        merged.Types = merged.Types
            .DistinctBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        RecomputeSurfaceCounts(merged);
        return merged;
    }

    internal static void MergeSelectedInspectionFailures(
        ApiSurface destination,
        ApiSurface source,
        IReadOnlyList<ApiType> selectedTypes,
        IReadOnlySet<string> selectedTypeNames,
        string defaultSourcePath)
    {
        var selectedSubjects =
            new HashSet<ApiSurfaceInspectionSubject>();
        var selectedSourcePaths =
            new HashSet<string>(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        foreach (ApiType type in selectedTypes)
        {
            string sourcePath =
                type.SourceAssemblyPath ?? defaultSourcePath;
            selectedSourcePaths.Add(sourcePath);
            Add(type.MetadataToken);
            foreach (ApiMember member in type.Members)
            {
                Add(member.MetadataToken);
                Add(member.GetterToken);
                Add(member.SetterToken);
                Add(member.AdderToken);
                Add(member.RemoverToken);
            }

            void Add(int? token)
            {
                if (token is int value)
                {
                    selectedSubjects.Add(
                        new ApiSurfaceInspectionSubject(
                            sourcePath,
                            value));
                }
            }
        }
        destination.MergeInspectionFailuresFrom(
            source,
            selectedSubjects.Contains,
            includeNonConstraintFailures: false);
        foreach (ApiSurfaceInspectionFailure failure
            in source.InspectionFailures)
        {
            if (failure.Operation
                    == ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation
                || !IncludesFailure(failure))
            {
                continue;
            }

            destination.InspectionFailures.Add(failure);
        }

        bool IncludesFailure(
            ApiSurfaceInspectionFailure failure)
        {
            if (failure.OwningTypeDefinition is { } owner)
            {
                return selectedTypeNames.Contains(
                    owner.ToMetadataFullName());
            }
            if (!failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
            {
                return failure.AffectedTypeDefinitions.Any(
                    affected =>
                        selectedTypeNames.Contains(
                            affected.ToMetadataFullName()));
            }

            string? sourcePath =
                failure.SourceAssemblyPath;
            if (failure.SubjectToken == 0)
            {
                return selectedTypes.Count == 0
                    || (sourcePath is not null
                        && selectedSourcePaths.Contains(
                            sourcePath));
            }

            return selectedSubjects.Contains(
                new ApiSurfaceInspectionSubject(
                    sourcePath,
                    failure.OwningTypeToken
                        ?? failure.SubjectToken));
        }
    }

    private static int? TryWritePrefixBrowse(
        ApiSurface api,
        string? apiDllPath,
        string? originalTypeQuery,
        string resolvedTypeName,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        TypeOptions options,
        SectionPipeline<ApiSurface> typePipeline)
    {
        if (options.BodyKindQuery.HasFilter || string.IsNullOrWhiteSpace(originalTypeQuery))
            return null;
        if (TypeMatcher.IsTypeGlobPattern(originalTypeQuery))
            return null;

        var matches = FindPrefixMatches(api.Types, originalTypeQuery);
        if (matches.Count == 0)
            return null;

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

        CommandError.WriteNote($"Type '{resolvedTypeName}' not found. Showing best-effort prefix matches for '{originalTypeQuery}'.");

        // The preamble resolved -S against the single-type pipeline because the argument shape
        // looked like one type. It is not: this renders a listing, so the section names have to be
        // re-resolved against the pipeline that will actually render them. Ordered after the note
        // so a rejected section name still says which view rejected it.
        var resolved = ApiCommand.ReresolveSectionsForListing(browseOptions);
        if (resolved == null)
            return 1;

        if (resolved.EffectiveDiscovery)
        {
            return ExecuteListingDiscovery(api, typePipeline, resolved);
        }

        return ApiCommand.WriteFullApiOutput(api, resolved, selectedTfm);
    }

    private static int ExecuteListingDiscovery(
        ApiSurface api,
        SectionPipeline<ApiSurface> typePipeline,
        TypeOptions options)
    {
        ApiCommand.ApplySurfaceFilters(api, options, options.TypeFilter);
        var schema = ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();
        var effective = typePipeline.GetDiscoverableSections(api, options.IncludeSections);
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
            verbosity: (int)options.Verbosity,
            sectionCostAnnotations: typePipeline.GetCostAnnotations(),
            sectionCategories: typePipeline.GetCategoryMap(),
            projection: options);
    }

    private static List<ApiType> FindPrefixMatches(IEnumerable<ApiType> types, string query)
    {
        var normalized = FqnParser.NormalizeTypeName(query.Trim());
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
