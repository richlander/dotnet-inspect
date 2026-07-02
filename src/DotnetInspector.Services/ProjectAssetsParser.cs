using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services;

public sealed record ProjectPackageReference(
    string PackageName,
    string Version,
    string? PackagePath,
    string TargetFramework);

/// <summary>
/// Outcome of locating a project's <c>project.assets.json</c>.
/// </summary>
public enum ProjectAssetsStatus
{
    /// <summary>The assets file was located.</summary>
    Found,

    /// <summary>The supplied project path does not exist as a file or directory.</summary>
    ProjectNotFound,

    /// <summary>The project exists but no <c>project.assets.json</c> was found (needs <c>dotnet restore</c>).</summary>
    AssetsNotRestored,
}

/// <summary>
/// Parses project.assets.json to discover NuGet package assemblies in a project.
/// </summary>
public static class ProjectAssetsParser
{
    /// <summary>
    /// Locates a project's <c>project.assets.json</c> and reports why discovery failed.
    /// Accepts a directory, a project file, or a direct <c>project.assets.json</c> path.
    /// Returns <see langword="true"/> and a non-null <paramref name="assetsPath"/> when found.
    /// </summary>
    public static bool TryFindAssets(string projectPath, [NotNullWhen(true)] out string? assetsPath, out ProjectAssetsStatus status)
    {
        assetsPath = FindAssets(projectPath);
        if (assetsPath is not null)
        {
            status = ProjectAssetsStatus.Found;
            return true;
        }

        status = HasProject(projectPath)
            ? ProjectAssetsStatus.AssetsNotRestored
            : ProjectAssetsStatus.ProjectNotFound;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="projectPath"/> points at a project file or a directory
    /// containing one. Used to distinguish "not a project" from "project exists but not
    /// restored" once <see cref="FindAssets"/> has failed.
    /// </summary>
    static bool HasProject(string projectPath)
    {
        var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(projectPath) ? "." : projectPath);
        if (File.Exists(fullPath))
            return Path.GetExtension(fullPath).EndsWith("proj", StringComparison.OrdinalIgnoreCase);

        if (Directory.Exists(fullPath))
        {
            foreach (var _ in Directory.EnumerateFiles(fullPath, "*.*proj", SearchOption.TopDirectoryOnly))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns a human-readable, severity-free explanation for a failed
    /// <see cref="TryFindAssets"/> lookup. Callers prepend their own
    /// <c>Warning:</c>/<c>Error:</c> prefix per their control flow.
    /// </summary>
    public static string DescribeMissingAssets(string projectPath, ProjectAssetsStatus status) => status switch
    {
        ProjectAssetsStatus.ProjectNotFound => $"Project not found '{projectPath}'.",
        ProjectAssetsStatus.AssetsNotRestored => $"project.assets.json not found for '{projectPath}'. Run 'dotnet restore'.",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status does not describe a failure."),
    };

    public static string? FindAssets(string projectPath)
    {
        var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(projectPath) ? "." : projectPath);
        if (File.Exists(fullPath)
            && Path.GetFileName(fullPath).Equals("project.assets.json", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        string projectDir;
        string projectName;
        if (Directory.Exists(fullPath))
        {
            projectDir = fullPath;
            var projectFiles = Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly);
            projectName = projectFiles.Length == 1
                ? Path.GetFileNameWithoutExtension(projectFiles[0])
                : new DirectoryInfo(projectDir).Name;
        }
        else if (File.Exists(fullPath))
        {
            projectDir = Path.GetDirectoryName(fullPath) ?? "";
            projectName = Path.GetFileNameWithoutExtension(fullPath);
        }
        else
        {
            return null;
        }

        var candidatePaths = new[]
        {
            Path.Combine(projectDir, "obj", "project.assets.json"),
            Path.Combine(projectDir, "..", "..", "artifacts", "obj", projectName, "project.assets.json"),
            Path.Combine(projectDir, "artifacts", "obj", projectName, "project.assets.json")
        };

        foreach (var candidate in candidatePaths)
        {
            var normalized = Path.GetFullPath(candidate);
            if (File.Exists(normalized))
                return normalized;
        }

        return null;
    }

    /// <summary>
    /// Parses a project.assets.json file and returns the assembly paths with package metadata.
    /// </summary>
    public static List<(string Path, string PackageName, string Version)> Parse(string assetsPath, string? tfmFilter, Action<string>? log)
    {
        List<(string Path, string PackageName, string Version)> results = [];
        var nugetCache = NuGetCache.GetNuGetCachePath();

        try
        {
            var json = File.ReadAllText(assetsPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("targets", out var targets))
                return results;

            var selectedTfm = SelectTargetFramework(targets, tfmFilter);

            if (selectedTfm == null)
                return results;

            log?.Invoke($"Using target framework: {selectedTfm}");

            if (!doc.RootElement.TryGetProperty("libraries", out var libraries))
                return results;

            var targetDeps = targets.GetProperty(selectedTfm);

            foreach (var dep in targetDeps.EnumerateObject())
            {
                var parts = dep.Name.Split('/');
                if (parts.Length != 2) continue;

                var packageName = parts[0];
                var version = parts[1];

                if (!libraries.TryGetProperty(dep.Name, out var libInfo))
                    continue;

                if (libInfo.TryGetProperty("type", out var typeElem) && typeElem.GetString() == "project")
                    continue;

                if (!libInfo.TryGetProperty("path", out var pathElem))
                    continue;

                var packagePath = pathElem.GetString();
                if (string.IsNullOrEmpty(packagePath))
                    continue;

                if (dep.Value.TryGetProperty("compile", out var compile))
                {
                    foreach (var asm in compile.EnumerateObject())
                    {
                        if (!asm.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (asm.Name.Contains("_._"))
                            continue;

                        var fullPath = Path.Combine(nugetCache, packagePath, asm.Name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullPath))
                        {
                            results.Add((fullPath, packageName, version));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Failed to parse project.assets.json: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Parses a project.assets.json file and returns direct package references with resolved versions.
    /// </summary>
    public static List<ProjectPackageReference> ParsePackageReferences(string assetsPath, string? tfmFilter, Action<string>? log)
    {
        List<ProjectPackageReference> results = [];

        try
        {
            var json = File.ReadAllText(assetsPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("targets", out var targets))
                return results;
            if (!doc.RootElement.TryGetProperty("libraries", out var libraries))
                return results;

            var selectedTfm = SelectTargetFramework(targets, tfmFilter);
            if (selectedTfm == null)
                return results;

            log?.Invoke($"Using target framework: {selectedTfm}");
            var baseTfm = GetBaseTfm(selectedTfm);
            if (!TryGetFrameworkDependencies(doc.RootElement, baseTfm, out var frameworkDependencies))
                return results;

            var targetDependencies = targets.GetProperty(selectedTfm);
            var directPackages = new List<string>();
            foreach (var dependency in frameworkDependencies.EnumerateObject())
            {
                if (IsProjectDependency(dependency.Value))
                    continue;

                directPackages.Add(dependency.Name);
            }

            foreach (var packageName in directPackages)
            {
                if (TryResolvePackage(packageName, targetDependencies, libraries, selectedTfm, out var packageReference))
                    results.Add(packageReference);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Failed to parse project.assets.json: {ex.Message}");
        }

        return results
            .OrderBy(result => result.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? SelectTargetFramework(JsonElement targets, string? tfmFilter)
    {
        string? selectedTfm = null;
        foreach (var target in targets.EnumerateObject())
        {
            var tfmName = target.Name;
            var baseTfm = GetBaseTfm(tfmName);

            if (!string.IsNullOrEmpty(tfmFilter))
            {
                if (baseTfm.Equals(tfmFilter, StringComparison.OrdinalIgnoreCase))
                {
                    selectedTfm = tfmName;
                    break;
                }
            }
            else if (selectedTfm == null
                || TfmResolver.GetTfmPriority(baseTfm) > TfmResolver.GetTfmPriority(GetBaseTfm(selectedTfm)))
            {
                selectedTfm = tfmName;
            }
        }

        return selectedTfm;
    }

    private static string GetBaseTfm(string tfmName)
        => tfmName.Contains('/') ? tfmName[..tfmName.IndexOf('/')] : tfmName;

    private static bool TryGetFrameworkDependencies(JsonElement root, string baseTfm, out JsonElement dependencies)
    {
        dependencies = default;
        if (!root.TryGetProperty("project", out var project)
            || !project.TryGetProperty("frameworks", out var frameworks)
            || !TryGetPropertyCaseInsensitive(frameworks, baseTfm, out var framework)
            || !framework.TryGetProperty("dependencies", out dependencies))
        {
            return false;
        }

        return dependencies.ValueKind == JsonValueKind.Object;
    }

    private static bool TryResolvePackage(
        string packageName,
        JsonElement targetDependencies,
        JsonElement libraries,
        string targetFramework,
        out ProjectPackageReference packageReference)
    {
        packageReference = null!;

        foreach (var targetDependency in targetDependencies.EnumerateObject())
        {
            if (!TrySplitPackageKey(targetDependency.Name, out var resolvedName, out var version)
                || !resolvedName.Equals(packageName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryGetPropertyCaseInsensitive(libraries, targetDependency.Name, out var library)
                || IsProjectLibrary(library))
            {
                return false;
            }

            packageReference = new ProjectPackageReference(
                resolvedName,
                version,
                ResolvePackagePath(library),
                targetFramework);
            return true;
        }

        foreach (var libraryEntry in libraries.EnumerateObject())
        {
            if (!TrySplitPackageKey(libraryEntry.Name, out var resolvedName, out var version)
                || !resolvedName.Equals(packageName, StringComparison.OrdinalIgnoreCase)
                || IsProjectLibrary(libraryEntry.Value))
            {
                continue;
            }

            packageReference = new ProjectPackageReference(
                resolvedName,
                version,
                ResolvePackagePath(libraryEntry.Value),
                targetFramework);
            return true;
        }

        return false;
    }

    private static bool IsProjectDependency(JsonElement dependency)
    {
        if (dependency.ValueKind != JsonValueKind.Object)
            return false;

        return dependency.TryGetProperty("target", out var target)
            && target.GetString()?.Equals("Project", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsProjectLibrary(JsonElement library)
        => library.ValueKind == JsonValueKind.Object
           && library.TryGetProperty("type", out var type)
           && type.GetString()?.Equals("project", StringComparison.OrdinalIgnoreCase) == true;

    private static string? ResolvePackagePath(JsonElement library)
    {
        if (library.ValueKind != JsonValueKind.Object
            || !library.TryGetProperty("path", out var pathElement))
        {
            return null;
        }

        var packagePath = pathElement.GetString();
        if (string.IsNullOrWhiteSpace(packagePath))
            return null;

        var normalized = packagePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(NuGetCache.GetNuGetCachePath(), normalized));
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TrySplitPackageKey(string key, out string packageName, out string version)
    {
        var separator = key.LastIndexOf('/');
        if (separator <= 0 || separator == key.Length - 1)
        {
            packageName = "";
            version = "";
            return false;
        }

        packageName = key[..separator];
        version = key[(separator + 1)..];
        return true;
    }
}
