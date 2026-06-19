using System;

namespace ILInspector.Decompiler.Tests;

public static class LifetimeSampleClass
{
    // ref return: the result borrows from the array argument.
    public static ref int FirstElement(int[] a) => ref a[0];

    // ref readonly return.
    public static ref readonly int FirstReadonly(int[] a) => ref a[0];

    // A stackalloc-backed span: its memory lives on the current frame and cannot
    // escape — the lifetime fact behind CS8350/CS8352, used locally so it compiles.
    public static int StackSpanSum(int seed)
    {
        Span<int> scratch = stackalloc int[4];
        scratch[0] = seed;
        scratch[1] = seed + 1;
        return scratch[0] + scratch[1];
    }

    // A ref struct parameter (Span<T>): cannot be boxed or put on the heap.
    public static int ReadSpan(Span<int> s) => s[0];

    // Returns a span borrowing from the input span.
    public static ReadOnlySpan<char> Tail(ReadOnlySpan<char> s) => s.Slice(1);

    // A plain value method for the negative case.
    public static int Add(int a, int b) => a + b;
}
