namespace ILInspector.Decompiler.Fixtures;

internal static class ScalarSelfUpdateSamples
{
    static int s_count;

    public static int Argument(int value, int amount)
    {
        value += amount;
        value--;
        return value;
    }

    public static int Local(int value, int amount)
    {
        int total = value;
        while (amount-- > 0)
            total += amount;
        return total;
    }

    public static void ByReference(ref int value, int amount)
    {
        value += amount;
        value++;
    }

    public static unsafe void Indirect(int* value, int amount)
    {
        unsafe { *value += amount; (*value)--; }
    }

    public static void Field(Counter counter, int amount)
    {
        counter.Count += amount;
        counter.Count++;
    }

    public static void StaticField(int amount)
    {
        s_count += amount;
        s_count--;
    }

    public static void VolatileField(Counter counter, int amount)
        => counter.VolatileCount += amount;

    public static void BooleanReference(ref bool value, bool other)
        => value |= other;

    public static void Property(Counter counter, int amount)
    {
        counter.Value += amount;
    }

    public static void Indexer(Counter counter, int index, int amount)
    {
        counter[index] += amount;
    }

    public static int Checked(int value, int amount)
    {
        checked { value += amount; }
        return value;
    }

    public static int CheckedRhs(int value, int amount)
    {
        checked { value += unchecked(amount + 1); }
        return value;
    }

    public static int Loop(int value, int amount)
    {
        for (value += amount; value < 100; value += amount)
            amount--;
        return value;
    }

    public static int CheckedLoop(int value, int amount)
    {
        for (value = checked(value + amount); value < 100; value = checked(value + amount))
            amount--;
        return value;
    }

    public static Action CapturingLambda(Counter counter, int amount)
        => () => { counter.Count += amount; };

    public static Func<int> LambdaLocal(int value, int amount)
        => () =>
        {
            int total = value;
            Observe(ref total);
            total += amount;
            Observe(ref total);
            return total;
        };

    static void Observe(ref int value) => GC.KeepAlive(value);

    public static void DifferentField(Counter counter, int amount)
        => counter.Count = counter.OtherCount + amount;

    public static void DifferentIndex(Counter counter, int index, int amount)
        => counter[index] = counter[index + 1] + amount;

    public static void EffectfulReceiver(Func<Counter> getCounter, int amount)
        => getCounter().Count = getCounter().Count + amount;

    public class Counter
    {
        public Counter(int otherCount = 0)
        {
            OtherCount = otherCount;
        }

        public int Count;
        public int OtherCount;
        public volatile int VolatileCount;
        public virtual int Value { get; set; }
        public int this[int index] { get => Count + index; set => Count = value - index; }
    }

    public class DerivedCounter : Counter
    {
        public void DifferentDispatch(int amount)
            => base.Value = Value + amount;
    }

    public sealed class OverridingCounter : DerivedCounter
    {
        public override int Value { get => Count; set => Count = value; }
    }
}
