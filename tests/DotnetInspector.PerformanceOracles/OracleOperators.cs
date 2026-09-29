using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

/// <summary>
/// Operators the pinned NLinq lacks, written once in its style so every
/// scorecard closing has an NLinq query.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Take{TEnum,T}"/> and <see cref="Skip{TEnum,T}"/> are lazy
/// enumerators, as NLinq's <c>Where</c> and <c>Select</c> are. Take never pulls
/// past its count, and Skip discards only when first pulled, so a pipeline
/// author places Skip before <c>Select</c> to avoid projecting skipped
/// elements.
/// </para>
/// <para>
/// A strict window is <c>Skip(first - 1)</c> followed by
/// <see cref="TryTakeExactly{TEnum,T}"/>: fewer than the requested elements is
/// a failure, never a shorter success, whether the source ran out while
/// skipping or while taking.
/// <see cref="TakeLast{TEnum,T}"/> keeps the last elements in a ring, in
/// source order. Every operator pulls with <c>TryGetNext</c>, so each acts only
/// on the elements that remain; none relies on the pinned NLinq's folds, which
/// restart collection sources from the first element.
/// </para>
/// </remarks>
public static class OracleOperators
{
    /// <summary>
    /// Groups the remaining source elements by key in source order.
    /// </summary>
    public static IReadOnlyDictionary<TKey, ImmutableArray<T>>
        ToImmutableLookup<TEnum, T, TKey, TKeySelector>(
            this TEnum source,
            TKeySelector keySelector)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TKey : notnull
        where TKeySelector : IFunc<T, TKey>, allows ref struct
    {
        var groups =
            new Dictionary<TKey, ImmutableArray<T>.Builder>();
        while (true)
        {
            T item = source.TryGetNext(out bool hasMore);
            if (!hasMore)
                break;

            TKey key = keySelector.Invoke(item);
            if (!groups.TryGetValue(
                    key,
                    out ImmutableArray<T>.Builder? group))
            {
                groups.Add(
                    key,
                    group = ImmutableArray.CreateBuilder<T>());
            }
            group.Add(item);
        }

        return groups.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToImmutable());
    }

    /// <summary>
    /// Builds a lazy lookup over contiguous source runs while preserving exact
    /// grouping through an eager fallback when a key reappears.
    /// </summary>
    public static IReadOnlyDictionary<TKey, ImmutableArray<T>>
        ToImmutableRunLookup<T, TKey, TKeySelector, TKeyComparer>(
            this ImmutableArray<T> source,
            TKeySelector keySelector,
            TKeyComparer keyComparer)
        where TKey : notnull
        where TKeySelector : IFunc<T, TKey>, allows ref struct
        where TKeyComparer : IComparer<TKey>
    {
        ReadOnlySpan<T> items = source.AsSpan();
        if (items.IsEmpty)
            return new Dictionary<TKey, ImmutableArray<T>>();

        TKey[] keys = ArrayPool<TKey>.Shared.Rent(items.Length);
        int[] ends = ArrayPool<int>.Shared.Rent(items.Length);
        try
        {
            int runs = 0;
            TKey current = keySelector.Invoke(items[0]);
            for (int i = 1; i < items.Length; i++)
            {
                TKey key = keySelector.Invoke(items[i]);
                if (EqualityComparer<TKey>.Default.Equals(key, current))
                    continue;
                keys[runs] = current;
                ends[runs++] = i;
                current = key;
            }
            keys[runs] = current;
            ends[runs++] = items.Length;

            bool ascending = true;
            for (int run = 1; run < runs && ascending; run++)
                ascending = keyComparer.Compare(keys[run - 1], keys[run]) < 0;
            Dictionary<TKey, int>? ordinals = null;
            if (!ascending)
            {
                ordinals = new Dictionary<TKey, int>(runs);
                for (int run = 0; run < runs; run++)
                {
                    if (!ordinals.TryAdd(keys[run], run))
                        return Interleaved<T, TKey, TKeySelector>(
                            source,
                            keySelector);
                }
            }

            return new ImmutableRunLookup<T, TKey, TKeyComparer>(
                source,
                keys.AsSpan(0, runs),
                ordinals,
                ends.AsSpan(0, runs),
                keyComparer);
        }
        finally
        {
            ArrayPool<TKey>.Shared.Return(
                keys,
                RuntimeHelpers.IsReferenceOrContainsReferences<TKey>());
            ArrayPool<int>.Shared.Return(ends);
        }
    }

    static Dictionary<TKey, ImmutableArray<T>> Interleaved<
        T,
        TKey,
        TKeySelector>(
        ImmutableArray<T> source,
        TKeySelector keySelector)
        where TKey : notnull
        where TKeySelector : IFunc<T, TKey>, allows ref struct
    {
        var builders =
            new Dictionary<TKey, ImmutableArray<T>.Builder>();
        foreach (T item in source)
        {
            TKey key = keySelector.Invoke(item);
            if (!builders.TryGetValue(
                    key,
                    out ImmutableArray<T>.Builder? builder))
            {
                builder = ImmutableArray.CreateBuilder<T>();
                builders.Add(key, builder);
            }
            builder.Add(item);
        }

        var groups =
            new Dictionary<TKey, ImmutableArray<T>>(
                builders.Count);
        foreach (var pair in builders)
            groups.Add(pair.Key, pair.Value.ToImmutable());
        return groups;
    }

    public static TakeEnumerator<TEnum, T> Take<TEnum, T>(this TEnum source, int count)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new TakeEnumerator<TEnum, T>(source, count);
    }

    public static SkipEnumerator<TEnum, T> Skip<TEnum, T>(this TEnum source, int count)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new SkipEnumerator<TEnum, T>(source, count);
    }

    /// <summary>
    /// Takes exactly <paramref name="count"/> elements. Returns false, with no
    /// rows, when the source ends first.
    /// </summary>
    public static bool TryTakeExactly<TEnum, T>(this TEnum source, int count, out List<T> rows)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var taken = new List<T>(count);
        while (taken.Count < count)
        {
            T item = source.TryGetNext(out bool hasMore);
            if (!hasMore)
            {
                rows = [];
                return false;
            }

            taken.Add(item);
        }

        rows = taken;
        return true;
    }

    /// <summary>
    /// The last <paramref name="count"/> elements that remain in the source,
    /// in source order. It pulls with <c>TryGetNext</c> rather than folding,
    /// because the pinned NLinq's collection folds restart from the first
    /// element regardless of what was already pulled.
    /// </summary>
    public static List<T> TakeLast<TEnum, T>(this TEnum source, int count)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
            return [];
        var ring = new Ring<T>(count);
        var keep = new KeepLast<T>();
        while (true)
        {
            T item = source.TryGetNext(out bool hasMore);
            if (!hasMore)
                break;
            ring = keep.Invoke(ring, item);
        }

        int kept = (int)Math.Min(ring.Seen, count);
        var rows = new List<T>(kept);
        for (long i = ring.Seen - kept; i < ring.Seen; i++)
            rows.Add(ring.Items[(int)(i % count)]);
        return rows;
    }

    sealed class Ring<T>(int count)
    {
        public T[] Items { get; } = new T[count];

        public long Seen;
    }

    struct KeepLast<T> : IFunc<Ring<T>, T, Ring<T>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Ring<T> Invoke(Ring<T> ring, T item)
        {
            ring.Items[(int)(ring.Seen++ % ring.Items.Length)] = item;
            return ring;
        }
    }

    sealed class ImmutableRunLookup<T, TKey, TKeyComparer> :
        IReadOnlyDictionary<TKey, ImmutableArray<T>>
        where TKey : notnull
        where TKeyComparer : IComparer<TKey>
    {
        readonly ImmutableArray<T> _source;
        readonly ImmutableArray<TKey> _keys;
        readonly Dictionary<TKey, int>? _ordinals;
        readonly int[] _ends;
        readonly ImmutableArray<T>[] _groups;
        readonly TKeyComparer _keyComparer;

        internal ImmutableRunLookup(
            ImmutableArray<T> source,
            ReadOnlySpan<TKey> keys,
            Dictionary<TKey, int>? ordinals,
            ReadOnlySpan<int> ends,
            TKeyComparer keyComparer)
        {
            _source = source;
            _keys = [.. keys];
            _ordinals = ordinals;
            _ends = ends.ToArray();
            _groups = new ImmutableArray<T>[_ends.Length];
            _keyComparer = keyComparer;
        }

        public int Count => _ends.Length;

        public ImmutableArray<T> this[TKey key] =>
            TryGetValue(key, out ImmutableArray<T> value)
                ? value
                : throw new KeyNotFoundException();

        public IEnumerable<TKey> Keys => _keys;

        public IEnumerable<ImmutableArray<T>> Values
        {
            get
            {
                for (int run = 0; run < _ends.Length; run++)
                    yield return Group(run);
            }
        }

        public bool ContainsKey(TKey key) => Run(key) >= 0;

        public bool TryGetValue(
            TKey key,
            out ImmutableArray<T> value)
        {
            int run = Run(key);
            if (run < 0)
            {
                value = default;
                return false;
            }
            value = Group(run);
            return true;
        }

        public IEnumerator<KeyValuePair<TKey, ImmutableArray<T>>>
            GetEnumerator()
        {
            for (int run = 0; run < _keys.Length; run++)
                yield return new(_keys[run], Group(run));
        }

        int Run(TKey key)
        {
            if (_ordinals is not null)
            {
                return _ordinals.TryGetValue(key, out int ordinal)
                    ? ordinal
                    : -1;
            }

            int low = 0;
            int high = _keys.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison =
                    _keyComparer.Compare(_keys[middle], key);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return -1;
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        ImmutableArray<T> Group(int run)
        {
            ImmutableArray<T> group = _groups[run];
            if (!group.IsDefault)
                return group;
            int start = run == 0 ? 0 : _ends[run - 1];
            int end = _ends[run];
            group = start == 0 && end == _source.Length
                ? _source
                : ImmutableArray.Create(_source, start, end - start);
            _groups[run] = group;
            return group;
        }
    }
}

/// <summary>Yields at most a count of elements and never pulls past it.</summary>
public ref struct TakeEnumerator<TEnum, T> : IEnumerator<TakeEnumerator<TEnum, T>, T>
    where TEnum : IEnumerator<TEnum, T>, allows ref struct
{
    TEnum _source;
    int _remaining;

    public TakeEnumerator(TEnum source, int count)
    {
        _source = source;
        _remaining = count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T TryGetNext(out bool hasMore)
    {
        if (_remaining <= 0)
        {
            hasMore = false;
            return default!;
        }

        T item = _source.TryGetNext(out hasMore);
        if (hasMore)
            _remaining--;
        else
            _remaining = 0;
        return item;
    }
}

/// <summary>Discards a count of elements on the first pull, then yields the rest.</summary>
public ref struct SkipEnumerator<TEnum, T> : IEnumerator<SkipEnumerator<TEnum, T>, T>
    where TEnum : IEnumerator<TEnum, T>, allows ref struct
{
    TEnum _source;
    int _toSkip;

    public SkipEnumerator(TEnum source, int count)
    {
        _source = source;
        _toSkip = count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T TryGetNext(out bool hasMore)
    {
        while (_toSkip > 0)
        {
            _source.TryGetNext(out hasMore);
            if (!hasMore)
            {
                _toSkip = 0;
                return default!;
            }

            _toSkip--;
        }

        return _source.TryGetNext(out hasMore);
    }
}
