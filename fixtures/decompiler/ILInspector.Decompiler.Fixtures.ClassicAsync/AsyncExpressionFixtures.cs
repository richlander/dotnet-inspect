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

    public class Reading
    {
        public virtual int Value => 42;
    }
}
