using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Commands;

public class ProjectCommand
{
    public const string Name = "project";

    public static async Task<int> ExecuteAsync(ProjectOptions options)
    {
        if (!ValidateOptions(options))
            return 1;

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var assetsPath = ProjectAssetsParser.FindAssets(options.ProjectPath);
        if (assetsPath == null)
        {
            Console.Error.WriteLine($"Error: project.assets.json not found for '{options.ProjectPath}'. Run 'dotnet restore'.");
            return 1;
        }

        logger.Log($"Using assets: {assetsPath}");
        var dependencies = ProjectAssetsParser.ParsePackageReferences(assetsPath, options.Tfm, logger.Log);
        if (dependencies.Count == 0)
        {
            Console.Error.WriteLine($"Error: No direct package references found in '{assetsPath}'.");
            return 1;
        }

        if (options.AgentsIndex)
            return WriteAgentsIndex(dependencies, options);

        return await WriteReadmeAsync(dependencies, options, context);
    }

    private static bool ValidateOptions(ProjectOptions options)
    {
        if (options.FrontmatterRequested && options.BodyRequested)
        {
            Console.Error.WriteLine("Error: --frontmatter/--yaml-header cannot be combined with --body.");
            return false;
        }

        var modeCount = (options.AgentsIndex ? 1 : 0) + (options.ReadmePackageId != null ? 1 : 0);
        if (modeCount != 1)
        {
            Console.Error.WriteLine("Error: Specify exactly one project mode: --agents-index or --readme <package-id>.");
            return false;
        }

        if (options.AgentsIndex && options.BodyRequested)
        {
            Console.Error.WriteLine("Error: --body cannot be combined with --agents-index.");
            return false;
        }

        if (options.ReadmePackageId != null && options.OneLine && !options.Jsonl)
        {
            Console.Error.WriteLine("Error: project --readme supports raw text, --json, or --jsonl; it cannot be combined with --table or --tsv.");
            return false;
        }

        return true;
    }

    private static int WriteAgentsIndex(IReadOnlyList<ProjectPackageReference> dependencies, ProjectOptions options)
    {
        var rows = dependencies
            .Select(CreateAgentsIndexRow)
            .ToList();

        var output = options.JsonOutput
            ? JsonSerializer.Serialize(rows.ToArray(), ProjectCommandJsonContext.Default.ProjectAgentsIndexRowArray)
            : options.Jsonl
                ? RenderAgentsIndexJsonl(rows)
                : options.OneLine
                    ? RenderAgentsIndexTable(rows, options)
                    : RenderAgentsIndexMarkdown(rows);

        WriteOutput(output, options.OutputPath);
        return 0;
    }

    private static ProjectAgentsIndexRow CreateAgentsIndexRow(ProjectPackageReference dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency.PackagePath) || !Directory.Exists(dependency.PackagePath))
            return EmptyAgentsIndexRow(dependency);

        var agentsPath = Path.Combine(dependency.PackagePath, "AGENTS.md");
        if (!File.Exists(agentsPath))
            return EmptyAgentsIndexRow(dependency);

        var content = File.ReadAllText(agentsPath);
        var frontmatter = MarkdownContent.ParseYamlFrontmatter(content);
        frontmatter.TryGetValue("name", out var name);
        frontmatter.TryGetValue("description", out var description);

        return new ProjectAgentsIndexRow(
            dependency.PackageName,
            dependency.Version,
            name ?? "",
            description ?? "",
            "AGENTS.md");
    }

    private static ProjectAgentsIndexRow EmptyAgentsIndexRow(ProjectPackageReference dependency)
        => new(
            dependency.PackageName,
            dependency.Version,
            Name: "",
            Description: "",
            Path: "");

    private static string RenderAgentsIndexJsonl(IEnumerable<ProjectAgentsIndexRow> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder.AppendLine(JsonSerializer.Serialize(row, ProjectCommandCompactJsonContext.Default.ProjectAgentsIndexRow));
        return builder.ToString();
    }

    private static string RenderAgentsIndexTable(IReadOnlyList<ProjectAgentsIndexRow> rows, ProjectOptions options)
        => OutputFormatter.RenderTable(!options.NoHeader, (writer, formatter) =>
        {
            var markoutWriter = new MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
            markoutWriter.WriteTable(
                ["Package", "Version", "Name", "Description"],
                ["package", "version", "name", "description"],
                rows.Select(row => new[] { row.Package, row.Version, row.Name, row.Description }).ToArray());
            markoutWriter.Flush();
        });

    private static string RenderAgentsIndexMarkdown(IReadOnlyList<ProjectAgentsIndexRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Project AGENTS.md Index");
        builder.AppendLine();
        builder.AppendLine("| Package | Version | Name | Description |");
        builder.AppendLine("| ------- | ------- | ---- | ----------- |");
        foreach (var row in rows)
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdownTableCell(row.Package));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Version));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Name));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Description));
            builder.AppendLine(" |");
        }

        return builder.ToString();
    }

    private static async Task<int> WriteReadmeAsync(
        IReadOnlyList<ProjectPackageReference> dependencies,
        ProjectOptions options,
        CommandContext context)
    {
        var dependency = dependencies.FirstOrDefault(dep =>
            dep.PackageName.Equals(options.ReadmePackageId, StringComparison.OrdinalIgnoreCase));
        if (dependency == null)
        {
            Console.Error.WriteLine($"Error: Package '{options.ReadmePackageId}' is not a direct dependency of '{options.ProjectPath}'.");
            return 1;
        }

        var document = await ReadBestPackageDocumentAsync(dependency, options, context);
        if (document == null)
        {
            Console.Error.WriteLine($"Error: Package '{dependency.PackageName}' does not contain a readme file.");
            return 1;
        }

        InfoTracker.SetDetail("readme", $"{document.Path} ({document.Size.ToString(CultureInfo.InvariantCulture)} B)");
        var output = options.JsonOutput
            ? JsonSerializer.Serialize(document, ProjectCommandJsonContext.Default.ProjectPackageDocument)
            : options.Jsonl
                ? JsonSerializer.Serialize(document, ProjectCommandCompactJsonContext.Default.ProjectPackageDocument) + Environment.NewLine
                : document.Content;

        WriteOutput(output, options.OutputPath);
        return 0;
    }

    private static async Task<ProjectPackageDocument?> ReadBestPackageDocumentAsync(
        ProjectPackageReference dependency,
        ProjectOptions options,
        CommandContext context)
    {
        var fromProjectAssets = ReadBestPackageDocumentFromDirectory(dependency, options.ContentScope);
        if (fromProjectAssets != null)
            return fromProjectAssets;

        PackageExtractionResult? resolution = null;
        try
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(
                context.HttpClient,
                dependency.PackageName,
                context.Logger.Log,
                sourceOptions: options.SourceOptions,
                version: dependency.Version);

            if (!outcome.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                return null;
            }

            resolution = outcome.Result!;
            return ReadBestPackageDocumentFromDirectory(
                dependency with
                {
                    Version = resolution.Version ?? dependency.Version,
                    PackagePath = resolution.ExtractPath
                },
                options.ContentScope);
        }
        finally
        {
            if (resolution is { FromCache: false, TempDir: not null } && Directory.Exists(resolution.TempDir))
            {
                try
                {
                    Directory.Delete(resolution.TempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static ProjectPackageDocument? ReadBestPackageDocumentFromDirectory(
        ProjectPackageReference dependency,
        PackageFileContentScope scope)
    {
        if (string.IsNullOrWhiteSpace(dependency.PackagePath) || !Directory.Exists(dependency.PackagePath))
            return null;

        var declaredReadme = ReadDeclaredReadme(dependency.PackagePath);
        var readme = PackageFileLister.ResolvePackageReadme(dependency.PackagePath, declaredReadme);
        if (readme == null)
            return null;

        var fullPath = Path.Combine(dependency.PackagePath, readme.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return null;

        var content = GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(
            MarkdownContent.ApplyScope(File.ReadAllText(fullPath), scope));
        return new ProjectPackageDocument(
            dependency.PackageName,
            dependency.Version,
            readme,
            new FileInfo(fullPath).Length,
            content);
    }

    private static string? ReadDeclaredReadme(string packagePath)
    {
        var nuspecFiles = Directory.GetFiles(packagePath, "*.nuspec", SearchOption.TopDirectoryOnly);
        return nuspecFiles.Length == 0
            ? null
            : NuspecParser.Parse(nuspecFiles[0]).ReadmeFile;
    }

    private static void WriteOutput(string output, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            File.WriteAllText(outputPath, output);
        else
            Console.Write(output);
    }

    private static string EscapeMarkdownTableCell(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

internal sealed record ProjectAgentsIndexRow(
    string Package,
    string Version,
    string Name,
    string Description,
    string Path);

internal sealed record ProjectPackageDocument(
    string Package,
    string Version,
    string Path,
    long Size,
    string Content);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectAgentsIndexRow))]
[JsonSerializable(typeof(ProjectAgentsIndexRow[]))]
[JsonSerializable(typeof(ProjectPackageDocument))]
internal partial class ProjectCommandJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectAgentsIndexRow))]
[JsonSerializable(typeof(ProjectPackageDocument))]
internal partial class ProjectCommandCompactJsonContext : JsonSerializerContext
{
}
