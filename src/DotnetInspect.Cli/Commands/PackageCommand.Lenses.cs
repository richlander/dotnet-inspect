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
using DotnetInspector.RowSelection;
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
            || !RequestsPackageInfoMeasurements(producerOptions, pipeline))
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

    private static void ApplyPackageInfoMeasurements(
        InspectionResult result,
        PackageExtractionResult resolution,
        long? fallbackPackageSize,
        Action<string>? log)
    {
        if (resolution.HouseSettlement
            is PackageHouseSettlement.Acquired settlement)
        {
            InspectionEnvelope<PackageInfoMeasurements> inspection =
                PackageInfoMeasurementInspection.Project(settlement);
            result.PackageInfoMeasurementInspection = inspection;
            result.PackageSize =
                inspection.Content.CompressedPackageBytes;
            foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
                log?.Invoke($"{diagnostic.Code}: {diagnostic.Summary}");
            return;
        }

        result.PackageSize = fallbackPackageSize;
    }

    private static int ListPackageLayout(string extractPath, InspectionOptions options, string packageName, TipLevel tipLevel)
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
            .Select(f => Path.GetRelativePath(relativeBase, f))
            .Where(p => !PackageFileLister.IsPlumbing(
                p.Replace('\\', '/')))
            .OrderBy(p => p);

        var results = options.Limit.HasValue
            ? relativePaths.Take(options.Limit.Value).ToList()
            : relativePaths.ToList();
        var visibleResults = RowWindow.Apply(options.Rows, results);

        if (LensProjection.TryProject(options, "--layout", visibleResults.Count, out var projectionExitCode))
            return projectionExitCode;

        PackageOutputFormatter.WriteFileTree([.. visibleResults]);
        WriteFileLayoutTips(extractPath, options, packageName, tipLevel, isLayout: true);
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
        var visibleTfms = RowWindow.Apply(options.Rows, tfms);

        if (LensProjection.TryProject(
                options,
                "--tfms",
                visibleTfms.Count,
                out var projectionExit,
                ["TFM"]))
            return projectionExit;

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
            options.Rows is { IsUnlimited: false } window
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
            Rows = options.Rows,
            Tabular = options.Tabular,
            Tsv = options.Tsv,
            Jsonl = options.Jsonl,
            NoHeader = options.NoHeader,
            Columns = options.Columns,
            Fields = options.Fields,
        };
        return DependsCommand.WriteAssetProjection(
            projection,
            projectionOptions,
            new HashSet<string>(
                [DependsAssetSections.DependencyHierarchy],
                StringComparer.OrdinalIgnoreCase));
    }
}
