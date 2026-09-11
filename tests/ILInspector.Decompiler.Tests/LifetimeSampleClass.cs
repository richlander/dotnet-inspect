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

    public ref struct SpanBackedView
    {
        private Span<int> _span;

        public SpanBackedView(Span<int> span) => _span = span;

        public int Length => _span.Length;

        public int At(int index) => _span[index];
    }

    public static int ReadSpanBackedView(Span<int> span, int index)
    {
        var view = new SpanBackedView(span);
        return view.At(index) + view.Length;
    }

    // Returns a span borrowing from the input span.
    public static ReadOnlySpan<char> Tail(ReadOnlySpan<char> s) => s.Slice(1);

    // Returns a raw pointer into stackalloc memory reclaimed on return: a dangling
    // pointer. The dangerous twin of StackSpanSum — the compiler rejects the
    // Span<int> form (CS8352) but raw pointers opt out of ref-safety, so this
    // compiles silently and the caller inherits an unverifiable lifetime obligation.
    public static unsafe int* EscapingStackPointer()
    {
        int* p = stackalloc int[4];
        return p;
    }

    // Returns a pointer that is NOT stack memory: it forwards the input pointer.
    // A pointer-return (the caller still inherits a lifetime obligation) but NOT a
    // stack escape — the precision control for lifetime.stack-escape.
    public static unsafe int* PassthroughPointer(int* p) => p;

    // A plain value method for the negative case.
    public static int Add(int a, int b) => a + b;
}
