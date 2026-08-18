using DotnetInspector.Commands;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

internal sealed class ProjectSkillsProvider
{
    static readonly string[] SkillPatterns =
        ["skills/SKILL.md", "skills/**/SKILL.md"];

    readonly string _assetsPath;
    readonly string? _targetFramework;
    readonly bool _deferContent;
    readonly ProjectDocumentContentStore _contentStore;

    public ProjectSkillsProvider(
        string assetsPath,
        string? targetFramework,
        bool deferContent,
        ProjectDocumentContentStore contentStore)
    {
        _assetsPath = assetsPath;
        _targetFramework = targetFramework;
        _deferContent = deferContent;
        _contentStore = contentStore;
    }

    public ProjectSkillsResult Read()
    {
        var skills = new List<ProjectSkillData>();
        var failures = new List<ProjectContentFailure>();
        foreach (ProjectPackageFileEntry file in
                 ProjectAssetsParser.ParsePackageFileEntries(
                     _assetsPath,
                     _targetFramework,
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
                skills.Add(new ProjectSkillData(
                    file.PackageName,
                    file.Version,
                    file.Path,
                    null,
                    "",
                    "",
                    null));
                continue;
            }

            long? size = null;
            try
            {
                long knownSize = new FileInfo(file.FullPath).Length;
                size = knownSize;
                if (_deferContent)
                {
                    _contentStore.Add(
                        ProjectSectionNames.Skills,
                        file.PackageName,
                        file.Path,
                        file.FullPath);
                    skills.Add(new ProjectSkillData(
                        file.PackageName,
                        file.Version,
                        file.Path,
                        knownSize,
                        "",
                        "",
                        ""));
                    continue;
                }

                string content = File.ReadAllText(file.FullPath);
                IReadOnlyDictionary<string, string> frontmatter =
                    MarkdownContent.ParseYamlFrontmatter(content);
                frontmatter.TryGetValue("name", out string? skillName);
                frontmatter.TryGetValue("description", out string? description);
                _contentStore.Add(
                    ProjectSectionNames.Skills,
                    file.PackageName,
                    file.Path,
                    file.FullPath);
                skills.Add(new ProjectSkillData(
                    file.PackageName,
                    file.Version,
                    file.Path,
                    knownSize,
                    skillName ?? "",
                    description ?? "",
                    ""));
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ProjectContentFailure(
                    file.PackageName,
                    file.Path,
                    ex.Message));
                if (size is long knownSize)
                {
                    skills.Add(new ProjectSkillData(
                        file.PackageName,
                        file.Version,
                        file.Path,
                        knownSize,
                        "",
                        "",
                        ""));
                }
            }
        }

        return ProjectSkillsQuery.Execute(skills, failures);
    }
}

internal sealed class ProjectAgentGuidanceProvider
{
    readonly IReadOnlyList<ProjectPackageReference> _dependencies;
    readonly bool _deferContent;
    readonly ProjectDocumentContentStore _contentStore;

    public ProjectAgentGuidanceProvider(
        IReadOnlyList<ProjectPackageReference> dependencies,
        bool deferContent,
        ProjectDocumentContentStore contentStore)
    {
        _dependencies = dependencies;
        _deferContent = deferContent;
        _contentStore = contentStore;
    }

    public ProjectAgentGuidanceResult Read()
    {
        var guidance = new List<ProjectAgentGuidanceData>();
        var failures = new List<ProjectContentFailure>();
        foreach (ProjectPackageReference dependency in _dependencies)
        {
            const string relativePath = "AGENTS.md";
            string? fullPath = string.IsNullOrWhiteSpace(dependency.PackagePath)
                ? null
                : Path.Combine(dependency.PackagePath, relativePath);
            if (fullPath is null || !File.Exists(fullPath))
            {
                guidance.Add(EmptyGuidance(dependency));
                continue;
            }

            try
            {
                if (_deferContent)
                {
                    _contentStore.Add(
                        ProjectSectionNames.AgentGuidance,
                        dependency.PackageName,
                        relativePath,
                        fullPath);
                    guidance.Add(new ProjectAgentGuidanceData(
                        dependency.PackageName,
                        dependency.Version,
                        relativePath,
                        "",
                        "",
                        ""));
                    continue;
                }

                string content = File.ReadAllText(fullPath);
                IReadOnlyDictionary<string, string> frontmatter =
                    MarkdownContent.ParseYamlFrontmatter(content);
                frontmatter.TryGetValue("name", out string? name);
                frontmatter.TryGetValue("description", out string? description);
                _contentStore.Add(
                    ProjectSectionNames.AgentGuidance,
                    dependency.PackageName,
                    relativePath,
                    fullPath);
                guidance.Add(new ProjectAgentGuidanceData(
                    dependency.PackageName,
                    dependency.Version,
                    relativePath,
                    name ?? "",
                    description ?? "",
                    ""));
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ProjectContentFailure(
                    dependency.PackageName,
                    relativePath,
                    ex.Message));
                guidance.Add(new ProjectAgentGuidanceData(
                    dependency.PackageName,
                    dependency.Version,
                    relativePath,
                    "",
                    "",
                    ""));
            }
        }

        return ProjectAgentGuidanceQuery.Execute(guidance, failures);
    }

    static ProjectAgentGuidanceData EmptyGuidance(
        ProjectPackageReference dependency)
        => new(
            dependency.PackageName,
            dependency.Version,
            "",
            "",
            "",
            null);
}

internal sealed class ProjectPackageDocumentsProvider
{
    static readonly string[] DocumentCandidates = ["README.md", "PROJECT.md"];

    readonly IReadOnlyList<ProjectPackageReference> _dependencies;
    readonly NuGetSourceOptions? _sourceOptions;
    readonly CommandContext _context;
    readonly ProjectDocumentContentStore _contentStore;

    public ProjectPackageDocumentsProvider(
        IReadOnlyList<ProjectPackageReference> dependencies,
        NuGetSourceOptions? sourceOptions,
        CommandContext context,
        ProjectDocumentContentStore contentStore)
    {
        _dependencies = dependencies;
        _sourceOptions = sourceOptions;
        _context = context;
        _contentStore = contentStore;
    }

    public async ValueTask<ProjectPackageDocumentsResult> ReadAsync(
        CancellationToken cancellationToken)
    {
        var documents = new List<ProjectPackageDocumentData>();
        var failures = new List<ProjectContentFailure>();
        foreach (ProjectPackageReference dependency in _dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquired = await ReadBestDocumentAsync(
                dependency,
                cancellationToken);
            if (acquired.Document is not null)
                documents.Add(acquired.Document);
            if (acquired.Failure is not null)
                failures.Add(acquired.Failure);
        }

        return ProjectPackageDocumentsQuery.Execute(documents, failures);
    }

    async Task<(
        ProjectPackageDocumentData? Document,
        ProjectContentFailure? Failure)> ReadBestDocumentAsync(
        ProjectPackageReference dependency,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(dependency.PackagePath)
            && Directory.Exists(dependency.PackagePath))
        {
            try
            {
                return (
                    ReadBestDocumentFromDirectory(dependency),
                    null);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
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
        bool retainTemporaryDirectory = false;
        try
        {
            // Package acquisition is single-flight. Cancel this caller's wait
            // without cancelling the shared acquisition for other callers.
            PackageExtractionOutcome outcome =
                await PackageExtractor.ExtractPackageAsync(
                        _context.HttpClient,
                        dependency.PackageName,
                        _context.Logger.Log,
                        sourceOptions: _sourceOptions,
                        version: dependency.Version)
                    .WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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
            string? cleanupDirectory = resolution is
            {
                FromCache: false,
                TempDir: not null,
            }
                ? resolution.TempDir
                : null;
            ProjectPackageDocumentData? document =
                ReadBestDocumentFromDirectory(
                    resolved,
                    cleanupDirectory);
            retainTemporaryDirectory =
                document is not null
                && cleanupDirectory is not null;
            return (document, null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
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
            if (!retainTemporaryDirectory
                && resolution is { FromCache: false, TempDir: not null }
                && Directory.Exists(resolution.TempDir))
            {
                try
                {
                    Directory.Delete(resolution.TempDir, recursive: true);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException)
                {
                    _context.Logger.LogWarning(
                        $"Could not remove temporary package directory "
                        + $"'{resolution.TempDir}': {ex.Message}");
                }
            }
        }
    }

    ProjectPackageDocumentData? ReadBestDocumentFromDirectory(
        ProjectPackageReference dependency,
        string? cleanupDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(dependency.PackagePath)
            || !Directory.Exists(dependency.PackagePath))
        {
            return null;
        }

        string? documentPath = ResolveDocumentPath(dependency.PackagePath);
        if (documentPath is null)
            return null;

        string fullPath = Path.Combine(
            dependency.PackagePath,
            documentPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return null;

        long size = new FileInfo(fullPath).Length;
        _contentStore.Add(
            ProjectSectionNames.PackageDocs,
            dependency.PackageName,
            documentPath,
            fullPath,
            cleanupDirectory);

        return new ProjectPackageDocumentData(
            dependency.PackageName,
            dependency.Version,
            documentPath,
            size,
            "");
    }

    static string? ResolveDocumentPath(string packagePath)
    {
        foreach (string candidate in DocumentCandidates)
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
}

internal sealed class ProjectDocumentContentStore(
    CommandContext context) : IDisposable
{
    readonly Dictionary<DocumentKey, string> _paths = [];
    readonly HashSet<string> _cleanupDirectories =
        new(StringComparer.Ordinal);

    public void Add(
        string section,
        string package,
        string path,
        string fullPath,
        string? cleanupDirectory = null)
    {
        _paths[new DocumentKey(section, package, path)] = fullPath;
        if (cleanupDirectory is not null)
            _cleanupDirectories.Add(cleanupDirectory);
    }

    public bool Contains(string section, string package, string path)
        => _paths.ContainsKey(new DocumentKey(section, package, path));

    public string? Read(string section, string package, string path)
    {
        if (!_paths.TryGetValue(
                new DocumentKey(section, package, path),
                out string? fullPath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            CommandError.WriteWarning(
                $"Could not read '{package}' file '{path}': {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        foreach (string directory in _cleanupDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                context.Logger.LogWarning(
                    $"Could not remove temporary package directory "
                    + $"'{directory}': {ex.Message}");
            }
        }
    }

    readonly record struct DocumentKey(
        string Section,
        string Package,
        string Path);
}
