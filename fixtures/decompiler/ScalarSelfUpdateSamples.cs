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

    public static int CheckedRhsNegation(int value, int amount)
    {
        checked { value -= unchecked(-amount); }
        return value;
    }

    public static int CheckedRhsConversion(int value, int amount)
    {
        checked { value *= unchecked((short)amount); }
        return value;
    }

    public static int CheckedRhsNested(int value, int amount, int step)
    {
        checked { value += unchecked(amount + checked(step * 2)); }
        return value + amount;
    }

    public static int CheckedRhsAllChecked(int value, int amount)
    {
        checked { value += amount + 1; }
        return value;
    }

    public static int UncheckedRhsChecked(int value, int amount)
    {
        value += checked(amount + 1);
        return value;
    }

    public static int CheckedRhsBitwise(int value, int amount)
    {
        checked { value += amount & 7; }
        return value;
    }

    public static void CheckedRhsStores(Counter counter, ref int value, int amount)
    {
        int local = value;
        Observe(ref local);
        checked
        {
            local += unchecked(amount + 1);
            value -= unchecked(amount * 2);
            counter.Count += unchecked(amount - 1);
            s_count += unchecked(-amount);
            counter.Value += unchecked((short)amount);
        }
        Observe(ref local);
    }

    public static void CheckedRhsIndexer(Counter counter, int index, int amount)
    {
        checked { counter[index] += unchecked(amount + 2); }
    }

    public static unsafe void CheckedRhsIndirect(int* value, int amount)
    {
        unsafe { checked { *value += unchecked(amount + 1); } }
    }

    public static int CheckedLocalHeader(int start, int step)
    {
        int value = start;
        for (value = checked(value + step); value < 100; value = checked(value + step))
            Console.WriteLine(value);
        return value;
    }

    public static int CheckedRhsHeader(int start, int step)
    {
        int value = start;
        for (value = checked(value + unchecked(step + 1)); value < 100;
            value = checked(value + unchecked(step + 1)))
            Console.WriteLine(value);
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
