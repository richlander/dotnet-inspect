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
    private readonly object _retainedBytesLock = new();
    private long _retainedBytes;

    public InMemoryPdbStore()
        : this(Array.MaxLength)
    {
    }

    public InMemoryPdbStore(long maxRetainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxRetainedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxRetainedBytes,
            Array.MaxLength);
        MaxRetainedBytes = maxRetainedBytes;
    }

    public long MaxRetainedBytes { get; }
    public long RetainedBytes
    {
        get
        {
            lock (_retainedBytesLock)
                return _retainedBytes;
        }
    }

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

        long? declaredLength =
            content.CanSeek
                ? content.Length - content.Position
                : null;
        byte[] bytes = await BoundedContentReader.ReadAllBytesAsync(
            content,
            MaxRetainedBytes,
            declaredLength,
            cancellationToken).ConfigureAwait(false);

        lock (_retainedBytesLock)
        {
            long previousLength =
                _entries.TryGetValue(key, out byte[]? previous)
                    ? previous.LongLength
                    : 0;
            long nextRetained =
                _retainedBytes - previousLength + bytes.LongLength;
            if (nextRetained > MaxRetainedBytes)
            {
                throw new InvalidDataException(
                    "The in-memory PDB store exceeds its retained-byte limit.");
            }

            _entries[key] = bytes;
            _retainedBytes = nextRetained;
        }
    }

    /// <inheritdoc />
    public string? TryGetLocalPath(string key) => null;
}
