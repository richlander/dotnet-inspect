using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NLinq;

public static class ImmutableArrayExtensions
{
    public static ImmutableArrayEnumerator<T> AsNLinq<T>(this ImmutableArray<T> array)
    {
        return new ImmutableArrayEnumerator<T>(array);
    }
}

public struct ImmutableArrayEnumerator<T> : IEnumerator<ImmutableArrayEnumerator<T>, T>
{
    private readonly ImmutableArray<T> _array;
    private int _index;

    public ImmutableArrayEnumerator(ImmutableArray<T> array)
    {
        _array = array;
        _index = -1;
    }

    public static int Count(scoped ref ImmutableArrayEnumerator<T> @this) => @this._array.Length - @this._index - 1;

    public T TryGetNext(out bool hasMore)
    {
        _index++;
        if (_index < _array.Length)
        {
            hasMore = true;
            return _array[_index];
        }
        else
        {
            hasMore = false;
            return default!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TAcc IEnumerator<ImmutableArrayEnumerator<T>, T>.Fold<TAcc, TFunc>(scoped ref ImmutableArrayEnumerator<T> @this, TAcc accumulator, TFunc func)
    {
        var span = ImmutableCollectionsMarshal.AsArray(@this._array)!.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            accumulator = func.Invoke(accumulator, span[i]);
        }
        return accumulator;
    }
}
