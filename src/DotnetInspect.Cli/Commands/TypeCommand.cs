using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using Markout;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using DotnetInspect.Cli.Planning;
using NuGet.Versioning;
using QuerySpace;
using QuerySpace.Operations;
using Decompiler = ILInspector.Decompiler;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Discovers types in a package or library (compact table, no docs by default).
/// </summary>
public static class TypeCommand
{
    public const string Name = "type";

    private static readonly InspectionEnvelopeJsonContract<
        ExactTypeInspectionResult> ExactTypeJsonContract =
            new(
                "exact-type",
                1,
                ExactTypeInspectionJsonContext.Default
                    .ExactTypeInspectionResult);

    private static readonly InspectionEnvelopeJsonContract<
        ExactLibraryApiInspectionResult> ExactLibraryApiJsonContract =
            new(
                "exact-library-api",
                1,
                ExactLibraryApiInspectionJsonContext.Default
                    .ExactLibraryApiInspectionResult);

    public static Task<int> ExecuteAsync(TypeOptions options)
        => ExecuteAsync(
            options,
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options));

    internal static Task<int> ExecuteAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan)
        => ExecuteCoreAsync(options, plan);

    internal static Task<int> ExecuteAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(
            options,
            plan,
            cancellationToken: cancellationToken);

    internal static Task<int> ExecuteAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        WorkspaceContextLoadOptions exactTypeCapabilities)
        => ExecuteCoreAsync(
            options,
            plan,
            exactTypeCapabilities: exactTypeCapabilities);

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
        var memberFilter =
            SharedParsers.ParseMemberFilter(
                options.RouterDeferredTypeMemberValues);
        return new()
        {
            TypeName = options.TypeName,
            PackagePath = options.PackagePath,
            PackageRangeAddress = options.PackageRangeAddress,
            AssemblyPath = options.AssemblyPath,
            PlatformAssembly = options.PlatformAssembly,
            PlatformFramework = options.PlatformFramework,
            ProjectPath = options.ProjectPath,
            ProjectAssetsPath = options.ProjectAssetsPath,
            SourceRepositories = options.SourceRepositories,
            Tfm = options.Tfm,
            IncludeAll = options.IncludeAll,
            Verbose = options.Verbose,
            ShowDocs = options.DocsExplicitlySet && options.ShowDocs,
            DocsExplicitlySet = options.DocsExplicitlySet,
            ShowSamples = options.ShowSamples,
            PreferRenderedUrls = options.PreferRenderedUrls,
            Verbosity = options.Verbosity,
            VerbosityExplicitlySet = options.VerbosityExplicitlySet,
            UserVerbosityOverride = options.UserVerbosityOverride,
            JsonOutput = options.JsonOutput,
            CompactJson = options.CompactJson,
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            TabularExplicitlySet = options.TabularExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            FormatFlagExplicitlySet = options.FormatFlagExplicitlySet,
            MarkdownExplicitlySet = options.MarkdownExplicitlySet,
            Format = options.Format,
            PlainText = options.PlainText,
            MermaidOutput = options.MermaidOutput,
            EmbeddedMermaid = options.EmbeddedMermaid,
            NoHeader = options.NoHeader,
            MemberFilter = memberFilter,
            KindFilter = options.KindFilter,
            UnsafeOnly = options.UnsafeOnly,
            IncludeSections = options.IncludeSections,
            ExactIncludeSectionsOverride = options.ExactIncludeSectionsOverride,
            Print = options.Print,
            PrintRow = options.PrintRow,
            Value = options.Value,
            Urls = options.Urls,
            Paths = options.Paths,
            Select = options.Select,
            SelectDefault = options.SelectDefault,
            Columns = options.Columns,
            Fields = options.Fields,
            FieldsExplicitlySet = options.FieldsExplicitlySet,
            Discover = options.Discover,
            Tree = options.Tree,
            ShapeOutput = options.ShapeOutput,
            Schema = options.Schema,
            Count = options.Count,
            Rows = options.Rows,
            JsonArray = options.JsonArray,
            PerformanceTriage = options.PerformanceTriage,
            BodyKindQuery = options.BodyKindQuery,
            CloneCandidateQuery = options.CloneCandidateQuery,
            SourceOptions = options.SourceOptions,
            TipLevel = options.TipLevel,
            RenderOptions = options.RenderOptions,
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
        ApiServices.LoadedApiSurface? loadedSurface = null,
        ApiType? preselectedType = null,
        WorkspaceContextLoadOptions? exactTypeCapabilities = null,
        CancellationToken cancellationToken = default)
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

        if (SelectsTypeRelations(options, plan))
        {
            return await ExecuteTypeRelationsAsync(
                options,
                plan,
                exactTypeCapabilities
                    ?? CreateWorkspaceContextLoadOptions(options),
                cancellationToken).ConfigureAwait(false);
        }

        if (resolvedSource is null
            && loadedSurface is null
            && options.WorkspacePacket is not null
            && !options.EnvelopeOutput
            && options.ShareFormat is not null
            && options.Discover is not null
            && !options.EffectiveDiscovery)
        {
            return await ExecuteWorkspaceExactTypeAsync(
                options,
                plan,
                exactTypeCapabilities
                    ?? CreateWorkspaceContextLoadOptions(options),
                cancellationToken).ConfigureAwait(false);
        }

        // Shared preamble: section validation, discovery, verbosity promotion
        var (preamble, error) =
            ApiCommand.RunPreamble(options, plan);
        if (error.HasValue) return error.Value;

        options = (TypeOptions)preamble.Options;
        var typePipeline = preamble.TypePipeline;
        var memberPipeline = preamble.MemberPipeline;

        if (resolvedSource is null
            && loadedSurface is null
            && options.WorkspacePacket is not null
            && !options.EnvelopeOutput)
        {
            return await ExecuteWorkspaceExactTypeAsync(
                options,
                plan,
                exactTypeCapabilities
                    ?? CreateWorkspaceContextLoadOptions(options),
                cancellationToken).ConfigureAwait(false);
        }

        if (!options.EnvelopeOutput)
        {
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
        }

        if (resolvedSource is null
            && loadedSurface is null
            && TryCreateSharedExactTypeRequest(
                options,
                out ExactTypeInspectionRequest? exactTypeRequest))
        {
            return await (exactTypeCapabilities is null
                ? ExecuteSharedExactTypeAsync(
                    options,
                    plan,
                    exactTypeRequest)
                : ExecuteSharedExactTypeAsync(
                    options,
                    plan,
                    exactTypeRequest,
                    exactTypeCapabilities)).ConfigureAwait(false);
        }

        if (resolvedSource is null
            && loadedSurface is null
            && TryCreateSharedExactLibraryApiRequest(
                options,
                out ExactLibraryApiInspectionRequest? exactLibraryRequest))
        {
            return await ExecuteSharedExactLibraryApiAsync(
                    options,
                    exactLibraryRequest,
                    exactTypeCapabilities)
                .ConfigureAwait(false);
        }

        if (options.EnvelopeOutput)
        {
            CommandError.Write(
                "--envelope on type requires exact package-backed Type or Library API inspection, or --match.");
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
                if (loadedSurface is null)
                {
                    int? libraryListingResult =
                        await LibraryTypeListingCommand.TryExecuteAsync(
                            source,
                            options,
                            cancellationToken);
                    if (libraryListingResult is not null)
                        return libraryListingResult.Value;

                    if (TryExecuteMetadataTypeCount(
                            source,
                            options)
                        is int countExitCode)
                    {
                        return countExitCode;
                    }
                }

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

                if (pdbLookupPath != null
                    && (listOptions.ShowDocs || listOptions.ShowSamples))
                    await DocumentationEnricher.EnrichAsync(
                        api.Types,
                        source,
                        loaded,
                        listOptions,
                        context.HttpClient,
                        includeMembers: false);

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
                            new(Name, $"{sourceFlag} --tree", "view type tree"),
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

                ApiTypeLookupResult lookupResult =
                    preselectedType is null
                        ? ApiTypeLookupService.LookupType(api, typeName)
                        : new(
                            typeName,
                            new LookupResult(
                                preselectedType.DefinitionName
                                    ?.ToEscapedFullName()
                                    ?? preselectedType.FullName,
                                []),
                            preselectedType);
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

                    ResolvedAssemblyReference? sourceAssembly =
                        loaded.TryGetSourceAssembly(apiType);
                    var acquisition = new ApiCommand.TypeAcquisitionContext(
                        loaded.GetLibraryAssetPath(source.PackageExtractPath),
                        packageName, packageVersion ?? apiVersion, apiSource, selectedTfm,
                        sourceAssembly);

                    // Default --docs on for single-type view at Normal+ unless explicitly disabled
                    TypeOptions effectiveOptions = options;
                    if (!options.DocsExplicitlySet && options.Verbosity >= Verbosity.Normal)
                        effectiveOptions = options with { ShowDocs = true };

                    // The resolved assembly path enables decompiler-backed
                    // sections (whole-type Decompiled Source).
                    effectiveOptions = effectiveOptions with { DllPath = apiType.SourceAssemblyPath ?? runtimeAssemblyPath ?? apiDllPath };

                    if (!CloneCandidatesCommand.ValidatePredicateSelection(
                            effectiveOptions.CloneCandidateQuery,
                            effectiveOptions.IncludeSections))
                    {
                        return 1;
                    }

                    if (CloneCandidatesCommand.IsSelected(
                            effectiveOptions.IncludeSections))
                    {
                        if (apiType.DefinitionName is not { } definitionName)
                        {
                            CommandError.Write(
                                $"Type '{apiType.FullName}' has no exact metadata definition identity for Clone Candidates.");
                            return 1;
                        }
                        if (effectiveOptions.DllPath is not { } clonePath)
                        {
                            CommandError.Write(
                                $"Type '{apiType.FullName}' has no resolved assembly path for Clone Candidates.");
                            return 1;
                        }
                        ResolvedAssemblyReference cloneAssembly =
                            sourceAssembly
                            ?? ResolvedAssemblyReference.CreateFromPath(
                                clonePath,
                                AssemblyResolutionProvenance.Local(
                                    "type Clone Candidates"));
                        return await CloneCandidatesCommand.ExecuteAsync(
                            cloneAssembly,
                            clonePath,
                            new StructuralCloneSearchSeed.Type(
                                definitionName),
                            effectiveOptions.CloneCandidateQuery,
                            CloneCandidateOutputOptions.From(
                                effectiveOptions),
                            new CloneCandidateWorkspaceOptions(
                                source.PackageExtractPath,
                                effectiveOptions.ProjectAssetsPath,
                                effectiveOptions.Tfm,
                                effectiveOptions.SourceOptions));
                    }

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

                    // Default to the tree renderer for a single Type when the user is not running
                    // a section/projection query and did not explicitly choose another renderer.
                    // Verbosity grows the tree view; --markdown opts into the section/document view.
                    if (ShouldDefaultToShape(effectiveOptions))
                        effectiveOptions = effectiveOptions with { ShapeOutput = true };

                    // Enrich with local XML docs only (source info is in the source command)
                    {
                        var dllPath = runtimeAssemblyPath ?? apiDllPath;
                        if (dllPath != null
                            && (effectiveOptions.ShowDocs
                                || effectiveOptions.ShowSamples))
                            await DocumentationEnricher.EnrichAsync(
                                [apiType],
                                source,
                                loaded,
                                effectiveOptions,
                                context.HttpClient);
                    }

                    if (effectiveOptions.EffectiveDiscovery)
                    {
                        return ApiCommand.ExecuteEffectiveDiscovery(
                            apiType, memberPipeline, effectiveOptions,
                            acquisition);
                    }

                    if (effectiveOptions.DllPath is { } sourceFilesDllPath
                        && AuthorizesSourceInfoAcquisition(
                            apiType,
                            effectiveOptions))
                    {
                        await SourceEnricher.EnrichTypeWithSourceInfoAsync(
                            apiType,
                            sourceFilesDllPath,
                            effectiveOptions with { ShowDocs = false },
                            logger,
                            context.HttpClient,
                            sourceAssembly,
                            fallbackPackageName: packageName,
                            fallbackPackageVersion: packageVersion);
                    }

                    if (AuthorizesTypeApiDeclarations(
                            apiType,
                            effectiveOptions))
                    {
                        effectiveOptions =
                            await AttachTypeApiDeclarationInspectionAsync(
                                apiType,
                                effectiveOptions,
                                loaded,
                                cancellationToken);
                    }

                    if (AuthorizesTypeSource(
                            apiType,
                            effectiveOptions))
                    {
                        effectiveOptions =
                            await AttachTypeSourceInspectionAsync(
                                apiType,
                                effectiveOptions,
                                loaded,
                                packageName,
                                packageVersion,
                                context.HttpClient,
                                cancellationToken);
                    }

                    if (effectiveOptions.DllPath is { } decompilationPath
                        && AuthorizesWholeTypeDecompilation(
                            apiType,
                            effectiveOptions))
                    {
                        effectiveOptions =
                            await AttachTypeDocumentInspectionAsync(
                                apiType,
                                effectiveOptions,
                                decompilationPath,
                                sourceAssembly,
                                context.HttpClient,
                                cancellationToken);
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
                        // Hold the rendered artifact until typed projection diagnostics confirm
                        // that the command can publish it with a successful exit.
                        var sw = new StringWriter { NewLine = "\n" };
                        var writeExitCode = await ApiCommand.WriteTypeOutputAsync(
                            apiType, acquisition.FoundIn, acquisition.PackageName, acquisition.PackageVersion,
                            acquisition.ApiSource, acquisition.SelectedTfm, effectiveOptions, sw, sourceAssembly,
                            sourceClient: context.HttpClient);
                        if (writeExitCode != 0)
                            return writeExitCode;
                        var rendered = sw.ToString();
                        Console.Out.Write(rendered);
                    }
                    else
                    {
                        var writeExitCode = await ApiCommand.WriteTypeOutputAsync(
                            apiType, acquisition.FoundIn, acquisition.PackageName, acquisition.PackageVersion,
                            acquisition.ApiSource, acquisition.SelectedTfm, effectiveOptions, sourceAssembly: sourceAssembly,
                            sourceClient: context.HttpClient);
                        if (writeExitCode != 0)
                            return writeExitCode;
                    }

                    // Notify when a requested section matched but has no data for this type.
                    // JSON and markdown both honor -S; tabular output falls back to showing all
                    // members and shape replaces selection, so skip those.
                    if (!effectiveOptions.Tabular
                        && effectiveOptions is not TypeOptions { ShapeOutput: true }
                        && !effectiveOptions.CountDefaultPopulation)
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

                        tips.Add(new(Name, $"{simpleName} {sourceFlag} --tree", "view type tree"));
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

    internal static bool TryCreateSharedExactTypeRequest(
        TypeOptions options,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out ExactTypeInspectionRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(options.PackagePath)
            || File.Exists(options.PackagePath)
            || options.PackagePath.Contains("::", StringComparison.Ordinal)
            || options.PackageRangeAddress is not null
            || options.AssemblyPath is not null
            || options.PlatformAssembly is not null
            || options.PlatformFramework is not null
            || options.ProjectPath is not null
            || options.ProjectAssetsPath is not null
            || options.WorkspacePacket is not null
            || string.IsNullOrWhiteSpace(options.Tfm)
            || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(options.TypeName)
            || !string.IsNullOrWhiteSpace(options.TypeFilter)
            || new TypeGestureIntent(options.TypeFilter)
                .SelectsListingCatalog(options.TypeName)
            || options.EffectiveDiscovery
            || options.Verbosity is not (
                Verbosity.Quiet or Verbosity.Minimal)
            || !IsCompleteInspectionOutput(options)
            || options.HasSectionQuery
            || options.IncludeSections is { Count: > 0 }
            || options.IncludeAll
            || options.ShowDocs
            || options.DocsExplicitlySet
            || options.ShowSamples
            || options.PreferRenderedUrls
            || options.MemberFilter.Count > 0
            || options.KindFilter.Count > 0
            || options.UnsafeOnly
            || options.Limit.HasValue
            || options.MemberLimit.HasValue
            || options.Rows is not null
            || options.PerformanceTriage.HasFilters
            || options.BodyKindQuery.HasFilter
            || options.RequestReadableLocalNames
            || options.SourceRepositories.Length > 0
            || options.DllPath is not null
            || options.PdbPath is not null
            || CloneCandidatesCommand.IsSelected(options.IncludeSections))
        {
            return false;
        }

        (string packageId, string? version) =
            PackageExtractor.ParsePackageReference(options.PackagePath!);
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        try
        {
            request = new ExactTypeInspectionRequest(
                packageId,
                version,
                options.Tfm!,
                options.TypeName!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static bool TryCreateSharedExactLibraryApiRequest(
        TypeOptions options,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out ExactLibraryApiInspectionRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(options.PackagePath)
            || File.Exists(options.PackagePath)
            || options.PackagePath.Contains("::", StringComparison.Ordinal)
            || options.PackageRangeAddress is not null
            || string.IsNullOrWhiteSpace(options.AssemblyPath)
            || options.PlatformAssembly is not null
            || options.PlatformFramework is not null
            || options.ProjectPath is not null
            || options.ProjectAssetsPath is not null
            || options.WorkspacePacket is not null
            || string.IsNullOrWhiteSpace(options.Tfm)
            || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(options.TypeFilter)
            || !(string.IsNullOrWhiteSpace(options.TypeName)
                || new TypeGestureIntent(options.TypeFilter)
                    .SelectsListingCatalog(options.TypeName))
            || options.EffectiveDiscovery
            || options.Verbosity is not (
                Verbosity.Quiet or Verbosity.Minimal)
            || !IsCompleteInspectionOutput(options)
            || options.HasSectionQuery
            || options.IncludeSections is { Count: > 0 }
            || options.IncludeAll
            || options.ShowDocs
            || options.DocsExplicitlySet
            || options.ShowSamples
            || options.PreferRenderedUrls
            || options.MemberFilter.Count > 0
            || options.KindFilter.Count > 0
            || options.UnsafeOnly
            || options.Limit.HasValue
            || options.MemberLimit.HasValue
            || options.Rows is not null
            || options.PerformanceTriage.HasFilters
            || options.BodyKindQuery.HasFilter
            || options.RequestReadableLocalNames
            || options.SourceRepositories.Length > 0
            || options.DllPath is not null
            || options.PdbPath is not null
            || CloneCandidatesCommand.IsSelected(options.IncludeSections))
        {
            return false;
        }

        (string packageId, string? version) =
            PackageExtractor.ParsePackageReference(options.PackagePath!);
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(version)
            || !NuGetVersion.TryParse(
                version,
                out NuGetVersion? parsedVersion))
        {
            return false;
        }

        try
        {
            request = new ExactLibraryApiInspectionRequest(
                packageId,
                parsedVersion.ToNormalizedString(),
                options.Tfm!,
                options.AssemblyPath!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static bool IsCompleteInspectionOutput(TypeOptions options)
    {
        bool completeFormat =
            options.IsDefaultInvocation
            || options.JsonOutput
            || options.EnvelopeOutput;

        return completeFormat
            && !options.Tree
            && !options.ShapeOutput
            && !options.Print
            && options.PrintRow is null
            && !options.Value
            && !options.Urls
            && !options.Paths
            && !options.JsonArray
            && !options.NoHeader
            && options.TypeListingRowSelection
                is not { Operations.Count: > 0 }
            && !options.MarkdownExplicitlySet
            && !options.PlainText
            && !options.Tabular
            && !options.Tsv
            && !options.Jsonl
            && !options.MermaidOutput
            && !options.EmbeddedMermaid
            && !options.Count
            && options.Discover is null
            && !options.Schema
            && options.Columns is null
            && options.Fields is null
            && !options.RequestAllTaste;
    }

    static Task<int> ExecuteSharedExactLibraryApiAsync(
        TypeOptions options,
        ExactLibraryApiInspectionRequest request,
        WorkspaceContextLoadOptions? capabilities) =>
        ExecuteSharedExactLibraryApiCoreAsync(
            options,
            request,
            capabilities
            ?? new WorkspaceContextLoadOptions
            {
                HttpClient = HttpClientFactory.Shared,
                SourceAuthorization =
                    new SourcePolicyPackageSourceAuthorization(
                        options.SourceOptions),
                PackageStore = new FileSystemPackageStore(),
                UseVersionCache = true,
                IncludePrerelease = true,
                Log = options.Verbose
                    ? CommandError.WriteLine
                    : null,
            });

    static async Task<int> ExecuteSharedExactLibraryApiCoreAsync(
        TypeOptions options,
        ExactLibraryApiInspectionRequest request,
        WorkspaceContextLoadOptions capabilities)
    {
        ExactLibraryApiInspectionExecution execution =
            await ExactLibraryApiInspectionOperation.ExecuteAsync(
                    request,
                    capabilities)
                .ConfigureAwait(false);
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;
        ExactLibraryApiInspectionResult result = envelope.Content;
        if (options.EnvelopeOutput || options.JsonOutput)
        {
            bool wrote = InspectionEnvelopeOutput.TryWrite(
                envelope,
                ExactLibraryApiJsonContract,
                options.EnvelopeOutput,
                options.CompactJson);
            WriteInspectionDiagnostics(envelope.Diagnostics);
            if (result.Outcome
                    is not ExactLibraryApiInspectionOutcome.Available
                && !envelope.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity
                        == InspectionDiagnosticSeverity.Error))
            {
                CommandError.Write(
                    result.Failures.FirstOrDefault()?.Detail
                    ?? "Could not extract API from library.");
            }
            else if (wrote && execution.Surface is not null)
            {
                WriteExactLibraryTips(
                    options,
                    request,
                    execution.Surface);
            }
            return wrote && result.IsComplete ? 0 : 1;
        }

        if (result.Outcome
                is not ExactLibraryApiInspectionOutcome.Available
            || execution.Surface is null)
        {
            CommandError.Write(
                result.Failures.FirstOrDefault()?.Detail
                ?? "Could not extract API from library.");
            return 1;
        }

        int writeExitCode =
            ApiCommand.WriteFullApiOutput(
                execution.Surface,
                options,
                result.Asset?.TargetFramework);
        if (writeExitCode != 0)
            return writeExitCode;

        if (!options.FormatExplicitlySet
            && !options.IsRawOutput)
        {
            WriteExactLibraryTips(
                options,
                request,
                execution.Surface);
        }

        return result.IsComplete ? 0 : 1;
    }

    static void WriteExactLibraryTips(
        TypeOptions options,
        ExactLibraryApiInspectionRequest request,
        ApiSurface surface)
    {
        ApiType? exampleType = surface.Types
            .OrderByDescending(type => type.Members.Count)
            .FirstOrDefault();
        if (exampleType is null)
            return;

        string sourceFlag =
            $"--package {request.PackageId} "
            + $"--library {request.Library}";
        string simpleName =
            TypeMatcher.GetSimpleName(exampleType.FullName);
        Hints.WriteTips(
            options.TipLevel,
            [
                new(
                    MemberCommand.Name,
                    $"{simpleName} {sourceFlag}",
                    "inspect type members"),
                new(
                    Name,
                    $"{sourceFlag} --tree",
                    "view type tree"),
                new(
                    Name,
                    $"-t \"*Writer*\" {sourceFlag}",
                    "filter types by pattern"),
            ]);
    }

    static Task<int> ExecuteSharedExactTypeAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionRequest request) =>
        ExecuteSharedExactTypeAsync(
            options,
            plan,
            request,
            CreateWorkspaceContextLoadOptions(options));

    private static bool SelectsTypeRelations(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan) =>
        (options.ExactIncludeSections?.Any(IsTypeRelationSection) is true
            || options.Select?.Any(selector =>
                IsTypeRelationSection(selector)
                || selector.Equals(
                    SectionCategoryNames.Relations,
                    StringComparison.OrdinalIgnoreCase)) is true)
        && plan.Selection.ResolvedSections.Any(IsTypeRelationSection);

    private static bool IsTypeRelationSection(string section) =>
        section.Equals(
            SectionNames.Implementers,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            SectionNames.DerivedTypes,
            StringComparison.OrdinalIgnoreCase);

    private static async Task<int> ExecuteTypeRelationsAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan inspectionPlan,
        WorkspaceContextLoadOptions capabilities,
        CancellationToken cancellationToken)
    {
        string[] unsupported =
        [
            .. inspectionPlan.Selection.ResolvedSections
                .Where(section => !IsTypeRelationSection(section)),
        ];
        if (unsupported.Length > 0)
        {
            CommandError.Write(
                "Subject Relations sections cannot yet be combined with "
                    + $"'{string.Join("', '", unsupported)}'.");
            return 1;
        }
        CliTypeRelationsRequest? request;
        try
        {
            request = await CreateSubjectRelationsTypeRequestAsync(
                    options,
                    capabilities,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        if (request is null)
        {
            CommandError.Write(
                "Subject Relations require one exact package version and "
                    + "target framework, one platform assembly, one local "
                    + "library, or one restored project.");
            return 1;
        }

        bool implementers =
            inspectionPlan.Selection.ResolvedSections.Contains(
                SectionNames.Implementers,
                StringComparer.OrdinalIgnoreCase);
        bool derivedTypes =
            inspectionPlan.Selection.ResolvedSections.Contains(
                SectionNames.DerivedTypes,
                StringComparer.OrdinalIgnoreCase);
        List<PortableQueryTerm> terms =
        [
            new(
                SubjectRelationsQuery.DirectionTermKey,
                PortableQueryOperator.Equal,
                "incoming"),
        ];
        if (implementers != derivedTypes)
        {
            terms.Add(
                new(
                    SubjectRelationsQuery.FormTermKey,
                    PortableQueryOperator.Equal,
                    implementers ? "interface" : "base-type"));
        }
        SubjectRelationsQueryPlanResult resolved =
            SubjectRelationsQuery.ResolveIntent(
                SubjectRelationsRouteKind.Type,
                PortableQueryIntent.Create(terms, [], [], []),
                cancellationToken);
        if (resolved is not SubjectRelationsQueryPlanResult.Accepted
            accepted)
        {
            CommandError.Write(
                "The Subject Relations query could not be resolved.");
            return 1;
        }

        ExactTypeRelationsInspectionOutcome outcome;
        try
        {
            outcome = await ExactTypeRelationsInspectionOperation
                .ExecuteAsync(
                    request.Inspection,
                    request.EmbeddedContent is null
                        ? capabilities
                        : capabilities with
                        {
                            EmbeddedContent = request.EmbeddedContent,
                        },
                    accepted.Plan,
                    count: options.Count
                        ? new SubjectRelationPopulationCountRequest()
                        : null,
                    rows: options.Count
                        ? null
                        : new SubjectRelationPopulationRowsRequest(
                            options.Limit ?? int.MaxValue),
                    includeNonPublic: options.IncludeAll,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        if (outcome
            is not ExactTypeRelationsInspectionOutcome.Available available)
        {
            CommandError.Write(
                ((ExactTypeRelationsInspectionOutcome.Unavailable)outcome)
                    .Detail);
            return 1;
        }

        if (options.Count)
        {
            if (available.Relations.Population.Count
                is SubjectRelationPopulationCountOutcome.Counted counted)
            {
                int count = options.Limit is int limit
                    ? Math.Min(counted.Value, limit)
                    : counted.Value;
                if (options.Rows is { IsUnlimited: false } rows)
                {
                    (int start, int end) = rows.Resolve(count);
                    count = end - start;
                }
                CountOutput.WriteCount(
                    count,
                    outputPath: null);
                return 0;
            }

            CommandError.Write(
                "The exact Subject Relations count is incomplete because "
                    + "one or more candidate assemblies could not be "
                    + "inspected or resolved.");
            return 1;
        }

        if (available.Relations.Population.Rows
            is not SubjectRelationPopulationRowsOutcome.Read read)
        {
            CommandError.Write(
                "Subject Relations rows are unavailable.");
            return 1;
        }
        List<TypeRelationResult> results =
        [
            .. read.Items.Select(row =>
                ToTypeRelationResult(
                    row,
                    available.Relations.Relations,
                    request)),
        ];
        results = [.. RowWindow.Apply(options.Rows, results)];
        TypeRelationsResultView view = BuildTypeRelationsView(
            request.Type,
            [.. results],
            implementers,
            derivedTypes);
        if (options.JsonOutput
            && (options.Columns is { Length: > 0 }
                || options.Fields is { Length: > 0 }))
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        writerOptions),
                !options.CompactJson);
            return available.Relations.Relations.Evidence.HasUsableRows
                ? 0
                : 1;
        }
        if (options.JsonOutput)
        {
            JsonOutputHelper.Write(
                results,
                TypeRelationsJsonContext.Default.ListTypeRelationResult,
                TypeRelationsCompactJsonContext.Default
                    .ListTypeRelationResult,
                options.CompactJson);
            return available.Relations.Relations.Evidence.HasUsableRows
                ? 0
                : 1;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        writerOptions),
                null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(
                Console.Out,
                rows: null,
                writerOptions => MarkoutSerializer.Serialize(
                    view,
                    SearchViewContext.Default,
                    writerOptions));
        }
        return available.Relations.Relations.Evidence.HasUsableRows
            ? 0
            : 1;
    }

    private sealed record CliTypeRelationsRequest(
        TypeRelationsInspectionRequest Inspection,
        string Type,
        string Source,
        string? SourceVersion,
        IEmbeddedContentProvider? EmbeddedContent = null);

    private static async Task<CliTypeRelationsRequest?>
        CreateSubjectRelationsTypeRequestAsync(
        TypeOptions options,
        WorkspaceContextLoadOptions capabilities,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.PlatformAssembly)
            && string.IsNullOrWhiteSpace(options.PackagePath)
            && options.AssemblyPath is null
            && options.ProjectPath is null
            && options.ProjectAssetsPath is null
            && options.WorkspacePacket is null
            && !string.IsNullOrWhiteSpace(options.TypeName))
        {
            var (assemblyPath, resolvedFamily, platformVersion, error) =
                await PlatformResolver.ResolveAssemblyAsync(
                        options.PlatformAssembly,
                        capabilities.HttpClient,
                        capabilities.Log,
                        options.PlatformFramework,
                        sourceOptions: options.SourceOptions)
                    .ConfigureAwait(false);
            if (assemblyPath is null
                || string.IsNullOrWhiteSpace(resolvedFamily)
                || string.IsNullOrWhiteSpace(platformVersion)
                || error is not null)
            {
                throw new InvalidOperationException(
                    error
                    ?? $"Could not resolve platform library "
                        + $"'{options.PlatformAssembly}'.");
            }
            string framework = options.Tfm
                ?? ApiSourceResolver.TryGetReferencePackTargetFramework(
                    assemblyPath)
                ?? PlatformTargetFramework(platformVersion);
            return new(
                new(
                    new WorkspaceContextInput
                    {
                        Framework = framework,
                        Members =
                        [
                            WorkspaceMemberCoordinate.Platform(
                                resolvedFamily,
                                options.PlatformAssembly,
                                version: platformVersion,
                                framework: framework),
                        ],
                    },
                    options.TypeName),
                options.TypeName,
                resolvedFamily,
                platformVersion);
        }

        if (!string.IsNullOrWhiteSpace(options.AssemblyPath)
            && string.IsNullOrWhiteSpace(options.PackagePath)
            && options.PlatformAssembly is null
            && options.ProjectPath is null
            && options.ProjectAssetsPath is null
            && options.WorkspacePacket is null
            && !string.IsNullOrWhiteSpace(options.TypeName))
        {
            return await CreateEmbeddedTypeRelationsRequestAsync(
                    [options.AssemblyPath],
                    options.TypeName,
                    SourceKind.Library,
                    sourceVersion: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(options.ProjectPath)
            && string.IsNullOrWhiteSpace(options.PackagePath)
            && options.AssemblyPath is null
            && options.PlatformAssembly is null
            && options.WorkspacePacket is null
            && !string.IsNullOrWhiteSpace(options.TypeName))
        {
            if (!ProjectAssetsParser.TryFindAssets(
                    options.ProjectPath,
                    out string? assetsPath,
                    out ProjectAssetsStatus assetsStatus))
            {
                throw new InvalidOperationException(
                    ProjectAssetsParser.DescribeMissingAssets(
                        options.ProjectPath,
                        assetsStatus));
            }
            string[] assemblies =
            [
                .. ProjectAssetsParser.Parse(
                        assetsPath,
                        options.Tfm,
                        log: null)
                    .Select(static asset => asset.Path)
                    .Distinct(StringComparer.Ordinal),
            ];
            return await CreateEmbeddedTypeRelationsRequestAsync(
                    assemblies,
                    options.TypeName,
                    SourceKind.Project,
                    options.Tfm,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(options.PackagePath)
            || File.Exists(options.PackagePath)
            || options.PackagePath.Contains("::", StringComparison.Ordinal)
            || options.PackageRangeAddress is not null
            || options.AssemblyPath is not null
            || options.PlatformAssembly is not null
            || options.PlatformFramework is not null
            || options.ProjectPath is not null
            || options.ProjectAssetsPath is not null
            || options.WorkspacePacket is not null
            || string.IsNullOrWhiteSpace(options.Tfm)
            || options.Tfm.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(options.TypeName))
        {
            return null;
        }

        (string packageId, string? version) =
            PackageExtractor.ParsePackageReference(options.PackagePath);
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        try
        {
            return new(
                new(
                    new WorkspaceContextInput
                    {
                        Framework = options.Tfm,
                        Members =
                        [
                            WorkspaceMemberCoordinate.Package(
                                packageId,
                                version,
                                options.Tfm),
                        ],
                    },
                    options.TypeName),
                options.TypeName,
                packageId,
                version);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<CliTypeRelationsRequest>
        CreateEmbeddedTypeRelationsRequestAsync(
            IReadOnlyList<string> assemblyPaths,
            string type,
            string source,
            string? sourceVersion,
            CancellationToken cancellationToken)
    {
        if (assemblyPaths.Count == 0)
        {
            throw new InvalidOperationException(
                "The selected source contains no managed assemblies.");
        }

        var content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var members = new List<WorkspaceMemberCoordinate>(
            assemblyPaths.Count);
        for (int index = 0; index < assemblyPaths.Count; index++)
        {
            string path = assemblyPaths[index];
            byte[] bytes = await File.ReadAllBytesAsync(
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
            string declaredName =
                System.Reflection.AssemblyName.GetAssemblyName(path).Name
                ?? throw new BadImageFormatException(
                    $"The selected library '{path}' has no assembly name.");

            string contentRef = $"assemblies/{index:D4}.dll";
            string digest = Convert.ToHexString(
                    SHA256.HashData(bytes))
                .ToLowerInvariant();
            content.Add(contentRef, bytes);
            members.Add(
                WorkspaceMemberCoordinate.Embedded(
                    contentRef,
                    digest,
                    declaredName));
        }

        return new(
            new(
                new WorkspaceContextInput
                {
                    Members = [.. members],
                },
                type),
            type,
            source,
            sourceVersion,
            new CliEmbeddedContentProvider(content));
    }

    private sealed class CliEmbeddedContentProvider(
        IReadOnlyDictionary<string, byte[]> content)
        : IEmbeddedContentProvider
    {
        public bool TryOpenContent(
            string contentRef,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? contentStream)
        {
            if (content.TryGetValue(contentRef, out byte[]? bytes))
            {
                contentStream = new MemoryStream(
                    bytes,
                    writable: false);
                return true;
            }

            contentStream = null;
            return false;
        }
    }

    private static string PlatformTargetFramework(string platformVersion)
    {
        if (!NuGetVersion.TryParse(
                platformVersion,
                out NuGetVersion? version))
        {
            throw new InvalidOperationException(
                $"Platform version '{platformVersion}' cannot be mapped "
                    + "to a target framework.");
        }
        return $"net{version.Major}.{version.Minor}";
    }

    private static TypeRelationResult ToTypeRelationResult(
        SubjectRelationRow row,
        WorkspaceTypeHierarchyRelationsResult relations,
        CliTypeRelationsRequest request)
    {
        var sourceType =
            (InspectionGraphTypeIdentity.AcquiredDefinition)
                ((InspectionGraphSubject.TypeSubject)row.Source).Identity;
        WorkspaceTypeHierarchyRelationSource source =
            relations.Sources.Single(candidate =>
                ReferenceEquals(
                    candidate.Registration,
                    sourceType.Registration));
        return new(
            MetadataTypeNameFormatter.FormatFullName(sourceType.Type),
            row.Form == SubjectRelationForm.Interface
                ? "interface"
                : "base type",
            source.Assembly.Name,
            request.Source,
            request.SourceVersion);
    }

    private static TypeRelationsResultView BuildTypeRelationsView(
        string targetType,
        List<TypeRelationResult> results,
        bool implementers,
        bool derivedTypes)
    {
        List<TypeRelationRow> Rows(SubjectRelationForm form) =>
        [
            .. results
                .Where(result =>
                    form == SubjectRelationForm.Interface
                        ? result.Relationship == "interface"
                        : result.Relationship == "base type")
                .OrderBy(
                    static result => result.Type,
                    StringComparer.Ordinal)
                .Select(result => new TypeRelationRow(
                    MarkoutInline.Code(result.Type),
                    result.Relationship,
                    result.Library,
                    SourceColumn.Format(
                        result.Source,
                        result.SourceVersion))),
        ];

        List<TypeRelationRow>? interfaceRows =
            implementers
                ? Rows(SubjectRelationForm.Interface)
                : null;
        List<TypeRelationRow>? baseRows =
            derivedTypes
                ? Rows(SubjectRelationForm.BaseType)
                : null;
        return new()
        {
            Title = $"Relations for {targetType}",
            Description = results.Count == 0
                ? "No matching type relations found."
                : null,
            Implementers = interfaceRows,
            DerivedTypes = baseRows,
        };
    }

    internal static async Task<int> ExecuteSharedExactTypeAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionRequest request,
        WorkspaceContextLoadOptions capabilities)
    {
        InspectionEnvelope<ExactTypeInspectionResult> envelope;
        try
        {
            envelope = await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                capabilities).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }

        ExactTypeInspectionResult result = envelope.Content;
        if (options.EnvelopeOutput || options.JsonOutput)
        {
            bool wrote = InspectionEnvelopeOutput.TryWrite(
                envelope,
                ExactTypeJsonContract,
                options.EnvelopeOutput,
                options.CompactJson);
            if (!result.IsAvailable)
                WriteExactTypeNonSuccess(envelope);
            else
                WriteInspectionDiagnostics(envelope.Diagnostics);
            return wrote && result.IsComplete ? 0 : 1;
        }

        if (!result.IsAvailable)
        {
            WriteExactTypeNonSuccess(envelope);
            return 1;
        }

        return await ExecuteExactTypeResultAsync(
            options,
            plan,
            result,
            envelope.Diagnostics,
            ExactTypeRenderSource.From(request)).ConfigureAwait(false);
    }

    static async Task<int> ExecuteWorkspaceExactTypeAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        WorkspaceContextLoadOptions capabilities,
        CancellationToken cancellationToken)
    {
        WorkspacePacketRestorationResult result =
            await WorkspacePacketRestoration.RestoreAsync(
                options.WorkspacePacket!,
                capabilities,
                cancellationToken).ConfigureAwait(false);
        if (result is WorkspacePacketRestorationResult.Failed failed)
        {
            CommandError.Write(failed.Summary, failed.Details);
            return 1;
        }

        WorkspacePacketRestoration restoration =
            ((WorkspacePacketRestorationResult.Restored)result).Value;
        return await restoration.ExecuteAsync(async activeRestoration =>
        {
            WorkspaceTypeShareChoice shareChoice =
                WorkspaceTypeShareChoice.From(options);
            SelectedContextExactTypeLiveTarget? liveTarget = null;
            InspectionEnvelope<SelectedContextExactTypeInspectionResult>
                envelope =
                    SelectedContextExactTypeInspectionOperation
                        .ExecuteWithLiveTarget(
                            activeRestoration.Workspace,
                            activeRestoration.Activation,
                            new SelectedContextExactTypeInspectionRequest(
                                options.TypeName!),
                            target => liveTarget = target,
                            facet: shareChoice.Facet,
                            scope: options.IncludeAll
                                ? ApiSurfaceScope.IncludeAll
                                : ApiSurfaceScope.PublicWithNonPublicTypes);
            ExactTypeInspectionResult inspection =
                envelope.Content.Inspection;
            if (!inspection.IsAvailable)
            {
                WriteExactTypeNonSuccess(
                    inspection,
                    envelope.Diagnostics,
                    envelope.Content.DefiningSources.Select(
                        FormatDefiningSource));
                return 1;
            }

            SelectedContextExactTypeSource source =
                AssertSingleDefiningSource(envelope.Content);
            int outputExitCode =
                await ExecuteWorkspaceExactTypeResultAsync(
                    options with
                    {
                        WorkspacePacket = null,
                        ShareFormat = null,
                    },
                    plan,
                    inspection,
                    envelope.Diagnostics,
                    ExactTypeRenderSource.From(source),
                    liveTarget
                        ?? throw new InvalidOperationException(
                            "An available Workspace Type result requires a "
                                + "live inspection target."))
                    .ConfigureAwait(false);
            if (options.ShareFormat is not { } shareFormat)
                return outputExitCode;

            InspectionShare share =
                shareChoice.Refusal ?? envelope.Share;
            int shareExitCode =
                WorkspaceShareOutput.Write(share, shareFormat);
            return outputExitCode != 0 || shareExitCode != 0
                ? 1
                : 0;
        }).ConfigureAwait(false);
    }

    static SelectedContextExactTypeSource AssertSingleDefiningSource(
        SelectedContextExactTypeInspectionResult result) =>
        result.DefiningSources.Length == 1
            ? result.DefiningSources[0]
            : throw new InvalidOperationException(
                "An available selected-context exact Type requires one "
                    + "defining source.");

    static async Task<int> ExecuteWorkspaceExactTypeResultAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionResult result,
        IEnumerable<InspectionDiagnostic> diagnostics,
        ExactTypeRenderSource renderSource,
        SelectedContextExactTypeLiveTarget target)
    {
        WriteInspectionDiagnostics(diagnostics);

        try
        {
            using WorkspaceTypeAssemblyPath assemblyPath =
                WorkspaceTypeAssemblyPath.Create(target);
            ApiSurface api = target.Surface;
            api.Name = renderSource.Name;
            api.Version = renderSource.Version;
            api.Source = renderSource.Source;
            api.Tfm = renderSource.TargetFramework;
            api.Library =
                target.Assembly.AssetFileName
                ?? (target.AssemblyPath is { } materializedPath
                    ? Path.GetFileName(materializedPath)
                    : target.Assembly.Identity.Name + ".dll");

            var sourceAssemblies =
                new Dictionary<ApiType, ResolvedAssemblyReference>(
                    ReferenceEqualityComparer.Instance)
                {
                    [target.Type] = target.Assembly,
                };
            var bindingContext = new SelectedTypeBindingContext(
                target.Occurrence,
                target.BindingPolicy);
            var bindingContexts =
                new Dictionary<ApiType, SelectedTypeBindingContext>(
                    ReferenceEqualityComparer.Instance)
                {
                    [target.Type] = bindingContext,
                };
            var loaded = new ApiServices.LoadedApiSurface(
                api,
                assemblyPath.Value,
                assemblyPath.Value,
                sourceAssemblies,
                RootBindingContext: bindingContext,
                BindingContexts: bindingContexts);
            var source = new ApiSourceResult(
                SearchPath: assemblyPath.Value,
                RuntimeAssemblyPath:
                    renderSource.Source == SourceKind.Platform
                        ? assemblyPath.Value
                        : null,
                PackageName: renderSource.PackageName,
                PackageVersion: renderSource.PackageVersion,
                ResolvedPackagePath: renderSource.ResolvedPackagePath,
                PackageExtractPath: target.PackageExtractPath,
                ApiSource: renderSource.Source,
                ApiVersion: renderSource.Version,
                PlatformFramework: renderSource.PlatformFramework,
                SelectedTfm: renderSource.TargetFramework,
                ProjectAssetsPath: null,
                TempDir: null,
                TypeName:
                    target.Type.DefinitionName?.ToEscapedFullName()
                    ?? target.Type.FullName,
                PackageReplaySourceUrls: null,
                PackageReplayUsesOriginalSources: false,
                Context: new CommandContext(options.Verbose));
            int exitCode = await ExecuteCoreAsync(
                options,
                plan,
                source,
                loaded,
                preselectedType: target.Type).ConfigureAwait(false);
            return exitCode != 0 || !result.IsComplete
                ? 1
                : 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    static WorkspaceContextLoadOptions CreateWorkspaceContextLoadOptions(
        TypeOptions options) =>
        new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization =
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            UseVersionCache = true,
            Log = options.Verbose
                ? CommandError.WriteLine
                : null,
        };

    static async Task<int> ExecuteExactTypeResultAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionResult result,
        IEnumerable<InspectionDiagnostic> diagnostics,
        ExactTypeRenderSource renderSource)
    {
        WriteInspectionDiagnostics(diagnostics);

        ExactTypeApi exactType = result.Type!;
        MetadataTypeDefinitionName definitionName =
            MetadataTypeDefinitionName.Create(
                exactType.DefinitionIdentity.Namespace,
                exactType.DefinitionIdentity.Segments) switch
            {
                MetadataTypeDefinitionNameResult.Valid valid =>
                    valid.Name,
                _ => throw new InvalidOperationException(
                    "The exact Type result contained an invalid definition identity."),
            };
        var type = new ApiType
        {
            Namespace = exactType.Namespace,
            Name = exactType.Name,
            MetadataName =
                definitionName.ToNestedMetadataName(),
            DefinitionName = definitionName,
            IntroducedTypeParameterCounts =
                [.. exactType.IntroducedTypeParameterCounts],
            Kind = exactType.Kind,
            Accessibility = exactType.Accessibility,
            Attributes = [.. exactType.Attributes],
            IsSealed = exactType.IsSealed,
            IsAbstract = exactType.IsAbstract,
            IsStatic = exactType.IsStatic,
            IsByRefLike = exactType.IsByRefLike,
            IsReadOnly = exactType.IsReadOnly,
            BaseType = exactType.BaseType,
            Interfaces = [.. exactType.Interfaces],
            DerivedTypes = [.. exactType.DerivedTypes],
            TypeParameters =
            [
                .. exactType.TypeParameters.Select(parameter =>
                    new TypeParameter
                    {
                        Name = parameter.Name,
                        Variance = parameter.Variance,
                        Constraints = [.. parameter.Constraints],
                    }),
            ],
            Members =
            [
                .. exactType.Members.Select(member =>
                    new ApiMember
                    {
                        Name = member.Name,
                        Kind = member.Kind,
                        Signature = member.Signature,
                        IsFinalizer = member.Kind == "finalizer",
                    }),
            ],
            EnumUnderlyingType = exactType.EnumUnderlyingType,
            IsForwarded = exactType.IsForwarded,
        };
        ExactTypeAssemblyIdentity supplier =
            result.SupplierAssembly!;
        string assemblyFile = supplier.Identity.Name + ".dll";
        var api = new ApiSurface
        {
            Name = renderSource.Name,
            Version = renderSource.Version,
            Source = renderSource.Source,
            Tfm = renderSource.TargetFramework,
            Library = assemblyFile,
            Types = [type],
            PublicTypeCount = 1,
            InspectionFailures =
            [
                .. result.InspectionFailures.Select(failure =>
                    new ApiSurfaceInspectionFailure(
                        failure.Operation,
                        failure.SubjectToken,
                        failure.Mechanism,
                        failure.Kind,
                        failure.Detail,
                        failure.SubjectAssembly,
                        failure.DependencyAssembly)),
            ],
        };
        var loaded = new ApiServices.LoadedApiSurface(
            api,
            assemblyFile,
            assemblyFile,
            new Dictionary<ApiType, ResolvedAssemblyReference>(
                ReferenceEqualityComparer.Instance));
        var source = new ApiSourceResult(
            SearchPath: ".",
            RuntimeAssemblyPath: null,
            PackageName: renderSource.PackageName,
            PackageVersion: renderSource.PackageVersion,
            ResolvedPackagePath: renderSource.ResolvedPackagePath,
            PackageExtractPath: ".",
            ApiSource: renderSource.Source,
            ApiVersion: renderSource.Version,
            PlatformFramework: renderSource.PlatformFramework,
            SelectedTfm: renderSource.TargetFramework,
            ProjectAssetsPath: null,
            TempDir: null,
            TypeName: exactType.FullName,
            PackageReplaySourceUrls: null,
            PackageReplayUsesOriginalSources: false,
            Context: new CommandContext(options.Verbose));
        int exitCode = await ExecuteCoreAsync(
            options,
            plan,
            source,
            loaded).ConfigureAwait(false);
        return exitCode != 0 || !result.IsComplete
            ? 1
            : 0;
    }

    static void WriteExactTypeNonSuccess(
        InspectionEnvelope<ExactTypeInspectionResult> envelope)
        => WriteExactTypeNonSuccess(
            envelope.Content,
            envelope.Diagnostics);

    static void WriteExactTypeNonSuccess(
        ExactTypeInspectionResult result,
        IEnumerable<InspectionDiagnostic> diagnostics,
        IEnumerable<string>? additionalDetails = null)
    {
        string? primaryCode = result.Outcome switch
        {
            ExactTypeInspectionOutcome.NotFound =>
                "exact-type.not-found",
            ExactTypeInspectionOutcome.Ambiguous =>
                "exact-type.ambiguous",
            _ => null,
        };
        InspectionDiagnostic? primary = primaryCode is null
            ? null
            : diagnostics.FirstOrDefault(diagnostic =>
                diagnostic.Code == primaryCode);
        WriteInspectionDiagnostics(
            primary is null
                ? diagnostics
                : diagnostics.Where(diagnostic =>
                    !ReferenceEquals(diagnostic, primary)));
        if (primary is not null)
        {
            CommandError.Write(
                primary.Summary.ToString(),
                [
                    .. ApiTypeLookupResult.SuggestionDetails(
                        result.Suggestions),
                    .. (additionalDetails ?? []),
                ]);
        }
        else if (!diagnostics.Any(diagnostic =>
            diagnostic.Severity
                == InspectionDiagnosticSeverity.Error))
        {
            CommandError.Write(
                $"Could not inspect Type '{result.RequestedType}'.");
        }
    }

    static string FormatDefiningSource(
        SelectedContextExactTypeSource source) =>
        source.Library switch
        {
            TypeDeclarationLocatorSectionCoordinate.PackageCoordinate package =>
                $"{package.Package.PackageId}@{package.Package.Version}: "
                    + source.Library.LibraryIdentity.Name,
            TypeDeclarationLocatorSectionCoordinate.PlatformCoordinate platform =>
                $"{platform.Family}: "
                    + source.Library.LibraryIdentity.Name,
            TypeDeclarationLocatorSectionCoordinate.ProjectCoordinate =>
                $"project: {source.Library.LibraryIdentity.Name}",
            TypeDeclarationLocatorSectionCoordinate.LocalCoordinate =>
                $"local: {source.Library.LibraryIdentity.Name}",
            _ => source.Library.LibraryIdentity.Name,
        };

    sealed record ExactTypeRenderSource(
    string Name,
    string? Version,
    string Source,
    string? TargetFramework,
    string? PackageName,
    string? PackageVersion,
    string? ResolvedPackagePath,
    string? PlatformFramework)
    {
        internal static ExactTypeRenderSource From(
            ExactTypeInspectionRequest request) =>
            new(
                request.PackageId,
                request.Version,
                SourceKind.NuGet,
                request.TargetFramework,
                request.PackageId,
                request.Version,
                $"{request.PackageId}@{request.Version}",
                PlatformFramework: null);

        internal static ExactTypeRenderSource From(
            SelectedContextExactTypeSource source)
        {
            return source.Observation.Realization switch
            {
                TypeDeclarationLocatorRealization.PackageRealization package =>
                    new(
                        package.PackageId,
                        package.Version,
                        SourceKind.NuGet,
                        package.Framework,
                        package.PackageId,
                        package.Version,
                        $"{package.PackageId}@{package.Version}",
                        PlatformFramework: null),
                TypeDeclarationLocatorRealization.PlatformRealization platform =>
                    new(
                        platform.Family,
                        platform.Version,
                        SourceKind.Platform,
                        platform.Framework,
                        PackageName: null,
                        PackageVersion: null,
                        ResolvedPackagePath: null,
                        PlatformFramework: platform.Framework),
                _ => new(
                    source.Library.LibraryIdentity.Name,
                    source.Library.LibraryIdentity.Version?.ToString(),
                    source.Library
                        is TypeDeclarationLocatorSectionCoordinate
                            .ProjectCoordinate
                            ? SourceKind.Project
                            : SourceKind.Library,
                    source.Observation.Selection switch
                    {
                        TypeDeclarationLocatorSelection.ProjectSelection project =>
                            project.Tfm,
                        _ => null,
                    },
                    PackageName: null,
                    PackageVersion: null,
                    ResolvedPackagePath: null,
                    PlatformFramework: null),
            };
        }
    }

    sealed class WorkspaceTypeAssemblyPath : IDisposable
    {
        const long MaximumCompiledDocumentationBytes = 8 * 1024 * 1024;

        WorkspaceTypeAssemblyPath(
            string value,
            string? temporaryDirectory)
        {
            Value = value;
            _temporaryDirectory = temporaryDirectory;
        }

        readonly string? _temporaryDirectory;

        internal string Value { get; }

        internal static WorkspaceTypeAssemblyPath Create(
            SelectedContextExactTypeLiveTarget target)
        {
            if (target.AssemblyPath is { } existing)
            {
                return new WorkspaceTypeAssemblyPath(
                    Path.GetFullPath(existing),
                    temporaryDirectory: null);
            }

            string temporaryDirectory =
                Directory.CreateTempSubdirectory(
                    "inspect-type-workspace").FullName;
            string path = Path.Combine(
                temporaryDirectory,
                "selected.dll");
            try
            {
                using Stream input = target.Assembly.OpenRead();
                using FileStream output = File.Create(path);
                input.CopyTo(output);
                if (target.OpenCompiledDocumentation is { } openDocumentation)
                {
                    using Stream? documentation =
                        openDocumentation(
                            MaximumCompiledDocumentationBytes);
                    if (documentation is not null)
                    {
                        using FileStream documentationOutput =
                            File.Create(
                                Path.ChangeExtension(path, ".xml"));
                        CopyBounded(
                            documentation,
                            documentationOutput,
                            MaximumCompiledDocumentationBytes);
                    }
                }
                return new WorkspaceTypeAssemblyPath(
                    path,
                    temporaryDirectory);
            }
            catch
            {
                TryDeleteTemporaryDirectory(temporaryDirectory);
                throw;
            }
        }

        static void CopyBounded(
            Stream input,
            Stream output,
            long maximumBytes)
        {
            byte[] buffer = GC.AllocateUninitializedArray<byte>(81920);
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += read;
                if (total > maximumBytes)
                {
                    throw new InvalidDataException(
                        "Compiled XML documentation exceeds the configured "
                            + "byte limit.");
                }

                output.Write(buffer, 0, read);
            }
        }

        public void Dispose()
        {
            if (_temporaryDirectory is not null)
                TryDeleteTemporaryDirectory(_temporaryDirectory);
        }

        static void TryDeleteTemporaryDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }

    internal sealed record WorkspaceTypeShareChoice(
        ViewFacetId? Facet,
        InspectionShare.NonProjectable? Refusal)
    {
        internal static WorkspaceTypeShareChoice From(
            TypeOptions options)
        {
            if (options.ShareFormat is null)
                return new(Facet: null, Refusal: null);

            if (options.IncludeAll
                || options.MemberFilter.Count != 0
                || options.KindFilter.Count != 0
                || options.UnsafeOnly
                || options.IncludeSections is { Count: > 0 }
                || options.Limit is not null
                || options.Select is { Length: > 0 }
                || options.SelectDefault
                || options.PerformanceTriage.HasFilters
                || options.BodyKindQuery.HasFilter
                || options.CloneCandidateQuery.HasPredicates)
            {
                return new(
                    Facet: null,
                    new InspectionShare.NonProjectable(
                        "type/query",
                        "The requested Type filtering or section selection "
                            + "has no portable Workspace query "
                            + "representation."));
            }

            return new(new ViewFacetId("type.api"), Refusal: null);
        }
    }

    static void WriteInspectionDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            string message = diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(message);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(message);
                    break;
                case InspectionDiagnosticSeverity.Error:
                    CommandError.Write(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(diagnostics),
                        diagnostic.Severity,
                        "Unknown inspection diagnostic severity.");
            }
        }
    }

    private static int? TryExecuteMetadataTypeCount(
        ApiSourceResult source,
        TypeOptions options)
    {
        if (!options.Count
            || !string.Equals(
                source.ApiSource,
                SourceKind.Platform,
                StringComparison.Ordinal)
            || source.RuntimeAssemblyPath is null
            || options.IncludeAll
            || options.TypeFilter is not null
            || options.KindFilter.Count > 0
            || options.UnsafeOnly
            || options.TypeListingRowSelection is not null
            || options.Limit.HasValue
            || options.Rows is not null
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || options.EffectiveDiscovery
            || options.EnvelopeOutput
            || options.PerformanceTriage.HasFilters
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates
            || options.IncludeSections
                is not { Count: > 0 } sections)
        {
            return null;
        }

        if (options.CountDefaultPopulation
            && sections.SetEquals(
                ApiTypeSectionDescriptors.FindingSectionNames))
        {
            if (ApiServices.CountTypeListing(source)
                is not ApiTypeInventoryCountResult.Counted total)
            {
                return null;
            }

            CountOutput.WriteCount(total.Count.Total);
            return 0;
        }

        if (sections.Count != 1)
            return null;

        ApiTypeInventoryKind? kind = sections.Single() switch
        {
            SectionNames.Classes => ApiTypeInventoryKind.Class,
            SectionNames.Structs => ApiTypeInventoryKind.Struct,
            SectionNames.Interfaces =>
                ApiTypeInventoryKind.Interface,
            SectionNames.Enums => ApiTypeInventoryKind.Enum,
            SectionNames.Delegates =>
                ApiTypeInventoryKind.Delegate,
            _ => null,
        };
        if (kind is null)
            return null;

        if (ApiServices.CountTypeListing(source)
            is not ApiTypeInventoryCountResult.Counted counted)
        {
            return null;
        }

        CountOutput.WriteCount(counted.Count.Count(kind.Value));
        return 0;
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
                   SectionNames.BodyShapeSummary,
               ]);

    internal static bool AuthorizesSourceInfoAcquisition(
        ApiType apiType,
        TypeOptions options)
        => ApiCommand.GetRequestedMemberSections(apiType, options)
            .Contains(SectionNames.SourceFiles);

    private static bool AuthorizesWholeTypeDecompilation(
        ApiType apiType,
        TypeOptions options)
        => options.Verbosity != Verbosity.Quiet
           && options.IncludeSections is { Count: > 0 }
           && ApiCommand.GetRequestedMemberSections(apiType, options)
               .Contains(SectionNames.DecompiledSource);

    private static bool AuthorizesTypeSource(
        ApiType apiType,
        TypeOptions options)
        => options.IncludeSections is { Count: > 0 }
           && options.IncludeSections.Contains(SectionNames.Source);

    private static bool AuthorizesTypeApiDeclarations(
        ApiType apiType,
        TypeOptions options)
        => options.Verbosity != Verbosity.Quiet
           && options.IncludeSections is { Count: > 0 }
           && ApiCommand.GetRequestedMemberSections(apiType, options)
               .Contains(SectionNames.ApiDeclarations);

    private static async Task<TypeOptions>
        AttachTypeApiDeclarationInspectionAsync(
        ApiType apiType,
        TypeOptions options,
        ApiServices.LoadedApiSurface loaded,
        CancellationToken cancellationToken)
    {
        MetadataTypeDefinitionName type =
            apiType.DefinitionName
            ?? throw new InvalidOperationException(
                $"Type '{apiType.FullName}' has no exact metadata definition "
                    + "identity for API Declarations.");
        ResolvedAssemblyReference definingAssembly =
            loaded.TryGetSourceAssembly(apiType)
            ?? ResolvedAssemblyReference.CreateFromPath(
                apiType.SourceAssemblyPath
                    ?? loaded.ApiDllPath,
                AssemblyResolutionProvenance.Local(
                    "type API declarations"));
        SelectedTypeBindingContext? bindingContext =
            loaded.TryGetBindingContext(apiType)
            ?? loaded.RootBindingContext;
        IAssemblyBindingPolicy bindingPolicy =
            bindingContext?.Policy
            ?? (definingAssembly.Path is { } definingAssemblyPath
                ? new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(
                        definingAssemblyPath)
                {
                    ProjectAssetsPath = options.ProjectAssetsPath,
                    TargetFramework = options.Tfm,
                    IncludeDepsJsonAssets = false,
                    IncludeAspNetCoreSharedFramework = false,
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                })
                : throw new InvalidOperationException(
                    "A pathless selected API participant requires its "
                        + "authoritative binding policy."));
        var participant =
            new AssemblyContextParticipant(
                definingAssembly,
                bindingPolicy);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        InspectionEnvelope<TypeApiDeclarationResult> inspection =
            TypeApiDeclarationInspection.Execute(
                group,
                participant,
                type,
                options.IncludeAll
                    ? TypeApiDeclarationScope.All
                    : TypeApiDeclarationScope.ApiVisible,
                new ApiSurfaceProjectionLimits(
                    1,
                    1_000_000,
                    1_000_000,
                    1_000,
                    1_000_000,
                    10_000_000),
                cancellationToken);
        WriteInspectionDiagnostics(inspection.Diagnostics);
        return options with
        {
            TypeApiDeclarationInspection = inspection,
        };
    }

    private static async Task<TypeOptions>
        AttachTypeSourceInspectionAsync(
        ApiType apiType,
        TypeOptions options,
        ApiServices.LoadedApiSurface loaded,
        string? packageName,
        string? packageVersion,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        string assemblyPath =
            apiType.SourceAssemblyPath
            ?? loaded.ApiDllPath;
        ResolvedAssemblyReference definingAssembly =
            loaded.TryGetSourceAssembly(apiType)
            ?? ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    "type Source"));
        SelectedTypeBindingContext? bindingContext =
            loaded.TryGetBindingContext(apiType)
            ?? loaded.RootBindingContext;
        var (participant, queryContext) =
            AuthoredSourceDocumentPrinter.CreateContext(
                assemblyPath,
                options,
                definingAssembly,
                packageName,
                packageVersion,
                httpClient,
                bindingContext?.Policy);

        InspectionEnvelope<AssemblyTypeSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([participant]);
            inspection =
                await TypeSourceInspection.ExecuteAsync(
                        group,
                        participant,
                        AssemblyTypeSourceRequest.From(
                            apiType,
                            options.RenderOptions),
                        queryContext,
                        cancellationToken)
                    .ConfigureAwait(false);
        }

        ApiCommand.WriteSourceInspectionDiagnostics(
            inspection.Diagnostics);
        return options with
        {
            TypeSourceInspection = inspection,
        };
    }

    private static async Task<TypeOptions>
        AttachTypeDocumentInspectionAsync(
            ApiType apiType,
            TypeOptions options,
            string apiDllPath,
            ResolvedAssemblyReference? sourceAssembly,
            HttpClient httpClient,
            CancellationToken cancellationToken)
    {
        string typeAssemblyPath =
            apiType.SourceAssemblyPath
            ?? apiDllPath;
        DecompilationInspectionPreparation.Prepared preparation =
            await DecompilationInspectionPreparation.CreateAsync(
                    typeAssemblyPath,
                    sourceAssembly,
                    options,
                    httpClient,
                    "type document",
                    cancellationToken)
                .ConfigureAwait(false);

        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [preparation.Participant]);
        InspectionEnvelope<Decompiler.CSharpTypeDocumentOutcome>
            inspection =
                await TypeDocumentInspection.ExecuteAsync(
                        group,
                        preparation.Participant,
                        AssemblyTypeSourceRequest.From(
                            apiType,
                            options.RenderOptions),
                        preparation.QueryContext,
                        preparation.PortablePdb,
                        cancellationToken)
                    .ConfigureAwait(false);
        return options with
        {
            TypeDocumentInspection = inspection,
        };
    }

    private static bool ShouldRejectQuietShape(TypeOptions options)
    {
        if (options.UserVerbosity != Verbosity.Quiet)
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
            logger,
            options.PlatformFramework);
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
            Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
        };

        // This renders a listing for what entered as a single-type request, so a select the
        // preamble deferred resolves here, against the pipeline doing the rendering. Without this
        // the deferred select would be dropped and the listing would ignore -S entirely.
        if (browseOptions.SelectDeferredToListing
            || browseOptions.DiscoverDeferredToListing
            || browseOptions.Select is { Length: > 0 }
            || browseOptions.SelectDefault
            || browseOptions.CountDefaultPopulation)
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
            PlatformFrameworks =
                string.IsNullOrWhiteSpace(options.PlatformFramework)
                    ? CommandLineBuilder.PlatformFrameworkNames
                    : [options.PlatformFramework],
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

        bool hasExplicitTarget =
            !string.IsNullOrWhiteSpace(options.PlatformFramework);
        var merged = new ApiSurface
        {
            Name = query,
            Source = SourceKind.Platform,
            Version = hasExplicitTarget
                ? null
                : string.Join(
                    ", ",
                    distinctResults
                        .Select(r => $"{r.Source}@{r.SourceVersion}")
                        .Distinct()),
            Tfm = hasExplicitTarget ? null : "platform"
        };
        string? targetVersion = null;
        string? targetTfm = null;

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

            if (hasExplicitTarget)
            {
                string? resolvedTfm =
                    ApiSourceResolver
                        .TryGetReferencePackTargetFramework(assemblyPath);
                if (string.IsNullOrWhiteSpace(version)
                    || string.IsNullOrWhiteSpace(resolvedTfm))
                {
                    throw new InvalidOperationException(
                        "The explicit platform target did not resolve "
                            + "an exact version and reference-pack TFM.");
                }
                if (targetVersion is not null
                    && (!string.Equals(
                            targetVersion,
                            version,
                            StringComparison.Ordinal)
                        || !string.Equals(
                            targetTfm,
                            resolvedTfm,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        "The explicit platform prefix browse resolved "
                            + "multiple target identities.");
                }
                targetVersion = version;
                targetTfm = resolvedTfm;
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

        if (hasExplicitTarget)
        {
            merged.Version = targetVersion;
            merged.Tfm = targetTfm;
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
            DiscoveryOutputRequest.Create(
                OutputFormatResolver.ResolveStored(
                    options.Format,
                    options.JsonOutput,
                    options.PlainText,
                    options.Tabular,
                    options.Tsv,
                    options.Jsonl),
                options.Tree,
                options.TabularExplicitlySet,
                options.NoHeader,
                (int)options.Verbosity,
                options),
            sectionCostAnnotations: typePipeline.GetCostAnnotations(),
            sectionCategories: typePipeline.GetCategoryMap());
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
