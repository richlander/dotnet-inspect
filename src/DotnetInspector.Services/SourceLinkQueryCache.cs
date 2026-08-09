using DotnetInspector.Core;

namespace DotnetInspector.Services;

/// <summary>
/// Optional cache used by SourceLink availability and integrity operations.
/// A host without durable storage may pass <see langword="null"/>.
/// </summary>
public interface ISourceLinkQueryCache
{
    string? TryGet(
        string category,
        string key,
        TimeSpan? maxAge,
        string extension);

    void Set(
        string category,
        string key,
        string content,
        string extension);
}

/// <summary>Adapts the process-wide product cache to SourceLink queries.</summary>
public sealed class CoreSourceLinkQueryCache : ISourceLinkQueryCache
{
    public static CoreSourceLinkQueryCache Instance { get; } = new();

    private CoreSourceLinkQueryCache()
    {
    }

    public string? TryGet(
        string category,
        string key,
        TimeSpan? maxAge,
        string extension)
        => maxAge is { } age
            ? CoreCache.TryGet(category, key, age, extension)
            : CoreCache.TryGet(category, key, extension);

    public void Set(
        string category,
        string key,
        string content,
        string extension)
        => CoreCache.Set(category, key, content, extension);
}
