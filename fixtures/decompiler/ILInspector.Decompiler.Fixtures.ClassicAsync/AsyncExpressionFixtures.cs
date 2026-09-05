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

    public static async Task<string> ReferenceCast(Task<object> a)
        => (string)await a;

    public static async Task<int> ValueCast(Task<object> a)
        => (int)await a;

    public static async Task<string> AsReference(Task<object> a)
        => await a as string;

    public static async Task<bool> TypeTest(Task<object> a)
        => await a is string;

    public static async Task<bool> NegatedTypeTest(Task<object> a)
        => await a is not string;

    public static async Task<int[]> NewIntArray(Task<int> a)
        => new int[await a];

    public static async Task<string[]> NewReferenceArray(Task<int> a)
        => new string[await a + 1];

    public static async Task<int> ArrayElementAfterAwait(Task<int[]> a, int index)
        => (await a)[index];

    public static async Task<bool> Not(Task<bool> a)
        => !await a;

    public static async Task<bool> EqualFalse(Task<bool> a)
        => await a == false;

    public static async Task<bool> EqualTrue(Task<bool> a)
        => await a == true;

    public static async Task<bool> NotEqualFalse(Task<bool> a)
        => await a != false;

    public static async Task<bool> NotEqualTrue(Task<bool> a)
        => await a != true;

    public static async Task<bool> CompareBooleans(Task<bool> a, bool b)
        => await a == b;

    public static async Task<bool> NotComparison(Task<int> a, int b)
        => !(await a > b);

    public static async Task<bool> NotFloatComparison(Task<double> a, double b)
        => !(await a > b);

    public static async Task<bool> NotUnsignedComparison(Task<uint> a, uint b)
        => !(await a > b);

    public static async Task<bool> DoubleNot(Task<bool> a)
        => !!await a;

    public static async Task<string> Coalesce(Task<string> a)
        => await a ?? "fallback";

    public static async Task<string> CoalesceCall(Task<string> a)
        => await a ?? Fallback();

    public static async Task<string> CoalesceParameter(Task<string> a, string fallback)
        => await a ?? fallback;

    public static async Task<string> CoalesceThenCall(Task<string> a)
        => (await a ?? Fallback()).Trim();

    public static async Task<string> CoalesceOperandCall(Task<string> a)
        => (await a).Trim() ?? Fallback();

    public static async Task<string> CoalesceBooleanArgument(Task<string> a, bool useFirst)
        => await a ?? ChooseFallback(!useFirst);

    static string Fallback() => Environment.TickCount.ToString();

    static string ChooseFallback(bool useFirst) => useFirst ? "first" : "second";

    public class Reading
    {
        public virtual int Value => 42;
        public int FieldValue;
        public static int StaticFieldValue;
        public volatile int VolatileValue;
    }
}
