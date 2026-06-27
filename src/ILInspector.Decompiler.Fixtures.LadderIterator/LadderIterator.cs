using System;
using System.Collections.Generic;

namespace LadderIterator;

// Iterator semantic boss for the decompiler product quality ladder (#1599 row 3).
// This fixture is not a complete C# iterator catalog. It pins the mainstream
// state-machine shapes the product currently recovers and the owned residuals
// that must degrade honestly rather than drop cleanup or captured side effects.
public static class IteratorSamples
{
    public static IEnumerable<int> Linear()
    {
        yield return 1;
        yield return 2;
    }

    public static IEnumerable<int> Counting(int n)
    {
        for (int i = 0; i < n; i++)
        {
            yield return i;
        }
    }

    public static IEnumerable<int> Conditional(bool flag)
    {
        if (flag)
        {
            yield return 1;
        }

        yield return 2;
    }

    public static IEnumerable<int> MultiYieldLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            yield return i;
            yield return -i;
        }
    }

    public static IEnumerable<int> NestedLoops()
    {
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                yield return i + j;
            }
        }
    }

    public static IEnumerable<int> ForeachDelegation(IEnumerable<int> source)
    {
        foreach (int value in source)
        {
            yield return value;
        }
    }

    public static IEnumerator<int> EnumeratorReturn()
    {
        yield return 7;
        yield return 8;
    }

    public static IEnumerable<int> Empty()
    {
        yield break;
    }

    public static IEnumerable<int> SideEffectThenBreak()
    {
        Console.WriteLine("side effect");
        yield break;
    }

    public static IEnumerable<int> CapturedParameterSideEffectThenBreak(int value)
    {
        Console.WriteLine(value);
        yield break;
    }

    public static IEnumerable<int> UserFinally(IEnumerable<int> source)
    {
        try
        {
            foreach (int value in source)
            {
                yield return value;
            }
        }
        finally
        {
            Console.WriteLine("cleanup");
        }
    }
}
