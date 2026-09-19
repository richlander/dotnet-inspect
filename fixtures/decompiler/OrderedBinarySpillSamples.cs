namespace ILInspector.Decompiler.Fixtures;

internal static class OrderedBinarySpillSamples
{
    public static int Conditional(int value, int amount, bool choose)
    {
        checked { value += choose ? unchecked(amount + 1) : checked(amount * 2); }
        return value;
    }

    public static int ReturnConditional(int value, int amount, bool choose)
        => checked(value + (choose ? unchecked(amount + 1) : checked(amount * 2)));

    public static int UncheckedConditional(int value, int amount, bool choose)
    {
        value += choose ? amount + 1 : amount * 2;
        return value;
    }

    public static long Signed64(long value, long amount, bool choose)
    {
        checked { value += choose ? unchecked(amount + 1) : checked(amount * 2); }
        return value;
    }

    public static ulong Unsigned64(ulong value, ulong amount, bool choose)
    {
        checked { value += choose ? unchecked(amount + 1) : checked(amount * 2); }
        return value;
    }

    public static int Subtract(int value, int amount, bool choose)
    {
        checked { value -= choose ? unchecked(amount + 1) : checked(amount * 2); }
        return value;
    }

    public static int Multiply(int value, int amount, bool choose)
    {
        checked { value *= choose ? unchecked(amount + 1) : checked(amount * 2); }
        return value;
    }

    public static int SnapshotAcrossMutation(int value, int amount, bool choose)
    {
        checked { value += choose ? Replace(ref value, amount) : checked(amount * 2); }
        return value;
    }

    public static int EffectfulOperands(int value, ref int state, bool choose)
    {
        value = Next(ref state) + (choose ? Next(ref state) : checked(Next(ref state) * 2));
        return value;
    }

    public static int LocalDestination(ref int state, bool choose)
    {
        int result = Next(ref state) + (choose ? Next(ref state) : checked(Next(ref state) * 2));
        Observe(ref result);
        return result;
    }

    public static int NestedConditional(int value, int amount, bool first, bool second)
    {
        checked { value += first ? unchecked(amount + 1) : second ? checked(amount * 2) : unchecked(-amount); }
        return value;
    }

    static int Replace(ref int value, int amount)
    {
        value = amount;
        return amount;
    }

    static int Next(ref int state) => ++state;

    static void Observe(ref int value) => GC.KeepAlive(value);
}
