using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LadderIterator;

// Iterator semantic boss for the decompiler product quality ladder (#1599 row 3).
// This fixture is not a complete C# iterator catalog. It pins the mainstream
// state-machine shapes the product currently recovers and the owned residuals
// that must degrade honestly rather than drop cleanup or captured side effects.
public static class IteratorSamples
{
    public static IEnumerable<int> Accessor
    {
        get
        {
            yield return 31;
            yield return 32;
        }
    }

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

    public static IEnumerable NonGenericEnumerable()
    {
        yield return "alpha";
        yield return "beta";
    }

    public static IEnumerator NonGenericEnumerator()
    {
        yield return "gamma";
        yield return "delta";
    }

    public static IEnumerable<T> Generic<T>(T first, T second)
    {
        yield return first;
        yield return second;
    }

    public static IEnumerable<int> CapturedYieldExpression(int value)
    {
        yield return value * 2;
        yield return value * 3;
    }

    public static IEnumerable<int> LambdaClosure(int value)
    {
        Func<int> next = () => value + 1;
        yield return next();
    }

    public static IEnumerable<int> ReferenceCleanup()
    {
        string text = "abc";
        yield return text.Length;
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

    public static IEnumerable<string> UsingStatement()
    {
        using var reader = new StringReader("first\nsecond");
        string line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }
}

public readonly struct IteratorPoint(int x, int y, int z)
{
    public IEnumerator<int> GetEnumerator()
    {
        yield return x;
        yield return y;
        yield return z;
    }

    public IEnumerable<int> InstanceSequence()
    {
        yield return x + y;
        yield return z;
    }
}
