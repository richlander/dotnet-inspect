namespace ILInspector.Decompiler.Fixtures;

internal static class PointerElementUpdateSamples
{
    public static unsafe void Accumulate(ulong* accumulators, int index, ulong value)
    {
        unsafe
        {
            accumulators[index ^ 1] += value;
            accumulators[index] += value * 2;
        }
    }

    public static unsafe void Signed32(int* values, int index, int value)
    {
        unsafe { values[index] += value; }
    }

    public static unsafe void Unsigned32(uint* values, int index, uint value)
    {
        unsafe { values[index] += value; }
    }

    public static unsafe void Signed64(long* values, int index, long value)
    {
        unsafe { values[index] += value; }
    }

    public static unsafe void Operations(ulong* values, int index, ulong value)
    {
        unsafe
        {
            values[index] -= value;
            values[index] *= value;
            values[index] &= value;
            values[index] |= value;
            values[index] ^= value;
        }
    }

    public static unsafe void ConstantIndex(ulong* values, ulong value)
    {
        unsafe
        {
            values[2] += value;
            values[-1] += value;
        }
    }

    public static unsafe void SideEffects(ulong* values, ref int index, ref int trace)
    {
        unsafe { Address(values, ref trace)[Index(ref index, ref trace)] += Right(values, ref trace); }
    }

    static unsafe ulong* Address(ulong* values, ref int trace)
    {
        trace = trace * 10 + 1;
        return values;
    }

    static int Index(ref int index, ref int trace)
    {
        trace = trace * 10 + 2;
        return index++;
    }

    static unsafe ulong Right(ulong* values, ref int trace)
    {
        trace = trace * 10 + 3;
        unsafe { values[0] = 100; }
        return 7;
    }

    public static unsafe void MutatingPointer(ulong* values, int index, ulong* replacement)
    {
        unsafe { values[index] += ReplacePointer(ref values, replacement); }
    }

    static unsafe ulong ReplacePointer(ref ulong* values, ulong* replacement)
    {
        values = replacement;
        return 11;
    }

    public static unsafe void MutatingIndex(ulong* values, int index)
    {
        unsafe { values[index] += ReplaceIndex(ref index); }
    }

    static ulong ReplaceIndex(ref int index)
    {
        index = 9;
        return 13;
    }

    public static unsafe ulong* RetainedCapture(ulong* values, int index, ulong value)
    {
        unsafe
        {
            ulong* selected = values + index;
            index = 9;
            *selected += value;
            return selected;
        }
    }

    public static unsafe void CheckedUpdate(int* values, int index, int value)
    {
        unsafe { checked { values[index] += value; } }
    }

    public static unsafe void NarrowElement(byte* values, int index, byte value)
    {
        unsafe { values[index] += value; }
    }

    public static unsafe void LongIndex(ulong* values, long index, ulong value)
    {
        unsafe { values[index] += value; }
    }

    public static unsafe void ByteOffset(int* values, int offset, int value)
    {
        unsafe { *(int*)((byte*)values + offset) += value; }
    }
}
