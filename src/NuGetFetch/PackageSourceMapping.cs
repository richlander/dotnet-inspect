namespace NuGetFetch;

/// <summary>
/// Package-id patterns that authorize configured package-source names.
/// </summary>
public sealed class PackageSourceMapping
{
    private readonly IReadOnlyList<SourcePatterns> _sources;

    internal PackageSourceMapping(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> sources)
    {
        _sources =
        [
            .. sources.Select(source => new SourcePatterns(source.Key, source.Value)),
        ];
    }

    /// <summary>
    /// Gets whether configuration declared at least one mapped source.
    /// </summary>
    public bool IsEnabled => _sources.Count > 0;

    /// <summary>
    /// Returns the configured source names authorized to serve <paramref name="packageId"/>.
    /// </summary>
    /// <remarks>
    /// Exact package-id patterns win over prefix patterns. Otherwise, the longest matching
    /// prefix ending in <c>*</c> wins, with <c>*</c> as the least-specific default. More than
    /// one source may declare the winning pattern.
    /// </remarks>
    public IReadOnlyList<string> GetConfiguredPackageSources(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        List<string> exact = [];
        foreach (SourcePatterns source in _sources)
        {
            if (source.Patterns.Any(pattern =>
                !pattern.EndsWith('*')
                && string.Equals(pattern, packageId, StringComparison.OrdinalIgnoreCase)))
            {
                exact.Add(source.Name);
            }
        }

        if (exact.Count > 0)
        {
            return exact;
        }

        List<string> prefixes = [];
        int bestLength = -1;
        foreach (SourcePatterns source in _sources)
        {
            int sourceBest = -1;
            foreach (string pattern in source.Patterns)
            {
                if (!pattern.EndsWith('*'))
                {
                    continue;
                }

                string prefix = pattern[..^1];
                if (prefix.Length >= sourceBest
                    && packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    sourceBest = Math.Max(sourceBest, prefix.Length);
                }
            }

            if (sourceBest < bestLength)
            {
                continue;
            }

            if (sourceBest > bestLength)
            {
                prefixes.Clear();
                bestLength = sourceBest;
            }

            if (sourceBest >= 0)
            {
                prefixes.Add(source.Name);
            }
        }

        return prefixes;
    }

    private sealed record SourcePatterns(string Name, IReadOnlyList<string> Patterns);
}
