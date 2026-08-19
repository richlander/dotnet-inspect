using System.Text;
using DotnetInspector.Commands;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

internal sealed class ProjectSkillsProvider
{
    enum SkillReadFailure
    {
        None,
        InvalidName,
        InvalidDescription,
    }

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
                    "a restored package skill listed in project.assets.json is missing from the package cache"));
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
                string content = File.ReadAllText(file.FullPath);
                IReadOnlyDictionary<string, string> frontmatter =
                    MarkdownContent.ParseYamlFrontmatter(content);
                SkillReadFailure validationFailure = ValidateMetadata(
                    file.Path,
                    frontmatter,
                    out string skillName,
                    out string description);
                if (validationFailure != SkillReadFailure.None)
                {
                    failures.Add(new ProjectContentFailure(
                        file.PackageName,
                        file.Path,
                        validationFailure == SkillReadFailure.InvalidName
                            ? "a restored package skill must declare an Agent Skills-compliant name that matches its containing directory"
                            : "a restored package skill must declare an Agent Skills-compliant description of 1 to 1024 characters"));
                    continue;
                }

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
                        skillName,
                        description,
                        ""));
                    continue;
                }

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
                    skillName,
                    description,
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

    static SkillReadFailure ValidateMetadata(
        string packagePath,
        IReadOnlyDictionary<string, string> frontmatter,
        out string name,
        out string description)
    {
        if (!TryGetName(packagePath, frontmatter, out name))
        {
            description = "";
            return SkillReadFailure.InvalidName;
        }

        frontmatter.TryGetValue("description", out string? candidateDescription);
        if (!IsValidDescription(candidateDescription))
        {
            description = "";
            return SkillReadFailure.InvalidDescription;
        }

        description = candidateDescription!;
        return SkillReadFailure.None;
    }

    static bool TryGetName(
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
            && IsValidName(name);
    }

    static bool IsValidName(string name)
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

    static bool IsValidDescription(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return false;

        int length = 0;
        foreach (Rune _ in description.EnumerateRunes())
        {
            if (++length > 1024)
                return false;
        }

        return true;
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
