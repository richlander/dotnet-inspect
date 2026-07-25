using System.Collections.Concurrent;

namespace DotnetInspector.Packages;

/// <summary>
/// In-memory <see cref="IPdbStore"/> for hosts without a persistent filesystem
/// (browser/WASM) and for tests. Keeps PDB payloads in a dictionary keyed by the
/// store-relative key; <see cref="TryGetLocalPath"/> always returns <c>null</c>
/// because entries never touch disk.
/// </summary>
public sealed class InMemoryPdbStore : IPdbStore
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<Stream?> TryOpenAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _entries.TryGetValue(key, out var bytes)
            ? ValueTask.FromResult<Stream?>(new MemoryStream(bytes, writable: false))
            : ValueTask.FromResult<Stream?>(null);
    }

    /// <inheritdoc />
    public async ValueTask PutAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        _entries[key] = buffer.ToArray();
    }

    /// <inheritdoc />
    public string? TryGetLocalPath(string key) => null;
}
