using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Ecosystems;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Lists product home demos and runs one through the normal type/member section
/// pipeline. Public <c>demo &lt;id&gt;</c> returns real section output — not a
/// resolve-only plan dump (see workspace-definitions product-demo constraint).
/// </summary>
public static class DemoCommand
{
    public const string Name = "demo";

    /// <summary>
    /// Rejects mermaid combinations that would otherwise resolve to another format
    /// and silently drop the diagram request (e.g. <c>--json --mermaid</c>).
    /// Allowed: <c>--mermaid</c> alone, or <c>--markdown --mermaid</c>.
    /// </summary>
    public static bool TryValidateMermaidCombinations(
        bool mermaid,
        bool markdown,
        bool json,
        bool plainText,
        bool tabular,
        out string? error)
    {
        error = null;
        if (!mermaid)
            return true;

        if (json || plainText || tabular)
        {
            error =
                "--mermaid cannot be combined with --json, --plaintext, --table, --tsv, or --jsonl. "
                + "Use --mermaid alone or --markdown --mermaid on a Call Graph home demo.";
            return false;
        }

        // markdown + mermaid is embedded mode; mermaid alone is standalone.
        _ = markdown;
        return true;
    }

    public static int ExecuteList(
        OutputFormat format = OutputFormat.Markdown,
        bool noHeader = false,
        bool mermaidRequested = false,
        RowSelectionIntent<string>? rowSelection = null)
    {
        if (format is OutputFormat.Mermaid || mermaidRequested)
        {
            CommandError.Write(
                "--mermaid is not supported for demo list. "
                + "Run a Call Graph home demo with --mermaid (for example 'demo extensions-callgraph --mermaid').");
            return 1;
        }

        IReadOnlyList<EcosystemDemoDescriptor> demos =
            EcosystemPackCatalog.DiscoverDemos();
        if (!CliSemanticRowSelection.TrySelect(
                rowSelection,
                demos,
                "Home demos",
                failure =>
                    $"Demo row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} demo rows are "
                    + "available.",
                out demos))
        {
            return 1;
        }

        if (format == OutputFormat.Json)
        {
            var rows = demos
                .Select(entry => new DemoListJsonRow(entry.ScenarioId, entry.Title, entry.Summary))
                .ToList();
            Console.WriteLine(JsonSerializer.Serialize(rows, DemoJsonContext.Default.ListDemoListJsonRow));
            return 0;
        }

        var view = new DemoListView
        {
            Demos = demos
                .Select(entry => new DemoListRow
                {
                    Id = entry.ScenarioId,
                    Title = entry.Title,
                    Summary = entry.Summary,
                })
                .ToList(),
        };

        WriteListView(view, format, noHeader);
        return 0;
    }

    public static async Task<int> ExecuteScenarioAsync(
        string scenarioId,
        OutputFormat format = OutputFormat.Markdown,
        bool noHeader = false,
        bool embeddedMermaid = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);

        if (string.Equals(scenarioId, "list", StringComparison.OrdinalIgnoreCase))
            return ExecuteList(format, noHeader, mermaidRequested: embeddedMermaid);

        EcosystemDemoSelectionResult result =
            EcosystemPackCatalog.SelectDemo(scenarioId);
        if (result is EcosystemDemoSelectionResult.Unknown)
        {
            CommandError.Write($"Unknown home demo '{scenarioId}'.");
            CommandError.WriteBlankLine();
            CommandError.WriteLine("Available demos:");
            foreach (EcosystemDemoDescriptor entry in EcosystemPackCatalog.DiscoverDemos())
                CommandError.WriteLine($"  {entry.ScenarioId}");
            CommandError.WriteBlankLine();
            CommandError.WriteLine("Run 'dotnet-inspect demo list' for titles and summaries.");
            return 1;
        }

        ResolvedScenario resolved =
            ((EcosystemDemoSelectionResult.Known)result).Selection.Scenario;
        if (!DemoScenarioRunner.TryCreateOptions(
                resolved, format, noHeader, embeddedMermaid, out var options, out var error))
        {
            CommandError.Write(error ?? "Could not lower home demo to a section run.");
            return 1;
        }

        if (options.PlatformAssembly is not null)
            return await ExecutePlatformScenarioAsync(resolved, options);

        return await (options switch
        {
            MemberOptions member => MemberCommand.ExecuteAsync(member),
            TypeOptions type => TypeCommand.ExecuteAsync(type),
            _ => throw new InvalidOperationException(
                $"Unexpected demo options type '{options.GetType().Name}'."),
        });
    }

    static async Task<int> ExecutePlatformScenarioAsync(
        ResolvedScenario resolved,
        ApiOptions options)
    {
        ProductDemoRunPlan plan = ProductDemoRunPlan.Create(resolved);
        WorkspaceMemberCoordinate.PlatformMember[] platformMembers =
        [
            .. plan.Context.Members
                .OfType<WorkspaceMemberCoordinate.PlatformMember>(),
        ];
        if (platformMembers.Length != plan.Context.Members.Count)
        {
            CommandError.Write(
                $"Home demo '{resolved.ScenarioId}' cannot mix package and Platform members.");
            return 1;
        }

        WorkspaceMemberCoordinate.PlatformMember? platform =
            platformMembers.FirstOrDefault(member => string.Equals(
                member.Assembly,
                options.PlatformAssembly,
                StringComparison.OrdinalIgnoreCase));
        if (platform is null)
        {
            CommandError.Write(
                $"Home demo '{resolved.ScenarioId}' has no matching Platform member to run.");
            return 1;
        }

        using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = options.Tfm ?? plan.Context.Framework,
                    Members = platformMembers,
                },
                new WorkspaceContextLoadOptions
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
                });
        if (outcome is WorkspaceContextLoadOutcome.Failed failed)
        {
            CommandError.Write(
                $"Home demo '{resolved.ScenarioId}' could not load its exact Platform implementation.",
                [
                    .. failed.Failures.Select(static failure =>
                        $"{failure.Kind}: {failure.Message}"),
                ]);
            return 1;
        }

        WorkspaceContextLoadOutcome.Loaded loaded =
            (WorkspaceContextLoadOutcome.Loaded)outcome;
        if (loaded.Members.Length != platformMembers.Length)
        {
            CommandError.Write(
                $"Home demo '{resolved.ScenarioId}' did not load every declared Platform member.");
            return 1;
        }

        string tempDirectory =
            Directory.CreateTempSubdirectory("inspect-demo-platform").FullName;
        try
        {
            string? assemblyPath = null;
            var materializedPaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < loaded.Members.Length; index++)
            {
                WorkspaceContextMember loadedMember = loaded.Members[index];
                WorkspaceMemberCoordinate.PlatformMember declared =
                    platformMembers[index];
                if (!Equals(loadedMember.Declared, declared))
                {
                    CommandError.Write(
                        $"Home demo '{resolved.ScenarioId}' loaded Platform members out of declaration order.");
                    return 1;
                }

                string materializedPath = Path.Combine(
                    tempDirectory,
                    $"{declared.Assembly}.dll");
                if (!materializedPaths.Add(materializedPath))
                {
                    CommandError.Write(
                        $"Home demo '{resolved.ScenarioId}' has Platform members that map to the same local image name.");
                    return 1;
                }

                await using (
                    Stream sourceStream =
                        loadedMember.Participant.Assembly.OpenRead())
                await using (
                    FileStream destination =
                        File.Create(materializedPath))
                {
                    await sourceStream.CopyToAsync(destination);
                }

                if (Equals(declared, platform))
                    assemblyPath = materializedPath;
            }

            if (assemblyPath is null)
            {
                CommandError.Write(
                    $"Home demo '{resolved.ScenarioId}' did not materialize its focused Platform member.");
                return 1;
            }

            var context = new CommandContext(options.Verbose);
            var source = new ApiSourceResult(
                assemblyPath,
                assemblyPath,
                PackageName: null,
                PackageVersion: null,
                ResolvedPackagePath: null,
                PackageExtractPath: tempDirectory,
                SourceKind.Platform,
                platform.Version,
                platform.Family,
                options.Tfm ?? plan.Context.Framework,
                ProjectAssetsPath: null,
                TempDir: null,
                options.TypeName,
                PackageReplaySourceUrls: null,
                PackageReplayUsesOriginalSources: false,
                context);
            ApiServices.LoadedApiSurface? surface =
                ApiServices.LoadTypeApi(source, options);
            if (surface is null)
            {
                CommandError.Write(
                    $"Home demo '{resolved.ScenarioId}' could not inspect its exact Platform implementation.");
                return 1;
            }

            return await (options switch
            {
                MemberOptions member =>
                    MemberCommand.ExecuteResolvedAsync(
                        loaded.Members.Length > 1
                            ? member with
                            {
                                CallerScopeDirectories = [tempDirectory],
                            }
                            : member,
                        source,
                        surface),
                TypeOptions type =>
                    TypeCommand.ExecuteResolvedAsync(type, source, surface),
                _ => throw new InvalidOperationException(
                    $"Unexpected demo options type '{options.GetType().Name}'."),
            });
        }
        finally
        {
            TryDeleteTempDirectory(tempDirectory);
        }
    }

    static void TryDeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteListView(DemoListView view, OutputFormat format, bool noHeader)
    {
        switch (format)
        {
            case OutputFormat.Table:
            case OutputFormat.Tsv:
            case OutputFormat.Jsonl:
                var options = OutputFormatter.CreateTableWriterOptions(
                    tsv: format == OutputFormat.Tsv,
                    jsonl: format == OutputFormat.Jsonl);
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    new TableFormatter(!noHeader),
                    DemoViewContext.Default,
                    options);
                break;
            case OutputFormat.PlainText:
                MarkoutSerializer.Serialize(view, Console.Out, new PlainTextFormatter(), DemoViewContext.Default);
                break;
            default:
                MarkoutSerializer.Serialize(view, Console.Out, DemoViewContext.Default);
                break;
        }
    }
}

/// <summary>
/// Lowers a resolved home demo to <see cref="TypeOptions"/> or
/// <see cref="MemberOptions"/> so run uses the existing section pipeline.
/// Multi-package workspaces map extra package members to
/// <see cref="MemberOptions.CallerScopePackages"/> (CLI encoding of the same
/// closed preset). Platform demos retain their typed coordinates for exact
/// implementation-pack activation; additional Platform members enter the
/// same caller-scope pipeline through one temporary directory.
/// </summary>
public static class DemoScenarioRunner
{
    public static bool TryCreateOptions(
        ResolvedScenario resolved,
        OutputFormat format,
        bool noHeader,
        out ApiOptions options,
        out string? error) =>
        TryCreateOptions(resolved, format, noHeader, embeddedMermaid: false, out options, out error);

    public static bool TryCreateOptions(
        ResolvedScenario resolved,
        OutputFormat format,
        bool noHeader,
        bool embeddedMermaid,
        out ApiOptions options,
        out string? error)
    {
        options = null!;
        error = null;

        ProductDemoRunPlan plan;
        try
        {
            plan = ProductDemoRunPlan.Create(resolved);
        }
        catch (InspectionDefinitionException ex)
        {
            error = ex.Message;
            return false;
        }
        var view = resolved.View!;
        if (plan.Member is { } member)
        {
            return TryCreateMemberOptions(
                resolved,
                view,
                member,
                plan.Section,
                plan.Context,
                format,
                noHeader,
                embeddedMermaid,
                out options,
                out error);
        }

        return TryCreateTypeOptions(
            resolved,
            view,
            plan.Section,
            plan.Context,
            format,
            noHeader,
            embeddedMermaid,
            out options,
            out error);
    }

    private static bool TryCreateTypeOptions(
        ResolvedScenario resolved,
        ViewDefinition view,
        string section,
        ResolvedWorkspaceContext context,
        OutputFormat format,
        bool noHeader,
        bool embeddedMermaid,
        out ApiOptions options,
        out string? error)
    {
        options = null!;
        error = null;

        if (!TryResolveSource(resolved, view, context, out var source, out error))
            return false;

        // Methods demos have no caller scope.
        if (!TryResolveRunSections(
                section,
                format,
                embeddedMermaid,
                hasCallerScope: false,
                out var runSections,
                out error))
            return false;

        // Select mirrors -S so HasSectionQuery is true and TypeCommand cannot
        // silently fall through to the default shape tree under --mermaid/etc.
        TypeOptions type = new()
        {
            TypeName = view.Type,
            Select = [.. runSections],
            IncludeSections = ToIncludeSet(runSections),
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = format == OutputFormat.Markdown || embeddedMermaid,
            PackagePath = source.PackagePath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.PlatformFramework,
            Tfm = source.Tfm,
        };

        options = ApplyFormat(type, format, noHeader, embeddedMermaid);
        return true;
    }

    private static bool TryCreateMemberOptions(
        ResolvedScenario resolved,
        ViewDefinition view,
        ProductDemoMemberSelection selection,
        string section,
        ResolvedWorkspaceContext context,
        OutputFormat format,
        bool noHeader,
        bool embeddedMermaid,
        out ApiOptions options,
        out string? error)
    {
        options = null!;
        error = null;

        var memberFilter =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                selection.Name,
            };
        var kindFilter = selection.Kind is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                selection.Kind,
            };

        if (!TryResolveSource(resolved, view, context, out var source, out error))
            return false;

        // Extra package members become caller-scope packages (CLI encoding of multi-package graph).
        if (!TryCollectCallerPackages(context, source.PackagePath, out var callers))
            return false;
        bool hasCallerScope = callers.Length > 0
            || HasAdditionalPlatformMembers(
                context,
                source.PlatformAssembly);

        if (!TryResolveRunSections(
                section,
                format,
                embeddedMermaid,
                hasCallerScope,
                out var runSections,
                out error))
            return false;

        MemberOptions member = new()
        {
            TypeName = view.Type,
            MemberFilter = memberFilter,
            KindFilter = kindFilter,
            MemberDigest = selection.Anchor,
            Select = [.. runSections],
            IncludeSections = ToIncludeSet(runSections),
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
            ShowDocs = false,
            DocsExplicitlySet = true,
            PackagePath = source.PackagePath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.PlatformFramework,
            Tfm = source.Tfm,
            CallerScopePackages = callers,
        };

        options = ApplyFormat(member, format, noHeader, embeddedMermaid);
        return true;
    }

    /// <summary>
    /// Format-aware section expansion for a closed home-demo binding.
    /// Markdown: Call Graph + Callers. Table/tsv/jsonl: Callers when the demo
    /// has caller scope (survives MemberCommand's Callers inject); Call Graph
    /// when it does not (package-local entry points with empty Callers).
    /// Standalone mermaid: Call Graph only. Embedded mermaid requires a Call
    /// Graph bind (member pipeline) and keeps the Markdown companion set.
    /// Structured document JSON is rejected for Call Graph/Callers binds until
    /// those sections project into that payload.
    /// </summary>
    private static bool TryResolveRunSections(
        string boundSection,
        OutputFormat format,
        bool embeddedMermaid,
        bool hasCallerScope,
        out IReadOnlyList<string> runSections,
        out string? error)
    {
        error = null;
        runSections = [];

        var isCallGraph = string.Equals(
            boundSection, ProductDemoSections.CallGraph, StringComparison.Ordinal);
        var isCallers = string.Equals(
            boundSection, ProductDemoSections.Callers, StringComparison.Ordinal);
        var wantsMermaid = format is OutputFormat.Mermaid || embeddedMermaid;

        if (wantsMermaid && !isCallGraph)
        {
            error =
                $"--mermaid requires a Call Graph home demo (got bound section '{boundSection}'). "
                + "Use default Markdown or another format for Methods demos.";
            return false;
        }

        if (format is OutputFormat.Mermaid && !embeddedMermaid)
        {
            runSections = [ProductDemoSections.CallGraph];
            return true;
        }

        if (format is OutputFormat.Json && (isCallGraph || isCallers))
        {
            error =
                "--json cannot represent Call Graph/Callers section output yet. "
                + "Use default Markdown, --mermaid, or --table/--tsv/--jsonl.";
            return false;
        }

        var singleSectionFormat = format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl;
        runSections = ProductDemoSections.ExpandRunSections(
            boundSection,
            singleSectionFormat,
            hasCallerScope);
        return true;
    }

    private static HashSet<string> ToIncludeSet(IReadOnlyList<string> sections) =>
        new(sections, StringComparer.OrdinalIgnoreCase);

    private readonly record struct DemoSource(
        string? PackagePath,
        string? PlatformAssembly,
        string? PlatformFramework,
        string? Tfm);

    private static bool TryResolveSource(
        ResolvedScenario resolved,
        ViewDefinition view,
        ResolvedWorkspaceContext context,
        out DemoSource source,
        out string? error)
    {
        source = default;
        error = null;

        // Prefer an explicit library (platform assembly) when the view names one.
        if (view.Library is { Length: > 0 } library)
        {
            var platformMember = context.Members
                .OfType<WorkspaceMemberCoordinate.PlatformMember>()
                .FirstOrDefault();
            source = PlatformSource(library, platformMember, context);
            return true;
        }

        var focusedPlatform = ResolveFocusedPlatform(resolved);
        if (focusedPlatform?.Assembly is { Length: > 0 } focusedAssembly)
        {
            source = PlatformSource(
                focusedAssembly,
                focusedPlatform,
                context);
            return true;
        }

        var primary = ResolvePrimaryPackage(resolved, context);
        if (primary is not null)
        {
            var packageVersion = primary.Version;
            var packagePath = packageVersion is { Length: > 0 }
                ? $"{primary.PackageId}@{packageVersion}"
                : primary.PackageId;

            source = new DemoSource(
                PackagePath: packagePath,
                PlatformAssembly: null,
                PlatformFramework: null,
                Tfm: primary.Framework ?? context.Framework);
            return true;
        }

        var primaryPlatform = context.Members
            .OfType<WorkspaceMemberCoordinate.PlatformMember>()
            .FirstOrDefault();
        if (primaryPlatform?.Assembly is { Length: > 0 } assembly)
        {
            source = PlatformSource(assembly, primaryPlatform, context);
            return true;
        }

        error = $"Home demo '{resolved.ScenarioId}' has no package or platform source to run.";
        return false;
    }

    private static DemoSource PlatformSource(
        string assembly,
        WorkspaceMemberCoordinate.PlatformMember? platform,
        ResolvedWorkspaceContext context)
    {
        var family = platform?.Family ?? "runtime";
        var version = platform?.Version;
        var framework = version is { Length: > 0 }
            ? $"{family}@{version}"
            : family;
        return new DemoSource(
            PackagePath: null,
            PlatformAssembly: assembly,
            PlatformFramework: framework,
            Tfm: context.Framework ?? platform?.Framework);
    }

    private static WorkspaceMemberCoordinate.PackageMember? ResolvePrimaryPackage(
        ResolvedScenario resolved,
        ResolvedWorkspaceContext context)
    {
        if (resolved.Navigation is { } nav
            && nav.FocusTab.Coordinate is WorkspaceMemberCoordinate.PackageMember focusPackage)
        {
            return focusPackage;
        }

        return context.Members.OfType<WorkspaceMemberCoordinate.PackageMember>().FirstOrDefault();
    }

    private static WorkspaceMemberCoordinate.PlatformMember?
        ResolveFocusedPlatform(ResolvedScenario resolved)
    {
        if (resolved.Navigation is { } nav
            && nav.FocusTab.Coordinate
                is WorkspaceMemberCoordinate.PlatformMember focusPlatform)
        {
            return focusPlatform;
        }

        return null;
    }

    private static bool TryCollectCallerPackages(
        ResolvedWorkspaceContext context,
        string? primaryPackagePath,
        out string[] callers)
    {
        callers = [];

        string? primaryId = null;
        if (primaryPackagePath is { Length: > 0 })
        {
            var at = primaryPackagePath.IndexOf('@');
            primaryId = at > 0 ? primaryPackagePath[..at] : primaryPackagePath;
        }

        var list = new List<string>();
        foreach (var member in context.Members.OfType<WorkspaceMemberCoordinate.PackageMember>())
        {
            if (primaryId is not null
                && string.Equals(member.PackageId, primaryId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            list.Add(member.Version is { Length: > 0 } version
                ? $"{member.PackageId}@{version}"
                : member.PackageId);
        }

        callers = list.ToArray();
        return true;
    }

    private static bool HasAdditionalPlatformMembers(
        ResolvedWorkspaceContext context,
        string? primaryAssembly)
    {
        if (primaryAssembly is null)
            return false;

        return context.Members
            .OfType<WorkspaceMemberCoordinate.PlatformMember>()
            .Any(member => !string.Equals(
                member.Assembly,
                primaryAssembly,
                StringComparison.OrdinalIgnoreCase));
    }

    private static TypeOptions ApplyFormat(
        TypeOptions options,
        OutputFormat format,
        bool noHeader,
        bool embeddedMermaid)
    {
        options = options with
        {
            NoHeader = noHeader,
            FormatExplicitlySet = true,
            FormatFlagExplicitlySet = format is not OutputFormat.Markdown || embeddedMermaid,
            MarkdownExplicitlySet = format == OutputFormat.Markdown || embeddedMermaid || options.MarkdownExplicitlySet,
            EmbeddedMermaid = embeddedMermaid,
        };
        return format switch
        {
            OutputFormat.Json => options with { JsonOutput = true },
            OutputFormat.Table => options with { Tabular = true, TabularExplicitlySet = true },
            OutputFormat.Tsv => options with { Tabular = true, Tsv = true, TabularExplicitlySet = true },
            OutputFormat.Jsonl => options with { Tabular = true, Jsonl = true, TabularExplicitlySet = true },
            OutputFormat.PlainText => options with { PlainText = true },
            OutputFormat.Mermaid => options with { MermaidOutput = true },
            _ => options,
        };
    }

    private static MemberOptions ApplyFormat(
        MemberOptions options,
        OutputFormat format,
        bool noHeader,
        bool embeddedMermaid)
    {
        options = options with
        {
            NoHeader = noHeader,
            FormatExplicitlySet = true,
            FormatFlagExplicitlySet = format is not OutputFormat.Markdown || embeddedMermaid,
            EmbeddedMermaid = embeddedMermaid,
        };
        return format switch
        {
            OutputFormat.Json => options with { JsonOutput = true },
            OutputFormat.Table => options with { Tabular = true, TabularExplicitlySet = true },
            OutputFormat.Tsv => options with { Tabular = true, Tsv = true, TabularExplicitlySet = true },
            OutputFormat.Jsonl => options with { Tabular = true, Jsonl = true, TabularExplicitlySet = true },
            OutputFormat.PlainText => options with { PlainText = true },
            OutputFormat.Mermaid => options with { MermaidOutput = true },
            _ => options,
        };
    }
}

public sealed record DemoListJsonRow(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<DemoListJsonRow>))]
internal partial class DemoJsonContext : JsonSerializerContext;
