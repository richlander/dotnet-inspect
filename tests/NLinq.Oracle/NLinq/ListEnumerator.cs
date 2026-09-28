using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NLinq;

public static class ListExtensions
{
    public static ListEnumerator<T> AsNLinq<T>(this List<T> list)
    {
        return new ListEnumerator<T>(list);
    }
}

public struct ListEnumerator<T> : IEnumerator<ListEnumerator<T>, T>
{
    private readonly List<T> _list;
    private int _index;

    public ListEnumerator(List<T> list)
    {
        _list = list;
        _index = -1;
    }

    public static int Count(scoped ref ListEnumerator<T> @this) => @this._list.Count - @this._index - 1;

    public T TryGetNext(out bool hasMore)
    {
        _index++;
        if (_index < _list.Count)
        {
            hasMore = true;
            return _list[_index];
        }
        else
        {
            hasMore = false;
            return default!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TAcc IEnumerator<ListEnumerator<T>, T>.Fold<TAcc, TFunc>(scoped ref ListEnumerator<T> @this, TAcc accumulator, TFunc func)
    {
        var span = CollectionsMarshal.AsSpan(@this._list);
        for (int i = 0; i < span.Length; i++)
        {
            accumulator = func.Invoke(accumulator, span[i]);
        }
        return accumulator;
    }
}
