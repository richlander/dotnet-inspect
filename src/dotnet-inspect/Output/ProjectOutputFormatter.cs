using System.Text.Json;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

internal static class ProjectOutputFormatter
{
    public static ProjectInspectionView BuildView(ProjectInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return new ProjectInspectionView
        {
            Skills = inspection.Skills is null
                ? null
                : [.. inspection.Skills.Skills.Select(skill => new ProjectSkillRow(
                    skill.Package,
                    skill.Version,
                    skill.Path,
                    skill.Size,
                    skill.Name,
                    skill.Description))],
            AgentGuidance = inspection.AgentGuidance is null
                ? null
                : [.. inspection.AgentGuidance.Guidance.Select(guidance =>
                    new ProjectAgentGuidanceRow(
                        guidance.Package,
                        guidance.Version,
                        guidance.Path,
                        guidance.Name,
                        guidance.Description))],
            PackageDocs = inspection.PackageDocuments is null
                ? null
                : [.. inspection.PackageDocuments.Documents.Select(document =>
                    new ProjectPackageDocumentRow(
                        document.Package,
                        document.Version,
                        document.Path,
                        document.Size))],
        };
    }

    public static string Render(
        ProjectInspection inspection,
        ProjectOptions options,
        HashSet<string> includeSections)
    {
        ProjectInspectionView view = BuildView(inspection);
        if (options.JsonOutput)
        {
            view = ApplyRowWindow(view, options.Rows);
            return JsonSerializer.Serialize(
                    view,
                    ProjectViewJsonContext.Default.ProjectInspectionView)
                + '\n';
        }

        if (options.Tabular)
        {
            using var output = new StringWriter();
            OutputFormatter.WriteProjectedTable(
                output,
                !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                MergeProjectionNames(options.Columns, options.Fields),
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        ProjectViewContext.Default,
                        writerOptions);
                },
                options.Rows);
            return output.ToString();
        }

        var markdownOptions = new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            Projection = OutputFormatter.BuildProjection(
                MergeProjectionNames(options.Columns, options.Fields)),
            RowWindow = RowWindow.ToMarkout(options.Rows),
        };
        return MarkoutSerializer.Serialize(
            view,
            ProjectViewContext.Default,
            markdownOptions);
    }

    static ProjectInspectionView ApplyRowWindow(
        ProjectInspectionView view,
        RowWindow? rows)
        => new()
        {
            Skills = Window(view.Skills, rows),
            AgentGuidance = Window(view.AgentGuidance, rows),
            PackageDocs = Window(view.PackageDocs, rows),
        };

    static List<T>? Window<T>(List<T>? source, RowWindow? rows) =>
        source is null ? null : RowWindow.Apply(rows, source).ToList();

    static string[]? MergeProjectionNames(
        string[]? columns,
        string[]? fields)
    {
        if (columns is not { Length: > 0 } && fields is not { Length: > 0 })
            return null;

        return [.. (columns ?? []).Concat(fields ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
