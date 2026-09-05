using System;
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

public static class AsyncExpressionFixtures
{
    public static async Task<long> WidenBeforeAdd(Task<int> a, int b)
        => (long)await a + b;

    public static async Task<double> WidenBeforeDivide(Task<int> a, int b)
        => (double)await a / b;

    public static async Task<long> CheckedWidenBeforeAdd(Task<int> a, int b)
        => checked((long)await a + b);

    public static async Task<int> IntegerAdd(Task<int> a, int b)
        => await a + b;

    public static async Task<int> AwaitedLength(Task<string> a)
        => (await a).Length;

    public static async Task<char> AwaitedIndexer(Task<string> a, int index)
        => (await a)[index];

    public static async Task<int> StaticPropertyAfterAwait(Task<int> a)
        => await a + Environment.TickCount;

    public static async Task<int> VirtualPropertyAfterAwait(Task<Reading> a)
        => (await a).Value;

    public static async Task<int> NegateAwaitedValue(Task<int> a)
        => -(await a);

    public static async Task<int> ComplementAwaitedValue(Task<int> a)
        => ~(await a);

    public static async Task<int> AwaitedArrayLength(Task<int[]> a)
        => (await a).Length;

    public static async Task<int> FieldAfterAwait(Task<Reading> a)
        => (await a).FieldValue;

    public static async Task<int> StaticFieldAfterAwait(Task<int> a)
        => await a + Reading.StaticFieldValue;

    public static async Task<int> VolatileFieldAfterAwait(Task<Reading> a)
        => (await a).VolatileValue;

    public static async Task<int> ConfiguredTask(Task<int> a)
        => await a.ConfigureAwait(false);

    public static async Task<int> ConfiguredTaskCapturesContext(Task<int> a)
        => await a.ConfigureAwait(true);

    public static async Task ConfiguredVoidTask(Task a)
        => await a.ConfigureAwait(false);

    public static async Task<int> ConfiguredValueTask(ValueTask<int> a)
        => await a.ConfigureAwait(false);

    public static async Task ConfiguredVoidValueTask(ValueTask a)
        => await a.ConfigureAwait(false);

    public static async Task<int> ConfiguredTaskWithOption(
        Task<int> a, bool continueOnCapturedContext)
        => await a.ConfigureAwait(continueOnCapturedContext);

    public class Reading
    {
        public virtual int Value => 42;
        public int FieldValue;
        public static int StaticFieldValue;
        public volatile int VolatileValue;
    }
}
