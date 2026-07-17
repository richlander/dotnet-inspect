using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

static class ResourceExtractor
{
    readonly record struct ExtractionPlan(
        string Name,
        int Rva,
        int Size,
        string DestinationPath,
        string DestinationFullPath);

    static readonly char[] s_portableInvalidFileNameCharacters = ['<', '>', '"', '|', '?', '*'];

    public static List<string> ExtractAll(PEReader peReader, string outputDirectory)
    {
        if (!peReader.HasMetadata)
            return [];
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var reader = peReader.GetMetadataReader();
        var resourcesDirectory = peReader.PEHeaders.CorHeader!.ResourcesDirectory;
        if (resourcesDirectory.Size == 0)
            return [];

        var outputRoot = Path.GetFullPath(outputDirectory);
        var plans = CreatePlans(
            peReader,
            reader,
            resourcesDirectory.RelativeVirtualAddress,
            resourcesDirectory.Size,
            outputDirectory,
            outputRoot);
        if (plans.Count == 0)
            return [];

        EnsureDirectoryIsSafe(outputRoot, outputRoot);
        List<string> extracted = [];
        foreach (var plan in plans)
        {
            var bytes = peReader.GetSectionData(plan.Rva)
                .GetReader(4, plan.Size)
                .ReadBytes(plan.Size);
            EnsureDirectoryIsSafe(outputRoot, Path.GetDirectoryName(plan.DestinationFullPath)!);
            using (var destination = new FileStream(
                plan.DestinationFullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                destination.Write(bytes);
            }
            extracted.Add(plan.DestinationPath);
        }
        return extracted;
    }

    static int ReadResourceSize(
        PEReader peReader,
        int resourcesSize,
        long offset,
        int rva,
        string name)
    {
        try
        {
            if (offset < 0
                || resourcesSize < sizeof(int)
                || offset > resourcesSize - sizeof(int))
            {
                throw new BadImageFormatException();
            }

            var sectionData = peReader.GetSectionData(rva);
            if (sectionData.Length < 4)
                throw new BadImageFormatException();

            int size = sectionData.GetReader(0, 4).ReadInt32();
            long endOffset = offset + sizeof(int) + (long)size;
            if (size < 0
                || endOffset > resourcesSize
                || size + (long)sizeof(int) > sectionData.Length)
            {
                throw new BadImageFormatException();
            }
            return size;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Manifest resource '{name}' has an invalid data range.",
                ex);
        }
    }

    static List<ExtractionPlan> CreatePlans(
        PEReader peReader,
        MetadataReader reader,
        int resourcesRva,
        int resourcesSize,
        string outputDirectory,
        string outputRoot)
    {
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        List<ExtractionPlan> plans = [];

        foreach (var handle in reader.ManifestResources)
        {
            var resource = reader.GetManifestResource(handle);
            if (!resource.Implementation.IsNil)
                continue;

            string name = reader.GetString(resource.Name);
            var relativePath = Path.Combine(GetSafePathComponents(name));
            var destinationPath = Path.Combine(outputDirectory, relativePath);
            var destinationFullPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
            EnsureContained(outputRoot, destinationFullPath, name);

            if (!destinations.Add(destinationFullPath.Normalize()))
            {
                throw new InvalidDataException(
                    $"Manifest resource '{name}' resolves to a duplicate extraction path.");
            }

            int rva;
            try
            {
                rva = checked(resourcesRva + (int)resource.Offset);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException(
                    $"Manifest resource '{name}' has an invalid data offset.",
                    ex);
            }
            int size = ReadResourceSize(
                peReader,
                resourcesSize,
                resource.Offset,
                rva,
                name);
            plans.Add(new ExtractionPlan(
                name,
                rva,
                size,
                destinationPath,
                destinationFullPath));
        }

        foreach (var plan in plans)
        {
            var parent = Path.GetDirectoryName(plan.DestinationFullPath);
            while (parent is not null && !PathsEqual(parent, outputRoot))
            {
                if (destinations.Contains(parent.Normalize()))
                {
                    throw new InvalidDataException(
                        $"Manifest resource '{plan.Name}' conflicts with another resource path.");
                }
                if (!TryGetAttributes(parent, out _)
                    && FindExistingAlias(parent) is { } alias)
                {
                    throw new IOException(
                        $"Resource extraction path conflicts with existing path: '{alias}'.");
                }
                EnsureExistingDirectoryIsSafe(parent);
                parent = Path.GetDirectoryName(parent);
            }

            if (TryGetAttributes(plan.DestinationFullPath, out _)
                || FindExistingAlias(plan.DestinationFullPath) is not null)
            {
                throw new IOException(
                    $"Resource destination already exists: '{plan.DestinationFullPath}'.");
            }
        }

        EnsureExistingDirectoryIsSafe(outputRoot);
        return plans;
    }

    static string[] GetSafePathComponents(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Any(char.IsControl)
            || name[0] is '/' or '\\'
            || Path.IsPathRooted(name)
            || IsDriveQualified(name))
        {
            throw UnsafeResourceName(name);
        }

        var components = name.Split(['/', '\\'], StringSplitOptions.None);
        foreach (var component in components)
        {
            if (component.Length == 0
                || component is "." or ".."
                || component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || component.IndexOfAny(s_portableInvalidFileNameCharacters) >= 0
                || component.Contains(':')
                || component.EndsWith(' ')
                || component.EndsWith('.')
                || IsWindowsDeviceName(component))
            {
                throw UnsafeResourceName(name);
            }
        }
        return components;
    }

    static bool IsDriveQualified(string name)
        => name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':';

    static bool IsWindowsDeviceName(string component)
    {
        var separator = component.IndexOf('.');
        var stem = (separator >= 0 ? component[..separator] : component).TrimEnd(' ', '.');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)
            || IsNumberedDevice(stem, "COM")
            || IsNumberedDevice(stem, "LPT");
    }

    static bool IsNumberedDevice(string stem, string prefix)
        => stem.Length == prefix.Length + 1
            && stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && IsDeviceDigit(stem[^1]);

    static bool IsDeviceDigit(char value)
        => value is >= '1' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3';

    static InvalidDataException UnsafeResourceName(string name)
        => new($"Manifest resource '{name}' is not a safe relative extraction path.");

    static void EnsureContained(string outputRoot, string destination, string name)
    {
        var relative = Path.GetRelativePath(outputRoot, destination);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw UnsafeResourceName(name);
        }
    }

    static void EnsureDirectoryIsSafe(string outputRoot, string directory)
    {
        Directory.CreateDirectory(outputRoot);
        EnsureExistingDirectoryIsSafe(outputRoot);

        var relative = Path.GetRelativePath(outputRoot, directory);
        if (relative == ".")
            return;
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException($"Resource extraction directory escapes the output root: '{directory}'.");
        }

        var current = outputRoot;
        foreach (var component in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if (!TryGetAttributes(current, out _))
            {
                if (FindExistingAlias(current) is { } alias)
                {
                    throw new IOException(
                        $"Resource extraction path conflicts with existing path: '{alias}'.");
                }
                Directory.CreateDirectory(current);
            }
            EnsureExistingDirectoryIsSafe(current);
        }
    }

    static string? FindExistingAlias(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
            return null;

        string expectedName = Path.GetFileName(path).Normalize();
        foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
        {
            if (string.Equals(
                Path.GetFileName(entry).Normalize(),
                expectedName,
                StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }

    static void EnsureExistingDirectoryIsSafe(string path)
    {
        if (!TryGetAttributes(path, out var attributes))
            return;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"Resource extraction path contains a symbolic link or reparse point: '{path}'.");
        }
        if ((attributes & FileAttributes.Directory) == 0)
            throw new IOException($"Resource extraction path is not a directory: '{path}'.");
    }

    static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }
}
