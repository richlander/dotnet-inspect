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

    private static async Task<int> ShowDependencyTreeAsync(
        HttpClient client,
        string packageReference,
        InspectionOptions options,
        VerboseLogger logger)
    {
        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                client,
                packageReference,
                options.Tfm,
                options.SourceOptions,
                logger,
                includePrerelease: options.IncludePrerelease,
                allowCompatibleFallbackForRequestedTfm: false);

        if (result is PackageDependencyGraphResult.Error error)
        {
            CommandError.Write(
                error.Message,
                error.Detail is null ? [] : [error.Detail]);
            return 1;
        }
        if (result is PackageDependencyGraphResult.Empty empty)
        {
            if (LensProjection.TryProject(
                    options,
                    "--dependencies",
                    rowCount: 0,
                    out var projectionExit,
                    ["Package", "Version", "Author"]))
            {
                return projectionExit;
            }
            var packageName =
                new InertString(
                    TextPolicy.Field,
                    empty.ManifestPackageName);
            var version =
                new InertString(
                    TextPolicy.Field,
                    empty.ManifestVersion);
            var description =
                new InertString(TextPolicy.Field, empty.Message);
            var emptyView = new EmptyDepsView
            {
                Title = InertString.Format(
                    TextPolicy.Field,
                    $"{packageName} ({version})").ToString(),
                Description = description.ToString()
            };
            OutputDestination.Write(
                options.OutputPath,
                options.Rows,
                writer => MarkoutSerializer.Serialize(
                    emptyView,
                    writer,
                    InspectionContext.Default));
            return 0;
        }

        var graph = (PackageDependencyGraphResult.Graph)result;
        var visibleCount = WindowedCount(
            TreeRowWindow.Count(graph.Dependencies, node => node.Children),
            options.Rows);
        if (LensProjection.TryProject(
                options,
                "--dependencies",
                visibleCount,
                out var countExit,
                ["Package", "Version", "Author"]))
        {
            return countExit;
        }

        var visibleNodes = TreeRowWindow.Apply(
            graph.Dependencies,
            options.Rows,
            node => node.Children,
            (node, children) => node with { Children = children });
        var packageText =
            new InertString(
                TextPolicy.Field,
                graph.ManifestPackageName);
        var versionText =
            new InertString(
                TextPolicy.Field,
                graph.ManifestVersion);
        var view = new PackageDependenciesView
        {
            Title = InertString.Format(
                TextPolicy.Field,
                $"{packageText} {versionText}").ToString(),
            Dependencies = ToTreeNodes(visibleNodes)
        };

        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            writer => MarkoutSerializer.Serialize(
                view,
                writer,
                PackageDependenciesContext.Default));
        return 0;
    }

    /// <summary>
    /// Builds the dependency tree's labels.
    /// </summary>
    /// <remarks>
    /// Every part of a label -- id, version, and author -- is nuspec text
    /// chosen by whoever built the package, and a tree label sits in a gutter
    /// where a line terminator forges a sibling node. Containment happens on
    /// the composed label, after the parts are joined, so the separators cannot
    /// be split apart either (issue #3319).
    /// </remarks>
    private static List<TreeNode> ToTreeNodes(List<DependencyNode> nodes)
    {
        return nodes.Select(n =>
        {
            var packageId = new InertString(TextPolicy.Field, n.PackageId);
            var version = new InertString(TextPolicy.Field, n.Version);
            var label = !string.IsNullOrEmpty(n.Author)
                ? InertString.Format(
                    TextPolicy.Field,
                    $"{packageId} {version} [{new InertString(TextPolicy.Field, n.Author)}]")
                : InertString.Format(TextPolicy.Field, $"{packageId} {version}");
            return n.Children.Count > 0
                ? new TreeNode(label.ToString()) { Children = ToTreeNodes(n.Children) }
                : new TreeNode(label.ToString());
        }).ToList();
    }
}
