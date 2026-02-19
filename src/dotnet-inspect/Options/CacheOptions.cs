namespace DotnetInspector.Options;

/// <summary>
/// Options for the cache management command.
/// </summary>
public record CacheOptions(
    bool Clean,
    bool Verbose,
    string? Session = null);
