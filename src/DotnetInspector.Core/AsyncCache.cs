using System.Collections.Concurrent;

namespace DotnetInspector.Core;

/// <summary>
/// Shares one asynchronous resolution per key and retains successful results.
/// Faulted and cancelled resolutions are removed so a later request can retry.
/// Cancellation captured by a value factory affects every waiter sharing that
/// resolution; caller-only cancellation should cancel the wait, not the factory.
/// </summary>
public sealed class AsyncCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _entries;

    public AsyncCache(IEqualityComparer<TKey>? comparer = null)
    {
        _entries = new ConcurrentDictionary<TKey, Lazy<Task<TValue>>>(
            comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>
    /// Returns the existing resolution task for <paramref name="key"/>, or
    /// publishes one new resolution task. Concurrent callers receive the exact
    /// same task.
    /// </summary>
    /// <param name="shouldCache">
    /// Optional predicate that decides whether a successfully resolved value
    /// remains cached. A rejected value is still returned to current waiters,
    /// but a later request can retry. The factory and predicate from the entry
    /// that wins publication define the shared resolution.
    /// </param>
    public Task<TValue> GetOrAddAsync(
        TKey key,
        Func<TKey, Task<TValue>> valueFactory,
        Func<TValue, bool>? shouldCache = null)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        Lazy<Task<TValue>>? candidate = null;
        candidate = new Lazy<Task<TValue>>(
            () => ResolveAsync(
                key,
                valueFactory,
                shouldCache,
                candidate!),
            LazyThreadSafetyMode.ExecutionAndPublication);

        return _entries.GetOrAdd(key, candidate).Value;
    }

    /// <summary>
    /// Removes <paramref name="key"/> only when it still identifies
    /// <paramref name="task"/>.
    /// </summary>
    public bool TryRemove(TKey key, Task<TValue> task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!_entries.TryGetValue(key, out var entry)
            || !entry.IsValueCreated
            || !ReferenceEquals(entry.Value, task))
        {
            return false;
        }

        return Remove(key, entry);
    }

    public void Clear() => _entries.Clear();

    private async Task<TValue> ResolveAsync(
        TKey key,
        Func<TKey, Task<TValue>> valueFactory,
        Func<TValue, bool>? shouldCache,
        Lazy<Task<TValue>> entry)
    {
        try
        {
            TValue value = await valueFactory(key).ConfigureAwait(false);
            if (shouldCache is not null && !shouldCache(value))
                Remove(key, entry);
            return value;
        }
        catch
        {
            Remove(key, entry);
            throw;
        }
    }

    private bool Remove(TKey key, Lazy<Task<TValue>> entry)
        => ((ICollection<KeyValuePair<TKey, Lazy<Task<TValue>>>>)_entries)
            .Remove(new KeyValuePair<TKey, Lazy<Task<TValue>>>(key, entry));
}
