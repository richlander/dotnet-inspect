
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NLinq;

public interface IEnumerator<TSelf, T>
    where TSelf : IEnumerator<TSelf, T>, allows ref struct
    where T : allows ref struct
{
    /// <summary>
    /// Returns next element in the sequence if available and sets done to false.
    /// If no more elements are available, sets done to true and returns default.
    /// </summary>
    T TryGetNext(out bool done);

    static virtual int Count(scoped ref TSelf @this)
    {
        int count = 0;
        bool hasMore;
        do
        {
            @this.TryGetNext(out hasMore);
            if (hasMore)
            {
                count++;
            }
        } while (hasMore);
        return count;
    }

    static virtual TAcc Fold<TAcc, TFunc>(scoped ref TSelf @this, TAcc accumulator, TFunc func)
        where TAcc : allows ref struct
        where TFunc : IFunc<TAcc, T, TAcc>, allows ref struct
    {
        bool hasMore;
        do
        {
            var item = @this.TryGetNext(out hasMore);
            if (hasMore)
            {
                accumulator = func.Invoke(accumulator, item);
            }
        } while (hasMore);
        return accumulator;
    }

    static virtual TAcc Fold<TAcc, TFunc>(scoped ref TSelf @this, TAcc accumulator, Func<TAcc, T, TAcc> func)
        where TAcc : allows ref struct
    {
        bool hasMore;
        do
        {
            var item = @this.TryGetNext(out hasMore);
            if (hasMore)
            {
                accumulator = func.Invoke(accumulator, item);
            }
        } while (hasMore);
        return accumulator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual Map<T, TOut, TSelf, TFunc> Select<TOut, TFunc>(scoped ref TSelf @this, TFunc func)
        where TOut : allows ref struct
        where TFunc : IFunc<T, TOut>, allows ref struct
    {
        return new Map<T, TOut, TSelf, TFunc>(@this, func);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual Map<T, TOut, TSelf> Select<TOut>(scoped ref TSelf @this, Func<T, TOut> func)
        where TOut : allows ref struct
    {
        return new Map<T, TOut, TSelf>(@this, func);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual Filter<T, TSelf, TFunc> Where<TFunc>(scoped ref TSelf @this, TFunc predicate)
        where TFunc : IFunc<T, bool>, allows ref struct
    {
        return new Filter<T, TSelf, TFunc>(@this, predicate);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual Filter<T, TSelf> Where(scoped ref TSelf @this, Func<T, bool> predicate)
    {
        return new Filter<T, TSelf>(@this, predicate);
    }
}

public ref struct Map<TIn, TOut, TEnum, TFunc>
    : IEnumerator<Map<TIn, TOut, TEnum, TFunc>, TOut>
    where TIn : allows ref struct
    where TOut : allows ref struct
    where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
    where TFunc : IFunc<TIn, TOut>, allows ref struct
{
    internal TEnum _source;
    internal TFunc _func;

    public Map(TEnum source, TFunc func)
    {
        _source = source;
        _func = func;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TOut TryGetNext(out bool hasMore)
    {
        var input = _source.TryGetNext(out hasMore);
        if (hasMore)
        {
            return _func.Invoke(input);
        }
        else
        {
            return default!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TAcc IEnumerator<Map<TIn, TOut, TEnum, TFunc>, TOut>.Fold<TAcc, TFoldFunc>(scoped ref Map<TIn, TOut, TEnum, TFunc> @this, TAcc accumulator, TFoldFunc func)
    {
        return TEnum.Fold(
            ref @this._source,
            accumulator,
            new MapFoldFunc<TAcc, TFoldFunc>(@this._func, func));
    }

    private ref struct MapFoldFunc<TAcc, TFoldFunc>
        : IFunc<TAcc, TIn, TAcc>
        where TAcc : allows ref struct
        where TFoldFunc : IFunc<TAcc, TOut, TAcc>, allows ref struct
    {
        private TFunc mapFunc;
        private TFoldFunc foldFunc;

        public MapFoldFunc(TFunc mapFunc, TFoldFunc foldFunc)
        {
            this.mapFunc = mapFunc;
            this.foldFunc = foldFunc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TAcc Invoke(TAcc acc, TIn t)
        {
            return foldFunc.Invoke(acc, mapFunc.Invoke(t));
        }
    }
}

public ref struct Map<TIn, TOut, TEnum>
    : IEnumerator<Map<TIn, TOut, TEnum>, TOut>
    where TIn : allows ref struct
    where TOut : allows ref struct
    where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
{
    internal TEnum _source;
    internal Func<TIn, TOut> _func;

    public Map(TEnum source, Func<TIn, TOut> func)
    {
        _source = source;
        _func = func;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TOut TryGetNext(out bool hasMore)
    {
        var input = _source.TryGetNext(out hasMore);
        if (hasMore)
        {
            return _func.Invoke(input);
        }
        else
        {
            return default!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TAcc IEnumerator<Map<TIn, TOut, TEnum>, TOut>.Fold<TAcc, TFoldFunc>(scoped ref Map<TIn, TOut, TEnum> @this, TAcc accumulator, TFoldFunc func)
    {
        return TEnum.Fold(
            ref @this._source,
            accumulator,
            new MapFoldFunc<TAcc, TFoldFunc>(@this._func, func));
    }

    private ref struct MapFoldFunc<TAcc, TFoldFunc>
        : IFunc<TAcc, TIn, TAcc>
        where TAcc : allows ref struct
        where TFoldFunc : IFunc<TAcc, TOut, TAcc>, allows ref struct
    {
        private Func<TIn, TOut> mapFunc;
        private TFoldFunc foldFunc;

        public MapFoldFunc(Func<TIn, TOut> mapFunc, TFoldFunc foldFunc)
        {
            this.mapFunc = mapFunc;
            this.foldFunc = foldFunc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TAcc Invoke(TAcc acc, TIn t)
        {
            return foldFunc.Invoke(acc, mapFunc.Invoke(t));
        }
    }
}

public ref struct Filter<T, TEnum, TFunc>(TEnum source, TFunc predicate)
    : IEnumerator<Filter<T, TEnum, TFunc>, T>
    where T : allows ref struct
    where TEnum : IEnumerator<TEnum, T>, allows ref struct
    where TFunc : IFunc<T, bool>, allows ref struct
{
    internal TEnum _source = source;
    internal TFunc _predicate = predicate;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T TryGetNext(out bool hasMore)
    {
        while (true)
        {
            var item = _source.TryGetNext(out hasMore);
            if (!hasMore)
            {
                return default!;
            }

            if (_predicate.Invoke(item))
            {
                return item;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TAcc IEnumerator<Filter<T, TEnum, TFunc>, T>.Fold<TAcc, TFoldFunc>(scoped ref Filter<T, TEnum, TFunc> @this, TAcc accumulator, TFoldFunc func)
    {
        return TEnum.Fold(
            ref @this._source,
            accumulator,
            new FilterFoldFunc<TAcc, TFoldFunc>(@this._predicate, func));
    }

    private ref struct FilterFoldFunc<TAcc, TFoldFunc>
        : IFunc<TAcc, T, TAcc>
        where TAcc : allows ref struct
        where TFoldFunc : IFunc<TAcc, T, TAcc>, allows ref struct
    {
        private TFunc predicate;
        private TFoldFunc foldFunc;

        public FilterFoldFunc(TFunc predicate, TFoldFunc foldFunc)
        {
            this.predicate = predicate;
            this.foldFunc = foldFunc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TAcc Invoke(TAcc acc, T t)
        {
            return predicate.Invoke(t) ? foldFunc.Invoke(acc, t) : acc;
        }
    }
}

public ref struct Filter<T, TEnum>(TEnum source, Func<T, bool> predicate)
    : IEnumerator<Filter<T, TEnum>, T>
    where T : allows ref struct
    where TEnum : IEnumerator<TEnum, T>, allows ref struct
{
    internal TEnum _source = source;
    internal Func<T, bool> _predicate = predicate;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T TryGetNext(out bool hasMore)
    {
        while (true)
        {
            var item = _source.TryGetNext(out hasMore);
            if (!hasMore)
            {
                return default!;
            }

            if (_predicate.Invoke(item))
            {
                return item;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TAcc IEnumerator<Filter<T, TEnum>, T>.Fold<TAcc, TFoldFunc>(scoped ref Filter<T, TEnum> @this, TAcc accumulator, TFoldFunc func)
    {
        return TEnum.Fold(
            ref @this._source,
            accumulator,
            new FilterFoldFunc<TAcc, TFoldFunc>(@this._predicate, func));
    }

    private ref struct FilterFoldFunc<TAcc, TFoldFunc>
        : IFunc<TAcc, T, TAcc>
        where TAcc : allows ref struct
        where TFoldFunc : IFunc<TAcc, T, TAcc>, allows ref struct
    {
        private Func<T, bool> predicate;
        private TFoldFunc foldFunc;

        public FilterFoldFunc(Func<T, bool> predicate, TFoldFunc foldFunc)
        {
            this.predicate = predicate;
            this.foldFunc = foldFunc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TAcc Invoke(TAcc acc, T t)
        {
            return predicate.Invoke(t) ? foldFunc.Invoke(acc, t) : acc;
        }
    }
}

public ref struct Concat<T, TEnum1, TEnum2>
    : IEnumerator<Concat<T, TEnum1, TEnum2>, T>
    where T : allows ref struct
    where TEnum1 : IEnumerator<TEnum1, T>, allows ref struct
    where TEnum2 : IEnumerator<TEnum2, T>, allows ref struct
{
    internal TEnum1 _first;
    internal TEnum2 _second;
    private bool _firstDone;

    public Concat(TEnum1 first, TEnum2 second)
    {
        _first = first;
        _second = second;
        _firstDone = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T TryGetNext(out bool hasMore)
    {
        if (!_firstDone)
        {
            var item = _first.TryGetNext(out hasMore);
            if (hasMore)
                return item;
            _firstDone = true;
        }
        return _second.TryGetNext(out hasMore);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static TAcc IEnumerator<Concat<T, TEnum1, TEnum2>, T>.Fold<TAcc, TFoldFunc>(
        scoped ref Concat<T, TEnum1, TEnum2> @this, TAcc accumulator, TFoldFunc func)
    {
        accumulator = TEnum1.Fold(ref @this._first, accumulator, func);
        return TEnum2.Fold(ref @this._second, accumulator, func);
    }
}
