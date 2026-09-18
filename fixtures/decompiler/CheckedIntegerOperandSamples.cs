namespace ILInspector.Decompiler.Fixtures;

internal static class CheckedIntegerOperandSamples
{
    public static ulong Unsigned64(ulong value, long amount)
    {
        checked { value += unchecked((ulong)(amount + 1)); }
        return value;
    }

    public static long Signed64(long value, ulong amount)
    {
        checked { value += unchecked((long)(amount + 1)); }
        return value;
    }

    public static uint Unsigned32(uint value, int amount)
    {
        checked { value += unchecked((uint)(amount + 1)); }
        return value;
    }

    public static int Signed32(int value, uint amount)
    {
        checked { value += unchecked((int)(amount + 1)); }
        return value;
    }

    public static nuint UnsignedNative(nuint value, nint amount)
    {
        checked { value += unchecked((nuint)amount); }
        return value;
    }

    public static nint SignedNative(nint value, nuint amount)
    {
        checked { value += unchecked((nint)amount); }
        return value;
    }

    public static ulong Unsigned64Header(ulong start, long step)
    {
        ulong value = start;
        for (value = checked(value + unchecked((ulong)step)); value < 100;
            value = checked(value + unchecked((ulong)step)))
            Console.WriteLine(value);
        return value;
    }

    public static long Signed64Header(long start, ulong step)
    {
        long value = start;
        for (value = checked(value + unchecked((long)step)); value < 100;
            value = checked(value + unchecked((long)step)))
            Console.WriteLine(value);
        return value;
    }

    public static nuint UnsignedNativeHeader(nuint start, nint step)
    {
        nuint value = start;
        for (value = checked(value + unchecked((nuint)step)); value < 100;
            value = checked(value + unchecked((nuint)step)))
            Console.WriteLine(value);
        return value;
    }

    public static nint SignedNativeHeader(nint start, nuint step)
    {
        nint value = start;
        for (value = checked(value + unchecked((nint)step)); value < 100;
            value = checked(value + unchecked((nint)step)))
            Console.WriteLine(value);
        return value;
    }

    public static ulong SignedOperationUnsignedDestination(ulong value, long amount)
    {
        value = unchecked((ulong)checked((long)value + amount));
        return value;
    }

    public static long UnsignedOperationSignedDestination(long value, ulong amount)
    {
        value = unchecked((long)checked((ulong)value + amount));
        return value;
    }

    public static long NestedDomains(long value, long amount, ulong step)
        => checked(value + unchecked((long)checked(unchecked((ulong)amount) * step)));

    public static ulong UnsignedSubtract(ulong value, long amount)
    {
        checked { value -= unchecked((ulong)amount); }
        return value;
    }

    public static long SignedMultiply(long value, ulong amount)
    {
        checked { value *= unchecked((long)amount); }
        return value;
    }

    public static void NativeReference(ref nuint value, nint amount)
    {
        checked { value += unchecked((nuint)amount); }
    }

    public static unsafe void NativePointer(nuint* value, nint amount)
    {
        unsafe { checked { *value += unchecked((nuint)amount); } }
    }

    public static ulong UnsignedArray(ulong value, ulong[] amounts, int index)
    {
        checked { value += amounts[index]; }
        return value;
    }

    public static void FieldsAndProperty(Counter counter, long amount)
    {
        checked
        {
            counter.Count += unchecked((ulong)amount);
            Counter.Total -= unchecked((ulong)amount);
            counter.Value *= unchecked((ulong)amount);
        }
    }

    public static uint UnitStep(uint value)
    {
        checked { value++; }
        return value;
    }

    public static object NegateUInt32(uint operand)
        => checked(0L - (long)operand);

    public static ulong UnsignedNegation(ulong value, uint amount)
    {
        checked { value += unchecked((ulong)-(long)amount); }
        return value;
    }

    public static Func<ulong, long, ulong> Lambda()
        => static (value, amount) => checked(value + unchecked((ulong)amount));

    public sealed class Counter
    {
        public ulong Count;
        public static ulong Total;
        public ulong Value { get; set; }
    }
}
