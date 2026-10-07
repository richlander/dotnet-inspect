using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;
using Markout.Formatting;
using QuerySpace.Composition;

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
        PackageChildrenCapabilityPlan capabilityPlan =
            PackageChildrenCapabilityPlanner.Plan(
                plan.Count,
                keepStart,
                keepEnd,
                options.Count
                    ? QuerySpaceTerminalRequirement.Count
                    : QuerySpaceTerminalRequirement.Rows);
        if (options.Count)
        {
            CountOutput.WriteCount(
                capabilityPlan.RequestedCount,
                options.OutputPath);
            return PackageIntegrityExitCode(result);
        }

        PackageChildrenProjection projection =
            await InspectPackageChildrenAsync(
                    plan,
                    capabilityPlan)
                .ConfigureAwait(false);
        if (!WritePackageChildren(
                projection,
                result,
                options))
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
            targetFramework: null,
            source: result.Source);
        bool toolSettingsAvailable =
            result.ToolSettingsProjectionStatus
                == DotnetToolSettingsProjectionStatus.Available
            || !string.IsNullOrWhiteSpace(result.ToolFormat);
        if (!result.ToolSettingsProjectionComplete
            || result.IsToolPackage && !toolSettingsAvailable)
        {
            return PackageChildrenPlan.FromDocument(
                PackageChildrenDocument.UnavailableLibraries(
                    subject,
                    PackageChildrenStatus.Unavailable,
                    result.ToolSettingsProjectionStatus
                        == DotnetToolSettingsProjectionStatus.Missing
                            ? "The declared tool Package contains no "
                                + "DotnetToolSettings.xml manifest."
                            : "Tool settings could not be projected "
                                + "completely."));
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

            IReadOnlySet<string>? entryPointFileNames =
                ToolSettings(extractPath)?
                    .CreateEntryPointFileNames();
            candidates =
            [
                .. toolSelection.SelectedEntries
                    .Select(assetPath =>
                    {
                        bool entryPoint =
                            entryPointFileNames?.Contains(
                                Path.GetFileName(assetPath)) == true;
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
                    targetFramework),
            subject.Source);

    private static ValueTask<PackageChildrenProjection>
        InspectPackageChildrenAsync(
            PackageChildrenPlan plan,
            PackageChildrenCapabilityPlan capabilityPlan)
    {
        if (plan.Document is { } document)
        {
            PackageChildrenDocument selected =
                SelectPackageChildren(
                    document,
                    capabilityPlan.SelectedStart,
                    capabilityPlan.SelectedEnd);
            return ValueTask.FromResult(
                new PackageChildrenProjection(
                    PackageChildrenEnvelope(selected),
                    capabilityPlan.SourceCount,
                    capabilityPlan.SelectedStart,
                    DuplicateLibraryNames(document.Libraries)));
        }

        PackageLibraryChildCandidate[] candidates =
        [
            .. plan.Candidates.Select(candidate =>
                new PackageLibraryChildCandidate(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.TargetFramework,
                    candidate.Role)),
        ];
        InspectionEnvelope<PackageChildrenDocument> inspection =
            PackageChildrenInspection.Execute(
                plan.Subject,
                candidates,
                capabilityPlan);
        return ValueTask.FromResult(
            new PackageChildrenProjection(
                inspection,
                capabilityPlan.SourceCount,
                capabilityPlan.SelectedStart,
                DuplicateLibraryNames(plan.Candidates)));
    }

    private static HashSet<string> DuplicateLibraryNames(
        IEnumerable<PackageLibraryChild> libraries) =>
    [
        .. libraries
            .GroupBy(
                static library => library.AssemblyName.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key),
    ];

    private static HashSet<string> DuplicateLibraryNames(
        IEnumerable<PackageChildCandidate> candidates) =>
    [
        .. candidates
            .GroupBy(
                static candidate => candidate.AssemblyName,
                StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key),
    ];

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
                    SubjectWithTargetFramework(
                        subject,
                        selection.TargetFramework),
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

    private static DotnetToolSettingsData? ToolSettings(
        string extractPath)
    {
        string tools = Path.Combine(extractPath, "tools");
        return Directory.Exists(tools)
            ? DotnetToolSettingsParser.FindAndParse(tools)
            : null;
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
        InspectionOptions options)
    {
        InspectionEnvelope<PackageChildrenDocument> inspection =
            projection.Inspection;
        PackageChildrenDocument content = inspection.Content;
        PackageChildOutputRow[] allRows =
            PackageChildRows(
                content,
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
                projection.DuplicateLibraryNames,
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
        IReadOnlySet<string> duplicateLibraryNames,
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
                    duplicateLibraryNames,
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
                    duplicateLibraryNames,
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
                    duplicateLibraryNames,
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
        IReadOnlySet<string> duplicateLibraryNames,
        InspectionOptions options,
        IMarkoutFormatter formatter)
    {
        if (formatter is not MermaidFormatter)
            output.WriteLine(PackageChildrenTitle(document));
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
                    && options.Rows is null,
                duplicateLibraryNames:
                    duplicateLibraryNames);
        if (formatter is MermaidFormatter)
        {
            writer.WriteTree(
            [
                new(PackageChildrenTitle(document))
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
                row.Status,
                row.Detail ?? "",
            }),
    ];

    private static PackageChildOutputRow[] PackageChildRows(
        PackageChildrenDocument document,
        int ordinalOffset) =>
        document.Kind switch
        {
            PackageChildrenKind.Libraries =>
            [
                .. document.Libraries.Select(
                    (library, index) => LibraryRow(
                        document,
                        library,
                        ordinalOffset + index + 1)),
            ],
            PackageChildrenKind.RuntimeIdentifierPackages =>
            [
                .. document.RuntimeIdentifierPackages.Select(
                    (package, index) => RuntimeIdentifierPackageRow(
                        document,
                        package,
                        ordinalOffset + index + 1)),
            ],
            PackageChildrenKind.NoManagedLibraries => [],
            _ => throw new InvalidOperationException(
                "Unknown Package children kind."),
        };

    private static PackageChildOutputRow LibraryRow(
        PackageChildrenDocument document,
        PackageLibraryChild library,
        int ordinal) =>
        new(
            ordinal,
            "Library",
            library.AssetId.ToString(),
            library.AssemblyName.ToString(),
            document.Subject.TargetFramework?.ToString(),
            library.Role.ToString(),
            library.AssetPath.ToString(),
            "available",
            null);

    private static PackageChildOutputRow RuntimeIdentifierPackageRow(
        PackageChildrenDocument document,
        PackageRuntimeIdentifierChild package,
        int ordinal)
    {
        string packageId = package.PackageId.ToString();
        return new(
            ordinal,
            "RID Package",
            packageId,
            packageId,
            package.RuntimeIdentifier.ToString(),
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
            document.Status.ToString(),
            document.Detail?.ToString());

    /// <summary>
    /// The Package Tree title: the subject identity followed by the
    /// owner-issued properties, rendered generically by
    /// <see cref="ResultTitle"/>.
    /// </summary>
    private static string PackageChildrenTitle(
        PackageChildrenDocument document) =>
        ResultTitle.Compose(
            $"{document.Subject.PackageId} {document.Subject.PackageVersion}",
            PackageChildrenProperties.For(document));

    private static List<TreeNode> PackageChildrenNodes(
        PackageChildrenDocument document,
        Verbosity verbosity,
        bool allowMinimalCollapse,
        IReadOnlySet<string> duplicateLibraryNames) =>
        document.Kind switch
        {
            PackageChildrenKind.Libraries =>
                LibraryNodes(
                    document,
                    verbosity,
                    allowMinimalCollapse,
                    duplicateLibraryNames),
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
        bool allowMinimalCollapse,
        IReadOnlySet<string> duplicateNames)
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
            && dependencies.Length > 8)
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
        int OrdinalOffset,
        IReadOnlySet<string> DuplicateLibraryNames);

}

internal sealed record PackageChildOutputRow(
    int? Ordinal,
    string Kind,
    string Identity,
    string Name,
    string? Target,
    string? Role,
    string? Asset,
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
