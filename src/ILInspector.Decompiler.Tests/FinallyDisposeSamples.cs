namespace ILInspector.Decompiler.Tests;

// Issue #1759: a reference-typed value produced by `as`/isinst and tested for
// truthiness in a branch (here the `?.Dispose()` null-conditional inside a
// finally) must render `is null`/`is not null`, not `!S` — `!IDisposable` is
// CS0023. IDisposable is a cross-assembly interface (TypeShape.Unknown), so the
// reference classification comes from the isinst provenance.
public static class FinallyDisposeSamples
{
    public static void DisposeEnumeratorInFinally(System.Collections.IDictionary dictionary)
    {
        System.Collections.IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
        try
        {
            while (enumerator.MoveNext())
            {
            }
        }
        finally
        {
            (enumerator as System.IDisposable)?.Dispose();
        }
    }
}

// Issue #1759 (review): a nested local function reusing the same stack-slot
// number must not disable the isinst provenance for the outer finally-dispose
// slot. Provenance is scoped to the current function body, so this still renders
// `is null`, not `!S`.
public static class FinallyDisposeNestedSamples
{
    public static int DisposeWithNestedLocalFunction(System.Collections.IDictionary dictionary, int x)
    {
        int seed = SquarePlusOne(x);
        System.Collections.IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
        try
        {
            while (enumerator.MoveNext())
            {
            }
        }
        finally
        {
            (enumerator as System.IDisposable)?.Dispose();
        }
        return seed;

        static int SquarePlusOne(int v)
        {
            int y = v + 1;
            return y * y;
        }
    }
}
