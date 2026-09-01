using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services;

/// <summary>
/// TFM selection and assembly discovery within package layouts.
/// </summary>
public static class TfmSelector
{
    private static List<string> FilterResourceAssemblies(IEnumerable<string> dlls)
        => dlls.Where(d => !IsSatelliteResourceAssembly(d)).ToList();

    public enum PackageLibraryResolutionStatus
    {
        Selected,
        NoAssemblies,
        NoMatchingTargetFramework,
        RequestedLibraryNotFound,
        Ambiguous
    }

    public sealed record PackageLibraryResolution(
        IReadOnlyList<string> Paths,
        string? Tfm,
        PackageLibraryResolutionStatus Status,
        IReadOnlyList<string> CandidatePaths)
    {
        public bool IsSelected => Status == PackageLibraryResolutionStatus.Selected && Paths.Count > 0;
    }

    public static IOrderedEnumerable<T> OrderByTfmPriorityDescending<T>(
        IEnumerable<T> items,
        Func<T, string?> tfmSelector)
    {
        return items.OrderByDescending(item => GetTfmPriority(tfmSelector(item)));
    }

    public static int GetTfmPriority(string? tfm)
    {
        string normalized = NormalizeTfm(tfm);
        int qualifierIndex = normalized.IndexOf('-');
        return TfmResolver.GetTfmPriority(
            qualifierIndex < 0
                ? normalized
                : normalized[..qualifierIndex]);
    }

    public static string? SelectHighestTfm(IEnumerable<string> tfms)
    {
        return OrderByTfmPriorityDescending(tfms, tfm => tfm).FirstOrDefault();
    }

    /// <summary>
    /// Normalizes short and NuGet long-form framework monikers to the product's package-layout
    /// spelling.
    /// </summary>
    public static string NormalizeTfm(string? tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm))
            return "";

        var value = tfm.Trim();
        var slashIndex = value.IndexOf('/');
        if (slashIndex >= 0)
            value = value[..slashIndex];

        var commaIndex = value.IndexOf(',');
        if (commaIndex >= 0)
        {
            var frameworkName = value[..commaIndex];
            string[] attributes = value[(commaIndex + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            string? version = AttributeValue(attributes, "Version");
            if (version != null)
            {
                string? normalized = NormalizeLongFormTfm(
                    frameworkName,
                    version.TrimStart('v', 'V'));
                return normalized is null
                    ? value
                    : AppendLongFormQualifiers(normalized, attributes);
            }
        }

        foreach (var prefix in new[] { ".NETStandard", ".NETFramework", ".NETCoreApp" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeLongFormTfm(prefix, value[prefix.Length..].TrimStart('v', 'V')) ?? value;
            }
        }

        return NormalizeShortFormPlatformVersion(value);
    }

    private static string NormalizeShortFormPlatformVersion(string value)
    {
        int qualifierIndex = value.IndexOf('-');
        if (qualifierIndex < 0)
            return value;

        int versionIndex = qualifierIndex + 1;
        while (versionIndex < value.Length
            && !char.IsAsciiDigit(value[versionIndex]))
        {
            versionIndex++;
        }

        if (versionIndex == value.Length
            || !TryNormalizeVersion(
                value[versionIndex..],
                out _,
                out string dotted,
                out _))
        {
            return value;
        }

        return value[..versionIndex] + dotted;
    }

    private static string? AttributeValue(
        IEnumerable<string> attributes,
        string name)
        => attributes
            .Select(attribute => attribute.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .FirstOrDefault(parts => parts[0].Equals(
                name,
                StringComparison.OrdinalIgnoreCase))?[1];

    private static string AppendLongFormQualifiers(
        string normalized,
        IEnumerable<string> attributes)
    {
        string? profile = AttributeValue(attributes, "Profile");
        if (!string.IsNullOrWhiteSpace(profile))
            normalized += "-" + NormalizeQualifier(profile);

        string? platform = AttributeValue(attributes, "Platform");
        if (!string.IsNullOrWhiteSpace(platform))
        {
            normalized += "-" + NormalizeQualifier(platform);
            string? platformVersion = AttributeValue(
                attributes,
                "PlatformVersion");
            if (!string.IsNullOrWhiteSpace(platformVersion))
            {
                string version = platformVersion
                    .Trim()
                    .TrimStart('v', 'V');
                normalized += TryNormalizeVersion(
                    version,
                    out _,
                    out string dotted,
                    out _)
                        ? dotted
                        : version;
            }
        }

        return normalized;
    }

    private static string NormalizeQualifier(string value)
        => value
            .Trim()
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string? NormalizeLongFormTfm(string frameworkName, string version)
    {
        if (!TryNormalizeVersion(
                version,
                out int major,
                out string dotted,
                out string compact))
            return null;

        if (frameworkName.Equals(".NETStandard", StringComparison.OrdinalIgnoreCase))
            return "netstandard" + dotted;

        if (frameworkName.Equals(".NETFramework", StringComparison.OrdinalIgnoreCase))
            return "net" + compact;

        if (frameworkName.Equals(".NETCoreApp", StringComparison.OrdinalIgnoreCase))
        {
            return major >= 5
                ? "net" + dotted
                : "netcoreapp" + dotted;
        }

        return null;
    }

    private static bool TryNormalizeVersion(
        string value,
        out int major,
        out string dotted,
        out string compact)
    {
        major = 0;
        dotted = "";
        compact = "";
        string[] parts = value.Trim().Split('.');
        if (parts.Length is 0 or > 4)
            return false;

        int[] numbers = new int[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out numbers[index])
                || numbers[index] < 0)
            {
                return false;
            }
        }

        int last = numbers.Length - 1;
        while (last > 1 && numbers[last] == 0)
            last--;

        major = numbers[0];
        dotted = string.Join('.', numbers[..(last + 1)]);
        compact = string.Concat(numbers[..(last + 1)]);
        return true;
    }

    public static List<string> GetPackageDlls(string extractPath)
    {
        var toolsDir = Path.Combine(extractPath, "tools");
        var refDir = Path.Combine(extractPath, "ref");
        var libDir = Path.Combine(extractPath, "lib");

        string[] candidates = [];
        if (Directory.Exists(toolsDir))
        {
            candidates = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories);
        }

        // Ref packages (e.g. Microsoft.NETCore.App.Ref) put assemblies in ref/
        if (candidates.Length == 0 && Directory.Exists(refDir))
        {
            candidates = Directory.GetFiles(refDir, "*.dll", SearchOption.AllDirectories);
        }

        if (candidates.Length == 0 && Directory.Exists(libDir))
        {
            candidates = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories);
        }

        if (candidates.Length == 0)
        {
            candidates = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
        }

        return candidates.OrderBy(f => f).ToList();
    }

    public static List<string> GetPackageAssemblies(string extractPath)
        => FilterResourceAssemblies(GetPackageDlls(extractPath));

    public static List<string> GetPackageTfms(string extractPath)
        => GetPackageTfms(GetPackageDlls(extractPath), extractPath);

    public static List<string> GetPackageTfms(IEnumerable<string> paths, string extractPath)
        => OrderByTfmPriorityDescending(
                paths.Select(path => GetTfm(extractPath, path))
                    .Where(tfm => tfm != null)
                    .Select(tfm => tfm!)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                tfm => tfm)
            .ToList();

    public static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string extractPath, string? packageName = null)
    {
        dlls = FilterResourceAssemblies(dlls);

        var byTfm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in dlls)
        {
            var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
            var tfm = TfmResolver.ExtractTfmFromPath(relativePath);
            if (tfm != null)
            {
                if (!byTfm.TryGetValue(tfm, out var list))
                {
                    list = [];
                    byTfm[tfm] = list;
                }
                list.Add(dll);
            }
        }

        if (byTfm.Count == 0)
            return (null, null);

        var highestTfm = SelectHighestTfm(byTfm.Keys)!;
        var assemblies = byTfm[highestTfm];

        // Prefer assembly matching the package name
        if (packageName != null)
        {
            var match = assemblies.FirstOrDefault(d =>
                Path.GetFileNameWithoutExtension(d).Equals(packageName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return (match, highestTfm);
        }

        var directDll = assemblies.FirstOrDefault(d =>
        {
            var relativePath = Path.GetRelativePath(extractPath, d).Replace('\\', '/');
            var parts = relativePath.Split('/');
            return parts.Length <= 3;
        });

        return (directDll ?? assemblies[0], highestTfm);
    }

    /// <summary>
    /// Returns ALL assemblies at the highest TFM (for multi-library packages).
    /// Filters out resource assemblies.
    /// </summary>
    public static (List<string> paths, string? tfm) SelectHighestTfmAssemblies(List<string> dlls, string extractPath)
    {
        dlls = FilterResourceAssemblies(dlls);

        var byTfm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in dlls)
        {
            var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
            var tfm = TfmResolver.ExtractTfmFromPath(relativePath);
            if (tfm != null)
            {
                if (!byTfm.TryGetValue(tfm, out var list))
                {
                    list = [];
                    byTfm[tfm] = list;
                }
                list.Add(dll);
            }
        }

        if (byTfm.Count == 0)
            return ([], null);

        var highestTfm = SelectHighestTfm(byTfm.Keys)!;

        return (byTfm[highestTfm], highestTfm);
    }

    public static (List<string> paths, string? tfm) SelectHighestAssembliesFromPackage(string extractPath, string? tfm = null)
    {
        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
            return (GetAllPackageAssemblies(extractPath), null);

        return !string.IsNullOrWhiteSpace(tfm)
            ? SelectAssembliesByTfmFromPackage(extractPath, tfm)
            : SelectHighestAssemblies(GetPackageDlls(extractPath), extractPath, tfm);
    }

    private static List<string> GetAllPackageAssemblies(string extractPath)
        => FilterResourceAssemblies(Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories))
            .OrderBy(path => GetExplicitTfmLookupPriority(extractPath, path))
            .ThenBy(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static (List<string> paths, string? tfm) SelectAssembliesByTfmFromPackage(string extractPath, string tfm)
    {
        var selected = GetAllPackageAssemblies(extractPath)
            .Where(path => string.Equals(GetTfm(extractPath, path), tfm, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (selected, tfm);
    }

    public static (List<string> paths, string? tfm) SelectHighestAssemblies(List<string> dlls, string extractPath, string? tfm = null)
    {
        dlls = FilterResourceAssemblies(dlls);
        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
            return (dlls, null);

        if (dlls.Count == 0)
            return ([], string.IsNullOrWhiteSpace(tfm) ? null : tfm);

        if (!string.IsNullOrWhiteSpace(tfm))
        {
            var selected = dlls
                .Where(path => string.Equals(GetTfm(extractPath, path), tfm, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return (selected, tfm);
        }

        var (highestTfmDlls, highestTfm) = SelectHighestTfmAssemblies(dlls, extractPath);
        return highestTfmDlls.Count > 0 ? (highestTfmDlls, highestTfm) : (dlls, null);
    }

    public static PackageLibraryResolution SelectPackageLibrary(
        string extractPath,
        string packageId,
        string? requestedLibrary,
        string? tfm = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedLibrary))
        {
            var (matchedAssembly, matchedTfm) = FindAssemblyInPackage(extractPath, requestedLibrary, tfm);
            return matchedAssembly != null
                ? new PackageLibraryResolution([matchedAssembly], matchedTfm, PackageLibraryResolutionStatus.Selected, [matchedAssembly])
                : new PackageLibraryResolution([], tfm, PackageLibraryResolutionStatus.RequestedLibraryNotFound, GetCandidateLibraries(extractPath, tfm));
        }

        var resolution = SelectPackageLibraries(extractPath, tfm);
        if (!resolution.IsSelected)
            return resolution;

        if (resolution.Paths.Count == 1)
            return new PackageLibraryResolution([resolution.Paths[0]], resolution.Tfm, PackageLibraryResolutionStatus.Selected, resolution.CandidatePaths);

        var packageNameMatches = resolution.Paths
            .Where(path => Path.GetFileNameWithoutExtension(path).Equals(packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return packageNameMatches.Count == 1
            ? new PackageLibraryResolution([packageNameMatches[0]], resolution.Tfm, PackageLibraryResolutionStatus.Selected, resolution.CandidatePaths)
            : new PackageLibraryResolution([], resolution.Tfm, PackageLibraryResolutionStatus.Ambiguous, resolution.Paths);
    }

    public static PackageLibraryResolution SelectPackageLibraries(string extractPath, string? tfm = null)
    {
        List<string> selected;
        string? selectedTfm;
        if (string.IsNullOrWhiteSpace(tfm))
        {
            var candidates = GetPackageAssemblies(extractPath);
            if (candidates.Count == 0)
                return new PackageLibraryResolution([], null, PackageLibraryResolutionStatus.NoAssemblies, []);

            (selected, selectedTfm) = SelectHighestAssemblies(candidates, extractPath);
        }
        else
        {
            (selected, selectedTfm) = SelectHighestAssembliesFromPackage(extractPath, tfm);
        }

        if (selected.Count == 0)
            return new PackageLibraryResolution([], tfm, PackageLibraryResolutionStatus.NoMatchingTargetFramework, GetCandidateLibraries(extractPath, tfm));

        var ordered = selected
            .OrderBy(path => Path.GetRelativePath(extractPath, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackageLibraryResolution(ordered, selectedTfm, PackageLibraryResolutionStatus.Selected, ordered);
    }

    private static List<string> GetCandidateLibraries(string extractPath, string? tfm)
        => string.IsNullOrWhiteSpace(tfm)
            ? GetPackageAssemblies(extractPath)
            : SelectHighestAssembliesFromPackage(extractPath, tfm).paths;

    public static (string? path, string? tfm) FindAssemblyInPackage(string extractPath, string assemblyName, string? tfm = null)
    {
        var dlls = !string.IsNullOrEmpty(tfm)
            ? SelectAssembliesByTfmFromPackage(extractPath, tfm).paths
            : GetPackageAssemblies(extractPath);
        return FindAssemblyInPackage(dlls, extractPath, assemblyName, tfm);
    }

    internal static (string? path, string? tfm) FindAssemblyInPackage(
        IReadOnlyList<string> dlls,
        string extractPath,
        string assemblyName,
        string? tfm)
    {
        if (dlls.Count == 0)
            return (null, null);

        var normalizedAssemblyName = assemblyName.Replace('\\', '/');
        var assemblyLeaf = Path.GetFileName(assemblyName);
        var bareName = assemblyLeaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(assemblyLeaf)
            : assemblyLeaf;
        var fileName = assemblyLeaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assemblyLeaf
            : $"{bareName}.dll";

        string? exactMatch = FindExactPackageAsset(
            dlls,
            extractPath,
            normalizedAssemblyName);
        if (exactMatch is not null)
        {
            string? exactTfm = TfmResolver.ExtractTfmFromPath(
                Path.GetRelativePath(extractPath, exactMatch).Replace('\\', '/'));
            return (exactMatch, exactTfm ?? tfm);
        }

        if (normalizedAssemblyName.Contains('/'))
            return (null, null);

        var matchingFiles = dlls
            .Where(dll =>
            {
                return Path.GetFileName(dll).Equals(fileName, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(dll).Equals(bareName, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (matchingFiles.Count == 0)
            return (null, null);

        var (selectedPath, selectedTfm) = SelectHighestTfmAssembly(matchingFiles, extractPath);
        return (selectedPath ?? matchingFiles[0], selectedTfm ?? tfm);
    }

    internal static string? FindExactPackageAsset(
        IReadOnlyList<string> paths,
        string extractPath,
        string normalizedAssemblyName)
    {
        string normalizedWithExtension = normalizedAssemblyName.EndsWith(
            ".dll",
            StringComparison.OrdinalIgnoreCase)
            ? normalizedAssemblyName
            : normalizedAssemblyName + ".dll";
        string? exact = paths.FirstOrDefault(path =>
            Path.GetRelativePath(extractPath, path)
                .Replace('\\', '/')
                .Equals(normalizedWithExtension, StringComparison.Ordinal));
        if (exact is not null)
            return exact;
        return null;
    }

    public static (string? path, string? tfm) FindAssemblyContainingType(string extractPath, string typeName, string? tfm = null)
    {
        var dlls = !string.IsNullOrEmpty(tfm)
            ? SelectAssembliesByTfmFromPackage(extractPath, tfm).paths
            : GetPackageAssemblies(extractPath);
        if (dlls.Count == 0)
            return (null, null);

        string? selectedTfm = tfm;
        var candidateDlls = new List<string>();

        if (!string.IsNullOrEmpty(tfm))
        {
            candidateDlls = dlls;
            selectedTfm = tfm;
        }
        else
        {
            (candidateDlls, selectedTfm) = SelectHighestAssemblies(dlls, extractPath);
        }

        foreach (var dll in candidateDlls)
        {
            if (PlatformResolver.HasType(dll, typeName))
            {
                selectedTfm ??= TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, dll).Replace('\\', '/'));
                return (dll, selectedTfm);
            }
        }

        // Fallback: if the highest-TFM scan misses, search the remaining DLLs so
        // `find` results from multi-library packages still lead to a working follow-up.
        foreach (var dll in dlls.Except(candidateDlls))
        {
            if (PlatformResolver.HasType(dll, typeName))
            {
                var matchedTfm = TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, dll).Replace('\\', '/'));
                return (dll, matchedTfm ?? selectedTfm);
            }
        }

        return (null, selectedTfm);
    }

    private static string? GetTfm(string extractPath, string path)
        => TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, path).Replace('\\', '/'));

    public static string? FindAssemblyByTfm(string extractPath, string tfm, string? packageName = null)
    {
        var (dlls, _) = SelectAssembliesByTfmFromPackage(extractPath, tfm);
        if (dlls.Count == 0)
            return null;

        var (selectedPath, _) = SelectHighestTfmAssembly(dlls, extractPath, packageName);
        return selectedPath ?? dlls[0];
    }

    private static int GetExplicitTfmLookupPriority(string extractPath, string path)
    {
        var parts = Path.GetRelativePath(extractPath, path).Replace('\\', '/').Split('/');
        if (parts.Length == 0)
            return 4;

        return parts[0] switch
        {
            "ref" => 0,
            "lib" => 1,
            "runtimes" => 2,
            "tools" => 3,
            _ => 4
        };
    }

    private static bool IsSatelliteResourceAssembly(string path)
    {
        if (!Path.GetFileName(path).EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            return false;

        var parentDirectory = Directory.GetParent(path);
        if (!IsCultureDirectoryName(parentDirectory?.Name))
            return false;

        var primaryAssemblyName = Path.GetFileName(path)[..^".resources.dll".Length] + ".dll";
        var primaryAssemblyPath = Path.Combine(
            parentDirectory!.Parent?.FullName ?? "",
            primaryAssemblyName);
        return File.Exists(primaryAssemblyPath);
    }

    private static bool IsCultureDirectoryName(string? name)
        => TfmResolver.IsCultureFolderName(name);
}
