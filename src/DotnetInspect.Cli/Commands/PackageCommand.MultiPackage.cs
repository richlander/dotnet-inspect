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

    private static async Task<int> ExecuteMultiPackageAsync(
        string[] packageArgs,
        InspectionOptions options,
        InspectionOptions producerOptions,
        CommandContext context,
        SectionCatalog<InspectionResult> sectionCatalog,
        PackageSourceQueryPlan sourceQueryPlan)
    {
        SectionPipeline<InspectionResult> pipeline = sectionCatalog.Pipeline;
        if (options.ShowContent)
            return await ExecuteMultiPackageContentAsync(packageArgs, options, context);

        string? rowSection = null;
        if (!options.Count
            && !TryResolveMultiPackageRowSection(options, out rowSection))
            return 1;
        var countSections = options.Count
            ? ResolveMultiPackageCountSections(options, pipeline)
            : null;
        bool wantsFilesSection = HasPathFilter(options)
            || IsPackageFileSection(rowSection)
            || options.IncludeSections?.Any(IsPackageFileSection) == true
            || (options.FixedOverview
                && sectionCatalog.BareSelectSectionNames.Any(IsPackageFileSection))
            || options.IncludeSections?.Contains(PackageSections.Signals) == true
            || options.IncludeSections?.Contains(PackageSections.AuditArtifactText) == true
            || options.IncludeSections?.Contains(PackageSections.AuditFindings) == true
            || options.FixedOverview
            || SelectResolver.IsActiveAllSelector(
                options.Select,
                options.IncludeSections)
            || countSections?.Any(IsPackageFileSection) == true;
        if (!options.Count && !options.JsonOutput && rowSection == null)
        {
            CommandError.Write("Multiple package output requires --format json or a row format such as --format table, --format tsv, or --format jsonl.");
            CommandError.WriteLine("For package surveys, try: dotnet-inspect package <pkg>... --path @readme --format tsv");
            return 1;
        }
        if (!ValidateMultiPackagePackageInfoColumns(
                options,
                countSections,
                rowSection))
        {
            return 1;
        }

        var targets = new List<PackageReferenceTarget>();
        foreach (var packageArg in packageArgs)
        {
            if (!TryCreatePackageTarget(packageArg, out var target))
                return 1;
            targets.Add(target);
        }

        var results = new List<InspectionResult>();
        foreach (var target in targets)
        {
            using var packageRequestScope = RequestTelemetry.Scope(
                target.Version.Length > 0 ? $"package {target.PackageName}@{target.Version}" : $"package {target.PackageName}",
                "package inspect");
            var result = await InspectPackageAsync(
                target,
                options,
                producerOptions,
                context,
                wantsFilesSection,
                sectionCatalog,
                sourceQueryPlan);
            if (result == null)
                return 1;
            results.Add(result);
        }

        if (options.Count)
            return WriteMultiPackageCount(results, options, pipeline);

        if (options.JsonOutput)
        {
            if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                return 1;

            OutputDestination.Write(
                options.OutputPath,
                options.Rows,
                output => output.WriteLine(
                    JsonSerializer.Serialize(
                        results.Select(static result =>
                            PackageInspectionJson.Create(result)).ToArray(),
                        PackageInspectionJsonContext.Default.PackageInspectionJsonArray)));
            return PackageIntegrityExitCode([.. results]);
        }

        try
        {
            OutputDestination.Write(
                options.OutputPath,
                options.Rows,
                output => WriteMultiPackageTable(
                    results,
                    rowSection!,
                    options,
                    output));
            return PackageIntegrityExitCode([.. results]);
        }
        catch (InvalidOperationException ex) when (
            IsUnmatchedColumnProjection(options, ex))
        {
            CommandError.Write(ex.Message);
            return 1;
        }
    }

    internal static int PackageIntegrityExitCode(params InspectionResult[] results)
        => PackageIntegrityExitCode(0, results);

    internal static int PackageIntegrityExitCode(
        int currentExitCode,
        params InspectionResult[] results)
    {
        foreach (DependsAssetProjection projection in results
                     .Select(static result =>
                         result.DependencyHierarchyProjection)
                     .OfType<DependsAssetProjection>())
        {
            DependsCommand.WriteAssetDiagnostics(projection);
            currentExitCode = Math.Max(
                currentExitCode,
                DependsCommand.AssetExitCode(projection));
        }

        var identifierFailures = results
            .Select(
                (result, index) =>
                    (
                        Input: index + 1,
                        Failure:
                            result.IdentifierConfusionFailure))
            .Where(
                failure =>
                    failure.Failure is not null)
            .Select(
                failure =>
                    (
                        failure.Input,
                        Failure: failure.Failure!.Value))
            .ToList();
        foreach (var (input, failure) in identifierFailures)
        {
            CommandError.WriteWarning(
                $"Identifier audit failed for package input #{input}: "
                + IdentifierConfusionAudit.DescribeFailure(failure));
        }

        if (currentExitCode != 0)
            return currentExitCode;

        return results.Any(
                static result =>
                    result.SourceIntegrity?.Mismatched is > 0)
            || identifierFailures.Count > 0
                ? 1
                : 0;
    }

    private static bool IsUnmatchedColumnProjection(
        InspectionOptions options,
        InvalidOperationException exception)
        => options.Columns is { Length: > 0 }
            && exception.Message.StartsWith(
                "No columns matched projection:",
                StringComparison.Ordinal);

    internal static int WriteMultiPackageCount(
        IReadOnlyList<InspectionResult> results,
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var projection = CaptureMultiPackageCountProjection(results, options, pipeline);
        var ordered = OutputFormatter.ResolveCountMapSections(
            pipeline, options.IncludeSections, options.FixedOverview);
        CountOutput.Write(
            projection, ordered, options.Format, options.NoHeader, options.OutputPath, options.Rows);
        return PackageIntegrityExitCode([.. results]);
    }

    private static CountProjection CaptureMultiPackageCountProjection(
        IReadOnlyList<InspectionResult> results,
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var selectedSections = ResolveMultiPackageCountSections(options, pipeline);
        var schema = PackageDiscoverySchema();

        var projection = new CountProjection();
        var documentSections = new HashSet<string>(
            selectedSections,
            StringComparer.OrdinalIgnoreCase);

        foreach (var section in selectedSections.Where(IsMultiPackageFieldSection))
        {
            documentSections.Remove(section);
            if (ProjectionExcludesSection(
                    schema, section, options, combinedRows: true))
            {
                projection.RecordRows(section, 0);
            }
            else
            {
                var rows = BuildMultiPackageFieldRows(
                    results,
                    section,
                    options.Fields);
                DiagnoseMissingPackageFieldSectionFields(
                    section,
                    options.Fields,
                    rows.Select(row => row[1]));
                projection.RecordRows(
                    section,
                    WindowedCount(rows.Length, options.Rows));
            }
        }

        foreach (var section in selectedSections.Where(IsPackageFileSection))
        {
            documentSections.Remove(section);
            int count = ProjectionExcludesSection(
                    schema, section, options, combinedRows: true)
                ? 0
                : BuildMultiPackageFileRows(
                    results, section, options.SkipEmpty).Count;
            projection.RecordRows(
                section,
                WindowedCount(count, options.Rows));
        }

        foreach (var section in documentSections.ToArray())
        {
            if (!ProjectionExcludesSection(schema, section, options))
                continue;

            documentSections.Remove(section);
            projection.RecordRows(section, 0);
        }

        if (documentSections.Count == 0)
            return projection;

        var documentOptions = options with
        {
            Select = null,
            SelectDefault = false,
            FixedOverview = false,
            IncludeSections = documentSections,
            Fields = HasMatchingProjection(
                schema, documentSections, "field", options.Fields)
                    ? options.Fields
                    : null,
            Columns = HasMatchingProjection(
                schema, documentSections, "column", options.Columns)
                    ? options.Columns
                    : null,
        };
        foreach (var result in results)
        {
            projection.Merge(OutputFormatter.CapturePackageCountProjection(
                result, documentOptions, pipeline));
        }

        return projection;
    }

    private static bool ProjectionExcludesSection(
        DocumentSchema schema,
        string section,
        InspectionOptions options,
        bool combinedRows = false)
    {
        var itemKind = schema.GetSection(section)?.ItemKind;
        if (itemKind?.Equals("field", StringComparison.OrdinalIgnoreCase) == true
            && options.Fields is { Length: > 0 }
            && !ProjectionMatches(schema, section, options.Fields))
        {
            return true;
        }

        return options.Columns is { Length: > 0 }
            && !ProjectionMatches(
                PackageCountColumnSchema(schema, combinedRows),
                section,
                options.Columns);
    }

    private static bool HasMatchingProjection(
        DocumentSchema schema,
        IEnumerable<string> sections,
        string itemKind,
        string[]? selectors)
        => selectors is { Length: > 0 }
            && sections.Any(section =>
                schema.GetSection(section)?.ItemKind.Equals(
                    itemKind,
                    StringComparison.OrdinalIgnoreCase) == true
                && ProjectionMatches(schema, section, selectors));

    private static bool ProjectionMatches(
        DocumentSchema schema,
        string section,
        string[] selectors)
        => schema.ValidateProjection(section, selectors).Resolved.Length > 0;

    private static HashSet<string> ResolveMultiPackageCountSections(
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
        => options.IncludeSections is { Count: > 0 } includeSections
            ? new HashSet<string>(includeSections, StringComparer.OrdinalIgnoreCase)
            : options.FixedOverview
                ? new HashSet<string>(
                    pipeline.BareSelectSectionNames,
                    StringComparer.OrdinalIgnoreCase)
                : throw new InvalidOperationException(
                    "Multi-package count requires at least one selected section.");

    private static bool TryResolveMultiPackageRowSection(InspectionOptions options, out string? section)
    {
        section = null;
        if (!options.Tabular)
            return true;

        if (SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections))
        {
            CommandError.Write("Multiple package row output requires one concrete section; @All produces a multi-section document.");
            return false;
        }

        if (options.IncludeSections is not { Count: > 0 })
        {
            section = PackageSections.PackageInfo;
            return true;
        }

        if (options.IncludeSections.Count != 1)
        {
            CommandError.Write($"Multiple package row output requires exactly one section; matched {options.IncludeSections.Count}: {string.Join(", ", options.IncludeSections)}.");
            return false;
        }

        section = options.IncludeSections.Single();
        if (IsMultiPackageFieldSection(section)
            || IsPackageFileSection(section))
        {
            return true;
        }

        CommandError.Write($"Multiple package row output does not support section: {section}.");
        CommandError.WriteLine("Use --format json, or select Package Info, Signature, Package files, or a package file section (see -D @Files).");
        return false;
    }

    private static bool ValidateMultiPackagePackageInfoColumns(
        InspectionOptions options,
        IReadOnlySet<string>? countSections,
        string? rowSection)
    {
        if (options.Columns is not { Length: > 0 }
            || (countSections is null && rowSection is null))
            return true;

        var schema = PackageDiscoverySchema();
        var columnSchema = PackageCountColumnSchema(
            schema,
            combinedRows: true);
        IReadOnlyCollection<string> selectedSections =
            countSections is { Count: > 0 }
                ? countSections
                : rowSection is not null
                    ? [rowSection]
                    : [];
        bool anyColumnMatches = options.Columns.Any(pattern =>
            selectedSections.Any(section =>
            {
                // "*" names the complete structural row. Narrower patterns that also
                // select package fields remain wrong-kind projections.
                if (IsMultiPackageFieldSection(section)
                    && !string.Equals(
                        pattern,
                        "*",
                        StringComparison.Ordinal)
                    && ResolveProjectionNames(
                        GetMultiPackageFieldNames(section),
                        [pattern]).Length > 0)
                {
                    return false;
                }

                return columnSchema.ValidateProjection(
                    section,
                    [pattern]).Resolved.Length > 0;
            }));
        if (anyColumnMatches)
            return true;

        CommandError.Write(
            $"No columns matched projection: {string.Join(", ", options.Columns)}");
        return false;
    }

    private static readonly string[] MultiPackageInfoColumnNames =
    [
        "Package",
        "Field",
        "Value",
    ];

    private static readonly string[] MultiPackageFileColumnNames =
    [
        "Package",
        "Version",
        "Path",
        "Size",
    ];
}
