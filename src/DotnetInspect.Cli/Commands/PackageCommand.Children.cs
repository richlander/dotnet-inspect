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
        InspectionEnvelope<PackageChildrenDocument> inspection =
            await InspectPackageChildrenAsync(
                    result,
                    resolution,
                    extractPath,
                    packageName,
                    version,
                    options,
                    CancellationToken.None)
                .ConfigureAwait(false);
        if (!WritePackageChildren(
                inspection,
                result,
                options))
        {
            return 1;
        }
        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            CommandError.WriteLine(
                $"{diagnostic.Code}: {diagnostic.Summary}");
        }
        return inspection.Content.IsComplete
            ? PackageIntegrityExitCode(result)
            : 1;
    }

    private static async ValueTask<
        InspectionEnvelope<PackageChildrenDocument>>
        InspectPackageChildrenAsync(
            InspectionResult result,
            PackageExtractionResult resolution,
            string extractPath,
            string packageName,
            string version,
            InspectionOptions options,
            CancellationToken cancellationToken)
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
            return PackageChildrenEnvelope(pointer);
        }

        List<PackageChildCandidate> candidates;
        string? targetFramework;
        if (result.IsToolPackage)
        {
            TfmSelector.PackageLibraryResolution toolSelection =
                TfmSelector.SelectPackageLibraries(
                    extractPath,
                    options.Tfm);
            if (!toolSelection.IsSelected)
            {
                return toolSelection.Status
                    == TfmSelector.PackageLibraryResolutionStatus
                        .NoMatchingTargetFramework
                    ? PackageChildrenEnvelope(
                        PackageChildrenDocument.UnavailableLibraries(
                            subject,
                            PackageChildrenStatus.NoApplicableTarget,
                            $"No tool Library slice matches target "
                                + $"'{options.Tfm}'."))
                    : PackageChildrenEnvelope(
                        PackageChildrenDocument.NoManagedLibraries(
                            subject,
                            "The selected tool payload contains no managed "
                                + "Libraries."));
            }

            targetFramework = toolSelection.Tfm;
            HashSet<string> entryPoints =
                ToolEntryPoints(extractPath);
            candidates =
            [
                .. toolSelection.Paths
                    .Select(path =>
                    {
                        string assetPath = PackageAssetPath(
                            extractPath,
                            path);
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
                return PackageChildrenEnvelope(
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

        subject = new(
            subject.PackageId,
            subject.PackageVersion,
            targetFramework is null
                ? null
                : new InertText.InertString(
                    InertText.TextPolicy.Field,
                    targetFramework));
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
        PackageInspectionSelection inspectionSelection =
            input.SelectAssemblies(
                candidates.Select(
                    static candidate =>
                        new PackageInspectionAssembly(
                            candidate.AssetPath,
                            candidate.TargetFramework,
                            candidate.TargetFramework)));

        await using var workspace = new InspectionWorkspace();
        using PackageInspectionAssemblyContext realization =
            await workspace.RealizePackageInspectionAsync(
                    inspectionSelection,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        var outcomes = realization.Assemblies.ToDictionary(
            static outcome => outcome.Selection.Path,
            StringComparer.Ordinal);
        PackageLibraryInspectionTarget[] targets =
        [
            .. candidates.Select(candidate =>
                CreateTarget(
                    candidate,
                    outcomes[candidate.AssetPath])),
        ];
        return await PackageChildrenInspection.ExecuteLibrariesAsync(
                subject,
                targets,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

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

    private static PackageLibraryInspectionTarget CreateTarget(
        PackageChildCandidate candidate,
        PackageInspectionAssemblyOutcome outcome) =>
        outcome switch
        {
            PackageInspectionAssemblyOutcome.Available available =>
                new(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.Role,
                    available.Group,
                    available.Participant),
            PackageInspectionAssemblyOutcome.WithoutAssembly =>
                PackageLibraryInspectionTarget.CreateUnavailable(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.Role,
                    PackageLibraryChildUnavailableReason.NotManagedAssembly,
                    "The selected Package Library is not a managed "
                        + "assembly."),
            PackageInspectionAssemblyOutcome.Unavailable unavailable =>
                PackageLibraryInspectionTarget.CreateUnavailable(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.Role,
                    PackageLibraryChildUnavailableReason
                        .AssemblyUnavailable,
                    unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Package inspection assembly outcome."),
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
        InspectionEnvelope<PackageChildrenDocument> inspection,
        InspectionResult package,
        InspectionOptions options)
    {
        PackageChildrenDocument content = inspection.Content;
        string packageSelector =
            PackageSelector(content, options);
        PackageChildOutputRow[] allRows =
            PackageChildRows(content, packageSelector);
        PackageChildOutputRow[] selectedRows =
        [
            .. RowWindow.Apply(options.Rows, allRows),
        ];
        if (options.Count)
        {
            CountOutput.WriteCount(
                selectedRows.Length,
                options.OutputPath);
            return true;
        }

        var outputDocument = new PackageChildrenOutputDocument(
            content.Subject.PackageId.ToString(),
            content.Subject.PackageVersion.ToString(),
            content.Subject.TargetFramework?.ToString(),
            content.Kind,
            content.Status,
            content.IsComplete,
            content.Detail?.ToString(),
            allRows.Length,
            selectedRows.Length,
            selectedRows);
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
                selectedRows,
                packageSelector,
                options));
        return true;
    }

    private static void WritePackageChildren(
        TextWriter output,
        InspectionResult package,
        PackageChildrenDocument content,
        PackageChildrenOutputDocument outputDocument,
        IReadOnlyList<PackageChildOutputRow> selectedRows,
        string packageSelector,
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
                        packageSelector,
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
                    packageSelector,
                    options);
                return;
            case OutputFormat.Mermaid:
                WritePackageChildrenTree(
                    output,
                    package,
                    content,
                    selectedRows,
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
                        packageSelector,
                        options);
                    return;
                }
                WritePackageChildrenTree(
                    output,
                    package,
                    content,
                    selectedRows,
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
                        packageSelector,
                        options);
                    return;
                }
                WritePackageChildrenTree(
                    output,
                    package,
                    content,
                    selectedRows,
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
        InspectionOptions options,
        IMarkoutFormatter formatter)
    {
        bool sourceHasRows =
            !document.Libraries.IsEmpty
            || !document.RuntimeIdentifierPackages.IsEmpty;
        PackageChildrenDocument selectedDocument =
            SelectPackageChildren(document, selectedRows);
        if (formatter is not MermaidFormatter)
        {
            output.WriteLine(DescribePackageChildrenSubject(
                package,
                document));
        }
        var writer = new MarkoutWriter(output, formatter);
        List<TreeNode> nodes = PackageChildrenNodes(
            selectedDocument,
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
        else if (sourceHasRows && selectedRows.Count == 0)
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
        string packageSelector,
        InspectionOptions options)
    {
        PackageChildOutputRow[] presentationRows =
            selectedRows.Count > 0
                ? [.. selectedRows]
                : PackageChildRows(
                    document,
                    packageSelector).Length == 0
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
        string packageSelector,
        InspectionOptions options)
    {
        PackageChildOutputRow[] presentationRows =
            selectedRows.Count > 0
                ? [.. selectedRows]
                : PackageChildRows(
                    document,
                    packageSelector).Length == 0
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

    private static PackageChildrenDocument SelectPackageChildren(
        PackageChildrenDocument document,
        IReadOnlyList<PackageChildOutputRow> selectedRows) =>
        new(
            document.Subject,
            document.Kind,
            document.Status,
            document.Kind == PackageChildrenKind.Libraries
                ?
                [
                    .. selectedRows
                        .Where(static row => row.Ordinal is not null)
                        .Select(
                            row => document.Libraries[
                                row.Ordinal!.Value - 1]),
                ]
                : [],
            document.Kind
                == PackageChildrenKind.RuntimeIdentifierPackages
                ?
                [
                    .. selectedRows
                        .Where(static row => row.Ordinal is not null)
                        .Select(
                            row => document.RuntimeIdentifierPackages[
                                row.Ordinal!.Value - 1]),
                ]
                : [],
            document.Detail,
            document.IsComplete);

    private static PackageChildOutputRow[] PackageChildRows(
        PackageChildrenDocument document,
        string packageSelector) =>
        document.Kind switch
        {
            PackageChildrenKind.Libraries =>
            [
                .. document.Libraries.Select(
                    (library, index) => LibraryRow(
                        document,
                        library,
                        packageSelector,
                        index + 1)),
            ],
            PackageChildrenKind.RuntimeIdentifierPackages =>
            [
                .. document.RuntimeIdentifierPackages.Select(
                    (package, index) => RuntimeIdentifierPackageRow(
                        document,
                        package,
                        index + 1)),
            ],
            PackageChildrenKind.NoManagedLibraries => [],
            _ => throw new InvalidOperationException(
                "Unknown Package children kind."),
        };

    private static PackageChildOutputRow LibraryRow(
        PackageChildrenDocument document,
        PackageLibraryChild library,
        string packageSelector,
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
            $"package {QuoteSelector(packageSelector)} "
                + $"--library {QuoteSelector(assetPath)}";
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
            $"package {QuoteSelector($"{packageId}@{version}")}",
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

    private static string QuoteSelector(string value) =>
        value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static string PackageSelector(
        PackageChildrenDocument document,
        InspectionOptions options)
    {
        string requested = options.PackageArgs.Single();
        return File.Exists(requested) || Directory.Exists(requested)
            ? Path.GetFullPath(requested)
            : $"{document.Subject.PackageId}"
                + $"@{document.Subject.PackageVersion}";
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
                    $"Dependencies ({dependencies.Length} Libraries)"));
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
