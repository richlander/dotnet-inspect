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

    public static class AsyncAttributeSpoofer
    {
        public static int Read() => 42;

        public static Threading.Tasks.Task<int> ReadAsync()
            => Threading.Tasks.Task.FromResult(42);

        [Runtime.CompilerServices.AsyncStateMachine(
            typeof(object))]
        public static int Analyze() => Read();
    }
}

namespace System.Runtime.CompilerServices
{
    public sealed class AsyncStateMachineAttribute
        : Attribute
    {
        public AsyncStateMachineAttribute(Type stateMachineType)
        {
        }
    }
}
