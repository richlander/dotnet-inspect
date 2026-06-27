// Declared in namespace System inside an assembly named System.Runtime (unsigned,
// canonicalizes to corelib) -> no framework public-key-token. Strong identity
// (#1708 Row A) must not report this Span<T>.ToArray lookalike as a span-to-array-copy.
namespace System
{
    public readonly struct Span<T>
    {
        public T[] ToArray() => new T[0];
    }

    public static class SpanSpoofer
    {
        public static int[] CallsFakeSpanToArray(Span<int> span) => span.ToArray();
    }
}
