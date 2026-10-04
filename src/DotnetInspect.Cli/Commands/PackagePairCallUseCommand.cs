using System.Collections.Immutable;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;
using Markout.Formatting;
using QuerySpace.Composition;

namespace DotnetInspect.Cli.Commands;

public sealed record PackagePairCallUseOptions
{
    public required IReadOnlyList<string> Packages { get; init; }
    public required string TargetFramework { get; init; }
    public OutputFormat Format { get; init; }
    public bool Count { get; init; }
    public RowSelectionIntent<string>? RowSelection { get; init; }
    public RowWindow? Rows { get; init; }
    public bool NoHeader { get; init; }
    public bool Verbose { get; init; }
    public IReadOnlyList<string> Sections { get; init; } = [];
    public int? Cluster { get; init; }
    public NuGetSourceOptions SourceOptions { get; init; } =
        NuGetSourceOptions.Default;
}

public static class PackagePairCallUseCommand
{
    public const string Name = "packages";
    public const string DirectUseClustersSection = "Direct Use Clusters";
    public const string LibraryPairsSection = "Library Pairs";
    public const string CallSitesSection = "Call Sites";

    static readonly HashSet<string> SupportedSections =
        new(
            [
                DirectUseClustersSection,
                LibraryPairsSection,
                CallSitesSection,
            ],
            StringComparer.OrdinalIgnoreCase);

    public static async Task<int> ExecuteAsync(
        PackagePairCallUseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Packages.Count != 2)
        {
            CommandError.Write(
                "Exactly two --package values are required.");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(options.TargetFramework))
        {
            CommandError.Write("--tfm is required.");
            return 1;
        }
        string[] sections = options.Sections.Count == 0
            ? [
                options.Cluster is null
                    ? DirectUseClustersSection
                    : CallSitesSection,
            ]
            : [.. options.Sections.Distinct(
                StringComparer.OrdinalIgnoreCase)];
        string? unsupported = sections.FirstOrDefault(
            section => !SupportedSections.Contains(section));
        if (unsupported is not null)
        {
            CommandError.Write(
                $"Unknown Package-pair section '{unsupported}'.");
            return 1;
        }
        if (options.RowSelection is not null
            && sections.Length != 1)
        {
            CommandError.Write(
                "Semantic row selection requires exactly one Package-pair section.");
            return 1;
        }
        if (options.Cluster <= 0)
        {
            CommandError.Write(
                "The Direct-Use Cluster ordinal must be positive.");
            return 1;
        }
        if (!InspectionGraphCommand.TryCreateMembers(
                options.Packages,
                out WorkspaceMemberCoordinate[] members))
        {
            return 1;
        }

        await using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome load =
            await WorkspaceContextLoader.LoadAsync(
                    workspace,
                    new WorkspaceContextInput
                    {
                        Framework = options.TargetFramework,
                        Members = members,
                    },
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = HttpClientFactory.Shared,
                        SourceAuthorization =
                            new SourcePolicyPackageSourceAuthorization(
                                options.SourceOptions),
                        PackageStore = new FileSystemPackageStore(),
                        IncludePackageRootBindings = true,
                        UseVersionCache = true,
                        Log = options.Verbose
                            ? CommandError.WriteLine
                            : null,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        if (load is WorkspaceContextLoadOutcome.Failed failed)
        {
            CommandError.Write(
                "The Package-pair workspace could not be loaded.",
                [
                    .. failed.Failures.Select(static failure =>
                        $"{failure.Kind}: {failure.Message}"),
                ]);
            return 1;
        }

        var context = (WorkspaceContextLoadOutcome.Loaded)load;
        using AssemblyContextGroup group = context.Group;
        if (context.PackageRoots.Length != 2)
        {
            CommandError.Write(
                "Both Package subjects must produce exact Root bindings.");
            return 1;
        }
        WorkspaceScopeSnapshot current =
            ((WorkspaceScopeReadResult.Available)
                await workspace.GetScopeSnapshotAsync()
                    .ConfigureAwait(false))
            .Snapshot;
        WorkspaceScopeOperationResult scopeResult =
            await workspace.ReplaceScopeAsync(
                    current.Revision,
                    context.PackageRoots,
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    cancellationToken)
                .ConfigureAwait(false);
        if (scopeResult
            is not WorkspaceScopeOperationResult.Committed committed)
        {
            CommandError.Write(
                "The Package-pair Workspace Scope could not be committed.");
            return 1;
        }

        InspectionEnvelope<PackagePairDirectUseClusterOperationOutcome>
            inspection =
                await PackagePairDirectUseClusterInspection.ExecuteAsync(
                        new(
                            workspace,
                            committed.Snapshot,
                            context.PackageRoots),
                        cancellationToken)
                    .ConfigureAwait(false);
        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            if (diagnostic.Severity
                == InspectionDiagnosticSeverity.Warning)
            {
                CommandError.WriteWarning(diagnostic.Summary.ToString());
            }
        }
        if (inspection.Content
                is not PackagePairDirectUseClusterOperationOutcome
                    .Completed completed
            || completed.Content
                is not PackagePairDirectUseClusterOutcome.Available
                    available)
        {
            CommandError.Write(
                "The Package-pair call topology is unavailable.",
                [
                    .. inspection.Diagnostics.Select(static diagnostic =>
                        diagnostic.Summary.ToString()),
                ]);
            return 1;
        }

        PackagePairCallUseView view = CreateView(
            available.Document,
            sections,
            options.Cluster);
        if (options.Cluster is int cluster
            && !available.Document.Clusters.Any(
                candidate => candidate.Ordinal == cluster))
        {
            CommandError.Write(
                $"Direct-Use Cluster {cluster} was not found.");
            return 1;
        }
        if (!TryApplyRows(
                view,
                sections,
                options,
                out PackagePairCallUseView selected))
        {
            return 1;
        }
        Write(selected, options);
        if (!available.Document.IsComplete)
        {
            CommandError.Write(
                "The Package-pair call topology is incomplete.",
                [
                    .. inspection.Diagnostics.Select(static diagnostic =>
                        diagnostic.Summary.ToString()),
                ]);
            return 1;
        }
        return 0;
    }

    static PackagePairCallUseView CreateView(
        PackagePairDirectUseClusterDocument document,
        IReadOnlyCollection<string> sections,
        int? focusedCluster)
    {
        bool clusters = sections.Contains(
            DirectUseClustersSection,
            StringComparer.OrdinalIgnoreCase);
        bool pairs = sections.Contains(
            LibraryPairsSection,
            StringComparer.OrdinalIgnoreCase);
        bool calls = sections.Contains(
            CallSitesSection,
            StringComparer.OrdinalIgnoreCase);
        return new()
        {
            DirectUseClusters = clusters
                ? [
                    .. PackagePairDirectUseClusterRows.Clusters(document)
                        .Where(row =>
                            focusedCluster is null
                            || row.Cluster == focusedCluster)
                        .Select(static row =>
                            new PackagePairDirectUseClusterViewRow
                            {
                                Cluster = row.Cluster,
                                LibraryPair = row.LibraryPair,
                                SourcePackage = row.SourcePackage,
                                SourceLibrary = row.SourceLibrary,
                                SourceMvid =
                                    row.SourceModuleVersionId.ToString("D"),
                                AnchorSourceToken =
                                    $"0x{row.AnchorSourceMethodToken:X8}",
                                TargetPackage = row.TargetPackage,
                                TargetLibrary = row.TargetLibrary,
                                TargetMvid =
                                    row.TargetModuleVersionId.ToString("D"),
                                AnchorTargetToken =
                                    $"0x{row.AnchorTargetMethodToken:X8}",
                                SourceMembers = row.SourceMembers,
                                ProviderTypes = row.ProviderTypes,
                                TargetMembers = row.TargetMembers,
                                ExtensionMethods = row.ExtensionMethods,
                                CallSites = row.CallSites,
                            }),
                ]
                : [],
            LibraryPairs = pairs
                ? [
                    .. PackagePairDirectUseClusterRows.LibraryPairs(document)
                        .Select(static row =>
                            new PackagePairLibraryPairViewRow
                            {
                                LibraryPair = row.LibraryPair,
                                FirstPackage = row.FirstPackage,
                                FirstLibrary = row.FirstLibrary,
                                SecondPackage = row.SecondPackage,
                                SecondLibrary = row.SecondLibrary,
                                Clusters = row.Clusters,
                                CallSites = row.CallSites,
                                Complete = row.Complete,
                            }),
                ]
                : [],
            CallSites = calls
                ? [
                    .. PackagePairDirectUseClusterRows.CallSites(document)
                        .Where(row =>
                            focusedCluster is null
                            || row.Cluster == focusedCluster)
                        .Select(static row =>
                            new PackagePairCallSiteViewRow
                            {
                                LibraryPair = row.LibraryPair,
                                Cluster = row.Cluster,
                                SourcePackage = row.SourcePackage,
                                SourceLibrary = row.SourceLibrary,
                                SourceMvid =
                                    row.SourceModuleVersionId.ToString("D"),
                                SourceMember =
                                    LibraryMetadataService.FormatMethod(
                                        row.SourceMethod),
                                SourceToken =
                                    $"0x{row.SourceMethod.MetadataToken:X8}",
                                TargetPackage = row.TargetPackage,
                                TargetLibrary = row.TargetLibrary,
                                TargetMvid =
                                    row.TargetModuleVersionId.ToString("D"),
                                TargetMember =
                                    LibraryMetadataService.FormatMethod(
                                        row.TargetMethod),
                                TargetToken =
                                    $"0x{row.TargetMethod.MetadataToken:X8}",
                                Call = row.Call.Kind.ToString(),
                                EvidenceMethod =
                                    LibraryMetadataService.FormatMethod(
                                        row.Call.EvidenceMethod),
                                EvidenceToken =
                                    $"0x{row.Call.EvidenceMethod.MetadataToken:X8}",
                                IlOffset = $"0x{row.Call.ILOffset:X4}",
                            }),
                ]
                : [],
        };
    }

    static bool TryApplyRows(
        PackagePairCallUseView view,
        string[] sections,
        PackagePairCallUseOptions options,
        out PackagePairCallUseView selected)
    {
        selected = view;
        if (sections.Length != 1)
            return true;
        string section = sections[0];
        if (section.Equals(
            DirectUseClustersSection,
            StringComparison.OrdinalIgnoreCase))
        {
            if (!Select(
                options,
                view.DirectUseClusters,
                "Package direct-use cluster",
                out IReadOnlyList<PackagePairDirectUseClusterViewRow> rows))
            {
                return false;
            }
            selected = view.WithRows(clusters: [.. rows]);
        }
        else if (section.Equals(
            LibraryPairsSection,
            StringComparison.OrdinalIgnoreCase))
        {
            if (!Select(
                options,
                view.LibraryPairs,
                "Package Library pair",
                out IReadOnlyList<PackagePairLibraryPairViewRow> rows))
            {
                return false;
            }
            selected = view.WithRows(pairs: [.. rows]);
        }
        else
        {
            if (!Select(
                options,
                view.CallSites,
                "Package call site",
                out IReadOnlyList<PackagePairCallSiteViewRow> rows))
            {
                return false;
            }
            selected = view.WithRows(calls: [.. rows]);
        }
        return true;
    }

    static bool Select<TRow>(
        PackagePairCallUseOptions options,
        IReadOnlyList<TRow> source,
        string subject,
        out IReadOnlyList<TRow> rows) =>
        CliSemanticRowSelection.TrySelectOrApplyLegacy(
            options.RowSelection,
            options.Rows,
            source,
            subject,
            failure =>
                $"{subject} row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} rows are available.",
            out rows);

    static PackagePairCallUseView WithRows(
        this PackagePairCallUseView view,
        List<PackagePairDirectUseClusterViewRow>? clusters = null,
        List<PackagePairLibraryPairViewRow>? pairs = null,
        List<PackagePairCallSiteViewRow>? calls = null) =>
        new()
        {
            DirectUseClusters = clusters ?? view.DirectUseClusters,
            LibraryPairs = pairs ?? view.LibraryPairs,
            CallSites = calls ?? view.CallSites,
        };

    static void Write(
        PackagePairCallUseView view,
        PackagePairCallUseOptions options)
    {
        if (options.Count)
        {
            CountProjection count = CountProjectionFormatter.Capture(
                view,
                PackagePairCallUseViewContext.Default,
                new MarkoutWriterOptions());
            CountOutput.WriteCount(count.Total);
            return;
        }
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions>
            serialize = (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    PackagePairCallUseViewContext.Default,
                    writerOptions);
        switch (options.Format)
        {
            case OutputFormat.Json:
                OutputFormatter.WriteProjectedJson(
                    Console.Out,
                    columns: null,
                    fields: null,
                    serialize);
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
            case OutputFormat.Jsonl:
                OutputFormatter.WriteProjectedTable(
                    Console.Out,
                    showHeader: !options.NoHeader,
                    tsv: options.Format == OutputFormat.Tsv,
                    jsonl: options.Format == OutputFormat.Jsonl,
                    columns: null,
                    fields: null,
                    serialize);
                break;
            default:
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    options.Format == OutputFormat.PlainText
                        ? new PlainTextFormatter()
                        : new MarkdownFormatter(),
                    PackagePairCallUseViewContext.Default);
                break;
        }
    }
}
