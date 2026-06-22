namespace DotnetInspector.BuildEvents;

public sealed record BuildEventLogResolution(string Input, string Path, string? EventLogId);

public static class BuildEventLogResolver
{
    public static BuildEventLogResolution Resolve(string input)
    {
        string expandedInput = ExpandUserPath(input);
        if (File.Exists(expandedInput))
        {
            string path = Path.GetFullPath(expandedInput);
            return new BuildEventLogResolution(input, path, Path.GetFileNameWithoutExtension(path));
        }

        if (LooksLikePath(input))
        {
            throw new FileNotFoundException($"Build event stream '{input}' not found.", expandedInput);
        }

        foreach (string candidate in FindManagedEventLogs(input))
        {
            return new BuildEventLogResolution(input, candidate, Path.GetFileNameWithoutExtension(candidate));
        }

        throw new FileNotFoundException($"Build event stream or EventLogId '{input}' not found.", input);
    }

    private static IEnumerable<string> FindManagedEventLogs(string eventLogId)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "build-events");

        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(root, eventLogId + ".jsonl", SearchOption.AllDirectories)
            .OrderByDescending(static path => path, StringComparer.Ordinal))
        {
            yield return file;
        }
    }

    private static bool LooksLikePath(string input)
    {
        return input.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            input.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            string.Equals(Path.GetExtension(input), ".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExpandUserPath(string input)
    {
        if (input == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        string homePrefix = "~" + Path.DirectorySeparatorChar;
        if (input.StartsWith(homePrefix, StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                input[homePrefix.Length..]);
        }

        return input;
    }
}
