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

using ILInspector.CSharp;

namespace DotnetInspector.Commands;

public class ProjectCommand
{
    public const string Name = "project";
    private const string ProjectSkillsSection = "Skills";
    private static readonly string[] ProjectSectionNames = [ProjectSkillsSection];
    private static readonly string[] ProjectSkillColumnNames =
    [
        "Package",
        "Version",
        "Path",
        "Size",
        "Name",
        "Description"
    ];
    private static readonly string[] ProjectReadmeCandidates = ["README.md", "PROJECT.md"];

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
                markdown: !options.Tabular && !options.JsonOutput,
                sectionCategories: ProjectCategoryMap());
        }

        if (options.Count && !CountOutput.ValidateSingleSection(selectResult.Sections))
            return 1;

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (shapeCount > 1)
        {
            Console.Error.WriteLine("Error: specify only one of --value, --urls, or --paths.");
            return 1;
        }

        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            if (!ShapeProjectionOutput.ValidateSingleSection(selectResult.Sections, optionName))
                return 1;
            if (options.Count || options.Print)
            {
                Console.Error.WriteLine($"Error: {optionName} cannot be combined with --count or --print.");
                return 1;
            }
            if (options.Rows is not null)
            {
                Console.Error.WriteLine($"Error: --rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N|first|last to select a projected row.");
                return 1;
            }
        }

        if (options.JsonArray && shapeCount == 0 && !options.Print)
        {
            Console.Error.WriteLine("Error: --json-array requires --value, --urls, --paths, or --print.");
            return 1;
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            Console.Error.WriteLine("Error: --json-array cannot be combined with --json or --jsonl.");
            return 1;
        }

        if ((options.Columns is { Length: > 0 } || options.Fields is { Length: > 0 })
            && shapeCount == 0
            && !ValidateProjectProjectionOptions())
        {
            return 1;
        }

        if (options.Print && !ValidateProjectPrintSelection(selectResult.Sections))
            return 1;

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
            Console.Error.WriteLine("Error: Specify exactly one project mode: -S Skills, --agents-index, or --readme <package-id>.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        if (!ProjectAssetsParser.TryFindAssets(options.ProjectPath, out var assetsPath, out var assetsStatus))
        {
            Console.Error.WriteLine($"Error: {ProjectAssetsParser.DescribeMissingAssets(options.ProjectPath, assetsStatus)}");
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
            return WriteSkills(assetsPath, options, log: null);

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

        if (options.ReadmePackageId != null && options.Tabular && !options.Jsonl)
        {
            Console.Error.WriteLine("Error: project --readme supports raw text, --json, or --jsonl; it cannot be combined with --table or --tsv.");
            return false;
        }

        if (options.Print && options.ReadmePackageId != null)
        {
            Console.Error.WriteLine("Error: --print cannot be combined with --readme.");
            return false;
        }

        if (options.Print && options.AgentsIndex)
        {
            Console.Error.WriteLine("Error: --print cannot be combined with --agents-index.");
            return false;
        }

        if (options.PrintRow is not null
            && !options.Print
            && !options.Value
            && !options.Urls
            && !options.Paths)
        {
            Console.Error.WriteLine("Error: --row requires --print, --value, --urls, or --paths.");
            return false;
        }

        if (options.Print && options.Rows is not null)
        {
            Console.Error.WriteLine("Error: --rows cannot be combined with --print; use --row N|first|last to choose a printed row.");
            return false;
        }

        if ((options.FrontmatterRequested || options.BodyRequested)
            && !options.Print
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
        if (sections is { Count: 1 } && sections.Contains(ProjectSkillsSection))
            return true;

        Console.Error.WriteLine("Error: --print requires -S/--select to match exactly one printable section.");
        return false;
    }

    private static DocumentSchema ProjectDiscoverySchema()
    {
        var schema = new DocumentSchema();
        schema.Add(ProjectSkillsSection, "column", ProjectSkillColumnNames);
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
                : options.Tabular
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

    private static int WriteSkills(string assetsPath, ProjectOptions options, Action<string>? log)
    {
        var rows = ProjectAssetsParser.ParsePackageFileEntries(
                assetsPath,
                options.Tfm,
                ["skills/SKILL.md", "skills/**/SKILL.md"],
                log)
            .Select(CreateSkillRow)
            .Where(row => row is not null)
            .Cast<ProjectSkillRow>()
            .ToList();

        if (options.Value || options.Urls || options.Paths)
            return WriteSkillShapeProjection(rows, options);

        if (options.Print || options.Bare)
            return PrintSkillDocument(rows, options);

        if (options.Count)
            ProjectionAudit.MarkHonored(ProjectionAudit.Count);

        var output = options.Count
            ? rows.Count.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
            : options.JsonOutput
                ? JsonSerializer.Serialize(rows.ToArray(), ProjectCommandJsonContext.Default.ProjectSkillRowArray)
                : options.Jsonl
                    ? RenderSkillJsonl(rows)
                    : options.Tabular
                        ? RenderSkillTable(rows, options)
                        : RenderSkillMarkdown(rows);

        WriteOutput(output, options.OutputPath);
        return 0;
    }

    private static ProjectSkillRow? CreateSkillRow(ProjectPackageFileEntry file)
    {
        if (string.IsNullOrWhiteSpace(file.FullPath) || !File.Exists(file.FullPath))
            return null;

        var content = File.ReadAllText(file.FullPath);
        var frontmatter = MarkdownContent.ParseYamlFrontmatter(content);
        frontmatter.TryGetValue("name", out var name);
        frontmatter.TryGetValue("description", out var description);

        return new ProjectSkillRow(
            file.PackageName,
            file.Version,
            file.Path,
            new FileInfo(file.FullPath).Length,
            name ?? "",
            description ?? "",
            file.FullPath);
    }

    private static string RenderSkillJsonl(IEnumerable<ProjectSkillRow> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder.AppendLine(JsonSerializer.Serialize(row, ProjectCommandCompactJsonContext.Default.ProjectSkillRow));
        return builder.ToString();
    }

    private static string RenderSkillTable(IReadOnlyList<ProjectSkillRow> rows, ProjectOptions options)
        => OutputFormatter.RenderTable(!options.NoHeader, (writer, formatter) =>
        {
            var markoutWriter = new MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
            markoutWriter.WriteTable(
                ["Package", "Version", "Path", "Size", "Name", "Description"],
                ["package", "version", "path", "size", "name", "description"],
                rows.Select(row => new[]
                {
                    row.Package,
                    row.Version,
                    row.Path,
                    row.Size.ToString(CultureInfo.InvariantCulture),
                    row.Name,
                    row.Description
                }).ToArray());
            markoutWriter.Flush();
        });

    private static string RenderSkillMarkdown(IReadOnlyList<ProjectSkillRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Skills");
        builder.AppendLine();
        if (rows.Count == 0)
        {
            builder.AppendLine("No skills found in the restored direct package dependencies.");
            return builder.ToString();
        }
        builder.AppendLine("| Package | Version | Path | Size | Name | Description |");
        builder.AppendLine("| ------- | ------- | ---- | ---: | ---- | ----------- |");
        foreach (var row in rows)
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdownTableCell(row.Package));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownTableCell(row.Version));
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

    private static int PrintSkillDocument(IReadOnlyList<ProjectSkillRow> rows, ProjectOptions options)
    {
        var documents = rows
            .Select((row, index) => CreatePrintableSkillDocument(row, index + 1, options))
            .Where(document => document is not null)
            .Cast<PrintableDocument>()
            .ToList();

        return PrintProjectionOutput.Write(
            documents,
            new PrintProjectionOptions(
                options.Bare && !options.Print ? RowSelector.FromIndex(1) : options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                options.OutputPath));
    }

    private static int WriteSkillShapeProjection(IReadOnlyList<ProjectSkillRow> rows, ProjectOptions options)
    {
        var kind = ShapeProjectionOutput.GetKind(options.Value, options.Urls, options.Paths);
        List<ShapeProjectionRow> projected = [];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Paths => row.Path,
                ShapeProjectionKind.Value => SelectSkillValue(row, options),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
                continue;
            projected.Add(new ShapeProjectionRow(i + 1, ProjectSkillsSection, value, Label: row.Package, Path: row.Path));
        }

        return ShapeProjectionOutput.Write(
            projected,
            new ShapeProjectionOptions(kind, options.PrintRow, options.JsonOutput, options.Jsonl, options.JsonArray));
    }

    private static string? SelectSkillValue(ProjectSkillRow row, ProjectOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "package" => row.Package,
            "version" => row.Version,
            "path" => row.Path,
            "size" => row.Size.ToString(CultureInfo.InvariantCulture),
            "name" => row.Name,
            "description" => row.Description,
            _ => row.Path
        };
    }

    private static PrintableDocument? CreatePrintableSkillDocument(ProjectSkillRow row, int rowNumber, ProjectOptions options)
    {
        if (row.FullPath == null || !File.Exists(row.FullPath))
            return null;

        var content = GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(
            MarkdownContent.ApplyScope(File.ReadAllText(row.FullPath), options.ContentScope));
        return new PrintableDocument(
            rowNumber,
            ProjectSkillsSection,
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

        var readme = ResolveProjectReadme(dependency.PackagePath);
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

    private static string? ResolveProjectReadme(string packagePath)
    {
        foreach (var candidate in ProjectReadmeCandidates)
        {
            var match = Directory.EnumerateFiles(packagePath, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file => Path.GetFileName(file).Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return Path.GetRelativePath(packagePath, match).Replace('\\', '/');
        }

        return null;
    }

    private static void WriteOutput(string output, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            File.WriteAllText(outputPath, output);
        else
            Console.Write(output);
    }

    /// <summary>
    /// Escapes a table cell for Markdown. This handles the pipe and the line
    /// break only; rendering hazards are contained upstream on the row records,
    /// which is the one place both of this command's table writers read from.
    /// </summary>
    private static string EscapeMarkdownTableCell(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

/// <summary>
/// A row of the agents index. Every field is display text, and every field but
/// the version came out of a package the user did not write, so each is
/// contained here at the row boundary rather than at one of the two writers
/// that render it (issue #3319).
/// </summary>
internal sealed record ProjectAgentsIndexRow(
    string Package,
    string Version,
    string Name,
    string Description,
    string Path)
{
    public string Package { get; init; } = CSharpIdentifier.ContainRenderedText(Package);
    public string Version { get; init; } = CSharpIdentifier.ContainRenderedText(Version);
    public string Name { get; init; } = CSharpIdentifier.ContainRenderedText(Name);
    public string Description { get; init; } = CSharpIdentifier.ContainRenderedText(Description);
    public string Path { get; init; } = CSharpIdentifier.ContainRenderedText(Path);
}

/// <summary>
/// A document read out of a package. <c>Content</c> is deliberately left raw:
/// the point of <c>--print</c> is to show the file as it is, and containing it
/// would misrepresent the bytes on disk. The identifying fields around it are
/// contained, because those are the tool's own framing (issue #3319).
/// </summary>
internal sealed record ProjectPackageDocument(
    string Package,
    string Version,
    string Path,
    long Size,
    string Content)
{
    // Redeclared in full, in constructor order; see ProjectSkillRow.
    public string Package { get; init; } = CSharpIdentifier.ContainRenderedText(Package);
    public string Version { get; init; } = CSharpIdentifier.ContainRenderedText(Version);
    public string Path { get; init; } = CSharpIdentifier.ContainRenderedText(Path);
    public long Size { get; init; } = Size;
    public string Content { get; init; } = Content;
}

/// <summary>
/// A row of the skills listing.
/// </summary>
/// <remarks>
/// <c>FullPath</c> is deliberately left raw: it is the path this command opens
/// to read the skill, so containing it would break file access. It is also
/// <see cref="JsonIgnoreAttribute"/>d and never rendered, so it is not a
/// display channel. <c>Path</c> is the rendered one and is contained.
/// </remarks>
internal sealed record ProjectSkillRow(
    string Package,
    string Version,
    string Path,
    long Size,
    string Name,
    string Description,
    string? FullPath)
{
    // Every positional property is redeclared, in constructor order, including
    // the ones that need no containment: a record's compiler-generated
    // properties are emitted before its explicitly declared ones, so
    // redeclaring only some of them reorders the rendered columns and the
    // serialized keys.
    public string Package { get; init; } = CSharpIdentifier.ContainRenderedText(Package);
    public string Version { get; init; } = CSharpIdentifier.ContainRenderedText(Version);
    public string Path { get; init; } = CSharpIdentifier.ContainRenderedText(Path);
    public long Size { get; init; } = Size;
    public string Name { get; init; } = CSharpIdentifier.ContainRenderedText(Name);
    public string Description { get; init; } = CSharpIdentifier.ContainRenderedText(Description);
    [JsonIgnore]
    public string? FullPath { get; init; } = FullPath;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectAgentsIndexRow))]
[JsonSerializable(typeof(ProjectAgentsIndexRow[]))]
[JsonSerializable(typeof(ProjectPackageDocument))]
[JsonSerializable(typeof(ProjectSkillRow))]
[JsonSerializable(typeof(ProjectSkillRow[]))]
internal partial class ProjectCommandJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectAgentsIndexRow))]
[JsonSerializable(typeof(ProjectPackageDocument))]
[JsonSerializable(typeof(ProjectSkillRow))]
internal partial class ProjectCommandCompactJsonContext : JsonSerializerContext
{
}
