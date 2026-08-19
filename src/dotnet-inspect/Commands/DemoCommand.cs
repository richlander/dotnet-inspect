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
/// Lists and resolves product home demos from <see cref="ProductInspectionDemos"/>.
/// Default activation is resolve-only: prints the lowered plan without acquiring
/// packages or opening an <see cref="AssemblyContextGroup"/>.
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

        WriteView(view, format, noHeader);
        return 0;
    }

    public static int ExecuteScenario(string scenarioId, OutputFormat format = OutputFormat.Markdown, bool noHeader = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);

        if (string.Equals(scenarioId, "list", StringComparison.OrdinalIgnoreCase))
            return ExecuteList(format, noHeader);

        if (!ProductInspectionDemos.TryResolveHomeScenario(scenarioId, out var resolved))
        {
            CommandError.Write($"Unknown home demo '{scenarioId}'.");
            CommandError.WriteBlankLine();
            CommandError.WriteLine("Available demos:");
            foreach (var entry in ProductInspectionDemos.Entries)
                CommandError.WriteLine($"  {entry.Id}");
            CommandError.WriteBlankLine();
            CommandError.WriteLine("Run 'dotnet-inspect demo list' for titles and summaries.");
            return 1;
        }

        if (format == OutputFormat.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToJsonPlan(resolved), DemoJsonContext.Default.DemoPlanJson));
            return 0;
        }

        WriteView(ToPlanView(resolved), format, noHeader);
        return 0;
    }

    private static void WriteView<TView>(TView view, OutputFormat format, bool noHeader)
        where TView : class
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

    private static DemoPlanView ToPlanView(ResolvedScenario resolved)
    {
        var selected = resolved.SelectedContext;
        var plan = new List<DemoPlanFieldRow>
        {
            new() { Field = "id", Value = resolved.ScenarioId },
            new() { Field = "title", Value = resolved.Title ?? "" },
            new() { Field = "description", Value = resolved.Description ?? "" },
            new() { Field = "workspace", Value = resolved.Workspace?.Id ?? "" },
            new() { Field = "context", Value = resolved.SelectedContextName ?? "" },
            new() { Field = "framework", Value = selected?.Framework ?? "" },
            new()
            {
                Field = "createsAssemblyContextGroup",
                Value = resolved.CreatesAssemblyContextGroup ? "true" : "false",
            },
            new() { Field = "view.type", Value = resolved.View?.Type ?? "" },
            new() { Field = "view.memberAnchor", Value = resolved.View?.MemberAnchor ?? "" },
            new() { Field = "view.memberKey", Value = resolved.View?.MemberKey ?? "" },
            new() { Field = "view.section", Value = resolved.View?.Section ?? "" },
            new() { Field = "view.library", Value = resolved.View?.Library ?? "" },
            new()
            {
                Field = "activation",
                Value = "resolve-only (no package acquisition in this command)",
            },
        };

        var members = (selected?.Members ?? Array.Empty<WorkspaceMemberCoordinate>())
            .Select(ToMemberRow)
            .ToList();

        var navigation = new List<DemoNavigationRow>();
        if (resolved.Navigation is { } nav)
        {
            foreach (var tab in nav.Tabs)
            {
                navigation.Add(new DemoNavigationRow
                {
                    Tab = tab.Id,
                    Focus = tab.Id == nav.FocusTabId ? "yes" : "",
                    Coordinate = FormatCoordinate(tab.Coordinate),
                });
            }
        }

        return new DemoPlanView
        {
            Title = resolved.Title is { Length: > 0 } title ? title : $"Demo {resolved.ScenarioId}",
            Description = resolved.Description,
            Plan = plan,
            Members = members,
            Navigation = navigation,
        };
    }

    private static DemoPlanJson ToJsonPlan(ResolvedScenario resolved)
    {
        var selected = resolved.SelectedContext;
        return new DemoPlanJson(
            resolved.ScenarioId,
            resolved.Title,
            resolved.Description,
            resolved.Workspace?.Id,
            resolved.SelectedContextName,
            selected?.Framework,
            resolved.CreatesAssemblyContextGroup,
            resolved.View is null
                ? null
                : new DemoViewJson(
                    resolved.View.Type,
                    resolved.View.MemberAnchor,
                    resolved.View.MemberKey,
                    resolved.View.Section,
                    resolved.View.Library),
            (selected?.Members ?? Array.Empty<WorkspaceMemberCoordinate>())
                .Select(ToMemberJson)
                .ToList(),
            resolved.Navigation is null
                ? null
                : new DemoNavigationJson(
                    resolved.Navigation.FocusTabId,
                    resolved.Navigation.FocusIndex,
                    resolved.Navigation.Tabs
                        .Select(tab => new DemoNavigationTabJson(
                            tab.Id,
                            FormatCoordinate(tab.Coordinate)))
                        .ToList()),
            Activation: "resolve-only");
    }

    private static DemoMemberRow ToMemberRow(WorkspaceMemberCoordinate member) =>
        member switch
        {
            WorkspaceMemberCoordinate.PackageMember package => new DemoMemberRow
            {
                Kind = "package",
                Identity = package.PackageId,
                Version = package.Version,
                Framework = package.Framework,
            },
            WorkspaceMemberCoordinate.PlatformMember platform => new DemoMemberRow
            {
                Kind = "platform",
                Identity = platform.Assembly is { Length: > 0 } assembly
                    ? $"{platform.Family}/{assembly}"
                    : platform.Family,
                Version = platform.Version,
                Framework = platform.Framework,
            },
            WorkspaceMemberCoordinate.EmbeddedMember embedded => new DemoMemberRow
            {
                Kind = "embedded",
                Identity = embedded.DeclaredName,
                Version = null,
                Framework = null,
            },
            _ => new DemoMemberRow
            {
                Kind = member.GetType().Name,
                Identity = member.ToString() ?? "",
            },
        };

    private static DemoMemberJson ToMemberJson(WorkspaceMemberCoordinate member)
    {
        var row = ToMemberRow(member);
        return new DemoMemberJson(row.Kind, row.Identity, row.Version, row.Framework);
    }

    private static string FormatCoordinate(WorkspaceMemberCoordinate coordinate)
    {
        var row = ToMemberRow(coordinate);
        var version = string.IsNullOrEmpty(row.Version) ? "" : $"@{row.Version}";
        var framework = string.IsNullOrEmpty(row.Framework) ? "" : $" ({row.Framework})";
        return $"{row.Kind}:{row.Identity}{version}{framework}";
    }
}

public sealed record DemoListJsonRow(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary);

public sealed record DemoPlanJson(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("workspace")] string? Workspace,
    [property: JsonPropertyName("context")] string? Context,
    [property: JsonPropertyName("framework")] string? Framework,
    [property: JsonPropertyName("creates_assembly_context_group")] bool CreatesAssemblyContextGroup,
    [property: JsonPropertyName("view")] DemoViewJson? View,
    [property: JsonPropertyName("members")] IReadOnlyList<DemoMemberJson> Members,
    [property: JsonPropertyName("navigation")] DemoNavigationJson? Navigation,
    [property: JsonPropertyName("activation")] string Activation);

public sealed record DemoViewJson(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("member_anchor")] string? MemberAnchor,
    [property: JsonPropertyName("member_key")] string? MemberKey,
    [property: JsonPropertyName("section")] string? Section,
    [property: JsonPropertyName("library")] string? Library);

public sealed record DemoMemberJson(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("identity")] string Identity,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("framework")] string? Framework);

public sealed record DemoNavigationJson(
    [property: JsonPropertyName("focus_tab_id")] string FocusTabId,
    [property: JsonPropertyName("focus_index")] int FocusIndex,
    [property: JsonPropertyName("tabs")] IReadOnlyList<DemoNavigationTabJson> Tabs);

public sealed record DemoNavigationTabJson(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("coordinate")] string Coordinate);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<DemoListJsonRow>))]
[JsonSerializable(typeof(DemoPlanJson))]
internal partial class DemoJsonContext : JsonSerializerContext;
