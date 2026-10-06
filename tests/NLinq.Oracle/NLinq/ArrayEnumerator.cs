using System.Runtime.CompilerServices;

namespace NLinq;

public static class ArrayExtensions
{
    public static ArrayEnumerator<T> AsNLinq<T>(this T[] array)
    {
        return new ArrayEnumerator<T>(array);
    }
}

public struct ArrayEnumerator<T> : IEnumerator<ArrayEnumerator<T>, T>
{
    private readonly T[] _array;
    private int _index;

    public ArrayEnumerator(T[] array)
    {
        _array = array;
        _index = -1;
    }

    public static int Count(scoped ref ArrayEnumerator<T> @this) => @this._array.Length - @this._index - 1;

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
    static TAcc IEnumerator<ArrayEnumerator<T>, T>.Fold<TAcc, TFunc>(scoped ref ArrayEnumerator<T> @this, TAcc accumulator, TFunc func)
    {
        var array = @this._array;
        for (int i = 0; i < array.Length; i++)
        {
            accumulator = func.Invoke(accumulator, array[i]);
        }
        return accumulator;
    }
}
