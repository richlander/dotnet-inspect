using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    private const string PackageChildrenSection = "Package Children";

    private static readonly string[] PackageChildrenDisplayNames =
    [
        "Ordinal",
        "Kind",
        "Identity",
        "Name",
        "Target",
        "Role",
        "Asset",
        "Selector",
        "Type Declarations",
        "Count Status",
        "Status",
        "Detail",
    ];

    private static readonly string[] PackageChildrenStableNames =
    [
        "ordinal",
        "kind",
        "identity",
        "name",
        "target",
        "role",
        "asset",
        "selector",
        "type_declarations",
        "count_status",
        "status",
        "detail",
    ];

    private static bool IsPackageChildrenProjection(
        InspectionOptions options) =>
        options.PackageArgs.Length == 1
        && options.Discover is null
        && options.IncludeSections is not { Count: > 0 }
        && !options.SelectExplicitlySet
        && !options.SelectDefault
        && !options.FixedOverview
        && !options.ListVersions
        && !options.ListLayout
        && !options.ListTfms
        && !options.ShowContent
        && !options.Raw
        && !options.Print
        && !options.Value
        && !options.Urls
        && !options.Paths
        && !options.Roots
        && !options.Schema
        && options.PackageLibrary is null
        && !options.AllLibraries;

    private static bool ValidatePackageChildrenProjection(
        InspectionOptions options)
    {
        string[]? columns = PackageChildrenColumns(options);
        if (columns is not { Length: > 0 })
            return true;
        if (options.Format == OutputFormat.Mermaid)
        {
            CommandError.Write(
                "--fields/--columns are not available with Mermaid Package children output.");
            return false;
        }

        var schema = new DocumentSchema();
        schema.Add(
            PackageChildrenSection,
            "column",
            [
                .. PackageChildrenDisplayNames,
                .. PackageChildrenStableNames,
            ]);
        return ProjectionDiagnostics.ValidateProjection(
            schema,
            PackageChildrenSection,
            fields: null,
            columns);
    }

    private static string[]? PackageChildrenColumns(
        InspectionOptions options)
    {
        if (options.Columns is not { Length: > 0 })
            return options.Fields;
        if (options.Fields is not { Length: > 0 })
            return options.Columns;
        return
        [
            .. options.Fields,
            .. options.Columns,
        ];
    }

    private static async Task<int> WritePackageChildrenAsync(
        InspectionResult result,
        PackageExtractionResult resolution,
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        PackageChildrenPlan plan = PlanPackageChildren(
            result,
            resolution,
            extractPath,
            packageName,
            version,
            options);
        if (options.Count && !plan.IsComplete)
        {
            CommandError.Write(
                plan.Document?.Detail?.ToString()
                    ?? "The Package child Count is unavailable.");
            return 1;
        }
        (int keepStart, int keepEnd) =
            options.Rows?.Resolve(plan.Count)
            ?? (0, plan.Count);
        if (options.Count)
        {
            CountOutput.WriteCount(
                keepEnd - keepStart,
                options.OutputPath);
            return PackageIntegrityExitCode(result);
        }

        PackageChildrenProjection projection =
            await InspectPackageChildrenAsync(
                    plan,
                    resolution,
                    extractPath,
                    result,
                    packageName,
                    version,
                    keepStart,
                    keepEnd,
                    CancellationToken.None)
                .ConfigureAwait(false);
        PackageChildSelectorContext? selectors = null;
        if ((!projection.Inspection.Content.Libraries.IsEmpty
                || !projection.Inspection.Content
                    .RuntimeIdentifierPackages.IsEmpty)
            && !TryCreatePackageChildSelectorContext(
                projection.Inspection.Content,
                options,
                out selectors,
                out string? selectorError))
        {
            CommandError.Write(selectorError!);
            return 1;
        }
        if (!WritePackageChildren(
                projection,
                result,
                options,
                selectors))
        {
            return 1;
        }
        foreach (InspectionDiagnostic diagnostic
            in projection.Inspection.Diagnostics)
        {
            CommandError.WriteLine(
                $"{diagnostic.Code}: {diagnostic.Summary}");
        }
        return projection.Inspection.Content.IsComplete
            ? PackageIntegrityExitCode(result)
            : 1;
    }

    private static PackageChildrenPlan PlanPackageChildren(
            InspectionResult result,
            PackageExtractionResult resolution,
            string extractPath,
            string packageName,
            string version,
            InspectionOptions options)
    {
        ToolWrapperPackage? requestedToolWrapper =
            result.IsRidSpecificPointerPackage
                ? resolution.ToolWrapperChain.FirstOrDefault()
                : null;
        var subject = new PackageChildrenSubject(
            requestedToolWrapper?.PackageName
                ?? result.PackageName
                ?? packageName,
            requestedToolWrapper?.Version
                ?? result.Version
                ?? version,
            targetFramework: null);
        if (!result.ToolSettingsProjectionComplete)
        {
            return PackageChildrenPlan.FromDocument(
                PackageChildrenDocument.UnavailableLibraries(
                    subject,
                    PackageChildrenStatus.Unavailable,
                    "Tool settings could not be projected completely."));
        }

        if (result.IsRidSpecificPointerPackage
            && result.RuntimeIdentifierPackages is { Count: > 0 }
                ridPackages)
        {
            PackageChildrenDocument pointer =
                PackageChildrenDocument.FromRuntimeIdentifierPackages(
                    subject,
                    ridPackages.Select(
                        static package =>
                            new PackageRuntimeIdentifierChild(
                                package.RuntimeIdentifier,
                                package.PackageId)));
            return PackageChildrenPlan.FromDocument(pointer);
        }

        List<PackageChildCandidate> candidates;
        string? targetFramework;
        if (result.IsToolPackage)
        {
            PackageToolSliceSelection toolSelection =
                PackageToolSliceMeasurementProjection.SelectEntries(
                    Directory.EnumerateFiles(
                            extractPath,
                            "*",
                            SearchOption.AllDirectories)
                        .Select(path =>
                            PackageAssetPath(extractPath, path)),
                    options.Tfm);
            if (toolSelection.Status
                != PackageToolSliceSelectionStatus.Selected)
            {
                PackageChildrenDocument document =
                    toolSelection.Status switch
                    {
                        PackageToolSliceSelectionStatus.NoApplicableSlice =>
                            PackageChildrenDocument.UnavailableLibraries(
                                subject,
                                PackageChildrenStatus.NoApplicableTarget,
                                $"No tool Library slice matches target "
                                    + $"'{options.Tfm}'."),
                        PackageToolSliceSelectionStatus.InvalidSelection =>
                            PackageChildrenDocument.UnavailableLibraries(
                                subject,
                                PackageChildrenStatus.InvalidSelection,
                                toolSelection.Detail!),
                        _ => PackageChildrenDocument.NoManagedLibraries(
                            subject,
                            "The selected tool payload contains no managed "
                                + "Libraries."),
                    };
                return PackageChildrenPlan.FromDocument(document);
            }

            targetFramework =
                toolSelection.SelectedTargetFramework!;
            subject = SubjectWithTargetFramework(
                subject,
                targetFramework);
            if (toolSelection.SelectedEntries.IsEmpty)
            {
                return PackageChildrenPlan.FromDocument(
                    PackageChildrenDocument.NoManagedLibraries(
                        subject,
                        "The selected tool payload contains no managed "
                            + "Libraries."));
            }

            HashSet<string> entryPoints =
                ToolEntryPoints(extractPath);
            candidates =
            [
                .. toolSelection.SelectedEntries
                    .Select(assetPath =>
                    {
                        bool entryPoint = entryPoints.Contains(
                            Path.GetFileName(assetPath));
                        return new PackageChildCandidate(
                            assetPath,
                            assetPath,
                            Path.GetFileNameWithoutExtension(assetPath),
                            targetFramework,
                            entryPoint
                                ? PackageLibraryChildRole.ToolEntryPoint
                                : PackageLibraryChildRole.ToolLibrary);
                    })
                    .OrderBy(static candidate =>
                        candidate.Role
                            == PackageLibraryChildRole.ToolEntryPoint
                                ? 0
                                : 1)
                    .ThenBy(
                        static candidate => candidate.AssetPath,
                        StringComparer.Ordinal),
            ];
        }
        else
        {
            PackageCompileAssetSelection selection =
                ResolvePackageCompileSelection(
                    extractPath,
                    packageName,
                    resolution,
                    options.Tfm);
            if (!selection.IsSelected)
            {
                return PackageChildrenPlan.FromDocument(
                    CompileSelectionDocument(subject, selection));
            }

            targetFramework = selection.TargetFramework;
            candidates =
            [
                .. selection.Assets.Select(
                    asset => new PackageChildCandidate(
                        asset.Id,
                        asset.Path,
                        asset.AssemblyName,
                        targetFramework,
                        PackageLibraryChildRole.Compile)),
            ];
        }

        subject = SubjectWithTargetFramework(subject, targetFramework);
        return new(subject, [.. candidates]);
    }

    private static PackageChildrenSubject SubjectWithTargetFramework(
        PackageChildrenSubject subject,
        string? targetFramework) =>
        new(
            subject.PackageId,
            subject.PackageVersion,
            targetFramework is null
                ? null
                : new InertText.InertString(
                    InertText.TextPolicy.Field,
                    targetFramework));

    private static async ValueTask<PackageChildrenProjection>
        InspectPackageChildrenAsync(
            PackageChildrenPlan plan,
            PackageExtractionResult resolution,
            string extractPath,
            InspectionResult result,
            string packageName,
            string version,
            int keepStart,
            int keepEnd,
            CancellationToken cancellationToken)
    {
        if (plan.Document is { } document)
        {
            PackageChildrenDocument selected =
                SelectPackageChildren(document, keepStart, keepEnd);
            return new(
                PackageChildrenEnvelope(selected),
                plan.Count,
                keepStart);
        }

        PackageChildCandidate[] candidates =
        [
            .. plan.Candidates
                .Skip(keepStart)
                .Take(keepEnd - keepStart),
        ];
        if (candidates.Length == 0)
        {
            var selected = new PackageChildrenDocument(
                plan.Subject,
                PackageChildrenKind.Libraries,
                plan.Count == 0
                    ? PackageChildrenStatus.SelectedEmpty
                    : PackageChildrenStatus.Available,
                [],
                [],
                detail: null,
                isComplete: true);
            return new(
                PackageChildrenEnvelope(selected),
                plan.Count,
                keepStart);
        }

        PackageInspectionInput input =
            resolution.AcquiredPayload is { } acquired
                ? PackageInspectionInput.CreateFromPayload(acquired)
                : PackageInspectionInput.CreateLocal(
                    new FileSystemPackageContent(
                        extractPath,
                        resolution.NupkgPath,
                        resolution.FromCache,
                        resolution.ProducerKey ?? packageName),
                    result.PackageName ?? packageName,
                    result.Version ?? version);
        PackageLibraryInspectionCandidate[] inspectionCandidates =
        [
            .. candidates.Select(candidate =>
                new PackageLibraryInspectionCandidate(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.TargetFramework,
                    candidate.Role)),
        ];
        InspectionEnvelope<PackageChildrenDocument> inspection =
            await PackageChildrenInspection.ExecutePackageEntriesAsync(
                    plan.Subject,
                    input,
                    inspectionCandidates,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        return new(inspection, plan.Count, keepStart);
    }

    private static PackageChildrenDocument SelectPackageChildren(
        PackageChildrenDocument document,
        int keepStart,
        int keepEnd) =>
        new(
            document.Subject,
            document.Kind,
            document.Status,
            document.Kind == PackageChildrenKind.Libraries
                ?
                [
                    .. document.Libraries
                        .Skip(keepStart)
                        .Take(keepEnd - keepStart),
                ]
                : [],
            document.Kind
                == PackageChildrenKind.RuntimeIdentifierPackages
                ?
                [
                    .. document.RuntimeIdentifierPackages
                        .Skip(keepStart)
                        .Take(keepEnd - keepStart),
                ]
                : [],
            document.Detail,
            document.IsComplete);

    private static PackageChildrenDocument CompileSelectionDocument(
        PackageChildrenSubject subject,
        PackageCompileAssetSelection selection) =>
        selection.Status switch
        {
            PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                new(
                    new(
                        subject.PackageId,
                        subject.PackageVersion,
                        selection.TargetFramework is null
                            ? null
                            : new InertText.InertString(
                                InertText.TextPolicy.Field,
                                selection.TargetFramework)),
                    PackageChildrenKind.Libraries,
                    PackageChildrenStatus.SelectedEmpty,
                    [],
                    [],
                    detail: null,
                    isComplete: true),
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                new(
                    subject,
                    PackageChildrenKind.Libraries,
                    PackageChildrenStatus.NoCompileAssets,
                    [],
                    [],
                    new InertText.InertString(
                        InertText.TextPolicy.Field,
                        "The Package contains no compile Libraries."),
                    isComplete: true),
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                PackageChildrenDocument.UnavailableLibraries(
                    subject,
                    PackageChildrenStatus.NoApplicableTarget,
                    selection.Message
                        ?? "No compile Library target matches the request."),
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                PackageChildrenDocument.UnavailableLibraries(
                    subject,
                    PackageChildrenStatus.InvalidSelection,
                    selection.Message
                        ?? "The selected compile Libraries have invalid "
                            + "implementation correspondence."),
            _ => PackageChildrenDocument.UnavailableLibraries(
                subject,
                PackageChildrenStatus.Unavailable,
                selection.Message
                    ?? "The compile Library population is unavailable."),
        };

    private static HashSet<string> ToolEntryPoints(
        string extractPath)
    {
        string tools = Path.Combine(extractPath, "tools");
        DotnetToolSettingsData? settings =
            Directory.Exists(tools)
                ? DotnetToolSettingsParser.FindAndParse(tools)
                : null;
        return new(
            settings?.CommandEntries?
                .Select(static command => command.EntryPoint)
                .OfType<string>()
                .Select(Path.GetFileName)
                .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                .Select(static entry => entry!)
                ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    private static string PackageAssetPath(
        string extractPath,
        string path) =>
        Path.GetRelativePath(
                extractPath,
                Path.GetFullPath(path))
            .Replace('\\', '/');

    private static InspectionEnvelope<PackageChildrenDocument>
        PackageChildrenEnvelope(
            PackageChildrenDocument document) =>
        new(
            document,
            new InspectionShare.NonProjectable(
                "package-children/share",
                "Package children do not yet have a canonical Workspace "
                    + "Share projection."));

    private static bool WritePackageChildren(
        PackageChildrenProjection projection,
        InspectionResult package,
        InspectionOptions options,
        PackageChildSelectorContext? selectors)
    {
        InspectionEnvelope<PackageChildrenDocument> inspection =
            projection.Inspection;
        PackageChildrenDocument content = inspection.Content;
        PackageChildOutputRow[] allRows =
            PackageChildRows(
                content,
                selectors,
                projection.OrdinalOffset);

        var outputDocument = new PackageChildrenOutputDocument(
            content.Subject.PackageId.ToString(),
            content.Subject.PackageVersion.ToString(),
            content.Subject.TargetFramework?.ToString(),
            content.Kind,
            content.Status,
            content.IsComplete,
            content.Detail?.ToString(),
            projection.TotalCount,
            allRows.Length,
            allRows);
        if (options.EnvelopeOutput)
        {
            var envelope =
                new InspectionEnvelope<PackageChildrenOutputDocument>(
                    outputDocument,
                    inspection.Share,
                    inspection.Diagnostics);
            return InspectionEnvelopeOutput.TryWrite(
                envelope,
                new InspectionEnvelopeJsonContract<
                    PackageChildrenOutputDocument>(
                        "package-children",
                        1,
                        PackageChildrenJsonContext.Default
                            .PackageChildrenOutputDocument),
                includeEnvelope: true,
                compactJson: options.CompactJson,
                outputPath: options.OutputPath);
        }

        OutputDestination.Write(
            options.OutputPath,
            null,
            output => WritePackageChildren(
                output,
                package,
                content,
                outputDocument,
                allRows,
                projection.TotalCount,
                options));
        return true;
    }

    private static void WritePackageChildren(
        TextWriter output,
        InspectionResult package,
        PackageChildrenDocument content,
        PackageChildrenOutputDocument outputDocument,
        IReadOnlyList<PackageChildOutputRow> selectedRows,
        int totalCount,
        InspectionOptions options)
    {
        switch (options.Format)
        {
            case OutputFormat.Json:
                if (PackageChildrenColumns(options)
                    is { Length: > 0 })
                {
                    WritePackageChildrenProjectedJson(
                        output,
                        content,
                        selectedRows,
                        totalCount,
                        options);
                }
                else
                {
                    output.WriteLine(
                        JsonSerializer.Serialize(
                            outputDocument,
                            options.CompactJson
                                ? PackageChildrenCompactJsonContext.Default
                                    .PackageChildrenOutputDocument
                                : PackageChildrenJsonContext.Default
                                    .PackageChildrenOutputDocument));
                }
                return;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
            case OutputFormat.Jsonl:
                WritePackageChildrenTable(
                    output,
                    content,
                    selectedRows,
                    totalCount,
                    options);
                return;
            case OutputFormat.Mermaid:
                WritePackageChildrenTree(
                    output,
                    package,
                    content,
                    selectedRows,
                    totalCount,
                    options,
                    new MermaidFormatter());
                return;
            case OutputFormat.PlainText:
                if (PackageChildrenColumns(options)
                    is { Length: > 0 })
                {
                    WritePackageChildrenTable(
                        output,
                        content,
                        selectedRows,
                        totalCount,
                        options);
                    return;
                }
                WritePackageChildrenTree(
                    output,
                    package,
                    content,
                    selectedRows,
                    totalCount,
                    options,
                    new PlainTextFormatter());
                return;
            default:
                if (PackageChildrenColumns(options)
                    is { Length: > 0 })
                {
                    WritePackageChildrenTable(
                        output,
                        content,
                        selectedRows,
                        totalCount,
                        options);
                    return;
                }
                WritePackageChildrenTree(
                    output,
                    package,
                    content,
                    selectedRows,
                    totalCount,
                    options,
                    new MarkdownFormatter());
                return;
        }
    }

    private static void WritePackageChildrenTree(
        TextWriter output,
        InspectionResult package,
        PackageChildrenDocument document,
        IReadOnlyList<PackageChildOutputRow> selectedRows,
        int totalCount,
        InspectionOptions options,
        IMarkoutFormatter formatter)
    {
        if (formatter is not MermaidFormatter)
        {
            output.WriteLine(DescribePackageChildrenSubject(
                package,
                document));
        }
        var writer = new MarkoutWriter(output, formatter);
        bool windowedEmpty =
            totalCount > 0
            && selectedRows.Count == 0;
        List<TreeNode> nodes = windowedEmpty
            ? []
            : PackageChildrenNodes(
                document,
                options.Verbosity,
                allowMinimalCollapse:
                    !options.FormatFlagExplicitlySet
                    && options.Rows is null);
        if (formatter is MermaidFormatter)
        {
            writer.WriteTree(
            [
                new(DescribePackageChildrenSubject(package, document))
                {
                    Children = [.. nodes],
                },
            ]);
        }
        else if (windowedEmpty)
        {
            writer.WriteTree([]);
        }
        else
        {
            writer.WriteTree([.. nodes]);
        }
        writer.Flush();
    }

    private static void WritePackageChildrenTable(
        TextWriter output,
        PackageChildrenDocument document,
        IReadOnlyList<PackageChildOutputRow> selectedRows,
        int totalCount,
        InspectionOptions options)
    {
        PackageChildOutputRow[] presentationRows =
            selectedRows.Count > 0
                ? [.. selectedRows]
                : totalCount == 0
                    ?
                    [
                        StatusRow(document),
                    ]
                    : [];
        OutputFormatter.WriteProjectedTable(
            output,
            showHeader: !options.NoHeader,
            tsv: options.Format == OutputFormat.Tsv,
            jsonl: options.Format == OutputFormat.Jsonl,
            PackageChildrenColumns(options),
            fields: null,
            (writer, formatter, writerOptions) =>
            {
                writerOptions.JsonTypedValues = true;
                var markout = new MarkoutWriter(
                    writer,
                    formatter,
                    writerOptions);
                markout.WriteTable(
                    PackageChildrenDisplayNames,
                    PackageChildrenStableNames,
                    PackageChildrenCells(presentationRows));
                markout.Flush();
            });
    }

    private static void WritePackageChildrenProjectedJson(
        TextWriter output,
        PackageChildrenDocument document,
        IReadOnlyList<PackageChildOutputRow> selectedRows,
        int totalCount,
        InspectionOptions options)
    {
        PackageChildOutputRow[] presentationRows =
            selectedRows.Count > 0
                ? [.. selectedRows]
                : totalCount == 0
                    ?
                    [
                        StatusRow(document),
                    ]
                    : [];
        var rendered = new StringWriter
        {
            NewLine = "\n",
        };
        OutputFormatter.WriteProjectedTable(
            rendered,
            showHeader: false,
            tsv: false,
            jsonl: true,
            PackageChildrenColumns(options),
            fields: null,
            (writer, formatter, writerOptions) =>
            {
                writerOptions.JsonTypedValues = true;
                var markout = new MarkoutWriter(
                    writer,
                    formatter,
                    writerOptions);
                markout.WriteTable(
                    PackageChildrenDisplayNames,
                    PackageChildrenStableNames,
                    PackageChildrenCells(presentationRows));
                markout.Flush();
            });
        string[] lines = rendered.ToString().Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        if (options.CompactJson)
        {
            output.Write('[');
            output.Write(string.Join(',', lines));
            output.WriteLine(']');
            return;
        }

        output.WriteLine('[');
        for (int index = 0; index < lines.Length; index++)
        {
            output.Write("  ");
            output.Write(lines[index]);
            output.WriteLine(index + 1 < lines.Length ? "," : "");
        }
        output.WriteLine(']');
    }

    private static string[][] PackageChildrenCells(
        IEnumerable<PackageChildOutputRow> rows) =>
    [
        .. rows.Select(
            static row => new[]
            {
                row.Ordinal?.ToString(
                    CultureInfo.InvariantCulture)
                    ?? "",
                row.Kind,
                row.Identity,
                row.Name,
                row.Target ?? "",
                row.Role ?? "",
                row.Asset ?? "",
                row.Selector ?? "",
                row.TypeDeclarations?.ToString(
                    CultureInfo.InvariantCulture)
                    ?? "",
                row.CountStatus ?? "",
                row.Status,
                row.Detail ?? "",
            }),
    ];

    private static PackageChildOutputRow[] PackageChildRows(
        PackageChildrenDocument document,
        PackageChildSelectorContext? selectors,
        int ordinalOffset) =>
        document.Kind switch
        {
            PackageChildrenKind.Libraries =>
            [
                .. document.Libraries.Select(
                    (library, index) => LibraryRow(
                        document,
                        library,
                        selectors!.LibraryCommand,
                        ordinalOffset + index + 1)),
            ],
            PackageChildrenKind.RuntimeIdentifierPackages =>
            [
                .. document.RuntimeIdentifierPackages.Select(
                    (package, index) => RuntimeIdentifierPackageRow(
                        document,
                        package,
                        selectors!.RuntimeIdentifierSourceArguments,
                        ordinalOffset + index + 1)),
            ],
            PackageChildrenKind.NoManagedLibraries => [],
            _ => throw new InvalidOperationException(
                "Unknown Package children kind."),
        };

    private static PackageChildOutputRow LibraryRow(
        PackageChildrenDocument document,
        PackageLibraryChild library,
        string libraryCommand,
        int ordinal)
    {
        (int? count, string countStatus, string? countDetail) =
            library.PublicTypeDeclarations switch
            {
                LibraryTypePopulationCountOutcome.Counted counted =>
                    (counted.Total, "counted", (string?)null),
                LibraryTypePopulationCountOutcome.Incomplete incomplete =>
                    ((int?)null, "incomplete",
                        $"{incomplete.Bound}: "
                            + $"{incomplete.Measured} > "
                            + $"{incomplete.Limit}"),
                LibraryTypePopulationCountOutcome.Unavailable unavailable =>
                    ((int?)null, "unavailable",
                        unavailable.Reason.ToString()),
                _ => ((int?)null, "unavailable", (string?)null),
            };
        string assetPath = library.AssetPath.ToString();
        string selector =
            libraryCommand
                + $" --library {ShellCommandText.Quote(assetPath)}";
        return new(
            ordinal,
            "Library",
            library.AssetId.ToString(),
            library.AssemblyName.ToString(),
            document.Subject.TargetFramework?.ToString(),
            library.Role.ToString(),
            assetPath,
            selector,
            count,
            countStatus,
            library.IsAvailable ? "available" : "unavailable",
            library.Unavailable?.Detail.ToString() ?? countDetail);
    }

    private static PackageChildOutputRow RuntimeIdentifierPackageRow(
        PackageChildrenDocument document,
        PackageRuntimeIdentifierChild package,
        string sourceArguments,
        int ordinal)
    {
        string packageId = package.PackageId.ToString();
        string version = document.Subject.PackageVersion.ToString();
        return new(
            ordinal,
            "RID Package",
            packageId,
            packageId,
            package.RuntimeIdentifier.ToString(),
            null,
            null,
            "package "
                + ShellCommandText.Quote($"{packageId}@{version}")
                + sourceArguments,
            null,
            null,
            "available",
            null);
    }

    private static PackageChildOutputRow StatusRow(
        PackageChildrenDocument document) =>
        new(
            null,
            "Status",
            $"{document.Subject.PackageId}"
                + $"@{document.Subject.PackageVersion}",
            document.Subject.PackageId.ToString(),
            document.Subject.TargetFramework?.ToString(),
            null,
            null,
            null,
            null,
            null,
            document.Status.ToString(),
            document.Detail?.ToString());

    private static bool TryCreatePackageChildSelectorContext(
        PackageChildrenDocument document,
        InspectionOptions options,
        out PackageChildSelectorContext? selectors,
        out string? error)
    {
        selectors = null;
        error = null;
        string requested = options.PackageArgs.Single();
        bool local =
            File.Exists(requested)
            || Directory.Exists(requested);
        string packageSelector = local
            ? Path.GetFullPath(requested)
            : $"{document.Subject.PackageId}"
                + $"@{document.Subject.PackageVersion}";
        NuGetSourceOptions? replaySourceOptions =
            options.SourceOptions;
        if (local)
        {
            string fullPath = Path.GetFullPath(requested);
            string localSource = File.Exists(fullPath)
                ? Path.GetDirectoryName(fullPath)!
                : fullPath;
            string[] configuredSources =
                options.SourceOptions?.Sources ?? [];
            replaySourceOptions = new NuGetSourceOptions
            {
                Sources = [localSource, .. configuredSources],
                AdditionalSources =
                    options.SourceOptions?.AdditionalSources ?? [],
                ConfigFile = options.SourceOptions?.ConfigFile,
                ConfigDirectory =
                    options.SourceOptions?.ConfigDirectory,
            };
        }
        if (!PackageReplaySourceArguments.TryCreate(
                replaySourceOptions,
                "package",
                out PackageReplaySources? replaySources,
                out error))
        {
            return false;
        }

        string sourceArguments =
            PackageReplaySourceArguments.Format(replaySources);
        if (sourceArguments.Length > 0)
            sourceArguments = " " + sourceArguments;
        string libraryCommand =
            "package "
                + ShellCommandText.Quote(packageSelector);
        if (document.Subject.TargetFramework is { } framework)
        {
            libraryCommand +=
                " --tfm "
                + ShellCommandText.Quote(framework.ToString());
        }
        if (!local)
            libraryCommand += sourceArguments;
        selectors = new(libraryCommand, sourceArguments);
        return true;
    }

    private static string DescribePackageChildrenSubject(
        InspectionResult package,
        PackageChildrenDocument document)
    {
        var context = new List<string>();
        if (package.Source is { } source)
            context.Add(source.ToString());
        if (!string.IsNullOrWhiteSpace(package.ToolFormat))
            context.Add(ShortToolFormat(package.ToolFormat));
        if (document.Subject.TargetFramework is { } framework)
            context.Add(framework.ToString());
        if (package.ContentDirectories is { Count: > 0 } directories)
        {
            context.Add(string.Join(
                ", ",
                directories.Order(StringComparer.Ordinal)));
        }
        if (package.ToolCommands is { Count: > 0 } commands)
        {
            context.Add(
                "command: "
                    + string.Join(
                        ", ",
                        commands.Order(StringComparer.Ordinal)));
        }

        string identity =
            $"{document.Subject.PackageId} "
                + $"{document.Subject.PackageVersion}";
        return context.Count == 0
            ? identity
            : $"{identity} ({string.Join("; ", context)})";
    }

    private static string ShortToolFormat(string format) =>
        format.Contains(
            "Version=\"2\"",
            StringComparison.Ordinal)
            ? "DotNetCliTool v2"
            : format.Contains(
                "Version=\"1\"",
                StringComparison.Ordinal)
                ? "DotNetCliTool v1"
                : format;

    private static List<TreeNode> PackageChildrenNodes(
        PackageChildrenDocument document,
        Verbosity verbosity,
        bool allowMinimalCollapse) =>
        document.Kind switch
        {
            PackageChildrenKind.Libraries =>
                LibraryNodes(
                    document,
                    verbosity,
                    allowMinimalCollapse),
            PackageChildrenKind.RuntimeIdentifierPackages =>
            [
                new(
                    $"RID packages "
                        + $"({document.RuntimeIdentifierPackages.Length})")
                {
                    Children =
                    [
                        .. document.RuntimeIdentifierPackages.Select(
                            static package =>
                                new TreeNode(
                                    $"{package.RuntimeIdentifier}: "
                                        + $"{package.PackageId}")),
                    ],
                },
            ],
            PackageChildrenKind.NoManagedLibraries =>
            [
                new(
                    "No managed Libraries"
                        + (document.Detail is { } detail
                            ? $" ({detail})"
                            : "")),
            ],
            _ => throw new InvalidOperationException(
                "Unknown Package children kind."),
        };

    private static List<TreeNode> LibraryNodes(
        PackageChildrenDocument document,
        Verbosity verbosity,
        bool allowMinimalCollapse)
    {
        if (document.Libraries.IsEmpty)
        {
            return
            [
                new(
                    document.Status switch
                    {
                        PackageChildrenStatus.SelectedEmpty =>
                            "No Libraries in the selected compile group",
                        PackageChildrenStatus.NoCompileAssets =>
                            "No compile Libraries",
                        PackageChildrenStatus.NoApplicableTarget =>
                            "No applicable Library target",
                        PackageChildrenStatus.InvalidSelection =>
                            "Invalid Library selection",
                        PackageChildrenStatus.Unavailable =>
                            "Library population unavailable",
                        _ => "No Libraries",
                    }
                    + (document.Detail is { } detail
                        ? $" ({detail})"
                        : "")),
            ];
        }

        HashSet<string> duplicateNames =
        [
            .. document.Libraries
                .GroupBy(
                    static library =>
                        library.AssemblyName.ToString(),
                    StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key),
        ];
        PackageLibraryChild[] entryPoints =
        [
            .. document.Libraries.Where(
                static library =>
                    library.Role
                        == PackageLibraryChildRole.ToolEntryPoint),
        ];
        PackageLibraryChild[] dependencies =
        [
            .. document.Libraries.Where(
                static library =>
                    library.Role
                        != PackageLibraryChildRole.ToolEntryPoint),
        ];
        var nodes = new List<TreeNode>(document.Libraries.Length);
        nodes.AddRange(
            entryPoints.Select(
                library => LibraryNode(
                    library,
                    duplicateNames)));
        if (allowMinimalCollapse
            && verbosity == Verbosity.Minimal
            && entryPoints.Length > 0
            && dependencies.Length > 8
            && dependencies.All(static library => library.IsAvailable))
        {
            nodes.Add(
                new(
                    $"Dependencies ({dependencies.Length} Libraries; "
                        + "use -v:n for full inventory)"));
        }
        else
        {
            nodes.AddRange(
                dependencies.Select(
                    library => LibraryNode(
                        library,
                        duplicateNames)));
        }
        return nodes;
    }

    private static TreeNode LibraryNode(
        PackageLibraryChild library,
        IReadOnlySet<string> duplicateNames)
    {
        string assemblyName = library.AssemblyName.ToString();
        string label = duplicateNames.Contains(assemblyName)
            ? library.AssetPath.ToString()
            : assemblyName;
        if (library.Role
            == PackageLibraryChildRole.ToolEntryPoint)
        {
            label += " (entry point)";
        }
        label += library.PublicTypeDeclarations switch
        {
            LibraryTypePopulationCountOutcome.Counted counted =>
                $" ({counted.Total} Type declarations)",
            LibraryTypePopulationCountOutcome.Incomplete incomplete =>
                $" (incomplete: {incomplete.Bound})",
            LibraryTypePopulationCountOutcome.Unavailable unavailable =>
                $" (Count unavailable: {unavailable.Reason})",
            null when library.Unavailable is { } unavailable =>
                $" (unavailable: {unavailable.Detail})",
            _ => " (Count unavailable)",
        };
        return new(label);
    }

    private sealed record PackageChildCandidate(
        string AssetId,
        string AssetPath,
        string AssemblyName,
        string? TargetFramework,
        PackageLibraryChildRole Role);

    private sealed record PackageChildrenPlan(
        PackageChildrenSubject Subject,
        ImmutableArray<PackageChildCandidate> Candidates,
        PackageChildrenDocument? Document = null)
    {
        internal PackageChildrenPlan(
            PackageChildrenSubject subject,
            ImmutableArray<PackageChildCandidate> candidates)
            : this(subject, candidates, Document: null)
        {
        }

        internal int Count =>
            Document?.Kind switch
            {
                PackageChildrenKind.Libraries =>
                    Document.Libraries.Length,
                PackageChildrenKind.RuntimeIdentifierPackages =>
                    Document.RuntimeIdentifierPackages.Length,
                _ => Candidates.Length,
            };

        internal bool IsComplete =>
            Document?.IsComplete ?? true;

        internal static PackageChildrenPlan FromDocument(
            PackageChildrenDocument document) =>
            new(document.Subject, [], document);
    }

    private sealed record PackageChildrenProjection(
        InspectionEnvelope<PackageChildrenDocument> Inspection,
        int TotalCount,
        int OrdinalOffset);

    private sealed record PackageChildSelectorContext(
        string LibraryCommand,
        string RuntimeIdentifierSourceArguments);
}

internal sealed record PackageChildOutputRow(
    int? Ordinal,
    string Kind,
    string Identity,
    string Name,
    string? Target,
    string? Role,
    string? Asset,
    string? Selector,
    int? TypeDeclarations,
    string? CountStatus,
    string Status,
    string? Detail);

internal sealed record PackageChildrenOutputDocument(
    string PackageId,
    string PackageVersion,
    string? TargetFramework,
    PackageChildrenKind Kind,
    PackageChildrenStatus Status,
    bool IsComplete,
    string? Detail,
    int TotalCount,
    int SelectedCount,
    PackageChildOutputRow[] Children);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PackageChildrenOutputDocument))]
internal partial class PackageChildrenJsonContext :
    JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PackageChildrenOutputDocument))]
internal partial class PackageChildrenCompactJsonContext :
    JsonSerializerContext;
