namespace CiChangeDetection.Planning;

/// <summary>
/// Compares exact-outcome mappings without treating comments or record order
/// as model changes. The runner still validates the complete current manifest.
/// </summary>
internal static class TlaManifestChanges
{
    internal const string ManifestPath = "eng/tla-expected-exit-codes.txt";

    internal static IReadOnlyList<byte[]> Compare(
        ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> after)
    {
        List<Mapping> previous = ReadMappings(before);
        List<Mapping> current = ReadMappings(after);
        List<byte[]> changed = [];
        foreach (Mapping mapping in previous)
        {
            Mapping? replacement = current.Find(candidate =>
                candidate.Path.AsSpan().SequenceEqual(mapping.Path));
            if (replacement is null
                || !replacement.Value.AsSpan().SequenceEqual(mapping.Value))
            {
                changed.Add(mapping.Path);
            }
        }

        foreach (Mapping mapping in current)
        {
            if (!previous.Exists(candidate =>
                candidate.Path.AsSpan().SequenceEqual(mapping.Path)))
            {
                changed.Add(mapping.Path);
            }
        }

        changed.Sort((left, right) =>
            left.AsSpan().SequenceCompareTo(right));
        return changed;
    }

    private static List<Mapping> ReadMappings(ReadOnlySpan<byte> content)
    {
        List<Mapping> mappings = [];
        int lineNumber = 0;
        foreach (Range range in content.Split((byte)'\n'))
        {
            lineNumber++;
            ReadOnlySpan<byte> line = content[range];
            if (line.IsEmpty || line[0] == (byte)'#')
            {
                continue;
            }

            int separator = line.IndexOf((byte)'=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceFraming,
                    $"the TLA manifest mapping at line {lineNumber} "
                    + "requires a path and value separated by '='");
            }

            byte[] path = line[..separator].ToArray();
            ChangePathRules.Validate(path);
            if (!IsConfigurationPath(path))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidencePath,
                    $"the TLA manifest mapping at line {lineNumber} "
                    + "does not name a supported configuration path");
            }

            if (mappings.Exists(mapping =>
                mapping.Path.AsSpan().SequenceEqual(path)))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceDuplicate,
                    $"the TLA manifest mapping at line {lineNumber} "
                    + "repeats a configuration path");
            }

            mappings.Add(new Mapping(path, line[(separator + 1)..].ToArray()));
        }

        return mappings;
    }

    private static bool IsConfigurationPath(ReadOnlySpan<byte> path)
    {
        ReadOnlySpan<byte> root = path.StartsWith("docs/design/models/"u8)
            ? "docs/design/models/"u8
            : "docs/models/"u8;
        if (!path.StartsWith(root) || !path.EndsWith(".cfg"u8))
        {
            return false;
        }

        ReadOnlySpan<byte> relative = path[root.Length..];
        int separator = relative.IndexOf((byte)'/');
        return separator > 0
            && relative[(separator + 1)..].IndexOf((byte)'/') < 0;
    }

    private sealed record Mapping(byte[] Path, byte[] Value);
}
