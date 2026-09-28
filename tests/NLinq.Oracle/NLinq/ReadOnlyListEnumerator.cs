using System.Collections.Generic;

namespace NLinq;

public static class ReadOnlyListExtensions
{
    public static ReadOnlyListEnumerator<T> AsNLinq<T>(this IReadOnlyList<T> list)
    {
        return new ReadOnlyListEnumerator<T>(list);
    }
}

public struct ReadOnlyListEnumerator<T> : IEnumerator<ReadOnlyListEnumerator<T>, T>
{
    private readonly IReadOnlyList<T> _list;
    private int _index;

    public ReadOnlyListEnumerator(IReadOnlyList<T> list)
    {
        _list = list;
        _index = -1;
    }

    public static int Count(scoped ref ReadOnlyListEnumerator<T> @this) => @this._list.Count - @this._index - 1;

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
}
