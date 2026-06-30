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
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

public class ProjectCommand
{
    public const string Name = "project";
    private static readonly string[] ProjectSectionNames = [PackageSections.PackageReadme];
    private static readonly string[] ProjectGroundingColumnNames =
    [
        "Package",
        "Version",
        "Kind",
        "Path",
        "Size",
        "Name",
        "Description"
    ];

    public static async Task<int> ExecuteAsync(ProjectOptions options)
    {
        if (!ValidateOptions(options))
            return 1;

        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select,
            ProjectSectionNames,
            infoSections: [],
            ProjectCategoryMap());
        if (SelectOutput.WriteUnresolved(selectResult))
            return 1;

        if (options.Discover != null)
        {
            return DiscoverOutput.Execute(
                options.Discover,
                ProjectDiscoverySchema(),
                tree: options.Tree,
                json: options.JsonOutput,
                tsv: options.Tsv,
                jsonl: options.Jsonl,
                markdown: !options.OneLine && !options.JsonOutput,
                sectionCategories: ProjectCategoryMap());
        }

        if (options.Count && !CountOutput.ValidateSingleSection(selectResult.Sections))
            return 1;

        if ((options.Print || options.PrintAll) && !ValidateProjectPrintSelection(selectResult.Sections))
            return 1;

        if ((options.Columns is { Length: > 0 } || options.Fields is { Length: > 0 })
            && !ValidateProjectProjectionOptions())
        {
            return 1;
        }

        if (options.Schema && options.Discover == null)
        {
            Console.Error.WriteLine("Error: --schema requires -D/--discover.");
            return 1;
        }

        var sectionMode = selectResult.Sections is { Count: > 0 };
        var legacyMode = options.AgentsIndex || options.ReadmePackageId != null;
        if (sectionMode && legacyMode)
        {
            Console.Error.WriteLine("Error: -S/--select cannot be combined with --agents-index or --readme.");
            return 1;
        }

        if (!sectionMode && !legacyMode)
        {
            Console.Error.WriteLine("Error: Specify exactly one project mode: -S Grounding, --agents-index, or --readme <package-id>.");
            return 1;
        }

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

        if (sectionMode)
            return WriteGrounding(dependencies, options);

        return await WriteReadmeAsync(dependencies, options, context);
    }

    private static bool ValidateOptions(ProjectOptions options)
    {
        if (options.FrontmatterRequested && options.BodyRequested)
        {
            Console.Error.WriteLine("Error: --frontmatter/--yaml-header cannot be combined with --body.");
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

        if ((options.Print || options.PrintAll) && options.ReadmePackageId != null)
        {
            Console.Error.WriteLine("Error: --print/--print-all cannot be combined with --readme.");
            return false;
        }

        if ((options.Print || options.PrintAll) && options.AgentsIndex)
        {
            Console.Error.WriteLine("Error: --print/--print-all cannot be combined with --agents-index.");
            return false;
        }

        if (options.Print && options.PrintAll)
        {
            Console.Error.WriteLine("Error: --print cannot be combined with --print-all.");
            return false;
        }

        if ((options.Print || options.PrintAll) && options.Rows is not null)
        {
            Console.Error.WriteLine("Error: --rows cannot be combined with --print or --print-all; use -n N without --rows to choose a printed row.");
            return false;
        }

        if ((options.FrontmatterRequested || options.BodyRequested)
            && !options.Print
            && !options.PrintAll
            && options.ReadmePackageId == null
            && !options.AgentsIndex)
        {
            Console.Error.WriteLine("Error: --frontmatter/--body require --print or --readme.");
            return false;
        }

        return true;
    }

    private static bool ValidateProjectPrintSelection(HashSet<string>? sections)
    {
        if (sections is { Count: 1 } && sections.Contains(PackageSections.PackageReadme))
            return true;

        Console.Error.WriteLine("Error: --print requires -S/--select to match exactly one printable section.");
        return false;
    }

    private static DocumentSchema ProjectDiscoverySchema()
    {
        var schema = new DocumentSchema();
        schema.Add(PackageSections.PackageReadme, "column", ProjectGroundingColumnNames);
        return schema;
    }

    private static IReadOnlyDictionary<string, string[]> ProjectCategoryMap()
        => new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [SelectResolver.AllSelector] = ProjectSectionNames,
            [SelectResolver.InfoSelector] = []
        };

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

    private static int WriteGrounding(IReadOnlyList<ProjectPackageReference> dependencies, ProjectOptions options)
    {
        var rows = dependencies
            .Select(CreateGroundingRow)
            .ToList();

        if (options.Print || options.PrintAll || options.Bare)
            return PrintFirstGroundingDocument(rows, options);

        var output = options.Count
            ? rows.Count.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
            : options.JsonOutput
                ? JsonSerializer.Serialize(rows.ToArray(), ProjectCommandJsonContext.Default.ProjectGroundingRowArray)
                : options.Jsonl
                    ? RenderGroundingJsonl(rows)
                    : options.OneLine
                        ? RenderGroundingTable(rows, options)
                        : RenderGroundingMarkdown(rows);

        WriteOutput(output, options.OutputPath);
        return 0;
    }

    private static ProjectGroundingRow CreateGroundingRow(ProjectPackageReference dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency.PackagePath) || !Directory.Exists(dependency.PackagePath))
            return EmptyGroundingRow(dependency);

        var declaredReadme = ReadDeclaredReadme(dependency.PackagePath);
        var readme = PackageFileLister.ResolvePackageReadme(dependency.PackagePath, declaredReadme);
        if (readme == null)
            return EmptyGroundingRow(dependency);

        var fullPath = Path.Combine(dependency.PackagePath, readme.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return EmptyGroundingRow(dependency);

        var content = File.ReadAllText(fullPath);
        var frontmatter = MarkdownContent.ParseYamlFrontmatter(content);
        frontmatter.TryGetValue("name", out var name);
        frontmatter.TryGetValue("description", out var description);

        return new ProjectGroundingRow(
            dependency.PackageName,
            dependency.Version,
            ClassifyGroundingKind(readme, declaredReadme),
            readme,
            new FileInfo(fullPath).Length,
            name ?? "",
            description ?? "",
            fullPath);
    }

    private static ProjectGroundingRow EmptyGroundingRow(ProjectPackageReference dependency)
        => new(
            dependency.PackageName,
            dependency.Version,
            Kind: "",
            Path: "",
            Size: 0,
            Name: "",
            Description: "",
            FullPath: null);

    private static string ClassifyGroundingKind(string path, string? declaredReadme)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase))
            return "agents";
        if (fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase))
            return "readme";
        if (fileName.Equals("PACKAGE.md", StringComparison.OrdinalIgnoreCase))
            return "package";
        if (!string.IsNullOrWhiteSpace(declaredReadme)
            && path.Equals(declaredReadme, StringComparison.OrdinalIgnoreCase))
        {
            return "declared";
        }

        return "markdown";
    }

    private static string RenderGroundingJsonl(IEnumerable<ProjectGroundingRow> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder.AppendLine(JsonSerializer.Serialize(row, ProjectCommandCompactJsonContext.Default.ProjectGroundingRow));
        return builder.ToString();
    }

    private static string RenderGroundingTable(IReadOnlyList<ProjectGroundingRow> rows, ProjectOptions options)
        => OutputFormatter.RenderTable(!options.NoHeader, (writer, formatter) =>
        {
            var markoutWriter = new MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
            markoutWriter.WriteTable(
                ["Package", "Version", "Kind", "Path", "Size", "Name", "Description"],
                ["package", "version", "kind", "path", "size", "name", "description"],
                rows.Select(row => new[]
                {
                    row.Package,
                    row.Version,
                    row.Kind,
                    row.Path,
                    row.Size.ToString(CultureInfo.InvariantCulture),
                    row.Name,
                    row.Description
                }).ToArray());
            markoutWriter.Flush();
        });

    private static string RenderGroundingMarkdown(IReadOnlyList<ProjectGroundingRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Grounding");
        builder.AppendLine();
        builder.AppendLine("| Package | Version | Kind | Path | Size | Name | Description |");
        builder.AppendLine("| ------- | ------- | ---- | ---- | ---: | ---- | ----------- |");
        foreach (var row in rows)
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdownTableCell(row.Package));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Version));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Kind));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Path));
            builder.Append(" | ");
            builder.Append(row.Size.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Name));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Description));
            builder.AppendLine(" |");
        }

        return builder.ToString();
    }

    private static int PrintFirstGroundingDocument(IReadOnlyList<ProjectGroundingRow> rows, ProjectOptions options)
    {
        var documents = rows
            .Select((row, index) => CreatePrintableGroundingDocument(row, index + 1, options))
            .Where(document => document is not null)
            .Cast<PrintableDocument>()
            .ToList();

        return PrintProjectionOutput.Write(
            documents,
            new PrintProjectionOptions(
                options.PrintAll,
                options.Bare && !options.Print && !options.PrintAll ? 1 : options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.Bare,
                options.OutputPath));
    }

    private static PrintableDocument? CreatePrintableGroundingDocument(ProjectGroundingRow row, int rowNumber, ProjectOptions options)
    {
        if (row.FullPath == null || !File.Exists(row.FullPath))
            return null;

        var content = GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(
            MarkdownContent.ApplyScope(File.ReadAllText(row.FullPath), options.ContentScope));
        return new PrintableDocument(
            rowNumber,
            PackageSections.PackageReadme,
            $"{row.Package} {row.Path}",
            row.Path,
            null,
            content);
    }

    private static bool ValidateProjectProjectionOptions()
    {
        Console.Error.WriteLine("Error: project does not currently support --columns or --fields.");
        return false;
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

internal sealed record ProjectGroundingRow(
    string Package,
    string Version,
    string Kind,
    string Path,
    long Size,
    string Name,
    string Description,
    [property: JsonIgnore] string? FullPath);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectAgentsIndexRow))]
[JsonSerializable(typeof(ProjectAgentsIndexRow[]))]
[JsonSerializable(typeof(ProjectPackageDocument))]
[JsonSerializable(typeof(ProjectGroundingRow))]
[JsonSerializable(typeof(ProjectGroundingRow[]))]
internal partial class ProjectCommandJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectAgentsIndexRow))]
[JsonSerializable(typeof(ProjectPackageDocument))]
[JsonSerializable(typeof(ProjectGroundingRow))]
internal partial class ProjectCommandCompactJsonContext : JsonSerializerContext
{
}
