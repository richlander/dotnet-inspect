using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// House-issued DLL candidates for Find, bound to one admitted directory
/// generation. Candidate membership does not establish image validity.
/// </summary>
public sealed class PackageHouseFindLibrarySelection
{
    private PackageHouseFindLibrarySelection(
        PackageHouseContentNarrowing.FindLibraries narrowing,
        PackageContentGenerationIdentity generation,
        string? selectedTargetFramework,
        IEnumerable<string> entryPaths)
    {
        Narrowing = narrowing;
        Generation = generation;
        SelectedTargetFramework = selectedTargetFramework;
        EntryPaths = Array.AsReadOnly(
            entryPaths.Order(StringComparer.Ordinal).ToArray());
    }

    public PackageHouseContentNarrowing.FindLibraries Narrowing { get; }

    public PackageContentGenerationIdentity Generation { get; }

    public string? SelectedTargetFramework { get; }

    public IReadOnlyList<string> EntryPaths { get; }

    internal static PackageHouseFindLibrarySelection Select(
        PackageHouseContentNarrowing.FindLibraries narrowing,
        PackageContentGenerationIdentity generation,
        IReadOnlyList<string> directoryEntries)
    {
        string[] candidates =
        [
            .. directoryEntries.Where(static path =>
                path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)),
        ];
        bool allFrameworks = string.Equals(
            narrowing.TargetFramework,
            "all",
            StringComparison.OrdinalIgnoreCase);
        if (narrowing.TargetFramework is null)
        {
            foreach (string root in new[] { "tools/", "ref/", "lib/" })
            {
                string[] rootCandidates =
                [
                    .. candidates.Where(path =>
                        path.StartsWith(root, StringComparison.Ordinal)),
                ];
                if (rootCandidates.Length != 0)
                {
                    candidates = rootCandidates;
                    break;
                }
            }
        }

        var admittedPaths = new HashSet<string>(
            directoryEntries,
            StringComparer.Ordinal);
        candidates =
        [
            .. candidates.Where(path => !IsSatellite(path, admittedPaths)),
        ];
        string? selectedTarget = allFrameworks
            ? null
            : narrowing.TargetFramework;
        if (selectedTarget is not null)
        {
            candidates =
            [
                .. candidates.Where(path => string.Equals(
                    TfmResolver.ExtractTfmFromPath(path),
                    selectedTarget,
                    StringComparison.OrdinalIgnoreCase)),
            ];
        }
        else if (!allFrameworks)
        {
            selectedTarget = candidates
                .Order(StringComparer.Ordinal)
                .Select(TfmResolver.ExtractTfmFromPath)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(FrameworkPriority)
                .FirstOrDefault();
            if (selectedTarget is not null)
            {
                candidates =
                [
                    .. candidates.Where(path => string.Equals(
                        TfmResolver.ExtractTfmFromPath(path),
                        selectedTarget,
                        StringComparison.OrdinalIgnoreCase)),
                ];
            }
        }

        return new(narrowing, generation, selectedTarget, candidates);
    }

    private static int FrameworkPriority(string framework)
    {
        int qualifier = framework.IndexOf('-');
        return TfmResolver.GetTfmPriority(
            (qualifier < 0 ? framework : framework[..qualifier]).Trim());
    }

    private static bool IsSatellite(
        string path,
        HashSet<string> admittedPaths)
    {
        const string suffix = ".resources.dll";
        if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        int fileSeparator = path.LastIndexOf('/');
        if (fileSeparator < 0)
            return false;
        int cultureSeparator = path.LastIndexOf('/', fileSeparator - 1);
        ReadOnlySpan<char> culture = path.AsSpan(
            cultureSeparator + 1,
            fileSeparator - cultureSeparator - 1);
        if (!TfmResolver.IsCultureFolderName(culture.ToString()))
            return false;

        string primaryPath = string.Concat(
            path.AsSpan(0, cultureSeparator + 1),
            path.AsSpan(
                fileSeparator + 1,
                path.Length - fileSeparator - 1 - suffix.Length),
            ".dll");
        return admittedPaths.Contains(primaryPath);
    }
}
