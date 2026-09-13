namespace DotnetInspector.Platforms.Installed;

internal sealed class InstalledObservationBudget
{
    private readonly int _maximum;

    internal InstalledObservationBudget(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        _maximum = maximum;
    }

    internal void Observe()
    {
        if (Count == _maximum)
            throw new InstalledObservationLimitException();
        Count++;
    }

    internal int Count { get; private set; }
}

internal static class InstalledHiveFileSystem
{
    internal static List<string> EnumerateEntriesBounded(
        string path,
        InstalledObservationBudget observation,
        CancellationToken cancellationToken)
    {
        var entries = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observation.Observe();
            entries.Add(entry);
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    internal static string? FindExactChild(
        string parent,
        string expectedName,
        InstalledEntryKind expectedKind,
        InstalledObservationBudget observation,
        CancellationToken cancellationToken)
    {
        bool foundCaseVariant = false;
        foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observation.Observe();

            string actualName = Path.GetFileName(entry);
            if (string.Equals(
                    actualName,
                    expectedName,
                    StringComparison.Ordinal))
            {
                bool isDirectory = IsDirectory(entry);
                if (isDirectory != (expectedKind == InstalledEntryKind.Directory))
                    throw new InstalledInvalidLayoutException();
                return entry;
            }

            foundCaseVariant |= string.Equals(
                actualName,
                expectedName,
                StringComparison.OrdinalIgnoreCase);
        }

        if (foundCaseVariant)
            throw new InstalledInvalidLayoutException();
        return null;
    }

    internal static bool IsDirectory(string path) =>
        (File.GetAttributes(path) & FileAttributes.Directory) != 0;

    internal static InstalledDirectoryProbe ProbeDirectory(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0
                ? InstalledDirectoryProbe.Directory
                : InstalledDirectoryProbe.NotDirectory;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or DirectoryNotFoundException)
        {
            return InstalledDirectoryProbe.Missing;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return InstalledDirectoryProbe.Failed;
        }
    }
}

internal sealed class InstalledObservationLimitException : Exception
{
}

internal sealed class InstalledInvalidLayoutException : Exception
{
}

internal enum InstalledEntryKind
{
    File,
    Directory,
}

internal enum InstalledDirectoryProbe
{
    Missing,
    Directory,
    NotDirectory,
    Failed,
}
