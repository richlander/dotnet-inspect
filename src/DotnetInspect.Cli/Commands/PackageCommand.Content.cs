using DotnetInspect.Cli.Models;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticRowSelection =
    DotnetInspect.Cli.CommandLine.CliSemanticRowSelection;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
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

public partial class PackageCommand
{

    private sealed record PackageFileContentSet(string PackageName, string Version, List<PackageFileContent> Files);

    private sealed class PackageFileContentAcquisition(
        PackageExtractionResult resolution,
        string packageName,
        string packageVersion,
        string? readmeFile,
        string? declaredReadmeFile,
        string? declaredLicenseFile) : IDisposable
    {
        public PackageFileContentSet Read(
            InspectionOptions options,
            bool suppressUnaryPayloadRead,
            string? selectedPayloadPath = null)
            => ReadPackageFileContents(
                resolution.ExtractPath,
                packageName,
                packageVersion,
                readmeFile,
                declaredReadmeFile,
                declaredLicenseFile,
                options,
                suppressUnaryPayloadRead,
                selectedPayloadPath);

        public void Dispose()
            => CleanupPackageExtraction(resolution);
    }

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

        var destination = PackagePayloadDestination(options);
        if (!ProjectionDestinationWriter.ValidateBeforeAcquisition(destination))
            return 1;

        var results = new List<PackageFileContentSet>();
        bool unaryPayload = RequiresUnaryPackageContent(options);
        if (!unaryPayload)
        {
            foreach (var target in targets)
            {
                var result = await ReadPackageFileContentsAsync(
                    target,
                    options,
                    context,
                    suppressUnaryPayloadRead: false);
                if (result == null)
                    return 1;
                results.Add(result);
            }

            return PrintPackageFileContents(results, options);
        }

        var acquisitions = new List<PackageFileContentAcquisition>();
        try
        {
            foreach (var target in targets)
            {
                PackageFileContentAcquisition? acquisition =
                    await AcquirePackageFileContentAsync(
                        target,
                        options,
                        context);
                if (acquisition == null)
                    return 1;
                acquisitions.Add(acquisition);
                results.Add(
                    acquisition.Read(
                        options,
                        suppressUnaryPayloadRead: true));
            }

            if (SelectUnaryPackageContent(results, options) is { } selectedFile)
            {
                int selectedPackage = results.FindIndex(
                    result => result.Files.Any(
                        file => ReferenceEquals(file, selectedFile)));
                if (selectedPackage < 0)
                    throw new InvalidOperationException(
                        "The selected package content row has no owning package.");

                results[selectedPackage] =
                    acquisitions[selectedPackage].Read(
                        options,
                        suppressUnaryPayloadRead: true,
                        selectedFile.Path);
            }

            return PrintPackageFileContents(results, options);
        }
        finally
        {
            foreach (var acquisition in acquisitions)
                acquisition.Dispose();
        }
    }

    private static PackageFileContent? SelectUnaryPackageContent(
        IReadOnlyList<PackageFileContentSet> results,
        InspectionOptions options)
    {
        List<PackageFileContent> visibleFiles =
        [
            .. RowWindow.Apply(
                options.Rows,
                FlattenPackageFileContentRows(results, options).ToList())
                .Where(static file => file.Found),
        ];
        return visibleFiles is [var selectedFile]
            ? selectedFile
            : null;
    }

    private static async Task<PackageFileContentSet?> ReadPackageFileContentsAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        CommandContext context,
        bool suppressUnaryPayloadRead)
    {
        using PackageFileContentAcquisition? acquisition =
            await AcquirePackageFileContentAsync(
                target,
                options,
                context);
        return acquisition?.Read(
            options,
            suppressUnaryPayloadRead);
    }

    private static async Task<PackageFileContentAcquisition?>
        AcquirePackageFileContentAsync(
            PackageReferenceTarget target,
            InspectionOptions options,
            CommandContext context)
    {
        var logger = context.Logger;
        PackageExtractionResult? resolution = null;
        bool ownershipTransferred = false;
        string version = target.Version;
        try
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(
                context.HttpClient,
                target.IsLocalFile
                    ? target.OriginalArgument
                    : target.PackageName,
                logger.Log,
                sourceOptions: options.SourceOptions,
                version: target.IsLocalFile
                    ? null
                    : (version.Length > 0 ? version : null),
                forceLatest: options.ForceLatest,
                includePrerelease: options.IncludePrerelease);

            if (!outcome.IsSuccess)
            {
                CommandError.Write($"{outcome.ErrorMessage}");
                return null;
            }

            resolution = outcome.Result!;
            version = resolution.Version ?? version;
            var nuspec =
                DotnetInspector.Services.NuspecParser.FindAndParse(
                    resolution.ExtractPath);
            var acquisition =
                new PackageFileContentAcquisition(
                    resolution,
                    nuspec?.PackageName
                        ?? resolution.PackageName
                        ?? target.PackageName,
                    nuspec?.Version ?? version,
                    PackageFileLister.ResolvePackageReadme(
                        resolution.ExtractPath,
                        nuspec?.ReadmeFile),
                    nuspec?.ReadmeFile,
                    nuspec?.LicenseDeclaration is
                        {
                            Kind: PackageLicenseDeclarationKind.File,
                        } license
                            ? license.Value
                            : null);
            ownershipTransferred = true;
            return acquisition;
        }
        finally
        {
            if (!ownershipTransferred)
                CleanupPackageExtraction(resolution);
        }
    }

    private static void CleanupPackageExtraction(
        PackageExtractionResult? resolution)
    {
        if (resolution is not
            {
                FromCache: false,
                TempDir: not null
            }
            || !Directory.Exists(resolution.TempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(
                resolution.TempDir,
                recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static async Task<InspectionResult?> InspectPackageAsync(
        PackageReferenceTarget target,
        InspectionOptions options,
        InspectionOptions producerOptions,
        CommandContext context,
        bool wantsFilesSection,
        SectionCatalog<InspectionResult> sectionCatalog,
        PackageSourceQueryPlan sourceQueryPlan)
    {
        SectionPipeline<InspectionResult> pipeline = sectionCatalog.Pipeline;
        var logger = context.Logger;
        string? extractPath = null;
        PackageExtractionResult? resolution = null;
        string version = target.Version;

        if (!TryCreatePackageInfoTargetContext(
                options,
                producerOptions,
                pipeline,
                out PackageHouseTargetContext? packageInfoTargetContext))
        {
            return null;
        }

        try
        {
            PackageExtractionOutcome outcome;
            if (!target.IsLocalFile
                && !DotnetInspector.Networking.HttpClientFactory.IsOffline)
            {
                outcome = PackageExtractor.TryNormalizePackageVersion(
                        version,
                        out string pinnedVersion)
                    ? await PackageExtractor.ExtractPinnedPackageAsync(
                        context.HttpClient,
                        target.PackageName,
                        pinnedVersion,
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        createComposition:
                            context.CreatePackageSourceComposition,
                        compileTargetContext:
                            packageInfoTargetContext)
                    : await PackageExtractor.ExtractSelectedPackageAsync(
                        context.HttpClient,
                        target.PackageName,
                        version.Length > 0 ? version : null,
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        includePrerelease: options.IncludePrerelease,
                        createComposition:
                            context.CreatePackageSourceComposition,
                        compileTargetContext:
                            packageInfoTargetContext);
            }
            else
            {
                outcome = await PackageExtractor.ExtractPackageAsync(
                    context.HttpClient,
                    target.IsLocalFile
                        ? target.OriginalArgument
                        : target.PackageName,
                    logger.Log,
                    sourceOptions: options.SourceOptions,
                    version: target.IsLocalFile
                        ? null
                        : version.Length > 0
                            ? version
                            : null,
                    forceLatest: options.ForceLatest,
                    includePrerelease: options.IncludePrerelease);
            }

            if (!outcome.IsSuccess)
            {
                CommandError.Write($"{outcome.ErrorMessage}");
                return null;
            }

            resolution = outcome.Result!;
            PackageVersionDisclosure.WriteServedPriorWarning(resolution);
            extractPath = resolution.ExtractPath;
            version = resolution.Version ?? version;
            string resolvedPackageName =
                resolution.PackageName ?? target.PackageName;

            bool wantsEcosystemDependencies =
                RequestsPackageEcosystemDependencies(
                    producerOptions,
                    pipeline);
            NuspecData? nuspec = FindPackageNuspecForInspection(
                extractPath,
                resolution,
                wantsEcosystemDependencies);

            long? packageSize = null;
            if (resolution.NupkgPath != null && File.Exists(resolution.NupkgPath))
                packageSize = new FileInfo(resolution.NupkgPath).Length;

            bool wantsSignals = RequestsSelectedOrDiscoveredSection(
                producerOptions,
                PackageSections.Signals,
                pipeline);
            bool wantsRidPackageAvailability =
                RequestsRidPackageAvailability(
                    producerOptions,
                    target.IsLocalFile,
                    pipeline);
            bool wantsIdentifierMetadata =
                RequiresIdentifierMetadata(producerOptions, pipeline);
            bool wantsPackageMetadata =
                RequiresPackageMetadata(producerOptions, pipeline);
            using var vulnerabilityTrafficScope = AllowsVulnerabilityTraffic(
                producerOptions)
                ? NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData)
                : null;
            var result = await PackageInspector.InspectAsync(
                resolution,
                resolvedPackageName,
                version,
                target.IsLocalFile,
                target.IsLocalFile ? target.OriginalArgument : null,
                nuspec,
                context.HttpClient,
                logger,
                options.ForceLatest,
                producerOptions.Verbosity,
                fetchMetadata: wantsPackageMetadata,
                requireIdentifierMetadata: wantsIdentifierMetadata,
                verifyRidPackageAvailability: wantsRidPackageAvailability,
                sourceOptions: options.SourceOptions);

            await ApplyPackageInfoMeasurementsAsync(
                result,
                resolution,
                packageSize,
                options.Tfm,
                logger.Log);
            if (wantsEcosystemDependencies)
            {
                await ApplyPackageEcosystemDependenciesAsync(
                    result,
                    resolution,
                    RequiresPackageEcosystemDiagnosticDisclosure(
                        producerOptions),
                    logger.Log);
            }

            await PopulatePackageSignatureAsync(
                result,
                resolution.NupkgPath,
                ShouldVerifyPackageSignature(options, wantsSignals),
                logger.Log);

            result.Source = target.IsLocalFile ? SourceKind.File : SourceKind.NuGet;

            if (wantsFilesSection)
                PopulatePackageFileSections(result, extractPath, options);

            if (ShouldPopulatePackageContentAudit(
                    producerOptions,
                    pipeline))
            {
                if (result.PackageFiles is null)
                    PopulatePackageFileSections(result, extractPath, options);
                PopulatePackageContentAudit(result, extractPath);
            }

            if (ShouldPopulatePackageSourceFiles(producerOptions)
                || !sourceQueryPlan.SectionPlan.Queries.IsEmpty)
            {
                await PopulatePackageSourceLinkAsync(
                    result,
                    extractPath,
                    resolvedPackageName,
                    version,
                    producerOptions,
                    context,
                    logger,
                    sourceQueryPlan);
            }

            FilterResultForOutput(result, options);

            if (wantsSignals)
            {
                result.BinarySignals = await PackageInspector.ScanBinarySignalsAsync(
                    extractPath, resolvedPackageName, version, context.HttpClient, logger,
                    acquirePdb: true, options.SourceOptions);
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

    private static async Task PopulatePackageSignalsAsync(
        InspectionResult result,
        string extractPath,
        string? packageName,
        string? version,
        HttpClient client,
        VerboseLogger logger,
        NuGetSourceOptions? sourceOptions)
    {
        result.BinarySignals = await PackageInspector.ScanBinarySignalsAsync(
            extractPath, packageName, version, client, logger,
            acquirePdb: true, sourceOptions);
        await AuditSignalBuilder.PopulatePackageAuditAsync(
            result, client, logger, sourceOptions);
    }

    private static async Task PopulatePackageSignatureAsync(
        InspectionResult result,
        string? nupkgPath,
        bool shouldVerify,
        Action<string> log)
    {
        if (nupkgPath is null || !shouldVerify)
            return;

        log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
        result.SignatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
    }

    private static bool ShouldVerifyPackageSignature(
        InspectionOptions options,
        bool wantsSignals)
        => options.Verbosity >= Verbosity.Normal
            || wantsSignals
            || ProjectionRequestsSigned(options.Fields)
            || options.Columns?.Contains(
                "Signed",
                StringComparer.OrdinalIgnoreCase) == true
            || options.IncludeSections?.Contains(
                PackageSections.Signature) == true;

    private static bool ProjectionRequestsSigned(string[]? selectors)
        => selectors is { Length: > 0 }
            && new DocumentSchema()
                .Add(PackageSections.PackageInfo, "field", "Signed")
                .ValidateProjection(PackageSections.PackageInfo, selectors)
                .Resolved.Length > 0;

    private static string[] VersionListingColumns(InspectionOptions options)
        => options.IncludeUnlisted
            ? ["Version", "Listing"]
            : ["Version"];

    private static string[] VersionFeedColumns(
        IReadOnlyList<PackageVersionSourceInfo> rows,
        InspectionOptions options)
        => options.JsonOutput || rows.Any(static row => !row.Listed)
            ? ["Version", "Feed", "Listing"]
            : ["Version", "Feed"];

    private static List<PackageFile> FilterPackageFiles(List<PackageFile> files, InspectionOptions options)
    {
        List<PackageFile> scopedFiles =
            HasTargetFrameworkFileFilter(options)
                ? PackageFileLister.FilterByTargetFramework(
                    files,
                    options.Tfm!)
                : files;
        var selectors = PathSelectors(options);
        if (selectors.Length == 0)
            return scopedFiles;

        if (options.PathMatchMode.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var selector in selectors)
            {
                var matches = PackageFileLister.Filter(
                    scopedFiles,
                    selector);
                if (matches.Count > 0)
                    return [matches[0]];
            }
            return [];
        }

        var selectedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var selector in selectors)
        {
            foreach (var match in PackageFileLister.Filter(
                scopedFiles,
                selector))
            {
                selectedPaths.Add(match.Path);
            }
        }
        return scopedFiles
            .Where(file => selectedPaths.Contains(file.Path))
            .ToList();
    }

    private static string[] PathSelectors(InspectionOptions options)
        => options.PathFilters is { Length: > 0 } filters ? filters
            : options.PathFilter is { Length: > 0 } filter ? [filter]
            : [];

    private static bool HasPathFilter(InspectionOptions options) => PathSelectors(options).Length > 0;

    private static bool HasTargetFrameworkFileFilter(
        InspectionOptions options)
        => options.Tfm is not null
            && !options.Tfm.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase);

    private static bool HasPackageFileFilter(InspectionOptions options)
        => HasPathFilter(options)
            || HasTargetFrameworkFileFilter(options);

    private static bool RequestsPackageFileRows(InspectionOptions options)
        => HasPathFilter(options)
            || options.IncludeSections?.Any(IsPackageFileSection) == true
            || SelectResolver.IsActiveAllSelector(
                options.Select,
                options.IncludeSections)
            || options.Discover != null;

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

    internal static PackageSourceQueryPlan CreatePackageSourceQueryPlan(
        SectionCatalog<InspectionResult> sectionCatalog,
        InspectionQueryCatalog<SourceLinkQueryContext> queryCatalog,
        InspectionOptions options,
        bool excludeUnbounded)
    {
        SectionQueryPlan sectionPlan = sectionCatalog.PlanQueries(
            options.Verbosity,
            options.IncludeSections,
            options.FixedOverview,
            excludeUnbounded);
        // The IEnumerable overload boxes ImmutableArray; use the direct common-plan overloads
        // and reserve general compilation for uncommon multi-query demand.
        InspectionQueryPlan<SourceLinkQueryContext> queryPlan =
            sectionPlan.Queries.Length switch
            {
                0 => queryCatalog.Plan(
                    Array.Empty<InspectionQueryDefinition>()),
                1 => queryCatalog.Plan(sectionPlan.Queries[0]),
                _ => queryCatalog.Plan(sectionPlan.Queries),
            };
        return new PackageSourceQueryPlan(
            sectionPlan,
            queryPlan);
    }

    internal readonly record struct PackageSourceQueryPlan(
        SectionQueryPlan SectionPlan,
        InspectionQueryPlan<SourceLinkQueryContext> QueryPlan);

    private static async Task PopulatePackageSourceLinkAsync(
        InspectionResult result,
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options,
        CommandContext context,
        VerboseLogger logger,
        PackageSourceQueryPlan sourceQueryPlan)
    {
        var requestedQueries = sourceQueryPlan.SectionPlan.Queries;
        bool collectSourceFiles = ShouldPopulatePackageSourceFiles(options);
        bool auditAvailability =
            requestedQueries.Contains(SourceAvailabilityQuery.Definition);
        bool auditIntegrity =
            requestedQueries.Contains(SourceIntegrityQuery.Definition);
        if (collectSourceFiles)
            result.SourceFiles = [];

        int auditedLibraries = 0;
        int totalSourceFiles = 0;
        int accessibleSourceFiles = 0;
        int embeddedSourceFiles = 0;
        List<PackageSourceLinkFile> missingFiles = [];
        List<PackageSourceLinkIssue> availabilityUnavailable = [];
        List<PackageSourceLinkIssue> availabilityFailed = [];

        int checkedLibraries = 0;
        int verified = 0;
        int mismatched = 0;
        int lineEndingNormalized = 0;
        int unverifiable = 0;
        List<PackageSourceLinkFile> mismatchedFiles = [];
        List<PackageSourceLinkIssue> integrityUnavailable = [];
        List<PackageSourceLinkIssue> integrityFailed = [];

        var libraries = SelectPackageLibrariesForSourceFiles(extractPath, options);
        foreach (var libraryPath in libraries)
        {
            var relativePath = Path.GetRelativePath(extractPath, libraryPath).Replace('\\', '/');
            try
            {
                using var source = SourceLinkService.Open(libraryPath, logger.Log);
                var queryContext = new SourceLinkQueryContext(
                    source,
                    new FindingSubject(
                        $"package:{packageName}@{version}:{relativePath}",
                        relativePath),
                    context.HttpClient,
                    DotnetInspector.Networking.HttpClientFactory.SharedUntrustedFetch,
                    packageName,
                    version,
                    isPlatformAssembly: false,
                    CoreSourceLinkQueryCache.Instance,
                    logger.Log,
                    options.SourceOptions);

                InspectionQueryResults? queryResults = null;
                if (!requestedQueries.IsEmpty)
                {
                    queryResults = await sourceQueryPlan.QueryPlan
                        .RunAsync(queryContext)
                        .ConfigureAwait(false);
                }
                else if (collectSourceFiles)
                {
                    await PdbAcquisitionService.AcquireAsync(
                        source.Context,
                        context.HttpClient,
                        packageName,
                        version,
                        isPlatformAssembly: false,
                        logger.Log,
                        sourceOptions: options.SourceOptions).ConfigureAwait(false);
                }

                if (collectSourceFiles)
                {
                    List<SourceFileInfo> rows = await SourceFileCollector.CollectAsync(
                        source,
                        libraryPath,
                        preferRenderedUrls: options.PreferRenderedUrls,
                        typeFilter: options.TypeFilter).ConfigureAwait(false);
                    result.SourceFiles!.AddRange(rows.Select(row => new PackageSourceFileInfo(
                        relativePath,
                        row.Type,
                        row.Url)));
                }

                if (auditAvailability
                    && queryResults!.TryGet(
                        SourceAvailabilityQuery.Definition,
                        out SourceAvailabilityResult? availability))
                {
                    switch (availability)
                    {
                        case SourceAvailabilityResult.Available available:
                            auditedLibraries++;
                            totalSourceFiles += available.Summary.TotalSourceFiles;
                            accessibleSourceFiles += available.Summary.AccessibleSourceFiles;
                            embeddedSourceFiles += available.Summary.EmbeddedSourceFiles;
                            missingFiles.AddRange(
                                available.Summary.MissingSourceFiles.Select(
                                    path => new PackageSourceLinkFile(relativePath, path)));
                            break;
                        case SourceAvailabilityResult.Absent absent:
                            availabilityUnavailable.Add(
                                new PackageSourceLinkIssue(
                                    relativePath,
                                    absent.Detail ?? "SourceLink input is unavailable."));
                            break;
                        case SourceAvailabilityResult.Failed failed:
                            availabilityFailed.Add(
                                new PackageSourceLinkIssue(relativePath, failed.Reason));
                            break;
                    }
                }

                if (auditIntegrity
                    && queryResults!.TryGet(
                        SourceIntegrityQuery.Definition,
                        out SourceIntegrityResult? integrity))
                {
                    switch (integrity)
                    {
                        case SourceIntegrityResult.Available available:
                            checkedLibraries++;
                            verified += available.Summary.Verified;
                            mismatched += available.Summary.Mismatched;
                            lineEndingNormalized += available.Summary.LineEndingNormalized;
                            unverifiable += available.Summary.Unverifiable;
                            mismatchedFiles.AddRange(
                                available.Summary.MismatchedFiles.Select(
                                    path => new PackageSourceLinkFile(relativePath, path)));
                            break;
                        case SourceIntegrityResult.Absent absent:
                            integrityUnavailable.Add(
                                new PackageSourceLinkIssue(
                                    relativePath,
                                    absent.Detail ?? "SourceLink input is unavailable."));
                            break;
                        case SourceIntegrityResult.Failed failed:
                            integrityFailed.Add(
                                new PackageSourceLinkIssue(relativePath, failed.Reason));
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InspectionQueryException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (collectSourceFiles || (!auditAvailability && !auditIntegrity))
                    throw;

                logger.LogWarning(
                    $"Could not inspect SourceLink for {relativePath}: {ex.Message}");
                if (auditAvailability)
                {
                    availabilityFailed.Add(
                        new PackageSourceLinkIssue(relativePath, ex.Message));
                }
                if (auditIntegrity)
                {
                    integrityFailed.Add(
                        new PackageSourceLinkIssue(relativePath, ex.Message));
                }
            }
        }

        if (auditAvailability)
        {
            result.SourceAvailability = new PackageSourceAvailability(
                libraries.Count,
                auditedLibraries,
                totalSourceFiles,
                accessibleSourceFiles,
                embeddedSourceFiles,
                NullIfEmpty(missingFiles),
                NullIfEmpty(availabilityUnavailable),
                NullIfEmpty(availabilityFailed));
        }

        if (auditIntegrity)
        {
            result.SourceIntegrity = new PackageSourceIntegrity(
                libraries.Count,
                checkedLibraries,
                verified,
                mismatched,
                lineEndingNormalized,
                unverifiable,
                NullIfEmpty(mismatchedFiles),
                NullIfEmpty(integrityUnavailable),
                NullIfEmpty(integrityFailed));
        }
    }

    private static List<T>? NullIfEmpty<T>(List<T> values)
        => values.Count == 0 ? null : values;

    private static List<string> SelectPackageLibrariesForSourceFiles(string extractPath, InspectionOptions options)
    {
        var (selected, _) = TfmSelector.SelectHighestAssembliesFromPackage(extractPath, options.Tfm);
        return selected
            .OrderBy(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void PopulatePackageFileSections(InspectionResult result, string extractPath, InspectionOptions options)
    {
        bool wantsPackageFileRows = RequestsPackageFileRows(options);

        var packageReadme = result.PackageReadmeFile
            ?? PackageFileLister.ResolvePackageReadme(extractPath, result.ReadmeFile);
        result.PackageReadmeFile = packageReadme;
        result.HasReadme = packageReadme != null;
        result.HasAgentDocumentation = File.Exists(Path.Combine(extractPath, "AGENTS.md"));
        var files = PackageFileLister.ListAll(
            extractPath,
            packageReadme,
            result.DeclaredLicenseFile);
        result.PackageFiles = files;
        if (wantsPackageFileRows
            && (HasPathFilter(options)
            || options.IncludeSections?.Contains(PackageSections.Files) == true
            || SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
            || options.Discover != null))
        {
            result.Files = HasPackageFileFilter(options)
                ? FilterPackageFiles(files, options)
                : files;
        }
    }

    private static bool ShouldPopulatePackageContentAudit(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
        => RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.AuditFindings,
            pipeline);

    private static void PopulatePackageContentAudit(
        InspectionResult result,
        string extractPath)
    {
        result.PackageContentAudit = PackageContentAudit.Scan(
            extractPath,
            result.PackageFiles?.Select(file => file.Path) ?? []);
    }

    private static PackageFileContentSet ReadPackageFileContents(
        string extractPath,
        string packageName,
        string version,
        string? readmeFile,
        string? declaredReadmeFile,
        string? declaredLicenseFile,
        InspectionOptions options,
        bool suppressUnaryPayloadRead = false,
        string? selectedPayloadPath = null)
    {
        var files = PackageFileLister.ListAll(
            extractPath,
            readmeFile,
            declaredLicenseFile);
        List<PackageFile> selectedFiles =
        [
            .. FilterPackageFiles(files, options)
                .Select(file => WithDeclaredReadmeRole(file, declaredReadmeFile)),
        ];
        bool unaryPayload = RequiresUnaryPackageContent(options);
        bool includeExactContent =
            unaryPayload
            && HasUnstructuredOutputPath(options)
            && options.ContentScope == PackageFileContentScope.Full;
        var contents = selectedFiles
            .Select(file => unaryPayload
                && (selectedPayloadPath is { } selectedPath
                    ? !file.Path.Equals(selectedPath, StringComparison.Ordinal)
                    : suppressUnaryPayloadRead || selectedFiles.Count != 1)
                ? new PackageFileContent(
                    packageName,
                    version,
                    file.Path,
                    file.Size,
                    Found: true,
                    Content: string.Empty,
                    file.IsReadme)
                : ReadPackageFileContent(
                    extractPath,
                    packageName,
                    version,
                    file,
                    options.ContentScope,
                    normalizeGithubLinksToRaw: !options.PreferRenderedUrls,
                    includeExactContent))
            .ToList();
        return new PackageFileContentSet(packageName, version, contents);
    }

    private static bool RequiresUnaryPackageContent(InspectionOptions options)
        => !LensProjection.IsRequested(options)
            && (options.Raw
                || HasUnstructuredOutputPath(options));

    private static bool RequiresEarlyPackagePayloadPreflight(
        InspectionOptions options)
    {
        if (options.ShowContent)
            return true;

        if (options.IncludeSections is not { Count: 1 } sections)
            return false;

        string section = sections.Single();
        return options.Print
            && PackageFileFamily.IsFamilySection(section)
            || options.Raw
            && section.Equals(
                PackageSections.FilesReadme,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnstructuredOutputPath(InspectionOptions options)
        => !string.IsNullOrEmpty(options.OutputPath)
            && !options.JsonOutput
            && !options.Jsonl
            && !options.JsonArray;

    private static bool MayResolveToSkillPayloadBeforeAcquisition(
        InspectionOptions options)
    {
        if ((options.Print || options.Raw)
            && options.IncludeSections is { Count: 1 } sections
            && (sections.Single().Equals(
                    PackageSections.FilesSkills,
                    StringComparison.OrdinalIgnoreCase)
                || sections.Single().Equals(
                    PackageSections.FilesReadme,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string[] selectors = PathSelectors(options);
        return options.ShowContent
            && selectors.Length > 0
            && selectors.All(static selector =>
                selector.Equals("@readme", StringComparison.OrdinalIgnoreCase)
                || !selector.Contains('*')
                    && !selector.Contains('?')
                    && PackageFileFamily.IsSkillDocumentPath(selector));
    }

    private static ProjectionDestination PackagePayloadDestination(
        InspectionOptions options,
        bool? resolvedSkillPayload = null)
        => new(
            options.OutputPath,
            options.Rows,
            ExactTransfer: (options.Print || RequiresUnaryPackageContent(options))
                && HasUnstructuredOutputPath(options)
                && options.ContentScope == PackageFileContentScope.Full
                && !(resolvedSkillPayload
                    ?? MayResolveToSkillPayloadBeforeAcquisition(options)));

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
        bool normalizeGithubLinksToRaw,
        bool includeExactContent)
    {
        var fullPath = Path.Combine(extractPath, file.Path.Replace('/', Path.DirectorySeparatorChar));
        byte[] exactContent = File.ReadAllBytes(fullPath);

        // Scoping and link rewriting are Markdown conventions. Applied to anything else they
        // corrupt the document the package shipped rather than presenting it, and the caller
        // has no way to see that it happened. So Markdown documents are presented, and every
        // other kind is passed through exactly as shipped -- including its byte order mark,
        // which ReadAllText would otherwise consume and silently shorten the document by.
        if (!IsMarkdownDocument(file.Path, file.IsReadme))
        {
            return new PackageFileContent(
                packageName,
                version,
                file.Path,
                file.Size,
                Found: true,
                ReadTextPreservingPreamble(exactContent),
                file.IsReadme,
                includeExactContent ? exactContent : null);
        }

        string sourceContent = ReadText(exactContent);
        var content = MarkdownContent.ApplyScope(
            sourceContent,
            scope,
            out int sourceLineOffset);
        if (PackageFileFamily.IsSkillDocument(file))
        {
            ContainmentSelectedText selected = AgentSkillDocument.PrepareForOutput(
                file.Path,
                content,
                sourceLineOffset,
                normalizeGithubLinksToRaw);
            return new PackageFileContent(
                packageName,
                version,
                file.Path,
                file.Size,
                Found: true,
                selected.ToString(),
                file.IsReadme,
                ExactContent: null,
                SelectedContent: selected);
        }

        if (normalizeGithubLinksToRaw)
            content = GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(content);

        return new PackageFileContent(
            packageName,
            version,
            file.Path,
            file.Size,
            Found: true,
            content,
            file.IsReadme,
            includeExactContent ? exactContent : null);
    }

    /// <summary>
    /// Reads text while keeping any byte order mark the file starts with. Decoding still detects
    /// the encoding from that mark, so the text is decoded correctly; the mark is then restored
    /// as a character so a verbatim document round-trips through the text pipeline with the same
    /// bytes it shipped with rather than three fewer.
    /// </summary>
    private static string ReadTextPreservingPreamble(byte[] content)
    {
        string text = ReadText(content);
        ReadOnlySpan<byte> bytes = content;
        bool hasPreamble =
            bytes.StartsWith(Encoding.UTF8.Preamble)
            || bytes.StartsWith(Encoding.Unicode.Preamble)
            || bytes.StartsWith(Encoding.BigEndianUnicode.Preamble)
            || bytes.StartsWith(Encoding.UTF32.Preamble)
            || bytes.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF });
        return hasPreamble ? '\uFEFF' + text : text;
    }

    private static string ReadText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static int PrintPackageFileContents(IReadOnlyList<PackageFileContentSet> results, InspectionOptions options)
    {
        var rows = FlattenPackageFileContentRows(results, options).ToList();
        var visibleRows = RowWindow.Apply(options.Rows, rows);

        // Same rule the print projection applies: a Markdown scope names a Markdown construct,
        // and non-Markdown documents are passed through verbatim. Without this the scope would
        // be accepted and then silently ignored, answering a --frontmatter request with the
        // whole document -- a projection answered from a different payload than the one asked
        // for, which is the defect class this command is being kept clear of.
        if (options.ContentScope != PackageFileContentScope.Full
            && visibleRows.FirstOrDefault(row => row.Found && !IsMarkdownDocument(row.Path, row.IsReadme)) is { } nonMarkdown)
        {
            CommandError.Write(
                $"--frontmatter/--yaml-header and --body apply to Markdown documents; '{nonMarkdown.Path}' is not Markdown. "
                + "Narrow the selection to Markdown, for example --path \"*.md\".");
            return 1;
        }

        // A path that matches nothing still yields one row so the render can show it as absent.
        // Counting that row would answer "one file matched" when none did, so count found files,
        // as the bare writer below already does.
        if (LensProjection.TryProject(options, "--content", visibleRows.Count(row => row.Found), out var contentProjectionExit))
            return contentProjectionExit;

        List<PackageFileContent> resolvedFiles =
            visibleRows.Where(row => row.Found).Take(2).ToList();
        bool? resolvedSkillPayload = resolvedFiles.Count == 1
            ? resolvedFiles[0].SelectedContent is not null
            : null;
        var destination = PackagePayloadDestination(
            options,
            resolvedSkillPayload);
        if (!ProjectionDestinationWriter.ValidateBeforeDestinationMutation(
                destination))
        {
            return 1;
        }

        if (options.Raw)
            return PrintBarePackageFileContentRows(visibleRows, destination);

        if (HasUnstructuredOutputPath(options)
            && ProjectionDestinationWriter.IsFile(destination))
        {
            List<PackageFileContent> found =
                visibleRows.Where(row => row.Found).ToList();
            if (found.Count != 1)
            {
                CommandError.Write(
                    $"--content --out requires exactly one selected package content file; found {found.Count}.");
                return 1;
            }

            ContainmentDiagnosticOutput.Write(found[0].SelectedContent);
            WritePackageFileExport(found[0], destination);
            return 0;
        }

        foreach (PackageFileContent row in visibleRows.Where(row => row.Found))
            ContainmentDiagnosticOutput.Write(row.SelectedContent);

        var textRows = visibleRows
            .Select(PackageFileContentText.Create)
            .ToList();
        var output = options.Jsonl
            ? RenderPackageFileContentJsonl(textRows)
            : RenderPackageFileContentBlocks(textRows);

        ProjectionDestinationWriter.WriteText(destination, output);

        return 0;
    }

    private static int PrintBarePackageFileContentRows(
        IReadOnlyList<PackageFileContent> rows,
        ProjectionDestination destination)
    {
        var found = rows.Where(row => row.Found).ToList();
        if (found.Count != 1)
        {
            CommandError.Write(found.Count == 0
                ? "--raw found no selected package content."
                : $"--raw requires exactly one selected package content file; found {found.Count}.");
            return 1;
        }

        ContainmentDiagnosticOutput.Write(found[0].SelectedContent);
        if (ProjectionDestinationWriter.IsFile(destination))
        {
            WritePackageFileExport(found[0], destination);
            return 0;
        }

        return WriteBarePackageContent(found[0], destination);
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

    private static string RenderPackageFileContentJsonl(IReadOnlyList<PackageFileContentText> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder
                .Append(JsonSerializer.Serialize(row, PackageFileContentJsonContext.Default.PackageFileContentText))
                .Append('\n');
        return builder.ToString();
    }

    private static string RenderPackageFileContentBlocks(IReadOnlyList<PackageFileContentText> rows)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                builder.AppendLine();

            var row = rows[i];
            var path = row.Found
                ? row.PathText
                : new InertString(TextPolicy.Field, "<absent>");
            // The separator is tool-owned framing, so its untrusted parts are
            // contained even though the file content below it is deliberately
            // raw -- otherwise a ZIP entry path forges a second separator.
            builder.AppendLine(InertString.Format(
                TextPolicy.Field,
                $"------------ {row.PackageText} :: {path} ------------").ToString());
            if (!row.Found)
            {
                builder.AppendLine("(absent)");
                continue;
            }

            builder.Append(row.RenderedContent);
            if (row.Content.Length == 0 || row.Content[^1] != '\n')
            {
                if (row.IsContainmentSelected)
                    builder.Append('\n');
                else
                    builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void WriteMultiPackageTable(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        if (IsPackageFileSection(section))
        {
            WriteMultiPackageFilesTable(results, section, options);
            return;
        }

        WriteMultiPackageFieldTable(results, section, options);
    }

    private static void WriteMultiPackageFilesTable(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        if (options.Jsonl)
        {
            WriteMultiPackageFilesJsonl(results, section, options);
            return;
        }

        var rows = BuildMultiPackageFileRows(results, section, options.SkipEmpty)
            .Select(row => new[]
            {
                row.Package,
                row.Version,
                row.Path,
                row.Size?.ToString(CultureInfo.InvariantCulture) ?? "",
            })
            .ToArray();
        var windowedRows = RowWindow.Apply(options.Rows, rows).ToArray();

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateProjectedWriterOptions(
                options.Columns,
                options.Fields);
            OutputFormatter.ConfigureTableWriterOptions(
                writerOptions,
                options.Tsv,
                options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(
                ["Package", "Version", "Path", "Size"],
                ["package", "version", "path", "size"],
                windowedRows);
            markoutWriter.Flush();
        });
    }

    private static void WritePackageFilesJsonl(
        InspectionResult result,
        string section,
        RowWindow? rows)
    {
        var text = new PackageInspectionText(result);
        var files = GetPackageFileTextRows(result, text, section);
        if (files.Count == 0)
            return;

        foreach (var file in RowWindow.Apply(rows, files))
        {
            var row = new PackageFileJsonRow(file.Path, file.Size);
            Console.WriteLine(JsonSerializer.Serialize(row, PackageFileJsonRowContext.Default.PackageFileJsonRow));
        }
    }

    private static void WriteMultiPackageFilesJsonl(IReadOnlyList<InspectionResult> results, string section, InspectionOptions options)
    {
        var rows = BuildMultiPackageFileRows(results, section, options.SkipEmpty);
        var selectedColumns = ResolveMultiPackageFileColumns(
            section,
            options.Columns);
        foreach (var row in RowWindow.Apply(options.Rows, rows))
            WriteMultiPackageFileJsonRow(row, selectedColumns);
    }

    private static List<PackageFileMultiJsonRow> BuildMultiPackageFileRows(
        IReadOnlyList<InspectionResult> results,
        string section,
        bool skipEmpty)
    {
        var rows = new List<PackageFileMultiJsonRow>();
        foreach (var result in results)
        {
            var text = new PackageInspectionText(result);
            var files = GetPackageFileTextRows(result, text, section);
            if (files.Count == 0)
            {
                if (!skipEmpty)
                {
                    rows.Add(
                        new PackageFileMultiJsonRow(
                            text.PackageName,
                            text.Version,
                            new InertString(TextPolicy.Field, ""),
                            null));
                }
                continue;
            }

            foreach (var file in files)
            {
                rows.Add(
                    new PackageFileMultiJsonRow(
                        text.PackageName,
                        text.Version,
                        file.Path,
                        file.Size));
            }
        }

        return rows;
    }

    private static string[] ResolveMultiPackageFileColumns(
        string section,
        string[]? columns)
    {
        if (columns is not { Length: > 0 })
            return MultiPackageFileColumnNames;

        return ResolveProjectionNames(
            MultiPackageFileColumnNames,
            columns);
    }

    private static void WriteMultiPackageFileJsonRow(
        PackageFileMultiJsonRow row,
        IReadOnlyList<string> columns)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (string column in columns)
            {
                switch (column)
                {
                    case "Package":
                        writer.WriteString("package", row.Package);
                        break;
                    case "Version":
                        writer.WriteString("version", row.Version);
                        break;
                    case "Path":
                        writer.WriteString("path", row.Path);
                        break;
                    case "Size":
                        if (row.Size is { } size)
                            writer.WriteNumber("size", size);
                        else
                            writer.WriteNull("size");
                        break;
                }
            }
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
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
            CommandError.Write("--raw requires exactly one -S section or --content payload.");
            return 1;
        }

        var section = include.Single();
        if (section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase))
        {
            var files = RowWindow.Apply(
                options.Rows,
                GetPackageFileRows(result, section));
            return PrintBarePackageFiles(extractPath, packageName, version, files, options, section);
        }

        if (section.Equals(PackageSections.SourceLinkFiles, StringComparison.OrdinalIgnoreCase))
        {
            var sourceFiles = RowWindow.Apply(options.Rows, result.SourceFiles ?? []);
            var urls = sourceFiles.Select(row => row.Url);
            return PrintBarePackageUrlColumn(
                urls,
                section,
                new ProjectionDestination(options.OutputPath, options.Rows));
        }

        CommandError.Write($"--raw does not support section '{section}'. Select a text section or a single URL section.");
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
                ? $"--raw found no package file in section '{section}'."
                : $"--raw requires section '{section}' to resolve exactly one package file; found {files.Count}.");
            return 1;
        }

        var destination = PackagePayloadDestination(
            options,
            PackageFileFamily.IsSkillDocument(files[0]));
        if (!ProjectionDestinationWriter.ValidateBeforeDestinationMutation(
                destination))
        {
            return 1;
        }

        var content = ReadPackageFileContent(
            extractPath,
            packageName,
            version,
            files[0],
            PackageFileContentScope.Full,
            normalizeGithubLinksToRaw: !options.PreferRenderedUrls,
            includeExactContent: HasUnstructuredOutputPath(options));
        ContainmentDiagnosticOutput.Write(content.SelectedContent);
        if (ProjectionDestinationWriter.IsFile(destination))
        {
            WritePackageFileExport(content, destination);
            return 0;
        }

        return WriteBarePackageContent(content, destination);
    }

    private static int PrintBarePackageUrlColumn(
        IEnumerable<string?>? urls,
        string section,
        ProjectionDestination destination)
    {
        var values = urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToList() ?? [];

        if (values.Count > 0)
            return WriteBarePackageText(string.Join('\n', values), destination);

        CommandError.Write($"--raw found no URL in section '{section}'.");
        return 1;
    }

    private static int WriteBarePackageText(
        string content,
        ProjectionDestination destination)
    {
        var output = content.EndsWith('\n') ? content : content + '\n';
        ProjectionDestinationWriter.WriteRenderedText(destination, output);
        return 0;
    }

    private static int WriteBarePackageContent(
        PackageFileContent content,
        ProjectionDestination destination)
    {
        if (content.SelectedContent is not { } selected)
            return WriteBarePackageText(content.Content, destination);

        ProjectionDestinationWriter.WriteText(
            destination,
            writer =>
            {
                writer.Write(selected.ToString());
                if (!content.Content.EndsWith('\n'))
                    writer.Write('\n');
            });
        return 0;
    }

    private static void WritePackageFileExport(
        PackageFileContent content,
        ProjectionDestination destination)
    {
        if (content.ExactContent is { } exact)
            ProjectionDestinationWriter.WriteExactBytes(destination, exact);
        else if (content.SelectedContent is { } selected)
            ProjectionDestinationWriter.WriteSelectedText(destination, selected);
        else
            ProjectionDestinationWriter.WriteRenderedText(destination, content.Content);
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

    private static List<PackageFileText> GetPackageFileTextRows(
        InspectionResult result,
        PackageInspectionText text,
        string section)
    {
        if (section.Equals(PackageSections.Files, StringComparison.OrdinalIgnoreCase))
            return text.Files ?? [];

        if (section.Equals(PackageSections.FilesReadme, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(result.PackageReadmeFile))
                return [];

            return text.SelectPackageFiles(file =>
                    string.Equals(
                        file.Path,
                        result.PackageReadmeFile,
                        StringComparison.OrdinalIgnoreCase))?
                .Take(1)
                .ToList() ?? [];
        }

        if (PackageFileFamily.PredicateFor(section) is { } predicate)
            return text.SelectPackageFiles(predicate) ?? [];

        return [];
    }

    private static void WriteMultiPackageFieldTable(
        IReadOnlyList<InspectionResult> results,
        string section,
        InspectionOptions options)
    {
        var rows = BuildMultiPackageFieldRows(
            results,
            section,
            options.Fields);

        string rendered = OutputFormatter.RenderProjectedTable(
            !options.NoHeader,
            options.Tsv,
            options.Jsonl,
            options.Columns,
            fields: null,
            (writer, formatter, writerOptions) =>
            {
                var markoutWriter =
                    new MarkoutWriter(writer, formatter, writerOptions);
                markoutWriter.WriteTable(
                    ["Package", "Field", "Value"],
                    ["package", "field", "value"],
                    rows);
                markoutWriter.Flush();
            });
        DiagnoseMissingPackageFieldSectionFields(
            section,
            options.Fields,
            rows.Select(row => row[1]));
        Console.Out.Write(
            OutputFormatter.LimitRenderedTableRows(
                rendered,
                options.Rows,
                !options.NoHeader));
    }

    private static void DiagnoseMissingPackageFieldSectionFields(
        string section,
        string[]? patterns,
        IEnumerable<string> renderedFields)
    {
        if (patterns is not { Length: > 0 })
            return;

        var rendered = renderedFields.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var missing = patterns
            .Where(pattern =>
            {
                string[] resolved = ResolveProjectionNames(
                    GetMultiPackageFieldNames(section),
                    [pattern]);
                return resolved.Length > 0
                    && !resolved.Any(rendered.Contains);
            })
            .ToArray();
        if (missing.Length == 0)
            return;

        string label = missing.Length == 1
            ? "field has"
            : "fields have";
        CommandError.WriteNote(
            $"{missing.Length} {label} no data: {string.Join(", ", missing)}");
    }

    private static IEnumerable<MarkoutField> SelectPackageInfoFields(
        InspectionResult result,
        IReadOnlyList<string>? selectedFields)
    {
        var metadata = new InspectionResultView(result).Metadata;
        if (selectedFields == null)
            return metadata;

        var byName = metadata.ToDictionary(
            field => field.Key,
            StringComparer.OrdinalIgnoreCase);
        return selectedFields
            .Where(byName.ContainsKey)
            .Select(field => byName[field]);
    }

    private static IEnumerable<MarkoutField> SelectPackageFieldSectionFields(
        InspectionResultView view,
        string section,
        IReadOnlyList<string>? selectedFields)
    {
        IEnumerable<MarkoutField> fields =
            section.Equals(
                PackageSections.PackageInfo,
                StringComparison.OrdinalIgnoreCase)
                ? view.Metadata
                : view.SigningSectionData?
                    .ToMarkoutFields()
                    ?? [];
        if (selectedFields == null)
            return fields;

        var byName = fields.ToDictionary(
            field => field.Key,
            StringComparer.OrdinalIgnoreCase);
        return selectedFields
            .Where(byName.ContainsKey)
            .Select(field => byName[field]);
    }

    private static string[][] BuildMultiPackageFieldRows(
        IReadOnlyList<InspectionResult> results,
        string section,
        string[]? fields)
    {
        var selectedFields = ResolvePackageFieldSectionFields(
            section,
            fields);

        return results
            .SelectMany(result =>
            {
                var view = new InspectionResultView(result);
                return SelectPackageFieldSectionFields(
                        view,
                        section,
                        selectedFields)
                .Select(field => new[]
                {
                    view.PackageName,
                    field.Key,
                    field.Value?.ToString() ?? "",
                });
            })
            .ToArray();
    }

    private static int WindowedCount(int count, RowWindow? rows)
    {
        var (start, end) = ResolveRowWindow(count, rows);
        return end - start;
    }

    private static (int Start, int End) ResolveRowWindow(int count, RowWindow? rows)
        => rows is { IsUnlimited: false } window
            ? window.Resolve(count)
            : (0, count);

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
        result.DeclaredLicenseFile = nuspec.LicenseDeclaration is
            {
                Kind: PackageLicenseDeclarationKind.File,
            } license
                ? license.Value
                : null;
        result.PackageTypes = nuspec.PackageTypes;
        result.IsToolPackage = nuspec.IsToolPackage;
        result.ReadmeFile = nuspec.ReadmeFile;
        result.DependencyGroups = nuspec.DependencyGroups;
    }
}
