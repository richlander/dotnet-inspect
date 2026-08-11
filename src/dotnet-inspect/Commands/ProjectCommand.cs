using System.Globalization;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.CSharp;
using Markout;

namespace DotnetInspector.Commands;

public class ProjectCommand
{
    public const string Name = "project";
    static readonly string[] ProjectReadmeCandidates = ["README.md", "PROJECT.md"];
    static readonly string[] SkillPatterns = ["skills/SKILL.md", "skills/**/SKILL.md"];

    public static async Task<int> ExecuteAsync(
        ProjectOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeLegacyModes(options, out options))
            return 1;
        if (!ValidateOptions(options))
            return 1;

        Verbosity userVerbosity = options.Verbosity;
        ProjectSectionCatalog catalog = ProjectSections.CreateCatalog();
        SectionPipeline<ProjectInspection> pipeline = catalog.Pipeline;
        DocumentSchema schema = ProjectSections.CreateSchema();
        SelectResult selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select,
            pipeline.SelectableSectionNames,
            pipeline.InfoSectionNames,
            pipeline.GetCategoryMap(),
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selectResult))
            return 1;

        HashSet<string>? selectedSections = selectResult.Sections;
        Verbosity requiredVerbosity = pipeline.GetRequiredVerbosity(selectedSections);
        if (requiredVerbosity > options.Verbosity)
            options = options with { Verbosity = requiredVerbosity };

        if (options.Discover is null
            && options.Verbosity == Verbosity.Quiet
            && selectedSections is null)
        {
            CommandError.Write(
                "-v:q requires an explicit project section. "
                + "Use -v:m for the default Skills view or select a section with -S.");
            return 1;
        }

        if (options.Effective && options.Discover is null)
        {
            CommandError.Write("--effective requires -D/--discover.");
            return 1;
        }
        if (options.Effective && options.Schema)
        {
            CommandError.Write("--effective cannot be combined with --schema.");
            return 1;
        }
        if (options.Schema && options.Discover is null)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }

        if (!TryResolveCandidateSections(
                options,
                pipeline,
                selectedSections,
                out HashSet<string> candidateSections))
        {
            return 1;
        }
        bool validProjection = options.Discover is not null
            ? DiscoverOutput.ValidateProjection(options.Fields, options.Columns)
            : ProjectionDiagnostics.ValidateProjection(
                schema,
                candidateSections,
                options.Fields,
                options.Columns);
        if (!validProjection)
            return 1;
        if (!ValidateShapeAndFormatOptions(
                options,
                candidateSections,
                selectedSections))
        {
            return 1;
        }
        if (options.Discover is not null
            && !LensProjection.Validate(options, "-D/--discover"))
        {
            return 1;
        }
        string? selectedValueField = null;
        if (options.Discover is null
            && !TryResolveValueField(
                schema,
                candidateSections,
                options,
                out selectedValueField))
        {
            return 1;
        }

        bool structuralDiscovery = options.Discover is not null
            && !options.Effective;
        HashSet<InspectionQueryDefinition> requestedQueries =
            candidateSections.Count == 0
                ? []
                : pipeline.GetRequiredQueries(
                    options.Verbosity,
                    candidateSections,
                    excludeUnbounded: options.Discover is not null);
        if (structuralDiscovery
            || (options.Discover is not null && requestedQueries.Count == 0))
        {
            return WriteDiscovery(
                options,
                userVerbosity,
                pipeline,
                schema,
                candidateSections,
                new ProjectInspection(),
                structuralDiscovery);
        }

        var commandContext = new CommandContext(options.Verbose);
        if (!ProjectAssetsParser.TryFindAssets(
                options.ProjectPath,
                out string? assetsPath,
                out ProjectAssetsStatus assetsStatus))
        {
            CommandError.Write(
                $"{ProjectAssetsParser.DescribeMissingAssets(options.ProjectPath, assetsStatus)}");
            return 1;
        }

        commandContext.Logger.Log($"Using assets: {assetsPath}");
        List<ProjectPackageReference> dependencies =
            ProjectAssetsParser.ParsePackageReferences(
                assetsPath,
                options.Tfm,
                commandContext.Logger.Log);
        if (dependencies.Count == 0)
        {
            CommandError.Write($"No direct package references found in '{assetsPath}'.");
            return 1;
        }

        if (!TryFilterDependencies(dependencies, options.PackageFilter, out var focusedDependencies))
            return 1;

        ProjectQueryContext queryContext = new(
            () => ReadSkills(assetsPath, options.Tfm),
            () => ReadAgentGuidance(dependencies),
            token => ReadPackageDocumentsAsync(
                focusedDependencies,
                options,
                commandContext,
                token));
        InspectionQueryResults queryResults = await catalog.QueryRegistry.RunAsync(
            requestedQueries,
            queryContext,
            cancellationToken: cancellationToken);
        var inspection = new ProjectInspection();
        inspection.Apply(queryResults);
        ProjectContentFailure[] failures = [.. inspection.Failures()];
        WriteFailures(failures);

        if (options.Discover is not null)
        {
            int discoverExitCode = WriteDiscovery(
                options,
                userVerbosity,
                pipeline,
                schema,
                candidateSections,
                inspection,
                structural: false);
            return discoverExitCode == 0 && failures.Length > 0
                ? 1
                : discoverExitCode;
        }

        if (options.PackageFilter is not null
            && candidateSections.Contains(ProjectSectionNames.PackageDocs)
            && !options.Count
            && inspection.PackageDocuments is { Documents.Length: 0 })
        {
            CommandError.Write(
                $"Package '{options.PackageFilter}' does not contain a README.md or PROJECT.md file.");
            return 1;
        }

        HashSet<string> renderedSections =
            pipeline.ComputeIncludeSections(
                inspection,
                options.Verbosity,
                candidateSections)
            ?? candidateSections;

        int outputExitCode;
        if (options.Value || options.Urls || options.Paths)
        {
            outputExitCode = WriteShapeProjection(
                inspection,
                options,
                renderedSections,
                selectedValueField);
        }
        else if (options.Count)
        {
            WriteCounts(
                inspection,
                renderedSections,
                options.Rows,
                options.OutputPath);
            outputExitCode = 0;
        }
        else if (options.Print || ShouldPrintBareDocument(options))
        {
            outputExitCode = PrintDocument(inspection, options, renderedSections);
        }
        else
        {
            string output = ProjectOutputFormatter.Render(
                inspection,
                options,
                renderedSections);
            WriteOutput(output, options.OutputPath);
            outputExitCode = 0;
        }

        return outputExitCode == 0 && failures.Length > 0 ? 1 : outputExitCode;
    }

    static bool TryNormalizeLegacyModes(
        ProjectOptions input,
        out ProjectOptions normalized)
    {
        normalized = input;
        if (input.AgentsIndex && input.ReadmePackageId is not null)
        {
            CommandError.Write("--agents-index cannot be combined with --readme.");
            return false;
        }

        bool hasExplicitSelection = input.Select is not null || input.SelectDefault;
        if ((input.AgentsIndex || input.ReadmePackageId is not null)
            && hasExplicitSelection)
        {
            CommandError.Write(
                "-S/--select cannot be combined with --agents-index or --readme.");
            return false;
        }

        if (input.ReadmePackageId is not null
            && input.PackageFilter is not null
            && !input.ReadmePackageId.Equals(
                input.PackageFilter,
                StringComparison.OrdinalIgnoreCase))
        {
            CommandError.Write("--readme and --package must name the same dependency.");
            return false;
        }

        if (input.AgentsIndex)
        {
            normalized = input with
            {
                AgentsIndex = false,
                Select = [ProjectSectionNames.AgentGuidance],
            };
        }
        else if (input.ReadmePackageId is not null)
        {
            normalized = input with
            {
                ReadmePackageId = null,
                Select = [ProjectSectionNames.PackageDocs],
                PackageFilter = input.ReadmePackageId,
                Print = true,
            };
        }

        return true;
    }

    static bool ValidateOptions(ProjectOptions options)
    {
        if (options.FrontmatterRequested && options.BodyRequested)
        {
            CommandError.Write(
                "--frontmatter/--yaml-header cannot be combined with --body.");
            return false;
        }

        if (options.PrintRow is not null
            && !options.Print
            && !options.Value
            && !options.Urls
            && !options.Paths)
        {
            CommandError.Write(
                "--row requires --print, --value, --urls, or --paths.");
            return false;
        }

        if (options.Print && options.Rows is not null)
        {
            CommandError.Write(
                "--rows cannot be combined with --print; "
                + "use --row N|first|last to choose a printed row.");
            return false;
        }

        if ((options.FrontmatterRequested || options.BodyRequested)
            && !options.Print)
        {
            CommandError.Write("--frontmatter/--body require --print or --readme.");
            return false;
        }

        if (options.Tree && options.Discover is null)
        {
            CommandError.Write("--tree is supported only with -D/--discover.");
            return false;
        }

        if (options.Count && options.Print)
        {
            CommandError.Write("--count cannot be combined with --print.");
            return false;
        }

        return true;
    }

    static bool TryResolveCandidateSections(
        ProjectOptions options,
        SectionPipeline<ProjectInspection> pipeline,
        HashSet<string>? selectedSections,
        out HashSet<string> candidateSections)
    {
        if (options.Discover is { Length: > 0 })
        {
            SelectResult discoverSelection = SelectResolver.ResolveSelectAsSections(
                options.Discover,
                pipeline.SelectableSectionNames,
                pipeline.InfoSectionNames,
                pipeline.GetCategoryMap(),
                selectDefault: false);
            if (SelectOutput.WriteUnresolved(discoverSelection))
            {
                candidateSections = [];
                return false;
            }

            HashSet<string> discoveredSections = discoverSelection.Sections ?? [];
            if (!options.Schema
                && selectedSections is { Count: > 0 })
                discoveredSections.IntersectWith(selectedSections);
            candidateSections = discoveredSections;
            return true;
        }

        if (options.Discover is not null)
        {
            HashSet<string> discoveredSections = [.. pipeline.BaseSectionNames];
            if (!options.Schema
                && selectedSections is { Count: > 0 })
                discoveredSections.IntersectWith(selectedSections);
            candidateSections = discoveredSections;
            return true;
        }

        candidateSections = pipeline.GetCandidateSections(
            options.Verbosity,
            selectedSections);
        return true;
    }

    static bool ValidateShapeAndFormatOptions(
        ProjectOptions options,
        HashSet<string> candidateSections,
        HashSet<string>? selectedSections)
    {
        if (options.PackageFilter is not null
            && selectedSections?.Contains(ProjectSectionNames.PackageDocs) != true)
        {
            CommandError.Write(
                "--package requires -S \"Package Docs\" or --readme.");
            return false;
        }

        if (options.Discover is not null)
        {
            if (options.JsonArray)
            {
                CommandError.Write(
                    "--json-array is not available with -D/--discover.");
                return false;
            }
            if (options.JsonOutput
                && (options.Columns is { Length: > 0 }
                    || options.Fields is { Length: > 0 }))
            {
                CommandError.Write(
                    "--fields/--columns cannot be combined with --json discovery; "
                    + "use --table, --tsv, or --jsonl.");
                return false;
            }
            if (options.Tree
                && (options.JsonOutput
                    || options.Tabular
                    || options.Columns is { Length: > 0 }
                    || options.Fields is { Length: > 0 }
                    || options.Rows is not null))
            {
                CommandError.Write(
                    "--tree cannot be combined with structured formats, "
                    + "--fields, --columns, or --rows.");
                return false;
            }
            return true;
        }

        if (!OutputFormatResolver.ValidateSingleSectionForTabular(
                options.Tabular && !options.Print,
                candidateSections))
        {
            return false;
        }

        int shapeCount = ShapeProjectionOutput.ActiveShapeCount(
            options.Value,
            options.Urls,
            options.Paths);
        if (shapeCount > 1)
        {
            CommandError.Write("specify only one of --value, --urls, or --paths.");
            return false;
        }

        if (shapeCount == 1)
        {
            string optionName = options.Value
                ? "--value"
                : options.Urls
                    ? "--urls"
                    : "--paths";
            if (!ShapeProjectionOutput.ValidateSingleSection(
                    candidateSections,
                    optionName))
            {
                return false;
            }
            if (options.Count || options.Print)
            {
                CommandError.Write(
                    $"{optionName} cannot be combined with --count or --print.");
                return false;
            }
            if (options.Rows is not null)
            {
                CommandError.Write(
                    $"--rows cannot be combined with {optionName}; "
                    + "use -n N to limit projected output lines or "
                    + "--row N|first|last to select a projected row.");
                return false;
            }
        }

        if (options.JsonArray && shapeCount == 0 && !options.Print)
        {
            CommandError.Write(
                "--json-array requires --value, --urls, --paths, or --print.");
            return false;
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            CommandError.Write(
                "--json-array cannot be combined with --json or --jsonl.");
            return false;
        }

        if ((options.Print || ShouldPrintBareDocument(options))
            && candidateSections.Count != 1)
        {
            CommandError.Write(
                "--print requires -S/--select to match exactly one printable section.");
            return false;
        }

        if (options.JsonOutput
            && shapeCount == 0
            && (options.Columns is { Length: > 0 }
                || options.Fields is { Length: > 0 }))
        {
            CommandError.Write(
                "--fields/--columns cannot be combined with --json for project sections; "
                + "use --table, --tsv, or --jsonl.");
            return false;
        }

        return true;
    }

    static bool ShouldPrintBareDocument(ProjectOptions options) =>
        options.Bare
        && !options.Count
        && !options.Value
        && !options.Urls
        && !options.Paths
        && options.Rows is null
        && options.Columns is not { Length: > 0 }
        && options.Fields is not { Length: > 0 };

    static int WriteDiscovery(
        ProjectOptions options,
        Verbosity userVerbosity,
        SectionPipeline<ProjectInspection> pipeline,
        DocumentSchema schema,
        HashSet<string> candidateSections,
        ProjectInspection inspection,
        bool structural)
    {
        List<string> discoverableSections =
            candidateSections.Count == 0
                ? []
                : pipeline.GetDiscoverableSections(
                    inspection,
                    candidateSections);
        return DiscoverOutput.ExecuteEffective(
            options.Discover!,
            discoverableSections,
            schema,
            tree: options.Tree,
            json: options.JsonOutput,
            tsv: options.Tsv,
            jsonl: options.Jsonl,
            markdown: !options.Tabular && !options.JsonOutput,
            verbosity: (int)userVerbosity,
            fullSchema: schema,
            sectionCostAnnotations: pipeline.GetCostAnnotations(),
            sectionCategories: pipeline.GetCategoryMap(),
            catalogHiddenSections: structural && options.Schema
                ? null
                : pipeline.GetCatalogHiddenSections(),
            listedCategoryDoors: pipeline.GetListedCategoryDoors(),
            projection: options,
            columns: options.Columns,
            fields: options.Fields,
            rows: options.Rows,
            outputPath: options.OutputPath);
    }

    static bool TryResolveValueField(
        DocumentSchema schema,
        IReadOnlyCollection<string> candidateSections,
        ProjectOptions options,
        out string? selectedField)
    {
        selectedField = null;
        if (!options.Value)
            return true;

        string[] requested =
        [
            .. options.Columns ?? [],
            .. options.Fields ?? [],
        ];
        if (requested.Length == 0)
            return true;

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string section in candidateSections)
        {
            foreach (string name in schema.ValidateProjection(section, requested).Resolved)
                resolved.Add(name);
        }

        if (resolved.Count > 1)
        {
            CommandError.Write(
                "--value accepts at most one field or column.");
            return false;
        }

        selectedField = resolved.SingleOrDefault()?.ToLowerInvariant();
        return true;
    }

    static bool TryFilterDependencies(
        IReadOnlyList<ProjectPackageReference> dependencies,
        string? packageFilter,
        out IReadOnlyList<ProjectPackageReference> focused)
    {
        if (packageFilter is null)
        {
            focused = dependencies;
            return true;
        }

        ProjectPackageReference? dependency = dependencies.FirstOrDefault(item =>
            item.PackageName.Equals(
                packageFilter,
                StringComparison.OrdinalIgnoreCase));
        if (dependency is null)
        {
            CommandError.Write(
                $"Package '{packageFilter}' is not a direct dependency of the project.");
            focused = [];
            return false;
        }

        focused = [dependency];
        return true;
    }

    static void WriteFailures(IEnumerable<ProjectContentFailure> failures)
    {
        foreach (ProjectContentFailure failure in failures)
        {
            CommandError.WriteWarning(
                $"Could not read '{failure.Package}' file '{failure.Path}': {failure.Reason}");
        }
    }

    static ProjectSkillsResult ReadSkills(string assetsPath, string? tfm)
    {
        var skills = new List<ProjectSkillData>();
        var failures = new List<ProjectContentFailure>();
        foreach (ProjectPackageFileEntry file in
                 ProjectAssetsParser.ParsePackageFileEntries(
                     assetsPath,
                     tfm,
                     SkillPatterns,
                     log: null))
        {
            if (string.IsNullOrWhiteSpace(file.FullPath)
                || !File.Exists(file.FullPath))
            {
                failures.Add(new ProjectContentFailure(
                    file.PackageName,
                    file.Path,
                    "the restored assets file declares the skill, but the file is missing"));
                continue;
            }

            try
            {
                string content = File.ReadAllText(file.FullPath);
                IReadOnlyDictionary<string, string> frontmatter =
                    MarkdownContent.ParseYamlFrontmatter(content);
                frontmatter.TryGetValue("name", out string? skillName);
                frontmatter.TryGetValue("description", out string? description);
                skills.Add(new ProjectSkillData(
                    file.PackageName,
                    file.Version,
                    file.Path,
                    new FileInfo(file.FullPath).Length,
                    skillName ?? "",
                    description ?? "",
                    content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ProjectContentFailure(
                    file.PackageName,
                    file.Path,
                    ex.Message));
            }
        }

        return ProjectSkillsQuery.Execute(skills, failures);
    }

    static ProjectAgentGuidanceResult ReadAgentGuidance(
        IReadOnlyList<ProjectPackageReference> dependencies)
    {
        var guidance = new List<ProjectAgentGuidanceData>();
        var failures = new List<ProjectContentFailure>();
        foreach (ProjectPackageReference dependency in dependencies)
        {
            const string relativePath = "AGENTS.md";
            string? fullPath = string.IsNullOrWhiteSpace(dependency.PackagePath)
                ? null
                : Path.Combine(dependency.PackagePath, relativePath);
            if (fullPath is null || !File.Exists(fullPath))
            {
                guidance.Add(new ProjectAgentGuidanceData(
                    dependency.PackageName,
                    dependency.Version,
                    "",
                    "",
                    "",
                    null));
                continue;
            }

            try
            {
                string content = File.ReadAllText(fullPath);
                IReadOnlyDictionary<string, string> frontmatter =
                    MarkdownContent.ParseYamlFrontmatter(content);
                frontmatter.TryGetValue("name", out string? name);
                frontmatter.TryGetValue("description", out string? description);
                guidance.Add(new ProjectAgentGuidanceData(
                    dependency.PackageName,
                    dependency.Version,
                    relativePath,
                    name ?? "",
                    description ?? "",
                    content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ProjectContentFailure(
                    dependency.PackageName,
                    relativePath,
                    ex.Message));
                guidance.Add(new ProjectAgentGuidanceData(
                    dependency.PackageName,
                    dependency.Version,
                    "",
                    "",
                    "",
                    null));
            }
        }

        return ProjectAgentGuidanceQuery.Execute(guidance, failures);
    }

    static async ValueTask<ProjectPackageDocumentsResult> ReadPackageDocumentsAsync(
        IReadOnlyList<ProjectPackageReference> dependencies,
        ProjectOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var documents = new List<ProjectPackageDocumentData>();
        var failures = new List<ProjectContentFailure>();
        foreach (ProjectPackageReference dependency in dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquired = await ReadBestPackageDocumentAsync(
                dependency,
                options.SourceOptions,
                context);
            if (acquired.Document is not null)
                documents.Add(acquired.Document);
            if (acquired.Failure is not null)
                failures.Add(acquired.Failure);
        }

        return ProjectPackageDocumentsQuery.Execute(documents, failures);
    }

    static async Task<(
        ProjectPackageDocumentData? Document,
        ProjectContentFailure? Failure)> ReadBestPackageDocumentAsync(
        ProjectPackageReference dependency,
        NuGetSourceOptions? sourceOptions,
        CommandContext context)
    {
        if (!string.IsNullOrWhiteSpace(dependency.PackagePath)
            && Directory.Exists(dependency.PackagePath))
        {
            try
            {
                return (ReadBestPackageDocumentFromDirectory(dependency), null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (
                    null,
                    new ProjectContentFailure(
                        dependency.PackageName,
                        "README.md|PROJECT.md",
                        ex.Message));
            }
        }

        PackageExtractionResult? resolution = null;
        try
        {
            PackageExtractionOutcome outcome = await PackageExtractor.ExtractPackageAsync(
                context.HttpClient,
                dependency.PackageName,
                context.Logger.Log,
                sourceOptions: sourceOptions,
                version: dependency.Version);
            if (!outcome.IsSuccess)
            {
                return (
                    null,
                    new ProjectContentFailure(
                        dependency.PackageName,
                        "README.md|PROJECT.md",
                        outcome.ErrorMessage ?? "package acquisition failed"));
            }

            resolution = outcome.Result!;
            var resolved = dependency with
            {
                Version = resolution.Version ?? dependency.Version,
                PackagePath = resolution.ExtractPath,
            };
            return (ReadBestPackageDocumentFromDirectory(resolved), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (
                null,
                new ProjectContentFailure(
                    dependency.PackageName,
                    "README.md|PROJECT.md",
                    ex.Message));
        }
        finally
        {
            if (resolution is { FromCache: false, TempDir: not null }
                && Directory.Exists(resolution.TempDir))
            {
                try
                {
                    Directory.Delete(resolution.TempDir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    context.Logger.LogWarning(
                        $"Could not remove temporary package directory "
                        + $"'{resolution.TempDir}': {ex.Message}");
                }
            }
        }
    }

    static ProjectPackageDocumentData? ReadBestPackageDocumentFromDirectory(
        ProjectPackageReference dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency.PackagePath)
            || !Directory.Exists(dependency.PackagePath))
        {
            return null;
        }

        string? readme = ResolveProjectReadme(dependency.PackagePath);
        if (readme is null)
            return null;

        string fullPath = Path.Combine(
            dependency.PackagePath,
            readme.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return null;

        return new ProjectPackageDocumentData(
            dependency.PackageName,
            dependency.Version,
            readme,
            new FileInfo(fullPath).Length,
            File.ReadAllText(fullPath));
    }

    static string? ResolveProjectReadme(string packagePath)
    {
        foreach (string candidate in ProjectReadmeCandidates)
        {
            string? match = Directory.EnumerateFiles(
                    packagePath,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file => Path.GetFileName(file).Equals(
                    candidate,
                    StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return Path.GetRelativePath(packagePath, match)
                    .Replace('\\', '/');
            }
        }

        return null;
    }

    static int PrintDocument(
        ProjectInspection inspection,
        ProjectOptions options,
        HashSet<string> renderedSections)
    {
        string section = renderedSections.Single();
        var printOptions = new PrintProjectionOptions(
            options.Bare && !options.Print
                ? RowSelector.First
                : options.PrintRow,
            options.JsonOutput,
            options.Jsonl,
            options.JsonArray,
            options.Bare,
            options.OutputPath);

        if (section == ProjectSectionNames.AgentGuidance)
        {
            IReadOnlyList<ProjectAgentGuidanceData> guidance =
                inspection.AgentGuidance?.Guidance ?? [];
            bool firstAvailable = options.Bare && !options.Print;
            var rows = new List<PrintableRow>(guidance.Count);
            var contentByRow = new Dictionary<int, string?>();
            for (int index = 0; index < guidance.Count; index++)
            {
                ProjectAgentGuidanceData item = guidance[index];
                if (firstAvailable && item.Content is null)
                    continue;

                int rowNumber = index + 1;
                rows.Add(new PrintableRow(
                    rowNumber,
                    section,
                    CSharpIdentifier.ContainRenderedText(
                        $"{item.Package} {item.Path}"),
                    CSharpIdentifier.ContainRenderedText(item.Path),
                    null));
                contentByRow[rowNumber] = item.Content is null
                    ? null
                    : Printable(
                        rowNumber,
                        section,
                        $"{item.Package} {item.Path}",
                        item.Path,
                        item.Content,
                        options.ContentScope).Content;
            }

            return PrintProjectionOutput.Write(
                rows,
                row => contentByRow[row.Row],
                printOptions);
        }

        List<PrintableDocument> documents = section switch
        {
            ProjectSectionNames.Skills => inspection.Skills?.Skills
                .Select((row, index) => Printable(
                    index + 1,
                    section,
                    $"{row.Package} {row.Path}",
                    row.Path,
                    row.Content,
                    options.ContentScope))
                .ToList() ?? [],
            ProjectSectionNames.PackageDocs =>
                inspection.PackageDocuments?.Documents
                    .Select((row, index) => Printable(
                        index + 1,
                        section,
                        $"{row.Package} {row.Path}",
                        row.Path,
                        row.Content,
                        options.ContentScope))
                    .ToList() ?? [],
            _ => [],
        };

        return PrintProjectionOutput.Write(
            documents,
            printOptions);
    }

    static PrintableDocument Printable(
        int row,
        string section,
        string label,
        string path,
        string content,
        PackageFileContentScope scope)
        => new(
            row,
            section,
            CSharpIdentifier.ContainRenderedText(label),
            CSharpIdentifier.ContainRenderedText(path),
            null,
            GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(
                MarkdownContent.ApplyScope(content, scope)));

    static int WriteShapeProjection(
        ProjectInspection inspection,
        ProjectOptions options,
        HashSet<string> renderedSections,
        string? selectedValueField)
    {
        string section = renderedSections.Single();
        ShapeProjectionKind kind = ShapeProjectionOutput.GetKind(
            options.Value,
            options.Urls,
            options.Paths);
        var projected = new List<ShapeProjectionRow>();
        switch (section)
        {
            case ProjectSectionNames.Skills when inspection.Skills is not null:
                AddSkillProjections(
                    inspection.Skills.Skills,
                    selectedValueField,
                    kind,
                    projected);
                break;
            case ProjectSectionNames.AgentGuidance
                when inspection.AgentGuidance is not null:
                AddAgentGuidanceProjections(
                    inspection.AgentGuidance.Guidance,
                    selectedValueField,
                    kind,
                    projected);
                break;
            case ProjectSectionNames.PackageDocs
                when inspection.PackageDocuments is not null:
                AddPackageDocumentProjections(
                    inspection.PackageDocuments.Documents,
                    selectedValueField,
                    kind,
                    projected);
                break;
        }

        return ShapeProjectionOutput.Write(
            projected,
            new ShapeProjectionOptions(
                kind,
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.OutputPath));
    }

    static void AddSkillProjections(
        IReadOnlyList<ProjectSkillData> rows,
        string? selectedValueField,
        ShapeProjectionKind kind,
        List<ShapeProjectionRow> projected)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            ProjectSkillData row = rows[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Paths => row.Path,
                ShapeProjectionKind.Value => SelectSkillValue(
                    row,
                    selectedValueField),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                projected.Add(new ShapeProjectionRow(
                    i + 1,
                    ProjectSectionNames.Skills,
                    value,
                    Label: row.Package,
                    Path: row.Path));
            }
        }
    }

    static void AddAgentGuidanceProjections(
        IReadOnlyList<ProjectAgentGuidanceData> rows,
        string? selectedValueField,
        ShapeProjectionKind kind,
        List<ShapeProjectionRow> projected)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            ProjectAgentGuidanceData row = rows[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Paths => row.Path,
                ShapeProjectionKind.Value => SelectAgentGuidanceValue(
                    row,
                    selectedValueField),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                projected.Add(new ShapeProjectionRow(
                    i + 1,
                    ProjectSectionNames.AgentGuidance,
                    value,
                    Label: row.Package,
                    Path: row.Path));
            }
        }
    }

    static void AddPackageDocumentProjections(
        IReadOnlyList<ProjectPackageDocumentData> rows,
        string? selectedValueField,
        ShapeProjectionKind kind,
        List<ShapeProjectionRow> projected)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            ProjectPackageDocumentData row = rows[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Paths => row.Path,
                ShapeProjectionKind.Value => SelectPackageDocumentValue(
                    row,
                    selectedValueField),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                projected.Add(new ShapeProjectionRow(
                    i + 1,
                    ProjectSectionNames.PackageDocs,
                    value,
                    Label: row.Package,
                    Path: row.Path));
            }
        }
    }

    static string? SelectSkillValue(
        ProjectSkillData row,
        string? selectedValueField)
        => selectedValueField switch
        {
            "package" => row.Package,
            "version" => row.Version,
            "path" => row.Path,
            "size" => row.Size.ToString(CultureInfo.InvariantCulture),
            "name" => row.Name,
            "description" => row.Description,
            _ => row.Path,
        };

    static string? SelectAgentGuidanceValue(
        ProjectAgentGuidanceData row,
        string? selectedValueField)
        => selectedValueField switch
        {
            "package" => row.Package,
            "version" => row.Version,
            "path" => row.Path,
            "name" => row.Name,
            "description" => row.Description,
            _ => row.Path,
        };

    static string? SelectPackageDocumentValue(
        ProjectPackageDocumentData row,
        string? selectedValueField)
        => selectedValueField switch
        {
            "package" => row.Package,
            "version" => row.Version,
            "path" => row.Path,
            "size" => row.Size.ToString(CultureInfo.InvariantCulture),
            _ => row.Path,
        };

    static void WriteCounts(
        ProjectInspection inspection,
        HashSet<string> renderedSections,
        RowWindow? rows,
        string? outputPath)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string section in renderedSections)
        {
            counts[section] = section switch
            {
                ProjectSectionNames.Skills =>
                    CountRows<ProjectSkillData>(
                        inspection.Skills?.Skills,
                        rows),
                ProjectSectionNames.AgentGuidance =>
                    CountRows<ProjectAgentGuidanceData>(
                        inspection.AgentGuidance?.Guidance,
                        rows),
                ProjectSectionNames.PackageDocs =>
                    CountRows<ProjectPackageDocumentData>(
                        inspection.PackageDocuments?.Documents,
                        rows),
                _ => 0,
            };
        }

        if (renderedSections.Count == 1)
            CountOutput.WriteCount(counts[renderedSections.Single()], outputPath);
        else
            CountOutput.WriteCountMap(counts, renderedSections.ToArray(), outputPath);
    }

    static int CountRows<T>(IReadOnlyList<T>? source, RowWindow? rows) =>
        source is null ? 0 : RowWindow.Apply(rows, source).Count;

    static void WriteOutput(string output, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            File.WriteAllText(outputPath, output);
        else
            Console.Write(output);
    }
}
