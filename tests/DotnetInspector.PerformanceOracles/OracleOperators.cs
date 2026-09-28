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
