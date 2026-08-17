using System.Collections.Concurrent;

namespace DotnetInspector.Services;

/// <summary>
/// Host-owned cache for checksum-verified SourceLink document bytes.
/// Backend failures may use any exception appropriate to the implementation;
/// callers preserve cancellation and fatal runtime exceptions while converting
/// other failures to typed source-acquisition evidence.
/// </summary>
public interface ISourceContentStore
{
    ValueTask<byte[]?> TryOpenAsync(
        string key,
        CancellationToken cancellationToken = default);

    ValueTask StoreAsync(
        string key,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-local source-content store for hosts without a filesystem.
/// </summary>
public sealed class InMemorySourceContentStore
    : ISourceContentStore
{
    readonly ConcurrentDictionary<string, byte[]> _entries =
        new(StringComparer.Ordinal);

    public ValueTask<byte[]?> TryOpenAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            _entries.TryGetValue(key, out byte[]? content)
                ? content.ToArray()
                : null);
    }

    public ValueTask StoreAsync(
        string key,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        _entries[key] = content.ToArray();
        return ValueTask.CompletedTask;
    }
}
