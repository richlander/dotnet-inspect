using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Services;
using ILInspector.CSharp;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Commands;

public class ProjectCommand
{
    private enum ProjectDocumentKind
    {
        Skill,
        Readme,
    }

    private enum ProjectSkillReadFailure
    {
        None,
        MissingFile,
        InvalidName,
        InvalidDescription,
    }

    public const string Name = "project";
    internal const string ProjectSkillsSection = "Skills";
    internal const string ProjectReadmeSection = "Package README file";

    private const string ProjectTitle = "Restored Project Package Documents";
    private const string ProjectDescription =
        "Package-authored documents from the restored project's direct dependencies.";

    private static readonly string[] ProjectSectionNames =
    [
        ProjectSkillsSection,
        ProjectReadmeSection,
    ];

    private static readonly IReadOnlyDictionary<string, string[]> NoCategories =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public static Task<int> ExecuteAsync(ProjectOptions options)
        => Task.FromResult(Execute(options));

    private static int Execute(ProjectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!ValidateOptions(options))
            return 1;

        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            ProjectSectionNames,
            infoSections: [ProjectSkillsSection],
            NoCategories,
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;

        DocumentSchema schema = ProjectDiscoverySchema();
        if (options.Discover is not null)
        {
            return DiscoverOutput.Execute(
                options.Discover,
                schema,
                projection: options,
                tree: options.Tree,
                json: options.Format == OutputFormat.Json,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                markdown: options.Format == OutputFormat.Markdown,
                plainText: options.Format == OutputFormat.PlainText,
                sectionCategories: NoCategories,
                rootLabel: ProjectTitle);
        }

        if (options.Tree)
        {
            CommandError.Write(
                "--tree is supported only with -D/--discover for project schema.");
            return 1;
        }

        int shapeCount = ShapeProjectionOutput.ActiveShapeCount(
            options.Value,
            options.Urls,
            options.Paths);
        if (shapeCount == 1
            && !ShapeProjectionOutput.ValidateSingleSection(
                selection.Sections,
                options.Value ? "--value"
                    : options.Urls ? "--urls"
                    : "--paths"))
        {
            return 1;
        }

        if ((options.Print || options.Bare)
            && selection.Sections is not { Count: 1 })
        {
            CommandError.Write(
                "--print requires -S/--select to match exactly one printable "
                + "section.");
            return 1;
        }

        if (selection.Sections is not { Count: > 0 } selectedNames)
        {
            CommandError.Write(
                "Select at least one project section: -S Skills or "
                + "-S \"Package README file\".");
            return 1;
        }

        string[] orderedNames =
        [
            .. ProjectSectionNames.Where(selectedNames.Contains),
        ];

        string[]? projectedColumns = ResolveProjectedColumns(options);
        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                selectedNames,
                fields: null,
                columns: projectedColumns))
        {
            return 1;
        }

        if (options.Format
                is OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl
            && orderedNames.Length != 1
            && !options.Count)
        {
            CommandError.Write(
                $"Selection matches {orderedNames.Length} sections: "
                + $"{string.Join(", ", orderedNames)}.");
            CommandError.WriteBlankLine();
            CommandError.WriteLine(
                "--table, --tsv, and --jsonl display one section at a time.");
            CommandError.WriteLine(
                "Use -S with a specific section name, or --markdown/--json "
                + "for multi-section output.");
            return 1;
        }

        if (!ProjectAssetsParser.TryFindAssets(
                options.ProjectPath,
                out string? assetsPath,
                out ProjectAssetsStatus assetsStatus)
            || assetsPath is null)
        {
            CommandError.Write(
                ProjectAssetsParser.DescribeMissingAssets(
                    options.ProjectPath,
                    assetsStatus));
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        context.Logger.Log($"Using assets: {assetsPath}");
        List<ProjectPackageReference> dependencies =
            ProjectAssetsParser.ParsePackageReferences(
                assetsPath,
                options.Tfm,
                context.Logger.Log);
        if (dependencies.Count == 0)
        {
            CommandError.Write(
                $"No direct package references found in '{assetsPath}'.");
            return 1;
        }

        var sections = new List<ProjectSection>(orderedNames.Length);
        foreach (string name in orderedNames)
        {
            if (!TryCreateSection(
                    name,
                    assetsPath,
                    options.Tfm,
                    context.Logger.Log,
                    out ProjectSection? section))
            {
                return 1;
            }

            sections.Add(section);
        }

        if (shapeCount == 1)
            return WriteShapeProjection(sections[0], options, projectedColumns);

        if (options.Print || options.Bare)
            return PrintDocument(sections[0], options);

        if (options.Count)
            return WriteCounts(sections, orderedNames, options);

        return RenderSections(sections, projectedColumns, options);
    }

    private static bool ValidateOptions(ProjectOptions options)
    {
        if (options.FrontmatterRequested && options.BodyRequested)
        {
            CommandError.Write(
                "--frontmatter/--yaml-header cannot be combined with --body.");
            return false;
        }

        int shapeCount = ShapeProjectionOutput.ActiveShapeCount(
            options.Value,
            options.Urls,
            options.Paths);
        if (shapeCount > 1)
        {
            CommandError.Write(
                "specify only one of --value, --urls, or --paths.");
            return false;
        }

        if (shapeCount == 1 && (options.Count || options.Print || options.Bare))
        {
            string optionName = options.Value ? "--value"
                : options.Urls ? "--urls"
                : "--paths";
            CommandError.Write(
                $"{optionName} cannot be combined with --count, --print, "
                + "or --bare.");
            return false;
        }

        if ((options.Print || options.Bare) && options.Count)
        {
            CommandError.Write(
                "--count cannot be combined with --print or --bare.");
            return false;
        }

        if (options.PrintRow is not null
            && !options.Print
            && !options.Bare
            && shapeCount == 0)
        {
            CommandError.Write(
                "--row requires --print, --bare, --value, --urls, or --paths.");
            return false;
        }

        if ((options.FrontmatterRequested || options.BodyRequested)
            && !options.Print
            && !options.Bare)
        {
            CommandError.Write(
                "--frontmatter/--body require --print or --bare.");
            return false;
        }

        if (options.JsonArray
            && shapeCount == 0
            && !options.Print
            && !options.Bare)
        {
            CommandError.Write(
                "--json-array requires --value, --urls, --paths, --print, "
                + "or --bare.");
            return false;
        }

        if (options.JsonArray
            && options.Format is OutputFormat.Json or OutputFormat.Jsonl)
        {
            CommandError.Write(
                "--json-array cannot be combined with --json or --jsonl.");
            return false;
        }

        if (options.Schema && options.Discover is null)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return false;
        }

        return true;
    }

    private static bool TryCreateSection(
        string name,
        string assetsPath,
        string? targetFramework,
        Action<string>? log,
        [NotNullWhen(true)] out ProjectSection? section)
    {
        if (name.Equals(ProjectSkillsSection, StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateSkillsSection(
                assetsPath,
                targetFramework,
                log,
                out section);
        }

        if (name.Equals(ProjectReadmeSection, StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateReadmeSection(
                assetsPath,
                targetFramework,
                log,
                out section);
        }

        throw new InvalidOperationException($"Unknown project section '{name}'.");
    }

    private static bool TryCreateSkillsSection(
        string assetsPath,
        string? targetFramework,
        Action<string>? log,
        out ProjectSection? section)
    {
        var documents = new List<ProjectDocumentRow>();
        foreach (ProjectPackageFileEntry file
                 in ProjectAssetsParser.ParsePackageFileEntries(
                     assetsPath,
                     targetFramework,
                     ["skills/SKILL.md", "skills/**/SKILL.md"],
                     log))
        {
            ProjectSkillReadFailure failure = CreateSkillRow(
                file,
                out ProjectDocumentRow? row);
            if (failure != ProjectSkillReadFailure.None)
            {
                CommandError.Write(failure switch
                {
                    ProjectSkillReadFailure.MissingFile =>
                        "A restored package skill listed in "
                        + "project.assets.json is missing from the package cache.",
                    ProjectSkillReadFailure.InvalidName =>
                        "A restored package skill must declare an Agent "
                        + "Skills-compliant name that matches its containing "
                        + "directory.",
                    _ =>
                        "A restored package skill must declare an Agent "
                        + "Skills-compliant description of 1 to 1024 characters.",
                });
                section = null;
                return false;
            }

            if (row is null)
            {
                throw new InvalidOperationException(
                    "A successful skill row projection must produce a row.");
            }

            documents.Add(row);
        }

        section = new ProjectSection(
            ProjectSkillsSection,
            "Valid Agent Skills documents from direct package dependencies.",
            ["Package", "Version", "Path", "Size", "Name", "Description"],
            ["package", "version", "path", "size", "name", "description"],
            documents,
            "No skills found in the restored direct package dependencies.");
        return true;
    }

    private static ProjectSkillReadFailure CreateSkillRow(
        ProjectPackageFileEntry file,
        out ProjectDocumentRow? row)
    {
        row = null;
        if (string.IsNullOrWhiteSpace(file.FullPath)
            || !File.Exists(file.FullPath))
        {
            return ProjectSkillReadFailure.MissingFile;
        }

        string content = File.ReadAllText(file.FullPath);
        IReadOnlyDictionary<string, string> frontmatter =
            MarkdownContent.ParseYamlFrontmatter(content);
        if (!TryGetSkillName(file.Path, frontmatter, out string name))
            return ProjectSkillReadFailure.InvalidName;

        frontmatter.TryGetValue("description", out string? description);
        if (!IsAgentSkillDescription(description))
            return ProjectSkillReadFailure.InvalidDescription;

        row = new ProjectDocumentRow(
            ProjectDocumentKind.Skill,
            file.PackageName,
            file.Version,
            file.Path,
            new FileInfo(file.FullPath).Length,
            ContainSkillMetadata(name),
            ContainSkillMetadata(
                FoldSkillDescriptionLineEndings(description ?? "")),
            file.FullPath);
        return ProjectSkillReadFailure.None;
    }

    private static bool TryCreateReadmeSection(
        string assetsPath,
        string? targetFramework,
        Action<string>? log,
        out ProjectSection? section)
    {
        List<ProjectPackageFileEntry> candidates =
            ProjectAssetsParser.ParsePackageFileEntries(
                assetsPath,
                targetFramework,
                ["README.md"],
                log);
        var documents = new List<ProjectDocumentRow>();
        foreach (IGrouping<string, ProjectPackageFileEntry> group
                 in candidates.GroupBy(
                     file => $"{file.PackageName}\0{file.Version}",
                     StringComparer.OrdinalIgnoreCase))
        {
            ProjectPackageFileEntry file = group
                .OrderBy(
                    candidate => candidate.Path.Equals(
                        "README.md",
                        StringComparison.Ordinal)
                        ? 0
                        : 1)
                .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
                .First();
            if (string.IsNullOrWhiteSpace(file.FullPath)
                || !File.Exists(file.FullPath))
            {
                CommandError.Write(
                    "A restored package README listed in project.assets.json "
                    + "is missing from the package cache.");
                section = null;
                return false;
            }

            documents.Add(new ProjectDocumentRow(
                ProjectDocumentKind.Readme,
                file.PackageName,
                file.Version,
                file.Path,
                new FileInfo(file.FullPath).Length,
                name: null,
                description: null,
                file.FullPath));
        }

        section = new ProjectSection(
            ProjectReadmeSection,
            "Root README.md documents from direct package dependencies.",
            ["Package", "Version", "Path", "Size"],
            ["package", "version", "path", "size"],
            documents,
            "No root README.md files found in the restored direct package "
            + "dependencies.");
        return true;
    }

    private static int WriteShapeProjection(
        ProjectSection section,
        ProjectOptions options,
        string[]? projectedColumns)
    {
        ShapeProjectionKind kind = ShapeProjectionOutput.GetKind(
            options.Value,
            options.Urls,
            options.Paths);
        int valueColumn = -1;
        if (kind == ShapeProjectionKind.Value)
        {
            if (!TryResolveValueColumn(
                    section,
                    projectedColumns,
                    out valueColumn))
            {
                return 1;
            }
        }

        var numberedDocuments =
            new List<(int Row, ProjectDocumentRow Document)>(
                section.Documents.Count);
        for (int index = 0; index < section.Documents.Count; index++)
            numberedDocuments.Add((index + 1, section.Documents[index]));

        var projected = new List<ShapeProjectionRow>();
        foreach ((int row, ProjectDocumentRow document) in
                 RowWindow.Apply(options.Rows, numberedDocuments))
        {
            string? value = kind switch
            {
                ShapeProjectionKind.Paths => document.Path,
                ShapeProjectionKind.Value => document.Cells[valueColumn],
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(value))
                continue;

            projected.Add(new ShapeProjectionRow(
                row,
                section.Name,
                value,
                Label: document.Package,
                Path: document.Path));
        }

        return ShapeProjectionOutput.Write(
            projected,
            new ShapeProjectionOptions(
                kind,
                options.PrintRow,
                options.Format == OutputFormat.Json,
                options.Format == OutputFormat.Jsonl,
                options.JsonArray,
                new ProjectionDestination(options.OutputPath, options.Rows)));
    }

    private static bool TryResolveValueColumn(
        ProjectSection section,
        string[]? projectedColumns,
        out int column)
    {
        if (projectedColumns is not { Length: > 0 })
        {
            column = Array.IndexOf(section.Ids, "path");
            return true;
        }

        if (projectedColumns.Length != 1)
        {
            CommandError.Write(
                "--value requires exactly one --fields/--columns entry.");
            column = -1;
            return false;
        }

        string requested = projectedColumns[0];
        column = Array.FindIndex(
            section.Ids,
            id => id.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (column < 0)
        {
            column = Array.FindIndex(
                section.Labels,
                label => label.Equals(
                    requested,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (column >= 0)
            return true;

        CommandError.Write(
            $"Column '{requested}' is not available in '{section.Name}'.");
        return false;
    }

    private static int PrintDocument(
        ProjectSection section,
        ProjectOptions options)
    {
        var printableRows = new List<PrintableRow>(section.Documents.Count);
        var documentByRow =
            new Dictionary<PrintableRow, ProjectDocumentRow>(
                ReferenceEqualityComparer.Instance);
        for (int index = 0; index < section.Documents.Count; index++)
        {
            ProjectDocumentRow document = section.Documents[index];
            var row = new PrintableRow(
                index + 1,
                section.Name,
                $"{document.Package} {document.Path}",
                document.Path,
                Url: null);
            printableRows.Add(row);
            documentByRow.Add(row, document);
        }

        IReadOnlyList<PrintableRow> visibleRows =
            RowWindow.Apply(options.Rows, printableRows);
        return PrintProjectionOutput.Write(
            visibleRows,
            row => ReadPrintableContent(documentByRow[row], options),
            new PrintProjectionOptions(
                options.PrintRow,
                options.Format == OutputFormat.Json,
                options.Format == OutputFormat.Jsonl,
                options.JsonArray,
                options.Bare,
                new ProjectionDestination(
                    options.OutputPath,
                    options.Rows,
                    ExactTransfer:
                        section.Name.Equals(
                            ProjectReadmeSection,
                            StringComparison.OrdinalIgnoreCase)
                        && HasUnstructuredOutputPath(options)
                        && options.ContentScope
                            == PackageFileContentScope.Full)));
    }

    private static PrintableContent ReadPrintableContent(
        ProjectDocumentRow document,
        ProjectOptions options)
    {
        byte[] exactContent = File.ReadAllBytes(document.FullPath);
        string content = MarkdownContent.ApplyScope(
            ReadText(exactContent),
            options.ContentScope);
        if (document.Kind == ProjectDocumentKind.Skill)
        {
            return PrintableContent.FromContainmentSelection(
                AgentSkillDocument.PrepareForOutput(
                    content,
                    normalizeGithubLinksToRaw: true));
        }

        return new PrintableContent(
            GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(content),
            ExactBytes:
                options.ContentScope == PackageFileContentScope.Full
                && HasUnstructuredOutputPath(options)
                    ? exactContent
                    : null);
    }

    private static bool HasUnstructuredOutputPath(ProjectOptions options)
        => !string.IsNullOrEmpty(options.OutputPath)
            && options.Format is not OutputFormat.Json
            && options.Format is not OutputFormat.Jsonl
            && !options.JsonArray;

    private static string ReadText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static int WriteCounts(
        IReadOnlyList<ProjectSection> sections,
        IReadOnlyList<string> orderedNames,
        ProjectOptions options)
    {
        if (sections.Count == 1)
        {
            CountOutput.WriteCount(
                RowWindow.Apply(
                    options.Rows,
                    sections[0].Documents).Count,
                options.OutputPath,
                options.Rows);
            return 0;
        }

        if (!CountOutput.ValidateMapFormat(options.Format, orderedNames))
            return 1;

        var projection = new CountProjection();
        foreach (ProjectSection section in sections)
        {
            projection.SetRows(
                section.Name,
                RowWindow.Apply(options.Rows, section.Documents).Count);
        }
        CountOutput.Write(
            projection,
            orderedNames,
            options.Format,
            options.NoHeader,
            options.OutputPath,
            options.Rows);
        return 0;
    }

    private static int RenderSections(
        IReadOnlyList<ProjectSection> sections,
        string[]? projectedColumns,
        ProjectOptions options)
    {
        ProjectSection[] rendered =
        [
            .. sections.Select(section => section with
            {
                Documents =
                [
                    .. RowWindow.Apply(options.Rows, section.Documents),
                ],
                WasLogicallyEmpty = section.Documents.Count == 0,
            }),
        ];

        var output = new StringWriter(CultureInfo.InvariantCulture)
        {
            NewLine = "\n",
        };
        if (options.Format == OutputFormat.Json)
        {
            OutputFormatter.WriteProjectedJson(
                output,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                    WriteDocument(
                        new MarkoutWriter(writer, formatter, writerOptions),
                        rendered,
                        includeDocumentHeading: rendered.Length > 1,
                        renderEmptyTables: true));
        }
        else if (options.Format
                     is OutputFormat.Table
                     or OutputFormat.Tsv
                     or OutputFormat.Jsonl)
        {
            ProjectSection section = rendered[0];
            OutputFormatter.WriteProjectedTable(
                output,
                showHeader: !options.NoHeader,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    var markout = new MarkoutWriter(
                        writer,
                        formatter,
                        writerOptions);
                    WriteTable(markout, section);
                    markout.Flush();
                });
        }
        else
        {
            MarkoutWriterOptions writerOptions =
                OutputFormatter.CreateProjectedWriterOptions(
                    projectedColumns,
                    fields: null);
            var writer = new MarkoutWriter(
                output,
                options.Format == OutputFormat.PlainText
                    ? new PlainTextFormatter()
                    : new MarkdownFormatter(),
                writerOptions);
            WriteDocument(
                writer,
                rendered,
                includeDocumentHeading: rendered.Length > 1);
            writer.Flush();
        }

        WriteOutput(output.ToString(), options.OutputPath);
        return 0;
    }

    private static void WriteDocument(
        MarkoutWriter writer,
        IEnumerable<ProjectSection> sections,
        bool includeDocumentHeading,
        bool renderEmptyTables = false)
    {
        if (includeDocumentHeading)
        {
            writer.WriteHeading(1, ProjectTitle);
            writer.WriteParagraph(ProjectDescription);
        }

        bool first = true;
        foreach (ProjectSection section in sections)
        {
            if (!first || includeDocumentHeading)
                writer.WriteBlankLine();
            first = false;
            writer.WriteHeading(2, section.Name);
            writer.WriteParagraph(section.Summary);
            if (section.Documents.Count == 0
                && section.WasLogicallyEmpty
                && !renderEmptyTables)
            {
                writer.WriteParagraph(section.EmptyText);
            }
            else
            {
                WriteTable(writer, section);
            }
        }
    }

    private static void WriteTable(
        MarkoutWriter writer,
        ProjectSection section)
        => writer.WriteTable(
            section.Labels,
            section.Ids,
            section.Documents.Select(document => document.Cells).ToArray());

    private static DocumentSchema ProjectDiscoverySchema()
    {
        var schema = new DocumentSchema();
        schema.Add(
            ProjectSkillsSection,
            "column",
            ["Package", "Version", "Path", "Size", "Name", "Description"]);
        schema.Add(
            ProjectReadmeSection,
            "column",
            ["Package", "Version", "Path", "Size"]);
        return schema;
    }

    private static string[]? ResolveProjectedColumns(ProjectOptions options)
    {
        if (options.Columns is not { Length: > 0 })
            return options.Fields is { Length: > 0 } ? options.Fields : null;
        if (options.Fields is not { Length: > 0 })
            return options.Columns;

        return
        [
            .. options.Columns
                .Concat(options.Fields)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static string FoldSkillDescriptionLineEndings(string value)
        => value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\n', ' ');

    private static string ContainSkillMetadata(string value)
        => new InertString(TextPolicy.Field, value)
            .ReplaceIfContainmentRequired(
                InertString.ContainmentRequiredPlaceholder)
            .ToString();

    private static bool TryGetSkillName(
        string packagePath,
        IReadOnlyDictionary<string, string> frontmatter,
        out string name)
    {
        string normalizedPath = packagePath.Replace('\\', '/');
        int fileSeparator = normalizedPath.LastIndexOf('/');
        if (fileSeparator <= 0)
        {
            name = "";
            return false;
        }

        string parentPath = normalizedPath[..fileSeparator].TrimEnd('/');
        int parentSeparator = parentPath.LastIndexOf('/');
        string directoryName = parentPath[(parentSeparator + 1)..];
        if (!frontmatter.TryGetValue("name", out name!))
            return false;

        return string.Equals(name, directoryName, StringComparison.Ordinal)
            && IsAgentSkillName(name);
    }

    private static bool IsAgentSkillName(string name)
    {
        if (name.Length is < 1 or > 64
            || name[0] == '-'
            || name[^1] == '-')
        {
            return false;
        }

        bool previousWasHyphen = false;
        foreach (char character in name)
        {
            bool isHyphen = character == '-';
            if (!(character is >= 'a' and <= 'z'
                  || character is >= '0' and <= '9'
                  || isHyphen)
                || isHyphen && previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = isHyphen;
        }

        return true;
    }

    private static bool IsAgentSkillDescription(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return false;

        int length = 0;
        foreach (var _ in description.EnumerateRunes())
        {
            if (++length > 1024)
                return false;
        }

        return true;
    }

    private static void WriteOutput(string output, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            File.WriteAllText(outputPath, output);
        else
            Console.Write(output);
    }

    private sealed record ProjectSection(
        string Name,
        string Summary,
        string[] Labels,
        string[] Ids,
        IReadOnlyList<ProjectDocumentRow> Documents,
        string EmptyText,
        bool WasLogicallyEmpty = false);

    private sealed record ProjectDocumentRow
    {
        public ProjectDocumentRow(
            ProjectDocumentKind kind,
            string package,
            string version,
            string path,
            long size,
            string? name,
            string? description,
            string fullPath)
        {
            Kind = kind;
            Package = CSharpIdentifier.ContainRenderedText(package);
            Version = CSharpIdentifier.ContainRenderedText(version);
            Path = CSharpIdentifier.ContainRenderedText(path);
            Size = size;
            Name = name;
            Description = description;
            FullPath = fullPath;
            Cells = kind == ProjectDocumentKind.Skill
                ?
                [
                    Package,
                    Version,
                    Path,
                    Size.ToString(CultureInfo.InvariantCulture),
                    Name ?? "",
                    Description ?? "",
                ]
                :
                [
                    Package,
                    Version,
                    Path,
                    Size.ToString(CultureInfo.InvariantCulture),
                ];
        }

        public ProjectDocumentKind Kind { get; }
        public string Package { get; }
        public string Version { get; }
        public string Path { get; }
        public long Size { get; }
        public string? Name { get; }
        public string? Description { get; }
        public string FullPath { get; }
        public string[] Cells { get; }
    }
}
