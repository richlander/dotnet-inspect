using System.Text.Json;
using System.Text.Json.Serialization;
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

    public static int ExecuteList(OutputFormat format = OutputFormat.Markdown, bool noHeader = false)
    {
        if (format == OutputFormat.Json)
        {
            var rows = ProductInspectionDemos.Entries
                .Select(entry => new DemoListJsonRow(entry.Id, entry.Title, entry.Summary))
                .ToList();
            Console.WriteLine(JsonSerializer.Serialize(rows, DemoJsonContext.Default.ListDemoListJsonRow));
            return 0;
        }

        var view = new DemoListView
        {
            Demos = ProductInspectionDemos.Entries
                .Select(entry => new DemoListRow
                {
                    Id = entry.Id,
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
            return Task.FromResult(ExecuteList(format, noHeader));

        if (!ProductInspectionDemos.TryResolveHomeScenario(scenarioId, out var resolved))
        {
            CommandError.Write($"Unknown home demo '{scenarioId}'.");
            CommandError.WriteBlankLine();
            CommandError.WriteLine("Available demos:");
            foreach (var entry in ProductInspectionDemos.Entries)
                CommandError.WriteLine($"  {entry.Id}");
            CommandError.WriteBlankLine();
            CommandError.WriteLine("Run 'dotnet-inspect demo list' for titles and summaries.");
            return Task.FromResult(1);
        }

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

        try
        {
            ProductDemoSections.EnsureHomeDemoBinding(resolved);
        }
        catch (InspectionDefinitionException ex)
        {
            error = ex.Message;
            return false;
        }

        var view = resolved.View!;
        var section = view.Section!;
        if (view.Type is not { Length: > 0 })
        {
            error = $"Home demo '{resolved.ScenarioId}' view must set type.";
            return false;
        }

        var context = resolved.SelectedContext;
        if (context is null)
        {
            error = $"Home demo '{resolved.ScenarioId}' has no selected workspace context.";
            return false;
        }

        var isMemberDemo = view.MemberAnchor is { Length: > 0 }
            || view.MemberSignature is { Length: > 0 }
            || view.MemberKey is { Length: > 0 };

        if (isMemberDemo)
        {
            return TryCreateMemberOptions(
                resolved, view, section, context, format, noHeader, embeddedMermaid, out options, out error);
        }

        return TryCreateTypeOptions(
            resolved, view, section, context, format, noHeader, embeddedMermaid, out options, out error);
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

        if (!TryResolveRunSections(section, format, embeddedMermaid, out var runSections, out error))
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

        if (!TryParseMemberKey(view.MemberKey, out var memberName, out var kind, out error))
            return false;

        if (memberName is null && view.MemberAnchor is null && view.MemberSignature is null)
        {
            error = $"Home demo '{resolved.ScenarioId}' member view needs memberKey, memberAnchor, or memberSignature.";
            return false;
        }

        // Anchor demos still need a member name for the CLI selector; prefer memberKey.
        if (memberName is null)
        {
            error = $"Home demo '{resolved.ScenarioId}' member view must set memberKey (kind:name) for CLI run.";
            return false;
        }

        var memberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { memberName };
        var kindFilter = kind is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { kind };

        if (!TryResolveSource(resolved, view, context, out var source, out error))
            return false;

        // Extra package members become caller-scope packages (CLI encoding of multi-package graph).
        if (!TryCollectCallerPackages(resolved, context, source.PackagePath, out var callers, out error))
            return false;

        if (!TryResolveRunSections(section, format, embeddedMermaid, out var runSections, out error))
            return false;

        MemberOptions member = new()
        {
            TypeName = view.Type,
            MemberFilter = memberFilter,
            KindFilter = kindFilter,
            MemberDigest = view.MemberAnchor,
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
    /// Markdown: Call Graph + Callers. Table/tsv/jsonl: Callers only (survives
    /// MemberCommand's caller-scope Callers inject). Standalone mermaid: Call
    /// Graph only. Structured document JSON is rejected for Call Graph demos
    /// until graph sections project into that payload.
    /// </summary>
    private static bool TryResolveRunSections(
        string boundSection,
        OutputFormat format,
        bool embeddedMermaid,
        out IReadOnlyList<string> runSections,
        out string? error)
    {
        error = null;
        runSections = [];

        var isCallGraph = string.Equals(
            boundSection, ProductDemoSections.CallGraph, StringComparison.Ordinal);

        if (format is OutputFormat.Mermaid && !embeddedMermaid)
        {
            if (!isCallGraph)
            {
                error =
                    $"--mermaid requires a Call Graph home demo (got bound section '{boundSection}'). "
                    + "Use default Markdown or another format for Methods demos.";
                return false;
            }

            runSections = [ProductDemoSections.CallGraph];
            return true;
        }

        if (format is OutputFormat.Json && isCallGraph)
        {
            error =
                "--json cannot represent Call Graph/Callers section output yet. "
                + "Use default Markdown, --mermaid, or --table/--tsv/--jsonl (Callers rows).";
            return false;
        }

        var singleSectionFormat = format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl;
        runSections = ProductDemoSections.ExpandRunSections(boundSection, singleSectionFormat);
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

    private static bool TryParseMemberKey(
        string? memberKey,
        out string? memberName,
        out string? kind,
        out string? error)
    {
        memberName = null;
        kind = null;
        error = null;
        if (string.IsNullOrWhiteSpace(memberKey))
            return true;

        var colon = memberKey.IndexOf(':');
        if (colon <= 0 || colon >= memberKey.Length - 1)
        {
            memberName = memberKey;
            return true;
        }

        kind = memberKey[..colon];
        memberName = memberKey[(colon + 1)..];
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(memberName))
        {
            error = $"Invalid memberKey '{memberKey}' (expected kind:name).";
            return false;
        }

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
