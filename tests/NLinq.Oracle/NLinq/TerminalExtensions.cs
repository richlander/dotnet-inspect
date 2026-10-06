using System.Runtime.CompilerServices;

namespace NLinq;

/// <summary>
/// Instance terminal methods added to each enumerator struct so C# can infer type parameters.
/// These delegate to the static interface methods.
/// </summary>
public static class TerminalMethodHelpers
{
    private struct AddToListFunc<T> : IFunc<List<T>, T, List<T>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<T> Invoke(List<T> list, T item)
        {
            list.Add(item);
            return list;
        }
    }

    public static List<T> ToListImpl<TSelf, T>(ref TSelf self)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        var list = new List<T>();
        TSelf.Fold(ref self, list, new AddToListFunc<T>());
        return list;
    }

    internal static int CountImpl<TSelf, T>(ref TSelf self)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        return TSelf.Count(ref self);
    }

    internal static T FirstImpl<TSelf, T>(ref TSelf self)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        var item = self.TryGetNext(out bool hasMore);
        if (!hasMore)
            throw new InvalidOperationException("Sequence contains no elements");
        return item;
    }

    internal static T? FirstOrDefaultImpl<TSelf, T>(ref TSelf self)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        var item = self.TryGetNext(out bool hasMore);
        return hasMore ? item : default;
    }

    internal static bool AnyImpl<TSelf, T>(ref TSelf self)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        self.TryGetNext(out bool hasMore);
        return hasMore;
    }

    internal static bool AnyImpl<TSelf, T>(ref TSelf self, Func<T, bool> predicate)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        bool hasMore;
        do
        {
            var item = self.TryGetNext(out hasMore);
            if (hasMore && predicate(item))
                return true;
        } while (hasMore);
        return false;
    }

    internal static bool AllImpl<TSelf, T>(ref TSelf self, Func<T, bool> predicate)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        bool hasMore;
        do
        {
            var item = self.TryGetNext(out hasMore);
            if (hasMore && !predicate(item))
                return false;
        } while (hasMore);
        return true;
    }

    internal static T SingleImpl<TSelf, T>(ref TSelf self)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        var item = self.TryGetNext(out bool hasMore);
        if (!hasMore)
            throw new InvalidOperationException("Sequence contains no elements");
        self.TryGetNext(out bool hasMore2);
        if (hasMore2)
            throw new InvalidOperationException("Sequence contains more than one element");
        return item;
    }

    internal static T SingleImpl<TSelf, T>(ref TSelf self, Func<T, bool> predicate)
        where TSelf : IEnumerator<TSelf, T>, allows ref struct
    {
        T result = default!;
        bool found = false;
        bool hasMore;
        do
        {
            var item = self.TryGetNext(out hasMore);
            if (hasMore && predicate(item))
            {
                if (found)
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                result = item;
                found = true;
            }
        } while (hasMore);
        if (!found)
            throw new InvalidOperationException("Sequence contains no matching element");
        return result;
    }
}

// Extension methods that provide fluent terminal operations.
// These use a trick: the `ref this` parameter lets C# infer all type parameters.
public static class TerminalExtensions
{
    public static List<T> ToList<T, TEnum>(this Filter<T, TEnum> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.ToListImpl<Filter<T, TEnum>, T>(ref e);
    }

    public static List<T> ToList<T, TEnum, TFunc>(this Filter<T, TEnum, TFunc> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TFunc : IFunc<T, bool>, allows ref struct
    {
        return TerminalMethodHelpers.ToListImpl<Filter<T, TEnum, TFunc>, T>(ref e);
    }

    public static List<TOut> ToList<TIn, TOut, TEnum>(this Map<TIn, TOut, TEnum> e)
        where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
    {
        return TerminalMethodHelpers.ToListImpl<Map<TIn, TOut, TEnum>, TOut>(ref e);
    }

    public static List<TOut> ToList<TIn, TOut, TEnum, TFunc>(this Map<TIn, TOut, TEnum, TFunc> e)
        where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
        where TFunc : IFunc<TIn, TOut>, allows ref struct
    {
        return TerminalMethodHelpers.ToListImpl<Map<TIn, TOut, TEnum, TFunc>, TOut>(ref e);
    }

    public static List<T> ToList<T>(this ImmutableArrayEnumerator<T> e)
    {
        return TerminalMethodHelpers.ToListImpl<ImmutableArrayEnumerator<T>, T>(ref e);
    }

    public static List<T> ToList<T>(this ArrayEnumerator<T> e)
    {
        return TerminalMethodHelpers.ToListImpl<ArrayEnumerator<T>, T>(ref e);
    }

    public static List<T> ToList<T>(this ListEnumerator<T> e)
    {
        return TerminalMethodHelpers.ToListImpl<ListEnumerator<T>, T>(ref e);
    }

    public static List<T> ToList<T>(this ReadOnlyListEnumerator<T> e)
    {
        return TerminalMethodHelpers.ToListImpl<ReadOnlyListEnumerator<T>, T>(ref e);
    }

    // Count
    public static int Count<T, TEnum>(this Filter<T, TEnum> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.CountImpl<Filter<T, TEnum>, T>(ref e);
    }

    public static int Count<T, TEnum, TFunc>(this Filter<T, TEnum, TFunc> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TFunc : IFunc<T, bool>, allows ref struct
    {
        return TerminalMethodHelpers.CountImpl<Filter<T, TEnum, TFunc>, T>(ref e);
    }

    // First
    public static T First<T, TEnum>(this Filter<T, TEnum> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.FirstImpl<Filter<T, TEnum>, T>(ref e);
    }

    public static TOut First<TIn, TOut, TEnum>(this Map<TIn, TOut, TEnum> e)
        where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
    {
        return TerminalMethodHelpers.FirstImpl<Map<TIn, TOut, TEnum>, TOut>(ref e);
    }

    // FirstOrDefault
    public static T? FirstOrDefault<T, TEnum>(this Filter<T, TEnum> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.FirstOrDefaultImpl<Filter<T, TEnum>, T>(ref e);
    }

    // Any (parameterless)
    public static bool Any<T, TEnum>(this Filter<T, TEnum> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.AnyImpl<Filter<T, TEnum>, T>(ref e);
    }

    public static bool Any<T, TEnum, TFunc>(this Filter<T, TEnum, TFunc> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TFunc : IFunc<T, bool>, allows ref struct
    {
        return TerminalMethodHelpers.AnyImpl<Filter<T, TEnum, TFunc>, T>(ref e);
    }

    // Any (with predicate)
    public static bool Any<T>(this ImmutableArrayEnumerator<T> e, Func<T, bool> predicate)
    {
        return TerminalMethodHelpers.AnyImpl<ImmutableArrayEnumerator<T>, T>(ref e, predicate);
    }

    public static bool Any<T, TEnum>(this Filter<T, TEnum> e, Func<T, bool> predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.AnyImpl<Filter<T, TEnum>, T>(ref e, predicate);
    }

    // Select from Filter (chains)
    public static Map<T, TOut, Filter<T, TEnum>> Select<T, TOut, TEnum>(this Filter<T, TEnum> e, Func<T, TOut> func)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return new Map<T, TOut, Filter<T, TEnum>>(e, func);
    }

    public static Map<T, TOut, ImmutableArrayEnumerator<T>> Select<T, TOut>(this ImmutableArrayEnumerator<T> e, Func<T, TOut> func)
    {
        return new Map<T, TOut, ImmutableArrayEnumerator<T>>(e, func);
    }

    // Where from concrete enumerator types
    public static Filter<T, ImmutableArrayEnumerator<T>> Where<T>(this ImmutableArrayEnumerator<T> e, Func<T, bool> predicate)
    {
        return new Filter<T, ImmutableArrayEnumerator<T>>(e, predicate);
    }

    public static Filter<T, ListEnumerator<T>> Where<T>(this ListEnumerator<T> e, Func<T, bool> predicate)
    {
        return new Filter<T, ListEnumerator<T>>(e, predicate);
    }

    public static Filter<T, ArrayEnumerator<T>> Where<T>(this ArrayEnumerator<T> e, Func<T, bool> predicate)
    {
        return new Filter<T, ArrayEnumerator<T>>(e, predicate);
    }

    public static Map<T, TOut, ListEnumerator<T>> Select<T, TOut>(this ListEnumerator<T> e, Func<T, TOut> func)
    {
        return new Map<T, TOut, ListEnumerator<T>>(e, func);
    }

    // SelectMany (eager, materialized) — mirrors the query-syntax collection+result selector form.
    public static ListEnumerator<TResult> SelectMany<T, TCollection, TResult>(
        this ImmutableArrayEnumerator<T> source,
        Func<T, System.Collections.Immutable.ImmutableArray<TCollection>> collectionSelector,
        Func<T, TCollection, TResult> resultSelector)
    {
        var results = new List<TResult>();
        while (true)
        {
            var item = source.TryGetNext(out var hasMore);
            if (!hasMore)
            {
                break;
            }
            foreach (var sub in collectionSelector(item))
            {
                results.Add(resultSelector(item, sub));
            }
        }
        return results.AsNLinq();
    }

    public static List<T> OrderByDescending<T, TKey>(this ListEnumerator<T> e, Func<T, TKey> keySelector)
        where TKey : IComparable<TKey>
    {
        return NLinqExtensions.OrderByDescending<ListEnumerator<T>, T, TKey>(e, keySelector);
    }

    // Single (parameterless — typically after Where)
    public static T Single<T, TEnum>(this Filter<T, TEnum> e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.SingleImpl<Filter<T, TEnum>, T>(ref e);
    }

    public static TOut Single<TIn, TOut, TEnum>(this Map<TIn, TOut, TEnum> e)
        where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
    {
        return TerminalMethodHelpers.SingleImpl<Map<TIn, TOut, TEnum>, TOut>(ref e);
    }

    // Single (with predicate)
    public static T Single<T>(this ImmutableArrayEnumerator<T> e, Func<T, bool> predicate)
    {
        return TerminalMethodHelpers.SingleImpl<ImmutableArrayEnumerator<T>, T>(ref e, predicate);
    }

    public static T Single<T>(this ListEnumerator<T> e, Func<T, bool> predicate)
    {
        return TerminalMethodHelpers.SingleImpl<ListEnumerator<T>, T>(ref e, predicate);
    }

    public static T Single<T, TEnum>(this Filter<T, TEnum> e, Func<T, bool> predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return TerminalMethodHelpers.SingleImpl<Filter<T, TEnum>, T>(ref e, predicate);
    }

    // Concat from concrete enumerator types
    public static Concat<T, ImmutableArrayEnumerator<T>, ImmutableArrayEnumerator<T>> Concat<T>(
        this ImmutableArrayEnumerator<T> first, ImmutableArrayEnumerator<T> second)
    {
        return new Concat<T, ImmutableArrayEnumerator<T>, ImmutableArrayEnumerator<T>>(first, second);
    }

    // Distinct from concrete pipeline types (returns List<T>)
    public static List<T> Distinct<T>(this Concat<T, ImmutableArrayEnumerator<T>, ImmutableArrayEnumerator<T>> e)
    {
        return NLinqExtensions.Distinct<Concat<T, ImmutableArrayEnumerator<T>, ImmutableArrayEnumerator<T>>, T>(e);
    }

    public static List<TOut> Distinct<TIn, TOut, TEnum>(this Map<TIn, TOut, TEnum> e)
        where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
    {
        return NLinqExtensions.Distinct<Map<TIn, TOut, TEnum>, TOut>(e);
    }

    // Max from concrete enumerator types
    public static T Max<T>(this ImmutableArrayEnumerator<T> e, IComparer<T> comparer)
    {
        return NLinqExtensions.Max<ImmutableArrayEnumerator<T>, T>(e, comparer);
    }

    public static TOut Max<TIn, TOut, TEnum>(this Map<TIn, TOut, TEnum> e, IComparer<TOut> comparer)
        where TEnum : IEnumerator<TEnum, TIn>, allows ref struct
    {
        return NLinqExtensions.Max<Map<TIn, TOut, TEnum>, TOut>(e, comparer);
    }

    public static T? MaxOrDefault<T>(this ImmutableArrayEnumerator<T> e, IComparer<T> comparer)
    {
        return NLinqExtensions.MaxOrDefault<ImmutableArrayEnumerator<T>, T>(e, comparer);
    }

    // Last from concrete enumerator types
    public static T Last<T>(this ImmutableArrayEnumerator<T> e)
    {
        return NLinqExtensions.Last<ImmutableArrayEnumerator<T>, T>(e);
    }

    public static T Last<T>(this ReadOnlyListEnumerator<T> e)
    {
        return NLinqExtensions.Last<ReadOnlyListEnumerator<T>, T>(e);
    }

    // Fold from concrete enumerator types
    public static TAcc Fold<T, TAcc>(this ImmutableArrayEnumerator<T> e, TAcc accumulator, Func<TAcc, T, TAcc> func)
    {
        return NLinqExtensions.Fold<ImmutableArrayEnumerator<T>, T, TAcc>(e, accumulator, func);
    }

    // OrderByDescending from Filter pipeline
    public static List<T> OrderByDescending<T, TEnum, TKey>(this Filter<T, TEnum> e, Func<T, TKey> keySelector)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TKey : IComparable<TKey>
    {
        return NLinqExtensions.OrderByDescending<Filter<T, TEnum>, T, TKey>(e, keySelector);
    }

    public static List<T> OrderBy<T, TEnum>(this Filter<T, TEnum> e, IComparer<T> comparer)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        return NLinqExtensions.OrderBy<Filter<T, TEnum>, T>(e, comparer);
    }

    // SequenceEqual from concrete enumerator types
    public static bool SequenceEqual<T>(this ImmutableArrayEnumerator<T> first, ImmutableArrayEnumerator<T> second)
    {
        return NLinqExtensions.SequenceEqual<ImmutableArrayEnumerator<T>, ImmutableArrayEnumerator<T>, T>(first, second);
    }
}
