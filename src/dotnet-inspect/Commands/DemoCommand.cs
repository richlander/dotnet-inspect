using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Ecosystems;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

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
        bool mermaidRequested = false)
    {
        if (format is OutputFormat.Mermaid || mermaidRequested)
        {
            CommandError.Write(
                "--mermaid is not supported for demo list. "
                + "Run a Call Graph home demo with --mermaid (for example 'demo extensions-callgraph --mermaid').");
            return 1;
        }

        if (format == OutputFormat.Json)
        {
            var rows = EcosystemPackCatalog.DiscoverDemos()
                .Select(entry => new DemoListJsonRow(entry.ScenarioId, entry.Title, entry.Summary))
                .ToList();
            Console.WriteLine(JsonSerializer.Serialize(rows, DemoJsonContext.Default.ListDemoListJsonRow));
            return 0;
        }

        var view = new DemoListView
        {
            Demos = EcosystemPackCatalog.DiscoverDemos()
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

    public static Task<int> ExecuteScenarioAsync(
        string scenarioId,
        OutputFormat format = OutputFormat.Markdown,
        bool noHeader = false,
        bool embeddedMermaid = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);

        if (string.Equals(scenarioId, "list", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ExecuteList(format, noHeader, mermaidRequested: embeddedMermaid));

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
            return Task.FromResult(1);
        }

        ResolvedScenario resolved =
            ((EcosystemDemoSelectionResult.Known)result).Selection.Scenario;
        if (!DemoScenarioRunner.TryCreateOptions(
                resolved, format, noHeader, embeddedMermaid, out var options, out var error))
        {
            CommandError.Write(error ?? "Could not lower home demo to a section run.");
            return Task.FromResult(1);
        }

        return options switch
        {
            MemberOptions member => MemberCommand.ExecuteAsync(member),
            TypeOptions type => TypeCommand.ExecuteAsync(type),
            _ => throw new InvalidOperationException(
                $"Unexpected demo options type '{options.GetType().Name}'."),
        };
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
/// closed preset); full <see cref="WorkspaceContextLoader"/> group identity is
/// residual host work for the engine surface.
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
        if (!TryCollectCallerPackages(resolved, context, source.PackagePath, out var callers, out error))
            return false;

        if (!TryResolveRunSections(
                section,
                format,
                embeddedMermaid,
                hasCallerScope: callers.Length > 0,
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
            var family = platformMember?.Family ?? "runtime";
            var platformVersion = platformMember?.Version;
            var platformFramework = platformVersion is { Length: > 0 }
                ? $"{family}@{platformVersion}"
                : family;

            source = new DemoSource(
                PackagePath: null,
                PlatformAssembly: library,
                PlatformFramework: platformFramework,
                Tfm: context.Framework ?? platformMember?.Framework);
            return true;
        }

        var primary = ResolvePrimaryPackage(resolved, context);
        if (primary is null)
        {
            error = $"Home demo '{resolved.ScenarioId}' has no package or platform source to run.";
            return false;
        }

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

    private static bool TryCollectCallerPackages(
        ResolvedScenario resolved,
        ResolvedWorkspaceContext context,
        string? primaryPackagePath,
        out string[] callers,
        out string? error)
    {
        callers = [];
        error = null;

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

        // Platform-only extras are not expressible as --caller-package; ignore for this encoding.
        _ = resolved;
        callers = list.ToArray();
        return true;
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
