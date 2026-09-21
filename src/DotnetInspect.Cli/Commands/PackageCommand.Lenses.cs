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
using DotnetInspector.Ecosystems;
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
using Markout.Formatting;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{

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

    private static bool TryValidatePackageTargetFramework(
        InspectionOptions options)
    {
        if (!HasTargetFrameworkFileFilter(options)
            || !RequestsPackageFileRows(options))
        {
            return true;
        }

        try
        {
            _ = PackageHouseTargetContext.Exact(options.Tfm!);
            return true;
        }
        catch (ArgumentException)
        {
            CommandError.Write(
                $"Invalid --tfm value '{options.Tfm}': expected a bounded ASCII target moniker.");
            return false;
        }
    }

    private static bool TryCreatePackageInfoTargetContext(
        InspectionOptions options,
        InspectionOptions producerOptions,
        SectionPipeline<InspectionResult> pipeline,
        out PackageHouseTargetContext? targetContext)
    {
        targetContext = null;
        if (options.ListVersions
            || options.ListLayout
            || options.ListTfms
            || options.ShowContent
            || options.PackageLibrary is not null
            || options.AllLibraries
            || !RequestsPackageHouseCompileRealization(
                producerOptions,
                pipeline))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.Tfm)
            || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            targetContext = PackageHouseTargetContext.OwnerDefault();
            return true;
        }

        try
        {
            targetContext = PackageHouseTargetContext.Exact(options.Tfm);
            return true;
        }
        catch (ArgumentException)
        {
            CommandError.Write(
                $"Invalid --tfm value '{options.Tfm}': expected a bounded ASCII target moniker.");
            return false;
        }
    }

    private static bool RequestsPackageInfoMeasurements(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (RequestsSelectedOrDiscoveredSection(
                options,
                PackageSections.PackageInfo,
                pipeline))
        {
            return true;
        }

        return options.IncludeSections is null
            && options.Discover is null
            && pipeline.GetCandidateSections(
                    options.Verbosity,
                    fixedOverview: options.FixedOverview)
                .Contains(PackageSections.PackageInfo);
    }

    private static bool RequestsPackageHouseCompileRealization(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline) =>
        RequestsPackageInfoMeasurements(options, pipeline)
        || RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.EcosystemDependencies,
            pipeline);

    private static bool RequestsPackageEcosystemDependencies(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline) =>
        RequestsPackageInfoMeasurements(options, pipeline)
        || RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.EcosystemDependencies,
            pipeline);

    private static NuspecData? FindPackageNuspecForInspection(
        string extractPath,
        PackageExtractionResult resolution,
        bool ecosystemRecognitionRequested)
    {
        try
        {
            return NuspecParser.FindAndParse(extractPath);
        }
        catch (NuspecParseException) when (
            ecosystemRecognitionRequested
            && CanAttributePackageEcosystemRecognition(resolution))
        {
            return null;
        }
    }

    private static bool CanAttributePackageEcosystemRecognition(
        PackageExtractionResult resolution) =>
        resolution.HouseSettlement is PackageHouseSettlement.Acquired
        || PackageEcosystemDependencyRecognitionInspection
            .TryCreateUnavailableWithoutAcquiredSettlement(
                resolution.PackageName,
                resolution.Version,
                resolution.ProducerKey,
                out _);

    private static async Task ApplyPackageInfoMeasurementsAsync(
        InspectionResult result,
        PackageExtractionResult resolution,
        long? fallbackPackageSize,
        string? requestedTargetFramework,
        Action<string>? log)
    {
        InspectionEnvelope<PackageInfoMeasurements>? inspection = null;
        if (resolution.HouseSettlement
                is PackageHouseSettlement.Acquired toolSettlement
            && await PackageToolDeclarationEvidence.TryCreateAsync(
                toolSettlement.Payload) is { } declaration)
        {
            inspection =
                PackageInfoMeasurementInspection.ProjectDeclaredTool(
                    toolSettlement,
                    declaration,
                    requestedTargetFramework?.Equals(
                        "all",
                        StringComparison.OrdinalIgnoreCase) == true
                            ? null
                            : requestedTargetFramework);
        }
        else if (resolution.HouseSettlement
            is PackageHouseSettlement.Acquired settlement)
        {
            inspection =
                PackageInfoMeasurementInspection.Project(settlement);
        }

        if (inspection is not null)
            ApplyPackageInfoMeasurementInspection(result, inspection, log);
        else
            result.PackageSize = fallbackPackageSize;
    }

    private static void ApplyPackageInfoMeasurementInspection(
        InspectionResult result,
        InspectionEnvelope<PackageInfoMeasurements> inspection,
        Action<string>? log)
    {
        result.PackageInfoMeasurementInspection = inspection;
        result.PackageSize = inspection.Content.CompressedPackageBytes;
        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
            log?.Invoke($"{diagnostic.Code}: {diagnostic.Summary}");
    }

    private static async Task ApplyPackageEcosystemDependenciesAsync(
        InspectionResult result,
        PackageExtractionResult resolution,
        bool discloseEmptyDetailDiagnostics,
        Action<string>? log)
    {
        InspectionEnvelope<EcosystemDependencyRecognitionOutcome> inspection;
        if (resolution.HouseSettlement
            is not PackageHouseSettlement.Acquired settlement)
        {
            if (!PackageEcosystemDependencyRecognitionInspection
                    .TryCreateUnavailableWithoutAcquiredSettlement(
                        result.PackageName,
                        result.Version,
                        resolution.ProducerKey,
                        out InspectionEnvelope<
                            EcosystemDependencyRecognitionOutcome>?
                            unavailableInspection)
                && !PackageEcosystemDependencyRecognitionInspection
                    .TryCreateUnavailableWithoutAcquiredSettlement(
                        resolution.PackageName,
                        resolution.Version,
                        resolution.ProducerKey,
                        out unavailableInspection))
            {
                throw new InvalidOperationException(
                    "Package ecosystem recognition cannot attribute the "
                    + "acquired package to an exact canonical package "
                    + "coordinate.");
            }

            inspection = unavailableInspection;
        }
        else
        {
            inspection =
                await PackageEcosystemDependencyRecognitionInspection
                    .ExecuteAsync(settlement)
                    .ConfigureAwait(false);
        }

        ApplyPackageEcosystemDependencies(
            result,
            inspection,
            discloseEmptyDetailDiagnostics,
            log);
    }

    private static void ApplyPackageEcosystemDependencies(
        InspectionResult result,
        InspectionEnvelope<EcosystemDependencyRecognitionOutcome> inspection,
        bool discloseEmptyDetailDiagnostics,
        Action<string>? log)
    {
        result.EcosystemDependencyRecognitionInspection = inspection;
        bool discloseDiagnostics =
            discloseEmptyDetailDiagnostics
            && inspection.Content switch
            {
                EcosystemDependencyRecognitionOutcome.Incomplete incomplete =>
                    incomplete.Document.Classification.Recognized.IsEmpty,
                EcosystemDependencyRecognitionOutcome.Unavailable => true,
                _ => false,
            };
        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            string message = $"{diagnostic.Code}: {diagnostic.Summary}";
            if (discloseDiagnostics)
            {
                CommandError.WriteWarning(message);
            }
            else
            {
                log?.Invoke(message);
            }
        }
    }

    private static bool RequiresPackageEcosystemDiagnosticDisclosure(
        InspectionOptions options) =>
        options.IncludeSections is { } sections
        && sections.Contains(PackageSections.EcosystemDependencies)
        && !sections.Contains(PackageSections.PackageInfo);

    private static int ListPackageLayout(string extractPath, InspectionOptions options, string packageName, TipLevel tipLevel)
    {
        string searchPath;
        string relativeBase;

        // Scope to a specific TFM if requested
        if (!string.IsNullOrEmpty(options.Tfm))
        {
            string? scope = options.ScopeLib
                ? "lib"
                : options.ScopeTools
                    ? "tools"
                    : null;
            string scopedDirectory =
                Path.Combine(extractPath, scope ?? "lib", options.Tfm);
            string toolsDirectory =
                Path.Combine(extractPath, "tools", options.Tfm);

            if (Directory.Exists(scopedDirectory))
                searchPath = scopedDirectory;
            else if (scope is null && Directory.Exists(toolsDirectory))
                searchPath = toolsDirectory;
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
            .Select(
                f => Path.GetRelativePath(relativeBase, f)
                    .Replace('\\', '/'))
            .Where(p => !PackageFileLister.IsPlumbing(
                p))
            .OrderBy(p => p);

        var results = relativePaths.ToList();
        if (!SemanticRowSelection.TrySelectOrApplyLegacy(
                options.PackageLayoutRowSelection,
                options.Rows,
                results,
                "Package layout files",
                failure =>
                    $"Package layout file row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} layout file rows are available.",
                out IReadOnlyList<string> visibleResults))
        {
            return 1;
        }

        if (LensProjection.TryProject(options, "--layout", visibleResults.Count, out var projectionExitCode))
            return projectionExitCode;

        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            output =>
            {
                if (options.JsonOutput)
                {
                    output.WriteLine(
                        JsonSerializer.Serialize(
                            visibleResults
                                .Select(path => new PackageLayoutFileJson(path))
                                .ToList(),
                            JsonContext.Default.ListPackageLayoutFileJson));
                    return;
                }

                if (options.Jsonl)
                {
                    OutputFormatter.WriteStringList(
                        visibleResults,
                        "Path",
                        "path",
                        tsv: false,
                        jsonl: true,
                        output: output);
                    return;
                }

                PackageOutputFormatter.WriteFileTree(
                    [.. visibleResults],
                    output);
            });

        WriteFileLayoutTips(
            extractPath,
            options,
            packageName,
            tipLevel,
            isLayout: true);
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
        if (!SemanticRowSelection.TrySelectOrApplyLegacy(
                options.PackageTfmRowSelection,
                options.Rows,
                tfms,
                "Package TFMs",
                failure =>
                    $"Package TFM row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} TFM rows are available.",
                out IReadOnlyList<string> visibleTfms))
        {
            return 1;
        }

        if (LensProjection.TryProject(
                options,
                "--tfms",
                visibleTfms.Count,
                out var projectionExit,
                ["TFM"]))
            return projectionExit;

        if (options.JsonOutput)
        {
            Console.Out.WriteLine(
                JsonSerializer.Serialize(
                    visibleTfms
                        .Select(tfm => new PackageTfmJson(tfm))
                        .ToList(),
                    JsonContext.Default.ListPackageTfmJson));
            return 0;
        }

        OutputFormatter.WriteStringList(visibleTfms, "TFM", "Tfm", options.Tsv, options.Jsonl, Console.Out);
        return 0;
    }

    private static bool IsSingleDependencyHierarchySelection(
        InspectionOptions options) =>
        options.IncludeSections is { Count: 1 }
        && options.IncludeSections.Contains(
            PackageSections.DependencyHierarchy);

    private static void WritePackageDependencyHierarchyTree(
        InspectionResult result,
        InspectionOptions options)
    {
        DependsAssetProjection projection =
            result.DependencyHierarchyProjection
            ?? throw new InvalidOperationException(
                "The Package dependency hierarchy was not acquired.");
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows =
            !options.DependencyHierarchyRowsSelected
            && options.Rows is { IsUnlimited: false } window
                ? window.Apply(projection.HierarchyRows)
                : projection.HierarchyRows;
        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            writer =>
            {
                var markout = new MarkoutWriter(
                    writer,
                    new PlainTextFormatter());
                markout.WriteGraph(
                    DependencyHierarchyOutputAdapter.ToGraph(
                        projection.Hierarchy,
                        rows,
                        markWindowedFragments: true));
                markout.Flush();
            });
    }

    private static bool WritePackageDependencyHierarchyProjection(
        InspectionResult result,
        InspectionOptions options)
    {
        DependsAssetProjection projection =
            result.DependencyHierarchyProjection
            ?? throw new InvalidOperationException(
                "The Package dependency hierarchy was not acquired.");
        var projectionOptions = new DependsOptions
        {
            Format = options.Format,
            Rows = options.DependencyHierarchyRowsSelected
                ? null
                : options.Rows,
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            NoHeader = options.NoHeader,
            Columns = options.Columns,
            Fields = options.Fields,
        };
        bool success = false;
        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            output =>
            {
                success = DependsCommand.WriteAssetProjection(
                    projection,
                    projectionOptions,
                    new HashSet<string>(
                        [DependsAssetSections.DependencyHierarchy],
                        StringComparer.OrdinalIgnoreCase),
                    output);
            });
        return success;
    }
}
