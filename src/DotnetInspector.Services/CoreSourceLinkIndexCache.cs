using DotnetInspector.Core;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Tool-tier implementation of the engine's <see cref="ISourceLinkIndexCache"/> seam,
/// backed by the shared <see cref="CoreCache"/> disk cache. Wiring this into
/// <see cref="SourceLinkService.DefaultCache"/> at startup lets the engine persist its
/// SourceLink index without the engine referencing the tool tier (dependency inversion).
/// </summary>
public sealed class CoreSourceLinkIndexCache : ISourceLinkIndexCache
{
    private const string Category = "sourcelink-index";

    /// <summary>Shared instance; the cache itself is stateless over <see cref="CoreCache"/>.</summary>
    public static CoreSourceLinkIndexCache Instance { get; } = new();

    public string? TryGet(string key) => CoreCache.TryGet(Category, key);

    public void Set(string key, string content) => CoreCache.Set(Category, key, content);
}
