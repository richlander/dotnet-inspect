using System.Runtime.CompilerServices;

namespace NLinq;

public static class NLinqExtensions
{
    public static int Count<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        return TEnum.Count(ref e);
    }

    public static int CountFold<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        return TEnum.Fold(ref e, 0, new CountFunc<T>());
    }

    private struct CountFunc<T> : IFunc<int, T, int>
        where T : allows ref struct
    {
        public int Invoke(int accumulator, T item)
        {
            return accumulator + 1;
        }
    }

    public static TAcc Fold<TEnum, T, TAcc, TFunc>(this TEnum e, TAcc accumulator, TFunc func)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
        where TAcc : allows ref struct
        where TFunc : IFunc<TAcc, T, TAcc>, allows ref struct
    {
        return TEnum.Fold(ref e, accumulator, func);
    }

    public static TAcc Fold<TEnum, T, TAcc>(this TEnum e, TAcc accumulator, Func<TAcc, T, TAcc> func)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
        where TAcc : allows ref struct
    {
        return TEnum.Fold<TAcc, Func<TAcc, T, TAcc>>(ref e, accumulator, func);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<T, TOut, TEnum, TFunc> Select<TEnum, T, TOut, TFunc>(this TEnum e, TFunc func)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
        where TOut : allows ref struct
        where TFunc : IFunc<T, TOut>, allows ref struct
    {
        return TEnum.Select<TOut, TFunc>(ref e, func);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Map<T, TOut, TEnum> Select<TEnum, T, TOut>(this TEnum e, Func<T, TOut> func)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
        where TOut : allows ref struct
    {
        return TEnum.Select<TOut>(ref e, func);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Filter<T, TEnum, TFunc> Where<TEnum, T, TFunc>(this TEnum e, TFunc predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
        where TFunc : IFunc<T, bool>, allows ref struct
    {
        return TEnum.Where<TFunc>(ref e, predicate);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Filter<T, TEnum> Where<TEnum, T>(this TEnum e, Func<T, bool> predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        return TEnum.Where(ref e, predicate);
    }

    public static List<T> ToList<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        var list = new List<T>();
        TEnum.Fold(ref e, list, new AddToListFunc<T>());
        return list;
    }

    private struct AddToListFunc<T> : IFunc<List<T>, T, List<T>>
    {
        public List<T> Invoke(List<T> list, T item)
        {
            list.Add(item);
            return list;
        }
    }

    public static void ForEach<TEnum, T>(this TEnum e, Action<T> action)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore)
            {
                action(item);
            }
        } while (hasMore);
    }

    public static T? FirstOrDefault<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        var item = e.TryGetNext(out bool hasMore);
        return hasMore ? item : default;
    }

    public static T First<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        var item = e.TryGetNext(out bool hasMore);
        if (!hasMore)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        return item;
    }

    public static bool Any<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        e.TryGetNext(out bool hasMore);
        return hasMore;
    }

    public static bool Any<TEnum, T, TFunc>(this TEnum e, TFunc predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
        where TFunc : IFunc<T, bool>, allows ref struct
    {
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore && predicate.Invoke(item))
            {
                return true;
            }
        } while (hasMore);
        return false;
    }

    public static bool Any<TEnum, T>(this TEnum e, Func<T, bool> predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore && predicate.Invoke(item))
            {
                return true;
            }
        } while (hasMore);
        return false;
    }

    public static bool All<TEnum, T, TFunc>(this TEnum e, TFunc predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
        where TFunc : IFunc<T, bool>, allows ref struct
    {
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore && !predicate.Invoke(item))
            {
                return false;
            }
        } while (hasMore);
        return true;
    }

    public static bool All<TEnum, T>(this TEnum e, Func<T, bool> predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore && !predicate.Invoke(item))
            {
                return false;
            }
        } while (hasMore);
        return true;
    }

    public static T Single<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        var item = e.TryGetNext(out bool hasMore);
        if (!hasMore)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        e.TryGetNext(out bool hasMore2);
        if (hasMore2)
        {
            throw new InvalidOperationException("Sequence contains more than one element");
        }
        return item;
    }

    public static T Single<TEnum, T>(this TEnum e, Func<T, bool> predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        T result = default!;
        bool found = false;
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore && predicate.Invoke(item))
            {
                if (found)
                {
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                }
                result = item;
                found = true;
            }
        } while (hasMore);
        if (!found)
        {
            throw new InvalidOperationException("Sequence contains no matching element");
        }
        return result;
    }

    public static T Single<TEnum, T, TFunc>(this TEnum e, TFunc predicate)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TFunc : IFunc<T, bool>, allows ref struct
        where T : allows ref struct
    {
        T result = default!;
        bool found = false;
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore && predicate.Invoke(item))
            {
                if (found)
                {
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                }
                result = item;
                found = true;
            }
        } while (hasMore);
        if (!found)
        {
            throw new InvalidOperationException("Sequence contains no matching element");
        }
        return result;
    }

    // Concat
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Concat<T, TEnum1, TEnum2> Concat<TEnum1, TEnum2, T>(this TEnum1 first, TEnum2 second)
        where TEnum1 : IEnumerator<TEnum1, T>, allows ref struct
        where TEnum2 : IEnumerator<TEnum2, T>, allows ref struct
        where T : allows ref struct
    {
        return new Concat<T, TEnum1, TEnum2>(first, second);
    }

    // Last
    public static T Last<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where T : allows ref struct
    {
        T result = default!;
        bool found = false;
        bool hasMore;
        do
        {
           var item = e.TryGetNext(out hasMore);
           if (hasMore)
           {
               result = item;
               found = true;
           }
        } while (hasMore);
        if (!found)
           throw new InvalidOperationException("Sequence contains no elements");
        return result;
    }

    // Max with comparer
    public static T Max<TEnum, T>(this TEnum e, IComparer<T> comparer)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        var first = e.TryGetNext(out bool hasMore);
        if (!hasMore)
           throw new InvalidOperationException("Sequence contains no elements");
        T max = first;
        do
        {
           var item = e.TryGetNext(out hasMore);
           if (hasMore && comparer.Compare(item, max) > 0)
               max = item;
        } while (hasMore);
        return max;
    }

    // MaxOrDefault with comparer (returns default when empty)
    public static T? MaxOrDefault<TEnum, T>(this TEnum e, IComparer<T> comparer)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        var first = e.TryGetNext(out bool hasMore);
        if (!hasMore)
           return default;
        T max = first;
        do
        {
           var item = e.TryGetNext(out hasMore);
           if (hasMore && comparer.Compare(item, max) > 0)
               max = item;
        } while (hasMore);
        return max;
    }

    // Distinct (terminal returning List<T>)
    public static List<T> Distinct<TEnum, T>(this TEnum e)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        var set = new HashSet<T>();
        var list = new List<T>();
        bool hasMore;
        do
        {
           var item = e.TryGetNext(out hasMore);
           if (hasMore && set.Add(item))
               list.Add(item);
        } while (hasMore);
        return list;
    }

    // OrderBy (terminal returning List<T>)
    public static List<T> OrderBy<TEnum, T>(this TEnum e, IComparer<T> comparer)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        var list = e.ToList<TEnum, T>();
        list.Sort(comparer);
        return list;
    }

    public static List<T> OrderByDescending<TEnum, T>(this TEnum e, IComparer<T> comparer)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        var list = e.ToList<TEnum, T>();
        list.Sort((a, b) => comparer.Compare(b, a));
        return list;
    }

    // OrderBy with key selector (terminal returning List<T>)
    public static List<T> OrderBy<TEnum, T, TKey>(this TEnum e, Func<T, TKey> keySelector)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TKey : IComparable<TKey>
    {
        var list = e.ToList<TEnum, T>();
        list.Sort((a, b) => keySelector(a).CompareTo(keySelector(b)));
        return list;
    }

    public static List<T> OrderByDescending<TEnum, T, TKey>(this TEnum e, Func<T, TKey> keySelector)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
        where TKey : IComparable<TKey>
    {
        var list = e.ToList<TEnum, T>();
        list.Sort((a, b) => keySelector(b).CompareTo(keySelector(a)));
        return list;
    }

    // Contains
    public static bool Contains<TEnum, T>(this TEnum e, T value)
        where TEnum : IEnumerator<TEnum, T>, allows ref struct
    {
        var comparer = EqualityComparer<T>.Default;
        bool hasMore;
        do
        {
            var item = e.TryGetNext(out hasMore);
            if (hasMore && comparer.Equals(item, value))
                return true;
        } while (hasMore);
        return false;
    }

    // SequenceEqual
    public static bool SequenceEqual<TEnum1, TEnum2, T>(this TEnum1 first, TEnum2 second)
        where TEnum1 : IEnumerator<TEnum1, T>, allows ref struct
        where TEnum2 : IEnumerator<TEnum2, T>, allows ref struct
    {
        var comparer = EqualityComparer<T>.Default;
        while (true)
        {
           var item1 = first.TryGetNext(out bool hasMore1);
           var item2 = second.TryGetNext(out bool hasMore2);
           if (hasMore1 != hasMore2)
               return false;
           if (!hasMore1)
               return true;
           if (!comparer.Equals(item1, item2))
               return false;
        }
    }
}
